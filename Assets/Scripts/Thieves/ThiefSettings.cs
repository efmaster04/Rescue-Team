using UnityEngine;

// U4 (kullanıcı kararları "Hırsızlar" + "Ek kararlar", 2026-10-19; şef varsayımları brief'te): hırsız dalgaları, kamyonet,
// hırsız yapay zekâsı ve oyuncu savunması sayıları. Koda gömülmez, buradan ayarlanır. Testler geçici kopya kullanır.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Hırsız Ayarları", fileName = "ThiefSettings")]
public class ThiefSettings : ScriptableObject
{
    [Header("Dalgalar")]
    [Tooltip("İlk kamyonetin şeride çıktığı an (oyun saniyesi).")]
    [Min(0f)] public float firstWaveSeconds = 180f;
    [Tooltip("Sonraki dalgalar arası (sn).")]
    [Min(5f)] public float waveIntervalSeconds = 300f;
    [Tooltip("Dalga başına hırsız: 1, 2, 3 … en fazla bu kadar.")]
    [Range(1, 10)] public int maxThieves = 5;
    [Tooltip("Dalgadan bu kadar önce HUD uyarısı (sn).")]
    [Min(0f)] public float warnLeadSeconds = 5f;
    [Tooltip("Kamyonet durakta en fazla bu kadar bekler (sn); hepsi dönerse erken kalkar.")]
    [Min(5f)] public float vanWaitSeconds = 90f;

    [Header("Kamyonet")]
    [Tooltip("Duraktan bu kadar önce bankete kaymaya başlar / kalkınca bu kadar yolda şeride döner (m). Kısa: durak çekicinin hemen önünde, yanından geçerken sürtmesin.")]
    [Min(2f)] public float pullOffLength = 5f;
    [Tooltip("Parktayken kamyonet kenarı ile şerit kenarı arasındaki boşluk (m).")]
    [Min(0f)] public float shoulderClearance = 0.3f;
    [Tooltip("Hırsızlar bu aralıkla iner (sn).")]
    [Min(0.1f)] public float unloadInterval = 0.8f;
    [Tooltip("Arka kapı açık açısı (°) ve açılma süresi (sn).")]
    public float doorOpenAngle = 150f;
    [Min(0.1f)] public float doorSeconds = 0.6f;

    [Header("Hırsız hareketi")]
    [Tooltip("Sinsi yürüyüş (Thief_Sneak_Walk target_speed_mps) ve koşu (Thief_Run / Thief_Run_Carry) hızı (m/sn).")]
    public float sneakSpeed = 1.6f;
    public float runSpeed = 3.6f;
    [Tooltip("Sedye sürüklerken koşu hızının bu kadarı.")]
    [Range(0.2f, 1f)] public float stretcherDragFactor = 0.6f;
    [Tooltip("Yürünebilir eğim (°) ve adımda en fazla yükseklik farkı (m).")]
    public float slopeLimit = 35f;
    public float stepHeight = 0.5f;
    [Tooltip("Ara noktaya vardı sayılma yarıçapı (m).")]
    public float waypointRadius = 0.9f;

    [Header("Kapma")]
    [Tooltip("Yerdeki eşyayı kapma menzili (m) ve oyuncunun elinden kapma menzili / açısı (oyuncunun önünde).")]
    public float grabRange = 1.2f;
    public float handGrabRange = 1.2f;
    public float handGrabAngle = 60f;
    [Tooltip("Kamyonetin arka noktasına bu kadar yaklaşınca eşyayı atar (m).")]
    public float throwRange = 2.0f;

    [Header("Saldırı (yumruk)")]
    [Tooltip("Elleri boşken bu yarıçaptaki (m) yaklaşan / yolunu kesen oyuncuya saldırır.")]
    public float attackRadius = 6f;
    public float punchDamage = 25f;
    [Tooltip("Vuruş karesinde önündeki bu menzil (m) / açı (°) içindeki oyuncular hasar alır.")]
    public float punchRange = 1.2f;
    public float punchAngle = 60f;
    public float punchCooldown = 2.5f;
    [Tooltip("Yumruk için oyuncuya bu kadar yaklaşır (m).")]
    public float punchApproach = 1.0f;

    [Header("Sersemleme / korku")]
    [Tooltip("İtilince (sn / geri savrulma m), tackle'da (sn), fırlatılan eşya isabetinde (sn), araç çarpınca yerde kalma (sn), düdükte korku (sn).")]
    public float pushStunSeconds = 2f;
    public float pushKnockback = 1.5f;
    public float tackleStunSeconds = 4f;
    public float throwHitStunSeconds = 3f;
    public float vehicleDownSeconds = 3f;
    public float fearSeconds = 4f;

    [Header("Oyuncu savunması")]
    [Tooltip("İtme: menzil (m), açı (°), bekleme (sn).")]
    public float pushRange = 1.4f;
    public float pushAngle = 60f;
    public float pushCooldown = 0.8f;
    [Tooltip("Tackle: kayma mesafesi (m), isabet yarıçapı (m), bekleme (sn).")]
    public float tackleDistance = 2.5f;
    public float tackleHitRadius = 1.2f;
    public float tackleCooldown = 3f;
    [Tooltip("Fırlatma hızı (m/sn).")]
    public float throwSpeed = 12f;
    [Tooltip("Düdük: menzil (m), bekleme (sn).")]
    public float whistleRadius = 8f;
    public float whistleCooldown = 20f;

    [Header("HUD")]
    public string waveWarningFormat = "Hırsızlar geliyor! (Dalga {0})";

    [Header("Test")]
    [Tooltip("Açıkken hırsız varyantı ve küçük rastgelelikler sabit sırayla.")]
    public bool useFixedSeed = false;
    public int seed = 20261019;

    // k. dalganın (0'dan başlar) hırsız sayısı: 1 → 2 → … → maxThieves.
    public int ThievesForWave(int k) { return Mathf.Clamp(k + 1, 1, maxThieves); }
    // k. dalganın kamyonet çıkış zamanı.
    public float WaveTime(int k) { return firstWaveSeconds + k * waveIntervalSeconds; }
}
