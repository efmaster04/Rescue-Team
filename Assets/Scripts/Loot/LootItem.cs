using System;
using System.Collections.Generic;
using UnityEngine;

// U3: değer taşıyan sahne eşyası. Üç tür:
//  - Luggage (valiz/çanta prop'u): tek elle taşınır (Carry_OneHand_Walk, koni kuralı), güvenli yere konunca valiz + içindekiler
//    tek tek para yazar.
//  - PatientItem (hastanın değerli eşyası, koyu kutu yer tutucu): hastanın kökünde, karnında durur; hasta yerde ya da sedyedeyken
//    alınır. Hasta eşyasıyla ambulansa binerse eşya kendiliğinden güvende sayılır.
//  - Equipment (sedye, halat kangalı, koni; sağlık çantası sahnede yoksa yok): taşınmaz, para kazandırmaz; çalınırsa − yazılır.
// Çalınma ve güvenlik: LootRegistry.ReportStolen / Secure. Güvenli yere konmuş eşya çalınamaz (IsSecured).
// U4: hırsız taşır (ThiefHolder; göğsünde, sedyeyi arkasında sürükler), kamyonete atınca ReportStolen → eşya kaybolur (ekipman dahil).
//     Oyuncu tek elle taşıdığı eşyayı fırlatabilir (Throw: geçici Rigidbody ile uçar, hırsıza isabet sersemletir, yere düşer;
//     FallRespawnPlane altına düşerse başlangıç yerine döner, kayıp sayılmaz).
public enum LootKind { Luggage, PatientItem, Equipment }

[DefaultExecutionOrder(60)] // taşıyıcı hareket ettikten sonra elde konumlanır (TrafficCone ile aynı)
public class LootItem : MonoBehaviour
{
    public static readonly List<LootItem> All = new List<LootItem>();

    // U4b: InVan = hırsız kamyonete attı, kamyonet henüz kalkmadı (açık bagajdan geri alınabilir; kalkınca Stolen)
    public enum LootState { AtStart, Held, Dropped, Secured, Stolen, Flying, InVan }

    [Tooltip("Ekranda görünen ad (\"Valiz\", \"Kamera\", \"Sedye\").")]
    public string displayName = "Eşya";
    [Tooltip("Eşyanın kendi değeri (₺).")]
    public int value;
    public LootKind kind = LootKind.Luggage;
    [Tooltip("Valiz içindekiler (ad + değer); güvenli yere konunca tek tek yazılır.")]
    public List<LootValues.Entry> contents = new List<LootValues.Entry>();
    [Tooltip("Taşınabilir mi (valiz, hasta eşyası).")]
    public bool carryable = true;
    [Tooltip("Hasta eşyasıysa sahibi.")]
    public Patient patient;
    [Tooltip("Alma menzili: oyuncu kökünden eşya merkezine yatay uzaklık (m).")]
    public float interactRange = 1.5f;

    public LootState State { get; private set; } = LootState.AtStart;
    public ThirdPersonPlayer Holder { get; private set; }
    // U4: taşıyan hırsız (varsa). Eşya onun göğsünde (sedye arkasında sürüklenir).
    public Thief ThiefHolder { get; private set; }
    // U4c: içinde durduğu kamyonet ve kasa yuvası (InVan iken; eşya kasa zemininde görünür durur, kamyonetle birlikte hareket eder)
    public ThiefVan Van { get; private set; }
    public int VanSlot { get; private set; } = -1;
    public bool IsHeld { get { return Holder != null; } }
    public bool IsHeldByThief { get { return ThiefHolder != null; } }
    public bool IsSecured { get { return State == LootState.Secured; } }
    public bool IsStolen { get { return State == LootState.Stolen; } }
    public bool IsFlying { get { return State == LootState.Flying; } }
    public bool IsInVan { get { return State == LootState.InVan; } }
    // U4 ölçüm: fırlatılan eşyanın isabet ettiği hırsız sayısı, uçuş sonrası yere inme / başa dönme
    public int ThrowHits { get; private set; }
    public int Throws { get; private set; }
    public int FallReturns { get; private set; }
    public float ThrowHitStun { get; private set; }
    public float ThrowHitDamage { get; private set; }
    public int VanRetrievals { get; private set; }
    // Toplam değer: kendi + içindekiler (çalınan çubuğu ve güvenli yer bunu sayar)
    public int TotalValue
    {
        get
        {
            int t = value;
            foreach (var c in contents) if (c != null) t += c.value;
            return t;
        }
    }
    // Alınabilir mi: taşınabilir, güvende/çalınmış değil, elde değil; hasta eşyası yalnız hasta yerde ya da sedyedeyken
    public bool CanPickUp
    {
        get
        {
            if (!carryable || State == LootState.Secured || State == LootState.Stolen || State == LootState.Flying || State == LootState.InVan || Holder != null || ThiefHolder != null || !gameObject.activeInHierarchy) return false;
            if (patient != null && State == LootState.AtStart)
                return patient.State == PatientState.LyingGround || patient.State == PatientState.OnStretcher;
            return true;
        }
    }

    // U4: hırsızın başının üstündeki yazı: "Laptop'lu valiz 320 ₺" (en değerli içerik + ünlü uyumlu ek), "Kamera 150 ₺", "Sedye 300 ₺".
    public string LabelText
    {
        get
        {
            string name = displayName;
            if (kind == LootKind.Luggage && contents.Count > 0)
            {
                LootValues.Entry best = null;
                foreach (var c in contents) if (c != null && (best == null || c.value > best.value)) best = c;
                if (best != null) name = best.name + Suffix(best.name) + " " + displayName.ToLowerInvariant();
            }
            return name + " " + TotalValue.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + " " + LootRegistry.MoneySymbol;
        }
    }

    // Türkçe "-lı/-li/-lu/-lü" eki: son ünlüye göre (Laptop → 'lu, Saat → 'lı, Kolye → 'li, Parfüm → 'lü).
    private static string Suffix(string word)
    {
        for (int i = word.Length - 1; i >= 0; i--)
        {
            char c = char.ToLowerInvariant(word[i]);
            if ("aı".IndexOf(c) >= 0) return "'lı";
            if ("ei".IndexOf(c) >= 0) return "'li";
            if ("ou".IndexOf(c) >= 0) return "'lu";
            if ("öü".IndexOf(c) >= 0) return "'lü";
        }
        return "'li";
    }

    private Collider[] colliders;
    private Transform startParent;
    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;
    private Quaternion startRotation;
    private float bottomOffset;
    private bool startKnown;
    // U4: uçuş
    private Rigidbody flightBody;
    private Collider tempCollider;
    private float flightStart = -1f, slowSince = -1f, killBelowY = float.NegativeInfinity;
    private readonly HashSet<Thief> hitThieves = new HashSet<Thief>();
    // U4: ekipman taşıma yardımcıları
    private StretcherController stretcher;
    private TrafficCone cone;
    private RopeCoil coil;
    private float dragWobble;

    private void Awake()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        stretcher = GetComponent<StretcherController>();
        cone = GetComponent<TrafficCone>();
        coil = GetComponent<RopeCoil>();
        RememberStart();
    }

    private void RememberStart()
    {
        if (startKnown) return;
        startKnown = true;
        startParent = transform.parent;
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
        startRotation = transform.rotation;
        // Pivotun tabandan yüksekliği: valiz dik durur (dünya sınırlarından); hasta kutusu yatan hastaya bağlı (yerel sınırlardan)
        var r = GetComponentInChildren<Renderer>(true);
        if (kind == LootKind.PatientItem || r == null) bottomOffset = r != null ? r.localBounds.extents.y * transform.lossyScale.y : 0.03f;
        else bottomOffset = Mathf.Max(0f, transform.position.y - WorldBounds.min.y);
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (patient != null) patient.StateChanged += OnPatientState;
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (patient != null) patient.StateChanged -= OnPatientState;
    }

    // Hasta eşyasıyla ambulansa bindi: eşya güvende (para yazılır)
    private void OnPatientState(Patient p)
    {
        if (State != LootState.AtStart || Holder != null || ThiefHolder != null) return;
        if (p.State == PatientState.InAmbulance && LootRegistry.Instance != null) LootRegistry.Instance.Secure(this);
    }

    public Bounds WorldBounds
    {
        get
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
    // Eşyanın merkezi (koni: koninin kendisi; kangal: tutuş ortası)
    public Vector3 Center
    {
        get
        {
            if (cone != null && cone.target != null) return cone.Center;
            if (coil != null) return coil.GripMid;
            return WorldBounds.center;
        }
    }

    // Oyuncu yakın ve yüzü dönük mü (U3 yüz kuralı: PlayerInteraction'ın açısıyla)
    public bool InRange(Vector3 p)
    {
        Vector3 d = Center - p;
        float dy = d.y;
        d.y = 0f;
        return d.magnitude <= interactRange && Mathf.Abs(dy) <= 1.8f;
    }

    // ---- taşıma (yalnız PlayerInteraction ve ThirdPersonPlayer çağırır) ----
    public void PickUp(ThirdPersonPlayer by)
    {
        Holder = by;
        ThiefHolder = null;
        State = LootState.Held;
        transform.SetParent(null, true);
        foreach (var c in colliders) if (c != null) c.enabled = false;
        FollowHolder();
    }

    // Verilen zemine bırakılır (dik; yatay dönüşü yaw).
    public void Drop(Vector3 groundPoint, float yaw)
    {
        Holder = null;
        ThiefHolder = null;
        State = LootState.Dropped;
        Quaternion rot = kind == LootKind.PatientItem ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.AngleAxis(yaw - startRotation.eulerAngles.y, Vector3.up) * startRotation;
        transform.SetPositionAndRotation(groundPoint + Vector3.up * (bottomOffset + 0.005f), rot);
        foreach (var c in colliders) if (c != null) c.enabled = true;
        Physics.SyncTransforms();
    }

    // Elden düşer (çarpılma / can 0 / yeniden doğma): taşıyanın önündeki zemine.
    public void DropNear(Vector3 from, Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 at = from + forward * 0.6f;
        if (!GroundProbe.Find(at + Vector3.up * 1.5f, 5f, out RaycastHit g)) { at = from; if (!GroundProbe.Find(at + Vector3.up * 1.5f, 5f, out g)) g.point = from; }
        Drop(g.point, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
    }

    // Güvenli yere kondu: kaybolur (kasaya/ambulansa yerleşmiş sayılır).
    public void Secure()
    {
        Holder = null;
        ThiefHolder = null;
        Van = null; VanSlot = -1;
        State = LootState.Secured;
        gameObject.SetActive(false);
    }

    // Çalındı (U4: kamyonet kaçtı): eşya kaybolur, ekipman dahil (koni: yönetilen koni parçası da gizlenir).
    public void MarkStolen()
    {
        Holder = null;
        ThiefHolder = null;
        Van = null; VanSlot = -1;
        State = LootState.Stolen;
        if (cone != null && cone.target != null) { cone.SetHeldExternal(false); cone.target.gameObject.SetActive(false); }
        gameObject.SetActive(false);
    }

    // U4b: kamyonete atıldı, kamyonet daha kalkmadı: "kamyonette" (açık bagajdan geri alınabilir).
    // U4c (kullanıcı: "alınan eşyaları göremiyoruz"): eşya gizlenmez; kasa zemininde verilen yuvada görünür durur (collider kapalı,
    // alınamaz / çalınamaz), kamyonet kaçınca kapalı kapakların ardında gider, kesinleşince MarkStolen gizler.
    public void MarkInVan(ThiefVan van, int slot)
    {
        if (Holder != null && Holder.HeldValuable == this) Holder.HeldValuable = null;
        Holder = null;
        ThiefHolder = null;
        Van = van;
        VanSlot = slot;
        State = LootState.InVan;
        if (stretcher == null && coil == null && cone == null) transform.SetParent(null, true);
        if (cone != null) cone.SetHeldExternal(true);
        if (coil != null) coil.TakeExternal();
        foreach (var c in colliders) if (c != null) c.enabled = false;
        gameObject.SetActive(true);
        FollowVan();
    }

    // U4c: kasa yuvasında dur (kamyonet hareket edince de). Sedye kasa ortasında boylu boyunca; koni dik; kangal yatık.
    private void FollowVan()
    {
        if (Van == null || !Van.CargoPose(this, out Vector3 floorPoint, out Vector3 fwd)) return;
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (stretcher != null)
        {
            stretcher.Teleport(floorPoint + Vector3.up * 0.02f, Quaternion.LookRotation(fwd, Vector3.up));
            return;
        }
        if (cone != null && cone.target != null)
        {
            Quaternion crot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.FromToRotation(cone.localUp.normalized, Vector3.up);
            cone.target.SetPositionAndRotation(floorPoint + Vector3.up * cone.baseOffset, crot);
            return;
        }
        if (coil != null)
        {
            coil.PoseFlat(floorPoint, Vector3.up, yaw);
            return;
        }
        Quaternion rot = kind == LootKind.PatientItem ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.AngleAxis(yaw - startRotation.eulerAngles.y, Vector3.up) * startRotation;
        transform.SetPositionAndRotation(floorPoint + Vector3.up * (bottomOffset + 0.005f), rot);
    }

    // U4b: bagajdan geri alındı. Tek elle taşınan (valiz / hasta eşyası) oyuncunun eli boşsa eline; ekipman ya da el doluysa
    // verilen zemin noktasına (bagajın arkası) konur. Tekrar normal eşya (çalınabilir, güvenli yere konabilir).
    public void RetrieveFromVan(ThirdPersonPlayer player, Vector3 groundPoint, Vector3 facing)
    {
        if (State != LootState.InVan) return;
        VanRetrievals++;
        Van = null; VanSlot = -1;
        gameObject.SetActive(true);
        State = LootState.Dropped;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        facing.Normalize();
        float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        if (stretcher != null)
        {
            foreach (var c in colliders) if (c != null) c.enabled = true;
            stretcher.Teleport(groundPoint + Vector3.up * 0.02f, Quaternion.LookRotation(Vector3.Cross(Vector3.up, facing), Vector3.up));
            return;
        }
        if (cone != null)
        {
            foreach (var c in colliders) if (c != null) c.enabled = true;
            if (cone.target != null) cone.target.gameObject.SetActive(true);
            cone.SetHeldExternal(false);
            cone.PlaceAt(groundPoint, yaw);
            return;
        }
        if (coil != null)
        {
            foreach (var c in colliders) if (c != null) c.enabled = true;
            coil.PutDown(groundPoint, Vector3.up, yaw);
            return;
        }
        bool handsFree = player != null && player.HeldValuable == null && player.HeldCone == null && player.HeldCoil == null && player.HeldStretcher == null && player.Piggyback == null && player.Rope == null;
        if (carryable && handsFree)
        {
            player.HeldValuable = this;
            PickUp(player);
            return;
        }
        Drop(groundPoint, yaw);
    }

    // Test: başlangıç durumuna döner.
    public void ResetToStart()
    {
        EndFlight(false);
        Holder = null;
        ThiefHolder = null;
        Van = null; VanSlot = -1;
        State = LootState.AtStart;
        gameObject.SetActive(true);
        if (cone != null && cone.target != null) { cone.target.gameObject.SetActive(true); cone.SetHeldExternal(false); }
        transform.SetParent(startParent, false);
        transform.SetLocalPositionAndRotation(startLocalPosition, startLocalRotation);
        foreach (var c in colliders) if (c != null) c.enabled = true;
        Physics.SyncTransforms();
    }

    // Elde: tek elle, taşıyanın sağ önünde bel hizasında (koni gibi; Carry_OneHand_Walk).
    private void FollowHolder()
    {
        if (Holder == null) return;
        Transform r = Holder.transform;
        Vector3 fwd = r.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Quaternion rot = kind == LootKind.PatientItem ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.AngleAxis(yaw - startRotation.eulerAngles.y, Vector3.up) * startRotation;
        // U4: nişan alırken (Throw_Aim) eşya sağ elde yukarı-geride (hold_grip RightHand avuç +5 cm)
        var combat = Holder.GetComponent<PlayerCombat>();
        if (combat != null && combat.Aiming && Holder.animator != null && Holder.animator.isHuman)
        {
            var hand = Holder.animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null)
            {
                transform.SetPositionAndRotation(hand.position + hand.up * 0.05f + hand.forward * 0.06f, rot);
                return;
            }
        }
        Vector3 basePos = r.position + fwd * 0.38f + right * 0.28f + Vector3.up * 0.42f;
        transform.SetPositionAndRotation(basePos, rot);
    }

    // ---- U4: hırsız taşıması ----
    public void PickUpByThief(Thief by)
    {
        // Oyuncunun elinden kapıldıysa oyuncunun kaydı temizlenir
        if (Holder != null && Holder.HeldValuable == this) Holder.HeldValuable = null;
        Holder = null;
        ThiefHolder = by;
        State = LootState.Held;
        if (stretcher == null && coil == null && cone == null) transform.SetParent(null, true);
        if (cone != null) cone.SetHeldExternal(true);
        if (coil != null) coil.TakeExternal();
        foreach (var c in colliders) if (c != null) c.enabled = false;
        dragWobble = 0f;
        FollowThief();
    }

    // Hırsız sersemleyince / kaçarken eşya yere düşer, tekrar alınabilir (ekipman yerinde kalır).
    public void DropByThief(Vector3 from, Vector3 forward)
    {
        ThiefHolder = null;
        if (stretcher != null)
        {
            State = LootState.Dropped;
            foreach (var c in colliders) if (c != null) c.enabled = true;
            Vector3 p = stretcher.transform.position;
            if (GroundProbe.Find(p + Vector3.up * 1f, 4f, out RaycastHit g)) stretcher.Teleport(g.point + Vector3.up * 0.02f, Quaternion.LookRotation(Flat(stretcher.transform.forward, forward), Vector3.up));
            return;
        }
        if (cone != null)
        {
            State = LootState.Dropped;
            foreach (var c in colliders) if (c != null) c.enabled = true;
            cone.SetHeldExternal(false);
            cone.Drop(Vector3.zero);
            return;
        }
        if (coil != null)
        {
            State = LootState.Dropped;
            foreach (var c in colliders) if (c != null) c.enabled = true;
            coil.Drop();
            return;
        }
        DropNear(from, forward);
    }

    // İki elle önde (loot_grip: eşyanın MERKEZİ iki avucun ortasında; Thief.HandsMid, animasyon sonrası LateUpdate'te kemiklerden);
    // sedye arkada sürüklenir; koni/kangal da ellerde. U4b: önceden pivot (valizin tabanı) göğüs hizasına konuyordu, valiz
    // kafanın arkasında görünüyordu (u4_kapma.png); şimdi merkez avuçlara, gövdeden en az 0.25 m önde.
    private void FollowThief()
    {
        if (ThiefHolder == null) return;
        Transform r = ThiefHolder.transform;
        Vector3 fwd = r.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (stretcher != null)
        {
            // Komik sürükleme: sedye hırsızın 1.3 m arkasında, yürüyüşle sağa sola yalpalar
            dragWobble += Time.deltaTime * 6f;
            Vector3 behind = r.position - fwd * 1.3f + Vector3.Cross(Vector3.up, fwd) * Mathf.Sin(dragWobble) * 0.15f;
            if (GroundProbe.Find(behind + Vector3.up * 1.2f, 3f, out RaycastHit g)) behind.y = g.point.y + 0.02f;
            stretcher.Teleport(behind, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 8f * Mathf.Sin(dragWobble), 0f));
            return;
        }
        // Avuçların ortası; kök uzayında en az 0.25 m önde ve 0.55–1.1 m yükseklikte (gövdeye / kafaya girmesin)
        ThiefHolder.HandsMid(out Vector3 grip, out _);
        Vector3 local = r.InverseTransformPoint(grip);
        local.z = Mathf.Max(local.z, 0.25f);
        local.y = Mathf.Clamp(local.y, 0.55f, 1.1f);
        local.x = Mathf.Clamp(local.x, -0.15f, 0.15f);
        Vector3 center = r.TransformPoint(local);
        if (cone != null && cone.target != null)
        {
            Quaternion crot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.FromToRotation(cone.localUp.normalized, Vector3.up);
            cone.target.SetPositionAndRotation(center + Vector3.up * (cone.baseOffset - cone.height * 0.4f), crot);
            return;
        }
        if (coil != null)
        {
            coil.SetPose(center, -r.right, fwd);
            return;
        }
        Quaternion rot = kind == LootKind.PatientItem ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.AngleAxis(yaw - startRotation.eulerAngles.y, Vector3.up) * startRotation;
        transform.rotation = rot;
        // Pivot → merkez farkı (valizde pivot tabanda): merkez avuçlara gelsin
        Vector3 pivotToCenter = WorldBounds.center - transform.position;
        transform.position = center - pivotToCenter;
    }

    // ---- U4: fırlatma ----
    // velocity: başlangıç hızı (m/sn); hitStun: isabet eden hırsız bu kadar sn sersemler (U4b: hitDamage kadar can kaybeder);
    // killBelow: bu yüksekliğin altı boşluk (başa döner).
    public void Throw(Vector3 velocity, float hitStun, float killBelow) { Throw(velocity, hitStun, 0f, killBelow); }
    public void Throw(Vector3 velocity, float hitStun, float hitDamage, float killBelow)
    {
        if (Holder != null && Holder.HeldValuable == this) Holder.HeldValuable = null;
        Holder = null;
        ThiefHolder = null;
        State = LootState.Flying;
        Throws++;
        ThrowHitStun = hitStun;
        ThrowHitDamage = hitDamage;
        killBelowY = killBelow;
        hitThieves.Clear();
        transform.SetParent(null, true);
        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (colliders.Length == 0 || Array.TrueForAll(colliders, c => c == null))
        {
            // Hasta eşyası kutusunun collider'ı yok: uçuş için geçici kutu
            var r = GetComponentInChildren<Renderer>(true);
            var bc = gameObject.AddComponent<BoxCollider>();
            if (r != null)
            {
                bc.center = transform.InverseTransformPoint(r.bounds.center);
                Vector3 s = r.bounds.size;
                bc.size = new Vector3(s.x / Mathf.Max(0.01f, transform.lossyScale.x), s.y / Mathf.Max(0.01f, transform.lossyScale.y), s.z / Mathf.Max(0.01f, transform.lossyScale.z));
            }
            tempCollider = bc;
        }
        flightBody = GetComponent<Rigidbody>();
        if (flightBody == null) flightBody = gameObject.AddComponent<Rigidbody>();
        flightBody.mass = 3f;
        flightBody.isKinematic = false;
        flightBody.useGravity = true;
        flightBody.interpolation = RigidbodyInterpolation.Interpolate;
        flightBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        flightBody.linearVelocity = velocity;
        flightBody.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
        flightStart = Time.time;
        slowSince = -1f;
    }

    private void FixedUpdate()
    {
        if (State != LootState.Flying || flightBody == null) return;
        // İsabet: merkeze 0.7 m içindeki hırsızlar (bir kez)
        Vector3 c = WorldBounds.center;
        foreach (var t in Thief.All)
        {
            if (t == null || hitThieves.Contains(t) || !t.CanBeHit) continue;
            if (Vector3.Distance(t.transform.position + Vector3.up * 0.8f, c) <= 0.7f)
            {
                hitThieves.Add(t);
                ThrowHits++;
                t.Hit(ThrowHitDamage, ThrowHitStun, Vector3.zero, "firlatma");
            }
        }
        if (transform.position.y < killBelowY) { EndFlight(true); return; }
        bool slow = flightBody.linearVelocity.magnitude < 0.2f && Time.time - flightStart > 0.4f;
        if (slow && slowSince < 0f) slowSince = Time.time;
        if (!slow) slowSince = -1f;
        if ((slowSince >= 0f && Time.time - slowSince > 0.4f) || Time.time - flightStart > 8f) EndFlight(false);
    }

    // Uçuş biter: yere indiyse Dropped (yattığı gibi kalır); boşluğa düştüyse başlangıç yerine döner (kayıp sayılmaz).
    private void EndFlight(bool fell)
    {
        if (flightBody != null) { Destroy(flightBody); flightBody = null; }
        if (tempCollider != null) { Destroy(tempCollider); tempCollider = null; }
        if (State != LootState.Flying) return;
        if (fell)
        {
            FallReturns++;
            State = LootState.AtStart;
            transform.SetParent(startParent, false);
            transform.SetLocalPositionAndRotation(startLocalPosition, startLocalRotation);
            foreach (var cl in colliders) if (cl != null) cl.enabled = true;
            Physics.SyncTransforms();
            return;
        }
        State = LootState.Dropped;
    }

    private static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
    }

    private void LateUpdate()
    {
        if (Holder != null) FollowHolder();
        else if (ThiefHolder != null) FollowThief();
        else if (State == LootState.InVan && Van != null) FollowVan();
    }
}
