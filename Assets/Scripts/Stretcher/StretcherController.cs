using System.Collections.Generic;
using UnityEngine;

// Sedye (kullanıcı kararları 8, 10, 11). Kinematik: konumu fizikle değil, taşıyıcıların el noktalarıyla belirlenir.
//
// Durumlar:
//  Resting  — yerde (ya da çekici kasasında) duruyor. Tek taşıyıcı tutsa bile kalkmaz.
//  Raised   — yeterli taşıyıcı var; iki sap, o uçtaki taşıyıcıların el noktalarında.
//  Tilted   — kalkıkken bir uç bırakıldı, öbürü hâlâ tutuluyor: bırakılan uç yerde, tutulan uç elde.
//
// Düşme kuralı: Kalkık sedyede biri bırakınca safeReleaseWindow (0.5 sn) beklenir. Bu sürede herkes bırakırsa
// sedye güvenle yere konur; bırakmazsa bırakılan uç yere iner ve hasta ragdoll olup kayarak düşer.
[DefaultExecutionOrder(100)] // taşıyıcılar (oyuncu Update, bot LateUpdate 50) hareket ettikten sonra
public class StretcherController : MonoBehaviour
{
    public enum Mode { Resting, Raised, Tilted }

    public static readonly List<StretcherController> All = new List<StretcherController>();

    public CarryRules rules;
    [Tooltip("Sap noktaları (modeldeki Handle_Front / Handle_Rear boş objeleri).")]
    public Transform handleFront, handleRear;
    [Tooltip("Hastanın yatırıldığı çerçeve (PatientAnchor'ın altında; +ileri = baş yönü, +yukarı = yüz).")]
    public Transform patientAnchor;
    [Tooltip("Sedye yerdeyken açık olan collider'lar (taşınırken kapanır).")]
    public Collider[] colliders = new Collider[0];
    [Tooltip("Kalkma / yere inme geçişi (sn).")]
    public float liftTime = 0.3f;
    [Tooltip("Düşen hastaya verilen küçük yanal itki (m/sn).")]
    public float fallSideSpeed = 1.2f;
    [Tooltip("E3 kararı 1: sedyedeki hasta çerçeveden ayak tarafına bu kadar kaydırılır (m); başı öndeki taşıyıcının sırtına girmez. Çerçeve, saplar ve el IK'sı değişmez.")]
    [Min(0f)] public float passengerTowardFeet = 0.18f;

    public Mode CurrentMode { get; private set; } = Mode.Resting;
    public bool IsRaised { get { return CurrentMode == Mode.Raised; } }
    public Patient Passenger { get; private set; }
    public int CarrierCount { get { return grips.Count; } }
    public Vector3 Velocity { get; private set; }
    public float HandleSpan { get { return Vector3.Distance(handleFront.position, handleRear.position); } }
    public bool DropPending { get { return pendingSince >= 0f; } }

    private struct Grip
    {
        public ICarrier carrier;
        public bool front;
    }

    private readonly List<Grip> grips = new List<Grip>();
    private float pendingSince = -1f;   // bir uç bırakıldı; güvenli pencere başladı (Time.time)
    private bool pendingFront;          // bırakılan uç ön mü
    private bool tiltedGroundFront;     // Tilted'da yerdeki uç ön mü
    private float blendT = 1f;          // kalkma / inme geçişi (0→1)
    private Vector3 blendFromFront, blendFromRear;
    private bool lowering;              // güvenli iniş animasyonu sürüyor
    private Vector3 lowerFront, lowerRear;
    private Vector3 lastPosition;

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        lastPosition = transform.position;
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    public bool IsHeldBy(ICarrier c)
    {
        foreach (var g in grips) if (g.carrier == c) return true;
        return false;
    }

    public bool HoldsFront(ICarrier carrier)
    {
        foreach (var grip in grips) if (grip.carrier == carrier) return grip.front;
        return false;
    }

    public Transform HandleFor(ICarrier carrier) => HoldsFront(carrier) ? handleFront : handleRear;

    public Vector3 GripPoint(ICarrier carrier, bool left)
    {
        Vector3 forward = (handleFront.position - handleRear.position).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return HandleFor(carrier).position + right * (left ? -0.293f : 0.293f);
    }

    public int CountAtEnd(bool front)
    {
        int n = 0;
        foreach (var g in grips) if (g.front == front) n++;
        return n;
    }

    // Bir uçta en fazla kaç kişi: gereken sayının yarısı (yukarı yuvarlanır), en az 1.
    public int MaxPerEnd { get { return Mathf.Max(1, Mathf.CeilToInt(rules.requiredCarriers / 2f)); } }

    public bool CanGrab(bool front)
    {
        return CountAtEnd(front) < MaxPerEnd;
    }

    // Taşıyıcı bir ucu tutar. notify: yetersizse "İki kişi gerekli" uyarısı hemen gösterilsin mi.
    public bool Grab(ICarrier carrier, bool front, bool notify = true)
    {
        if (carrier.HeldStretcher != null || IsHeldBy(carrier) || !CanGrab(front)) return false;
        grips.Add(new Grip { carrier = carrier, front = front });
        carrier.HeldStretcher = this;
        if (grips.Count >= rules.requiredCarriers)
            Raise();
        else if (notify)
            NotifyIfUnderstaffed();
        return true;
    }

    public void NotifyIfUnderstaffed()
    {
        if (grips.Count > 0 && grips.Count < rules.requiredCarriers && CurrentMode == Mode.Resting)
            GameNotice.Show(rules.NotEnoughCarriersMessage);
    }

    public void Release(ICarrier carrier)
    {
        int idx = grips.FindIndex(g => g.carrier == carrier);
        if (idx < 0) return;
        bool front = grips[idx].front;
        grips.RemoveAt(idx);
        carrier.HeldStretcher = null;

        if (grips.Count == 0)
        {
            // Herkes bıraktı (kalkıksa güvenli pencere içinde): sedye güvenle yere konur, hasta düşmez.
            pendingSince = -1f;
            if (CurrentMode != Mode.Resting) LowerToGround();
            return;
        }
        if (CurrentMode == Mode.Raised && grips.Count < rules.requiredCarriers && pendingSince < 0f)
        {
            pendingSince = Time.time;
            pendingFront = CountAtEnd(front) == 0 ? front : !front;
        }
    }

    // Yerdeki sedyeye hasta yatırma (kullanıcı kararı 10).
    public bool CanAcceptPatient(Patient p, Vector3 helperPosition, float stretcherRange, float playerRange)
    {
        if (p == null || !p.CanBePickedUp || Passenger != null) return false;
        if (CurrentMode != Mode.Resting || grips.Count > 0 || lowering) return false;
        if (Vector3.Distance(patientAnchor.position, p.Position) > stretcherRange) return false;
        return Vector3.Distance(helperPosition, p.Position) <= playerRange;
    }

    public void PlacePatient(Patient p)
    {
        Passenger = p;
        // Çerçevenin +ileri ekseni baş yönüdür; hasta ayak tarafına (geriye) kaydırılır.
        p.LieOn(patientAnchor, PatientState.OnStretcher, 0f, Vector3.back * passengerTowardFeet);
    }

    // Ambulansa yükleme: hasta sedyeden alınır (sedye taşıyıcılarda kalır).
    public Patient TakePatient()
    {
        var p = Passenger;
        Passenger = null;
        return p;
    }

    // Test ve editör: sedyeyi bir yere ışınlar (kimse tutmuyorken).
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        lowering = false;
        transform.SetPositionAndRotation(position, rotation);
        lastPosition = position;
        Physics.SyncTransforms();
    }

    private void Raise()
    {
        pendingSince = -1f;
        lowering = false;
        CurrentMode = Mode.Raised;
        blendT = 0f;
        blendFromFront = handleFront.position;
        blendFromRear = handleRear.position;
        SetColliders(false);
    }

    private void LowerToGround()
    {
        CurrentMode = Mode.Resting;
        pendingSince = -1f;
        lowering = true;
        blendT = 0f;
        blendFromFront = handleFront.position;
        blendFromRear = handleRear.position;
        lowerFront = GroundHandlePoint(blendFromFront);
        lowerRear = GroundHandlePoint(blendFromRear);
    }

    // Bir sap noktasının yere konmuş hâli: zemin + sapın kökten yüksekliği.
    private Vector3 GroundHandlePoint(Vector3 p)
    {
        float handleHeight = HandleHeightAboveRoot();
        if (GroundProbe.Find(new Vector3(p.x, p.y + 0.5f, p.z), 30f, out RaycastHit hit))
            return new Vector3(p.x, hit.point.y + handleHeight, p.z);
        return p;
    }

    private float HandleHeightAboveRoot()
    {
        return (transform.InverseTransformPoint(handleFront.position).y + transform.InverseTransformPoint(handleRear.position).y) * 0.5f;
    }

    private void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // Güvenli pencere doldu ve hâlâ biri tutuyor: bırakılan uç yere iner, hasta düşer.
        if (pendingSince >= 0f && Time.time - pendingSince >= rules.safeReleaseWindow)
        {
            pendingSince = -1f;
            CurrentMode = Mode.Tilted;
            tiltedGroundFront = pendingFront;
            blendT = 0f;
            blendFromFront = handleFront.position;
            blendFromRear = handleRear.position;
            if (Passenger != null)
            {
                var p = TakePatient();
                p.Fall(Velocity + transform.right * fallSideSpeed + Vector3.down * 0.3f);
            }
        }

        if (CurrentMode == Mode.Raised || CurrentMode == Mode.Tilted)
            SolveCarried(dt);
        else if (lowering)
        {
            blendT = Mathf.Min(1f, blendT + dt / Mathf.Max(0.01f, liftTime));
            float u = Mathf.SmoothStep(0f, 1f, blendT);
            SetHandles(Vector3.Lerp(blendFromFront, lowerFront, u), Vector3.Lerp(blendFromRear, lowerRear, u));
            if (blendT >= 1f)
            {
                lowering = false;
                SetColliders(true);
            }
        }

        Velocity = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
    }

    private void SolveCarried(float dt)
    {
        Vector3 center = (handleFront.position + handleRear.position) * 0.5f;
        bool hasFront = AverageHand(true, center, out Vector3 front);
        bool hasRear = AverageHand(false, center, out Vector3 rear);
        float span = HandleSpanLocal();

        if (CurrentMode == Mode.Raised && pendingSince >= 0f)
        {
            // Güvenli pencere: bırakılan uç son yerinde bekler.
            if (pendingFront) { front = handleFront.position; hasFront = true; }
            else { rear = handleRear.position; hasRear = true; }
        }

        if (CurrentMode == Mode.Tilted)
        {
            // Tutulan uç elde, öbür uç yerde ve sap aralığı kadar uzakta (sürüklenir).
            Vector3 held = tiltedGroundFront ? rear : front;
            bool hasHeld = tiltedGroundFront ? hasRear : hasFront;
            if (!hasHeld) held = tiltedGroundFront ? handleRear.position : handleFront.position;
            Vector3 other = tiltedGroundFront ? handleFront.position : handleRear.position;
            Vector3 dir = other - held;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = tiltedGroundFront ? transform.forward : -transform.forward;
            Vector3 grounded = GroundHandlePoint(held + dir.normalized * span);
            // Eğik durumda sap aralığını koru
            Vector3 d = grounded - held;
            grounded = held + d.normalized * span;
            if (tiltedGroundFront) { front = grounded; rear = held; }
            else { rear = grounded; front = held; }
            hasFront = hasRear = true;
        }
        else
        {
            // Bir uçta kimse yoksa (ör. 3 kişi aynı uçta) o uç öbür ucun hizasında, sap aralığı kadar uzakta durur.
            if (!hasFront && hasRear) front = rear + Flat(handleFront.position - handleRear.position) * span;
            if (!hasRear && hasFront) rear = front + Flat(handleRear.position - handleFront.position) * span;
            if (!hasFront && !hasRear) return;
        }

        if (blendT < 1f)
        {
            blendT = Mathf.Min(1f, blendT + dt / Mathf.Max(0.01f, liftTime));
            float u = Mathf.SmoothStep(0f, 1f, blendT);
            front = Vector3.Lerp(blendFromFront, front, u);
            rear = Vector3.Lerp(blendFromRear, rear, u);
        }
        SetHandles(front, rear);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    private bool AverageHand(bool front, Vector3 center, out Vector3 point)
    {
        point = Vector3.zero;
        int n = 0;
        foreach (var g in grips)
        {
            if (g.front != front) continue;
            point += g.carrier.GetHandPoint(center);
            n++;
        }
        if (n > 0) point /= n;
        return n > 0;
    }

    private float HandleSpanLocal()
    {
        return Vector3.Distance(transform.InverseTransformPoint(handleFront.position), transform.InverseTransformPoint(handleRear.position));
    }

    // Kökü, iki sap verilen noktalara gelecek şekilde konumlar (sap orta noktası ve ekseni).
    private void SetHandles(Vector3 front, Vector3 rear)
    {
        Vector3 axis = front - rear;
        if (axis.sqrMagnitude < 1e-6f) return;
        Vector3 localFront = transform.InverseTransformPoint(handleFront.position);
        Vector3 localRear = transform.InverseTransformPoint(handleRear.position);
        Vector3 localAxis = localFront - localRear;
        Vector3 localMid = (localFront + localRear) * 0.5f;
        // Yerel sap ekseni → dünya ekseni dönüşü (yukarı yönü korunarak)
        Quaternion world = Quaternion.LookRotation(axis.normalized, Vector3.up);
        Quaternion local = Quaternion.LookRotation(localAxis.normalized, Vector3.up);
        Quaternion rot = world * Quaternion.Inverse(local);
        Vector3 pos = (front + rear) * 0.5f - rot * localMid;
        transform.SetPositionAndRotation(pos, rot);
    }

    private void SetColliders(bool on)
    {
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }
}
