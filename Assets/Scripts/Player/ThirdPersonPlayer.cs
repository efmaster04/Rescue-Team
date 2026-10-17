using UnityEngine;

// Oyuncu hareketi (kullanıcı kararı 15): CharacterController tabanlı 3. şahıs.
// Girdi bu sınıfa dışarıdan verilir (PlayerInputReader); böylece oyun durumu ile girdi ayrı kalır (ileride ağ senkronu).
// Shift koşar; sedye taşırken hız sabittir. Düşme ve kaldırılma PlayerKnockdown'da.
[RequireComponent(typeof(CharacterController))]
public class ThirdPersonPlayer : MonoBehaviour, ICarrier
{
    [Tooltip("Hareket yönü bu kameranın yatay bakışına göre hesaplanır.")]
    public Transform cameraTransform;
    public Animator animator;
    public CarryRules rules;
    [Tooltip("Animator'daki hız parametresi (bekleme / yürüme / koşma blend'i).")]
    public string speedParameter = "Speed";

    [Tooltip("Yürüme hızı (m/sn).")]
    public float moveSpeed = 2.4f;
    [Min(0f)] public float runSpeed = 3.2f;
    [Min(0f)] public float hardLandingHeight = 3f;
    [Tooltip("Sedye taşırken yürüme hızı (m/sn). Taşıma ağır: ekip yük altında sendeler. Koni taşırken moveSpeed geçerli.")]
    public float carryStretcherSpeed = 1.4f;
    [Tooltip("Dönüş hızı (derece/sn).")]
    public float turnSpeed = 600f;
    [Tooltip("Zıplama yüksekliği (m).")]
    public float jumpHeight = 0.8f;
    public float gravity = -20f;
    [Tooltip("Araç çarpınca düşme ve arkadaş kaldırması (ragdoll).")]
    public PlayerKnockdown knockdown;
    [Header("D2: sırtta taşıma ve halat")]
    [Tooltip("Sırtta hastayla yürüme hızı (m/sn; Piggyback_Walk target_speed_mps). Koşulamaz.")]
    public float piggybackSpeed = 1.2f;
    [Tooltip("Sırttaki hastanın bağlandığı boş obje (Spine1 altında, clips_meta piggyback_socket).")]
    public Transform piggybackSocket;

    public Transform Root { get { return transform; } }
    public StretcherController HeldStretcher { get; set; }
    // Elde taşınan koni (en fazla 1). Yalnız PlayerInteraction yazar.
    public TrafficCone HeldCone { get; set; }
    // Sırttaki hasta (D2). Yalnız PlayerInteraction yazar (SetPiggyback).
    public Patient Piggyback { get; private set; }
    // Bağlı olunan halat (D2); halattayken CharacterController kapalı, konum halattan gelir.
    public RopeAnchor Rope { get; private set; }
    public bool IsDown { get { return knockdown != null && knockdown.IsDown; } }

    // Ambulans çarpınca kontrol kaybı (kullanıcı kararı 13); araç çarpıp düşünce kaldırılana kadar kontrol yok.
    public bool HasControl { get { return Time.time >= controlLostUntil && !IsDown; } }
    public Vector3 HorizontalVelocity { get; private set; }
    // Zıplama gerçekten başladı (animasyon: Jump).
    public event System.Action Jumped;

    private CharacterController controller;
    private Vector2 moveInput;
    private bool jumpRequested;
    private bool runHeld;
    private float lastGroundHeight;
    private bool airborne;
    private Vector3 steepNormal;
    private float steepUntil;
    public float LastLandingDrop { get; private set; }
    public float RequestedSpeed => HeldStretcher != null ? carryStretcherSpeed : Piggyback != null ? piggybackSpeed : runHeld ? runSpeed : moveSpeed;
    private float verticalSpeed;
    private float controlLostUntil = -1f;
    private Vector3 knockRemaining;     // itilme yer değiştirmesi (kısa sürede uygulanır)
    private float knockTimeLeft;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        lastGroundHeight = transform.position.y;
    }

    private void OnEnable() { CarrierRegistry.Add(this); }

    private void OnDisable()
    {
        if (HeldStretcher != null) HeldStretcher.Release(this);
        CarrierRegistry.Remove(this);
    }

    // ---- Girdi (PlayerInputReader ya da test çağırır) ----
    public void SetMoveInput(Vector2 move) { moveInput = Vector2.ClampMagnitude(move, 1f); }
    public void RequestJump() { jumpRequested = true; }
    public void SetRunInput(bool held) { runHeld = held; }

    public Vector3 GetHandPoint(Vector3 towards)
    {
        return CarrierRegistry.HandPoint(transform, rules != null ? rules.handHeight : 0.72f, rules != null ? rules.handReach : 0.3f, towards);
    }

    // Işınlama (test ve yeniden doğma).
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = Rope == null;
        verticalSpeed = 0f;
        lastGroundHeight = position.y;
        airborne = true;
        steepUntil = -1f;
        Physics.SyncTransforms();
    }

    // Ambulans teması: oyuncu verilen yer değiştirme kadar (şerit dışına) itilir ve kontrolü bir süre kaybeder.
    public void KnockAside(Vector3 displacement, float controlLossSeconds)
    {
        displacement.y = 0f;
        knockRemaining = displacement;
        knockTimeLeft = 0.25f;
        controlLostUntil = Time.time + controlLossSeconds;
    }

    // Yeniden doğma (şef kararı 8): elindekini bırakır, yerdeyse ayağa kalkar, verilen noktaya ışınlanır.
    public void RespawnAt(Vector3 position, Quaternion rotation)
    {
        DropPiggyback(Vector3.zero);
        if (Rope != null) { var r = Rope; Rope = null; r.Detach(this, position, rotation); }
        if (HeldStretcher != null) HeldStretcher.Release(this);
        if (HeldCone != null)
        {
            var cone = HeldCone;
            HeldCone = null;
            cone.Drop(Vector3.zero);
        }
        if (knockdown != null) knockdown.ResetStanding();
        moveInput = Vector2.zero;
        knockRemaining = Vector3.zero;
        knockTimeLeft = 0f;
        controlLostUntil = -1f;
        Teleport(position, rotation);
    }

    // Kontrolü en az seconds boyunca askıya alır (ör. GetUp_FromBack kalkış animasyonu sürerken).
    public void SuspendControl(float seconds)
    {
        controlLostUntil = Mathf.Max(controlLostUntil, Time.time + seconds);
    }

    // Saman balyası (pikaptan düşen, Rigidbody) itilir: oyuncu yürüyerek iter (şef kararı 5).
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!GroundProbe.IsMovingGameObject(hit.collider) && hit.normal.y > 0.01f
            && hit.normal.y < Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad)
            && hit.point.y < transform.position.y + controller.radius + 0.15f)
        {
            steepNormal = hit.normal;
            steepUntil = Time.time + 0.15f;
        }
        var bale = hit.collider != null ? hit.collider.GetComponent<LooseBale>() : null;
        if (bale != null) bale.PushFrom(hit.moveDirection, HorizontalVelocity.magnitude);
    }

    // Araç çarptı (kullanıcı kararları 5, 7): sedyeyi bırakmış sayılır, elindeki koni düşer, ragdoll olup savrulur.
    public void KnockDown(Vector3 velocity)
    {
        if (knockdown == null || knockdown.IsDown) return;
        DropPiggyback(velocity * 0.5f);
        if (HeldStretcher != null) HeldStretcher.Release(this);
        if (HeldCone != null)
        {
            var cone = HeldCone;
            HeldCone = null;
            cone.Drop(velocity * 0.5f);
        }
        moveInput = Vector2.zero;
        knockRemaining = Vector3.zero;
        knockTimeLeft = 0f;
        verticalSpeed = 0f;
        knockdown.KnockDown(velocity);
    }

    // ---- D2: sırtta taşıma ----
    public void SetPiggyback(Patient p)
    {
        Piggyback = p;
        if (p != null && piggybackSocket != null) p.AttachToBack(piggybackSocket);
    }

    public Patient ReleasePiggyback()
    {
        var p = Piggyback;
        Piggyback = null;
        return p;
    }

    // Araç çarpması / yeniden doğma: sırttaki hasta düşer (ragdoll), sonra sedyeyle alınır.
    private void DropPiggyback(Vector3 velocity)
    {
        if (Piggyback == null) return;
        var p = ReleasePiggyback();
        p.Fall(velocity);
    }

    // ---- D2: halat ----
    public void AttachRope(RopeAnchor rope)
    {
        Rope = rope;
        controller.enabled = false;
        verticalSpeed = 0f;
        jumpRequested = false;
        transform.SetPositionAndRotation(rope.ClimbPoint(rope.T), rope.ClimbRotation);
        HorizontalVelocity = Vector3.zero;
    }

    public void DetachRope(Vector3 position, Quaternion rotation)
    {
        Rope = null;
        Teleport(position, rotation);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (IsDown)
        {
            HorizontalVelocity = Vector3.zero;
            return;
        }
        if (Rope != null)
        {
            // Halatta: konum halattan; W/S yalnız kontrol varken (sırta alma / çekme animasyonu sürerken yok)
            jumpRequested = false;
            Vector3 at = Rope.Step(this, moveInput, HasControl, dt);
            if (Rope != null) transform.SetPositionAndRotation(at, Rope.ClimbRotation);
            HorizontalVelocity = Vector3.zero;
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetFloat(speedParameter, 0f);
            return;
        }

        // Kameraya göre yatay hareket yönü
        Vector3 forward = Vector3.forward;
        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = cameraTransform.up;
            forward.y = 0f;
            forward.Normalize();
        }
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float speed = RequestedSpeed;
        Vector3 wish = HasControl ? (forward * moveInput.y + right * moveInput.x) * speed : Vector3.zero;

        if (wish.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(wish.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        // Yerçekimi ve zıplama
        bool supported = controller.isGrounded && Time.time >= steepUntil;
        if (supported && verticalSpeed <= 0f)
        {
            lastGroundHeight = transform.position.y;
            airborne = false;
            verticalSpeed = -2f;
        }
        else airborne = true;
        if (jumpRequested && supported && HasControl && Piggyback == null) // sırtta hastayla zıplanmaz (D2, sade seçim)
        {
            verticalSpeed = Mathf.Sqrt(2f * -gravity * jumpHeight);
            airborne = true;
            Jumped?.Invoke();
        }
        jumpRequested = false;
        verticalSpeed += gravity * dt;

        // Dik yüzeye havada temas da tırmanış sayılır; yeniden zıplamaya dayanak olmaz.
        if (Time.time < steepUntil)
        {
            Vector3 outward = Vector3.ProjectOnPlane(steepNormal, Vector3.up).normalized;
            wish -= outward * Mathf.Min(0f, Vector3.Dot(wish, outward));
            wish += outward * 2f;
            verticalSpeed = Mathf.Min(verticalSpeed, -2f);
        }

        // İtilme
        Vector3 knock = Vector3.zero;
        if (knockTimeLeft > 0f)
        {
            float part = Mathf.Min(1f, dt / knockTimeLeft);
            knock = knockRemaining * part;
            knockRemaining -= knock;
            knockTimeLeft -= dt;
        }

        Vector3 before = transform.position;
        Vector3 horizontal = NpcBody.RestrictMotion(this, wish * dt + knock, controller.radius);
        if (controller.enabled)
        {
            controller.Move(horizontal + Vector3.up * verticalSpeed * dt);
            if (Time.time < steepUntil && transform.position.y > before.y)
            {
                // Fizik çözümünün eğime bastırarak verdiği yukarı adımı geri al.
                Vector3 corrected = before + Vector3.ProjectOnPlane(steepNormal, Vector3.up).normalized * dt * 2f;
                controller.enabled = false;
                transform.position = corrected;
                controller.enabled = true;
                verticalSpeed = Mathf.Min(verticalSpeed, -2f);
            }
            if (airborne && controller.isGrounded && verticalSpeed < 0f && Time.time >= steepUntil)
            {
                LastLandingDrop = lastGroundHeight - transform.position.y;
                airborne = false;
                if (LastLandingDrop > hardLandingHeight)
                {
                    KnockDown(wish + Vector3.down * 2f);
                    return;
                }
            }
        }
        Vector3 moved = transform.position - before;
        moved.y = 0f;
        HorizontalVelocity = moved / dt;

        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetFloat(speedParameter, wish.magnitude, 0.1f, dt);
    }
}
