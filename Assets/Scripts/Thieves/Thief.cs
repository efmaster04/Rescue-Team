using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// U4 (kullanıcı kararı "Hırsızlar", 2026-10-19): hırsız yapay zekâsı. Kamyonetten iner, güvende olmayan en "kârlı" eşyayı
// (puan = değer / yol) kapar, kamyonete koşup atar (ReportStolen → eşya kaybolur), hedef kalmayınca kamyonete biner.
//  - Hareket: NavMeshAgent. NavMesh sahne kurulurken fiziksel collider'lardan fırınlanır (Bolum1ThiefBuilder: eğim ≤ 35°, basamak
//    ≤ 0.5 m, yarıçap 0.3); yani yalnız yürünebilir zemin, rampa dahil; kaya çıkıntısı ve uçurum bağlantısız, minibüs / çekici /
//    kamyonet (NavMeshObstacle) engel. Yolu olmayan hedef (NavMeshPathStatus ≠ Complete) seçilmez.
//  - Kapma: yerdeki eşya (Thief_Grab, grab_frame), oyuncunun ELİNDEKİ eşya (oyuncunun ≤ handGrabRange önündeyse), yatan/sedyedeki
//    hastanın eşyası, boş sedye (arkasında komik sürükler), kangal (dolapta/yerde; bağlı halat çalınmaz), koni.
//  - Saldırı: elleri boşken attackRadius içinde yaklaşan / yolunu kesen ya da hedefi elinde tutan oyuncuya Thief_Punch (hit_frame'de
//    önündeki punchRange/punchAngle içindekilere punchDamage), bekleme punchCooldown. Eşya taşıyan vurmaz, kaçar.
//  - U4b canı var (thiefHealth 100; itme 25, fırlatma 35, tackle 50): her darbe sersemletir (Thief_Stunned; elindekini düşürür),
//    can 0 → PES: sersemlik bitince kamyonetin ÖN kapısına yürür (yolcu tarafı; dalganın son hırsızı şoför kapısı), kapı açılır,
//    biner, gizlenir; bir daha çıkmaz, saldırmaz, hedef seçmez. Düdükle korkar (kamyonete kaçar, eşyasını bırakmaz),
//    araç çarparsa ragdoll olup vehicleDownSeconds sonra kalkar (GetUp_FromBack), devam eder.
// Oyun mantığı yalnız olay yayınlar; etiket + can barı (ThiefLabel) ve HUD okur.
[DefaultExecutionOrder(55)] // oyuncudan sonra, taşınan eşya (LootItem 60) konumlanmadan önce
public class Thief : MonoBehaviour
{
    public static readonly List<Thief> All = new List<Thief>();

    public enum ThiefState { Unloading, Seek, Grab, Carry, Throw, Punch, Stunned, Feared, Down, GettingUp, Boarding, Flee, Gone, Surrender }

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
    public NavMeshAgent agent;
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
    public bool IsSurrendering { get { return State == ThiefState.Surrender; } }
    // Pes eden (yerde / kalkan / giden / kapıya yürüyen) hırsıza vurulmaz
    public bool CanBeHit { get { return State != ThiefState.Down && State != ThiefState.GettingUp && State != ThiefState.Gone && State != ThiefState.Surrender; } }
    // U4b: can
    public float Health { get; private set; } = -1f;
    public float MaxHealth { get { return settings != null ? settings.thiefHealth : 100f; } }
    public float HealthFraction { get { return MaxHealth > 0f ? Mathf.Clamp01((Health < 0f ? MaxHealth : Health) / MaxHealth) : 0f; } }
    public bool Surrendered { get; private set; }
    public bool UsedDriverDoor { get; private set; }
    public int Hits { get; private set; }
    public float DamageTaken { get; private set; }
    public string LastHitReason { get; private set; } = "";
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
    // Tanı (doğrulama okur)
    public string LastBlock { get; private set; } = "";
    public float StuckTime { get { return stuckTime; } }
    public string PathStatus { get { return agent != null && agent.enabled ? (agent.pathPending ? "pending" : agent.pathStatus.ToString()) : "agent kapalı"; } }
    public float RemainingDistance { get { return agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending ? agent.remainingDistance : -1f; } }

    public event Action<Thief, LootItem> Grabbed;
    public event Action<Thief, LootItem> Stole;
    public event Action<Thief> BoardedVan;
    public event Action<Thief, ThirdPersonPlayer> Punched;
    public event Action<Thief, float> Damaged;
    public event Action<Thief> SurrenderedEvent;

    private Vector3 destination;
    private bool hasDestination;
    private float stuckTime;
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
    private NavMeshPath scratchPath; // Awake'te kurulur (alan başlatıcısında NavMeshPath yaratılamaz)

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
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
        Health = MaxHealth;
        if (agent != null)
        {
            agent.updateRotation = true;
            agent.updateUpAxis = false;
            agent.autoBraking = true;
            agent.stoppingDistance = 0.2f;
        }
    }

    private void Start()
    {
        EnsureOnNavMesh();
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
        EnsureOnNavMesh();
    }

    // Ajan NavMesh'te değilse en yakın NavMesh noktasına (≤ 2.5 m) ışınla.
    private bool EnsureOnNavMesh()
    {
        if (agent == null || !agent.enabled) return false;
        if (agent.isOnNavMesh) return true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) { agent.Warp(hit.position); return agent.isOnNavMesh; }
        LastBlock = "navmesh yok";
        return false;
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
                if (stateTimer >= GetUpSeconds) AfterStun();
                break;
            case ThiefState.Boarding: TickBoarding(dt); break;
            case ThiefState.Flee: TickFlee(dt); break;
            case ThiefState.Surrender: TickSurrender(dt); break;
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
        // Yerinde yapılan eylemlerde ajan durur ve yüz kodla çevrilir
        bool stationary = s == ThiefState.Grab || s == ThiefState.Throw || s == ThiefState.Punch || s == ThiefState.Stunned || s == ThiefState.Unloading || s == ThiefState.GettingUp;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = stationary;
            if (stationary) agent.ResetPath();
            agent.updateRotation = !stationary;
        }
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
                Enter(ThiefState.Grab);
                FaceNow(Target.Center);
                Trigger(TriggerGrab);
                return;
            }
            if (Time.time >= punchReadyAt)
            {
                if (d > settings.punchApproach + 0.15f)
                {
                    GoTo(player.transform.position, SneakSpeed * 1.5f, settings.punchApproach);
                    return;
                }
                PunchTarget = player;
                Enter(ThiefState.Punch);
                FaceNow(player.transform.position);
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
            Enter(ThiefState.Grab);
            FaceNow(c);
            Trigger(TriggerGrab);
            return;
        }
        GoTo(c, SneakSpeed, settings.grabRange * 0.8f);
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
        // İniş noktasına varınca ya da kamyonet gövdesinin arka yarısına ≤ 1.5 m yaklaşınca (iniş noktası dar aralıktaysa) atar
        bool nearBody = Van.DistanceToBody(transform.position) <= 1.5f && Vector3.Dot(Flat(transform.position - Van.transform.position), Flat(Van.transform.forward)) < 0.5f;
        if (dist <= settings.throwRange || (nearBody && stateTimer > 0.5f))
        {
            Enter(ThiefState.Throw);
            FaceNow(Van.transform.position);
            Trigger(TriggerThrow);
            return;
        }
        GoTo(rear, speed, settings.throwRange * 0.8f);
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
                // U4b: kamyonette (geri alınabilir); kamyonet yoksa doğrudan çalındı
                if (Van != null && !Van.IsGone) Van.AddLoot(item);
                else
                {
                    var reg = LootRegistry.Instance;
                    if (reg == null || !reg.ReportStolen(item)) { if (reg == null) item.MarkStolen(); }
                }
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
            if (step.sqrMagnitude > 1e-8f)
            {
                if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(step); // NavMesh içinde kalır (uçurum / duvar yok)
                else transform.position += step;
            }
        }
        if (Time.time >= stunUntil) AfterStun();
    }

    // Sersemlik / korku bitti: pes ettiyse kapıya, değilse taşımaya ya da aramaya
    private void AfterStun()
    {
        if (Surrendered) { Enter(ThiefState.Surrender); return; }
        Enter(Carried != null ? ThiefState.Carry : ThiefState.Seek);
    }

    // ---- U4b: pes — kamyonetin ön kapısına yürü, bin, gizlen ----
    // Dalgada aktif (gitmemiş) başka hırsız kalmadıysa şoför kapısı (son pes eden kamyoneti sürer), yoksa yolcu kapısı.
    private bool IsLastActive()
    {
        if (Van == null) return true;
        foreach (var t in Van.Thieves) if (t != null && t != this && !t.IsGone && t.gameObject.activeInHierarchy) return false;
        return true;
    }

    private void TickSurrender(float dt)
    {
        if (Carried != null) { var c = Carried; Carried = null; c.DropByThief(transform.position, transform.forward); }
        Target = null;
        PunchTarget = null;
        if (Van == null || Van.IsGone || Van.Departing) { Enter(ThiefState.Flee); return; }
        bool driver = IsLastActive();
        Vector3 to = Van.FrontDoorPoint(driver, out Vector3 door);
        float d = Flat(to - transform.position).magnitude;
        float reach = settings != null ? settings.frontDoorReach : 1.0f;
        if (d <= 3f) Van.RequestFrontDoor(driver); // kapı açılır
        bool arrived = d <= reach || (Flat(door - transform.position).magnitude <= reach + 0.6f && stateTimer > 1f)
            || (stateTimer > 20f && Van.DistanceToBody(transform.position) <= 1.5f); // takıldıysa gövdenin yanından biner
        if (arrived && Van.FrontDoorAmount(driver) >= 0.5f)
        {
            UsedDriverDoor = driver;
            State = ThiefState.Gone;
            Van.BoardFront(this, driver);
            BoardedVan?.Invoke(this);
            gameObject.SetActive(false);
            return;
        }
        if (arrived) { if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath(); FaceNow(door); return; }
        GoTo(to, SneakSpeed, reach * 0.8f);
    }

    private void TickFeared(float dt)
    {
        Vector3 to = Van != null && !Van.IsGone ? Van.RearPoint : transform.position + Flat(transform.position - NearestPlayerPos()).normalized * 10f;
        float dist = Flat(to - transform.position).magnitude;
        if (dist > 1.5f) GoTo(to, RunSpeed, 1.5f);
        else if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
        if (Time.time >= fearUntil) AfterStun();
    }

    // Hedef kalmayınca binmeden önce iniş noktasında etrafa bakınma süresi (sn): yeni hedef (düşürülen eşya) çıkarsa döner.
    public const float IdleBeforeBoard = 15f;

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
        if (stateTimer < IdleBeforeBoard)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
            return;
        }
        Vector3 rear = Van.RearPoint;
        float dist = Flat(rear - transform.position).magnitude;
        if (dist <= 1.2f || (Van.DistanceToBody(transform.position) <= 1.2f && stateTimer > 1f))
        {
            State = ThiefState.Gone;
            Van.OnThiefBoarded(this);
            BoardedVan?.Invoke(this);
            gameObject.SetActive(false);
            return;
        }
        GoTo(rear, RunSpeed, 1.0f);
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
        GoTo(to, RunSpeed, 2.5f);
    }

    // =====================================================================================
    // Dış etkiler
    // =====================================================================================

    // U4b: darbe (itme / tackle / fırlatma isabeti): can düşer, sersemler; can 0 → pes (sersemlik bitince ön kapıya yürür).
    public bool Hit(float damage, float stunSeconds, Vector3 knockback, string reason)
    {
        if (!CanBeHit) return false;
        if (Health < 0f) Health = MaxHealth;
        Health = Mathf.Max(0f, Health - Mathf.Max(0f, damage));
        DamageTaken += damage;
        Hits++;
        LastHitReason = reason;
        Damaged?.Invoke(this, damage);
        if (Health <= 0f && !Surrendered)
        {
            Surrendered = true;
            SurrenderedEvent?.Invoke(this);
        }
        return Stun(stunSeconds, knockback, reason);
    }

    // Sersemleme (itme / tackle / fırlatma; testlerde hasarsız bekletme): elindekini düşürür, knockback kadar geri savrulur.
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
        if (!CanBeHit || Surrendered || State == ThiefState.Stunned || State == ThiefState.Flee) return false; // binmeye giden de korkar (zaten kamyonete koşar)
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
        if (agent != null && agent.enabled && NavMesh.SamplePosition(position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) { agent.Warp(hit.position); position = hit.position; }
        transform.SetPositionAndRotation(position, rotation);
        lastPos = position;
        hasDestination = false;
        stuckTime = 0f;
    }

    // Test: elindekini bırakır, hedefini ve beklemelerini sıfırlar, canı doldurur (pes geri alınır), aramaya döner.
    public void ResetForTest()
    {
        if (State == ThiefState.Gone) gameObject.SetActive(true);
        if (Carried != null) { var c = Carried; Carried = null; c.DropByThief(transform.position, transform.forward); }
        Target = null;
        PunchTarget = null;
        punchReadyAt = -100f;
        stunUntil = 0f;
        fearUntil = 0f;
        Health = MaxHealth;
        Surrendered = false;
        unreachableUntil.Clear();
        if (State == ThiefState.Down || State == ThiefState.GettingUp) return;
        Enter(ThiefState.Seek);
    }

    // Test: eşyayı doğrudan eline ver (Carry durumuna geçer).
    public void GiveItemForTest(LootItem item)
    {
        if (item == null) return;
        if (Carried != null && Carried != item) { var c = Carried; Carried = null; c.DropByThief(transform.position, transform.forward); }
        item.PickUpByThief(this);
        Carried = item;
        Target = null;
        if (State != ThiefState.Stunned && State != ThiefState.Down && State != ThiefState.GettingUp) Enter(ThiefState.Carry);
    }

    public void ForceTarget(LootItem item)
    {
        Target = item;
        retargetAt = Time.time + 30f;
        hasDestination = false;
        if (item != null) unreachableUntil.Remove(item);
        if (State == ThiefState.Boarding) Enter(ThiefState.Seek);
        if (State == ThiefState.Seek) retargetAt = Time.time + 30f;
    }

    // Kamyonet kalkıyor: dönmemiş hırsız panikler (sersem / yerdeki olan kalkınca kaçar).
    public void OnVanLeaving()
    {
        if (State == ThiefState.Gone || State == ThiefState.Down || State == ThiefState.GettingUp || State == ThiefState.Stunned || State == ThiefState.Flee || State == ThiefState.Surrender) return;
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
        if (agent != null) agent.enabled = false;
        SetRagdoll(true);
        foreach (var rb in ragdollBodies) if (rb != null) rb.linearVelocity = velocity;
    }

    // U4b: iki avucun ortası (clips_meta loot_grip: el kemiği + 5 cm kemik yönünde). Animator yoksa false.
    public bool HandsMid(out Vector3 mid, out Vector3 across)
    {
        mid = transform.position + transform.forward * 0.32f + Vector3.up * 0.8f;
        across = transform.right;
        if (animator == null || !animator.isHuman || !animator.enabled) return false;
        var l = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var r = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var lf = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var rf = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (l == null || r == null) return false;
        Vector3 lp = l.position, rp = r.position;
        if (lf != null) lp += (l.position - lf.position).normalized * 0.05f;
        if (rf != null) rp += (r.position - rf.position).normalized * 0.05f;
        mid = (lp + rp) * 0.5f;
        across = rp - lp;
        return true;
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
        if (agent != null) { agent.enabled = true; EnsureOnNavMesh(); }
        Enter(ThiefState.GettingUp); // bitince AfterStun gibi: pes ettiyse kapıya
        if (animator != null && animator.runtimeAnimatorController != null) animator.Play(StateGetUpAnim, 0, 0f);
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

    // NavMesh yolu var mı ve uzunluğu: hedefin yakınındaki NavMesh noktasına (≤ 2 m) tam yol gerekir.
    public bool PathTo(Vector3 target, out float length)
    {
        length = Flat(target - transform.position).magnitude;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;
        if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 2.0f, NavMesh.AllAreas)) return false;
        if (scratchPath == null) scratchPath = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, scratchPath) || scratchPath.status != NavMeshPathStatus.PathComplete) return false;
        var corners = scratchPath.corners;
        length = 0f;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return true;
    }

    // Puan = değer / yol uzunluğu; yolu olmayan eşya atlanır; başka hırsızın hedefi (varsa alternatif) atlanır.
    public LootItem PickTarget()
    {
        LootItem best = null, bestAny = null;
        float bestScore = float.MinValue, bestAnyScore = float.MinValue;
        foreach (var item in LootItem.All)
        {
            if (!IsStealable(item)) continue;
            Vector3 c = item.Center;
            if (item.IsHeld && item.Holder != null) c = item.Holder.transform.position;
            if (!PathTo(c, out float len)) { unreachableUntil[item] = Time.time + 10f; continue; }
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
    // Hareket (NavMeshAgent)
    // =====================================================================================

    // Hedefe git: hedef 0.5 m'den çok kaydıysa yeniden yol; yolu olmayan hedef 2 sn içinde bırakılır.
    private void GoTo(Vector3 target, float speed, float arrive)
    {
        if (agent == null || !agent.enabled) { transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime); return; }
        if (!EnsureOnNavMesh()) { stuckTime += Time.deltaTime; GiveUpIfStuck(); return; }
        agent.isStopped = false;
        agent.updateRotation = true;
        agent.speed = speed;
        agent.acceleration = Mathf.Max(8f, speed * 4f);
        agent.stoppingDistance = Mathf.Max(0.1f, arrive * 0.5f);
        if (!hasDestination || Flat(target - destination).magnitude > 0.5f)
        {
            destination = target;
            hasDestination = true;
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 2.0f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
            else { LastBlock = "hedef navmesh dışı"; stuckTime += 1f; GiveUpIfStuck(); return; }
        }
        if (!agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete) { LastBlock = "yol " + agent.pathStatus; stuckTime += Time.deltaTime; }
        else if (!agent.pathPending && agent.hasPath && agent.velocity.sqrMagnitude < 0.01f && agent.remainingDistance > arrive + 0.5f) { LastBlock = "duruyor"; stuckTime += Time.deltaTime; }
        else stuckTime = 0f;
        GiveUpIfStuck();
    }

    private void GiveUpIfStuck()
    {
        if (stuckTime <= 2f) return;
        stuckTime = 0f;
        StuckGiveUps++;
        if (Target != null) unreachableUntil[Target] = Time.time + 15f;
        Target = null;
        hasDestination = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
        if (State == ThiefState.Seek) retargetAt = 0f;
    }

    private void Face(Vector3 target)
    {
        Vector3 d = Flat(target - transform.position);
        if (d.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), 540f * Time.deltaTime);
    }

    private void FaceNow(Vector3 target)
    {
        Vector3 d = Flat(target - transform.position);
        if (d.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
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
