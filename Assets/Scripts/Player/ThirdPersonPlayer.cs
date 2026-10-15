using UnityEngine;

// Oyuncu hareketi (kullanıcı kararı 15): CharacterController tabanlı 3. şahıs.
// Girdi bu sınıfa dışarıdan verilir (PlayerInputReader); böylece oyun durumu ile girdi ayrı kalır (ileride ağ senkronu).
// Koşma tuşu YOK. Araç çarpınca düşme ve kaldırılma PlayerKnockdown'da (ragdoll).
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
    [Tooltip("Dönüş hızı (derece/sn).")]
    public float turnSpeed = 600f;
    [Tooltip("Zıplama yüksekliği (m).")]
    public float jumpHeight = 0.8f;
    public float gravity = -20f;
    [Tooltip("Araç çarpınca düşme ve arkadaş kaldırması (ragdoll).")]
    public PlayerKnockdown knockdown;

    public Transform Root { get { return transform; } }
    public StretcherController HeldStretcher { get; set; }
    // Elde taşınan koni (en fazla 1). Yalnız PlayerInteraction yazar.
    public TrafficCone HeldCone { get; set; }
    public bool IsDown { get { return knockdown != null && knockdown.IsDown; } }

    // Ambulans çarpınca kontrol kaybı (kullanıcı kararı 13); araç çarpıp düşünce kaldırılana kadar kontrol yok.
    public bool HasControl { get { return Time.time >= controlLostUntil && !IsDown; } }
    public Vector3 HorizontalVelocity { get; private set; }
    // Zıplama gerçekten başladı (animasyon: Jump).
    public event System.Action Jumped;

    private CharacterController controller;
    private Vector2 moveInput;
    private bool jumpRequested;
    private float verticalSpeed;
    private float controlLostUntil = -1f;
    private Vector3 knockRemaining;     // itilme yer değiştirmesi (kısa sürede uygulanır)
    private float knockTimeLeft;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
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

    public Vector3 GetHandPoint(Vector3 towards)
    {
        return CarrierRegistry.HandPoint(transform, rules != null ? rules.handHeight : 0.72f, rules != null ? rules.handReach : 0.3f, towards);
    }

    // Işınlama (test ve yeniden doğma).
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
        verticalSpeed = 0f;
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
        var bale = hit.collider != null ? hit.collider.GetComponent<LooseBale>() : null;
        if (bale != null) bale.PushFrom(hit.moveDirection, HorizontalVelocity.magnitude);
    }

    // Araç çarptı (kullanıcı kararları 5, 7): sedyeyi bırakmış sayılır, elindeki koni düşer, ragdoll olup savrulur.
    public void KnockDown(Vector3 velocity)
    {
        if (knockdown == null || knockdown.IsDown) return;
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

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (IsDown)
        {
            HorizontalVelocity = Vector3.zero;
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
        Vector3 wish = HasControl ? (forward * moveInput.y + right * moveInput.x) * moveSpeed : Vector3.zero;

        if (wish.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(wish.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        // Yerçekimi ve zıplama
        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f; // zemine bastır (rampada kopmasın)
        if (jumpRequested && controller.isGrounded && HasControl)
        {
            verticalSpeed = Mathf.Sqrt(2f * -gravity * jumpHeight);
            Jumped?.Invoke();
        }
        jumpRequested = false;
        verticalSpeed += gravity * dt;

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
        if (controller.enabled)
            controller.Move(wish * dt + knock + Vector3.up * verticalSpeed * dt);
        Vector3 moved = transform.position - before;
        moved.y = 0f;
        HorizontalVelocity = moved / dt;

        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetFloat(speedParameter, wish.magnitude, 0.1f, dt);
    }
}
