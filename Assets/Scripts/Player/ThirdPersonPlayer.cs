using UnityEngine;

// Oyuncu hareketi (kullanıcı kararı 15): CharacterController tabanlı 3. şahıs.
// Girdi bu sınıfa dışarıdan verilir (PlayerInputReader); böylece oyun durumu ile girdi ayrı kalır (ileride ağ senkronu).
// Shift koşar; sedye taşırken hız sabittir. Düşme ve kaldırılma PlayerKnockdown'da.
// Eğilme (kullanıcı isteği 2026-11-05, sol Ctrl basılı): crouchSpeed ile yürür, koşu ve zıplama yok, CharacterController boyu
// crouchHeight'a iner (alçak engelin altından geçilir); tuş bırakılınca üstte engel yoksa kalkar, varsa eğik kalır (CanStandUp).
// Sedye / sırtta hasta / kangal / halat / yerdeyken eğilinmez (eğikken biri başlarsa kalkar). Animasyon: Animator'ın "Crouch"
// katmanı (bacak + gövde + baş maskeli Crouch_Idle/Walk) CrouchAmount ağırlığıyla; kollar taban katmandan (yumruk, itme, taşıma pozu).
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
    [Header("Eğilme (sol Ctrl)")]
    [Tooltip("Eğik yürüme hızı (m/sn; Crouch_Walk target_speed_mps). Koşulamaz, zıplanamaz.")]
    public float crouchSpeed = 1.2f;
    [Tooltip("Eğikken CharacterController boyu (m); taban yerinde kalır. Ayakta boy Awake'te okunur (1.5).")]
    public float crouchHeight = 1.0f;
    [Tooltip("Ayakta ↔ eğik görsel geçiş süresi (sn): Animator katman ağırlığı ve kamera pivotu bununla yumuşar.")]
    public float crouchBlendSeconds = 0.2f;
    [Tooltip("Eğilince gövdenin (kalça / el hizası) yaklaşık alçalması (m); elde eşya noktası bunu okur (CrouchBodyDrop).")]
    public float crouchBodyDrop = 0.15f;
    public const string CrouchLayerName = "Crouch";
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
    // U3: tek elle taşınan değerli eşya (valiz, hasta eşyası; koni kuralı: koşu serbest). Yalnız PlayerInteraction yazar.
    public LootItem HeldValuable { get; set; }
    // Ofis-garaj: tek elle taşınan dış eşya (CarryableItem köprüsü; IHeldProp). Yalnız köprü (CompanyPlayerBridge) ve PlayerCombat yazar.
    // Koni kuralı: koşu ve zıplama serbest. Çarpılma / yeniden doğmada düşer (DropValuable).
    public IHeldProp HeldProp { get; set; }
    // Sırttaki hasta (D2). Yalnız PlayerInteraction yazar (SetPiggyback).
    public Patient Piggyback { get; private set; }
    // Bağlı olunan halat (D2); halattayken CharacterController kapalı, konum halattan gelir.
    public RopeAnchor Rope { get; private set; }
    public bool IsDown { get { return knockdown != null && knockdown.IsDown; } }
    // FP (kullanıcı kararı 2, 2026-10-28): açıkken gövde yürüme yönüne değil kameranın yatay bakışına (yaw) döner; A/D yana adım
    // olur. OrbitCamera birinci şahısta açar, üçüncü şahısta kapatır (TP davranışı aynen kalır).
    public bool FaceCameraYaw { get; set; }
    // Eğilme: sol Ctrl basılı mı (girdi), şu an eğik mi (kapsül alçak), görsel geçiş 0..1 (SmoothStep'li; kamera ve Animator okur)
    public bool CrouchHeld { get { return crouchHeld; } }
    public bool IsCrouching { get; private set; }
    public float CrouchAmount { get { return Mathf.SmoothStep(0f, 1f, crouchBlend); } }
    public float CrouchBodyDrop { get { return crouchBodyDrop * CrouchAmount; } }
    // Ölçüm: eğilme / kalkma sayısı, tuş bırakıldığı hâlde üstteki engel yüzünden kalkılamayan kare sayısı ve son engel
    public int Crouches { get; private set; }
    public int StandUps { get; private set; }
    public int StandBlockedFrames { get; private set; }
    public string LastStandBlocker { get; private set; } = "";
    public float StandingHeight { get { return standHeight; } }

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
    public float RequestedSpeed => HeldStretcher != null ? carryStretcherSpeed : Piggyback != null ? piggybackSpeed : IsCrouching ? crouchSpeed : HeldCoil != null ? moveSpeed : runHeld ? runSpeed : moveSpeed;
    private float verticalSpeed;
    private Vector3 moveWish; // bu karenin yatay hareket isteği (OnControllerColliderHit okur)
    private bool walkableUnderNow; // bu karede ayağın altı yürünebilir zemin (OnControllerColliderHit okur)
    private float controlLostUntil = -1f;
    private Vector3 knockRemaining;     // itilme yer değiştirmesi (kısa sürede uygulanır)
    private float knockTimeLeft;
    private bool crouchHeld;
    private float crouchBlend;          // 0 ayakta … 1 eğik (doğrusal; CrouchAmount SmoothStep'li)
    private float standHeight = 1.5f;
    private Vector3 standCenter = new Vector3(0f, 0.77f, 0f);
    private int crouchLayer = -2;        // Animator "Crouch" katmanı (-2: aranmadı, -1: yok)
    private static readonly Collider[] standHits = new Collider[16];

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        lastGroundHeight = transform.position.y;
        standHeight = controller.height;
        standCenter = controller.center;
    }

    private void OnEnable() { CarrierRegistry.Add(this); }

    private void OnDisable()
    {
        if (HeldStretcher != null) HeldStretcher.Release(this);
        CarrierRegistry.Remove(this);
        // Devre dışı kalınca (minibüse binme) ayağa kalkar; tuş bırakılmış sayılır
        crouchHeld = false;
        if (IsCrouching) SetCrouchCapsule(false);
        crouchBlend = 0f;
        ApplyCrouchLayer();
    }

    // ---- Girdi (PlayerInputReader ya da test çağırır) ----
    public void SetMoveInput(Vector2 move) { moveInput = Vector2.ClampMagnitude(move, 1f); }
    public void RequestJump() { jumpRequested = true; }
    public void SetRunInput(bool held) { runHeld = held; }
    public void SetCrouchInput(bool held) { crouchHeld = held; }

    // Eğilmeye izin var mı: yerde değil, halatta değil, sedye / sırtta hasta / kangal yok. Elde tek elle eşya (koni, valiz, garaj
    // eşyası) serbest.
    public bool CanCrouch { get { return !IsDown && Rope == null && HeldStretcher == null && Piggyback == null && HeldCoil == null; } }

    // Ayakta boyun üst kısmı (eğik kapsülün tepesinden ayakta tepeye) boş mu? Kendi gövdesi, tetikleyiciler, karakterler ve küçük
    // fizik nesneleri (elde / yerde eşya, koni, kutu) engel sayılmaz; tavan, alçak kiriş, araç gövdesi sayılır.
    public bool CanStandUp()
    {
        if (controller == null) return true;
        float r = controller.radius * 0.95f;
        float crouchTop = CrouchCenterY + crouchHeight * 0.5f - controller.radius;
        float standTop = standCenter.y + standHeight * 0.5f - controller.radius;
        Vector3 a = transform.position + Vector3.up * crouchTop, b = transform.position + Vector3.up * standTop;
        int n = Physics.OverlapCapsuleNonAlloc(a, b, r, standHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = standHits[i];
            if (c == null || c == controller || c.transform.IsChildOf(transform)) continue;
            if (c.attachedRigidbody != null)
            {
                Vector3 size = c.bounds.size;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) <= 1.5f) continue; // koni, valiz, kutu
            }
            var anim = c.GetComponentInParent<Animator>();
            if (anim != null && anim.isHuman) continue; // hırsız, bot, hasta
            LastStandBlocker = c.name;
            return false;
        }
        return true;
    }

    private float CrouchCenterY { get { return standCenter.y - (standHeight - crouchHeight) * 0.5f; } }

    // Kapsül: taban yerinde kalır, boy ve merkez değişir.
    private void SetCrouchCapsule(bool crouch)
    {
        IsCrouching = crouch;
        if (controller == null) return;
        controller.height = crouch ? crouchHeight : standHeight;
        controller.center = crouch ? new Vector3(standCenter.x, CrouchCenterY, standCenter.z) : standCenter;
        if (crouch) Crouches++; else StandUps++;
    }

    // Her kare: girdi + izin → eğil / kalk; görsel geçiş ve Animator katmanı.
    private void UpdateCrouch(float dt)
    {
        bool allowed = CanCrouch;
        if (!IsCrouching && crouchHeld && allowed) SetCrouchCapsule(true);
        else if (IsCrouching && !allowed)
        {
            if (IsDown || CanStandUp()) SetCrouchCapsule(false);
            else StandBlockedFrames++;
        }
        else if (IsCrouching && !crouchHeld)
        {
            if (CanStandUp()) SetCrouchCapsule(false);
            else StandBlockedFrames++;
        }
        crouchBlend = IsDown ? 0f : Mathf.MoveTowards(crouchBlend, IsCrouching ? 1f : 0f, dt / Mathf.Max(0.01f, crouchBlendSeconds));
        ApplyCrouchLayer();
    }

    private void ApplyCrouchLayer()
    {
        if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled) return;
        if (crouchLayer == -2) crouchLayer = animator.GetLayerIndex(CrouchLayerName);
        if (crouchLayer >= 0) animator.SetLayerWeight(crouchLayer, CrouchAmount);
    }

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

    // Taşıma kilidi: aday poz (kök konumu + dönüş) izinli mi? İki taşıyıcıda bot izleyebilmeli (CanFollowFrom), tek taşıyıcıda
    // serbest uç collider'a girmemeli (SoloCarryClear). Reddin nedeni LastCarryBlockReason'a yazılır.
    private bool CarryPoseAllowed(Vector3 pos, Quaternion rot, StretcherHelperBot follower)
    {
        if (follower != null)
        {
            if (follower.CanFollowFrom(pos, rot)) return true;
            LastCarryBlockReason = follower.LastBlockReason;
            return false;
        }
        if (HeldStretcher == null || HeldStretcher.SoloCarryClear(this, pos, rot, out string why)) return true;
        LastCarryBlockReason = why;
        return false;
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
        Lunge(displacement, 0.25f, controlLossSeconds);
    }

    // U4: yer değiştirme verilen sürede uygulanır (tackle kayması: zemine ve duvarlara saygılı, CharacterController.Move ile).
    public void Lunge(Vector3 displacement, float seconds, float controlLossSeconds)
    {
        displacement.y = 0f;
        knockRemaining = displacement;
        knockTimeLeft = Mathf.Max(0.05f, seconds);
        controlLostUntil = Mathf.Max(controlLostUntil, Time.time + controlLossSeconds);
    }

    // U4: Shift basılı mı (koşu isteği; PlayerCombat tackle için okur)
    public bool RunHeld { get { return runHeld; } }

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
        DropValuable();
        if (knockdown != null) knockdown.ResetStanding();
        moveInput = Vector2.zero;
        knockRemaining = Vector3.zero;
        knockTimeLeft = 0f;
        controlLostUntil = -1f;
        if (IsCrouching) SetCrouchCapsule(false);
        crouchHeld = false;
        crouchBlend = 0f;
        ApplyCrouchLayer();
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
        DropValuable();
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

    // U3: elindeki değerli eşya önüne, yere düşer (çarpılma / can 0 / yeniden doğma).
    private void DropValuable()
    {
        if (HeldProp != null)
        {
            var prop = HeldProp;
            HeldProp = null;
            prop.DropFrom(this);
        }
        if (HeldValuable == null) return;
        var v = HeldValuable;
        HeldValuable = null;
        v.DropNear(transform.position, transform.forward);
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
        UpdateCrouch(dt);
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
        // Tek taşıyıcı (boş sedye): bot yok, serbest uç 2.1 m ötede havada; aynı kilit StretcherController.SoloCarryClear ile
        // (serbest uç ve sedye gövdesi duvara / minibüs gövdesine girmez).
        bool soloGuard = stretcherWallGuard && follower == null && HeldStretcher != null && HeldStretcher.IsRaised && HeldStretcher.CarrierCount == 1;
        bool guarded = follower != null || soloGuard;
        CarryBlockedNow = false;
        if (FaceCameraYaw && cameraTransform != null && HasControl)
        {
            // Birinci şahıs: gövde kamera yönüne (yaw) bakar; E4 kilidi burada da geçerli (bot izleyemeyecekse dönmez). Kontrol
            // yokken (itme / tackle / fırlatma penceresi, itilme) TP'deki gibi dönmez: FaceForAttack'in hırsıza çevirdiği yüz korunur.
            Quaternion target = Quaternion.LookRotation(forward, Vector3.up);
            if (!guarded || CarryPoseAllowed(transform.position, target, follower))
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            else { CarryTurnBlocks++; CarryBlockedNow = true; }
        }
        else if (wish.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(wish.normalized, Vector3.up);
            // Dönüş İSTENEN SON yöne göre sınanır: o yönde sedye dik yüzeye bakacaksa dönüş hiç başlamaz. Ara adımlar
            // sınanmaz; yoksa dar rampada 180° dönüşte 2.1 m'lik süpürme yayı her iki yanda da bankı kestiğinden dönüş
            // imkânsızlaşıyordu (ölçüldü: 86.849 engelli kare, çıkış 30 sn 0 m/sn). Süpürme sırasında botu E3 kuralı düz tutar.
            if (!guarded || CarryPoseAllowed(transform.position, target, follower))
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            else { CarryTurnBlocks++; CarryBlockedNow = true; }
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
        if (jumpRequested && supported && HasControl && Piggyback == null && HeldCoil == null && !IsCrouching) // sırtta hastayla / kangalla / eğikken zıplanmaz (D2, U2)
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
        // E4: bu yöndeki adım botu dik yüzeye/collider içine sokacaksa (tek taşıyıcıda: serbest ucu) yatay istek sıfırlanır (itilme kalır).
        if (guarded && !sliding && wish.sqrMagnitude > 0.01f && !CarryPoseAllowed(before + wish * dt, transform.rotation, follower))
        {
            wish = Vector3.zero;
            CarryMoveBlocks++;
            CarryBlockedNow = true;
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
