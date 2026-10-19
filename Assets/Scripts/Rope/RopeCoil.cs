using System.Collections.Generic;
using UnityEngine;

// U2 (üçüncü test turu kararı 3): halat kangalı (SM_RopeCoil). Oyun başında çekicinin L1 dolabında asılıdır; oyuncu alır,
// iki eliyle göğsünde taşır (Carry_Coil_*), yere bırakabilir, kazığa bağlar (Rope_TieStake) ve uçuruma atar (Rope_Throw).
// Attıktan sonra kangal kaya çıkıntısında halatın dibinde küçük bir yığın olarak kalır; halat (RopeAnchor) o anda hazır olur.
//  - Elde: iki avucun ortası kangal merkezi, Grip_R → Grip_L ekseni sağ avuçtan sol avuca, kangal dik (clips_meta coil_grip).
//  - Collider ve Rigidbody yok (dekor + taşınan eşya); yere bırakılınca zemine (GroundProbe) yatay konur.
//  - Uçuş (atış) RopeAnchor tarafından sürülür (hedef halat dibi, balistik eğri).
// Sarmal: FBX kökü (model) bu objenin çocuğudur; model eksenleri Grip_L/Grip_R boşlarından ve mesh'in ince ekseninden okunur.
[DefaultExecutionOrder(150)] // Animator kemikleri yazdıktan sonra elleri izle, RopeAnchor (200) çizmeden önce
public class RopeCoil : MonoBehaviour
{
    public static readonly List<RopeCoil> All = new List<RopeCoil>();

    public enum CoilState { Locker, Held, Ground, Flying, Bottom }

    [Tooltip("Kangalın bağlı olduğu halat noktası (kazık).")]
    public RopeAnchor anchor;
    [Tooltip("Kangalın başlangıçta durduğu dolap.")]
    public TowTruckLocker locker;
    [Tooltip("FBX kökü (SM_RopeCoil).")]
    public Transform model;
    [Tooltip("Tutuş noktaları (FBX boşları Grip_L / Grip_R).")]
    public Transform gripL, gripR;
    [Tooltip("Avuç: el kemiğinden parmaklara doğru (m) (clips_meta palm_offset_hand_local_m).")]
    public float palmOffset = 0.05f;
    [Tooltip("Demet derinliğinin yarısı (m): yere yatarken merkez bu kadar yukarıda (rope_coil_meta bundle_depth_m / 2).")]
    public float halfDepth = 0.057f;
    [Tooltip("Dış yarıçap (m): dik dururken merkez bu kadar yukarıda (rope_coil_meta dimensions / 2).")]
    public float radius = 0.217f;
    [Tooltip("Alma / bırakma menzili: oyuncu kökünden kangal merkezine yatay uzaklık (m) (U1 kuralı: 1.0).")]
    public float interactRange = 1.0f;
    [Tooltip("Yüz kangala dönük: yatay ileri yön ile hedefe yön arasındaki açı ≤ bu kadar °.")]
    public float facingAngle = 30f;

    public CoilState State { get; private set; } = CoilState.Locker;
    public ThirdPersonPlayer Holder { get; private set; }
    public bool InLocker { get { return State == CoilState.Locker; } }
    public bool OnGround { get { return State == CoilState.Ground; } }
    public bool IsHeld { get { return Holder != null; } }
    // Alınabilir mi: dolapta (kapı açıkken; dolap kontrol eder) ya da yerde; halat hazır olunca kangal halatın parçasıdır
    public bool CanPickUp { get { return (State == CoilState.Locker || State == CoilState.Ground) && (anchor == null || !anchor.IsReady); } }
    // Son karede avuç–tutuş noktası hatası (m): sol / sağ (doğrulama okur)
    public float GripErrorLeft { get; private set; }
    public float GripErrorRight { get; private set; }

    // Bağlama klibinde kangal yerdeyken (coil_ground_frames) eller kangaldan ayrıdır; bu sırada verilen pozda kalır.
    private bool tiePose;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void Awake()
    {
        if (model == null && transform.childCount > 0) model = transform.GetChild(0);
        if (gripL == null || gripR == null) FindGrips();
    }

    private void FindGrips()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Grip_L") gripL = t;
            else if (t.name == "Grip_R") gripR = t;
        }
    }

    // ---- model eksenleri (dünya) ----
    public Vector3 GripMid { get { return gripL != null && gripR != null ? (gripL.position + gripR.position) * 0.5f : transform.position; } }
    // Grip_R → Grip_L: kangalın +X ekseni (clips_meta "Grip_R→Grip_L kangal +X ekseni")
    public Vector3 AxisX { get { return gripL != null && gripR != null ? (gripL.position - gripR.position).normalized : transform.right; } }
    // Disk normali: mesh sınırlarının en ince ekseni (demet derinliği)
    public Vector3 Normal
    {
        get
        {
            var mf = model != null ? model.GetComponentInChildren<MeshFilter>(true) : GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null) return transform.forward;
            Vector3 e = mf.sharedMesh.bounds.extents;
            Vector3 axis = e.x <= e.y && e.x <= e.z ? Vector3.right : e.y <= e.z ? Vector3.up : Vector3.forward;
            return mf.transform.TransformDirection(axis).normalized;
        }
    }
    public Bounds WorldBounds
    {
        get
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }

    // Kangalı verilen merkeze, +X ekseni xAxis, disk normali normal olacak şekilde koyar (düzenleyicide de çalışır).
    public void SetPose(Vector3 center, Vector3 xAxis, Vector3 normal)
    {
        if (gripL == null || gripR == null) FindGrips();
        normal = Vector3.ProjectOnPlane(normal, xAxis);
        if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(xAxis, Vector3.up);
        normal.Normalize();
        Vector3 curX = AxisX, curN = Vector3.ProjectOnPlane(Normal, curX).normalized;
        if (curN.sqrMagnitude < 1e-6f) curN = Vector3.Cross(curX, Vector3.up).normalized;
        Quaternion cur = Quaternion.LookRotation(curN, Vector3.Cross(curN, curX));
        Quaternion tgt = Quaternion.LookRotation(normal, Vector3.Cross(normal, xAxis));
        transform.rotation = tgt * Quaternion.Inverse(cur) * transform.rotation;
        transform.position += center - GripMid;
    }

    // Yatay yığın: zeminde, disk yüzü yukarı
    public void PoseFlat(Vector3 groundPoint, Vector3 groundNormal, float yawDeg)
    {
        Vector3 x = Quaternion.AngleAxis(yawDeg, groundNormal) * Vector3.ProjectOnPlane(Vector3.right, groundNormal).normalized;
        if (x.sqrMagnitude < 1e-6f) x = Vector3.ProjectOnPlane(Vector3.forward, groundNormal).normalized;
        SetPose(groundPoint + groundNormal * halfDepth, x, groundNormal);
    }

    // Dik: merkez yerden radius yukarıda, disk normali yatay (facing)
    public void PoseUpright(Vector3 groundPoint, Vector3 facing)
    {
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        facing.Normalize();
        SetPose(groundPoint + Vector3.up * radius, Vector3.Cross(Vector3.up, facing), facing);
    }

    // ---- durumlar ----
    public void PlaceInLocker(Vector3 center, Vector3 doorOutward)
    {
        State = CoilState.Locker;
        Holder = null;
        tiePose = false;
        Vector3 n = doorOutward; n.y = 0f;
        if (n.sqrMagnitude < 1e-6f) n = Vector3.forward;
        n.Normalize();
        SetPose(center, Vector3.Cross(Vector3.up, n), n);
    }

    public void PickUp(ThirdPersonPlayer by)
    {
        Holder = by;
        State = CoilState.Held;
        tiePose = false;
        FollowHands();
    }

    // U4: hırsız aldı (konumu LootItem her kare SetPose ile verir; Holder yok).
    public void TakeExternal()
    {
        Holder = null;
        State = CoilState.Held;
        tiePose = false;
    }

    // Yere bırak (zemin noktası + normali): yatay yığın; tutan serbest kalır.
    public void PutDown(Vector3 groundPoint, Vector3 groundNormal, float yawDeg)
    {
        Holder = null;
        tiePose = false;
        State = CoilState.Ground;
        PoseFlat(groundPoint, groundNormal, yawDeg);
    }

    // Elden düşer (çarpılma / yeniden doğma): bulunduğu yerin altındaki zemine yatar.
    public void Drop()
    {
        Vector3 from = IsHeld && Holder != null ? Holder.transform.position + Vector3.up * 0.5f : GripMid + Vector3.up * 0.3f;
        if (GroundProbe.Find(from, 4f, out RaycastHit g)) PutDown(g.point, g.normal, Random.Range(0f, 360f));
        else PutDown(GripMid - Vector3.up * radius, Vector3.up, 0f);
    }

    // Bağlama klibi: kangal kazığın yanında dik durur (coil_ground_frames), eller serbesttir; tutan değişmez.
    public void BeginTiePose(Vector3 groundPoint, Vector3 facing)
    {
        tiePose = true;
        PoseUpright(groundPoint, facing);
    }

    public void EndTiePose()
    {
        tiePose = false;
        if (IsHeld) FollowHands();
    }

    // Atış: elden çıkar, RopeAnchor uçuşu sürer (SetPose ile her kare).
    public void BeginFlight()
    {
        Holder = null;
        tiePose = false;
        State = CoilState.Flying;
    }

    // Dipte yığın: halat hazır.
    public void RestAtBottom(Vector3 groundPoint, Vector3 groundNormal)
    {
        Holder = null;
        tiePose = false;
        State = CoilState.Bottom;
        PoseFlat(groundPoint, groundNormal, 20f);
    }

    // Oyuncu kangala yakın ve yüzü dönük mü (U1 kuralı: ≤ interactRange, ≤ facingAngle).
    public bool CanInteract(Vector3 p, Vector3 forward)
    {
        Vector3 c = GripMid;
        Vector3 d = c - p;
        float dy = d.y;
        d.y = 0f;
        if (d.magnitude > interactRange || Mathf.Abs(dy) > 1.6f) return false;
        return RopeAnchor.FacingTarget(p, forward, c, facingAngle, 0.2f);
    }

    public Vector3 Palm(Animator a, bool left)
    {
        var hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (hand == null) return a.transform.position;
        return hand.position + hand.up * palmOffset;
    }

    // Elde: merkez iki avucun ortası, +X sağ avuçtan sol avuca, kangal dik (yukarı = taşıyanın yukarısı).
    private void FollowHands()
    {
        var a = Holder != null ? Holder.animator : null;
        if (a == null || !a.isHuman)
        {
            if (Holder != null) SetPose(Holder.transform.position + Vector3.up * 0.8f + Holder.transform.forward * 0.33f, -Holder.transform.right, Holder.transform.forward);
            return;
        }
        Vector3 l = Palm(a, true), r = Palm(a, false);
        Vector3 x = l - r;
        if (x.sqrMagnitude < 1e-6f) x = -Holder.transform.right;
        x.Normalize();
        Vector3 up = Holder.transform.up;
        Vector3 n = Vector3.Cross(x, up);
        if (n.sqrMagnitude < 1e-6f) n = Holder.transform.forward;
        SetPose((l + r) * 0.5f, x, n);
        GripErrorLeft = gripL != null ? Vector3.Distance(gripL.position, l) : 0f;
        GripErrorRight = gripR != null ? Vector3.Distance(gripR.position, r) : 0f;
    }

    private void LateUpdate()
    {
        if (State == CoilState.Held && Holder != null && !tiePose) FollowHands();
    }
}
