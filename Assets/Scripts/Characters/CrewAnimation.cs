using System;
using System.Linq;
using UnityEngine;

// Ekip karakterinin (oyuncu ve test botu) prosedürel animasyonlarını tetikler. Oyun durumunu DEĞİŞTİRMEZ, yalnız okur:
//  - Taşıma: elde sedye → CarryStretcher; tek başına taşınan eşyada sınıfa göre CarryCone (hafif), CarryTwoHand (orta), CarryHeavy (ağır):
//    bekleme / yürüme / koşu klipleri Speed ile karışır. Duruyorken eşya alınca PickUp, bırakınca PutDown (tek sefer), sonra uygun döngü;
//    yürürken alma / bırakma ayak kaydırmasın diye klip oynamaz, döngüye yumuşak geçilir.
//  - Jump: ThirdPersonPlayer.Jumped olayı. Elde eşya varken (kollar eşyada kalsın) Jump yalnız "LowerBody" katmanında oynar.
//  - Yana / geri adım: FP'de ya da nişanda gövde kameraya bakar; LowerBody katmanı (bacak + gövde, kollar taban katmandan) Strafe_Left/Right,
//    Walk_Back oynatır. Ağırlık gerçek gidiş yönünden gelir; öne yürürken 0.
//  - Startled: 6 m içinde bir trafik aracı korna çalınca (VehicleAudio.HornPlayed). Elde bir şey varken ATLANIR (sade seçim).
//  - Revive_Helper: kaldıran taraf (oyuncu ya da bot) her kare MarkReviving çağırır; çağrı kesilince biter.
//  - GetUp_FromBack: ragdoll'dan kalkış (BeginGetUp), ragdoll pozundan animasyona yumuşak geçişle.
//  - Celebrate: görev başarıyla bitince.
// Animator Controller: AC_Crew_Locomotion (Bolum1AnimBuilder kurar; durum ve parametre adları aşağıda).
[DefaultExecutionOrder(100)]
public class CrewAnimation : MonoBehaviour
{
    public const string StateLocomotion = "Locomotion";
    public const string StateCarryStretcher = "CarryStretcher";
    public const string StateCarryStretcherFront = "CarryStretcherFront";
    public const int CarryStretcherFront = 4;
    public const string StateCarryCone = "CarryCone";
    public const string StatePickUp = "PickUp";
    public const string StatePutDown = "PutDown";
    public const string StateJump = "Jump";
    public const string StateStartled = "Startled";
    public const string StateGetUp = "GetUp";
    public const string StateRevive = "ReviveHelper";
    public const string StateCelebrate = "Celebrate";
    // D2: minibüsten çekme, sırtta taşıma, halat
    public const string StateExtract = "ExtractPull";
    public const string StatePiggybackPickUp = "PiggybackPickUp";
    public const string StatePiggybackPutDown = "PiggybackPutDown";
    public const string StateCarryPiggyback = "CarryPiggyback";
    public const string StateRopeHang = "RopeHang", StateRopeClimb = "RopeClimb", StateRopeDescend = "RopeDescend";
    public const string StateRopePiggybackClimb = "RopePiggybackClimb", StateRopePull = "RopePull";
    public const string ParamRope = "Rope";        // int: 0 yok, 1 asılı, 2 tırmanış, 3 iniş, 4 sırtta yükle tırmanış (çekiliyor)
    public const string ParamPulling = "Pulling";  // bool: halatı çekiyor (Rope_Pull)
    public const string ParamAction = "Action";    // bool: tek seferlik D2 eylemi sürüyor (halat durumlarına geçiş bekler)
    public const int CarryPiggyback = 3;
    // U2: halat kangalı — dolaptan alma, göğüste taşıma (Carry_Coil_Idle/Walk), kazığa bağlama, atma
    public const string StateCarryCoil = "CarryCoil", StateTakeCoil = "CabinetTakeCoil", StateTieStake = "RopeTieStake", StateRopeThrow = "RopeThrow";
    public const int CarryCoil = 5;
    // U3: darbe tepkisi (HitReact 0.5 sn; PlayerHealth.TakeDamage → PlayAction)
    public const string StateHitReact = "HitReact";
    // U4: savunma — itme, tackle (dalış → kalkış), fırlatma (nişan döngüsü → atış), düdük (PlayerCombat → PlayAction / HoldAction)
    public const string StatePushShove = "PushShove", StateTackleDive = "TackleDive", StateTackleGetUp = "TackleGetUp";
    public const string StateThrowAim = "ThrowAim", StateThrowRelease = "ThrowRelease", StateWhistle = "WhistleBlow";

    // Taşıma sınıfları (2026-11-05): orta iki el, ağır kucakta. Hafif = CarryCone (Carry 2).
    public const string StateCarryTwoHand = "CarryTwoHand", StateCarryHeavy = "CarryHeavy";
    public const int CarryLight = 2, CarryTwoHand = 6, CarryHeavy = 7;
    // Alt gövde katmanı: yana / geri adım ve elde eşyayla zıplama
    public const string LowerBodyLayerName = "LowerBody", StateLowerStrafe = "Strafe";
    public const string ParamMoveX = "MoveX", ParamMoveY = "MoveY", ParamStrafeRate = "StrafeRate", ParamCarryRate = "CarryRate";
    public const float StrafeClipSpeed = 1.6f;   // Strafe_* ve Walk_Back klibinin yazıldığı hız (m/sn)
    public const float CarriedJumpSeconds = 0.68f; // Jump klibinin elde eşyayla oynayan kısmı (sn)

    public const string ParamCarry = "Carry";        // int: 0 yok, 1 sedye, 2 hafif eşya, 3 sırtta hasta (D2), 4 sedye ön uç, 5 kangal (U2), 6 orta eşya, 7 ağır eşya
    public const string ParamReviving = "Reviving";  // bool
    public const string TriggerPickUp = "PickUp", TriggerPutDown = "PutDown", TriggerJump = "Jump", TriggerStartled = "Startled", TriggerCelebrate = "Celebrate";

    public Animator animator;
    [Tooltip("Oyuncu ise (taşıma, zıplama, kontrol). Bot ise boş.")]
    public ThirdPersonPlayer player;
    [Tooltip("Test botu ise.")]
    public StretcherHelperBot bot;
    [Tooltip("Korna bu mesafe (m) içinde çalınca irkilir.")]
    public float startleRadius = 6f;
    [Tooltip("Ragdoll pozundan GetUp animasyonuna geçiş süresi (sn).")]
    public float getUpBlendSeconds = 0.4f;
    [Tooltip("GetUp sırasında kapatılıp sonunda açılan bileşenler (ayak IK'sı).")]
    public Behaviour[] disableDuringGetUp = new Behaviour[0];

    public int Carry { get; private set; }
    // Tanı: alt gövde katmanının ağırlığı ve elde eşyayla zıplama sürüyor mu
    public float LowerBodyWeight { get; private set; }
    public bool CarriedJumpActive { get { return Time.time < lowerJumpUntil; } }
    public int CarriedJumps { get; private set; }
    private int lowerLayer = -2;   // -2: aranmadı, -1: yok
    private float lowerJumpUntil = -1f;
    private PlayerCombat combat;
    public bool GettingUp { get { return getUpEnd > 0f && Time.time < getUpEnd; } }
    public float GetUpEndTime { get { return getUpEnd; } }
    public int Startles { get; private set; }
    public int GetUps { get; private set; }
    public event Action<CrewAnimation> GetUpFinished;

    // Görev sonu kutlaması, hangi görevden (sahnede tek)
    private MissionDirector mission;
    private float lastReviveMark = -100f;
    private float lastPullMark = -100f;
    private float actionUntil = -1f;
    public string LastAction { get; private set; } = "";
    public int RopeMode { get; private set; }
    private float getUpStart = -1f, getUpEnd = -1f;
    private bool getUpPending;
    private PoseSnapshot getUpFrom;

    // GetUp klibinin süresi (sn); Animator'dan okunur, yoksa klip süresi
    public float getUpSeconds = 1.6f;

    // Bağ pozundaki yerel konumlar: ragdoll eklemleri kemikleri biraz kaydırır, Humanoid animasyon ise (kalça hariç)
    // yalnız dönüş yazar. Kalkışta konumlar bağ pozuna döner, yoksa bacaklar uzamış/kaymış kalır.
    private Transform[] bindBones;
    private Vector3[] bindPositions;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        combat = GetComponent<PlayerCombat>();
        bindBones = GetComponentsInChildren<Transform>(true);
        bindPositions = bindBones.Select(t => t.localPosition).ToArray();
    }

    private void RestoreBindPositions()
    {
        var hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        for (int i = 1; i < bindBones.Length; i++)
            if (bindBones[i] != null && bindBones[i] != hips) bindBones[i].localPosition = bindPositions[i];
    }

    private void OnEnable()
    {
        VehicleAudio.HornPlayed += OnHorn;
        if (player != null) player.Jumped += OnJumped;
    }

    private void OnDisable()
    {
        VehicleAudio.HornPlayed -= OnHorn;
        if (player != null) player.Jumped -= OnJumped;
        if (mission != null) mission.MissionSucceeded -= OnMissionSucceeded;
        mission = null;
        // Minibüse binince bu bileşen kapanır, Animator açık kalır: katman ağırlığı donarsa koltukta bacaklar Strafe oynar
        LowerBodyWeight = 0f;
        lowerJumpUntil = -1f;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            if (lowerLayer == -2) lowerLayer = animator.GetLayerIndex(LowerBodyLayerName);
            if (lowerLayer >= 0) animator.SetLayerWeight(lowerLayer, 0f);
        }
    }

    private bool Ready { get { return animator != null && animator.enabled && animator.runtimeAnimatorController != null && animator.isActiveAndEnabled; } }

    private bool IsDown
    {
        get
        {
            if (player != null) return player.IsDown;
            if (bot != null) return bot.IsDown;
            return false;
        }
    }

    private int CurrentCarry()
    {
        if (player != null)
        {
            if (player.HeldStretcher != null) return player.HeldStretcher.HoldsFront(player) ? CarryStretcherFront : 1;
            switch (player.HeldClass)
            {
                case CarryClass.Light: return CarryLight;
                case CarryClass.Medium: return CarryTwoHand;
                case CarryClass.Heavy: return CarryHeavy;
            }
            return player.Piggyback != null ? CarryPiggyback : player.HeldCoil != null ? CarryCoil : 0;
        }
        if (bot != null) return bot.HeldStretcher != null ? (bot.HeldStretcher.HoldsFront(bot) ? CarryStretcherFront : 1) : 0;
        return 0;
    }

    private void Update()
    {
        if (mission == null && MissionDirector.Instance != null)
        {
            mission = MissionDirector.Instance;
            mission.MissionSucceeded += OnMissionSucceeded;
        }
        int carry = CurrentCarry();
        if (!Ready || IsDown)
        {
            Carry = carry;
            return;
        }
        if (carry != Carry)
        {
            // Sırta alma / bırakma kendi klibini PlayAction ile oynatır (genel PickUp/PutDown tetiklenmez).
            // U2: kangal dolaptan alınırken / atılırken de kendi klibi sürer (InAction); yerden alma ve yere bırakma genel PickUp/PutDown.
            // U4: fırlatma sırasında (Throw_Release) eşya elden çıkınca PutDown tetiklenmez (atış klibi tamamlanır).
            bool coilAction = (carry == CarryCoil || Carry == CarryCoil) && InAction;
            bool throwing = InAction && (LastAction == StateThrowRelease || LastAction == StateThrowAim);
            if (carry != CarryPiggyback && Carry != CarryPiggyback && !coilAction && !throwing)
            {
                // Yürürken alma / bırakma klibi oynamaz: klip ayakları yerinde tutar, kayma görünürdü. Döngüler arası geçiş yumuşak (0.2 sn).
                bool moving = player != null && player.HorizontalVelocity.sqrMagnitude > 0.36f;
                if (Carry == 0 && carry != 0) { if (!moving) animator.SetTrigger(TriggerPickUp); }
                else if (carry == 0) { if (!moving) animator.SetTrigger(TriggerPutDown); }
            }
            Carry = carry;
        }
        animator.SetInteger(ParamCarry, carry);
        animator.SetBool(ParamReviving, Time.time - lastReviveMark < 0.2f);
        animator.SetBool(ParamPulling, Time.time - lastPullMark < 0.2f);
        animator.SetBool(ParamAction, Time.time < actionUntil);
        RopeMode = 0;
        if (player != null && player.Rope != null)
        {
            float v = player.Rope.VerticalSpeed;
            RopeMode = v > 0.01f ? (player.Piggyback != null ? 4 : 2) : v < -0.01f ? 3 : 1;
        }
        animator.SetInteger(ParamRope, RopeMode);
        UpdateLowerBody();
    }

    // Alt gövde katmanı: gövde kameraya bakarken (FP, nişan) gerçek gidiş yönünden yana / geri adım ağırlığı; elde eşyayla zıplamada 1.
    private void UpdateLowerBody()
    {
        if (player == null) return;
        if (lowerLayer == -2) lowerLayer = animator.GetLayerIndex(LowerBodyLayerName);
        if (lowerLayer < 0) return;
        float target = 0f, rate = 1f;
        Vector3 v = player.HorizontalVelocity;
        float speed = v.magnitude;
        bool aiming = combat != null && combat.Aiming;
        bool facesLook = player.FaceCameraYaw || aiming;
        // Nişan süresiz eylemdir (HoldAction: InAction hep açık): nişanda da ayaklar gidiş yönüne adım atar, kollar Throw_Aim'de kalır
        if (facesLook && speed > 0.2f && player.HasControl && player.Rope == null && player.HeldStretcher == null && player.Piggyback == null && (!InAction || aiming))
        {
            Vector3 local = transform.InverseTransformDirection(v / speed);
            animator.SetFloat(ParamMoveX, local.x, 0.08f, Time.deltaTime);
            animator.SetFloat(ParamMoveY, local.z, 0.08f, Time.deltaTime);
            float forward = Mathf.Max(0f, local.z);
            target = (1f - forward * forward) * Mathf.Clamp01(speed / 0.5f);
            rate = Mathf.Clamp(speed / StrafeClipSpeed, 0.5f, 2.2f);
        }
        animator.SetFloat(ParamStrafeRate, rate);
        bool jumping = Time.time < lowerJumpUntil;
        if (jumping) target = 1f;
        LowerBodyWeight = Mathf.MoveTowards(LowerBodyWeight, target, Time.deltaTime / (jumping ? 0.06f : 0.12f));
        animator.SetLayerWeight(lowerLayer, LowerBodyWeight);
    }

    // Kaldıran taraf her kare çağırır.
    public void MarkReviving() { lastReviveMark = Time.time; }
    // Halatı çeken taraf her kare çağırır (D2).
    public void MarkPulling() { lastPullMark = Time.time; }
    public bool Pulling { get { return Time.time - lastPullMark < 0.2f; } }

    // Tek seferlik D2 eylemi (ExtractPull, PiggybackPickUp, PiggybackPutDown): doğrudan geçiş, süre boyunca Action açık.
    public void PlayAction(string state) { PlayAction(state, state == StatePiggybackPutDown ? 1.2f : 1.5f); }

    // seconds: klibin süresi (Action bu kadar açık kalır; U2 klipleri 1.4 / 2.0 / 1.4 sn).
    public void PlayAction(string state, float seconds)
    {
        LastAction = state;
        if (!Ready) return;
        actionUntil = Time.time + seconds;
        animator.SetBool(ParamAction, true);
        animator.CrossFadeInFixedTime(state, 0.1f, 0, 0f);
    }
    public bool InAction { get { return Time.time < actionUntil; } }

    // U4: süresiz eylem (Throw_Aim döngüsü): ReleaseAction çağrılana kadar Action açık kalır.
    public void HoldAction(string state)
    {
        LastAction = state;
        if (!Ready) return;
        actionUntil = float.PositiveInfinity;
        animator.SetBool(ParamAction, true);
        animator.CrossFadeInFixedTime(state, 0.15f, 0, 0f);
    }

    public void ReleaseAction()
    {
        if (float.IsPositiveInfinity(actionUntil)) actionUntil = Time.time;
    }

    private void OnJumped()
    {
        if (!Ready || IsDown) return;
        // Elde eşya: taban katman taşıma pozunda kalır (el IK'sı ve eşya kopmaz), yalnız bacak + gövde zıplar.
        if ((Carry == CarryLight || Carry == CarryTwoHand) && lowerLayer != -1)
        {
            if (lowerLayer == -2) lowerLayer = animator.GetLayerIndex(LowerBodyLayerName);
            if (lowerLayer >= 0)
            {
                animator.CrossFadeInFixedTime(StateJump, 0.06f, lowerLayer, 0.1f);
                lowerJumpUntil = Time.time + CarriedJumpSeconds;
                CarriedJumps++;
                return;
            }
        }
        animator.SetTrigger(TriggerJump);
    }

    private void OnHorn(VehicleAudio source, Vector3 at)
    {
        if (!Ready || IsDown || GettingUp) return;
        if (CurrentCarry() != 0 || InAction || (player != null && player.Rope != null) || Pulling) return; // taşırken / halatta irkilme atlanır
        Vector3 d = at - transform.position;
        if (d.magnitude > startleRadius) return;
        animator.SetTrigger(TriggerStartled);
        Startles++;
    }

    private void OnMissionSucceeded()
    {
        if (Ready && !IsDown) animator.SetTrigger(TriggerCelebrate);
    }

    // ---- Ragdoll → GetUp_FromBack ----

    // Ragdoll pozu (kalkmadan hemen önce yakalanır): kemiklerin yerel dönüşleri + kalçanın ve kafanın dünya pozu.
    public class PoseSnapshot
    {
        public Transform[] bones;
        public Quaternion[] local;
        public Transform hips;
        public Vector3 hipsPosition;
        public Quaternion hipsRotation;
        public Vector3 headPosition;
    }

    public static PoseSnapshot Capture(Animator a)
    {
        var s = new PoseSnapshot();
        var list = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var t = a.GetBoneTransform((HumanBodyBones)i);
            if (t != null) list.Add(t);
        }
        s.bones = list.ToArray();
        s.local = new Quaternion[s.bones.Length];
        for (int i = 0; i < s.bones.Length; i++) s.local[i] = s.bones[i].localRotation;
        s.hips = a.GetBoneTransform(HumanBodyBones.Hips);
        s.hipsPosition = s.hips.position;
        s.hipsRotation = s.hips.rotation;
        var head = a.GetBoneTransform(HumanBodyBones.Head);
        s.headPosition = head != null ? head.position : s.hipsPosition + Vector3.forward;
        return s;
    }

    // Ragdoll kapatıldıktan ve kök zemine konduktan SONRA çağrılır. place: kökü taşıyan fonksiyon (oyuncuda Teleport).
    // Kök, klibin ilk karesindeki baş–kalça yönü ragdoll'unkiyle aynı olacak şekilde döndürülür ve kalça hizasına kaydırılır;
    // sonra getUpBlendSeconds boyunca ragdoll pozundan animasyona geçilir.
    public void BeginGetUp(PoseSnapshot from, Action<Vector3, Quaternion> place)
    {
        if (animator == null) return;
        RestoreBindPositions();
        foreach (var b in disableDuringGetUp) if (b != null) b.enabled = false;
        foreach (var t in new[] { TriggerPickUp, TriggerPutDown, TriggerJump, TriggerStartled, TriggerCelebrate }) animator.ResetTrigger(t);
        Carry = CurrentCarry();
        animator.SetInteger(ParamCarry, Carry);
        animator.SetBool(ParamReviving, false);
        animator.Play(StateGetUp, 0, 0f);
        animator.Update(0f);

        if (from == null)
        {
            // Ragdoll pozu yok (ör. uçurumdan düşen bot yerinde kalkar): geçişsiz oynar.
            getUpFrom = null;
            getUpStart = Time.time;
            getUpEnd = Time.time + getUpSeconds;
            getUpPending = true;
            GetUps++;
            return;
        }
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 animDir = Flat(head.position - hips.position);
        Vector3 ragDir = Flat(from.headPosition - from.hipsPosition);
        Quaternion rot = transform.rotation;
        Vector3 pos = transform.position;
        if (animDir.sqrMagnitude > 0.01f && ragDir.sqrMagnitude > 0.01f)
        {
            float yaw = Vector3.SignedAngle(animDir, ragDir, Vector3.up);
            rot = Quaternion.AngleAxis(yaw, Vector3.up) * rot;
            // Kalçanın kök etrafında dönmüş yeri
            Vector3 hipsAfter = pos + Quaternion.AngleAxis(yaw, Vector3.up) * (hips.position - pos);
            pos += Flat(from.hipsPosition - hipsAfter);
        }
        else pos += Flat(from.hipsPosition - hips.position);
        place(pos, rot);
        animator.Play(StateGetUp, 0, 0f);
        animator.Update(0f);

        getUpFrom = from;
        getUpStart = Time.time;
        getUpEnd = Time.time + getUpSeconds;
        getUpPending = true;
        GetUps++;
    }

    private void LateUpdate()
    {
        if (getUpFrom != null)
        {
            float t = (Time.time - getUpStart) / Mathf.Max(0.01f, getUpBlendSeconds);
            if (t >= 1f || !Ready) getUpFrom = null;
            else
            {
                // Ağırlık: 1 = ragdoll pozu, 0 = animasyon
                float w = 1f - Mathf.SmoothStep(0f, 1f, t);
                var s = getUpFrom;
                for (int i = 0; i < s.bones.Length; i++)
                {
                    if (s.bones[i] == null || s.bones[i] == s.hips) continue;
                    s.bones[i].localRotation = Quaternion.Slerp(s.bones[i].localRotation, s.local[i], w);
                }
                s.hips.SetPositionAndRotation(Vector3.Lerp(s.hips.position, s.hipsPosition, w), Quaternion.Slerp(s.hips.rotation, s.hipsRotation, w));
            }
        }
        if (getUpPending && Time.time >= getUpEnd)
        {
            getUpPending = false;
            foreach (var b in disableDuringGetUp) if (b != null) b.enabled = true;
            GetUpFinished?.Invoke(this);
        }
    }

    // Verify / hata ayıklama: şu anki (ya da geçilen) durum adı bu mu?
    public bool InState(string state)
    {
        if (animator == null || !animator.isActiveAndEnabled) return false;
        if (animator.GetCurrentAnimatorStateInfo(0).IsName(state)) return true;
        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(state);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
