using System;
using System.Collections.Generic;
using UnityEngine;

// U4: hırsız kamyoneti (trafik PanelVan prefab'ının koyu kopyası). Sürüş ve trafik kuralları (kuyruk, koni, oyuncuya yavaşlama,
// çarpma, şerit sonunda kaybolma) aynen TrafficVehicle'da; bu bileşen yalnız durağı ve parkı yönetir:
//  Approaching: dış durak (TrafficVehicle.externalStopS) = ThiefVanStop'un şeritteki s'si + parkOvershoot (durağı geçip şeritte durur).
//  Maneuvering: (2026-11-05) geri geri bankete park manevrası; yer doluysa (kötü park etmiş minibüs) vura vura iter, ceza yazar;
//               boşaltamazsa şeritte park eder. Ayrıntı: "Sürüş fazları" bölümünün başındaki açıklama.
//  Parked:      TrafficVehicle.parked → sürülmez; şerit kaydından (LaneOccupancy) çıkar, yani şeridi kapatmaz, arkadaki trafik
//               geçer. Arka kapılar açılır ("tak") ve vanWaitSeconds (30) boyunca AÇIK kalır; hırsızlar unloadInterval arayla
//               sırayla arka ve yan kapıdan iner. Hepsi dönünce / pes edince ya da süre dolunca kalkar.
//  Departing:   kapılar kapanır (bagaj artık erişilemez → içindekiler kesin çalındı: LootRegistry.ReportStolen, %50 kontrolü
//               yalnız bu anda), arkadan gelen araçla boşluk yoksa bekler; şeride kaydolur, ileri S-eğrisiyle şeride girer (Merge),
//               sonra trafik gibi gider. Geride kalan hırsızlar kaçar (Thief.Flee).
// U4b (kullanıcı kararları 2026-10-20):
//  - Kamyonete atılan eşya hemen çalınmış sayılmaz: "kamyonette" (LootItem.InVan, LootRegistry.InVanValue, HUD turuncu bölüm).
//    Oyuncu açık bagaja ≤ retrieveRange yaklaşıp yüzünü dönünce en değerliden başlayarak geri alır (PlayerInteraction "E: Geri al").
//  - Pes eden hırsız ÖN kapıdan biner (yolcu tarafı; dalganın son hırsızı şoför kapısı): kapı açılır, biner, kapı kapanır.
[RequireComponent(typeof(LaneFollower))]
[RequireComponent(typeof(TrafficVehicle))]
public class ThiefVan : MonoBehaviour
{
    public static readonly List<ThiefVan> All = new List<ThiefVan>();
    // Test (ThiefVanVerify C): minibüs kayaya dayalı gibi kıpırdamaz (fiziksel itme başlatılmaz). Play başında sıfırlanır.
    public static bool ImmovableMinibusForTest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { ImmovableMinibusForTest = false; All.Clear(); }

    public enum VanPhase { Approaching, Maneuvering, Parked, Departing }

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
    // U4c: kasa içi (builder kurar): iç paneller kökü ve kasa iç sınırları (kök uzayında). Eşyalar kasa zemininde yuvalarda durur.
    [Tooltip("U4c: kasa iç panelleri (zemin, yan duvarlar, tavan, ön bölme).")]
    public Transform cargoRoot;
    [Tooltip("Kasa iç sınırları, kök uzayında: zemin üstü y, tavan altı y, yarı genişlik, arka açıklık z (negatif), ön bölme z.")]
    public float cargoFloorY = 0.57f, cargoCeilingY = 2.3f, cargoHalfWidth = 0.95f, cargoRearZ = -2.7f, cargoFrontZ = 0.5f;
    // Yuva düzeni: 2 sütun × 3 sıra = 6 yuva zeminde; fazlası üst üste (katman başına +stackHeight)
    public const int CargoColumns = 2, CargoRows = 3, CargoFloorSlots = CargoColumns * CargoRows;
    public const float CargoColumnX = 0.5f, CargoFirstRowDepth = 0.55f, CargoRowSpacing = 0.75f, CargoStackHeight = 0.45f;

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
        // Kalkmadan yok edildiyse (test temizliği): kamyonetteki eşyalar çalınmış sayılmaz, yalnız "kamyonette" kaydı düşülür;
        // U4c: eşya kasada görünür durduğundan başlangıç yerine döner (havada kalmasın)
        if (Loot.Count > 0 && !LootFinalized)
        {
            var reg = LootRegistry.Instance;
            foreach (var item in Loot.ToArray())
            {
                if (item == null) continue;
                if (reg != null) reg.ReportRetrieved(item);
                item.ResetToStart();
            }
            Loot.Clear();
        }
        usedSlots.Clear();
        Despawned?.Invoke(this);
    }

    // ---- U4c: kasa yuvaları ----
    private readonly HashSet<int> usedSlots = new HashSet<int>();

    private int FirstFreeSlot()
    {
        int k = 0;
        while (usedSlots.Contains(k)) k++;
        return k;
    }

    // Yuvanın kök uzayındaki zemin noktası: sütun x (±CargoColumnX), sıra z (arkadan öne), katman y (6'dan sonrası üst üste)
    public Vector3 CargoSlotLocal(int slot)
    {
        if (slot < 0) slot = 0;
        int layer = slot / CargoFloorSlots, k = slot % CargoFloorSlots;
        int row = k / CargoColumns, col = k % CargoColumns;
        float x = (col == 0 ? -1f : 1f) * Mathf.Min(CargoColumnX, cargoHalfWidth - 0.3f);
        float z = cargoRearZ + CargoFirstRowDepth + row * CargoRowSpacing;
        z = Mathf.Min(z, cargoFrontZ - 0.35f);
        return new Vector3(x, cargoFloorY + layer * CargoStackHeight, z);
    }

    // Eşyanın kasadaki duruşu (dünya): zemin noktası + yön (kamyonet ileri). Sedye kasa ortasında boylu boyunca, yuva kullanmaz.
    public bool CargoPose(LootItem item, out Vector3 floorPoint, out Vector3 forward)
    {
        forward = transform.forward;
        if (item == null) { floorPoint = transform.position; return false; }
        if (item.GetComponent<StretcherController>() != null)
        {
            floorPoint = transform.TransformPoint(new Vector3(0f, cargoFloorY, (cargoRearZ + cargoFrontZ) * 0.5f));
            return true;
        }
        floorPoint = transform.TransformPoint(CargoSlotLocal(item.VanSlot));
        return true;
    }

    // Kasa iç kutusu (dünya; testler okur): eşya sınırları bunun içinde kalmalı
    public Bounds CargoWorldBounds
    {
        get
        {
            var b = new Bounds(transform.TransformPoint(new Vector3(0f, (cargoFloorY + cargoCeilingY) * 0.5f, (cargoRearZ + cargoFrontZ) * 0.5f)), Vector3.zero);
            foreach (float x in new[] { -cargoHalfWidth, cargoHalfWidth })
                foreach (float y in new[] { cargoFloorY, cargoCeilingY })
                    foreach (float z in new[] { cargoRearZ, cargoFrontZ })
                        b.Encapsulate(transform.TransformPoint(new Vector3(x, y, z)));
            return b;
        }
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
        usedSlots.Remove(item.VanSlot);
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

    // Test: bütün kaydı düş (LootRegistry.ResetForTest)
    public void ClearLootForTest() { Loot.Clear(); usedSlots.Clear(); }

    // Test / geri yükleme: eşyayı kamyonet kaydından sessizce düş (RestoreForTest)
    public static void ForgetLoot(LootItem item)
    {
        foreach (var v in All) if (v != null && v.Loot.Remove(item)) v.usedSlots.Remove(item.VanSlot);
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
        usedSlots.Clear();
        LootFinalizedEvent?.Invoke(this);
    }

    // ---- U4b: ön kapılar ----
    // Kapı noktası (driver: şoför = sol/-sağ; yolcu = sağ) ve hırsızın yürüyeceği NavMesh noktası (kapının 0.7 m dışı, yola bağlı).
    // İstenen tarafın önü yürünemezse (banket eğimi / NavMesh yok) öbür ön kapı kullanılır; usedDriver gerçekte kullanılan taraf.
    public Vector3 FrontDoorPoint(bool driver, out Vector3 doorPos) { return FrontDoorPoint(driver, out doorPos, out _); }

    public Vector3 FrontDoorPoint(bool driver, out Vector3 doorPos, out bool usedDriver)
    {
        Frame(out var f, out var r, out _, out float front, out float halfW);
        Vector3 road = Follower != null && Follower.lane != null ? Follower.lane.PointAt(Follower.Distance) : transform.position - r * ShoulderLateral;
        foreach (bool side in new[] { driver, !driver })
        {
            float sgn = side ? -1f : 1f;
            Vector3 basePoint = transform.position + f * (front - 1.4f) + r * sgn * halfW;
            // Kapının 0.7 m dışı; orada yoksa biraz daha dışarı / içeri ve öne / arkaya (kapıya yakın kalsın)
            foreach (float o in new[] { 0.7f, 1.0f, 1.3f, 0.5f })
                foreach (float dz in new[] { 0f, -0.4f, 0.4f })
                {
                    Vector3 cand = basePoint + r * sgn * o + f * dz;
                    if (UnityEngine.AI.NavMesh.SamplePosition(cand, out UnityEngine.AI.NavMeshHit hit, 0.6f, UnityEngine.AI.NavMesh.AllAreas) && ConnectedTo(hit.position, road))
                    {
                        doorPos = basePoint + Vector3.up * 0.9f;
                        usedDriver = side;
                        return hit.position;
                    }
                }
        }
        float s0 = driver ? -1f : 1f;
        Vector3 bp = transform.position + f * (front - 1.4f) + r * s0 * halfW;
        doorPos = bp + Vector3.up * 0.9f;
        usedDriver = driver;
        Vector3 approach = bp + r * s0 * 0.7f;
        if (GroundProbe.Find(approach + Vector3.up * 1.5f, 4f, out RaycastHit g)) return g.point;
        return approach;
    }

    // İstenen taraftaki ön kapıya yürünebiliyor mu (testler okur)
    public bool FrontDoorReachable(bool driver) { FrontDoorPoint(driver, out _, out bool used); return used == driver; }

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
    // Park manevrası (kullanıcı kararı 2026-11-05: "gerçekten park eder gibi görünsün"): kamyonet durağı şeritte parkOvershoot kadar
    // geçip durur (son 12 m'de şeridin sağına parkPreShift kayar), sonra kinematik bisiklet modeliyle (arka aks referans, direksiyon
    // açısı → sapma) GERİ GERİ S-eğrisi çizerek bankete girer; tekerlekler döner ve direksiyon kırılır (LaneFollower.ApplyExternalPose).
    // İleri yanaşma seçilmedi: durak çekicinin (park alanının) hemen önünde; gerçek bir yayla ileri yanaşmak iyi park etmiş
    // minibüsün ön köşesini süpürürdü (eski davranış gövdeyi yan kaydırıyordu, direksiyon yoktu).
    // Yer doluysa (kötü park etmiş minibüs durağın üstünde): temasta minibüs fiziksel itmeye geçer (ServiceVanDriver.BeginPhysicalShove,
    // PhysX kinematik kamyonetle iter), kamyonet ramPushDistance kadar iter, ramBackOff ileri açılır, artan hızla yeniden vurur. Her
    // vuruş ThiefDirector.ChargeRam → ₺ ceza (hıza göre), HUD uçan yazı, görev sonu hesabı. Bütçe: maxRams / maxRamSeconds / maxBlockedRams; yer
    // yine boşalmadıysa (minibüs kayaya dayalı) kamyonet şeride geri çıkıp ORADA park eder (ParkedInLane; şerit kaydında kalır,
    // arkadaki trafik bekler) — sonsuz döngü yok, zemin varsayımı yok. Kalkışta şeride ileri S-eğrisiyle girer, sonra LaneFollower sürer.
    // Arkada araç: geri rotası önce bankete kırılır, sonra düz gelir; arka köşe arkadaki aracın önünden geçer, gövde şeritten çıkınca
    // şerit kaydından düşer ve araç geçer. Yine de araca değecekse kamyonet geri gitmeyi kesip şeritte park eder (çarpmaz). Her temas
    // (hangi adımda olursa olsun) tek noktada ücretlenir: CheckMinibusContact.
    public enum ManeuverStep { None, Reverse, Push, BackOff, Pause, Fallback, Merge }

    public ManeuverStep Step { get; private set; }
    public float OvershootS { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public Vector3 TargetForward { get; private set; }
    public bool HasParked { get; private set; }
    public bool ParkedInLane { get; private set; }
    public bool SpotOccupied { get; private set; }
    public int Rams { get; private set; }
    public int RamPenaltyTotal { get; private set; }
    public float ManeuverTime { get; private set; }
    public float ManeuverSpeed { get; private set; }     // işaretli (geri −)
    public float ManeuverSteer { get; private set; }     // ön tekerlek açısı (sağa +)
    public float MinSpeedSeen { get; private set; }
    public float MaxSteerAbsSeen { get; private set; }
    public float ReverseDistance { get; private set; }
    public float ForwardDistance { get; private set; }
    public bool TouchingMinibus { get; private set; }
    public bool LaneCleared { get; private set; }
    public readonly List<string> ManeuverLog = new List<string>();

    public event Action<ThiefVan, int, float> RammedMinibus; // ceza (₺), çarpma hızı

    private Rigidbody body;
    private Vector3 rearAxle, heading;
    private float steer, speedSigned, halfSpan, wheelbase;
    private readonly List<Vector3> path = new List<Vector3>();
    private float[] pathCum = new float[0];
    private int pathIndex;
    private int pathDir = 1;
    private Vector3 pathEndPos, pathEndFwd;
    private float maneuverStart = -1f, pauseUntil, hitTime, pushTravel, nextPushNote;
    private Vector3 backOffStart, hitMinibusPos;
    private int attempt, blockedStreak, seenShoves;
    private float lastHitTime = -100f;
    private bool justHit;
    private const float HitCooldown = 0.8f;   // temas titreşimi çift ceza yazmasın
    private const float LaneClearMargin = 0.3f;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private const float SoftSnapLength = 1.5f;

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
        Step = ManeuverStep.None;
        float overshoot = settings != null ? settings.parkOvershoot : 8.5f;
        OvershootS = Mathf.Min(stopS + overshoot, lane.Length - Follower.frontOverhang - 2f);
        TargetPosition = lane.PointAt(stopS) + lane.RightAt(stopS) * shoulderLateral;
        TargetForward = Flat(lane.TangentAt(stopS));
        Follower.Place(lane, 0f, startSpeed);
        Vehicle.externalStopS = OvershootS;
        Vehicle.parked = false;
        SetDoors(0f);
    }

    private void FixedUpdate()
    {
        if (Follower.lane == null) return;
        float dt = Time.fixedDeltaTime;
        UpdateFrontDoors(dt);
        justHit = false;
        if (Phase != VanPhase.Parked) CheckMinibusContact(Step != ManeuverStep.None ? Mathf.Abs(speedSigned) : Follower.Speed);
        switch (Phase)
        {
            case VanPhase.Approaching:
            {
                // Son 12 m'de şeridin sağına küçük kayma: geri manevrada burun karşı şeride taşmaz
                float pre = settings != null ? settings.parkPreShift : 0.8f;
                float u = Mathf.InverseLerp(OvershootS - 12f, OvershootS, Follower.Distance);
                Follower.lateralOffset = Mathf.Lerp(BaseLateral, BaseLateral + pre, Mathf.SmoothStep(0f, 1f, u));
                if (Follower.AtStop && Follower.StopDistance >= OvershootS - 0.001f) BeginManeuver();
                break;
            }
            case VanPhase.Maneuvering:
                Maneuver(dt);
                break;
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
                if (Step == ManeuverStep.Merge) Maneuver(dt);
                break;
        }
    }

    // ---- manevra ----
    private void BeginManeuver()
    {
        Phase = VanPhase.Maneuvering;
        Vehicle.parked = true; // TrafficVehicle sürmez / kaybolmaz; şerit kaydı sürer (gövde şeritte: arkadaki trafik bekler)
        InitController();
        maneuverStart = Time.time;
        ManeuverTime = 0f;
        attempt = 0;
        blockedStreak = 0;
        MinSpeedSeen = 0f; MaxSteerAbsSeen = 0f; ReverseDistance = 0f; ForwardDistance = 0f;
        Note("manevra başladı s " + Follower.Distance.ToString("F1") + " hedef s " + StopS.ToString("F1") + " yanal " + ShoulderLateral.ToString("F2"));
        StartReverse();
    }

    private void InitController()
    {
        wheelbase = Follower.Wheelbase;
        halfSpan = wheelbase * 0.5f;
        heading = Flat(transform.forward);
        rearAxle = transform.position - heading * halfSpan;
        steer = 0f;
        speedSigned = 0f;
    }

    private void StartReverse()
    {
        // Rota: arka aks, şimdiki (s, yanal)'dan (durak − yarım dingil, banket)'e S-eğrisiyle; geri gidiş
        var lane = Follower.lane;
        float s0 = lane.Project(rearAxle, out float lat0, out _);
        float s1 = StopS - halfSpan;
        BuildPath(lane, s0, lat0, s1, ShoulderLateral, -1, settings != null ? settings.reverseSwingShare : 0.7f);
        pathEndPos = TargetPosition; pathEndFwd = TargetForward;
        SpotOccupied = IsSpotOccupied();
        Step = ManeuverStep.Reverse;
    }

    // (s, yanal) uzayında yumuşak S-eğrisi; 0.4 m adım. dir: +1 ileri (s artar), −1 geri (s azalır)
    // swingShare: yanal geçişin bittiği rota oranı (1 = bütün yol boyunca; küçükse sonrası düz)
    private void BuildPath(LanePath lane, float s0, float lat0, float s1, float lat1, int dir, float swingShare = 1f)
    {
        path.Clear();
        float len = Mathf.Abs(s1 - s0);
        int n = Mathf.Max(2, Mathf.CeilToInt(len / 0.4f));
        for (int i = 0; i <= n; i++)
        {
            float u = (float)i / n;
            float s = Mathf.Lerp(s0, s1, u);
            float lat = Mathf.Lerp(lat0, lat1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / swingShare)));
            path.Add(lane.PointAt(s) + lane.RightAt(s) * lat);
        }
        pathCum = new float[path.Count];
        for (int i = 1; i < path.Count; i++) pathCum[i] = pathCum[i - 1] + Horizontal(path[i] - path[i - 1]).magnitude;
        pathIndex = 0;
        pathDir = dir;
    }

    private float RamSpeed(int k)
    {
        if (settings == null) return 1.5f + 0.6f * k;
        return Mathf.Min(settings.ramSpeedMax, settings.ramSpeedMin + settings.ramSpeedStep * k);
    }

    private void Maneuver(float dt)
    {
        ManeuverTime += dt;
        var minibus = ServiceVanDriver.Instance;
        float cruise = settings != null ? settings.maneuverSpeed : 1.8f;
        float reverse = settings != null ? settings.reverseSpeed : 1.4f;
        float pushDist = settings != null ? settings.ramPushDistance : 1.2f;
        float backOff = settings != null ? settings.ramBackOff : 2.2f;
        int maxRams = settings != null ? settings.maxRams : 6;
        float maxSec = settings != null ? settings.maxRamSeconds : 40f;
        if (Phase == VanPhase.Maneuvering)
        {
            UpdateLaneClear();
            // Bekçi: hangi adımda olursa olsun manevra sonsuza uzamaz
            if (Step != ManeuverStep.Fallback && ManeuverTime > maxSec + 25f) { Note("manevra süresi doldu → şeritte park"); StartFallback(); }
        }
        switch (Step)
        {
            case ManeuverStep.Reverse:
            {
                // Gövde hâlâ şeritteyken arkadan araç yaklaştıysa geri gidilmez: şeritte kal (araç bekler; çarpmaz)
                if (!LaneCleared && RearConflict()) { Note("arkada araç → geri manevra iptal, şeritte park"); StartFallback(); break; }
                // Yer doluysa artan vuruş hızıyla, boşsa park hızıyla geri
                float v = SpotOccupied ? RamSpeed(attempt) : reverse;
                bool done = FollowPath(dt, v, out _);
                if (justHit && minibus != null)
                {
                    Step = ManeuverStep.Push; pushTravel = 0f; hitTime = Time.time; hitMinibusPos = minibus.transform.position; nextPushNote = Time.time;
                }
                else if (done)
                {
                    // Rota bitti ama yer hâlâ dolu (temas algılanmadan, ör. dönmüş minibüs): gömülü park etme, açıl ve yeniden dene (süre bütçesi)
                    if (IsSpotOccupied() || TouchingMinibus) { Note("rota bitti, yer hâlâ dolu → geri açıl"); backOffStart = rearAxle; Step = ManeuverStep.BackOff; }
                    else FinishShoulderPark();
                }
                break;
            }
            case ManeuverStep.Push:
            {
                // Temas sürerken rotada geri itmeye devam; bütçe: itme yolu, minibüs kıpırdamıyorsa (dayalı) ya da rota bitti
                bool done = FollowPath(dt, RamSpeed(attempt), out _);
                pushTravel += Mathf.Abs(speedSigned) * dt;
                // Dayalı mı: itme yönünde engel / zemin kenarı var, minibüs kamyonetin yolunu izlemiyor (kamyonet gövdeye gömülüyor) ya da
                // 0.8 sn'de kıpırdamadı. Erken bırakılır: sıkışan minibüsü PhysX açık tarafa fırlatır (ölçüldü).
                float moved = minibus != null ? Horizontal(minibus.transform.position - hitMinibusPos).magnitude : 0f;
                bool noRoom = minibus != null && !minibus.CanBePushed(heading * pathDir, 0.8f, GetComponent<Rigidbody>());
                bool blocked = minibus != null && (noRoom || (Time.time - hitTime > 0.15f && pushTravel - moved > 0.45f) || (Time.time - hitTime > 0.8f && moved < 0.15f));
                if (Time.time >= nextPushNote) { nextPushNote = Time.time + 0.25f; Note("  itme: kamyonet " + pushTravel.ToString("F2") + " m, minibüs " + moved.ToString("F2") + " m, hız " + (minibus != null ? minibus.Body.linearVelocity.magnitude.ToString("F2") : "-") + " durum " + (minibus != null ? minibus.State.ToString() : "-")); }
                SpotOccupied = IsSpotOccupied();
                if (!SpotOccupied && !TouchingMinibus) { Note("yer boşaldı (" + Rams + ". vuruş)"); Step = ManeuverStep.Reverse; }
                else if (pushTravel >= pushDist || blocked || done)
                {
                    Note((noRoom ? "minibüsün arkası dolu / kenar" : blocked ? "minibüs kıpırdamıyor" : "itme bütçesi doldu") + " → geri açıl");
                    blockedStreak = moved > 0.3f ? 0 : blockedStreak + 1;
                    backOffStart = rearAxle;
                    Step = ManeuverStep.BackOff;
                }
                break;
            }
            case ManeuverStep.BackOff:
            {
                // Düz ileri (yön korunur), ramBackOff kadar
                Integrate(dt, cruise, 0f);
                if (Horizontal(rearAxle - backOffStart).magnitude >= backOff) { Step = ManeuverStep.Pause; pauseUntil = Time.time + 0.6f; }
                break;
            }
            case ManeuverStep.Pause:
            {
                Integrate(dt, 0f, 0f);
                if (Time.time < pauseUntil) break;
                SpotOccupied = IsSpotOccupied();
                int maxBlocked = settings != null ? settings.maxBlockedRams : 3;
                // Yer boş olsa bile bütçe geçerli: rotadaki dayalı minibüs vuruşu sonsuza uzatmasın
                if (Rams < maxRams && Time.time - maneuverStart < maxSec && blockedStreak < maxBlocked) { attempt++; StartReverse(); }
                else
                {
                    Note("yer boşaltılamadı (" + Rams + " vuruş, " + (Time.time - maneuverStart).ToString("F0") + " sn) → şeritte park");
                    StartFallback();
                }
                break;
            }
            case ManeuverStep.Fallback:
            {
                if (FollowPath(dt, cruise, out _)) FinishLanePark();
                break;
            }
            case ManeuverStep.Merge:
            {
                if (FollowPath(dt, cruise * 1.6f, out _)) FinishMerge();
                break;
            }
        }
    }

    // Tek ücret noktası (fizik adımı başı): gövde minibüse YENİ değdi ya da PhysX gövde teması itmeyi başlattı (ScanForPusher; Touches
    // kutusu kaçırmış olabilir) → vuruş. Eskiden yalnız Reverse adımındaki kutu teması sayılıyordu: ikinci fiziksel itme ücretsiz kalıyordu.
    // Oyuncu sürerken minibüsü kamyonete çarparsa ceza yok (kötü park değil).
    private void CheckMinibusContact(float speed)
    {
        var m = ServiceVanDriver.Instance;
        if (m == null || !m.isActiveAndEnabled) { TouchingMinibus = false; return; }
        bool was = TouchingMinibus;
        TouchingMinibus = Touches(m);
        if (m.State == ServiceVanDriver.VanState.Driving) { seenShoves = m.PhysicalShoves; return; }
        if (body == null) body = GetComponent<Rigidbody>();
        // Gidiş yönü minibüse doğru mu: geri açılırken süren temas (ya da açılan itme) vuruş sayılmaz
        Vector3 moveDir = Step != ManeuverStep.None ? heading * Mathf.Sign(speedSigned) : Flat(transform.forward);
        // (BackOff başında kamyonet hâlâ geriye doğru yavaşlıyor: aynı vuruşun kuyruğu, yeni vuruş değil)
        bool toward = Step != ManeuverStep.BackOff && Step != ManeuverStep.Pause && Vector3.Dot(moveDir, Horizontal(m.transform.position - transform.position)) > 0f;
        // Test: "kayaya dayalı" minibüs — fiziksel itme başlatılmaz, minibüs kinematik kalır (kıpırdamaz)
        if (speed >= 0.3f && toward && TouchingMinibus && !ImmovableMinibusForTest) m.BeginPhysicalShove(body, moveDir);
        bool shovedByMe = m.PhysicalShoves != seenShoves && m.LastPusher == body;
        if (!shovedByMe || !toward) seenShoves = m.PhysicalShoves;
        if (!toward || !(shovedByMe || (TouchingMinibus && !was && speed >= 0.3f)) || Time.time - lastHitTime < HitCooldown) return; // itme ücreti kilit bitince yazılır
        seenShoves = m.PhysicalShoves;
        Hit(m, speed);
    }

    // Trafik kuralı çarpması (bekle-korna-çarp; ThiefDirector ücretler): sayaç ve çift ücret kilidi
    public void NoteLaneRam(int fine, float speed)
    {
        Rams++;
        RamPenaltyTotal += fine;
        lastHitTime = Time.time;
        Note(Rams + ". vuruş (şerit) " + speed.ToString("F1") + " m/sn → −₺" + fine);
        RammedMinibus?.Invoke(this, fine, speed);
    }

    // Vuruş: ceza yazılır, uçan yazı HUD'da; Reverse adımı justHit ile itmeye geçer
    private void Hit(ServiceVanDriver minibus, float v)
    {
        Rams++;
        justHit = true;
        lastHitTime = Time.time;
        int fine = director != null ? director.ChargeRam(this, v, minibus.transform.position, "manevra") : (settings != null ? settings.RamPenalty(v) : 100);
        RamPenaltyTotal += fine;
        Note(Rams + ". vuruş " + v.ToString("F1") + " m/sn → −₺" + fine);
        RammedMinibus?.Invoke(this, fine, v);
    }

    // Rota takibi (pure pursuit, arka aks referans). Dönüş: rota bitti mi. remaining: rotada kalan yol (m).
    private bool FollowPath(float dt, float cruise, out float remaining)
    {
        if (path.Count < 2) { remaining = 0f; Integrate(dt, 0f, 0f); return true; }
        // En yakın rota noktası (geriye gitmez)
        float best = float.MaxValue; int bi = pathIndex;
        for (int i = pathIndex; i < path.Count; i++)
        {
            float d = Horizontal(path[i] - rearAxle).sqrMagnitude;
            if (d < best) { best = d; bi = i; }
        }
        pathIndex = bi;
        float total = pathCum[path.Count - 1];
        remaining = total - pathCum[pathIndex];
        // Sonu geçtiyse bitti
        Vector3 toEnd = Horizontal(path[path.Count - 1] - rearAxle);
        bool passedEnd = pathIndex >= path.Count - 1 || (remaining < 0.6f && Vector3.Dot(toEnd, heading * pathDir) <= 0.02f);
        if (remaining < 0.08f || passedEnd) { Integrate(dt, 0f, 0f); ApplyPose(dt, 0f, 1f); return Mathf.Abs(speedSigned) < 0.05f; }
        // İleriye bakış noktası
        float look = settings != null ? settings.lookAhead : 3.2f;
        Vector3 target = path[path.Count - 1];
        for (int i = pathIndex; i < path.Count; i++)
            if (pathCum[i] - pathCum[pathIndex] >= look) { target = path[i]; break; }
        Vector3 right = Vector3.Cross(Vector3.up, heading);
        Vector3 rel = Horizontal(target - rearAxle);
        float d2 = Mathf.Max(0.25f, rel.sqrMagnitude);
        float curvature = 2f * Vector3.Dot(rel, right) / d2;
        float steerTarget = Mathf.Atan(curvature * wheelbase) * Mathf.Rad2Deg;
        // Sona yaklaşırken yavaşla (v² = 2·a·d)
        float accel = settings != null ? settings.maneuverAccel : 2.5f;
        float v = Mathf.Min(cruise, Mathf.Sqrt(2f * accel * Mathf.Max(0f, remaining)) + 0.3f);
        Integrate(dt, v * pathDir, steerTarget);
        float w = 1f - Mathf.Clamp01(remaining / SoftSnapLength);
        ApplyPose(dt, speedSigned * dt, w);
        return false;
    }

    // Bisiklet modeli adımı: direksiyon ve hız sınırlı değişir, sapma = yol / dingil × tan(direksiyon)
    private void Integrate(float dt, float targetSpeed, float steerTarget)
    {
        float maxSteer = Follower.maxSteerAngle;
        float steerRate = settings != null ? settings.steerRateDegPerSec : 110f;
        float accel = settings != null ? settings.maneuverAccel : 2.5f;
        steer = Mathf.MoveTowards(steer, Mathf.Clamp(steerTarget, -maxSteer, maxSteer), steerRate * dt);
        speedSigned = Mathf.MoveTowards(speedSigned, targetSpeed, accel * dt);
        float ds = speedSigned * dt;
        float dyaw = ds / Mathf.Max(0.5f, wheelbase) * Mathf.Tan(steer * Mathf.Deg2Rad) * Mathf.Rad2Deg;
        heading = Flat(Quaternion.AngleAxis(dyaw, Vector3.up) * heading);
        rearAxle += heading * ds;
        ManeuverSpeed = speedSigned; ManeuverSteer = steer;
        MinSpeedSeen = Mathf.Min(MinSpeedSeen, speedSigned);
        MaxSteerAbsSeen = Mathf.Max(MaxSteerAbsSeen, Mathf.Abs(steer));
        if (ds < 0f) ReverseDistance -= ds; else ForwardDistance += ds;
        if (Step == ManeuverStep.BackOff || Step == ManeuverStep.Pause) ApplyPose(dt, ds, 0f);
    }

    // Pozu uygula: kök = arka aks + yarım dingil; yükseklik ve eğim aks altındaki zeminden; snap: hedef poza yumuşak karışım (0–1)
    private void ApplyPose(float dt, float travelled, float snap)
    {
        Vector3 fwd = heading;
        Vector3 rearP = rearAxle, frontP = rearAxle + fwd * wheelbase;
        rearP.y = GroundY(rearP, transform.position.y);
        frontP.y = GroundY(frontP, transform.position.y);
        Vector3 pos = (rearP + frontP) * 0.5f;
        Quaternion rot = Quaternion.LookRotation((frontP - rearP).normalized, Vector3.up);
        if (snap > 0f)
        {
            // Son metrelerde rota hatası hedef poza yumuşakça karıştırılır (sıçrama yok, park pozu tam duraktır)
            Vector3 end = pathEndPos;
            end.y = GroundY(pathEndPos, pos.y);
            pos = Vector3.Lerp(pos, end, snap);
            rot = Quaternion.Slerp(rot, Quaternion.LookRotation(pathEndFwd, Vector3.up), snap);
        }
        float laneS = Follower.lane.Project(pos);
        Follower.ApplyExternalPose(pos, rot, travelled, steer, laneS, dt);
    }

    // Zemin yüksekliği (kendi parçaları, minibüs ve hareketli nesneler hariç)
    private float GroundY(Vector3 p, float fallback)
    {
        int n = Physics.RaycastNonAlloc(p + Vector3.up * 2.5f, Vector3.down, groundHits, 5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue, y = fallback;
        for (int i = 0; i < n; i++)
        {
            var h = groundHits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<ServiceVanDriver>() != null || GroundProbe.IsMovingGameObject(h.collider)) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        return y;
    }

    // Kamyonet gövdesi minibüse değiyor mu? İki yönlü: kamyonet gövdesinin örnek noktaları (tampon yüksekliği) minibüsün ayak
    // izinde YA DA minibüsün köşeleri / kenar ortaları kamyonet gövdesinde (dönmüş minibüsün köşesi iki örnek arasına girebilir).
    private bool Touches(ServiceVanDriver m)
    {
        float halfW = Vehicle.bodySize.x * 0.5f, rear = Follower.rearOverhang, front = Follower.frontOverhang;
        foreach (float x in new[] { -halfW, 0f, halfW })
            foreach (float z in new[] { -rear, -rear * 0.5f, 0f, front * 0.5f, front })
                if (m.InsideFootprint(transform.TransformPoint(new Vector3(x, 0.6f, z)), 0.08f)) return true;
        var fp = m.Footprint();
        for (int i = 0; i < fp.Length; i++)
        {
            if (InBody(fp[i], 0.08f) || InBody((fp[i] + fp[(i + 1) % fp.Length]) * 0.5f, 0.08f)) return true;
        }
        return false;
    }

    // Dünya noktası kamyonet gövde kutusunda mı (yatay; düşeyde gövde ± 0.5 m)
    private bool InBody(Vector3 world, float margin)
    {
        Vector3 l = transform.InverseTransformPoint(world) - Vehicle.bodyCenter;
        Vector3 h = Vehicle.bodySize * 0.5f;
        return Mathf.Abs(l.x) <= h.x + margin && Mathf.Abs(l.z) <= h.z + margin && Mathf.Abs(l.y) <= h.y + 0.5f;
    }

    // Testler: kamyonet şu an minibüse değiyor / gömülü mü
    public bool OverlapsMinibus() { return ServiceVanDriver.Instance != null && Touches(ServiceVanDriver.Instance); }

    // Hedef park dikdörtgeni (durak pozu, gövde + 0.35 m pay) minibüsle kesişiyor mu? İki yönlü: minibüs köşeleri dikdörtgende
    // ya da dikdörtgen örnekleri minibüs ayak izinde (minibüs kamyonetten büyük: ortalanmışsa köşeleri dışarıda kalır).
    public bool IsSpotOccupied()
    {
        var m = ServiceVanDriver.Instance;
        if (m == null || !m.isActiveAndEnabled) return false;
        float pad = 0.35f;
        float halfW = Vehicle.bodySize.x * 0.5f + pad, rear = Follower.rearOverhang + pad, front = Follower.frontOverhang + pad;
        Vector3 fwd = TargetForward, right = Vector3.Cross(Vector3.up, fwd);
        var fp = m.Footprint();
        for (int i = 0; i < fp.Length; i++)
        {
            foreach (var p in new[] { fp[i], (fp[i] + fp[(i + 1) % fp.Length]) * 0.5f })
            {
                Vector3 rel = Horizontal(p - TargetPosition);
                float dx = Vector3.Dot(rel, right), dz = Vector3.Dot(rel, fwd);
                if (Mathf.Abs(dx) <= halfW && dz >= -rear && dz <= front && Mathf.Abs(p.y - TargetPosition.y) < 3f) return true;
            }
        }
        foreach (float x in new[] { -halfW, 0f, halfW })
            foreach (float z in new[] { -rear, -rear * 0.5f, 0f, front * 0.5f, front })
                if (m.InsideFootprint(TargetPosition + right * x + fwd * z + Vector3.up * 0.6f, 0f)) return true;
        return false;
    }

    // Gövdenin dört köşesi de şerit dışında mı (banket tarafında): dışındaysa şerit kaydından düşülür (arkadaki araç geçer), şeride
    // dönünce yeniden kaydolunur
    private void UpdateLaneClear()
    {
        var lane = Follower.lane;
        float halfW = Vehicle.bodySize.x * 0.5f, limit = lane.width * 0.5f + LaneClearMargin;
        float side = ShoulderLateral >= 0f ? 1f : -1f;
        bool clear = true;
        foreach (float z in new[] { -Follower.rearOverhang, Follower.frontOverhang })
            foreach (float x in new[] { -halfW, halfW })
            {
                lane.Project(transform.TransformPoint(new Vector3(x, 0.6f, z)), out float lat, out _);
                if (lat * side < limit) clear = false;
            }
        if (clear == LaneCleared) return;
        LaneCleared = clear;
        if (clear) LaneOccupancy.Unregister(Follower); else LaneOccupancy.Register(Follower);
    }

    // Geri giderken arka köşe arkadaki araca değecek mi: araç bu kadar yakında VE yanal olarak gövdesi hizasında
    private bool RearConflict()
    {
        float need = settings != null ? settings.reverseRearClearance : 1.2f;
        var lane = Follower.lane;
        float side = ShoulderLateral >= 0f ? 1f : -1f, halfW = Vehicle.bodySize.x * 0.5f;
        float cornerLat = float.PositiveInfinity;
        foreach (float x in new[] { -halfW, halfW })
        {
            lane.Project(transform.TransformPoint(new Vector3(x, 0.6f, -Follower.rearOverhang)), out float lat, out _);
            cornerLat = Mathf.Min(cornerLat, lat * side);
        }
        foreach (var f in LaneOccupancy.Followers)
        {
            if (f == null || f == Follower || f.lane != lane || f.Distance >= Follower.Distance) continue;
            if (f.FrontS <= Follower.RearS - need) continue;
            var tv = f.GetComponent<TrafficVehicle>();
            float edge = f.lateralOffset * side + (tv != null ? tv.bodySize.x * 0.5f : 1f) + 0.25f;
            if (cornerLat < edge) return true;
        }
        return false;
    }

    // Rotayı şeride geri çıkıp şeritte park etmek üzere kurar (yer boşaltılamadı / ileri çıkılamıyor)
    private void StartFallback()
    {
        var lane = Follower.lane;
        float s0 = lane.Project(rearAxle, out float lat0, out _);
        float s1 = Mathf.Max(OvershootS, s0 + halfSpan + 3f);
        s1 = Mathf.Min(s1, lane.Length - Follower.frontOverhang - 2f);
        BuildPath(lane, s0, lat0, s1 - halfSpan, BaseLateral, 1);
        Follower.lateralOffset = BaseLateral;
        Follower.Pose(s1, out Vector3 p, out Quaternion r);
        pathEndPos = p; pathEndFwd = Flat(r * Vector3.forward);
        Step = ManeuverStep.Fallback;
    }


    private void FinishShoulderPark()
    {
        Note("bankete park: s " + Follower.Distance.ToString("F1") + " (" + Rams + " vuruş, " + ManeuverTime.ToString("F1") + " sn)");
        Step = ManeuverStep.None;
        Park(false);
    }

    private void FinishLanePark()
    {
        Note("şeritte park: s " + Follower.Distance.ToString("F1"));
        Step = ManeuverStep.None;
        LaneCleared = false;
        LaneOccupancy.Register(Follower);
        Park(true);
    }

    private void Park(bool inLane)
    {
        Phase = VanPhase.Parked;
        HasParked = true;
        ParkedInLane = inLane;
        ParkedTime = 0f;
        ParkedAtElapsed = director != null ? director.Elapsed : Time.time;
        Vehicle.parked = true;
        if (!inLane) LaneOccupancy.Unregister(Follower); // bankette şeridi kapatmaz; şeritte kaldıysa kayıtlı kalır (trafik bekler)
        Follower.lateralOffset = inLane ? BaseLateral : ShoulderLateral;
        nextUnloadAt = Time.time + (settings != null ? settings.doorSeconds : 0.6f);
        if (!doorSoundDone) { doorSoundDone = true; doorSource.Play(); }
        Parked?.Invoke(this);
    }

    // Kalkış: şeride kaydol; bankettense ileri S-eğrisiyle şeride gir (Merge), sonra LaneFollower sürer; şeritteyse hemen sür
    private void Depart()
    {
        WaitingForGap = false;
        Phase = VanPhase.Departing;
        LaneOccupancy.Register(Follower);
        Departed?.Invoke(this);
        if (ParkedInLane) { Resume(Follower.Distance, 0f); return; }
        InitController();
        var lane = Follower.lane;
        float s0 = lane.Project(rearAxle, out float lat0, out _);
        float s1 = Mathf.Min(OvershootS + 2f, lane.Length - Follower.frontOverhang - 2f) - halfSpan;
        BuildPath(lane, s0, lat0, s1, BaseLateral, 1);
        Follower.lateralOffset = BaseLateral;
        Follower.Pose(s1 + halfSpan, out Vector3 p, out Quaternion r);
        pathEndPos = p; pathEndFwd = Flat(r * Vector3.forward);
        Step = ManeuverStep.Merge;
        Note("kalkış: şeride giriş s " + s0.ToString("F1") + " → " + (s1 + halfSpan).ToString("F1"));
    }

    private void FinishMerge()
    {
        Step = ManeuverStep.None;
        Resume(Follower.lane.Project(transform.position), Mathf.Abs(speedSigned));
    }

    // LaneFollower'a devir: şeritteki s'den normal trafik gibi sürer
    private void Resume(float s, float speed)
    {
        Follower.lateralOffset = BaseLateral;
        Follower.Place(Follower.lane, s, speed);
        Vehicle.parked = false;
        Vehicle.externalStopS = float.PositiveInfinity;
        Follower.ClearStop();
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

    private void Note(string s)
    {
        ManeuverLog.Add((director != null ? director.Elapsed : Time.time).ToString("F1") + ": " + s);
        if (ManeuverLog.Count > 60) ManeuverLog.RemoveAt(0);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }
    private static Vector3 Horizontal(Vector3 v) { v.y = 0f; return v; }

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
        // U4c: kasa zemininde görünür yuva (sedye yuva kullanmaz; kasa ortasında)
        int slot = item.GetComponent<StretcherController>() != null ? -1 : FirstFreeSlot();
        if (slot >= 0) usedSlots.Add(slot);
        item.MarkInVan(this, slot);
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
