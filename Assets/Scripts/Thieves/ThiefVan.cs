using System;
using System.Collections.Generic;
using UnityEngine;

// U4: hırsız kamyoneti (trafik PanelVan prefab'ının koyu kopyası). Sürüş ve trafik kuralları (kuyruk, koni, oyuncuya yavaşlama,
// çarpma, şerit sonunda kaybolma) aynen TrafficVehicle'da; bu bileşen yalnız durağı ve parkı yönetir:
//  Approaching: dış durak (TrafficVehicle.externalStopS) = ThiefVanStop'un şeritteki s'si; son pullOffLength metrede yanal
//               ofset şerit kenarının dışına (bankete) kayar.
//  Parked:      TrafficVehicle.parked → sürülmez; şerit kaydından (LaneOccupancy) çıkar, yani şeridi kapatmaz, arkadaki trafik
//               geçer. Arka kapılar açılır ("tak") ve vanWaitSeconds (30) boyunca AÇIK kalır; hırsızlar unloadInterval arayla
//               sırayla arka ve yan kapıdan iner. Hepsi dönünce / pes edince ya da süre dolunca kalkar.
//  Departing:   kapılar kapanır (bagaj artık erişilemez → içindekiler kesin çalındı: LootRegistry.ReportStolen, %50 kontrolü
//               yalnız bu anda), arkadan gelen araçla boşluk yoksa bekler; şeride kaydolur, yanal ofset geri gelir, trafik gibi gider.
//               Geride kalan hırsızlar kaçar (Thief.Flee).
// U4b (kullanıcı kararları 2026-10-20):
//  - Kamyonete atılan eşya hemen çalınmış sayılmaz: "kamyonette" (LootItem.InVan, LootRegistry.InVanValue, HUD turuncu bölüm).
//    Oyuncu açık bagaja ≤ retrieveRange yaklaşıp yüzünü dönünce en değerliden başlayarak geri alır (PlayerInteraction "E: Geri al").
//  - Pes eden hırsız ÖN kapıdan biner (yolcu tarafı; dalganın son hırsızı şoför kapısı): kapı açılır, biner, kapı kapanır.
[RequireComponent(typeof(LaneFollower))]
[RequireComponent(typeof(TrafficVehicle))]
public class ThiefVan : MonoBehaviour
{
    public static readonly List<ThiefVan> All = new List<ThiefVan>();

    public enum VanPhase { Approaching, Parked, Departing }

    public ThiefSettings settings;
    [Tooltip("Arka kapaklar (builder kurar; pivot menteşede, kök uzayında dikey eksen).")]
    public Transform doorLeft, doorRight;
    public float doorLeftOpenAngle = -150f, doorRightOpenAngle = 150f;
    [Tooltip("Arka kapakların ortak kökü (U1 beyaz vurgusu bu kökün altındaki renderer'ları çizer).")]
    public Transform rearDoors;
    [Tooltip("U4b: ön kapılar (sol = şoför, sağ = yolcu); pivot ön kenarda, kök uzayında dikey eksen.")]
    public Transform doorFrontLeft, doorFrontRight;
    public float frontDoorOpenAngle = 70f;
    public AudioSource doorSource;

    public LaneFollower Follower { get; private set; }
    public TrafficVehicle Vehicle { get; private set; }
    public VanPhase Phase { get; private set; } = VanPhase.Approaching;
    public float StopS { get; private set; } = float.PositiveInfinity;
    public float ShoulderLateral { get; private set; }
    public float BaseLateral { get; private set; }
    public int WaveIndex { get; private set; }
    public int PlannedThieves { get; private set; }
    public int Spawned { get; private set; }
    public int Boarded { get; private set; }
    public int Fled { get; private set; }
    // U4b: pes edip ön kapıdan binen (Boarded'a da sayılır), bagajdan geri alınan, kaçışta kesin çalınan eşya sayıları
    public int Surrendered { get; private set; }
    public int Retrieved { get; private set; }
    public int StolenCount { get; private set; }
    public bool LastBoardWasDriverDoor { get; private set; }
    // Kamyonete atılan eşya sayısı (toplam; geri alınanlar düşülmez)
    public int LootCount { get; private set; }
    public float ParkedTime { get; private set; }
    public float ParkedAtElapsed { get; private set; } = -1f;
    public float DoorAmount { get; private set; }
    public bool WaitingForGap { get; private set; }
    public bool IsGone { get; private set; }
    public bool LootFinalized { get; private set; }
    public bool IsParked { get { return Phase == VanPhase.Parked; } }
    public bool Departing { get { return Phase == VanPhase.Departing || WaitingForGap; } }
    // U4b: bagaj açık ve erişilebilir (parkta, kalkış başlamadı, kapaklar açık)
    public bool TrunkOpen { get { return !IsGone && Phase == VanPhase.Parked && !WaitingForGap && DoorAmount >= 0.9f; } }
    public readonly List<Thief> Thieves = new List<Thief>();
    // Şu an kamyonette duran ("kamyonette" durumundaki) eşyalar
    public readonly List<LootItem> Loot = new List<LootItem>();
    // İniş kaydı: her hırsız için "arka" / "yan" (testler okur)
    public readonly List<string> ExitLog = new List<string>();

    public event Action<ThiefVan> Parked;
    public event Action<ThiefVan> Departed;
    public event Action<ThiefVan> Despawned;
    public event Action<ThiefVan, Thief> ThiefSpawned;
    public event Action<ThiefVan, LootItem> LootRetrieved;
    public event Action<ThiefVan> LootFinalizedEvent;

    private ThiefDirector director;
    private Thief[] prefabs;
    private float nextUnloadAt;
    private Quaternion doorLeftClosed, doorRightClosed, frontLeftClosed, frontRightClosed;
    private int variantOffset;
    private bool doorSoundDone, departSoundDone;
    // U4b: ön kapılar — [0] şoför (sol), [1] yolcu (sağ): açık kalma zamanı ve mevcut açıklık
    private readonly float[] frontOpenUntil = { -1f, -1f };
    private readonly float[] frontAmount = { 0f, 0f };

    private void Awake()
    {
        Follower = GetComponent<LaneFollower>();
        Vehicle = GetComponent<TrafficVehicle>();
        if (doorLeft != null) doorLeftClosed = doorLeft.localRotation;
        if (doorRight != null) doorRightClosed = doorRight.localRotation;
        if (doorFrontLeft != null) frontLeftClosed = doorFrontLeft.localRotation;
        if (doorFrontRight != null) frontRightClosed = doorFrontRight.localRotation;
        if (doorSource == null) doorSource = gameObject.AddComponent<AudioSource>();
        doorSource.clip = SynthClips.Cached("VanDoor", SynthClips.VanDoor);
        doorSource.playOnAwake = false;
        doorSource.spatialBlend = 1f;
        doorSource.minDistance = 4f;
        doorSource.maxDistance = 120f;
        doorSource.volume = 0.8f;
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void OnDestroy()
    {
        IsGone = true;
        // Kalkmadan yok edildiyse (test temizliği): kamyonetteki eşyalar çalınmış sayılmaz, yalnız "kamyonette" kaydı düşülür
        if (Loot.Count > 0 && !LootFinalized)
        {
            var reg = LootRegistry.Instance;
            foreach (var item in Loot) if (item != null && reg != null) reg.ReportRetrieved(item);
            Loot.Clear();
        }
        Despawned?.Invoke(this);
    }

    // =====================================================================================
    // İniş / atış noktaları
    // =====================================================================================
    // Birincil iniş / atış noktası: arka tamponun 1.3 m gerisi, NavMesh'te ve engelsiz. Arkası kapalıysa (durak başka bir aracın
    // burnuna yakın, ör. çekicinin önü) arka köşelerin hemen dışı ("arka kapıdan köşeye adım"), o da olmazsa banket tarafındaki
    // yan kapı hizası (arka tekerin yanı, gövdeden 0.5 m dışarıda), o da olmazsa yol tarafı.
    public bool SideExit { get; private set; }
    public bool RearExitOk { get; private set; }
    public string RearExitKind { get; private set; } = "-";
    public Vector3 RearPoint { get { return ExitPoint(out _); } }
    public Vector3 SidePoint { get { ExitPoint(out _); return sideExitPoint; } }

    public Vector3 ExitPoint(out Vector3 spread)
    {
        if (exitFrame == Time.frameCount) { spread = exitSpread; return exitPoint; }
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float rear = Follower != null ? Follower.rearOverhang : 2.7f;
        float halfW = Vehicle != null ? Vehicle.bodySize.x * 0.5f : 1f;
        float sideSign = ShoulderLateral >= 0f ? 1f : -1f;
        Vector3 bumper = transform.position - fwd * rear + Vector3.up * 0.8f;
        Vector3 rearCand = transform.position - fwd * (rear + 1.3f);
        // Yan kapı: gövdeden 0.5 m dışarıda (yol tarafındaysa şerit ortasındaki araçlara değmez), arka tekerin hemen önünde
        Vector3 sideCand = transform.position - fwd * (rear * 0.3f) + right * sideSign * (halfW + 0.5f);
        Vector3 roadCand = transform.position - fwd * (rear * 0.3f) - right * sideSign * (halfW + 0.5f);
        // Yola bağlı olmalı: kamyonetin oyduğu NavMesh ile banket eğimi arasında kalan şerit yoldan kopuk olabilir
        Vector3 road = Follower != null && Follower.lane != null ? Follower.lane.PointAt(Follower.Distance) : transform.position - right * ShoulderLateral;
        Vector3 rearNav = rearCand, sideNav = sideCand, roadNav = roadCand;
        // Arka: önce tam arka, sonra arka köşelerin dışı (banket tarafı, yol tarafı)
        RearExitOk = false;
        RearExitKind = "-";
        if (!GroundProbe.LineBlocked(bumper, rearCand + Vector3.up * 0.8f) && !StaticObstacle(rearCand, fwd, 0.8f) && OnNavMesh(rearCand, out rearNav) && ConnectedTo(rearNav, road))
        { RearExitOk = true; RearExitKind = "arka"; rearExitPoint = rearNav; rearSpread = right; }
        else
        {
            foreach (float s in new[] { sideSign, -sideSign })
            {
                Vector3 corner = transform.position - fwd * (rear + 0.5f) + right * s * (halfW + 0.45f);
                if (StaticObstacle(corner, fwd, 0.3f) || !OnNavMesh(corner, out Vector3 cornerNav) || !ConnectedTo(cornerNav, road)) continue;
                RearExitOk = true; RearExitKind = "arka köşe"; rearExitPoint = cornerNav; rearSpread = fwd;
                break;
            }
        }
        // Yan
        if (!StaticObstacle(sideCand, fwd, 0.8f) && OnNavMesh(sideCand, out sideNav) && ConnectedTo(sideNav, road)) { sideExitPoint = sideNav; sideExitOk = true; }
        else if (OnNavMesh(roadCand, out roadNav)) { sideExitPoint = roadNav; sideExitOk = true; }
        else { sideExitOk = false; sideExitPoint = GroundProbe.Find(roadCand + Vector3.up * 1.5f, 4f, out RaycastHit gs) ? gs.point : roadCand; }
        if (RearExitOk) { SideExit = false; exitSpread = rearSpread; exitPoint = rearExitPoint; }
        else { SideExit = true; exitSpread = fwd; exitPoint = sideExitPoint; }
        exitFrame = Time.frameCount;
        spread = exitSpread;
        return exitPoint;
    }

    // k. hırsızın iniş noktası: çift sıra arka (varsa), tek sıra yan (varsa); yoksa öbürü. kind: "arka" / "arka köşe" / "yan".
    public Vector3 ExitFor(int index, out Vector3 spread, out string kind)
    {
        ExitPoint(out _);
        bool wantRear = index % 2 == 0;
        if (wantRear ? RearExitOk : !sideExitOk && RearExitOk) { spread = rearSpread; kind = RearExitKind; return rearExitPoint; }
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        spread = fwd; kind = "yan";
        return sideExitPoint;
    }

    private static bool OnNavMesh(Vector3 p, out Vector3 onMesh)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 0.8f, UnityEngine.AI.NavMesh.AllAreas)) { onMesh = hit.position; return true; }
        onMesh = p;
        return false;
    }

    private static UnityEngine.AI.NavMeshPath connectPath;
    private static bool ConnectedTo(Vector3 from, Vector3 to)
    {
        if (!UnityEngine.AI.NavMesh.SamplePosition(to, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) return true; // yol NavMesh'te değilse kontrol yapılamaz
        if (connectPath == null) connectPath = new UnityEngine.AI.NavMeshPath();
        return UnityEngine.AI.NavMesh.CalculatePath(from, hit.position, UnityEngine.AI.NavMesh.AllAreas, connectPath) && connectPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
    }
    private int exitFrame = -1;
    private Vector3 exitPoint, exitSpread, rearExitPoint, rearSpread, sideExitPoint;
    private bool sideExitOk;
    private static readonly Collider[] exitOverlap = new Collider[16];

    // İniş alanında (2·half × 2·half m, 0.3–1.9 m yükseklik) hareketsiz bir collider (çekici, korkuluk, kaya) var mı?
    private bool StaticObstacle(Vector3 at, Vector3 fwd, float half)
    {
        int n = Physics.OverlapBoxNonAlloc(at + Vector3.up * 1.1f, new Vector3(half, 0.8f, half), exitOverlap, Quaternion.LookRotation(fwd, Vector3.up), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = exitOverlap[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c is MeshCollider mc && !mc.convex) continue; // zemin / yol (korkuluk hariç tutulmaz: ince ama dik; aşağıda LineBlocked yakalar)
            if (GroundProbe.IsMovingGameObject(c)) continue;
            return true;
        }
        return false;
    }

    // Kamyonet gövdesine (BoxCollider) en yakın uzaklık (yatay): hırsız eşyayı buradan da atabilir (iniş noktası dar bir aralıktaysa).
    public float DistanceToBody(Vector3 p)
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return Vector3.Distance(p, transform.position);
        Vector3 q = box.ClosestPoint(new Vector3(p.x, transform.position.y + 1f, p.z));
        q.y = 0f; p.y = 0f;
        return Vector3.Distance(p, q);
    }

    // ---- U4b: bagaj (arka tampon) ----
    private void Frame(out Vector3 fwd, out Vector3 right, out float rear, out float front, out float halfW)
    {
        fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        right = Vector3.Cross(Vector3.up, fwd);
        rear = Follower != null ? Follower.rearOverhang : 2.7f;
        front = Follower != null ? Follower.frontOverhang : 2.7f;
        halfW = Vehicle != null ? Vehicle.bodySize.x * 0.5f : 1f;
    }

    // Arka tamponun ortası (0.8 m yükseklikte): yüz kuralı ve vurgu için
    public Vector3 TrunkCenter { get { Frame(out var f, out _, out float rear, out _, out _); return transform.position - f * rear + Vector3.up * 0.8f; } }

    // Noktanın arka tampon doğru parçasına (iki arka köşe arası) yatay uzaklığı: köşeden çapraz yaklaşan oyuncu da bagaja erişir
    // (durak çekicinin hemen önünde; tam arkada yer olmayabilir).
    public float TrunkDistance(Vector3 p)
    {
        Frame(out var f, out var r, out float rear, out _, out float halfW);
        Vector3 c = transform.position - f * rear;
        Vector3 a = c - r * halfW, b = c + r * halfW;
        a.y = b.y = p.y = 0f;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }

    // Oyuncu bagajdan eşya alabilir mi (bagaj açık, eşya var, menzilde ve yükseklik yakın); best = en değerli.
    public bool CanRetrieve(Vector3 playerPos, out LootItem best)
    {
        best = null;
        if (!TrunkOpen || Loot.Count == 0) return false;
        float range = settings != null ? settings.retrieveRange : 1.0f;
        if (TrunkDistance(playerPos) > range || Mathf.Abs(playerPos.y - transform.position.y) > 1.5f) return false;
        best = MostValuable;
        return best != null;
    }

    public LootItem MostValuable
    {
        get
        {
            LootItem best = null;
            foreach (var l in Loot) if (l != null && (best == null || l.TotalValue > best.TotalValue)) best = l;
            return best;
        }
    }

    // Bagajdan geri al: tek elle taşınan eşya oyuncunun eline, büyük eşya (sedye, kangal, koni) bagajın arkasına yere.
    public bool Retrieve(LootItem item, ThirdPersonPlayer player)
    {
        if (item == null || !Loot.Contains(item)) return false;
        Loot.Remove(item);
        var reg = LootRegistry.Instance;
        if (reg != null) reg.ReportRetrieved(item);
        Frame(out var f, out _, out _, out _, out _);
        Vector3 ground = RearPoint;
        if (player != null && TrunkDistance(player.transform.position) < 2.5f)
        {
            // Büyük eşya oyuncunun yanına / arkasına (bagaj ile oyuncu arasında kalmasın)
            Vector3 away = player.transform.position - (transform.position - f * (Follower != null ? Follower.rearOverhang : 2.7f)); away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) ground = player.transform.position + away.normalized * 1.2f;
            if (GroundProbe.Find(ground + Vector3.up * 1.5f, 4f, out RaycastHit g)) ground = g.point;
        }
        item.RetrieveFromVan(player, ground, -f);
        Retrieved++;
        LootRetrieved?.Invoke(this, item);
        return true;
    }

    // Test / geri yükleme: eşyayı kamyonet kaydından sessizce düş (RestoreForTest)
    public static void ForgetLoot(LootItem item)
    {
        foreach (var v in All) if (v != null) v.Loot.Remove(item);
    }

    // Kalkış: bagaj kapandı, içindekiler kesin çalındı (ReportStolen; %50 kontrolü burada)
    private void FinalizeLoot()
    {
        if (LootFinalized) return;
        LootFinalized = true;
        var reg = LootRegistry.Instance;
        foreach (var item in Loot.ToArray())
        {
            if (item == null) continue;
            if (reg != null) reg.ReportStolen(item); else item.MarkStolen();
            StolenCount++;
        }
        Loot.Clear();
        LootFinalizedEvent?.Invoke(this);
    }

    // ---- U4b: ön kapılar ----
    // Kapı noktası (driver: şoför = sol/-sağ; yolcu = sağ) ve hırsızın yürüyeceği NavMesh noktası (kapının 0.7 m dışı).
    public Vector3 FrontDoorPoint(bool driver, out Vector3 doorPos)
    {
        Frame(out var f, out var r, out _, out float front, out float halfW);
        float side = driver ? -1f : 1f;
        doorPos = transform.position + f * (front - 1.4f) + r * side * halfW + Vector3.up * 0.9f;
        Vector3 basePoint = transform.position + f * (front - 1.4f) + r * side * halfW;
        // Kapının 0.7 m dışı; NavMesh orada yoksa biraz daha dışarı / içeri (kapıya yakın kalsın, uzak bir noktaya oturmasın)
        foreach (float o in new[] { 0.7f, 1.0f, 1.3f, 0.5f })
        {
            Vector3 cand = basePoint + r * side * o;
            if (UnityEngine.AI.NavMesh.SamplePosition(cand, out UnityEngine.AI.NavMeshHit hit, 0.6f, UnityEngine.AI.NavMesh.AllAreas)) return hit.position;
        }
        Vector3 approach = basePoint + r * side * 0.7f;
        if (GroundProbe.Find(approach + Vector3.up * 1.5f, 4f, out RaycastHit g)) return g.point;
        return approach;
    }

    // Yaklaşan hırsız her kare çağırır: kapı açık kalır (çağrı kesilince kapanır)
    public void RequestFrontDoor(bool driver)
    {
        frontOpenUntil[driver ? 0 : 1] = Time.time + 0.6f;
    }

    public float FrontDoorAmount(bool driver) { return frontAmount[driver ? 0 : 1]; }

    // Pes eden hırsız ön kapıdan bindi
    public void BoardFront(Thief t, bool driver)
    {
        Boarded++;
        Surrendered++;
        LastBoardWasDriverDoor = driver;
        RequestFrontDoor(driver);
        if (doorSource != null) doorSource.Play();
    }

    private void UpdateFrontDoors(float dt)
    {
        float sec = settings != null ? settings.frontDoorSeconds : 0.4f;
        for (int i = 0; i < 2; i++)
        {
            float target = Time.time < frontOpenUntil[i] ? 1f : 0f;
            frontAmount[i] = Mathf.MoveTowards(frontAmount[i], target, dt / Mathf.Max(0.05f, sec));
        }
        // Sol kapı pivotu ön kenarda: dışa açılış sol tarafta −, sağ tarafta +
        if (doorFrontLeft != null) doorFrontLeft.localRotation = frontLeftClosed * Quaternion.AngleAxis(-frontDoorOpenAngle * frontAmount[0], Vector3.up);
        if (doorFrontRight != null) doorFrontRight.localRotation = frontRightClosed * Quaternion.AngleAxis(frontDoorOpenAngle * frontAmount[1], Vector3.up);
    }

    // =====================================================================================
    // Sürüş fazları
    // =====================================================================================
    // Yönetmen çağırır: şeridin başına koyar, durağı verir.
    public void Begin(LanePath lane, float stopS, float shoulderLateral, int thieves, int waveIndex, ThiefDirector dir, Thief[] thiefPrefabs, float startSpeed)
    {
        if (Follower == null) Follower = GetComponent<LaneFollower>();
        if (Vehicle == null) Vehicle = GetComponent<TrafficVehicle>();
        director = dir;
        prefabs = thiefPrefabs;
        StopS = stopS;
        ShoulderLateral = shoulderLateral;
        BaseLateral = Follower.lateralOffset;
        PlannedThieves = thieves;
        WaveIndex = waveIndex;
        variantOffset = waveIndex;
        Phase = VanPhase.Approaching;
        Follower.Place(lane, 0f, startSpeed);
        Vehicle.externalStopS = stopS;
        Vehicle.parked = false;
        SetDoors(0f);
    }

    private void FixedUpdate()
    {
        if (Follower.lane == null) return;
        float dt = Time.fixedDeltaTime;
        float pull = settings != null ? settings.pullOffLength : 12f;
        UpdateFrontDoors(dt);
        switch (Phase)
        {
            case VanPhase.Approaching:
            {
                float u = Mathf.InverseLerp(StopS - pull, StopS, Follower.Distance);
                Follower.lateralOffset = Mathf.Lerp(BaseLateral, ShoulderLateral, Mathf.SmoothStep(0f, 1f, u));
                if (Follower.AtStop && Follower.StopDistance >= StopS - 0.001f) Park();
                break;
            }
            case VanPhase.Parked:
            {
                ParkedTime += dt;
                float doorSec = settings != null ? settings.doorSeconds : 0.6f;
                if (!WaitingForGap) SetDoors(Mathf.Clamp01(ParkedTime / doorSec));
                // İndirme
                if (!WaitingForGap && DoorAmount >= 0.999f && Spawned < PlannedThieves && Time.time >= nextUnloadAt)
                {
                    SpawnThief();
                    nextUnloadAt = Time.time + (settings != null ? settings.unloadInterval : 0.8f);
                }
                // Kalkış kararı: hepsi döndü / pes etti / kaçtı ya da süre doldu
                bool allBack = Spawned >= PlannedThieves && Boarded + Fled >= Spawned;
                bool timeUp = ParkedTime >= (settings != null ? settings.vanWaitSeconds : 30f);
                if (!WaitingForGap && (allBack || timeUp))
                {
                    WaitingForGap = true;
                    if (!departSoundDone) { departSoundDone = true; doorSource.Play(); }
                    // Geride kalanlar panikler
                    foreach (var t in Thieves) if (t != null && !t.IsGone) t.OnVanLeaving();
                }
                if (WaitingForGap)
                {
                    SetDoors(Mathf.Clamp01(DoorAmount - dt / doorSec));
                    if (DoorAmount <= 0.001f)
                    {
                        FinalizeLoot(); // bagaj kapandı: içindekiler kesin çalındı
                        if (GapClear()) Depart();
                    }
                }
                break;
            }
            case VanPhase.Departing:
            {
                float u = Mathf.InverseLerp(StopS, StopS + pull, Follower.Distance);
                Follower.lateralOffset = Mathf.Lerp(ShoulderLateral, BaseLateral, Mathf.SmoothStep(0f, 1f, u));
                break;
            }
        }
    }

    private void Park()
    {
        Phase = VanPhase.Parked;
        ParkedTime = 0f;
        ParkedAtElapsed = director != null ? director.Elapsed : Time.time;
        Vehicle.parked = true;
        LaneOccupancy.Unregister(Follower); // şeridi kapatmaz
        Follower.lateralOffset = ShoulderLateral;
        nextUnloadAt = Time.time + (settings != null ? settings.doorSeconds : 0.6f);
        if (!doorSoundDone) { doorSoundDone = true; doorSource.Play(); }
        Parked?.Invoke(this);
    }

    private void Depart()
    {
        WaitingForGap = false;
        Phase = VanPhase.Departing;
        LaneOccupancy.Register(Follower);
        Vehicle.parked = false;
        Vehicle.externalStopS = float.PositiveInfinity;
        Follower.ClearStop();
        Departed?.Invoke(this);
    }

    // Şeride dönüş için boşluk: arkadan gelen aracın önüyle en az minGap + hız × 2 sn; önde minGap.
    private bool GapClear()
    {
        var s = Vehicle.settings;
        float minGap = s != null ? s.minGap : 4f;
        float headway = s != null ? s.timeHeadway : 2f;
        foreach (var f in LaneOccupancy.Followers)
        {
            if (f == null || f == Follower || f.lane != Follower.lane) continue;
            if (f.Distance < Follower.Distance)
            {
                if (Follower.RearS - f.FrontS < minGap + f.Speed * headway) return false;
            }
            else if (f.RearS - Follower.FrontS < minGap) return false;
        }
        return true;
    }

    private void SpawnThief()
    {
        if (prefabs == null || prefabs.Length == 0) return;
        var prefab = prefabs[(Spawned + variantOffset) % prefabs.Length];
        Vector3 exit = ExitFor(Spawned, out Vector3 spread, out string kind);
        Vector3 at = exit + spread * ((Spawned % 2 == 0) ? -0.45f : 0.45f);
        if (GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit g)) at = g.point;
        Vector3 away = exit - transform.position; away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) away = -transform.forward;
        var t = Instantiate(prefab, at, Quaternion.LookRotation(away.normalized, Vector3.up));
        t.name = prefab.name + "_W" + (WaveIndex + 1) + "_" + (Spawned + 1);
        t.settings = settings;
        t.BeginUnload(this, director);
        Thieves.Add(t);
        ExitLog.Add(kind);
        Spawned++;
        if (director != null) director.RegisterThief(t);
        ThiefSpawned?.Invoke(this, t);
    }

    public void OnThiefBoarded(Thief t) { Boarded++; }
    public void OnThiefGone(Thief t) { Fled++; }

    // Hırsız eşyayı kamyonete attı: "kamyonette" (geri alınabilir; kalkışta kesin çalınır)
    public void AddLoot(LootItem item)
    {
        if (item == null) return;
        var reg = LootRegistry.Instance;
        if (LootFinalized || !IsParked)
        {
            // Bagaj kapandıktan sonra atılan: doğrudan çalındı
            if (reg != null) reg.ReportStolen(item); else item.MarkStolen();
            StolenCount++;
            LootCount++;
            return;
        }
        item.MarkInVan();
        if (reg != null) reg.ReportInVan(item);
        Loot.Add(item);
        LootCount++;
    }

    private void SetDoors(float amount)
    {
        DoorAmount = amount;
        if (doorLeft != null) doorLeft.localRotation = doorLeftClosed * Quaternion.AngleAxis(doorLeftOpenAngle * amount, Vector3.up);
        if (doorRight != null) doorRight.localRotation = doorRightClosed * Quaternion.AngleAxis(doorRightOpenAngle * amount, Vector3.up);
    }
}
