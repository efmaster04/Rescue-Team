using System;
using System.Collections.Generic;
using UnityEngine;

// U4 (kullanıcı kararı "Hırsızlar" + "Ek kararlar", 2026-10-19): hırsız dalgaları. Saat fizik adımında ilerler (görevle aynı).
//  - İlk kamyonet firstWaveSeconds'ta (180), sonra waveIntervalSeconds'ta bir (300) şeride çıkar; hırsız sayısı 1 → 2 → … → maxThieves (5).
//  - Dalgadan warnLeadSeconds önce HUD uyarısı ("Hırsızlar geliyor! (Dalga N)").
//  - Kamyonet şeridin başında yer yoksa (şerit doluluk) spawn bekler; gerçekleşen zaman WaveLog'a yazılır.
//  - Rota yardımcıları: yol ↔ patika geçişi rampa ara noktalarıyla (markers.json geometry_probes.ramp); kaya çıkıntısı ulaşılmaz.
//  - Görev bitince yeni dalga çıkmaz. Testler SuppressedForTests ile kapatır ya da Begin(kopya ayar) ile zamanı değiştirir.
[DefaultExecutionOrder(-45)]
public class ThiefDirector : MonoBehaviour
{
    public static ThiefDirector Instance { get; private set; }
    // Testler (trafik, ambulans, animasyon doğrulamaları) hırsızları kapatmak için açar. Play başında sıfırlanır.
    public static bool SuppressedForTests;

    public ThiefSettings settings;
    public LanePath lane;
    [Tooltip("Sahnedeki taşınabilir durak işareti (ThiefVanStop): şeride izdüşümü durağın s'si, yanal uzaklığı banket ofseti.")]
    public Transform vanStop;
    public ThiefVan vanPrefab;
    [Tooltip("Hırsız prefab'ları (Black, Red, Green): dalga içinde sırayla.")]
    public Thief[] thiefPrefabs = new Thief[0];
    public MissionDirector mission;
    public TrafficDirector traffic;
    [Tooltip("Yoldan patikaya rampa ara noktaları (Unity, zeminde, yukarıdan aşağıya sıralı).")]
    public Vector3[] rampPoints = new Vector3[0];
    [Tooltip("Yol ve patika zemin yükseklikleri (m): seviye ayrımı ikisinin ortasından.")]
    public float roadLevelY = 0f, pathLevelY = -9f;
    [Tooltip("Bu yüksekliğin altı boşluk (FallRespawnPlane; fırlatılan eşya geri döner).")]
    public float killBelowY = -50f;
    public bool autoStart = true;

    public struct WaveRecord { public int index; public float plannedTime; public float spawnTime; public int thieves; }

    public ThiefSettings Active { get; private set; }
    public float Elapsed { get; private set; }
    // Çıkan kamyonet sayısı (= sıradaki dalganın indeksi)
    public int WaveIndex { get; private set; }
    public float NextWaveTime { get { return Active != null ? Active.WaveTime(WaveIndex) : float.PositiveInfinity; } }
    public bool WarningActive { get; private set; }
    public string WarningText { get; private set; } = "";
    public ThiefVan ActiveVan { get; private set; }
    public float StopS { get; private set; }
    public float ShoulderLateral { get; private set; }
    public readonly List<WaveRecord> WaveLog = new List<WaveRecord>();
    public readonly List<ThiefVan> Vans = new List<ThiefVan>();
    public readonly List<Thief> Thieves = new List<Thief>();
    public int WarningsShown { get; private set; }

    public event Action<ThiefVan> VanSpawned;

    private float lastSpawnElapsed = -100f;
    private int warnedWave = -1;

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { SuppressedForTests = false; }

    private void Start()
    {
        if (autoStart) Begin(settings);
    }

    // Dalgaları verilen ayarla (yeniden) başlatır. Testler geçici kopya verir; asset değişmez.
    public void Begin(ThiefSettings use)
    {
        Active = use;
        Elapsed = 0f;
        WaveIndex = 0;
        warnedWave = -1;
        lastSpawnElapsed = -100f;
        WarningActive = false;
        WaveLog.Clear();
        ClearAll();
        ComputeStop();
    }

    public void ClearAll()
    {
        foreach (var v in Vans) if (v != null) Destroy(v.gameObject);
        Vans.Clear();
        foreach (var t in Thieves) if (t != null) Destroy(t.gameObject);
        Thieves.Clear();
        ActiveVan = null;
    }

    private void ComputeStop()
    {
        if (lane == null || vanStop == null) return;
        StopS = lane.Project(vanStop.position, out float lateral, out _);
        ShoulderLateral = lateral;
    }

    private void FixedUpdate()
    {
        if (Active == null) return;
        Elapsed += Time.fixedDeltaTime;
        Vans.RemoveAll(v => v == null);
        Thieves.RemoveAll(t => t == null);
        if (ActiveVan != null && ActiveVan.IsGone) ActiveVan = null;
        if (SuppressedForTests) { WarningActive = false; return; }
        if (mission != null && mission.IsFinished) { WarningActive = false; return; }

        float next = NextWaveTime;
        float lead = Active.warnLeadSeconds;
        bool pre = Elapsed >= next - lead;
        bool post = Elapsed - lastSpawnElapsed < 3f;
        WarningActive = pre || post;
        if (pre && warnedWave != WaveIndex)
        {
            warnedWave = WaveIndex;
            WarningText = string.Format(Active.waveWarningFormat, WaveIndex + 1);
            WarningsShown++;
            GameNotice.Show(WarningText);
        }
        if (Elapsed >= next && vanPrefab != null)
        {
            var van = SpawnVan(Active.ThievesForWave(WaveIndex), WaveIndex);
            if (van != null)
            {
                WaveLog.Add(new WaveRecord { index = WaveIndex, plannedTime = next, spawnTime = Elapsed, thieves = Active.ThievesForWave(WaveIndex) });
                WaveIndex++;
                lastSpawnElapsed = Elapsed;
            }
        }
    }

    // Kamyoneti şeridin başına koyar (yer yoksa null; sonraki adımda yeniden denenir). Testler de çağırır.
    public ThiefVan SpawnVan(int thieves, int waveIndex)
    {
        if (vanPrefab == null || lane == null) return null;
        var pf = vanPrefab.GetComponent<LaneFollower>();
        var tsettings = traffic != null ? (traffic.Active != null ? traffic.Active : traffic.settings) : null;
        float cruise = pf.cruiseSpeed;
        if (tsettings != null) { var e = tsettings.Entry(TrafficVehicleType.PanelVan); if (e != null) cruise = e.cruiseSpeed; }
        float minGap = tsettings != null ? tsettings.minGap : 4f;
        float braking = tsettings != null ? tsettings.braking : 6f;
        float headway = tsettings != null ? tsettings.timeHeadway : 2f;
        float rearAhead = LaneOccupancy.FirstRearAhead(lane, -pf.rearOverhang, out _);
        float gap = rearAhead - pf.frontOverhang;
        if (gap < minGap + 0.5f) return null;
        float speed = Mathf.Min(cruise, Mathf.Sqrt(2f * braking * Mathf.Max(0f, gap - minGap)), gap / headway);

        var v = Instantiate(vanPrefab);
        v.name = "ThiefVan_W" + (waveIndex + 1);
        v.settings = Active;
        var vehicle = v.GetComponent<TrafficVehicle>();
        if (tsettings != null) vehicle.settings = tsettings;
        var f = v.GetComponent<LaneFollower>();
        f.cruiseSpeed = cruise;
        if (tsettings != null) { f.acceleration = tsettings.acceleration; f.braking = tsettings.braking; }
        v.Begin(lane, StopS, ShoulderLateral, thieves, waveIndex, this, thiefPrefabs, speed);
        Vans.Add(v);
        ActiveVan = v;
        VanSpawned?.Invoke(v);
        return v;
    }

    public void RegisterThief(Thief t) { if (t != null && !Thieves.Contains(t)) Thieves.Add(t); }

    // ---- rota yardımcıları ----
    public bool IsRoadLevel(Vector3 p) { return p.y > (roadLevelY + pathLevelY) * 0.5f; }
    // Kaya çıkıntısı (patikadan ~5 m aşağıda) ve boşluk ulaşılmaz.
    public bool Reachable(Vector3 p) { return p.y > pathLevelY - 2.5f; }

    // from → to: aynı seviyede düz; farklı seviyede rampa noktalarından (en yakınından başlayarak) geçer.
    public List<Vector3> Route(Vector3 from, Vector3 to)
    {
        var r = new List<Vector3>();
        if (rampPoints == null || rampPoints.Length < 2 || IsRoadLevel(from) == IsRoadLevel(to)) { r.Add(to); return r; }
        bool down = IsRoadLevel(from);
        int n = rampPoints.Length;
        // Rampa sırası: yukarıdan aşağı (down) ya da tersi
        int start = down ? 0 : n - 1, step = down ? 1 : -1;
        // En yakın rampa noktasından başla (geri dönmesin), ama en az bir sonraki noktayı da al
        int nearest = start;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = Flat(rampPoints[i] - from).magnitude + Mathf.Abs(rampPoints[i].y - from.y) * 2f;
            if (d < best) { best = d; nearest = i; }
        }
        for (int i = nearest; i >= 0 && i < n; i += step) r.Add(rampPoints[i]);
        r.Add(to);
        return r;
    }

    public float RouteLength(Vector3 from, Vector3 to)
    {
        float len = 0f;
        Vector3 prev = from;
        foreach (var p in Route(from, to)) { len += Vector3.Distance(prev, p); prev = p; }
        return len;
    }

    // Panikle kaçış hedefi: şeridin sonu (yol seviyesinde).
    public Vector3 FleePoint(Vector3 from)
    {
        if (lane == null) return from + Vector3.right * 60f;
        return lane.PointAt(lane.Length);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
