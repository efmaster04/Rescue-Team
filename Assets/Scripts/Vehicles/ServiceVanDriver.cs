using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// Servis minibüsü sürüşü (kullanıcı kararı 2026-11-04: Bölüm 1, ekip minibüsün içinde yolda başlar).
//
// Durumlar:
//  - Driving: Rigidbody dinamik, yerçekimi kapalı; hız ve yön kodla verilir (arcade), yükseklik ve eğim dört tekerlek
//    noktasındaki zemin raycast'inden (süspansiyon). Hız her adımda fizikten geri okunur: korkuluk / kaya gibi statik engeller
//    aracı gerçekten durdurur, ani hız düşüşü hasar sayılır. W/S gaz / fren-geri, A/D direksiyon, E (dururken) iniş, V kamera.
//  - Parked: Rigidbody kinematik, NavMeshObstacle oyar (hırsız ve bot yolu minibüsün GERÇEK yerine göre); yük bölümündeki
//    ekipman serbest fizikte (bölümün geri kalanı park hâlindeki minibüsü bekler).
//  - Shoved: trafik aracı çarptı (ILaneObstacle.Ram): dinamik, girdi yok, itme hızı söner; durunca yeniden Parked.
// Yük: sürerken ve itilirken yük bölümündeki Rigidbody'ler (sedye, koni) ve dolaptaki kangal minibüse bağlanır, kinematik olur;
// durunca eski ebeveyn ve fizik durumu geri gelir (düşmez, titremez).
// Ekip: oturunca oyuncu ve bot koltuk objelerine bağlanır, hareket / etkileşim bileşenleri kapanır, Animator "Seated" parametresi
// (1 şoför, 2 yolcu) oturma döngüsünü seçer; inince serbest noktalara ışınlanır, bileşenler açılır.
// Şerit: ILaneObstacle olarak LaneOccupancy'ye kayıtlıdır; ayak izi bir şeridin şeridine giriyorsa o şeridin trafiği durur,
// korna çalar, 5 sn sonra çarpar (TrafficVehicle). Çarpma hasar ekler (Damage → FieldPayout.vanDamage → vehicleWear).
[DefaultExecutionOrder(-20)]
public class ServiceVanDriver : MonoBehaviour, ILaneObstacle
{
    public enum VanState { Parked, Driving, Shoved }

    public const string ParamSeated = "Seated";
    public const string StateDriveSeated = "Drive_Seated";
    public const string StatePassengerSeated = "Passenger_Seated";
    public const string NoExitWhileMovingText = "Durmadan inilmez";

    public static ServiceVanDriver Instance { get; private set; }

    [Header("Gövde (kök-yerel kapalı gövde kutusu; Bolum1ServiceVan.LocalBounds)")]
    public Vector3 bodyMin = new Vector3(-1.3f, 0f, -3f);
    public Vector3 bodyMax = new Vector3(1.3f, 2.7f, 3.4f);
    [Tooltip("Tekerlek izdüşümleri (kök-yerel), zemin raycast'i buradan.")]
    public Vector3[] wheelPointsLocal = new Vector3[0];
    [Tooltip("Yük bölümü kutusu (kök-yerel): sürerken içindeki Rigidbody'ler bağlanır.")]
    public Vector3 cargoMin = new Vector3(-1f, 0.55f, -0.75f);
    public Vector3 cargoMax = new Vector3(1f, 2.55f, 2.9f);

    [Header("Sürüş")]
    public float maxSpeed = 14f;
    public float maxReverseSpeed = 4f;
    public float acceleration = 4.5f;
    public float brakeDecel = 9f;
    public float coastDecel = 1.5f;
    [Tooltip("Tam direksiyonda dönüş hızı (°/sn, steerSpeedRef hızında).")]
    public float maxSteerDegPerSec = 55f;
    public float steerSpeedRef = 6f;
    [Tooltip("Yanal kayma sönümü (m/sn²): sürerken lastik tutuşu, itilirken daha az.")]
    public float lateralGripDecel = 8f, shovedLateralDecel = 2.5f, shovedForwardDecel = 3f;
    [Tooltip("Zemin düzeltmesi: tekerlek noktası ile zemin farkının saniyede kapatılan oranı.")]
    public float groundGain = 10f;
    public float maxGroundVelocity = 3f;
    public float maxTurnRate = 240f;

    [Header("Hasar")]
    [Tooltip("Kontrol hızından bu kadar (m/sn) ani sapma çarpma sayılır.")]
    public float impactSpeedThreshold = 2.5f;
    [Tooltip("Eşiğin üstündeki her m/sn için hasar (%).")]
    public float damagePerImpactSpeed = 2f;
    public float maxDamage = 100f;

    [Header("Bağlantılar")]
    public ThirdPersonPlayer player;
    public StretcherHelperBot bot;
    public Transform driverSeat, passengerSeat;
    public OrbitCamera orbitCamera;
    public float driveCameraDistance = 7.5f;
    public float driveCameraPivotHeight = 1.4f;
    public ParkingZone parkingZone;
    public NavMeshObstacle navObstacle;
    public VehicleAudio sound;
    [Tooltip("Direksiyon simidi (şoför koltuğunun altında; direksiyon girdisiyle döner).")]
    public Transform steeringWheel;
    public float steeringWheelTurnDeg = 45f;
    [Tooltip("Zemin sayılan kökler (SM_B1_Ground, SM_B1_Road); boşsa minibüs dışı her statik collider.")]
    public Transform[] groundRoots = new Transform[0];

    public VanState State { get; private set; } = VanState.Parked;
    public float Speed { get; private set; }           // ileri hız (m/sn, geri negatif)
    public float Damage { get; private set; }          // birikmiş hasar (%)
    public int Rams { get; private set; }
    public int Impacts { get; private set; }
    public bool CrewInside { get; private set; }
    public bool HasExited { get; private set; }
    public bool GoodParking { get; private set; }
    public float Throttle { get; private set; }
    public float Steer { get; private set; }
    public float MaxSpeedReached { get; private set; }
    public int HeldCargoCount { get { return held.Count; } }
    public bool CargoHeld { get { return held.Count > 0; } }
    public Vector3 Forward { get { return -transform.forward; } } // kök +Z arka (garaj araç uzayı)
    public Rigidbody Body { get { return rb; } }
    public Vector3 LastExitPoint { get; private set; }
    public Vector3 LastBotExitPoint { get; private set; }

    public event Action<ServiceVanDriver> Exited;
    public event Action<ServiceVanDriver, TrafficVehicle> Rammed;
    public event Action<ServiceVanDriver> Settled;

    private sealed class HeldItem
    {
        public Transform t, parent;
        public Rigidbody rb;
        public bool kinematic;
        public RigidbodyInterpolation interpolation;
        // Bağlıyken kapatılan collider'lar: kinematik çocuk gövde ile dinamik minibüs arasında dinlenme teması (raftaki koni) minibüsü itmesin
        public readonly List<Collider> colliders = new List<Collider>();
    }

    private sealed class SeatedCrew
    {
        public Transform root, parent;
        public Animator animator;
        public readonly List<Behaviour> disabled = new List<Behaviour>();
        public readonly List<Collider> disabledColliders = new List<Collider>();
    }

    private Rigidbody rb;
    private readonly List<HeldItem> held = new List<HeldItem>();
    private readonly List<SeatedCrew> seated = new List<SeatedCrew>();
    private Vector3 lateral;
    private Vector3 groundUp = Vector3.up;
    private float lastRamContact = -100f, lastRamDamage = -100f;
    private TrafficVehicle lastRammer;
    private float savedDistance, savedPivot;
    private float lastHeadingYaw;
    private bool testInput;
    private float testThrottle, testSteer;
    private InputAction moveAction, lookAction, interactAction, viewAction;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private float settleSince = -1f;
    private Quaternion wheelBaseRotation = Quaternion.identity;

    private void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.centerOfMass = new Vector3(0f, 0.6f, 0.2f);
        rb.isKinematic = true;
        if (navObstacle == null) navObstacle = GetComponent<NavMeshObstacle>();
        if (sound == null) sound = GetComponent<VehicleAudio>();
        if (steeringWheel != null) wheelBaseRotation = steeringWheel.localRotation;
        if (groundRoots == null || groundRoots.Length == 0)
        {
            var list = new List<Transform>();
            foreach (var n in new[] { "SM_B1_Ground", "SM_B1_Road" }) { var g = GameObject.Find(n); if (g != null) list.Add(g.transform); }
            groundRoots = list.ToArray();
        }
        moveAction = new InputAction("VanMove", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        lookAction = new InputAction("VanLook", InputActionType.Value, "<Mouse>/delta");
        interactAction = new InputAction("VanExit", InputActionType.Button, "<Keyboard>/e");
        viewAction = new InputAction("VanView", InputActionType.Button, PlayerInputReader.ViewTogglePath);
    }

    private void OnEnable() { LaneOccupancy.RegisterObstacle(this); if (Instance == null) Instance = this; }

    private void Start()
    {
        // Park hâlinde ve ekip dışarıdaysa (sürüş girişi atlandı) motor sesi çalmaz
        if (State == VanState.Parked && !CrewInside && sound != null && sound.engine != null) sound.engine.Stop();
        // Oturtma Awake'te yapıldı; başka bileşenlerin Awake'inde sonradan eklenenler (NpcBody gövde kutusu) da kapatılır
        foreach (var crew in seated) DisableForSeat(crew);
    }

    private static void DisableForSeat(SeatedCrew crew)
    {
        if (crew.root == null) return;
        // Başka bileşenlerin Awake'i (PlayerKnockdown.SetRagdoll(false) CharacterController'ı açar) kapattığımızı yeniden açabilir:
        // listede olsa da açık bulunan yeniden kapatılır.
        foreach (var b in crew.root.GetComponents<Behaviour>())
        {
            if (b is Animator || b is FirstPersonBody || b is PlayerHealth || b is PlayerFallRespawn || b is ServiceVanDriver) continue;
            if (!b.enabled) continue;
            b.enabled = false;
            if (!crew.disabled.Contains(b)) crew.disabled.Add(b);
        }
        // Kök ve alt collider'lar (CharacterController, NpcBody gövdesi; ragdoll zaten kapalı): minibüsün içinde çarpışmasın
        foreach (var c in crew.root.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            c.enabled = false;
            if (!crew.disabledColliders.Contains(c)) crew.disabledColliders.Add(c);
        }
    }

    // Gövde kutusuyla kesişen yabancı collider'lar (tanı ve başlangıç yeri seçimi): kendi hiyerarşisi, tetikleyiciler, zemin hariç.
    public List<string> OverlapReport()
    {
        var result = new List<string>();
        Vector3 center = transform.TransformPoint(new Vector3((bodyMin.x + bodyMax.x) * .5f, (bodyMin.y + .15f + bodyMax.y) * .5f, (bodyMin.z + bodyMax.z) * .5f));
        Vector3 half = new Vector3((bodyMax.x - bodyMin.x) * .5f, (bodyMax.y - bodyMin.y - .15f) * .5f, (bodyMax.z - bodyMin.z) * .5f);
        foreach (var c in Physics.OverlapBox(center, half, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c.transform.IsChildOf(transform)) continue;
            bool ground = false;
            foreach (var r in groundRoots) if (r != null && c.transform.IsChildOf(r)) ground = true;
            if (ground) continue;
            result.Add(c.name + (c.attachedRigidbody != null ? "(rb)" : ""));
        }
        return result;
    }
    private void OnDisable() { LaneOccupancy.UnregisterObstacle(this); DisableInput(); }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        moveAction?.Dispose(); lookAction?.Dispose(); interactAction?.Dispose(); viewAction?.Dispose();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    // ---------------------------------------------------------------------------------------------
    // Durum geçişleri
    // ---------------------------------------------------------------------------------------------

    public void SetDriving()
    {
        HoldCargo();
        if (navObstacle != null) navObstacle.enabled = false;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        State = VanState.Driving;
        settleSince = -1f;
        if (sound != null && sound.engine != null && !sound.engine.isPlaying) sound.engine.Play();
    }

    public void SetParked()
    {
        Speed = 0f;
        lateral = Vector3.zero;
        if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        rb.isKinematic = true;
        State = VanState.Parked;
        ReleaseCargo();
        if (navObstacle != null) navObstacle.enabled = true;
        if (sound != null) { sound.SetSpeed(0f); if (!CrewInside && sound.engine != null) sound.engine.Stop(); }
        Physics.SyncTransforms();
    }

    private void BeginShove()
    {
        HoldCargo();
        if (navObstacle != null) navObstacle.enabled = false;
        rb.isKinematic = false;
        State = VanState.Shoved;
        settleSince = -1f;
    }

    // Işınlama (sürüş girişi başlangıcı ve testler): yük bağlı taşınır, zemine oturtulur, verilen durumda bırakılır.
    public void TeleportTo(Vector3 position, Quaternion rotation, bool parked)
    {
        HoldCargo();
        bool wasKinematic = rb.isKinematic;
        rb.isKinematic = true;
        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation;
        Physics.SyncTransforms();
        AlignToGroundNow();
        lateral = Vector3.zero;
        Speed = 0f;
        rb.isKinematic = wasKinematic;
        if (parked) SetParked(); else SetDriving();
    }

    // Dört tekerlek noktasındaki zemin düzlemine hemen oturtur (yaw korunur).
    public void AlignToGroundNow()
    {
        if (!GroundPlane(out Vector3 up, out float lift)) return;
        Vector3 rear = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (rear.sqrMagnitude < 1e-6f) return;
        var rot = Quaternion.LookRotation(rear, up);
        var pos = transform.position + Vector3.up * lift;
        transform.SetPositionAndRotation(pos, rot);
        rb.position = pos;
        rb.rotation = rot;
        groundUp = up;
        Physics.SyncTransforms();
        if (GroundPlane(out _, out float lift2)) { transform.position += Vector3.up * lift2; rb.position = transform.position; Physics.SyncTransforms(); }
    }

    // ---------------------------------------------------------------------------------------------
    // Girdi
    // ---------------------------------------------------------------------------------------------

    public void SetTestInput(float throttle, float steer) { testInput = true; testThrottle = Mathf.Clamp(throttle, -1f, 1f); testSteer = Mathf.Clamp(steer, -1f, 1f); }
    public void ClearTestInput() { testInput = false; testThrottle = 0f; testSteer = 0f; }

    private void EnableInput() { moveAction.Enable(); lookAction.Enable(); interactAction.Enable(); viewAction.Enable(); }
    private void DisableInput()
    {
        if (moveAction == null) return;
        moveAction.Disable(); lookAction.Disable(); interactAction.Disable(); viewAction.Disable();
    }

    private void Update()
    {
        if (!CrewInside) return;
        if (testInput) { Throttle = testThrottle; Steer = testSteer; }
        else
        {
            Vector2 m = moveAction.ReadValue<Vector2>();
            Throttle = m.y;
            Steer = m.x;
            if (orbitCamera != null)
            {
                orbitCamera.Rotate(lookAction.ReadValue<Vector2>());
                if (viewAction.WasPressedThisFrame()) orbitCamera.ToggleMode();
            }
            if (interactAction.WasPressedThisFrame()) TryExit();
        }
        // Kamera minibüsle birlikte döner (fare yine yörüngeyi oynatır)
        if (orbitCamera != null)
        {
            float yaw = HeadingYaw;
            orbitCamera.Yaw += Mathf.DeltaAngle(lastHeadingYaw, yaw);
            lastHeadingYaw = yaw;
        }
        // Direksiyon simidi: sağa çevirince şoförün gördüğü yönde saat yönünde döner
        if (steeringWheel != null) steeringWheel.localRotation = wheelBaseRotation * Quaternion.AngleAxis(-Steer * steeringWheelTurnDeg, Vector3.up);
    }

    public float HeadingYaw { get { Vector3 f = Forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up).eulerAngles.y : 0f; } }

    // ---------------------------------------------------------------------------------------------
    // Fizik adımı
    // ---------------------------------------------------------------------------------------------

    private bool firstStepDone;

    private void FixedUpdate()
    {
        if (!firstStepDone) { firstStepDone = true; foreach (var crew in seated) DisableForSeat(crew); } // Start'ta açılanlar da kapanır
        if (State == VanState.Parked) return;
        float dt = Time.fixedDeltaTime;
        Vector3 fwdFlat = Forward; fwdFlat.y = 0f; fwdFlat.Normalize();
        Vector3 hv = rb.linearVelocity; hv.y = 0f;
        float actual = Vector3.Dot(hv, fwdFlat);
        Vector3 lateralActual = hv - fwdFlat * actual;

        // Çarpma: kontrol hızından ani sapma (statik engel); trafik çarpması ayrıca hasar yazar (Ram)
        float deviation = Mathf.Abs(actual - Speed);
        if (State == VanState.Driving && deviation > impactSpeedThreshold && Time.time - lastRamContact > 1f)
        {
            Impacts++;
            AddDamage((deviation - impactSpeedThreshold) * damagePerImpactSpeed);
        }
        Speed = actual;
        lateral = lateralActual;

        float yawRate = 0f;
        if (State == VanState.Driving)
        {
            float t = CrewInside ? Throttle : 0f;
            float s = CrewInside ? Steer : 0f;
            if (t > 0.01f)
            {
                if (Speed < -0.1f) Speed = Mathf.MoveTowards(Speed, 0f, brakeDecel * dt);
                else Speed = Mathf.MoveTowards(Speed, maxSpeed * t, acceleration * dt);
            }
            else if (t < -0.01f)
            {
                if (Speed > 0.1f) Speed = Mathf.MoveTowards(Speed, 0f, brakeDecel * dt);
                else Speed = Mathf.MoveTowards(Speed, maxReverseSpeed * t, acceleration * 0.7f * dt);
            }
            else Speed = Mathf.MoveTowards(Speed, 0f, coastDecel * dt);
            float speedFactor = Mathf.Clamp(Mathf.Abs(Speed) / Mathf.Max(0.1f, steerSpeedRef), 0.3f, 1f);
            yawRate = Mathf.Abs(Speed) < 0.05f ? 0f : s * maxSteerDegPerSec * speedFactor * Mathf.Sign(Speed);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero, lateralGripDecel * dt);
        }
        else
        {
            Speed = Mathf.MoveTowards(Speed, 0f, shovedForwardDecel * dt);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero, shovedLateralDecel * dt);
        }
        MaxSpeedReached = Mathf.Max(MaxSpeedReached, Mathf.Abs(Speed));

        // Zemin: tekerlek noktalarından düzlem; yoksa son eğim ve yükseklik korunur
        float liftVel = 0f;
        if (GroundPlane(out Vector3 up, out float lift))
        {
            groundUp = up;
            liftVel = Mathf.Clamp(lift * groundGain, -maxGroundVelocity, maxGroundVelocity);
        }
        Vector3 heading = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * fwdFlat;
        Vector3 headingOnGround = Vector3.ProjectOnPlane(heading, groundUp).normalized;
        Vector3 rear = -headingOnGround;
        Quaternion targetRot = Quaternion.LookRotation(rear, groundUp);

        rb.linearVelocity = headingOnGround * Speed + lateral + Vector3.up * liftVel;
        Quaternion delta = targetRot * Quaternion.Inverse(rb.rotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f) rb.angularVelocity = Vector3.zero;
        else rb.angularVelocity = axis.normalized * Mathf.Clamp(angle / dt, -maxTurnRate, maxTurnRate) * Mathf.Deg2Rad;

        if (sound != null) sound.SetSpeed(Mathf.Abs(Speed));

        // İtilme bitti mi: yavaşladı ve 1 sn'dir temas yok → park (kinematik, ekipman serbest)
        if (State == VanState.Shoved)
        {
            bool slow = hv.magnitude < 0.15f && Time.time - lastRamContact > 1f;
            if (slow) { if (settleSince < 0f) settleSince = Time.time; }
            else settleSince = -1f;
            if (settleSince >= 0f && Time.time - settleSince > 0.5f)
            {
                SetParked();
                if (parkingZone != null) GoodParking = parkingZone.Evaluate(Footprint());
                Settled?.Invoke(this);
            }
        }
        // Sürerken (ekip içerideyken) oyuncunun kendisi değil ama dışarıdaki oyuncu itilen minibüse çarpılabilir
        if (State == VanState.Shoved && player != null && player.isActiveAndEnabled && hv.magnitude > 1f && InsideFootprint(player.transform.position, 0.4f))
            player.KnockDown(hv + Vector3.up * 1.5f);
    }

    private void AddDamage(float amount)
    {
        if (amount <= 0f) return;
        Damage = Mathf.Min(maxDamage, Damage + amount);
    }

    // Dört tekerlek noktasının altındaki zeminden: yukarı vektör ve ortalama yükseklik düzeltmesi (+ = yukarı kaldır).
    private bool GroundPlane(out Vector3 up, out float lift)
    {
        up = groundUp; lift = 0f;
        if (wheelPointsLocal == null || wheelPointsLocal.Length < 4) return false;
        var ground = new Vector3[4];
        var world = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            world[i] = transform.TransformPoint(wheelPointsLocal[i]);
            if (!GroundBelow(world[i], out RaycastHit hit)) return false;
            ground[i] = hit.point;
        }
        Vector3 frontMid = (ground[0] + ground[1]) * .5f, rearMid = (ground[2] + ground[3]) * .5f;
        Vector3 leftMid = (ground[0] + ground[2]) * .5f, rightMid = (ground[1] + ground[3]) * .5f;
        Vector3 rearDir = (rearMid - frontMid).normalized;
        Vector3 n = Vector3.Cross(rearDir, leftMid - rightMid).normalized; // +Z arka, +X sol → yukarı = Z × X
        if (n.sqrMagnitude < 1e-6f) return false;
        if (n.y < 0f) n = -n;
        up = n;
        for (int i = 0; i < 4; i++) lift += ground[i].y - world[i].y;
        lift /= 4f;
        return true;
    }

    // Tekerlek noktasının 1 m üstünden 1.5 m altına kadar bakılır: yol kenarını aşan tekerlek, uçurum yamacındaki uzak zemini
    // "zemin" sanmasın (ölçüldü: 8 m'lik ışın vadi yamacını bulup düzlemi deviriyor, minibüs yola gömülüp kilitleniyordu).
    private bool GroundBelow(Vector3 point, out RaycastHit ground)
    {
        int n = Physics.RaycastNonAlloc(point + Vector3.up * 1f, Vector3.down, hits, 2.5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        ground = default;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.distance >= best || h.collider.transform.IsChildOf(transform)) continue;
            if (groundRoots != null && groundRoots.Length > 0)
            {
                bool ok = false;
                foreach (var r in groundRoots) if (r != null && h.collider.transform.IsChildOf(r)) { ok = true; break; }
                if (!ok) continue;
            }
            else if (GroundProbe.IsMovingGameObject(h.collider)) continue;
            best = h.distance;
            ground = h;
        }
        return best < float.MaxValue;
    }

    // ---------------------------------------------------------------------------------------------
    // Ayak izi ve şerit (ILaneObstacle)
    // ---------------------------------------------------------------------------------------------

    // Gövde köşeleri, taban yüksekliğinde (dünya).
    public Vector3[] Footprint()
    {
        return new[]
        {
            transform.TransformPoint(new Vector3(bodyMin.x, bodyMin.y, bodyMin.z)),
            transform.TransformPoint(new Vector3(bodyMax.x, bodyMin.y, bodyMin.z)),
            transform.TransformPoint(new Vector3(bodyMax.x, bodyMin.y, bodyMax.z)),
            transform.TransformPoint(new Vector3(bodyMin.x, bodyMin.y, bodyMax.z)),
        };
    }

    // Şerit kontrolü için 9 örnek (köşeler, kenar ortaları, merkez), gövde orta yüksekliğinde.
    public Vector3[] LaneSamples()
    {
        float y = bodyMin.y + 0.5f;
        var list = new List<Vector3>(9);
        foreach (float x in new[] { bodyMin.x, (bodyMin.x + bodyMax.x) * .5f, bodyMax.x })
            foreach (float z in new[] { bodyMin.z, (bodyMin.z + bodyMax.z) * .5f, bodyMax.z })
                list.Add(transform.TransformPoint(new Vector3(x, y, z)));
        return list.ToArray();
    }

    public bool InsideFootprint(Vector3 world, float margin)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return l.x >= bodyMin.x - margin && l.x <= bodyMax.x + margin && l.z >= bodyMin.z - margin && l.z <= bodyMax.z + margin
            && l.y >= bodyMin.y - 0.5f && l.y <= bodyMax.y + 0.5f;
    }

    public bool BlocksLane(LanePath lane, out float edgeS, out float farS, out float speedAlong)
    {
        edgeS = float.PositiveInfinity; farS = float.NegativeInfinity; speedAlong = 0f;
        if (lane == null || !isActiveAndEnabled) return false;
        float half = lane.width * 0.5f;
        bool any = false;
        foreach (var p in LaneSamples())
        {
            float s = lane.Project(p, out float lat, out Vector3 on);
            if (Mathf.Abs(p.y - on.y) > 3f) continue; // başka kot (patika)
            if (Mathf.Abs(lat) > half) continue;
            any = true;
            edgeS = Mathf.Min(edgeS, s);
            farS = Mathf.Max(farS, s);
        }
        if (!any) return false;
        Vector3 v = rb != null && !rb.isKinematic ? rb.linearVelocity : Vector3.zero;
        speedAlong = Vector3.Dot(v, lane.TangentAt(edgeS));
        return true;
    }

    // Trafik aracı bekleme süresi dolunca ilerliyor: gövdesi ayak izine değince hasar + itme (araç başına 1.5 sn'de bir).
    public void Ram(TrafficVehicle by)
    {
        if (by == null || by.settings == null) return;
        if (State == VanState.Parked) BeginShove();
        bool touch = false;
        foreach (var p in LaneSamples()) if (by.Touches(p, 0.45f)) { touch = true; break; }
        if (!touch) return;
        lastRamContact = Time.time;
        settleSince = -1f;
        Vector3 carFwd = by.transform.forward; carFwd.y = 0f; carFwd.Normalize();
        Vector3 fwdFlat = Forward; fwdFlat.y = 0f; fwdFlat.Normalize();
        Vector3 away = Vector3.zero;
        float clearSpeed = 0f;
        if (by.Follower != null && by.Follower.lane != null)
        {
            var lane = by.Follower.lane;
            float s = lane.Project(transform.position, out float lat, out _);
            away = lane.RightAt(s) * (lat >= 0f ? 1f : -1f);
            // Şeridi boşaltacak yanal yol: şerit yarı genişliği + gövde yarı genişliği − mevcut yanal + pay; sönümle kat edilecek hız
            float halfBody = (bodyMax.x - bodyMin.x) * 0.5f;
            float needed = Mathf.Clamp(lane.width * 0.5f + halfBody - Mathf.Abs(lat) + 0.3f, 1f, 4f);
            clearSpeed = Mathf.Max(1.5f, Mathf.Sqrt(2f * shovedLateralDecel * needed));
        }
        // Temas sürdükçe şeridin dışına doğru yanal hız korunur (yoksa araç minibüsü şerit boyunca önüne katıp götürüyordu: ölçüldü 49 m)
        if (away != Vector3.zero)
        {
            Vector3 awayFlat = away - fwdFlat * Vector3.Dot(away, fwdFlat);
            if (awayFlat.sqrMagnitude > 1e-4f)
            {
                awayFlat.Normalize();
                float have = Vector3.Dot(lateral, awayFlat);
                if (have < clearSpeed) lateral += awayFlat * (clearSpeed - have);
            }
        }
        // Hasar ve ilk itki: aynı aracın bir çarpma döngüsünde bir kez (temas sürerken tekrar yazılmaz)
        if (lastRammer != by || Time.time - lastRamDamage >= by.settings.vanRamMaxSeconds)
        {
            lastRamDamage = Time.time;
            lastRammer = by;
            Rams++;
            AddDamage(by.settings.vanRamDamage);
            Vector3 push = carFwd * by.settings.vanRamPushSpeed;
            float along = Vector3.Dot(push, fwdFlat);
            Speed += along;
            lateral += push - fwdFlat * along;
            Rammed?.Invoke(this, by);
        }
        if (!rb.isKinematic) rb.linearVelocity = fwdFlat * Speed + lateral + Vector3.up * rb.linearVelocity.y;
    }

    // ---------------------------------------------------------------------------------------------
    // Yük bağlama
    // ---------------------------------------------------------------------------------------------

    private bool InCargo(Vector3 world)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return l.x >= cargoMin.x && l.x <= cargoMax.x && l.y >= cargoMin.y && l.y <= cargoMax.y && l.z >= cargoMin.z && l.z <= cargoMax.z;
    }

    private static bool IsCrewOrCharacter(Component c)
    {
        return c.GetComponentInParent<ThirdPersonPlayer>() != null || c.GetComponentInParent<StretcherHelperBot>() != null
            || c.GetComponentInParent<Patient>() != null || c.GetComponentInParent<LaneFollower>() != null || c.GetComponentInParent<Thief>() != null;
    }

    public void HoldCargo()
    {
        if (held.Count > 0) return;
        foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body == null || body == rb || body.transform.IsChildOf(transform) || IsCrewOrCharacter(body)) continue;
            if (!InCargo(body.position)) continue;
            var stretcher = body.GetComponent<StretcherController>();
            if (stretcher != null && stretcher.CarrierCount > 0) continue;
            held.Add(new HeldItem { t = body.transform, parent = body.transform.parent, rb = body, kinematic = body.isKinematic, interpolation = body.interpolation });
        }
        // Kangal (Rigidbody yok): Awake sırasında RopeCoil.All henüz dolmamış olabilir, sahneden aranır
        foreach (var coil in FindObjectsByType<RopeCoil>(FindObjectsSortMode.None))
        {
            if (coil == null || !coil.InLocker || coil.transform.IsChildOf(transform) || coil.GetComponent<Rigidbody>() != null || !InCargo(coil.transform.position)) continue;
            held.Add(new HeldItem { t = coil.transform, parent = coil.transform.parent });
        }
        foreach (var h in held)
        {
            if (h.rb != null)
            {
                if (!h.rb.isKinematic) { h.rb.linearVelocity = Vector3.zero; h.rb.angularVelocity = Vector3.zero; }
                h.rb.isKinematic = true;
                h.rb.interpolation = RigidbodyInterpolation.None;
                foreach (var c in h.rb.GetComponentsInChildren<Collider>())
                    if (c.enabled && c.attachedRigidbody == h.rb) { c.enabled = false; h.colliders.Add(c); }
            }
            h.t.SetParent(transform, true);
        }
    }

    public void ReleaseCargo()
    {
        foreach (var h in held)
        {
            if (h.t == null) continue;
            h.t.SetParent(h.parent, true);
            foreach (var c in h.colliders) if (c != null) c.enabled = true;
            if (h.rb != null)
            {
                h.rb.isKinematic = h.kinematic;
                h.rb.interpolation = h.interpolation;
                if (!h.kinematic) { h.rb.linearVelocity = Vector3.zero; h.rb.angularVelocity = Vector3.zero; h.rb.WakeUp(); }
            }
        }
        held.Clear();
    }

    // ---------------------------------------------------------------------------------------------
    // Ekip: oturma ve inme
    // ---------------------------------------------------------------------------------------------

    public void SeatCrew()
    {
        if (CrewInside) return;
        CrewInside = true;
        HasExited = false;
        if (player != null) Seat(player.transform, driverSeat, 1);
        if (bot != null) Seat(bot.transform, passengerSeat, 2);
        if (orbitCamera != null)
        {
            savedDistance = orbitCamera.distance;
            savedPivot = orbitCamera.pivotHeight;
            orbitCamera.distance = driveCameraDistance;
            orbitCamera.pivotHeight = driveCameraPivotHeight;
            orbitCamera.obstructionIgnoreRoot = transform;
            orbitCamera.Yaw = HeadingYaw;
            lastHeadingYaw = HeadingYaw;
        }
        if (!testInput) EnableInput();
    }

    private void Seat(Transform root, Transform seat, int seatedValue)
    {
        var crew = new SeatedCrew { root = root, parent = root.parent, animator = root.GetComponent<Animator>() };
        DisableForSeat(crew);
        if (seat != null)
        {
            root.SetParent(seat, true);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
        }
        else root.SetParent(transform, true);
        SetSeated(crew.animator, seatedValue);
        seated.Add(crew);
    }

    private static void SetSeated(Animator a, int value)
    {
        if (a == null || a.runtimeAnimatorController == null) return;
        foreach (var p in a.parameters)
            if (p.name == ParamSeated && p.type == AnimatorControllerParameterType.Int) { a.SetInteger(ParamSeated, value); return; }
    }

    // E: yalnız dururken. Hareket hâlindeyse uyarı.
    public bool TryExit()
    {
        if (!CrewInside) return false;
        Vector3 hv = rb.isKinematic ? Vector3.zero : rb.linearVelocity; hv.y = 0f;
        if (State != VanState.Driving || Mathf.Abs(Speed) > 0.3f || hv.magnitude > 0.3f)
        {
            GameNotice.Show(NoExitWhileMovingText);
            return false;
        }
        Exit();
        return true;
    }

    public void Exit()
    {
        if (!CrewInside) return;
        Throttle = 0f; Steer = 0f;
        ClearTestInput();
        DisableInput();
        AlignToGroundNow();
        SetParked();
        UnseatCrew();
        CrewInside = false;
        HasExited = true;
        GoodParking = parkingZone != null && parkingZone.Evaluate(Footprint());
        if (sound != null && sound.engine != null) sound.engine.Stop();
        Exited?.Invoke(this);
    }

    private void UnseatCrew()
    {
        var used = new List<Vector3>();
        foreach (var crew in seated)
        {
            if (crew.root == null) continue;
            crew.root.SetParent(crew.parent, true);
            SetSeated(crew.animator, 0);
            bool isPlayer = player != null && crew.root == player.transform;
            Vector3 point = ExitPoint(isPlayer, used);
            used.Add(point);
            Quaternion facing = Quaternion.LookRotation(Flat(transform.forward), Vector3.up); // yüz minibüsün arkasına (yük bölümüne)
            foreach (var c in crew.disabledColliders) if (c != null) c.enabled = true;
            foreach (var b in crew.disabled) if (b != null) b.enabled = true;
            if (isPlayer)
            {
                player.Teleport(point, facing);
                LastExitPoint = point;
            }
            else
            {
                crew.root.SetPositionAndRotation(point, facing);
                LastBotExitPoint = point;
            }
            if (crew.animator != null && crew.animator.runtimeAnimatorController != null) crew.animator.Play(CrewAnimation.StateLocomotion, 0, 0f);
        }
        seated.Clear();
        if (orbitCamera != null)
        {
            orbitCamera.distance = savedDistance;
            orbitCamera.pivotHeight = savedPivot;
            orbitCamera.obstructionIgnoreRoot = null;
        }
        Physics.SyncTransforms();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // İniş noktası: şoför sol kapıdan (+X), yolcu sağ kapıdan (−X); yer yoksa arka sol / arka / arka sağ; zemin raycast, kapsül boş.
    private Vector3 ExitPoint(bool driver, List<Vector3> used)
    {
        float seatZ = driverSeat != null ? driverSeat.localPosition.z : bodyMin.z + 1.6f;
        float side = bodyMax.x + 0.9f;
        var candidates = driver
            ? new[] { new Vector3(side, 0f, seatZ), new Vector3(side, 0f, bodyMax.z + 0.5f), new Vector3(0f, 0f, bodyMax.z + 2.2f), new Vector3(-side, 0f, seatZ) }
            : new[] { new Vector3(-side, 0f, seatZ), new Vector3(-side, 0f, bodyMax.z + 0.5f), new Vector3(0.9f, 0f, bodyMax.z + 2.2f), new Vector3(side, 0f, seatZ) };
        Vector3 fallback = transform.TransformPoint(candidates[0]);
        foreach (var local in candidates)
        {
            Vector3 world = transform.TransformPoint(local);
            if (!GroundBelow(world + Vector3.up * 0.5f, out RaycastHit hit)) continue;
            Vector3 p = hit.point;
            bool tooClose = false;
            foreach (var u in used) if (Vector3.Distance(u, p) < 1f) tooClose = true;
            if (tooClose) continue;
            if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.3f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
            return p;
        }
        if (GroundBelow(fallback + Vector3.up * 0.5f, out RaycastHit g)) fallback = g.point;
        return fallback;
    }

    // Test: hasarı sıfırlar (senaryolar arası).
    public void ResetDamageForTest() { Damage = 0f; Rams = 0; Impacts = 0; }
}
