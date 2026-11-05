using System.Collections.Generic;
using UnityEngine;

// Yakın kamera saydamlığı (kullanıcı kararı 2026-11-05): üçüncü şahıs kamerası karaktere çok yaklaşınca (küçük oda köşesi) karakter
// ekranı kaplamasın, görüş açık kalsın. Yöntem dither: opak kuyrukta pikseller ekran deseniyle atılır (RescueTeam/CameraFadeLit).
// Transparent yerine dither: tek mesh'li karakterde kol gövdenin arkasından görünmez, derinlik ve SSAO bozulmaz, gölge tam kalır.
//  - Yalnız OrbitCamera'nın hedefine uygulanır (OrbitCamera kendi ekler ve Bind eder); co-op'ta öbür oyuncuda bu bileşen olmaz.
//  - Mesafe OrbitCamera.CurrentDistance (pivot-kamera): animasyonla oynamaz, bu yüzden saydamlık yürürken titremez.
//  - Uzakta (CurrentFade 0) hiçbir şey değişmez: renderer'lar kaynak malzemelerle çizilir, görüntü birebir aynı, maliyet yok.
//  - Yaklaşınca renderer'ların sharedMaterials dizisi çalışma zamanı kopyalarıyla değişir; asset'e yazılmaz, Play dışında çalışmaz.
//    Kopyalar bileşenle birlikte yok edilir (sahne geçişinde sızmaz); statik durum tutulmaz (domain reload kapalı).
//  - FP'de ve V geçişinde (Blend > 0) hedef 0: birinci şahıs gövdesi tam görünür, baş gizleme (FirstPersonBody) bağımsız kalır.
//  - Elde taşınanlar da dahil: oyuncunun altındakiler (CarryableItem) ve kökte duran HeldValuable / HeldCone / HeldCoil / Piggyback;
//    elden çıkan aynı kare kaynak malzemesine döner. CarryableItem.UpdateHandVisibility yalnız shadowCastingMode yazar, çakışmaz.
//  - URP Lit dışı ya da saydam malzemeler olduğu gibi kalır (dönüşecek karşılığı yok).
// LateUpdate 450: OrbitCamera (-50) mesafeyi, köprü (CompanyPlayerBridge.LateUpdate) eldeki eşyayı, FirstPersonBody (400) başı
// yazdıktan sonra.
[DefaultExecutionOrder(450)]
public class CameraProximityFade : MonoBehaviour
{
    public const string ShaderName = "RescueTeam/CameraFadeLit";
    public const string CloneSuffix = " (CameraFade)";
    private const string SourceShaderName = "Universal Render Pipeline/Lit";
    private static readonly int VisibilityId = Shader.PropertyToID("_RescueCameraFadeVisibility");

    [Tooltip("Saydamlığın başladığı kamera-pivot uzaklığı (m). Tekerlekle en yakın serbest uzaklık 2.25 m olduğundan normal oyunda hiç devreye girmez; yalnız engel kamerayı çekince çalışır. Ölçüldü: opak karakter 1.4 m'de ekranın yaklaşık %11'ini, 0.6 m'de %35'ini kaplıyor.")]
    public float fadeStartDistance = 1.4f;
    [Tooltip("Saydamlığın tamamlandığı uzaklık (m). minObstructedDistance 0.6 m'nin biraz üstünde: kamera en dibe varmadan görüş tamamen açılır.")]
    public float fadeEndDistance = 0.7f;
    [Range(0f, 1f), Tooltip("Tam saydamlıkta kalan görünürlük. Sıfır değil: oyuncu karakterinin nerede olduğunu hisseder.")]
    public float minVisibility = 0.12f;
    [Tooltip("Saydamlığın saniyedeki en yüksek değişimi. Uzaklık sıçrasa da (V geçişi, ışınlanma) görüntü sıçramaz: 6/sn ≈ 0.17 sn, V geçişinden (0.25 sn) kısa.")]
    public float fadeSpeed = 6f;

    // 0 = opak (kaynak malzemeler), 1 = en saydam
    public float CurrentFade { get; private set; }
    // Bu karede uygulanan görünürlük (1 = tam)
    public float Visibility { get { return Mathf.Lerp(1f, minVisibility, CurrentFade); } }
    // Kopya malzemeyle çizilen renderer sayısı (tanı, testler)
    public int FadedRendererCount { get { return originals.Count; } }
    public bool IsFaded(Renderer r) { return r != null && originals.ContainsKey(r); }

    private OrbitCamera orbit;
    private ThirdPersonPlayer player;
    private Shader fadeShader;
    private bool shaderWarned;
    private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
    private readonly List<Renderer> scratch = new List<Renderer>();
    private readonly List<Renderer> stale = new List<Renderer>();
    private readonly HashSet<Renderer> current = new HashSet<Renderer>();

    // OrbitCamera hedefini bağlarken çağırır. Yalnız bağlı kameranın hedefi iken saydamlaşır.
    public void Bind(OrbitCamera camera) { orbit = camera; }

    // Uzaklıktan saydamlık (0..1): başlangıçta 0, bitişte 1, arası yumuşak.
    public float FadeForDistance(float distance)
    {
        if (fadeStartDistance <= fadeEndDistance) return distance <= fadeEndDistance ? 1f : 0f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStartDistance, fadeEndDistance, distance));
    }

    private void Awake() { player = GetComponent<ThirdPersonPlayer>(); }

    private float TargetFade()
    {
        if (orbit == null || !orbit.enabled || orbit.target != transform) return 0f;
        if (orbit.Mode != OrbitCamera.ViewMode.ThirdPerson || orbit.Blend > 0f || orbit.CurrentDistance < 0f) return 0f;
        return FadeForDistance(orbit.CurrentDistance);
    }

    private void LateUpdate()
    {
        // Hedef kalmadıysa (FP'ye tam geçti, kamera başkasına döndü) beklemeden opak; aksi halde hız sınırlı.
        bool active = orbit != null && orbit.enabled && orbit.target == transform && orbit.Blend < 1f;
        CurrentFade = active ? Mathf.MoveTowards(CurrentFade, TargetFade(), fadeSpeed * Time.deltaTime) : 0f;
        if (CurrentFade <= 0f)
        {
            RestoreAll();
            return;
        }
        if (!ResolveShader())
        {
            CurrentFade = 0f;
            return;
        }
        SyncRenderers();
        Shader.SetGlobalFloat(VisibilityId, Visibility);
    }

    private bool ResolveShader()
    {
        if (fadeShader == null) fadeShader = Shader.Find(ShaderName);
        if (fadeShader != null && fadeShader.isSupported) return true;
        if (!shaderWarned) Debug.LogWarning("CameraProximityFade: " + ShaderName + " bulunamadı ya da desteklenmiyor, karakter opak kalır.");
        shaderWarned = true;
        return false;
    }

    // Bu karede saydamlaşacak renderer'ları topla, yeni gelenlere kopya tak, listeden çıkanları (elden düşen eşya) geri çevir.
    private void SyncRenderers()
    {
        current.Clear();
        Collect(transform);
        if (player != null)
        {
            if (player.HeldValuable != null) Collect(player.HeldValuable.transform);
            if (player.HeldCone != null) Collect(player.HeldCone.target != null ? player.HeldCone.target : player.HeldCone.transform); // görsel target altında
            if (player.HeldCoil != null) Collect(player.HeldCoil.transform);
            if (player.Piggyback != null) Collect(player.Piggyback.transform);
        }
        stale.Clear();
        foreach (var pair in originals)
            if (pair.Key == null || !current.Contains(pair.Key)) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++)
        {
            var r = stale[i];
            if (r != null) r.sharedMaterials = originals[r];
            originals.Remove(r);
        }
        foreach (var r in current)
            if (!originals.ContainsKey(r)) Apply(r);
    }

    private void Collect(Transform root)
    {
        root.GetComponentsInChildren(false, scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            var r = scratch[i];
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            // Yalnız gölge çizenler (FP gölge vekili, elde gizlenen eşya) ekrana çizmez; zaten takılı olan kopya kalsın
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly && !originals.ContainsKey(r)) continue;
            current.Add(r);
        }
    }

    private void Apply(Renderer r)
    {
        var source = r.sharedMaterials;
        var swapped = new Material[source.Length];
        for (int i = 0; i < source.Length; i++) swapped[i] = CloneOf(source[i]);
        originals[r] = source;
        r.sharedMaterials = swapped;
    }

    // Kaynağın dither'lı kopyası; dönüşemeyen malzeme (Lit değil, saydam) kendisi kalır.
    private Material CloneOf(Material source)
    {
        if (source == null || source.shader == null || source.shader.name != SourceShaderName) return source;
        // Saydamlık kuyruk ve anahtardan okunur: özellik okumaya gerek yok, kaynak malzemeye hiç dokunulmaz
        if (source.renderQueue > 2500 || source.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) return source;
        if (clones.TryGetValue(source, out Material clone)) return clone;
        clone = new Material(source) { name = source.name + CloneSuffix, hideFlags = HideFlags.HideAndDontSave, shader = fadeShader };
        foreach (string keyword in source.shaderKeywords) clone.EnableKeyword(keyword);
        clone.renderQueue = source.renderQueue;
        clones[source] = clone;
        return clone;
    }

    private void RestoreAll()
    {
        if (originals.Count == 0) return;
        foreach (var pair in originals)
            if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        originals.Clear();
    }

    private void OnDisable()
    {
        CurrentFade = 0f;
        RestoreAll();
    }

    private void OnDestroy()
    {
        RestoreAll();
        foreach (var pair in clones)
            if (pair.Value != null) Destroy(pair.Value);
        clones.Clear();
    }
}
