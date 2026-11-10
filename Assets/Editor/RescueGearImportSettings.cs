using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// Kurtarma ekipmanı modelleri (02_KAYNAK_MODELLER: bot, römork, çeki demiri, drone sistemi, köpek, yiyecek-içecek).
// Blender'dan FBX olarak gelir (Art/Blender/Scripts/export_rescue_gear.py); her FBX'in yanında Blender'ın Principled BSDF
// değerlerini taşıyan <ad>.materials.json bulunur. URP'nin FBX okuyucusu malzemeyi oluşturduktan sonra (sıra -980) bu
// sınıf renk, metal, pürüzlülük, ışıma ve saydamlığı JSON'daki gerçek değerlerle yazar. Dokular yok: modeller düz renkli.
// Köpek: Generic iskelet, 16 Blender aksiyonu ayrı klip olarak (döngüler işaretli), kök hareketi yok (yerinde).
internal class RescueGearImportSettings : AssetPostprocessor
{
    public const string Folder = "Assets/_Project/Art/Models/RescueGear/";
    public const string DogFile = "Kopek.fbx";
    // Döngüye girecek köpek klipleri; geçişler (yatma, kalkma, oturma, zıplama) tek seferlik.
    public static readonly HashSet<string> DogLoops = new HashSet<string>
        { "Idle", "Idle_Happy", "Lie_Idle", "Lie_Sleep", "Sit_Idle", "Walk", "Trot", "Run", "Sniff", "Eat", "Bark" };
    // Blender'ın ışıma gücü Unity'de bloom'la fazla parlıyor: ölçek ve üst sınır.
    public const float EmissionScale = 0.45f, EmissionMax = 3.2f;
    public const float MinAlpha = 0.16f;

    [Serializable] private class MatInfo { public string name; public float[] @base; public float metallic, roughness; public float[] emission; public float emissionStrength, alpha, transmission; }
    [Serializable] private class MatList { public MatInfo[] materials; }

    private static bool IsGear(string path) => path.StartsWith(Folder) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
    private static bool IsDog(string path) => IsGear(path) && Path.GetFileName(path) == DogFile;

    // v4: statik modeller iki yüzlü (ters sarımlı, ters normalli ikinci katman); saydam malzemede parlaklık sınırı
    public override uint GetVersion() { return 4; }

    private void OnPreprocessModel()
    {
        if (!IsGear(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;          // çarpıştırıcıları yerleşim kurulumu ekler (CompanyPlayerBridge en yakın çarpıştırıcıyı seçer)
        importer.isReadable = false;           // GarageRuntimeOptimization okunamayan meshleri statik birleştirmeye katmaz
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        if (IsDog(assetPath))
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }
        else
        {
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
        }
    }

    private void OnPreprocessAnimation()
    {
        if (!IsDog(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            string n = clip.takeName;
            int bar = n.LastIndexOf('|');
            if (bar >= 0) n = n.Substring(bar + 1);
            clip.name = n;
            bool loop = DogLoops.Contains(n);
            clip.loopTime = loop;
            clip.loopPose = loop;
            // yerinde oynatılır: yürüyüşü KopekDavranisi taşır
            clip.lockRootPositionXZ = true;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = false;
            clip.keepOriginalPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
        }
        importer.clipAnimations = clips;
    }

    // Kaynak malzemelerin hepsi glTF'de doubleSided: Blender ve glTF görüntüleyicileri arka yüzü de çizdiği için bazı parçaların
    // normalleri ters kalmış (ör. kurtarma vanının sağ ön kapısı %82, ön cam %85 içe bakıyor; Unity arka yüzü keser ve yük bölmesinin
    // yan duvarı görünmez olur). Her statik meshe ters sarımlı ve ters normalli ikinci bir katman eklenir: arka yüz kırpması açık
    // kalır, her yön doğru normalle tek kez çizilir (Blender'ın iki yüzlü görünümü). Köpek (iskeletli, tutarlı normaller) hariç.
    private void OnPostprocessModel(GameObject g)
    {
        if (!IsGear(assetPath) || IsDog(assetPath)) return;
        var done = new HashSet<Mesh>();
        foreach (var f in g.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh != null && done.Add(f.sharedMesh)) MakeTwoSided(f.sharedMesh);
    }

    private static void MakeTwoSided(Mesh m)
    {
        var v = new List<Vector3>(); m.GetVertices(v);
        var n = new List<Vector3>(); m.GetNormals(n);
        var uv = new List<Vector2>(); m.GetUVs(0, uv);
        var t = new List<Vector4>(); m.GetTangents(t);
        int count = v.Count;
        if (count == 0 || n.Count != count) return;
        var v2 = new List<Vector3>(v); var n2 = new List<Vector3>(n);
        for (int i = 0; i < count; i++) { v2.Add(v[i]); n2.Add(-n[i]); }
        var subs = new List<int[]>();
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var tri = m.GetTriangles(s);
            var both = new int[tri.Length * 2];
            for (int i = 0; i < tri.Length; i += 3)
            {
                both[i] = tri[i]; both[i + 1] = tri[i + 1]; both[i + 2] = tri[i + 2];
                int j = tri.Length + i;
                both[j] = tri[i] + count; both[j + 1] = tri[i + 2] + count; both[j + 2] = tri[i + 1] + count;
            }
            subs.Add(both);
        }
        m.indexFormat = v2.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : m.indexFormat;
        m.SetVertices(v2);
        m.SetNormals(n2);
        if (uv.Count == count) { var uv2 = new List<Vector2>(uv); uv2.AddRange(uv); m.SetUVs(0, uv2); }
        if (t.Count == count) { var t2 = new List<Vector4>(t); foreach (var x in t) t2.Add(new Vector4(-x.x, -x.y, -x.z, x.w)); m.SetTangents(t2); }
        for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s, false);
        m.RecalculateBounds();
    }

    private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (!IsGear(assetPath)) return;
        var info = Find(assetPath, description.materialName);
        if (info == null || !material.HasProperty("_BaseColor")) return;
        Apply(material, info);
    }

    private static readonly Dictionary<string, MatList> cache = new Dictionary<string, MatList>();

    private static MatInfo Find(string fbxPath, string materialName)
    {
        string sidecar = fbxPath.Substring(0, fbxPath.Length - 4) + ".materials.json";
        if (!cache.TryGetValue(sidecar, out var list) || list == null)
        {
            list = File.Exists(sidecar) ? JsonUtility.FromJson<MatList>(File.ReadAllText(sidecar)) : null;
            cache[sidecar] = list;
        }
        if (list == null || list.materials == null) return null;
        foreach (var m in list.materials) if (m.name == materialName) return m;
        return null;
    }

    private static void Apply(Material m, MatInfo info)
    {
        // Blender değerleri lineer; Material.SetColor lineer projede sRGB bekler
        var b = info.@base != null && info.@base.Length >= 3 ? info.@base : new[] { 0.8f, 0.8f, 0.8f, 1f };
        float alpha = Mathf.Clamp01(info.alpha);
        bool transparent = alpha < 0.99f || info.transmission > 0.3f;
        if (transparent) alpha = Mathf.Clamp(info.transmission > 0.3f ? Mathf.Min(alpha, 0.35f) : alpha, MinAlpha, 0.95f);
        var baseColor = new Color(b[0], b[1], b[2], 1f).gamma;
        baseColor.a = transparent ? alpha : 1f;
        m.SetColor("_BaseColor", baseColor);
        if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
        if (m.HasProperty("_WorkflowMode")) m.SetFloat("_WorkflowMode", 1f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", Mathf.Clamp01(info.metallic));
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Clamp(1f - info.roughness, 0.05f, 0.92f));
        if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);
        if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 1f);

        float e = info.emission != null && info.emission.Length >= 3 ? Mathf.Max(info.emission[0], Mathf.Max(info.emission[1], info.emission[2])) : 0f;
        if (info.emissionStrength > 0f && e > 0.001f && m.HasProperty("_EmissionColor"))
        {
            float k = Mathf.Min(info.emissionStrength * EmissionScale, EmissionMax);
            var ec = new Color(info.emission[0], info.emission[1], info.emission[2], 1f).gamma * k;
            m.SetColor("_EmissionColor", ec);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else if (m.HasProperty("_EmissionColor"))
        {
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        if (transparent)
        {
            // saydam yüzeyler (camlar) parlak tavan ışıklarını aynalamasın: üst sınır
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.55f));
            MakeTransparent(m);
        }
    }

    private static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = (int)RenderQueue.Transparent;
    }
}
