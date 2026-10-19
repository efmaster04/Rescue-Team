using System;
using System.Collections.Generic;
using UnityEngine;

// U4 (kullanıcı kararı "Hırsızlar", 2026-10-19): hırsız yapay zekâsı. Kamyonetten iner, güvende olmayan en "kârlı" eşyayı
// (puan = değer / yol) kapar, kamyonete koşup atar (ReportStolen → eşya kaybolur), hedef kalmayınca kamyonete biner.
//  - Hareket: mevcut bot gibi GroundProbe adımlarıyla; yalnız ≤ slopeLimit zemin, ≤ stepHeight basamak; yön tarama (±35/70/110°)
//    engelleri dolaşır; yol ↔ patika geçişi ThiefDirector'ın rampa ara noktalarıyla. Halat kullanmaz, minibüse girmez, kenardan düşmez.
//  - Kapma: yerdeki eşya (Thief_Grab, grab_frame), oyuncunun ELİNDEKİ eşya (oyuncunun ≤ handGrabRange önündeyse), yatan/sedyedeki
//    hastanın eşyası, boş sedye (arkasında komik sürükler), kangal (dolapta/yerde; bağlı halat çalınmaz), koni.
//  - Saldırı: elleri boşken attackRadius içinde yaklaşan / yolunu kesen ya da hedefi elinde tutan oyuncuya Thief_Punch (hit_frame'de
//    önündeki punchRange/punchAngle içindekilere punchDamage), bekleme punchCooldown. Eşya taşıyan vurmaz, kaçar.
//  - Canı yok: sersemletilir (Thief_Stunned; elindekini düşürür), düdükle korkar (kamyonete kaçar, eşyasını bırakmaz),
//    araç çarparsa ragdoll olup vehicleDownSeconds sonra kalkar (GetUp_FromBack), devam eder.
// Oyun mantığı yalnız olay yayınlar; etiket (ThiefLabel) ve HUD okur.
[DefaultExecutionOrder(55)] // oyuncudan sonra, taşınan eşya (LootItem 60) konumlanmadan önce
public class Thief : MonoBehaviour
{
    public static readonly List<Thief> All = new List<Thief>();

    public enum ThiefState { Unloading, Seek, Grab, Carry, Throw, Punch, Stunned, Feared, Down, GettingUp, Boarding, Flee, Gone }

    // Animator (AC_Thief; Bolum1ThiefBuilder kurar)
    public const string ParamSpeed = "Speed", ParamCarry = "Carry", ParamStunned = "Stunned";
    public const string TriggerGrab = "Grab", TriggerPunch = "Punch", TriggerThrow = "Throw", TriggerGetUp = "GetUp";
    public const string StateMove = "Move", StateMoveCarry = "MoveCarry", StateGrabAnim = "Grab", StatePunchAnim = "Punch";
    public const string StateStunnedAnim = "Stunned", StateThrowAnim = "ThrowInVan", StateGetUpAnim = "GetUp";
    // Klip olayları (clips_meta.json, 1 tabanlı kare → (kare−1)/30 sn)
    public const float GrabSeconds = 0.8f, GrabTime = 10f / 30f;
    public const float PunchSeconds = 0.8f, PunchTime = 13f / 30f;
    public const float ThrowSeconds = 0.8f, ThrowTime = 13f / 30f;
    public const float GetUpSeconds = 1.6f;

    public ThiefSettings settings;
    public Animator animator;
    [Tooltip("Kalça kemiği (Humanoid Hips).")]
    public Transform hips;
    public Rigidbody[] ragdollBodies = new Rigidbody[0];
    public Collider[] ragdollColliders = new Collider[0];
    [Tooltip("Renk varyantı (Black / Red / Green).")]
    public string variant = "Black";
    public AudioSource audioSource;

    public ThiefState State { get; private set; } = ThiefState.Unloading;
    public LootItem Carried { get; private set; }
    public LootItem Target { get; private set; }
    public ThiefVan Van { get; set; }
    public ThiefDirector Director { get; set; }
    public ThirdPersonPlayer PunchTarget { get; private set; }
    public float Speed { get; private set; }
    public bool IsStunned { get { return State == ThiefState.Stunned; } }
    public bool IsFeared { get { return State == ThiefState.Feared; } }
    public bool IsDown { get { return State == ThiefState.Down; } }
    public bool IsGone { get { return State == ThiefState.Gone; } }
    public bool CanBeHit { get { return State != ThiefState.Down && State != ThiefState.GettingUp && State != ThiefState.Gone; } }
    public float StunRemaining { get { return IsStunned ? Mathf.Max(0f, stunUntil - Time.time) : 0f; } }
    public float FearRemaining { get { return IsFeared ? Mathf.Max(0f, fearUntil - Time.time) : 0f; } }
    public string LastStunReason { get; private set; } = "";
    // Ölçüm sayaçları
    public int Grabs { get; private set; }
    public int HandGrabs { get; private set; }
    public int Throws { get; private set; }
    public int Punches { get; private set; }
    public int PunchHits { get; private set; }
    public int Stuns { get; private set; }
    public int Fears { get; private set; }
    public int VehicleHits { get; private set; }
    public int GetUps { get; private set; }
    public int StuckGiveUps { get; private set; }
    public Vector3 LastKnockback { get; private set; }

    public event Action<Thief, LootItem> Grabbed;
    public event Action<Thief, LootItem> Stole;
    public event Action<Thief> BoardedVan;
    public event Action<Thief, ThirdPersonPlayer> Punched;

    private readonly List<Vector3> route = new List<Vector3>();
    private int routeIndex;
    private Vector3 destination;
    private bool hasDestination;
    private float stuckTime;
    private int steerSign = 1;
    private readonly Dictionary<LootItem, float> unreachableUntil = new Dictionary<LootItem, float>();
    private float stateTimer, stunUntil, fearUntil, punchReadyAt = -100f, downAt;
    private bool grabDone, throwDone, punchDone, grabFromHand;
    private Vector3 knock;
    private float knockTime;
    private Transform[] bones;
    private Vector3[] bonePositions;
    private Quaternion[] boneRotations;
    private ThiefLabel label;
    private System.Random rng;
    private float smoothSpeed;
    private Vector3 lastPos;
    private float retargetAt;
    private static readonly Collider[] overlap = new Collider[24];

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (hips == null && animator != null && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        bones = GetComponentsInChildren<Transform>(true);
        bonePositions = new Vector3[bones.Length];
        boneRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++) { bonePositions[i] = bones[i].localPosition; boneRotations[i] = bones[i].localRotation; }
        SetRagdoll(false);
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 60f;
        audioSource.volume = 0.8f;
        rng = new System.Random(settings != null && settings.useFixedSeed ? settings.seed + All.Count * 131 : Environment.TickCount + All.Count);
        lastPos = transform.position;
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (label == null) label = ThiefLabel.Create(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (label != null) label.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (label != null) Destroy(label.gameObject);
    }

    private float SneakSpeed { get { return settings != null ? settings.sneakSpeed : 1.6f; } }
    private float RunSpeed { get { return settings != null ? settings.runSpeed : 3.6f; } }

    // Kamyonetten indi: kısa duraksama, sonra hedef arama.
    public void BeginUnload(ThiefVan van, ThiefDirector director)
    {
        Van = van;
        Director = director;
        State = ThiefState.Unloading;
        stateTimer = 0f;
        hasDestination = false;
    }

    // =====================================================================================
    // Durum makinesi
    // =====================================================================================
    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || State == ThiefState.Gone) return;
        stateTimer += dt;
        switch (State)
        {
            case ThiefState.Unloading:
                if (stateTimer >= 0.5f) Enter(ThiefState.Seek);
                break;
            case ThiefState.Seek: TickSeek(dt); break;
            case ThiefState.Grab: TickGrab(dt); break;
            case ThiefState.Carry: TickCarry(dt); break;
            case ThiefState.Throw: TickThrow(dt); break;
            case ThiefState.Punch: TickPunch(dt); break;
            case ThiefState.Stunned: TickStunned(dt); break;
            case ThiefState.Feared: TickFeared(dt); break;
            case ThiefState.Down:
                if (Time.time - downAt >= (settings != null ? settings.vehicleDownSeconds : 3f)) StandUp();
                break;
            case ThiefState.GettingUp:
                if (stateTimer >= GetUpSeconds) Enter(ThiefState.Seek);
                break;
            case ThiefState.Boarding: TickBoarding(dt); break;
            case ThiefState.Flee: TickFlee(dt); break;
        }
        // Hız (animasyon): yatay yer değiştirme
        Vector3 moved = transform.position - lastPos; moved.y = 0f;
        float v = State == ThiefState.Down ? 0f : moved.magnitude / dt;
        smoothSpeed = Mathf.Lerp(smoothSpeed, v, Mathf.Clamp01(dt * 10f));
        Speed = smoothSpeed;
        lastPos = transform.position;
        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(ParamSpeed, smoothSpeed);
            animator.SetBool(ParamCarry, Carried != null);
            animator.SetBool(ParamStunned, State == ThiefState.Stunned);
        }
    }

    private void Enter(ThiefState s)
    {
        State = s;
        stateTimer = 0f;
        hasDestination = false;
        grabDone = throwDone = punchDone = false;
        if (s == ThiefState.Seek && Target == null) retargetAt = 0f;
    }

    private void Trigger(string t)
    {
        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null) animator.SetTrigger(t);
    }

    // ---- Seek: hedef seç, gerekirse vur, yaklaş, kap ----
    private void TickSeek(float dt)
    {
        if (Van == null || Van.IsGone) { Enter(ThiefState.Flee); return; }
        if (Van.Departing) { Enter(ThiefState.Flee); return; }
        if (Target == null || !IsStealable(Target) || Time.time >= retargetAt)
        {
            Target = PickTarget();
            retargetAt = Time.time + 2.5f;
            hasDestination = false;
            if (Target == null) { Enter(ThiefState.Boarding); return; }
        }
        // Elleri boş: yaklaşan / yolunu kesen / hedefi tutan oyuncu
        var player = ThreateningPlayer();
        if (player != null)
        {
            bool holdsTarget = player.HeldValuable == Target && Target != null;
            float d = Flat(player.transform.position - transform.position).magnitude;
            if (holdsTarget && d <= settings.handGrabRange && InFrontOf(player, transform.position, settings.handGrabAngle))
            {
                grabFromHand = true;
                Face(Target.Center);
                Enter(ThiefState.Grab);
                Trigger(TriggerGrab);
                return;
            }
            if (Time.time >= punchReadyAt)
            {
                if (d > settings.punchApproach + 0.15f)
                {
                    MoveDirect(player.transform.position, SneakSpeed * 1.5f, dt);
                    return;
                }
                Face(player.transform.position);
                PunchTarget = player;
                Enter(ThiefState.Punch);
                Trigger(TriggerPunch);
                punchReadyAt = Time.time + settings.punchCooldown;
                Punches++;
                return;
            }
        }
        // Hedefe git
        Vector3 c = Target.Center;
        if (Target.IsHeld && Target.Holder != null) c = Target.Holder.transform.position + Target.Holder.transform.forward * 0.6f;
        float dist = Flat(c - transform.position).magnitude;
        if (dist <= settings.grabRange && !Target.IsHeld)
        {
            // Kangal dolaptaysa kapağı aç
            var coil = Target.GetComponent<RopeCoil>();
            if (coil != null && coil.InLocker && coil.locker != null && !coil.locker.IsOpen && !coil.locker.IsMoving) coil.locker.Toggle();
            grabFromHand = false;
            Face(c);
            Enter(ThiefState.Grab);
            Trigger(TriggerGrab);
            return;
        }
        MoveAlongRoute(c, SneakSpeed, dt, settings.grabRange * 0.8f);
    }

    private void TickGrab(float dt)
    {
        if (Target != null) Face(Target.Center);
        if (!grabDone && stateTimer >= GrabTime)
        {
            grabDone = true;
            if (Target != null && IsStealable(Target) && (!grabFromHand || HandInReach(Target)))
            {
                var item = Target;
                item.PickUpByThief(this);
                Carried = item;
                Target = null;
                Grabs++;
                if (grabFromHand) HandGrabs++;
                Grabbed?.Invoke(this, item);
            }
            else Target = null;
        }
        if (stateTimer >= GrabSeconds) Enter(Carried != null ? ThiefState.Carry : ThiefState.Seek);
    }

    private bool HandInReach(LootItem item)
    {
        if (item == null || !item.IsHeld || item.Holder == null) return false;
        float d = Flat(item.Holder.transform.position - transform.position).magnitude;
        return d <= settings.handGrabRange + 0.3f;
    }

    private void TickCarry(float dt)
    {
        if (Carried == null) { Enter(ThiefState.Seek); return; }
        if (Van == null || Van.IsGone || Van.Departing) { Enter(ThiefState.Flee); return; }
        Vector3 rear = Van.RearPoint;
        float speed = RunSpeed * (Carried.GetComponent<StretcherController>() != null ? settings.stretcherDragFactor : 1f);
        float dist = Flat(rear - transform.position).magnitude;
        if (dist <= settings.throwRange)
        {
            Face(Van.transform.position);
            Enter(ThiefState.Throw);
            Trigger(TriggerThrow);
            return;
        }
        MoveAlongRoute(rear, speed, dt, settings.throwRange * 0.8f);
    }

    private void TickThrow(float dt)
    {
        if (Van != null) Face(Van.transform.position);
        if (!throwDone && stateTimer >= ThrowTime)
        {
            throwDone = true;
            var item = Carried;
            Carried = null;
            if (item != null)
            {
                var reg = LootRegistry.Instance;
                bool ok = reg != null ? reg.ReportStolen(item) : false;
                if (!ok && reg == null) item.MarkStolen();
                if (Van != null) Van.AddLoot(item);
                Throws++;
                Stole?.Invoke(this, item);
            }
        }
        if (stateTimer >= ThrowSeconds) Enter(ThiefState.Seek);
    }

    private void TickPunch(float dt)
    {
        if (PunchTarget != null && stateTimer < PunchTime) Face(PunchTarget.transform.position);
        if (!punchDone && stateTimer >= PunchTime)
        {
            punchDone = true;
            foreach (var c in CarrierRegistry.All)
            {
                var p = c as ThirdPersonPlayer;
                if (p == null || p.IsDown) continue;
                if (!InCone(p.transform.position, settings.punchRange, settings.punchAngle)) continue;
                var h = p.GetComponent<PlayerHealth>();
                if (h != null && h.TakeDamage(settings.punchDamage, transform))
                {
                    PunchHits++;
                    Punched?.Invoke(this, p);
                }
            }
            Play(SynthClips.Cached("Punch", SynthClips.Punch));
        }
        if (stateTimer >= PunchSeconds) { PunchTarget = null; Enter(ThiefState.Seek); }
    }

    private void TickStunned(float dt)
    {
        if (knockTime > 0f)
        {
            float part = Mathf.Min(1f, dt / knockTime);
            Vector3 step = knock * part;
            knock -= step;
            knockTime -= dt;
            if (step.sqrMagnitude > 1e-8f && TryStep(step.normalized, step.magnitude, out Vector3 to, null)) transform.position = to;
        }
        if (Time.time >= stunUntil) Enter(Carried != null ? ThiefState.Carry : ThiefState.Seek);
    }

    private void TickFeared(float dt)
    {
        Vector3 to = Van != null && !Van.IsGone ? Van.RearPoint : transform.position + Flat(transform.position - NearestPlayerPos()).normalized * 10f;
        float dist = Flat(to - transform.position).magnitude;
        if (dist > 1.5f) MoveAlongRoute(to, RunSpeed, dt, 1.5f);
        if (Time.time >= fearUntil) Enter(Carried != null ? ThiefState.Carry : ThiefState.Seek);
    }

    private void TickBoarding(float dt)
    {
        if (Van == null || Van.IsGone || Van.Departing) { Enter(ThiefState.Flee); return; }
        // Bu arada yeni hedef çıktıysa (düşürülen eşya) tekrar dene
        if (stateTimer > 1f && Time.time >= retargetAt)
        {
            retargetAt = Time.time + 2f;
            var t = PickTarget();
            if (t != null) { Target = t; Enter(ThiefState.Seek); return; }
        }
        Vector3 rear = Van.RearPoint;
        float dist = Flat(rear - transform.position).magnitude;
        if (dist <= 1.2f)
        {
            State = ThiefState.Gone;
            Van.OnThiefBoarded(this);
            BoardedVan?.Invoke(this);
            gameObject.SetActive(false);
            return;
        }
        MoveAlongRoute(rear, RunSpeed, dt, 1.0f);
    }

    private void TickFlee(float dt)
    {
        if (Carried != null) { var c = Carried; Carried = null; c.DropByThief(transform.position, transform.forward); }
        Vector3 to = Director != null ? Director.FleePoint(transform.position) : transform.position + transform.forward * 50f;
        float dist = Flat(to - transform.position).magnitude;
        if (dist <= 3f || stateTimer > 25f || (stateTimer > 6f && Flat(transform.position - NearestPlayerPos()).magnitude > 45f))
        {
            State = ThiefState.Gone;
            if (Van != null) Van.OnThiefGone(this);
            Destroy(gameObject);
            return;
        }
        MoveAlongRoute(to, RunSpeed, dt, 2.5f);
    }

    // =====================================================================================
    // Dış etkiler
    // =====================================================================================

    // Sersemleme (itme / tackle / fırlatma): elindekini düşürür, knockback kadar geri savrulur.
    public bool Stun(float seconds, Vector3 knockback, string reason)
    {
        if (!CanBeHit) return false;
        if (Carried != null) { var c = Carried; Carried = null; c.DropByThief(transform.position, transform.forward); }
        Target = null;
        PunchTarget = null;
        stunUntil = Mathf.Max(State == ThiefState.Stunned ? stunUntil : 0f, Time.time + seconds);
        knock = knockback; knock.y = 0f;
        knockTime = knock.sqrMagnitude > 1e-6f ? 0.3f : 0f;
        LastKnockback = knock;
        LastStunReason = reason;
        Stuns++;
        if (State != ThiefState.Stunned) Enter(ThiefState.Stunned);
        return true;
    }

    // Düdük: kamyonete doğru kaçar, eşyasını bırakmaz. Sersem / yerdeyken etkilenmez.
    public bool Fear(float seconds)
    {
        if (!CanBeHit || State == ThiefState.Stunned || State == ThiefState.Boarding || State == ThiefState.Flee) return false;
        fearUntil = Time.time + seconds;
        Target = null;
        PunchTarget = null;
        Fears++;
        Enter(ThiefState.Feared);
        return true;
    }

    // Test: ışınla (rota sıfırlanır) ve hedef dayat (bir sonraki yeniden seçime kadar).
    public void TeleportForTest(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        lastPos = position;
        hasDestination = false;
        stuckTime = 0f;
    }

    public void ForceTarget(LootItem item)
    {
        Target = item;
        retargetAt = Time.time + 30f;
        hasDestination = false;
        unreachableUntil.Remove(item);
        if (State == ThiefState.Boarding) Enter(ThiefState.Seek);
        if (State == ThiefState.Seek) retargetAt = Time.time + 30f;
    }

    // Kamyonet kalkıyor: dönmemiş hırsız panikler (sersem / yerdeki olan kalkınca kaçar).
    public void OnVanLeaving()
    {
        if (State == ThiefState.Gone || State == ThiefState.Down || State == ThiefState.GettingUp || State == ThiefState.Stunned || State == ThiefState.Flee) return;
        Target = null;
        Enter(ThiefState.Flee);
    }

    // Trafik aracı çarptı: ragdoll, elindekini düşürür; vehicleDownSeconds sonra kalkar.
    public void HitByVehicle(Vector3 velocity)
    {
        if (!CanBeHit) return;
        if (Carried != null) { var c = Carried; Carried = null; c.DropByThief(transform.position, transform.forward); }
        Target = null;
        VehicleHits++;
        downAt = Time.time;
        Enter(ThiefState.Down);
        SetRagdoll(true);
        foreach (var rb in ragdollBodies) if (rb != null) rb.linearVelocity = velocity;
    }

    private void StandUp()
    {
        Vector3 at = hips != null ? hips.position : transform.position;
        Vector3 ground = transform.position;
        if (GroundProbe.Find(at + Vector3.up * 1f, 5f, out RaycastHit g)) ground = g.point;
        else ground = new Vector3(at.x, transform.position.y, at.z);
        SetRagdoll(false);
        for (int i = 1; i < bones.Length; i++) if (bones[i] != null) bones[i].SetLocalPositionAndRotation(bonePositions[i], boneRotations[i]);
        transform.position = ground;
        lastPos = ground;
        GetUps++;
        Enter(ThiefState.GettingUp);
        if (animator != null && animator.runtimeAnimatorController != null) { animator.Play(StateGetUpAnim, 0, 0f); }
    }

    private void SetRagdoll(bool on)
    {
        foreach (var rb in ragdollBodies)
        {
            if (rb == null) continue;
            if (!on && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = !on;
            rb.useGravity = on;
            rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
        foreach (var c in ragdollColliders) if (c != null) c.enabled = on;
        var body = GetComponent<CapsuleCollider>();
        if (body != null) body.enabled = !on;
        if (animator != null) animator.enabled = !on;
    }

    // =====================================================================================
    // Hedef seçimi
    // =====================================================================================

    // Çalınabilir mi: güvende/çalınmış/uçan değil, başka hırsızda değil, ulaşılabilir; türe göre kurallar.
    public bool IsStealable(LootItem item)
    {
        if (item == null || !item.gameObject.activeInHierarchy || item.IsSecured || item.IsStolen || item.IsFlying || item.IsHeldByThief) return false;
        if (unreachableUntil.TryGetValue(item, out float until) && Time.time < until) return false;
        if (Director != null && !Director.Reachable(item.Center)) return false;
        switch (item.kind)
        {
            case LootKind.Luggage:
                return item.IsHeld || item.CanPickUp;
            case LootKind.PatientItem:
                return item.IsHeld || item.CanPickUp;
            default:
            {
                var s = item.GetComponent<StretcherController>();
                if (s != null) return s.Passenger == null && s.CarrierCount == 0 && !s.DropPending;
                var cone = item.GetComponent<TrafficCone>();
                if (cone != null) return cone.target != null && !cone.IsHeld && !cone.HeldExternal;
                var coil = item.GetComponent<RopeCoil>();
                if (coil != null) return (coil.InLocker || coil.OnGround) && (coil.anchor == null || coil.anchor.State == RopeAnchor.RopeState.Stowed);
                return false;
            }
        }
    }

    // Puan = değer / yol uzunluğu; başka hırsızın hedefi (varsa alternatif) atlanır.
    public LootItem PickTarget()
    {
        LootItem best = null, bestAny = null;
        float bestScore = float.MinValue, bestAnyScore = float.MinValue;
        foreach (var item in LootItem.All)
        {
            if (!IsStealable(item)) continue;
            Vector3 c = item.Center;
            float len = Director != null ? Director.RouteLength(transform.position, c) : Flat(c - transform.position).magnitude;
            float score = item.TotalValue / Mathf.Max(1.5f, len) * (1f + 0.02f * (float)rng.NextDouble());
            bool claimed = false;
            foreach (var t in All) if (t != null && t != this && t.Target == item && t.State != ThiefState.Gone) { claimed = true; break; }
            if (score > bestAnyScore) { bestAnyScore = score; bestAny = item; }
            if (!claimed && score > bestScore) { bestScore = score; best = item; }
        }
        return best != null ? best : bestAny;
    }

    // Elleri boşken tehdit: attackRadius içinde en yakın oyuncu; yaklaşıyor (hızı bize doğru > 0.5), yolu kesiyor (≤ 2 m, önümüzde)
    // ya da hedefi elinde tutuyor. Yerdeki oyuncuya vurulmaz.
    private ThirdPersonPlayer ThreateningPlayer()
    {
        ThirdPersonPlayer best = null;
        float bestD = float.MaxValue;
        foreach (var c in CarrierRegistry.All)
        {
            var p = c as ThirdPersonPlayer;
            if (p == null || p.IsDown || !p.gameObject.activeInHierarchy) continue;
            Vector3 d = Flat(p.transform.position - transform.position);
            float dist = d.magnitude;
            if (dist > settings.attackRadius || Mathf.Abs(p.transform.position.y - transform.position.y) > 2.5f) continue;
            bool holdsTarget = Target != null && p.HeldValuable == Target;
            bool approaching = dist > 0.1f && Vector3.Dot(p.HorizontalVelocity, -d.normalized) > 0.5f;
            bool blocking = dist <= 2f && Vector3.Angle(transform.forward, d) <= 70f;
            if (!(holdsTarget || approaching || blocking)) continue;
            if (dist < bestD) { bestD = dist; best = p; }
        }
        return best;
    }

    private Vector3 NearestPlayerPos()
    {
        Vector3 best = transform.position + transform.forward;
        float bestD = float.MaxValue;
        foreach (var c in CarrierRegistry.All)
        {
            var p = c as ThirdPersonPlayer;
            if (p == null) continue;
            float d = Flat(p.transform.position - transform.position).magnitude;
            if (d < bestD) { bestD = d; best = p.transform.position; }
        }
        return best;
    }

    // =====================================================================================
    // Hareket
    // =====================================================================================

    // Rotalı hareket: hedef değişince ThiefDirector rotası (rampa ara noktaları) alınır. Varınca true.
    private bool MoveAlongRoute(Vector3 target, float speed, float dt, float arriveRadius)
    {
        if (!hasDestination || Flat(target - destination).magnitude > 1.0f)
        {
            destination = target;
            hasDestination = true;
            route.Clear();
            if (Director != null) route.AddRange(Director.Route(transform.position, target));
            else route.Add(target);
            routeIndex = 0;
        }
        else if (route.Count > 0) route[route.Count - 1] = target; // hedef biraz kaydıysa son noktayı güncelle
        if (routeIndex >= route.Count) return true;
        Vector3 wp = route[routeIndex];
        bool last = routeIndex == route.Count - 1;
        float radius = last ? arriveRadius : settings.waypointRadius;
        Vector3 d = Flat(wp - transform.position);
        if (d.magnitude <= radius)
        {
            routeIndex++;
            if (routeIndex >= route.Count) return true;
            wp = route[routeIndex];
            d = Flat(wp - transform.position);
        }
        Step(d.normalized, Mathf.Min(speed * dt, d.magnitude), dt);
        return false;
    }

    // Rotasız doğrudan yaklaşma (yumruk için oyuncuya).
    private void MoveDirect(Vector3 target, float speed, float dt)
    {
        Vector3 d = Flat(target - transform.position);
        if (d.magnitude < 0.05f) return;
        Step(d.normalized, Mathf.Min(speed * dt, d.magnitude), dt);
    }

    // Bir adım: yön taraması (düz, ±35, ±70, ±110°); adım atılamazsa takılma sayacı, 2 sn sonra hedef vazgeçilir.
    private void Step(Vector3 dir, float dist, float dt)
    {
        if (dist <= 1e-5f) return;
        float[] angles = { 0f, 35f * steerSign, -35f * steerSign, 70f * steerSign, -70f * steerSign, 110f * steerSign, -110f * steerSign };
        foreach (float a in angles)
        {
            Vector3 d2 = Quaternion.AngleAxis(a, Vector3.up) * dir;
            if (!TryStep(d2, dist, out Vector3 to, Carried)) continue;
            if (a != 0f) steerSign = a > 0f ? 1 : -1;
            transform.position = to;
            Face(transform.position + d2);
            stuckTime = 0f;
            return;
        }
        stuckTime += dt;
        if (stuckTime > 2f)
        {
            stuckTime = 0f;
            StuckGiveUps++;
            if (Target != null) unreachableUntil[Target] = Time.time + 15f;
            Target = null;
            hasDestination = false;
            steerSign = -steerSign;
            if (State == ThiefState.Seek) retargetAt = 0f;
        }
    }

    // Adım kabul: zemin var, eğim ≤ slopeLimit, basamak ≤ stepHeight, bel hizasında statik engel yok, hasta/kamyonet/sedye gövdesi yok.
    private bool TryStep(Vector3 dir, float dist, out Vector3 to, LootItem carrying)
    {
        to = transform.position;
        Vector3 from = transform.position;
        Vector3 cand = from + dir * dist;
        if (!GroundProbe.Find(cand + Vector3.up * 1.2f, 2.6f, out RaycastHit hit)) return false;
        float limit = settings != null ? settings.slopeLimit : 35f;
        if (hit.normal.y < Mathf.Cos(limit * Mathf.Deg2Rad)) return false;
        if (Mathf.Abs(hit.point.y - from.y) > (settings != null ? settings.stepHeight : 0.5f)) return false;
        if (GroundProbe.LineBlocked(from + Vector3.up * 0.6f, hit.point + Vector3.up * 0.6f)) return false;
        if (GroundProbe.LineBlocked(from + Vector3.up * 1.2f, hit.point + Vector3.up * 1.2f)) return false;
        if (BodyBlocked(hit.point, carrying)) return false;
        to = hit.point;
        return true;
    }

    // Hareketli ama katı gövdeler: yatan hasta (NpcBody), kamyonet gövdesi (arka noktaya ≤ 2.5 m değilse), başka sedye.
    private bool BodyBlocked(Vector3 at, LootItem carrying)
    {
        int n = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * 0.4f, at + Vector3.up * 1.3f, 0.3f, overlap, ~0, QueryTriggerInteraction.Ignore);
        bool nearVanRear = Van != null && Flat(Van.RearPoint - at).magnitude <= 2.5f;
        for (int i = 0; i < n; i++)
        {
            var c = overlap[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<Patient>() != null) return true;
            if (!nearVanRear && c.GetComponentInParent<ThiefVan>() != null) return true;
            var s = c.GetComponentInParent<StretcherController>();
            if (s != null && (carrying == null || s.gameObject != carrying.gameObject)) return true;
        }
        return false;
    }

    private void Face(Vector3 target)
    {
        Vector3 d = Flat(target - transform.position);
        if (d.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), 540f * Time.deltaTime);
    }

    // Nokta önümüzdeki koni içinde mi (yatay)
    public bool InCone(Vector3 p, float range, float angle)
    {
        Vector3 d = Flat(p - transform.position);
        return d.magnitude <= range && Vector3.Angle(Flat(transform.forward), d) <= angle * 0.5f + 1e-3f && Mathf.Abs(p.y - transform.position.y) < 2f;
    }

    private static bool InFrontOf(ThirdPersonPlayer p, Vector3 point, float angle)
    {
        Vector3 d = Flat(point - p.transform.position);
        return Vector3.Angle(Flat(p.transform.forward), d) <= angle * 0.5f;
    }

    private void Play(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        audioSource.clip = clip;
        audioSource.Play();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
