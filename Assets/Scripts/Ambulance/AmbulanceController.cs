using System;
using System.Collections.Generic;
using UnityEngine;

public enum AmbulancePhase { Approaching, Waiting, Departing }

// Ambulansa özel davranış (kullanıcı kararları 1–5, 12, 13, 17). Sürüşün kendisi LaneFollower'da.
//
// Durak sırası (bekleme süresi W, varsayılan 15 sn; t = duruştan beri geçen süre):
//   t=0        duruş → kapılar 100°'ye, basamak 90°'ye açılır (doorMoveTime = 0.8 sn)
//   t=0.8      yükleme açık
//   t=W−5      iki kısa korna
//   t=W−1      kapılar kapanmaya başlar, yükleme biter
//   t=W        kalkış (Departed olayı → görev, ambulansta olmayan hastaların sayacını artırır)
// Şeridin sonunda (TrafficDespawn_East) kaybolur, içindeki hastalar "Rescued" olur.
[RequireComponent(typeof(LaneFollower))]
public class AmbulanceController : MonoBehaviour
{
    [Header("Parçalar (modelden)")]
    public Transform doorLeft;
    public Transform doorRight;
    public Transform rearStep;
    [Tooltip("Arka kapıların 1.2 m gerisinde, yerde.")]
    public Transform loadPoint;
    [Tooltip("Hasta yatma çerçeveleri (PatientSlot_01, _02 altında). Kapasite = bu dizinin uzunluğu.")]
    public Transform[] patientSlots = new Transform[0];
    [Tooltip("Çakarlar: sol grup ve sağ grup karşılıklı yanıp söner.")]
    public Renderer[] flashLeft = new Renderer[0];
    public Renderer[] flashRight = new Renderer[0];

    [Header("Kapı ve basamak (ambulans kök uzayında eksen, işaretli açı)")]
    public Vector3 doorLeftAxis = Vector3.up;
    public float doorLeftOpenAngle = 100f;
    public Vector3 doorRightAxis = Vector3.up;
    public float doorRightOpenAngle = -100f;
    public Vector3 stepAxis = Vector3.right;
    public float stepOpenAngle = 90f;
    public float doorMoveTime = 0.8f;

    [Header("Durak")]
    public float hornBeforeDeparture = 5f;
    public float closeBeforeDeparture = 1f;
    [Tooltip("Hastalı sedyenin PatientAnchor'ı LoadPoint'e bu kadar yaklaşınca hasta otomatik yüklenir (m).")]
    public float loadRadius = 2.5f;
    [Tooltip("Hastanın sedyeden ambulanstaki yerine kayma süresi (sn).")]
    public float loadBlendTime = 0.5f;

    [Header("Öncelik (kullanıcı kararı 13)")]
    [Tooltip("Ön tampondan ileriye bakılan mesafe (m).")]
    public float lookAhead = 12f;
    [Tooltip("Önde taşıyıcı varken hız, seyir hızının bu oranına iner (durmaz).")]
    [Range(0.05f, 1f)] public float yieldSpeedFactor = 0.4f;
    [Tooltip("Gövde kutusu (kök uzayında): temas kontrolü ve uzunluk.")]
    public Vector3 bodyCenter = new Vector3(0f, 1.3f, 0f);
    public Vector3 bodySize = new Vector3(2f, 2.3f, 6f);
    [Tooltip("Temas eden oyuncu, şerit kenarından bu kadar dışarı itilir (m).")]
    public float knockClearance = 0.6f;
    public float knockControlLoss = 1f;

    [Header("Çakar")]
    public float flashHz = 2f;
    [ColorUsage(false, true)] public Color flashColor = new Color(0.1f, 0.35f, 1f) * 6f;

    public AmbulanceAudio sound;

    public LaneFollower Follower { get; private set; }
    public AmbulancePhase Phase { get; private set; } = AmbulancePhase.Approaching;
    public float WaitDuration { get; private set; } = 15f;
    public float WaitTime { get; private set; }
    public float WaitRemaining { get { return Mathf.Max(0f, WaitDuration - WaitTime); } }
    public bool LoadingOpen
    {
        get { return Phase == AmbulancePhase.Waiting && WaitTime >= doorMoveTime && WaitTime < WaitDuration - closeBeforeDeparture; }
    }
    public int Capacity { get { return patientSlots.Length; } }
    public IReadOnlyList<Patient> Passengers { get { return passengers; } }
    public float DoorOpenAmount { get; private set; }
    // Bu fizik adımında önde (şeritte) taşıyıcı var mıydı (hız düşürme + korna)
    public bool YieldingToCarrier { get; private set; }
    // Kökten arka tampona uzaklık (m)
    public float RearOverhang { get { return -(bodyCenter.z - bodySize.z * 0.5f); } }
    public float FrontOverhang { get { return bodyCenter.z + bodySize.z * 0.5f; } }

    public event Action<AmbulanceController> Arrived;
    public event Action<AmbulanceController> Departed;
    public event Action<AmbulanceController> Despawned;
    public event Action<AmbulanceController, Patient> PatientLoaded;

    private readonly List<Patient> passengers = new List<Patient>();
    private Quaternion closedLeft, closedRight, closedStep;
    private Vector3 axisLeftLocal, axisRightLocal, axisStepLocal;
    private bool hornDone, closingStarted;
    private readonly List<Material> leftMats = new List<Material>(), rightMats = new List<Material>();
    private readonly Dictionary<StretcherController, float> fullNoticeTimes = new Dictionary<StretcherController, float>();
    private static readonly Collider[] overlap = new Collider[16];

    private void Awake()
    {
        Follower = GetComponent<LaneFollower>();
        if (sound == null) sound = GetComponent<AmbulanceAudio>();
        CacheHinge(doorLeft, doorLeftAxis, out closedLeft, out axisLeftLocal);
        CacheHinge(doorRight, doorRightAxis, out closedRight, out axisRightLocal);
        CacheHinge(rearStep, stepAxis, out closedStep, out axisStepLocal);
        CollectFlashMaterials(flashLeft, leftMats);
        CollectFlashMaterials(flashRight, rightMats);
    }

    // Kök uzayındaki ekseni parçanın kendi yerel uzayına çevirir (FBX parçalarının eksenleri Blender'a göre olabilir).
    private void CacheHinge(Transform part, Vector3 rootAxis, out Quaternion closed, out Vector3 localAxis)
    {
        closed = part != null ? part.localRotation : Quaternion.identity;
        localAxis = part != null ? Quaternion.Inverse(part.rotation) * (transform.rotation * rootAxis.normalized) : Vector3.up;
    }

    private static void CollectFlashMaterials(Renderer[] renderers, List<Material> into)
    {
        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var m in r.materials) // örnek (instance) materyaller: modelin asıl materyali değişmez
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                into.Add(m);
            }
        }
    }

    // Görev bu ambulansı şeridin başına koyar. stopDistance: AmbulanceStop'un şeritteki s değeri.
    public void Begin(LanePath lane, float stopDistance, float waitDuration, float startSpeed)
    {
        if (Follower == null) Follower = GetComponent<LaneFollower>();
        WaitDuration = waitDuration;
        Phase = AmbulancePhase.Approaching;
        Follower.Place(lane, 0f, startSpeed);
        Follower.StopAt(stopDistance);
        SetDoors(0f);
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (Follower.lane == null) return;

        if (Phase == AmbulancePhase.Waiting)
        {
            YieldingToCarrier = false;
            WaitTime += dt;
            float closeStart = WaitDuration - closeBeforeDeparture;
            if (!hornDone && WaitTime >= WaitDuration - hornBeforeDeparture)
            {
                hornDone = true;
                if (sound != null) sound.PlayHorn();
            }
            if (!closingStarted && WaitTime >= closeStart)
            {
                closingStarted = true;
                if (sound != null) sound.PlayDoor();
            }
            float amount = WaitTime < closeStart
                ? Mathf.Clamp01(WaitTime / doorMoveTime)
                : Mathf.Clamp01(1f - (WaitTime - closeStart) / doorMoveTime);
            SetDoors(amount);
            if (LoadingOpen) TryLoadStretchers();

            if (WaitTime >= WaitDuration)
            {
                SetDoors(0f);
                Phase = AmbulancePhase.Departing;
                Follower.ClearStop();
                Departed?.Invoke(this);
            }
        }
        else
        {
            YieldingToCarrier = CarrierAhead();
            float limit = YieldingToCarrier ? Follower.cruiseSpeed * yieldSpeedFactor : float.PositiveInfinity;
            if (YieldingToCarrier && sound != null) sound.PlayHorn();
            Follower.Tick(dt, limit);
            PushPlayersOnContact();

            if (Phase == AmbulancePhase.Approaching && Follower.AtStop)
            {
                Phase = AmbulancePhase.Waiting;
                WaitTime = 0f;
                hornDone = false;
                closingStarted = false;
                if (sound != null) sound.PlayDoor();
                Arrived?.Invoke(this);
            }
            else if (Phase == AmbulancePhase.Departing && Follower.AtEnd)
            {
                Despawn();
                return;
            }
        }

        if (sound != null) sound.SetMotion(Phase != AmbulancePhase.Waiting, Follower.Speed);
    }

    private void Update()
    {
        // Çakarlar ambulans sahnedeyken hep yanıp söner: ~2 Hz, sol ve sağ karşılıklı.
        bool leftOn = Mathf.Repeat(Time.time * flashHz, 1f) < 0.5f;
        foreach (var m in leftMats) m.SetColor("_EmissionColor", leftOn ? flashColor : Color.black);
        foreach (var m in rightMats) m.SetColor("_EmissionColor", leftOn ? Color.black : flashColor);
    }

    // Kapı ve basamak açıklığı (0 = kapalı, 1 = açık).
    private void SetDoors(float amount)
    {
        DoorOpenAmount = amount;
        if (doorLeft != null) doorLeft.localRotation = closedLeft * Quaternion.AngleAxis(doorLeftOpenAngle * amount, axisLeftLocal);
        if (doorRight != null) doorRight.localRotation = closedRight * Quaternion.AngleAxis(doorRightOpenAngle * amount, axisRightLocal);
        if (rearStep != null) rearStep.localRotation = closedStep * Quaternion.AngleAxis(stepOpenAngle * amount, axisStepLocal);
    }

    public float DoorLeftAngle { get { return doorLeft != null ? Quaternion.Angle(closedLeft, doorLeft.localRotation) : 0f; } }
    public float DoorRightAngle { get { return doorRight != null ? Quaternion.Angle(closedRight, doorRight.localRotation) : 0f; } }
    public float StepAngle { get { return rearStep != null ? Quaternion.Angle(closedStep, rearStep.localRotation) : 0f; } }

    // Ambulansa yükleme (kullanıcı kararı 12).
    private void TryLoadStretchers()
    {
        foreach (var s in StretcherController.All)
        {
            if (s == null || !s.IsRaised || s.Passenger == null) continue;
            if (Vector3.Distance(s.patientAnchor.position, loadPoint.position) > loadRadius) continue;
            if (passengers.Count >= Capacity)
            {
                // Dolu: uyarıyı sedye menzildeyken aralıklarla tekrarla
                if (!fullNoticeTimes.TryGetValue(s, out float last) || Time.time - last > 2f)
                {
                    fullNoticeTimes[s] = Time.time;
                    GameNotice.Show("Ambulans dolu");
                }
                continue;
            }
            var p = s.TakePatient();
            p.LieOn(patientSlots[passengers.Count], PatientState.InAmbulance, loadBlendTime);
            passengers.Add(p);
            PatientLoaded?.Invoke(this, p);
        }
    }

    // Ön tampondan lookAhead metre içinde, şerit genişliğinde taşıyıcı (oyuncu ya da bot) var mı?
    private bool CarrierAhead()
    {
        var lane = Follower.lane;
        float front = Follower.Distance + FrontOverhang;
        foreach (var c in CarrierRegistry.All)
        {
            if (c == null || c.Root == null) continue;
            Vector3 p = c.Root.position;
            float s = lane.Project(p, out float lateral, out Vector3 onLane);
            if (Mathf.Abs(p.y - onLane.y) > 2.5f) continue;              // başka kotta (ör. aşağıdaki patika)
            if (Mathf.Abs(lateral) > lane.width * 0.5f) continue;        // şeridin dışında
            float ahead = s - front;
            if (ahead >= -0.2f && ahead <= lookAhead) return true;
        }
        return false;
    }

    // Gövdeye değen oyuncu şeritten dışarı itilir ve kısa süre kontrol kaybeder (yere düşme ayrı iş).
    private void PushPlayersOnContact()
    {
        Vector3 center = transform.TransformPoint(bodyCenter);
        int n = Physics.OverlapBoxNonAlloc(center, bodySize * 0.5f + Vector3.one * 0.1f, overlap, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var player = overlap[i].GetComponent<ThirdPersonPlayer>();
            if (player == null || !player.HasControl) continue;
            var lane = Follower.lane;
            float s = lane.Project(player.transform.position, out float lateral, out _);
            // Gövdenin sağ yarısının dışındaysa sağa (banket), değilse sola (karşı şerit, açık yol) itilir.
            float side = lateral > bodySize.x * 0.5f ? 1f : -1f;
            float target = side * (lane.width * 0.5f + knockClearance);
            player.KnockAside(lane.RightAt(s) * (target - lateral), knockControlLoss);
        }
    }

    private void Despawn()
    {
        foreach (var p in passengers)
            if (p != null) p.MarkRescued();
        passengers.Clear();
        Despawned?.Invoke(this);
        Destroy(gameObject);
    }
}
