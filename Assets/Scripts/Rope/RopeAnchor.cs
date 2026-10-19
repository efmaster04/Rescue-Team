using System.Collections.Generic;
using UnityEngine;

// Halat noktası (D2 kullanıcı kararları 3, 4): patika kenarında kazık (SM_RopeAnchor), kaya çıkıntısının tam üstünde.
//  - Kazıkta (topStand yakınında) ya da çıkıntıda halatın dibinde E → halata bağlan.
//  - Halatta: S iner (Rope_Descend, 0.6 m/sn), W çıkar (Rope_Climb, 0.3 m/sn), beklerken Rope_Hang_Idle.
//    Tırmanış hattı, kaya duvarına klibin duvar düzlemi kadar (0.36 m) uzakta, alt ve üst nokta arasında düz bir çizgidir.
//  - Dipte (kayaya basınca) E → halattan çözülür, kayada yürür; tepeye varınca kendiliğinden patikaya (topStand) çıkar.
//  - Sırtında hasta varken kendi tırmanamaz: kazıktaki arkadaş E'yi basılı tutarak çeker (Pull; Rope_Pull,
//    halattaki Piggyback_Rope_Climb, 0.25 m/sn). Multiplayer gelene kadar test botu kazığa ışınlanıp çeker.
// Halat LineRenderer ile çizilir: RopeAttach → (çeken varsa elleri) → kenar → halattakinin üst eli → alt eli → sarkan uç.
//
// U2 (üçüncü test turu kararı 3): halat başta hazır DEĞİLDİR. Kazık (SM_RopeAnchor_Bare) boş durur, çizgi yok, bağlanılamaz.
//  Stowed → (kangal kazığa bağlanır, Rope_TieStake) Tied → (kangal atılır, Rope_Throw) Thrown → (kangal dibe iner) Ready.
//  Tied: çizgi kazıktan kangala (ellerde ya da yerde). Thrown: kazık → (kenar) → uçan kangal (balistik, throwSeconds).
//  Ready: eski akış (bağlanma / iniş / çekme). Bağlanmış ama atılmamış halat çözülemez (en sade seçenek).
[DefaultExecutionOrder(200)] // oyuncu ve bot hareket ettikten, Animator kemikleri yazdıktan sonra çizim (LateUpdate)
public class RopeAnchor : MonoBehaviour
{
    public static readonly List<RopeAnchor> All = new List<RopeAnchor>();

    public enum RopeState { Stowed, Tied, Thrown, Ready }

    [Tooltip("Halatın bağlandığı yaka (SM_RopeAnchor/RopeAttach).")]
    public Transform ropeAttach;
    [Header("U2: halat zinciri")]
    [Tooltip("Halat kangalı (dolaptan alınır, kazığa bağlanır, atılır). Boşsa halat baştan hazırdır.")]
    public RopeCoil coil;
    [Tooltip("Atış menzili: oyuncu kökünden uçurum kenarı noktasına (edgePoint) yatay uzaklık (m).")]
    public float throwRange = 1.5f;
    [Tooltip("Kangalın kazıktan kaya çıkıntısına uçuş süresi (sn).")]
    public float throwSeconds = 0.9f;
    [Tooltip("Dipteki kangal yığını halat kökünden (climbBottom) duvar boyunca bu kadar yanda (m).")]
    public float bottomCoilSide = 0.45f;
    public LineRenderer line;
    [Tooltip("Uçurum kenarındaki halat dönüm noktası (patika yüzeyinde).")]
    public Transform edgePoint;
    [Tooltip("Tırmanış hattının altı: kaya çıkıntısında, halattaki kişinin kökü (ayak).")]
    public Transform climbBottom;
    [Tooltip("Tırmanış hattının üstü: kenarın hemen altında asılı kişinin kökü.")]
    public Transform climbTop;
    [Tooltip("Patikada bağlanma / çıkış noktası (kazığın yanında), yönü patikaya doğru.")]
    public Transform topStand;
    [Tooltip("Çeken kişinin durduğu yer (kazığın arkası), yönü uçuruma.")]
    public Transform pullerStand;

    [Tooltip("Bağlanma / çekme menzili (m): topStand'e, dipte halat köküne ya da pullerStand'e yatay uzaklık (U1 kararı 1: 1.0).")]
    public float attachRange = 1.0f;
    [Tooltip("U1 kararı 1: bağlanma ve çekme yalnız yüz hedefe dönükken (yatay ileri yön ile hedefe yön arasındaki açı ≤ bu kadar °).")]
    public float facingAngle = 30f;
    [Tooltip("Hedefe bundan yakınken (m) yön koşulu aranmaz (yön belirsiz; örn. tam topStand'de).")]
    public float facingIgnoreDistance = 0.2f;
    [Tooltip("İniş hızı (m/sn, dikey; Rope_Descend vertical_speed_mps).")]
    public float descendSpeed = 0.6f;
    [Tooltip("Boşken tırmanış hızı (m/sn, dikey; Rope_Climb).")]
    public float climbSpeed = 0.3f;
    [Tooltip("Çekilme hızı (m/sn, dikey; Piggyback_Rope_Climb 0.25 = Rope_Pull halat hızı 0.255'e en yakın).")]
    public float pullSpeed = 0.25f;
    [Tooltip("Halat tutma noktası: el kemiğinden avuca (m).")]
    public float gripOffset = 0.05f;

    // Halattaki kişi ve hattaki konumu (0 dip, 1 tepe)
    public ThirdPersonPlayer Climber { get; private set; }
    public float T { get; private set; }
    public float VerticalSpeed { get; private set; }
    public bool AtBottom { get { return Climber != null && T <= 0.0001f; } }
    // Son çeken (bot ya da oyuncu) ve son çekme zamanı
    public Transform Puller { get; private set; }
    public bool BeingPulled { get { return Time.time - lastPull < 0.15f; } }
    public float PulledDistance { get; private set; }

    private float lastPull = -10f;
    private float pullThisFrame;
    private Animator pullerAnimator;

    // U2: zincir durumu
    public RopeState State { get; private set; } = RopeState.Stowed;
    public bool IsReady { get { return State == RopeState.Ready; } }
    public float ThrowStartTime { get; private set; } = -1f;
    public float FlightSeconds { get; private set; }
    private Vector3 flightFrom, flightTo, flightNormal, flightAxis;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void Awake()
    {
        if (coil == null) State = RopeState.Ready; // kangal yoksa (eski sahne) halat baştan hazır
        if (coil != null && coil.State == RopeCoil.CoilState.Bottom) State = RopeState.Ready;
    }

    // ---- U2: bağlama ve atma ----

    // Kangal elde ve halat bağlı değilken kazığa ≤ attachRange yakın, yüz kazığa dönük
    public bool CanTie(Vector3 p, Vector3 forward)
    {
        return State == RopeState.Stowed && coil != null && coil.IsHeld && Near(p, forward, transform.position);
    }

    // Rope_TieStake'in düğüm karesinde çağrılır: halat ucu kazıkta.
    public void Tie()
    {
        if (State != RopeState.Stowed) return;
        State = RopeState.Tied;
    }

    // Bağlı kangal elde, oyuncu kenara ≤ throwRange yakın ve yüzü kenara (uçuruma) dönük
    public bool CanThrow(Vector3 p, Vector3 forward)
    {
        if (State != RopeState.Tied || coil == null || !coil.IsHeld || edgePoint == null) return false;
        if (FlatDistance(p, edgePoint.position) > throwRange || Mathf.Abs(p.y - edgePoint.position.y) > 1.5f) return false;
        return Facing(p, forward, edgePoint.position);
    }

    // Dipteki kangal yığınının yeri (zemin noktası) ve normali
    public Vector3 BottomCoilSpot(out Vector3 normal)
    {
        normal = Vector3.up;
        Vector3 side = Vector3.Cross(Vector3.up, Flat(climbBottom.forward));
        Vector3 at = climbBottom.position + side * bottomCoilSide;
        if (GroundProbe.Find(at + Vector3.up * 1f, 3f, out RaycastHit g)) { at = g.point; normal = g.normal; }
        return at;
    }

    // Rope_Throw'un bırakış karesinde çağrılır: kangal ellerden çıkar, balistik eğriyle halat dibine uçar.
    public void BeginThrow()
    {
        if (State != RopeState.Tied || coil == null) return;
        State = RopeState.Thrown;
        flightFrom = coil.GripMid;
        flightTo = BottomCoilSpot(out flightNormal) + flightNormal * coil.halfDepth;
        // Yörünge iki parça: kısa bir savuruşla kenarın üstündeki tepe noktasına (kenar + 0.5 m), oradan serbest düşüşle dibe.
        // Tek parabol kısa sürede (0.9 sn) 6 m düşerken kenarın içinden geçiyordu; tepe noktası kangalı patikanın üstünde tutar.
        Vector3 valley = Flat(climbBottom.position - edgePoint.position);
        flightMid = edgePoint.position + valley * 0.25f + Vector3.up * 0.5f;
        flightAxis = coil.AxisX;
        flightPrev = flightFrom;
        ThrowStartTime = Time.time;
        FlightSeconds = Mathf.Max(0.2f, throwSeconds);
        coil.BeginFlight();
    }

    private const float FlightRiseShare = 0.35f; // sürenin bu kadarı savuruş (ele → kenar üstü), kalanı düşüş
    private Vector3 flightMid, flightPrev;

    // Uçuştaki konum (t: 0..1)
    public Vector3 FlightPoint(float u)
    {
        if (u < FlightRiseShare)
        {
            float s = u / FlightRiseShare;
            return Vector3.Lerp(flightFrom, flightMid, s) + Vector3.up * (0.25f * Mathf.Sin(Mathf.PI * s));
        }
        float f = (u - FlightRiseShare) / (1f - FlightRiseShare);
        Vector3 p = Vector3.Lerp(flightMid, flightTo, f);  // yatayda sabit hız
        p.y = Mathf.Lerp(flightMid.y, flightTo.y, f * f);   // dikeyde ivmeli düşüş (parabol)
        return p;
    }

    // Test / hızlı kurulum: zinciri atlayıp halatı hazır duruma getirir (kangal dipte yığın).
    public void ForceReady()
    {
        if (coil != null)
        {
            Vector3 at = BottomCoilSpot(out Vector3 n);
            coil.RestAtBottom(at, n);
        }
        State = RopeState.Ready;
    }

    private void StepFlight()
    {
        float t = Time.time - ThrowStartTime;
        if (t >= FlightSeconds)
        {
            coil.RestAtBottom(flightTo - flightNormal * coil.halfDepth, flightNormal);
            State = RopeState.Ready;
            return;
        }
        Vector3 pos = FlightPoint(t / FlightSeconds);
        Vector3 vel = pos - flightPrev;
        flightPrev = pos;
        Vector3 dir = vel.sqrMagnitude > 1e-8f ? vel.normalized : Vector3.down;
        // Kangal uçuş yönüne döner, kendi ekseni etrafında hafifçe yuvarlanır
        Vector3 axis = Quaternion.AngleAxis(360f * t / FlightSeconds, dir) * flightAxis;
        Vector3 normal = Vector3.Cross(axis, dir);
        if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
        coil.SetPose(pos, axis, normal);
    }

    public static bool FacingTarget(Vector3 p, Vector3 forward, Vector3 target, float maxAngle, float ignoreDistance)
    {
        Vector3 to = target - p; to.y = 0f;
        if (to.magnitude < ignoreDistance) return true;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return false;
        return Vector3.Angle(forward, to) <= maxAngle;
    }

    public float Height { get { return climbTop.position.y - climbBottom.position.y; } }
    public Vector3 ClimbPoint(float t) { return Vector3.Lerp(climbBottom.position, climbTop.position, Mathf.Clamp01(t)); }
    // Halattaki kişi kaya duvarına bakar (climbBottom'un ileri yönü)
    public Quaternion ClimbRotation { get { return climbBottom.rotation; } }

    // Patikadan aşağı mı (kaya çıkıntısı seviyesi): sırttaki hastayı bırakma bunu kullanır.
    public bool IsBelowEdge(Vector3 p) { return p.y < topStand.position.y - 2f; }

    public bool NeedsPull { get { return Climber != null && Climber.Piggyback != null && T < 1f; } }

    // Oyuncu (p, yatay ileri yön forward) hedef noktaya yakın VE yüzü dönük mü (U1 kararı 1).
    public bool Near(Vector3 p, Vector3 forward, Vector3 target)
    {
        if (FlatDistance(p, target) > attachRange || Mathf.Abs(p.y - target.y) >= 1.2f) return false;
        return Facing(p, forward, target);
    }

    public bool Facing(Vector3 p, Vector3 forward, Vector3 target) { return FacingTarget(p, forward, target, facingAngle, facingIgnoreDistance); }

    // Bağlanabilir mi: halat HAZIR (U2), boş ve oyuncu ≤ attachRange yakında, yüzü bağlanma noktasına (kazıkta topStand, dipte halat kökü) dönük
    public bool CanAttach(Vector3 p, Vector3 forward, out bool atBottom)
    {
        atBottom = false;
        if (State != RopeState.Ready || Climber != null) return false;
        if (Near(p, forward, topStand.position)) return true;
        if (Near(p, forward, climbBottom.position)) { atBottom = true; return true; }
        return false;
    }

    // Çekebilir mi: halatta sırtında hastayla biri var, oyuncu pullerStand'de (≤ attachRange) ve yüzü kazığa (halata) dönük
    public bool CanPull(Vector3 p, Vector3 forward)
    {
        if (!NeedsPull || pullerStand == null) return false;
        if (FlatDistance(p, pullerStand.position) > attachRange || Mathf.Abs(p.y - pullerStand.position.y) > 1.2f) return false;
        return Facing(p, forward, ropeAttach != null ? ropeAttach.position : pullerStand.position + pullerStand.forward);
    }

    public bool Attach(ThirdPersonPlayer p)
    {
        if (!CanAttach(p.transform.position, p.transform.forward, out bool bottom)) return false;
        Climber = p;
        T = bottom ? 0f : 0.98f;
        VerticalSpeed = 0f;
        PulledDistance = 0f;
        p.AttachRope(this);
        return true;
    }

    // Dipte çözül (kayaya bas): kişi kaya çıkıntısında serbest yürür.
    public bool CanDetachAtBottom(ThirdPersonPlayer p) { return Climber == p && AtBottom; }

    public void Detach(ThirdPersonPlayer p, Vector3 at, Quaternion rotation)
    {
        if (Climber != p) return;
        Climber = null;
        VerticalSpeed = 0f;
        p.DetachRope(at, rotation);
    }

    public void DetachAtBottom(ThirdPersonPlayer p)
    {
        if (!CanDetachAtBottom(p)) return;
        Vector3 at = climbBottom.position;
        if (GroundProbe.Find(at + Vector3.up * 1f, 3f, out RaycastHit g)) at.y = g.point.y + 0.02f;
        Detach(p, at, Quaternion.LookRotation(-Flat(climbBottom.forward), Vector3.up));
    }

    // Çeken taraf her kare çağırır (E basılı / bot). Yalnız sırtında hasta olan kişi çekilir.
    public bool Pull(Transform puller, Animator animator, float dt)
    {
        if (!NeedsPull) return false;
        Puller = puller;
        pullerAnimator = animator;
        lastPull = Time.time;
        pullThisFrame += pullSpeed * dt;
        return true;
    }

    // Halattaki oyuncunun hareketi (ThirdPersonPlayer.Update çağırır). input.y > 0: W, < 0: S.
    public Vector3 Step(ThirdPersonPlayer p, Vector2 input, bool hasControl, float dt)
    {
        float h = Mathf.Max(0.01f, Height);
        float dy = 0f;
        if (pullThisFrame > 0f) dy = pullThisFrame;                          // çekiliyor
        else if (hasControl && input.y < -0.5f) dy = -descendSpeed * dt;     // S: iniş (yükle de)
        else if (hasControl && input.y > 0.5f && p.Piggyback == null) dy = climbSpeed * dt; // W: yalnız boşken
        pullThisFrame = 0f;
        float before = T;
        T = Mathf.Clamp01(T + dy / h);
        VerticalSpeed = (T - before) * h / Mathf.Max(dt, 1e-5f);
        if (dy > 0f && BeingPulled) PulledDistance += (T - before) * h;
        if (T >= 1f && dy > 0f)
        {
            // Tepeye vardı: patikaya çık (topStand), halattan kendiliğinden çözül.
            Vector3 at = topStand.position;
            if (GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit g)) at.y = g.point.y + 0.02f;
            Detach(p, at, topStand.rotation);
            return at;
        }
        return ClimbPoint(T);
    }

    private void LateUpdate()
    {
        if (State == RopeState.Thrown && coil != null) StepFlight();
        if (line == null || ropeAttach == null) return;
        // U2: zincir tamamlanmadan halat çizilmez / kısa çizilir
        if (State == RopeState.Stowed || coil == null && State != RopeState.Ready)
        {
            line.enabled = false;
            return;
        }
        if (State == RopeState.Tied || State == RopeState.Thrown)
        {
            line.enabled = true;
            var tied = new List<Vector3> { ropeAttach.position };
            Vector3 c = coil.GripMid;
            if (State == RopeState.Thrown && edgePoint != null && c.y < edgePoint.position.y) tied.Add(edgePoint.position); // kenardan kırılır
            tied.Add(c);
            line.positionCount = tied.Count;
            line.SetPositions(tied.ToArray());
            return;
        }
        line.enabled = true;
        var pts = new List<Vector3> { ropeAttach.position };
        if (BeingPulled && pullerAnimator != null)
            AddHands(pts, pullerAnimator, true);
        pts.Add(edgePoint.position);
        if (Climber != null && Climber.animator != null)
        {
            AddHands(pts, Climber.animator, false);
            Vector3 low = pts[pts.Count - 1];
            pts.Add(new Vector3(low.x, Mathf.Min(low.y - 0.8f, climbBottom.position.y + 0.05f), low.z));
        }
        else
            pts.Add(climbBottom.position + Vector3.up * 0.05f); // boş halat dibe sarkar
        line.positionCount = pts.Count;
        line.SetPositions(pts.ToArray());
    }

    // İki elin avucu: halat çizgisinde üstteki önce (halattaki kişi) ya da kazığa yakın olan önce (çeken).
    private void AddHands(List<Vector3> pts, Animator a, bool puller)
    {
        Vector3 l = Grip(a, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm);
        Vector3 r = Grip(a, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm);
        bool lFirst = puller ? (l - ropeAttach.position).sqrMagnitude < (r - ropeAttach.position).sqrMagnitude : l.y > r.y;
        if (puller) { pts.Add(lFirst ? l : r); pts.Add(lFirst ? r : l); }
        else { pts.Add(lFirst ? l : r); pts.Add(lFirst ? r : l); }
    }

    public Vector3 Grip(Animator a, HumanBodyBones hand, HumanBodyBones forearm)
    {
        var h = a.GetBoneTransform(hand);
        var f = a.GetBoneTransform(forearm);
        if (h == null) return a.transform.position;
        Vector3 dir = f != null ? (h.position - f.position).normalized : Vector3.zero;
        return h.position + dir * gripOffset;
    }

    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
}
