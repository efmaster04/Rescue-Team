using System.Collections.Generic;
using UnityEngine;

// U1 (kullanıcı kararı 2): PlayerInteraction'ın o an E ipucunu gösterdiği hedef nesnenin etrafı beyaz dış hatla yanar.
// Hedefin her renderer'ı için sahne köküne gizli bir kopya kurulur (hedefin hiyerarşisine dokunulmaz, malzemeleri değişmez):
//  - MeshRenderer → kopya mesh (köşe normalleri pozisyona göre ortalanmış) + RescueTeam/HighlightOutline (ters kabuk, Cull Front)
//  - SkinnedMeshRenderer → aynı kemiklerle ikinci SkinnedMeshRenderer (hasta, arkadaş): kemikleri izler
//  - LineRenderer (halat) → daha geniş beyaz kopya çizgi (RescueTeam/HighlightLine), konumlar her kare eşlenir
// Hedef değişince / kaybolunca kopyalar aynı kare silinir. Yalnız yerel oyuncunun PlayerInteraction'ı için çalışır.
[DefaultExecutionOrder(300)] // PlayerInteraction.Update ve RopeAnchor.LateUpdate (200) sonrasında eşle
public class InteractionHighlight : MonoBehaviour
{
    public PlayerInteraction interaction;
    public Color color = Color.white;
    [Tooltip("Dış hat kalınlığı (px, 1080p'de).")]
    public float widthPx = 3f;
    [Tooltip("Parlaklık salınımı (rad/sn) ve en düşük parlaklık.")]
    public float pulseSpeed = 4f;
    [Range(0f, 1f)] public float pulseMin = 0.65f;

    public const string OutlineShaderName = "RescueTeam/HighlightOutline";
    public const string LineShaderName = "RescueTeam/HighlightLine";

    // Şu an vurgulanan hedef (PlayerInteraction.HighlightTarget ile aynı; testler okur) ve kopya sayısı
    public Transform Current { get; private set; }
    public int CopyCount { get { return entries.Count; } }
    public IEnumerable<Renderer> Copies { get { foreach (var e in entries) if (e.copy != null) yield return e.copy; } }

    private sealed class Entry
    {
        public Renderer source;
        public GameObject go;
        public Renderer copy;
        public LineRenderer sourceLine, copyLine;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Material outlineMaterial, lineMaterial;
    private bool shaderWarned;
    private static readonly Dictionary<Mesh, Mesh> smoothedMeshes = new Dictionary<Mesh, Mesh>();

    private void Awake()
    {
        if (interaction == null) interaction = GetComponent<PlayerInteraction>();
    }

    private void OnDisable() { Clear(); }

    private void OnDestroy()
    {
        Clear();
        if (outlineMaterial != null) Destroy(outlineMaterial);
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    private void LateUpdate()
    {
        Transform target = interaction != null && interaction.isActiveAndEnabled ? interaction.HighlightTarget : null;
        if (target != Current)
        {
            Clear();
            Current = target;
            if (target != null) Build(target);
        }
        else if (Current != null)
        {
            // Hedefin renderer'ı yok olduysa (ör. ragdoll değişimi) yeniden kur
            foreach (var e in entries)
                if (e.source == null) { Clear(); Build(Current); break; }
        }
        Sync();
    }

    private bool EnsureMaterials()
    {
        if (outlineMaterial != null && lineMaterial != null) return true;
        var outlineShader = Shader.Find(OutlineShaderName);
        var lineShader = Shader.Find(LineShaderName);
        if (outlineShader == null || lineShader == null)
        {
            if (!shaderWarned) { shaderWarned = true; Debug.LogWarning("InteractionHighlight: vurgu shader'ı bulunamadı (" + OutlineShaderName + " / " + LineShaderName + ")"); }
            return false;
        }
        outlineMaterial = new Material(outlineShader) { hideFlags = HideFlags.HideAndDontSave, name = "HighlightOutline (runtime)" };
        lineMaterial = new Material(lineShader) { hideFlags = HideFlags.HideAndDontSave, name = "HighlightLine (runtime)" };
        return true;
    }

    private void ApplyMaterialSettings()
    {
        foreach (var m in new[] { outlineMaterial, lineMaterial })
        {
            if (m == null) continue;
            m.SetColor("_Color", color);
            m.SetFloat("_PulseSpeed", pulseSpeed);
            m.SetFloat("_PulseMin", pulseMin);
        }
        if (outlineMaterial != null) outlineMaterial.SetFloat("_WidthPx", widthPx);
    }

    private void Build(Transform target)
    {
        if (!EnsureMaterials()) return;
        ApplyMaterialSettings();
        bool targetIsPatient = target.GetComponentInParent<Patient>() != null;
        foreach (var r in target.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled) continue;
            // Sedyedeki / sırttaki hasta hedefin çocuğu olabilir; hedef hasta değilse hastanın gövdesi vurgulanmaz
            if (!targetIsPatient && r.GetComponentInParent<Patient>() != null) continue;
            if (r is MeshRenderer mr) BuildMesh(mr);
            else if (r is SkinnedMeshRenderer smr) BuildSkinned(smr);
            else if (r is LineRenderer lr) BuildLine(lr);
        }
    }

    private GameObject NewCopy(Renderer source)
    {
        var go = new GameObject("Highlight_" + source.name) { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
        go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        return go;
    }

    private static void Configure(Renderer copy, Material m, int count)
    {
        var mats = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < mats.Length; i++) mats[i] = m;
        copy.sharedMaterials = mats;
        copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        copy.receiveShadows = false;
        copy.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        copy.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        copy.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        copy.allowOcclusionWhenDynamic = false;
    }

    private void BuildMesh(MeshRenderer source)
    {
        var filter = source.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        var go = NewCopy(source);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = Smoothed(filter.sharedMesh);
        var mr = go.AddComponent<MeshRenderer>();
        Configure(mr, outlineMaterial, mf.sharedMesh.subMeshCount);
        entries.Add(new Entry { source = source, go = go, copy = mr });
    }

    private void BuildSkinned(SkinnedMeshRenderer source)
    {
        if (source.sharedMesh == null) return;
        var go = NewCopy(source);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = Smoothed(source.sharedMesh);
        smr.bones = source.bones;
        smr.rootBone = source.rootBone;
        smr.localBounds = source.localBounds;
        smr.quality = source.quality;
        smr.updateWhenOffscreen = source.updateWhenOffscreen;
        Configure(smr, outlineMaterial, smr.sharedMesh.subMeshCount);
        entries.Add(new Entry { source = source, go = go, copy = smr });
    }

    private void BuildLine(LineRenderer source)
    {
        var go = NewCopy(source);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = source.useWorldSpace;
        lr.alignment = source.alignment;
        lr.textureMode = source.textureMode;
        lr.numCapVertices = source.numCapVertices;
        lr.numCornerVertices = source.numCornerVertices;
        lr.widthCurve = source.widthCurve;
        Configure(lr, lineMaterial, 1);
        var e = new Entry { source = source, go = go, copy = lr, sourceLine = source, copyLine = lr };
        entries.Add(e);
        SyncLine(e);
    }

    // Kopyalar kaynağın dünya pozunu ve görünürlüğünü izler; halat çizgisi noktaları her kare eşlenir.
    private void Sync()
    {
        foreach (var e in entries)
        {
            if (e.source == null || e.go == null) continue;
            bool visible = e.source.enabled && e.source.gameObject.activeInHierarchy;
            if (e.go.activeSelf != visible) e.go.SetActive(visible);
            if (!visible) continue;
            e.go.transform.SetPositionAndRotation(e.source.transform.position, e.source.transform.rotation);
            e.go.transform.localScale = e.source.transform.lossyScale;
            if (e.copyLine != null) SyncLine(e);
        }
    }

    private static Vector3[] linePoints = new Vector3[16];

    private void SyncLine(Entry e)
    {
        var src = e.sourceLine;
        var dst = e.copyLine;
        int n = src.positionCount;
        if (linePoints.Length < n) linePoints = new Vector3[n * 2];
        src.GetPositions(linePoints);
        dst.positionCount = n;
        dst.SetPositions(linePoints);
        // Kalınlık: ekranda widthPx piksel ⇒ kameraya uzaklığa göre dünya genişliği
        Vector3 mid = n > 0 ? linePoints[n / 2] : src.transform.position;
        if (src.useWorldSpace == false) mid = src.transform.TransformPoint(mid);
        // Çok uzaktan / çok yakından bakılırken ince ama okunur kalsın
        float extra = Mathf.Clamp(WorldWidthPerPixel(mid) * widthPx * 2f, 0.01f, 0.08f);
        dst.widthMultiplier = src.widthMultiplier + extra;
    }

    // Bir pikselin verilen noktadaki dünya genişliği (1080 satırlık referans ekranda).
    private static float WorldWidthPerPixel(Vector3 at)
    {
        var cam = Camera.main;
        if (cam == null) return 0.002f;
        float d = Mathf.Max(0.3f, Vector3.Distance(cam.transform.position, at));
        return 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / 1080f;
    }

    private void Clear()
    {
        foreach (var e in entries) if (e.go != null) Destroy(e.go);
        entries.Clear();
        Current = null;
    }

    // Dış hat için mesh kopyası: aynı konumdaki köşelerin normalleri ortalanır; böylece sert kenarlarda (düz gölgeli
    // low-poly modeller) itilen yüzler birbirinden ayrılıp boşluk bırakmaz. Kaynak mesh değişmez; kopya önbelleğe alınır.
    public static Mesh Smoothed(Mesh source)
    {
        if (source == null) return null;
        if (smoothedMeshes.TryGetValue(source, out Mesh cached) && cached != null) return cached;
        Mesh copy = source;
        try
        {
            copy = Instantiate(source);
            copy.name = source.name + "_outline";
            copy.hideFlags = HideFlags.HideAndDontSave;
            var verts = copy.vertices;
            var normals = copy.normals;
            if (normals == null || normals.Length != verts.Length) { copy.RecalculateNormals(); normals = copy.normals; }
            var sums = new Dictionary<Vector3Int, Vector3>(verts.Length);
            var keys = new Vector3Int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(verts[i].x * 2000f), Mathf.RoundToInt(verts[i].y * 2000f), Mathf.RoundToInt(verts[i].z * 2000f));
                keys[i] = k;
                sums[k] = (sums.TryGetValue(k, out Vector3 s) ? s : Vector3.zero) + normals[i];
            }
            var smoothed = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 s = sums[keys[i]];
                smoothed[i] = s.sqrMagnitude > 1e-8f ? s.normalized : normals[i];
            }
            copy.normals = smoothed;
        }
        catch (System.Exception e)
        {
            // Okunamayan mesh (Read/Write kapalı build): kaynak mesh olduğu gibi kullanılır
            Debug.LogWarning("InteractionHighlight: " + source.name + " için normal ortalama yapılamadı: " + e.Message);
            if (copy != source && copy != null) Destroy(copy);
            copy = source;
        }
        smoothedMeshes[source] = copy;
        return copy;
    }
}
