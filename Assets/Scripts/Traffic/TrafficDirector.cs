using System;
using System.Collections.Generic;
using UnityEngine;

// Trafik yönetmeni (kullanıcı kararları 1, 2, 8): iki yön (EastBound, WestBound) birbirinden bağımsız zamanlanır.
//   Sakin aralık (20–40 sn, en fazla bir tek araç) → dalga (3–4 araç, araçlar arası 2–4 sn) → sakin → ...
// Doğma noktasında yer yoksa (şerit doluluk servisi) spawn bekler. Ambulansın doğmasından 20 sn önce
// EastBound'a yeni araç çıkmaz. Saat fizik adımında ilerler (ambulans ve görevle aynı).
[DefaultExecutionOrder(-40)]
public class TrafficDirector : MonoBehaviour
{
    public static TrafficDirector Instance { get; private set; }

    // Testler (ör. ambulans doğrulaması) trafiği kapatmak için açar. Sahne yeniden yüklense de kalır, Play başında sıfırlanır.
    public static bool SuppressedForTests;

    public TrafficSettings settings;
    public LanePath eastBound, westBound;
    [Tooltip("Tip sırasıyla prefab'lar: Sedan, Hatchback, Pickup, PanelVan, BoxTruck.")]
    public TrafficVehicle[] prefabs = new TrafficVehicle[0];
    [Tooltip("Ambulans zamanlaması buradan okunur (doğmadan 20 sn önce EastBound boş).")]
    public MissionDirector mission;
    public bool autoStart = true;

    public struct SpawnRecord
    {
        public float time;
        public bool east;
        public TrafficVehicleType type;
        public bool inWave;
        public bool forced;
        public int paint;
    }

    public struct PhaseRecord
    {
        public bool east;
        public bool wave;
        public float start;
        public float planned;    // sakin: planlanan süre (sn); dalga: planlanan araç sayısı
    }

    public float Elapsed { get; private set; }
    public TrafficSettings Active { get; private set; }
    public readonly List<TrafficVehicle> Vehicles = new List<TrafficVehicle>();
    public readonly List<SpawnRecord> SpawnLog = new List<SpawnRecord>();
    public readonly List<PhaseRecord> PhaseLog = new List<PhaseRecord>();
    public event Action<TrafficVehicle> VehicleSpawned;

    private class Stream
    {
        public LanePath lane;
        public bool east;
        public System.Random rng;
        public bool wave;
        public float phaseEnd;       // sakin aralığın bitişi
        public float singleAt = -1f; // sakin aralıktaki tek aracın zamanı (yoksa -1)
        public int waveLeft;
        public float nextSpawnAt;
        public int pendingType = -1;  // sıradaki aracın tipi (bir kez seçilir; yer beklerken rastgele sayı tüketilmez)
        public int pendingPaint = -1;
    }

    private readonly Stream[] streams = new Stream[2];

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { SuppressedForTests = false; }

    private void Start()
    {
        if (autoStart) Begin(settings);
    }

    // Trafiği verilen ayarlarla (yeniden) başlatır. Testler geçici kopya verir; asset değişmez.
    public void Begin(TrafficSettings use)
    {
        Active = use;
        Elapsed = 0f;
        ClearVehicles();
        SpawnLog.Clear();
        PhaseLog.Clear();
        int seed = use.useFixedSeed ? use.seed : Environment.TickCount;
        streams[0] = new Stream { lane = eastBound, east = true, rng = new System.Random(seed * 31 + 1) };
        streams[1] = new Stream { lane = westBound, east = false, rng = new System.Random(seed * 31 + 2) };
        foreach (var s in streams) StartCalm(s);
    }

    public void ClearVehicles()
    {
        foreach (var v in Vehicles) if (v != null) Destroy(v.gameObject);
        Vehicles.Clear();
    }

    private void FixedUpdate()
    {
        if (Active == null) return;
        Elapsed += Time.fixedDeltaTime;
        Vehicles.RemoveAll(v => v == null);
        if (SuppressedForTests) return;
        foreach (var s in streams) TickStream(s);
    }

    private void TickStream(Stream s)
    {
        if (s.lane == null) return;
        float t = Elapsed;
        if (!s.wave)
        {
            if (s.singleAt >= 0f && t >= s.singleAt)
            {
                if (TrySpawn(s, false)) s.singleAt = -1f;
                return;
            }
            if (t >= s.phaseEnd && s.singleAt < 0f)
            {
                s.wave = true;
                s.waveLeft = RangeInt(s.rng, Active.waveSize.x, Active.waveSize.y);
                s.nextSpawnAt = t;
                PhaseLog.Add(new PhaseRecord { east = s.east, wave = true, start = t, planned = s.waveLeft });
            }
            return;
        }
        if (s.waveLeft > 0 && t >= s.nextSpawnAt && TrySpawn(s, true))
        {
            s.waveLeft--;
            s.nextSpawnAt = t + Range(s.rng, Active.waveGapSeconds.x, Active.waveGapSeconds.y);
            if (s.waveLeft == 0) StartCalm(s);
        }
    }

    // Sakin aralık: süre rastgele, isteğe bağlı tek araç aralığın ortalarında bir yerde.
    private void StartCalm(Stream s)
    {
        float duration = Range(s.rng, Active.calmSeconds.x, Active.calmSeconds.y);
        s.wave = false;
        s.phaseEnd = Elapsed + duration;
        s.singleAt = s.rng.NextDouble() < Active.calmSingleVehicleChance ? Elapsed + duration * Range(s.rng, 0.25f, 0.75f) : -1f;
        PhaseLog.Add(new PhaseRecord { east = s.east, wave = false, start = Elapsed, planned = duration });
    }

    // EastBound ambulans penceresi: ambulansın doğmasına ≤ ambulanceClearSeconds kaldıysa yeni araç çıkmaz.
    public bool EastBlockedForAmbulance
    {
        get
        {
            if (mission == null || Active == null) return false;
            float next = mission.NextAmbulanceSpawnTime;
            return mission.Elapsed >= next - Active.ambulanceClearSeconds && mission.Elapsed < next;
        }
    }

    private bool TrySpawn(Stream s, bool inWave)
    {
        if (s.east && EastBlockedForAmbulance) return false;
        if (s.pendingType < 0)
        {
            s.pendingType = (int)PickType(s.rng);
            s.pendingPaint = TrafficSettings.IsPassengerCar((TrafficVehicleType)s.pendingType) ? s.rng.Next(4) : -1;
        }
        var type = (TrafficVehicleType)s.pendingType;
        int paint = s.pendingPaint;
        var v = SpawnVehicle(s.lane, type, paint);
        if (v == null) return false;
        s.pendingType = -1;
        SpawnLog.Add(new SpawnRecord { time = Elapsed, east = s.east, type = type, inWave = inWave, paint = paint });
        return true;
    }

    // Test: belirli tipte aracı hemen çıkarmaya çalışır (yer yoksa null). Ambulans penceresine bakmaz.
    public TrafficVehicle ForceSpawn(bool east, TrafficVehicleType type)
    {
        var v = SpawnVehicle(east ? eastBound : westBound, type, TrafficSettings.IsPassengerCar(type) ? 0 : -1);
        if (v != null) SpawnLog.Add(new SpawnRecord { time = Elapsed, east = east, type = type, forced = true });
        return v;
    }

    // Doğma noktasında (s = 0) yer varsa aracı şeridin başına koyar; hız, öndeki boşlukta durabileceği kadar.
    private TrafficVehicle SpawnVehicle(LanePath lane, TrafficVehicleType type, int paint)
    {
        var prefab = PrefabFor(type);
        if (prefab == null || lane == null) return null;
        var pf = prefab.GetComponent<LaneFollower>();
        var entry = Active.Entry(type);
        float cruise = entry != null ? entry.cruiseSpeed : pf.cruiseSpeed;
        float rearAhead = LaneOccupancy.FirstRearAhead(lane, -pf.rearOverhang, out float leaderSpeed);
        float gap = rearAhead - pf.frontOverhang;
        if (gap < Active.minGap + 0.5f) return null; // yer yok: bekle
        float speed = Mathf.Min(cruise, Mathf.Sqrt(2f * Active.braking * Mathf.Max(0f, gap - Active.minGap)), gap / Active.timeHeadway);

        var v = Instantiate(prefab);
        v.name = prefab.name + "_" + (SpawnLog.Count + 1);
        v.settings = Active;
        var f = v.Follower;
        f.cruiseSpeed = cruise;
        f.acceleration = Active.acceleration;
        f.braking = Active.braking;
        f.Place(lane, 0f, speed);
        if (paint >= 0) v.ApplyPaint(paint);
        Vehicles.Add(v);
        VehicleSpawned?.Invoke(v);
        return v;
    }

    private TrafficVehicle PrefabFor(TrafficVehicleType type)
    {
        foreach (var p in prefabs) if (p != null && p.type == type) return p;
        return null;
    }

    private TrafficVehicleType PickType(System.Random rng)
    {
        float total = 0f;
        foreach (var e in Active.types) if (e != null) total += Mathf.Max(0f, e.weight);
        double r = rng.NextDouble() * total;
        foreach (var e in Active.types)
        {
            if (e == null) continue;
            r -= Mathf.Max(0f, e.weight);
            if (r < 0) return e.type;
        }
        return TrafficVehicleType.Sedan;
    }

    private static float Range(System.Random rng, float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
    private static int RangeInt(System.Random rng, int a, int b) { return rng.Next(Mathf.Min(a, b), Mathf.Max(a, b) + 1); }
}
