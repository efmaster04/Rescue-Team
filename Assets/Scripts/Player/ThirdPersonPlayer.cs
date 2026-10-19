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
    [Header("E4: taşıma yön kilidi")]
    [Tooltip("Açıkken sedye taşırken yardımcı botun hedefi dik yüzeye (bank, yamaç, duvar) ya da collider içine düşecekse o yöne dönüş ve yürüyüş durur (yerinde kalır, başka yöne dönebilir). Yalnız taşıma sırasında; görünmez duvar değildir.")]
    public bool stretcherWallGuard = true;
    // E4 ölçüm: engellenen dönüş / yürüyüş kare sayısı ve son kare engelli miydi
    public int CarryTurnBlocks { get; private set; }
    public int CarryMoveBlocks { get; private set; }
    public bool CarryBlockedNow { get; private set; }
    public string LastCarryBlockReason { get; private set; } = "";

    public Transform Root { get { return transform; } }
    public StretcherController HeldStretcher { get; set; }
    // Elde taşınan koni (en fazla 1). Yalnız PlayerInteraction yazar.
    public TrafficCone HeldCone { get; set; }
    // U2: iki elle taşınan halat kangalı. Yalnız PlayerInteraction yazar. Taşırken koşu ve zıplama yok.
    public RopeCoil HeldCoil { get; set; }
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
    public bool IsSliding { get; private set; }
    public int SlideStarts { get; private set; }
    public float RequestedSpeed => HeldStretcher != null ? carryStretcherSpeed : Piggyback != null ? piggybackSpeed : HeldCoil != null ? moveSpeed : runHeld ? runSpeed : moveSpeed;
    private float verticalSpeed;
    private Vector3 moveWish; // bu karenin yatay hareket isteği (OnControllerColliderHit okur)
    private bool walkableUnderNow; // bu karede ayağın altı yürünebilir zemin (OnControllerColliderHit okur)
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
        if (HeldStretcher != null) return StretcherHandIK.MarkerPoint(this);
        return CarrierRegistry.HandPoint(transform, rules != null ? rules.handHeight : 0.72f, rules != null ? rules.handReach : 0.3f, towards);
    }

    // E4: tuttuğum sedyenin öbür ucunu izleyen yardımcı bot (yoksa null; sedye kalkık değilse kilit yok).
    private StretcherHelperBot FollowerBot()
    {
        if (HeldStretcher == null || !HeldStretcher.IsRaised) return null;
        foreach (var c in CarrierRegistry.All)
        {
            var b = c as StretcherHelperBot;
            if (b != null && b.HeldStretcher == HeldStretcher && b.Leader == (ICarrier)this) return b;
        }
        return null;
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
        IsSliding = false;
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
        DropCoil();
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
            && hit.point.y < transform.position.y + controller.radius + 0.15f
            // E2: ayak yürünebilir zemindeyken (ör. patikada dik banka değerken) yan temas sayılmaz; yükselirken yalnız
            // yüzeye doğru itiyorsa sayılır (dik yamaca zıplayarak tırmanma yine engellenir).
            && !walkableUnderNow
            && (verticalSpeed <= 0f || Vector3.Dot(moveWish, -Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized) > 0.1f))
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
        DropCoil();
        moveInput = Vector2.zero;
        knockRemaining = Vector3.zero;
        knockTimeLeft = 0f;
        verticalSpeed = 0f;
        knockdown.KnockDown(velocity);
    }

    // U2: elindeki kangal yere düşer (çarpılma / yeniden doğma); bağlıysa bağlı kalır, yerden tekrar alınır.
    private void DropCoil()
    {
        if (HeldCoil == null) return;
        var c = HeldCoil;
        HeldCoil = null;
        c.Drop();
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

        // E4: taşırken yön kilidi — botun aday pozda gömülmeden izleyemeyeceği dönüş/yürüyüş yapılmaz.
        // "Mevcut poz zaten engelliyse kilidi kaldır" kuralı denendi ve kaldırıldı: sınırda cırcır etkisi yapıp oyuncuyu
        // yarım hızla duvara sokuyordu (ölçüldü: duvarda bot gövdesi 38.6 cm içeride).
        StretcherHelperBot follower = stretcherWallGuard ? FollowerBot() : null;
        CarryBlockedNow = false;
        if (wish.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(wish.normalized, Vector3.up);
            // Dönüş İSTENEN SON yöne göre sınanır: o yönde sedye dik yüzeye bakacaksa dönüş hiç başlamaz. Ara adımlar
            // sınanmaz; yoksa dar rampada 180° dönüşte 2.1 m'lik süpürme yayı her iki yanda da bankı kestiğinden dönüş
            // imkânsızlaşıyordu (ölçüldü: 86.849 engelli kare, çıkış 30 sn 0 m/sn). Süpürme sırasında botu E3 kuralı düz tutar.
            if (follower == null || follower.CanFollowFrom(transform.position, target))
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            else { CarryTurnBlocks++; CarryBlockedNow = true; LastCarryBlockReason = follower.LastBlockReason; }
        }

        // Yerçekimi ve zıplama
        // Kapsül teması seyrekleşse de ayak altındaki dik zemin kaymayı sürdürür.
        bool groundFound = GroundProbe.Find(transform.position + Vector3.up * 0.2f, 0.55f, out RaycastHit support);
        bool steepGround = groundFound && support.normal.y < Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
        if (steepGround) { steepNormal = support.normal; steepUntil = Time.time + 0.08f; }
        // E2: ayağın altı yürünebilir zeminse (ör. patikada dik banka yaslanmış) yandaki dik temas kaydırmaz.
        bool walkableUnder = groundFound && !steepGround && support.distance <= 0.35f; // 0.2 ışın başı + deri payı
        walkableUnderNow = walkableUnder;
        bool sliding = Time.time < steepUntil && !walkableUnder;
        // Yerinde zıplarken yükselişte kayma yok; dik yüzeye doğru itiyorsa var (zıplayarak tırmanma engellenir).
        if (sliding && verticalSpeed > 0f && Vector3.Dot(wish, -Vector3.ProjectOnPlane(steepNormal, Vector3.up).normalized) <= 0.1f) sliding = false;
        if (sliding && !IsSliding) SlideStarts++;
        IsSliding = sliding;
        bool supported = controller.isGrounded && !sliding;
        if (supported && verticalSpeed <= 0f)
        {
            if (airborne && lastGroundHeight - transform.position.y > hardLandingHeight)
            {
                LastLandingDrop = lastGroundHeight - transform.position.y;
                KnockDown(wish + Vector3.down * 2f);
                return;
            }
            lastGroundHeight = transform.position.y;
            airborne = false;
            verticalSpeed = -2f;
        }
        else airborne = true;
        if (jumpRequested && supported && HasControl && Piggyback == null && HeldCoil == null) // sırtta hastayla / kangalla zıplanmaz (D2, U2)
        {
            verticalSpeed = Mathf.Sqrt(2f * -gravity * jumpHeight);
            airborne = true;
            Jumped?.Invoke();
        }
        jumpRequested = false;
        verticalSpeed += gravity * dt;

        // Dik yüzeye havada temas da tırmanış sayılır; yeniden zıplamaya dayanak olmaz.
        if (sliding)
        {
            Vector3 outward = Vector3.ProjectOnPlane(steepNormal, Vector3.up).normalized;
            wish = outward * 2f;
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
        // E4: bu yöndeki adım botu dik yüzeye/collider içine sokacaksa yatay istek sıfırlanır (itilme kalır).
        if (follower != null && !sliding && wish.sqrMagnitude > 0.01f && !follower.CanFollowFrom(before + wish * dt, transform.rotation))
        {
            wish = Vector3.zero;
            CarryMoveBlocks++;
            CarryBlockedNow = true;
            LastCarryBlockReason = follower.LastBlockReason;
        }
        moveWish = wish;
        Vector3 horizontal = NpcBody.RestrictMotion(this, wish * dt + knock, controller.radius);
        if (controller.enabled)
        {
            controller.Move(horizontal + Vector3.up * verticalSpeed * dt);
            if (sliding && transform.position.y > before.y)
            {
                // Fizik çözümünün eğime bastırarak verdiği yukarı adımı geri al.
                Vector3 corrected = before + Vector3.ProjectOnPlane(steepNormal, Vector3.up).normalized * dt * 2f;
                controller.enabled = false;
                transform.position = corrected;
                controller.enabled = true;
                verticalSpeed = Mathf.Min(verticalSpeed, -2f);
            }
            if (airborne && controller.isGrounded && verticalSpeed < 0f && !sliding)
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
