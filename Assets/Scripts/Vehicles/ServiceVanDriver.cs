using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// Park hâlindeki minibüsü gövdesiyle fiziksel itebilen kinematik gövde (oyunda yalnız ThiefVan).
public interface IMinibusPusher
{
    bool MayPush(ServiceVanDriver minibus);
}

// Servis minibüsü sürüşü (kullanıcı kararı 2026-11-04: Bölüm 1, ekip minibüsün içinde yolda başlar).
//
// Fizik (kullanıcı kararı 2026-11-05, "arcade-gerçekçi"): dört WheelCollider (lastik mesh'lerinin merkezinde, Awake'te kurulur;
// sahne dosyasında yalnız kök Rigidbody + kutu collider'lar durur), süspansiyon (yay = tekerlek yükü / çökme, sönüm oranı
// suspensionDampingRatio), motor ve fren torku acceleration / brakeDecel'den türetilir (kütle × ivme × yarıçap / 4), direksiyon
// hız arttıkça daralır (maxSteerAngle → highSpeedSteerAngle), lastik tutuşu yüksek (az kayma), ağırlık merkezi alçak ve
// "dik tutma yardımı" (UprightAssist: eğim uprightDeadbandDeg'i aşınca düzeltici tork) → ağırlık, süspansiyon ve virajda yatma
// hissedilir ama kolay sürülür, zor devrilir. Görsel lastik + jant mesh'leri WheelPivot altına alınır ve WheelCollider.GetWorldPose
// ile döner / direksiyonla kırılır.
//
// Durumlar:
//  - Driving: Rigidbody dinamik + yerçekimi; girdi tekerlek torku ve direksiyon açısına gider, hız fizikten okunur. Statik engel
//    (korkuluk, kaya) gerçek temasla durdurur; yatay normalli çarpışma itkisi (Δv > impactSpeedThreshold) hasar yazar.
//    W/S gaz / fren-geri, A/D direksiyon, E (dururken) iniş, V kamera. Dururken girdi yoksa el freni (yokuşta kaymaz).
//  - Parked: Rigidbody kinematik, NavMeshObstacle oyar (hırsız ve bot yolu minibüsün GERÇEK yerine göre); yük bölümündeki
//    ekipman serbest fizikte (bölümün geri kalanı park hâlindeki minibüsü bekler).
//  - Shoved (betikli): trafik aracı çarptı (ILaneObstacle.Ram): dinamik, girdi yok, yatay hız betikle verilir ve söner (deterministik,
//    tekerlek sürtünmesi düşürülür), düşey hareket süspansiyondan; durunca yeniden Parked. İtilme yönü döngü başında seçilir
//    (boş tarafa yoklama), toplam yol maxShoveDistance ile sınırlı, tekerlek altı zemin bitince durur (uçuruma gitmez).
//  - Shoved (fiziksel): park hâlindeyken gövdeye HAREKETLİ KİNEMATİK bir gövde (hırsız kamyoneti, MovePosition ile sürülen araç)
//    girerse minibüs dinamik olur, el freniyle kayarak PhysX tarafından itilir; temas bitip durunca Parked. Hırsız kamyonetinin
//    kötü park etmiş minibüsü vura vura yerine itmesi bu yoldan (ya da TrafficVehicle'ın betikli Ram'ıyla) olur.
//    Yük bölümünde ayakta duran ekip minibüsle taşınır, durunca yere düşer (HoldRiders).
// Yük: sürerken ve itilirken yük bölümündeki Rigidbody'ler (sedye, koni) ve dolaptaki kangal minibüse bağlanır, kinematik olur;
// durunca eski ebeveyn ve fizik durumu geri gelir (düşmez, titremez).
// Ekip: oturunca oyuncu ve bot koltuk objelerine bağlanır, hareket / etkileşim bileşenleri kapanır, Animator "Seated" parametresi
// (1 şoför, 2 yolcu) oturma döngüsünü seçer; inince serbest noktalara ışınlanır, bileşenler açılır.
// Yeniden binme (kullanıcı şikâyeti 2026-11-04: dururken yanlışlıkla E'ye basan oyuncu iniyor ve bir daha binemiyordu): minibüs
// Parked'ken oyuncu şoför kapısının yanında E'ye basınca (PlayerInteraction.BoardVan → Board) yeniden oturur; bot yakındaysa ve
// hastalı sedye taşımıyorsa yolcu koltuğuna biner, yoksa oyuncu yalnız sürer. Binişten hemen sonra E yok sayılır (çift basış).
// Zemin: dört tekerleğin de altında zemin yoksa (yol bitti) yerçekimiyle düşer; fallBelowY (FallRespawnPlane) altına inince ekip
// ve yüküyle sürüş girişinin başlangıç şeridine ışınlanır, sürüş sürer. Oturan ekibin kendi yeniden doğması (PlayerFallRespawn)
// oturma boyunca kapalıdır: minibüs "ekip içeride" sanırken oyuncu dışarıda kalamaz.
// Arka kapılar: sürerken kapanır, park edince eski (açık) açıya döner (yük bölümüne erişim).
// Şerit: ILaneObstacle olarak LaneOccupancy'ye kayıtlıdır; ayak izi bir şeridin şeridine giriyorsa o şeridin trafiği durur,
// korna çalar, 5 sn sonra çarpar (TrafficVehicle). Çarpma hasar ekler (Damage → FieldPayout.vanDamage → vehicleWear).
// Yük bölümünde ayakta / yerde ekip varken (CrewInCargo) trafik çarpmaz, korna çalıp bekler.
[DefaultExecutionOrder(-20)]
public class ServiceVanDriver : MonoBehaviour, ILaneObstacle
{
    public enum VanState { Parked, Driving, Shoved }

    public const string ParamSeated = "Seated";
    public const string StateDriveSeated = "Drive_Seated";
    public const string StatePassengerSeated = "Passenger_Seated";
    public const string NoExitWhileMovingText = "Durmadan inilmez";
    public const string NoSafeExitText = "Burada inilemez — minibüsü biraz açın";
    public const string BoardedText = "Minibüse bindiniz: W/S gaz-fren, A/D direksiyon, durunca E ile inin";
    public const string RearDoorLeftName = "RearDoor_Left", RearDoorRightName = "RearDoor_Right", DriverDoorName = "FrontDoor_Left";

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
    [Tooltip("Tam gazda hedef ivme (m/sn²): motor torku = kütle × ivme × tekerlek yarıçapı / 4.")]
    public float acceleration = 4.5f;
    [Tooltip("Tam frende hedef yavaşlama (m/sn²): fren torku aynı formülle; dururken el freni de bu.")]
    public float brakeDecel = 9f;
    [Tooltip("Gaz yokken motor freni (m/sn²).")]
    public float coastDecel = 1.5f;
    [Tooltip("Betikli itilmede (trafik çarpması) yanal / ileri hız sönümü (m/sn²).")]
    public float shovedLateralDecel = 2.5f, shovedForwardDecel = 3f;

    [Header("Tekerlek fiziği (Awake'te WheelCollider kurulur)")]
    [Tooltip("Dururken tam direksiyon açısı (°); maxSpeed'de highSpeedSteerAngle'a iner (kolay kontrol). 40°: dingil 3.74 m → dönüş yarıçapı ≈ 4.5 m (park cebine girilebilsin; ölçüldü 32° ile cebe 1.1 m eksik yanaştı).")]
    public float maxSteerAngle = 40f;
    public float highSpeedSteerAngle = 12f;
    [Tooltip("Direksiyonun dönme hızı (°/sn): klavye girdisi yumuşar.")]
    public float steerRateDegPerSec = 150f;
    [Tooltip("Süspansiyon yolu (m) ve 1 g ek yükte çökme (m): yay = tekerlek yükü / çökme (dinlenme noktası yolun %40'ında).")]
    public float suspensionTravel = .30f, suspensionSag = .08f;
    [Tooltip("Süspansiyon sönüm oranı (1 = kritik; 0.5 bir kez salınıp oturur).")]
    public float suspensionDampingRatio = .5f;
    [Tooltip("Lastik tutuşu (WheelFrictionCurve.stiffness): ileri / yanal; fiziksel itilmede (kayarak itilir) düşürülür.")]
    public float forwardStiffness = 1.6f, sidewaysStiffness = 2.2f, shovedTireStiffness = .5f;
    [Tooltip("Betikli itilmede (trafik Ram) lastik tutuşu ≈ 0: yatay hız betikten gelir, lastik sürtünmesi sönümü büyütmesin (ölçüldü 2026-11-05: 0.5 ile 4.4 m yerine 1.7 m itildi).")]
    public float scriptedShoveTireStiffness = .05f;
    [Tooltip("Ağırlık merkezi: gövde tabanının bu kadar üstünde (m) ve kök-yerel z'de (arkaya +).")]
    public float centerOfMassHeight = .5f, centerOfMassZ = .2f;
    [Tooltip("Dik tutma yardımı: zemin düzlemine göre eğim bu açıyı (°) aşınca düzeltici tork (rad/sn² per rad) ve yatay açısal hız sönümü (1/sn).")]
    public float uprightDeadbandDeg = 5f, uprightSpring = 25f, uprightDamping = 4f;
    [Tooltip("Lastik mesh'i bulunamazsa kullanılan yarıçap (m).")]
    public float wheelRadiusFallback = .40f;
    [Tooltip("Park hâlindeyken gövdeye giren hareketli kinematik gövde (hırsız kamyoneti) fiziksel itmeyi başlatır.")]
    public bool physicalShoveEnabled = true;
    [Tooltip("Fiziksel itmeyi başlatan kinematik gövdenin en düşük hızı (m/sn).")]
    public float physicalShoveMinSpeed = .3f;
    [Tooltip("Fiziksel itilirken el freni (tam frenin oranı): temas bitince kısa yolda durur.")]
    public float shovedHandbrake = .5f;
    [Tooltip("Fiziksel itmede temas kesilince yatay hız bu ivmeyle (m/sn²) söner: tek vuruş minibüsü metrelerce kaydırmaz.")]
    public float shoveBrakeDecel = 7f;
    [Tooltip("Son temastan bu kadar sn sonra temas kesilmiş sayılır (fiziksel itme).")]
    public float shoveContactGrace = .15f;
    [Tooltip("Fiziksel itme başlamadan itme yönünde bu kadar (m) boşluk ve zemin aranır; yoksa minibüs kıpırdamaz.")]
    public float shovePushProbe = .8f;
    [Tooltip("Fiziksel itmede gövde hız sınırları (m/sn, rad/sn) ve PhysX ayırma hızı: kinematik gövdenin köşe teması minibüsü fırlatmaz (ölçüldü: 18 m/sn).")]
    public float shoveMaxSpeed = 4f, shoveMaxAngularSpeed = 0.6f, shoveMaxDepenetration = 1.5f;
    [Tooltip("Fiziksel itmede itme yönüne dik hızın sönümü (m/sn²).")]
    public float shoveSideDecel = 20f;

    [Header("İtilme (trafik çarpması)")]
    [Tooltip("Bir çarpma döngüsünde minibüs en fazla bu kadar (m, yatay) itilir; sonra hız eklenmez ve durur.")]
    public float maxShoveDistance = 6f;
    [Tooltip("İtilme yönü seçiminde statik engele / zemin kenarına bırakılan pay (m).")]
    public float shoveProbeMargin = .4f;
    [Tooltip("İtilme bitince yük bölümünde ayakta duran ekip bu hızla (m/sn) yere düşer (ragdoll).")]
    public float riderKnockSpeed = 2.5f;

    [Header("Düşme (zemin bitince)")]
    [Tooltip("Bu yüksekliğin altı boşluk (FallRespawnPlane); Awake'te oyuncunun PlayerFallRespawn.killBelowY değerinden alınır.")]
    public float fallBelowY = -60f;

    [Header("Yeniden binme ve kapılar")]
    [Tooltip("Oyuncu şoför kapısı noktasına bu kadar yakınken (yatay, m) binebilir.")]
    public float boardRange = 2f;
    [Tooltip("Bot minibüse bu kadar yakınsa (m) oyuncuyla birlikte yolcu koltuğuna biner.")]
    public float botBoardRange = 12f;
    [Tooltip("Binişten sonra E bu kadar süre (sn) yok sayılır: binme basışı inişe dönüşmesin.")]
    public float exitLockoutSeconds = .5f;
    [Tooltip("Arka kapıların kapanma / açılma süresi (sn).")]
    public float doorSeconds = .6f;
    [Tooltip("Birinci şahısta sürerken kamera eğimi (°, + aşağı): ön panel yolu kapatmasın.")]
    public float driveFirstPersonPitch = 0f;
    [Tooltip("Sürerken göz noktasına eklenen yükseklik (m). Baş merkezi 1.72, ön panel üstü 1.63: göz panelin biraz üstünde kalır.")]
    public float driveEyeUp = .08f;
    [Tooltip("Sürerken gözün baştan ileri uzaklığı (m; OrbitCamera.eyeForward yerine). Ölçüldü 2026-11-05: yürüyüşteki 0.16 gözü ön camın 7 cm ÖNÜNE (kabin dışına) çıkarıyordu.")]
    public float driveEyeForward = .02f;

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
    // İtilme (test ve hata ayıklama): döngü başından bu yana yatay yer değiştirme, seçilen yön ve nedeni, taşınan ekip sayısı
    public float ShoveDistance { get { return shoveOriginSet ? Horizontal(transform.position - shoveOrigin).magnitude : 0f; } }
    public Vector3 ShoveDirection { get { return shoveAway; } }
    public string LastShoveReason { get; private set; } = "";
    public int RiderCount { get { return riders.Count; } }
    public int RidersKnocked { get; private set; }
    // Yeniden binme / düşme / kapılar (test ve HUD)
    public int Boardings { get; private set; }
    public int Exits { get; private set; }
    public bool BotSeated { get { foreach (var c in seated) if (bot != null && c.root == bot.transform) return true; return false; } }
    public int FallRespawns { get; private set; }
    public bool Falling { get; private set; }
    public float RearDoorOpenFraction { get { return doorOpenT; } }  // 1 açık, 0 kapalı
    public bool RearDoorsClosed { get { return rearDoors.Count > 0 && doorOpenT <= 0.01f; } }
    public bool RearDoorsOpen { get { return rearDoors.Count == 0 || doorOpenT >= 0.99f; } }
    public Transform DriverDoor { get; private set; }
    // Tekerlek fiziği (test ve HUD): collider'lar (ön sol, ön sağ, arka sol, arka sağ), görsel pivotlar, yarıçap, yerdeki tekerlek sayısı,
    // süspansiyon sıkışması (0 tam açık … 1 tam kapalı; en az / en çok), zemin düzlemine göre eğim, yanal kayma açısı, direksiyon açısı
    public WheelCollider[] Wheels { get { return wheels; } }
    public Transform[] WheelPivots { get { return wheelPivots; } }
    public float WheelRadius { get; private set; }
    public int WheelsGrounded { get; private set; }
    public float SuspensionMin { get; private set; }
    public float SuspensionMax { get; private set; }
    public float TiltDeg { get; private set; }
    public float RollDeg { get; private set; }
    public float SlipAngleDeg { get; private set; }
    public float SteerAngle { get; private set; }
    public float MotorTorque { get; private set; }
    public float BrakeTorque { get; private set; }
    public bool PhysicalShove { get; private set; }   // Shoved: fiziksel (kinematik gövde itiyor) mi, betikli (trafik Ram) mi
    public int PhysicalShoves { get; private set; }
    public Rigidbody LastPusher { get; private set; }
    // Şoför kapısının önündeki biniş noktası (dünya, taban yüksekliğinde)
    public Vector3 DriverDoorPoint { get { float seatZ = driverSeat != null ? driverSeat.localPosition.z : bodyMin.z + 1.6f; return transform.TransformPoint(new Vector3(bodyMax.x + 0.7f, bodyMin.y, seatZ)); } }

    public event Action<ServiceVanDriver> Boarded;
    public event Action<ServiceVanDriver> Exited;
    public event Action<ServiceVanDriver> FallRespawned;
    public event Action<ServiceVanDriver, TrafficVehicle> Rammed;
    // Yeni çarpma döngüsü (araç temas etti, döngü başında bir kez): hasar kuralından (araç başına bir kez, Rammed) bağımsız
    public event Action<ServiceVanDriver, TrafficVehicle> RamCycleStarted;
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
    private readonly WheelCollider[] wheels = new WheelCollider[4];
    private readonly Transform[] wheelPivots = new Transform[4];
    private float steerAngle;
    private bool groundValid;                       // bu adımda dört tekerlek altında da zemin bulundu (groundUp güncel)
    private float lastFallRespawn = -100f;        // plan altı ışınlamaları en çok saniyede bir (ölçüldü: test planı başlangıcın üstündeyken her adımda ışınlıyordu)
    private float dynamicSince = -100f;             // son dinamikleşme (SetDriving / BeginShove): ilk 0.5 sn oturma, hasar sayılmaz
    private readonly Collider[] overlaps = new Collider[96];
    private readonly List<HeldItem> held = new List<HeldItem>();
    private readonly List<SeatedCrew> seated = new List<SeatedCrew>();
    // İtilirken yük bölümünde ayakta duran ekip (oyuncu / bot): minibüse bağlı taşınır, durunca yere düşer
    private readonly List<SeatedCrew> riders = new List<SeatedCrew>();
    private Vector3 shoveOrigin, shoveAway, lastShoveVelocity;
    private bool shoveOriginSet;
    private float shoveLateralBudget;
    private readonly HashSet<TrafficVehicle> damagedBy = new HashSet<TrafficVehicle>(); // hasar yazan araçlar (araç başına bir kez)
    private bool cameraYawPending; // oturma Awake'te; OrbitCamera.Start yaw'ı ezer → ilk Update'te yeniden yazılır (review P3-5)
    private Vector3 lateral;
    private Vector3 groundUp = Vector3.up;
    private float lastRamContact = -100f, lastRamDamage = -100f;
    private float savedDepenetration = -1f;
    private Vector3 shoveDirection = Vector3.forward;
    private TrafficVehicle lastRammer;
    private float savedDistance, savedPivot;
    private float lastHeadingYaw;
    private bool testInput;
    private float testThrottle, testSteer;
    private InputAction moveAction, lookAction, interactAction, viewAction;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private float settleSince = -1f;
    private Quaternion wheelBaseRotation = Quaternion.identity;
    private float boardedAt = -100f;
    private Vector3 fallRespawnPosition;           // plan altına düşünce dönülen poz (DriveIntro yazar; yoksa sahne pozu)
    private Quaternion fallRespawnRotation;
    private readonly List<Transform> rearDoors = new List<Transform>();
    private readonly List<Quaternion> rearDoorOpenRotations = new List<Quaternion>();
    private float doorOpenT = 1f;
    private float savedPitch, savedEyeUp, savedEyeForward;
    private OrbitCamera.ViewMode lastCameraMode;

    private void Awake()
    {
        Instance = this;
        fallRespawnPosition = transform.position;
        fallRespawnRotation = transform.rotation;
        // Arka kapılar (sahnede açık pozda) ve şoför kapısı: görsel hiyerarşide ada göre
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == RearDoorLeftName || t.name == RearDoorRightName) { rearDoors.Add(t); rearDoorOpenRotations.Add(t.localRotation); }
            else if (t.name == DriverDoorName) DriverDoor = t;
        }
        if (player != null)
        {
            var respawn = player.GetComponent<PlayerFallRespawn>();
            if (respawn != null) fallBelowY = respawn.killBelowY;
        }
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.linearDamping = 0f;
        rb.angularDamping = .3f;
        rb.centerOfMass = new Vector3(0f, bodyMin.y + centerOfMassHeight, centerOfMassZ);
        rb.isKinematic = true;
        SetupWheels();
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

    // ---------------------------------------------------------------------------------------------
    // Tekerlekler: lastik ("Araç lastiği | Ön Sol" …) ve jant ("Jant ve göbek | …") mesh'leri garaj görselinde kök uzayında
    // (birim dönüşüm) durur; merkez ve yarıçap mesh bounds'undan okunur. Her köşe için kökün altında bir WheelCollider (Wheel_FL …)
    // ve bir görsel pivot (WheelPivot_FL …, lastik + jant bunun altına taşınır). WheelCollider kendi GameObject'inin dönüşünü yok sayar
    // ve Rigidbody'nin +Z'sini ileri alır (ölçüldü 2026-11-05: 180° çevrilmiş GO ile pozitif tork minibüsü GERİ sürdü); kökte +Z arka
    // olduğundan motor torku ters işaretle verilir, direksiyon açısı +: sağ (ölçülür), GetWorldPose dönüşü doğrudan uygulanır.
    // Sıra wheelPointsLocal ile aynı: ön sol (+x), ön sağ (−x), arka sol, arka sağ. Mesh bulunamazsa tekerlek noktası + yarıçap.
    // ---------------------------------------------------------------------------------------------
    private static readonly string[][] WheelCornerKeys = { new[] { "Ön", "Sol" }, new[] { "Ön", "Sa" }, new[] { "Arka", "Sol" }, new[] { "Arka", "Sa" } };
    public static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
    public static readonly string[] WheelPivotNames = { "WheelPivot_FL", "WheelPivot_FR", "WheelPivot_RL", "WheelPivot_RR" };
    public const string TireKey = "lasti", RimKey = "Jant";

    private void SetupWheels()
    {
        var renderers = GetComponentsInChildren<MeshFilter>(true);
        float radius = wheelRadiusFallback;
        var centers = new Vector3[4];
        var found = new bool[4];
        var parts = new List<Transform>[4];
        for (int i = 0; i < 4; i++)
        {
            parts[i] = new List<Transform>();
            centers[i] = (wheelPointsLocal != null && wheelPointsLocal.Length == 4 ? wheelPointsLocal[i] : Vector3.zero) + Vector3.up * wheelRadiusFallback;
            foreach (var mf in renderers)
            {
                string n = mf.name;
                if (!n.Contains(WheelCornerKeys[i][0]) || !n.Contains(WheelCornerKeys[i][1])) continue;
                bool tire = n.Contains(TireKey), rim = n.Contains(RimKey);
                if (!tire && !rim) continue;
                parts[i].Add(mf.transform);
                if (tire && mf.sharedMesh != null)
                {
                    var b = mf.sharedMesh.bounds;
                    centers[i] = transform.InverseTransformPoint(mf.transform.TransformPoint(b.center));
                    radius = Mathf.Max(.15f, b.extents.y * Mathf.Abs(mf.transform.lossyScale.y / Mathf.Max(1e-4f, transform.lossyScale.y)));
                    found[i] = true;
                }
            }
        }
        WheelRadius = radius;
        float load = rb.mass * Physics.gravity.magnitude / 4f;
        float spring = load / Mathf.Max(.01f, suspensionSag);
        float damper = 2f * suspensionDampingRatio * Mathf.Sqrt(spring * rb.mass / 4f);
        // Dinlenme: targetPosition = dinlenmede sıkışma oranı (0 tam açık, 1 tam kapalı); PhysX taşınan kütlenin ağırlığını bu noktada
        // kendiliğinden dengeler (ölçüldü 2026-11-05: 0.93 ile tekerlek tepeye 2 cm kala durdu, gövde şasiyle yola oturup kilitlendi).
        // %40 sıkışmış dinlenme: tümseğe %60, sarkmaya %40 yol. Yay sertliği yalnız dinamik tepkiyi belirler (1 g'de suspensionSag çökme).
        const float targetPosition = .4f;
        float restExtension = (1f - targetPosition) * suspensionTravel;
        var localRot = Quaternion.identity;
        for (int i = 0; i < 4; i++)
        {
            var pivot = new GameObject(WheelPivotNames[i]).transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = centers[i];
            pivot.localRotation = localRot;
            foreach (var p in parts[i]) p.SetParent(pivot, true);
            wheelPivots[i] = pivot;

            var go = new GameObject(WheelNames[i]);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = centers[i] + Vector3.up * restExtension;
            go.transform.localRotation = localRot;
            var wc = go.AddComponent<WheelCollider>();
            wc.radius = radius;
            wc.mass = 30f;
            wc.wheelDampingRate = .25f;
            wc.suspensionDistance = suspensionTravel;
            wc.forceAppPointDistance = 0f;
            wc.center = Vector3.zero;
            wc.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = targetPosition };
            wheels[i] = wc;
        }
        ApplyTireStiffness(forwardStiffness, sidewaysStiffness);
        SetWheelTorques(0f, 0f, 0f);
    }

    private void ApplyTireStiffness(float forward, float sideways)
    {
        foreach (var wc in wheels)
        {
            if (wc == null) continue;
            wc.forwardFriction = new WheelFrictionCurve { extremumSlip = .4f, extremumValue = 1f, asymptoteSlip = .8f, asymptoteValue = .6f, stiffness = forward };
            wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = .2f, extremumValue = 1f, asymptoteSlip = .5f, asymptoteValue = .8f, stiffness = sideways };
        }
    }

    // motor: minibüsün ÖNÜNE doğru (+); WheelCollider'ın ilerisi kök +Z (arka) olduğundan işaret çevrilir
    private void SetWheelTorques(float motor, float brake, float steer)
    {
        MotorTorque = motor; BrakeTorque = brake; SteerAngle = steer;
        for (int i = 0; i < 4; i++)
        {
            var wc = wheels[i];
            if (wc == null) continue;
            wc.motorTorque = -motor;
            wc.brakeTorque = brake;
            if (i < 2) wc.steerAngle = steer;
        }
    }

    // Tanı (test raporu): her tekerlek için yerde mi, çarptığı collider, dönüş hızı, süspansiyon kuvveti
    public string WheelReport()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 4; i++)
        {
            var wc = wheels[i];
            if (wc == null) { sb.Append(WheelNames[i] + ":YOK "); continue; }
            bool hit = wc.GetGroundHit(out WheelHit h);
            wc.GetWorldPose(out Vector3 wp, out _);
            Vector3 wpLocal = transform.InverseTransformPoint(wp);
            float roadY = GroundBelow(wp, out RaycastHit g) ? g.point.y : float.NaN;
            sb.Append(WheelNames[i] + ":" + (hit ? h.collider.name + " F" + h.force.ToString("F0") + " slip" + h.forwardSlip.ToString("F2") + "/" + h.sidewaysSlip.ToString("F2") + " temasY" + h.point.y.ToString("F2") + " n" + h.normal.ToString("F2") : "havada") + " rpm" + wc.rpm.ToString("F0") + " M" + wc.motorTorque.ToString("F0") + " B" + wc.brakeTorque.ToString("F0") + " S" + wc.steerAngle.ToString("F0")
                + " tepe" + transform.InverseTransformPoint(wc.transform.position).ToString("F2") + " merkez(yerel)" + wpLocal.ToString("F2") + " merkezY" + wp.y.ToString("F2") + " yolY" + roadY.ToString("F2") + " | ");
        }
        // Gövde: şasi kutusu dünyada; kesişen collider'lar (zemin dahil) ve şasi altı–yol açıklığı
        Vector3 chassisCenter = transform.TransformPoint(new Vector3(0f, bodyMin.y + .22f + .15f, (bodyMin.z + bodyMax.z) * .5f));
        Vector3 chassisHalf = new Vector3(.95f, .15f, (bodyMax.z - bodyMin.z - .8f) * .5f);
        var names = new List<string>();
        foreach (var c in Physics.OverlapBox(chassisCenter, chassisHalf, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            if (c != null && !c.transform.IsChildOf(transform)) names.Add(c.name + (c.attachedRigidbody != null ? "(rb)" : ""));
        Vector3 bottom = transform.TransformPoint(new Vector3(0f, bodyMin.y + .22f, (bodyMin.z + bodyMax.z) * .5f));
        float clear = Physics.Raycast(bottom, Vector3.down, out RaycastHit ch, 3f, ~0, QueryTriggerInteraction.Ignore) ? ch.distance : float.NaN;
        var all = new List<string>();
        foreach (var c in Physics.OverlapBox(transform.TransformPoint(new Vector3(0f, (bodyMin.y + bodyMax.y) * .5f, (bodyMin.z + bodyMax.z) * .5f)), new Vector3((bodyMax.x - bodyMin.x) * .5f, (bodyMax.y - bodyMin.y) * .5f, (bodyMax.z - bodyMin.z) * .5f), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            if (c != null && !c.transform.IsChildOf(transform)) all.Add(c.name + (c.attachedRigidbody != null ? (c.attachedRigidbody.isKinematic ? "(krb)" : "(rb)") : ""));
        sb.Append(" şasi kesişen [" + string.Join(",", names) + "] şasi altı açıklık " + clear.ToString("F2") + " m (" + (float.IsNaN(clear) ? "-" : ch.collider.name) + "), gövde kutusu kesişen [" + string.Join(",", all) + "], kök y " + transform.position.y.ToString("F2") + " ağırlık merkezi " + rb.centerOfMass.ToString("F2") + " kütle " + rb.mass.ToString("F0") + " hız " + rb.linearVelocity.ToString("F2") + " açısal " + rb.angularVelocity.ToString("F2") + " tekerlek nokta yolY " + string.Join("/", System.Array.ConvertAll(wheelPointsLocal, p => GroundBelow(transform.TransformPoint(p), out RaycastHit gg) ? gg.point.y.ToString("F2") : "-")));
        return sb.ToString();
    }

    private float MaxMotorTorque { get { return rb.mass * acceleration * WheelRadius / 4f; } }
    private float MaxBrakeTorque { get { return rb.mass * brakeDecel * WheelRadius / 4f; } }
    private float CoastBrakeTorque { get { return rb.mass * coastDecel * WheelRadius / 4f; } }

    // Tekerlek tanısı: yerdeki sayı, süspansiyon sıkışma aralığı (0 açık … 1 kapalı)
    private void SampleWheels()
    {
        WheelsGrounded = 0; SuspensionMin = 1f; SuspensionMax = 0f;
        foreach (var wc in wheels)
        {
            if (wc == null) continue;
            if (wc.isGrounded) WheelsGrounded++;
            wc.GetWorldPose(out Vector3 p, out _);
            float ext = Vector3.Dot(wc.transform.position - p, wc.transform.up); // tepe noktasından aşağı uzanım
            float comp = 1f - Mathf.Clamp01(ext / Mathf.Max(.01f, wc.suspensionDistance));
            SuspensionMin = Mathf.Min(SuspensionMin, comp);
            SuspensionMax = Mathf.Max(SuspensionMax, comp);
        }
        if (SuspensionMin > SuspensionMax) SuspensionMin = SuspensionMax = 0f;
    }

    // Dik tutma yardımı (arcade): zemin düzlemine (yoksa dünya yukarısına) göre eğim ölü bandı aşınca düzeltici açısal ivme;
    // yatay açısal hız her zaman sönümlenir (süspansiyon salınımı yumuşar). Yaw'a dokunmaz.
    private void UprightAssist()
    {
        Vector3 refUp = groundValid ? groundUp : Vector3.up;
        Vector3 up = transform.up;
        float tilt = Vector3.Angle(up, refUp);
        Vector3 w = rb.angularVelocity;
        Vector3 wHorizontal = w - refUp * Vector3.Dot(w, refUp);
        Vector3 torque = -wHorizontal * uprightDamping;
        if (tilt > uprightDeadbandDeg)
        {
            Vector3 axis = Vector3.Cross(up, refUp);
            if (axis.sqrMagnitude > 1e-6f) torque += axis.normalized * ((tilt - uprightDeadbandDeg) * Mathf.Deg2Rad * uprightSpring);
        }
        rb.AddTorque(torque, ForceMode.Acceleration);
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
        // PlayerFallRespawn da kapanır: oturan oyuncu minibüsle plan altına inerse minibüs ışınlanır (FixedUpdate); oyuncunun kendi
        // yeniden doğması koltuktayken ışınlayıp CharacterController'ı açıyor, minibüs "ekip içeride" sanırken oyuncu dışarıda kalıyordu.
        foreach (var b in crew.root.GetComponents<Behaviour>())
        {
            if (b is Animator || b is FirstPersonBody || b is PlayerHealth || b is ServiceVanDriver) continue;
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
            if (c == null || c.transform.IsChildOf(transform) || IsCrewOrCharacter(c)) continue; // ekip / karakter (ragdoll dahil) statik engel değil
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
        rb.useGravity = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        State = VanState.Driving;
        PhysicalShove = false;
        settleSince = -1f;
        dynamicSince = Time.time;
        Falling = false;
        ApplyTireStiffness(forwardStiffness, sidewaysStiffness);
        SetWheelTorques(0f, MaxBrakeTorque, 0f);
        if (sound != null && sound.engine != null && !sound.engine.isPlaying) sound.engine.Play();
    }

    public void SetParked()
    {
        Speed = 0f;
        lateral = Vector3.zero;
        if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        rb.isKinematic = true;
        State = VanState.Parked;
        PhysicalShove = false;
        if (savedDepenetration >= 0f) rb.maxDepenetrationVelocity = savedDepenetration;
        Falling = false;
        steerAngle = 0f;
        SetWheelTorques(0f, MaxBrakeTorque, 0f);
        ReleaseCargo();
        ReleaseRiders();
        shoveOriginSet = false;
        if (navObstacle != null) navObstacle.enabled = true;
        if (sound != null) { sound.SetSpeed(0f); if (!CrewInside && sound.engine != null) sound.engine.Stop(); }
        Physics.SyncTransforms();
    }

    private void BeginShove(bool physical)
    {
        HoldCargo();
        HoldRiders();
        if (navObstacle != null) navObstacle.enabled = false;
        rb.isKinematic = false;
        rb.useGravity = true;
        State = VanState.Shoved;
        PhysicalShove = physical;
        if (physical)
        {
            PhysicalShoves++;
            if (savedDepenetration < 0f) savedDepenetration = rb.maxDepenetrationVelocity;
            rb.maxDepenetrationVelocity = shoveMaxDepenetration;
        }
        settleSince = -1f;
        dynamicSince = Time.time;
        lastShoveVelocity = Vector3.zero;
        steerAngle = 0f;
        // İtilirken lastikler kayar (betikli hızla savaşmasın, fiziksel itmede devrilme momenti küçük kalsın); fiziksel itmede el freni
        float stiff = physical ? shovedTireStiffness : scriptedShoveTireStiffness;
        ApplyTireStiffness(stiff, stiff);
        SetWheelTorques(0f, physical ? MaxBrakeTorque * shovedHandbrake : 0f, 0f);
    }

    // Park hâlindeyken gövde kutusuna giren hareketli kinematik gövde (hırsız kamyoneti; MovePosition ile sürülen araç): fiziksel itme başlar.
    // Dinamik gövdeler (koni, ragdoll), ekip / karakterler, kendi parçaları ve duran kinematik cisimler (sedye) sayılmaz.
    private bool ScanForPusher()
    {
        Vector3 center = transform.TransformPoint(new Vector3((bodyMin.x + bodyMax.x) * .5f, (bodyMin.y + .2f + bodyMax.y) * .5f, (bodyMin.z + bodyMax.z) * .5f));
        Vector3 half = new Vector3((bodyMax.x - bodyMin.x) * .5f, (bodyMax.y - bodyMin.y - .2f) * .5f, (bodyMax.z - bodyMin.z) * .5f);
        int n = Physics.OverlapBoxNonAlloc(center, half, overlaps, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = overlaps[i];
            if (c == null || c.isTrigger || c.transform.IsChildOf(transform)) continue;
            var other = c.attachedRigidbody;
            if (other == null || !other.isKinematic || other.linearVelocity.magnitude < physicalShoveMinSpeed) continue;
            // Yalnız itmesine izin verilen gövde (hırsız kamyoneti): trafik aracı ve ambulans betikli Ram / bekleme kuralını kullanır,
            // karakter ve eşya hiç itmez. Kamyonet de şu an itemeyebilir (yükte ekip, görev bitti).
            var pusher = other.GetComponentInParent<IMinibusPusher>();
            if (pusher == null || !pusher.MayPush(this)) continue;
            // Arkası dolu / kenardaysa dinamikleşmez: kinematik gövde sıkışan minibüsü PhysX ile açık tarafa fırlatırdı (ölçüldü: 3 m)
            if (!CanBePushed(other.linearVelocity, PushProbeFor(other.linearVelocity.magnitude), other)) continue;
            shoveDirection = Flat(other.linearVelocity);
            LastPusher = other;
            BeginShove(true);
            lastRamContact = Time.time;
            return true;
        }
        return false;
    }

    // Dış API (hırsız kamyoneti vb.): park hâlindeki minibüsü fiziksel itmeye geçirir; itici dinamik gövdeyi PhysX iter.
    public bool BeginPhysicalShove(Rigidbody by, Vector3 pushDirection)
    {
        // Zaten fiziksel itiliyorsa yalnız temas tazelenir (itici temas sürerken frenlenmez, park hâline geçilmez)
        if (State == VanState.Shoved && PhysicalShove) { LastPusher = by; lastRamContact = Time.time; settleSince = -1f; return true; }
        if (State != VanState.Parked || !CanBePushed(pushDirection, PushProbeFor(by != null ? by.linearVelocity.magnitude : 0f), by)) return false;
        shoveDirection = Flat(pushDirection);
        LastPusher = by;
        BeginShove(true);
        lastRamContact = Time.time;
        return true;
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
        steerAngle = 0f;
        rb.isKinematic = wasKinematic;
        if (parked) SetParked(); else SetDriving();
    }

    // Çarpışma itkisi hasarı: yatay normalli temas (korkuluk, kaya, araç) Δv = itki / kütle eşiği aşınca; alt / üst temas (gravel dudağı,
    // zemin) ve dinamikleşmeden sonraki ilk oturma sayılmaz. Trafik aracının betikli çarpması ayrıca hasar yazar (Ram); o hasarın
    // 1 sn içinde aynı temastan itki hasarıyla tekrarlanmaması için yalnız betikli itilmede ve yalnız Ram hasarından sonraki 1 sn
    // atlanır (Codex review P2: eskiden her kinematik temas lastRamContact'ı güncelleyip sürüşteki araç çarpmasını ve 1 sn
    // içindeki kaya çarpmasını da bastırıyordu).
    private void OnCollisionEnter(Collision c)
    {
        if (rb == null || rb.isKinematic || c.collider == null || c.collider.transform.IsChildOf(transform)) return;
        NoteKinematicContact(c);
        if (Time.time - dynamicSince < .5f) return;
        if (State == VanState.Shoved && !PhysicalShove && Time.time - lastRamDamage < 1f) return;
        if (c.contactCount == 0) return;
        Vector3 n = c.GetContact(0).normal;
        if (Mathf.Abs(n.y) > .7f) return;
        float dv = c.impulse.magnitude / Mathf.Max(1f, rb.mass);
        if (dv > impactSpeedThreshold)
        {
            Impacts++;
            AddDamage((dv - impactSpeedThreshold) * damagePerImpactSpeed);
        }
    }

    private void OnCollisionStay(Collision c)
    {
        if (rb == null || rb.isKinematic || c.collider == null || c.collider.transform.IsChildOf(transform)) return;
        NoteKinematicContact(c);
    }

    // Kinematik gövdeyle (araç) temas: itilme bitişi (settle) temas kesilince sayılır
    private void NoteKinematicContact(Collision c)
    {
        var other = c.rigidbody;
        if (other == null || !other.isKinematic || IsCharacter(c.collider)) return; // araçlar sayılır (review P1)
        if (other.linearVelocity.magnitude < physicalShoveMinSpeed) return;
        lastRamContact = Time.time;
        LastPusher = other;
    }

    // Plan altına düşünce dönülecek poz (DriveIntro: sürüş girişinin başlangıç şeridi).
    public void SetFallRespawn(Vector3 position, Quaternion rotation) { fallRespawnPosition = position; fallRespawnRotation = rotation; }

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
        // Arka kapılar: sürerken kapalı, aksi hâlde sahnedeki açık poz
        float doorGoal = State == VanState.Driving ? 0f : 1f;
        if (rearDoors.Count > 0 && doorOpenT != doorGoal)
        {
            doorOpenT = Mathf.MoveTowards(doorOpenT, doorGoal, Time.deltaTime / Mathf.Max(0.01f, doorSeconds));
            float eased = Mathf.SmoothStep(0f, 1f, doorOpenT);
            for (int i = 0; i < rearDoors.Count; i++)
                if (rearDoors[i] != null) rearDoors[i].localRotation = Quaternion.Slerp(Quaternion.identity, rearDoorOpenRotations[i], eased);
        }
        // Görsel tekerlekler: dinamikken WheelCollider pozu (süspansiyon, dönüş, direksiyon); park hâlinde son poz kalır
        if (!rb.isKinematic)
            for (int i = 0; i < 4; i++)
                if (wheels[i] != null && wheelPivots[i] != null) { wheels[i].GetWorldPose(out Vector3 p, out Quaternion q); wheelPivots[i].SetPositionAndRotation(p, q); }
        if (!CrewInside) return;
        // Sürerken birinci şahsa geçilirse eğim sıfırlanır (TP'nin 18° aşağı eğimi ön paneli ekranın yarısına çıkarıyordu)
        if (orbitCamera != null && orbitCamera.Mode != lastCameraMode)
        {
            lastCameraMode = orbitCamera.Mode;
            if (lastCameraMode == OrbitCamera.ViewMode.FirstPerson) orbitCamera.Pitch = driveFirstPersonPitch;
        }
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
            if (cameraYawPending) { cameraYawPending = false; orbitCamera.Yaw = yaw; lastHeadingYaw = yaw; }
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
        if (State == VanState.Parked)
        {
            if (physicalShoveEnabled) ScanForPusher();
            if (State == VanState.Parked) return;
        }
        float dt = Time.fixedDeltaTime;
        Vector3 fwdFlat = Forward; fwdFlat.y = 0f; fwdFlat.Normalize();
        Vector3 hv = rb.linearVelocity; hv.y = 0f;
        float actual = Vector3.Dot(hv, fwdFlat);
        Speed = actual;
        lateral = hv - fwdFlat * actual;
        SlipAngleDeg = hv.magnitude > 1f ? Vector3.Angle(hv, fwdFlat * Mathf.Sign(actual == 0f ? 1f : actual)) : 0f;
        MaxSpeedReached = Mathf.Max(MaxSpeedReached, Mathf.Abs(Speed));

        // Zemin düzlemi (tekerlek noktası raycast'leri): dik tutma referansı, betikli itilmede eğim hedefi, kenar denetimi
        groundValid = GroundPlane(out Vector3 up, out _, out int groundedPoints);
        if (groundValid) groundUp = up;
        SampleWheels();
        Vector3 refUp = groundValid ? groundUp : Vector3.up;
        TiltDeg = Vector3.Angle(transform.up, refUp);
        RollDeg = Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(refUp, Forward).normalized, Vector3.ProjectOnPlane(transform.up, Forward).normalized, Forward));

        // Düşme: hiçbir tekerlek yerde değil ve tekerlek noktalarının altında zemin yok (yol bitti) → yerçekimi düşürür; plan altında ışınlama
        Falling = WheelsGrounded == 0 && groundedPoints == 0 && Time.time - dynamicSince > .3f;
        if (transform.position.y < fallBelowY && Time.time - lastFallRespawn > 1f)
        {
            lastFallRespawn = Time.time;
            // Boşluğa düştü: ekip ve yükle sürüş girişinin başlangıç şeridine (ya da sahne pozuna) dön; ekip içerideyse sürüş sürer,
            // değilse (itilip düşen park hâlindeki minibüs) park hâlinde durur (yeniden binilebilir)
            FallRespawns++;
            TeleportTo(fallRespawnPosition, fallRespawnRotation, !CrewInside);
            GameNotice.Show("Minibüs uçuruma düştü: yolun başına dönüldü");
            FallRespawned?.Invoke(this);
            return;
        }

        if (State == VanState.Driving)
        {
            float t = CrewInside ? Throttle : 0f;
            float s = CrewInside ? Steer : 0f;
            // Direksiyon: hız arttıkça açı daralır; girdi yumuşatılır. WheelCollider +steerAngle = sağa (collider +X sağ)
            float steerMax = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, Mathf.Clamp01(Mathf.Abs(Speed) / Mathf.Max(.1f, maxSpeed)));
            steerAngle = Mathf.MoveTowards(steerAngle, s * steerMax, steerRateDegPerSec * dt);
            float motor = 0f, brake;
            if (t > .01f)
            {
                if (Speed < -.3f) brake = MaxBrakeTorque;                                  // geri giderken gaz = fren
                else { motor = Speed < maxSpeed ? t * MaxMotorTorque : 0f; brake = Speed > maxSpeed + .5f ? CoastBrakeTorque : 0f; }
            }
            else if (t < -.01f)
            {
                if (Speed > .3f) brake = MaxBrakeTorque;                                    // ileri giderken S = fren
                else { motor = Speed > -maxReverseSpeed ? t * MaxMotorTorque * .7f : 0f; brake = 0f; }
            }
            else
            {
                // Gaz yok: motor freni; durmaya yakın el freni (yokuşta kaymaz, iniş için tam durur)
                float hold = Mathf.Clamp01(1f - Mathf.Abs(Speed) / .5f);
                brake = Mathf.Lerp(CoastBrakeTorque, MaxBrakeTorque, hold);
            }
            SetWheelTorques(motor, brake, steerAngle);
            UprightAssist();
        }
        else if (PhysicalShove)
        {
            // Fiziksel itilme: kinematik gövde PhysX ile iter; el freni + düşük lastik tutuşuyla kayar, dik tutma yardımı devrilmeyi önler
            SetWheelTorques(0f, MaxBrakeTorque * shovedHandbrake, 0f);
            UprightAssist();
            // Temas kesilince kısa yolda dur (ölçüldü: el freni + düşük tutuşla 1.5 m/sn'lik tek vuruş 4.7 m kaydırıyordu); bir tekerleğin
            // altında zemin yoksa (yol / banket kenarı) yatay hız hemen kesilir: fiziksel itme de uçuruma götürmez.
            bool edge = !groundValid && groundedPoints < 4;
            if (edge || Time.time - lastRamContact > shoveContactGrace)
            {
                Vector3 v = rb.linearVelocity;
                Vector3 h = edge ? Vector3.zero : Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), Vector3.zero, shoveBrakeDecel * dt);
                rb.linearVelocity = new Vector3(h.x, v.y, h.z);
                if (edge && !LastShoveReason.EndsWith("zemin yok, durdu")) LastShoveReason += " | kenar: zemin yok, durdu";
            }
            Vector3 lv = rb.linearVelocity;
            Vector3 lh = Vector3.ClampMagnitude(new Vector3(lv.x, 0f, lv.z), shoveMaxSpeed);
            // Lastik yana kaymaz: itme yönüne dik hız söner. Köşe teması minibüsü korkuluğa doğru savuruyor, itme arkası-dolu ile kesiliyordu
            // (ölçüldü: aynı senaryo koşudan koşuya yer boşaltamıyordu)
            Vector3 along = shoveDirection * Vector3.Dot(lh, shoveDirection);
            lh = along + Vector3.MoveTowards(lh - along, Vector3.zero, shoveSideDecel * dt);
            rb.linearVelocity = new Vector3(lh.x, lv.y, lh.z);
            rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, shoveMaxAngularSpeed);
        }
        else
        {
            // Betikli itilme (trafik Ram): yatay hız betikle verilir ve söner, düşey hız fizikten; eğim zemin düzlemine kilitli
            Speed = Mathf.MoveTowards(Speed, 0f, shovedForwardDecel * dt);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero, shovedLateralDecel * dt);
            if (!groundValid && groundedPoints < 4)
            {
                // Bir tekerleğin altında zemin yok (yol / banket kenarı): itilme burada biter, minibüs uçuruma gitmez.
                Speed = 0f;
                lateral = Vector3.zero;
                if (!LastShoveReason.EndsWith("zemin yok, durdu")) LastShoveReason += " | kenar: zemin yok, durdu";
            }
            Vector3 headingOnGround = Vector3.ProjectOnPlane(fwdFlat, groundUp).normalized;
            Quaternion targetRot = Quaternion.LookRotation(-headingOnGround, groundUp);
            rb.linearVelocity = headingOnGround * Speed + lateral + Vector3.up * rb.linearVelocity.y;
            Quaternion delta = targetRot * Quaternion.Inverse(rb.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f) rb.angularVelocity = Vector3.zero;
            else rb.angularVelocity = axis.normalized * Mathf.Clamp(angle / dt, -240f, 240f) * Mathf.Deg2Rad;
            SetWheelTorques(0f, 0f, 0f);
        }

        if (sound != null) sound.SetSpeed(Mathf.Abs(Speed));

        // İtilme bitti mi: yavaşladı ve 1 sn'dir temas yok → park (kinematik, ekipman serbest)
        if (State == VanState.Shoved)
        {
            if (hv.magnitude > 0.5f) lastShoveVelocity = hv; // durunca ekibin düşme yönü
            bool slow = hv.magnitude < 0.15f && Time.time - lastRamContact > 1f;
            if (slow) { if (settleSince < 0f) settleSince = Time.time; }
            else settleSince = -1f;
            if (settleSince >= 0f && Time.time - settleSince > 0.5f)
            {
                SetParked();
                GoodParking = EvaluateParking();
                Settled?.Invoke(this);
            }
        }
        // Sürerken (ekip içerideyken) oyuncunun kendisi değil ama dışarıdaki oyuncu / bot itilen minibüse çarpılabilir
        // (bot da devrilir: NpcBody gövdesi statik collider olduğundan devrilmezse minibüse duvar olurdu; review P1-2d)
        if (State == VanState.Shoved && hv.magnitude > 1f)
        {
            if (player != null && player.isActiveAndEnabled && InsideFootprint(player.transform.position, 0.4f)) player.KnockDown(hv + Vector3.up * 1.5f);
            if (bot != null && bot.isActiveAndEnabled && !bot.IsDown && InsideFootprint(bot.transform.position, 0.4f)) bot.HitByVehicle(null, hv + Vector3.up * 1.5f);
        }
    }

    private void AddDamage(float amount)
    {
        if (amount <= 0f) return;
        Damage = Mathf.Min(maxDamage, Damage + amount);
    }

    // Dört tekerlek noktasının altındaki zeminden: yukarı vektör ve ortalama yükseklik düzeltmesi (+ = yukarı kaldır).
    private bool GroundPlane(out Vector3 up, out float lift) { return GroundPlane(out up, out lift, out _); }

    // grounded: altında zemin bulunan tekerlek sayısı (0–4); düzlem yalnız dördü de bulununca döner.
    private bool GroundPlane(out Vector3 up, out float lift, out int grounded)
    {
        up = groundUp; lift = 0f; grounded = 0;
        if (wheelPointsLocal == null || wheelPointsLocal.Length < 4) return false;
        var ground = new Vector3[4];
        var world = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            world[i] = transform.TransformPoint(wheelPointsLocal[i]);
            if (GroundBelow(world[i], out RaycastHit hit)) { ground[i] = hit.point; grounded++; }
        }
        if (grounded < 4) return false;
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

    private bool EvaluateParking()
    {
        if (parkingZone == null) return false;
        bool good = parkingZone.Evaluate(Footprint(), LaneSamples());
        if (parkingZone.lanes != null)
            foreach (var lane in parkingZone.lanes)
                if (BlocksLane(lane, out _, out _, out _)) good = false;
        return good;
    }

    // Trafik aracı bekleme süresi dolunca ilerliyor: gövdesi ayak izine değince hasar + itme (araç başına 1.5 sn'de bir).
    public void Ram(TrafficVehicle by)
    {
        if (by == null || by.settings == null) return;
        bool touch = false;
        foreach (var p in LaneSamples()) if (by.Touches(p, 0.45f)) { touch = true; break; }
        if (!touch) return; // temas yokken Parked'ta kal (review P2-2: temas öncesi Shoved↔Parked titremesi yük / NavMesh'i boşa açıp kapatıyordu)
        if (State == VanState.Parked) BeginShove(false);
        else if (PhysicalShove) { PhysicalShove = false; ApplyTireStiffness(scriptedShoveTireStiffness, scriptedShoveTireStiffness); } // trafik çarpması betikli yolu alır
        lastRamContact = Time.time;
        settleSince = -1f;
        Vector3 carFwd = by.transform.forward; carFwd.y = 0f; carFwd.Normalize();
        Vector3 fwdFlat = Forward; fwdFlat.y = 0f; fwdFlat.Normalize();
        var lane = by.Follower != null ? by.Follower.lane : null;
        // Yeni çarpma döngüsü: itilme yönü ve bütçesi döngü başında seçilir (ölçüldü: şerit ortasındaki minibüste yanal işaret ±0
        // gürültüsüne bağlıydı → bir koşuda 5.8 m, öbüründe 18.8 m). Hasar ve ilk itki ARAÇ BAŞINA BİR KEZ (review P1-2: şeridi
        // boşaltamayan minibüse aynı araç her 12 sn'lik döngüde yeniden %12 yazıyordu); araç yok olunca kayıt düşer.
        bool newCycle = lastRammer != by || Time.time - lastRamDamage >= by.settings.vanRamMaxSeconds;
        if (newCycle || !shoveOriginSet)
        {
            shoveOrigin = transform.position;
            shoveOriginSet = true;
            shoveAway = ChooseShoveDirection(lane, out shoveLateralBudget);
        }
        damagedBy.RemoveWhere(v => v == null);
        bool firstHit = !damagedBy.Contains(by);
        // Temas sürdükçe şeridin dışına doğru yanal hız korunur (yoksa araç minibüsü şerit boyunca önüne katıp götürüyordu: ölçüldü 49 m);
        // yanal yol bütçeyle sınırlı: şeridi boşaltmaya yetecek kadar, boş alandan ve maxShoveDistance'tan fazla değil.
        if (lane != null && shoveAway != Vector3.zero)
        {
            float toClear = LateralToClear(lane, shoveAway);
            float travelled = Vector3.Dot(Horizontal(transform.position - shoveOrigin), shoveAway);
            float needed = Mathf.Min(toClear, shoveLateralBudget - travelled, maxShoveDistance - ShoveDistance);
            if (needed > 0.05f)
            {
                float clearSpeed = Mathf.Max(1.5f, Mathf.Sqrt(2f * shovedLateralDecel * Mathf.Clamp(needed, 0.5f, 4f)));
                Vector3 awayFlat = shoveAway - fwdFlat * Vector3.Dot(shoveAway, fwdFlat);
                if (awayFlat.sqrMagnitude > 1e-4f)
                {
                    awayFlat.Normalize();
                    float have = Vector3.Dot(lateral, awayFlat);
                    if (have < clearSpeed) lateral += awayFlat * (clearSpeed - have);
                }
            }
        }
        if (newCycle) { lastRamDamage = Time.time; lastRammer = by; RamCycleStarted?.Invoke(this, by); }
        if (firstHit)
        {
            damagedBy.Add(by);
            Rams++;
            AddDamage(by.settings.vanRamDamage);
            Vector3 push = carFwd * by.settings.vanRamPushSpeed;
            float along = Vector3.Dot(push, fwdFlat);
            Speed += along;
            lateral += push - fwdFlat * along;
            Rammed?.Invoke(this, by);
        }
        // Bütçe doldu: hız eklenmez, kalan hız sönümle biter (araç temasta kalırsa bekleme döngüsüne döner)
        if (ShoveDistance >= maxShoveDistance) { Speed = 0f; lateral = Vector3.zero; }
        if (!rb.isKinematic) rb.linearVelocity = fwdFlat * Speed + lateral + Vector3.up * rb.linearVelocity.y;
    }

    // İtilme yönü: şeridin sağı ya da solu; iki yan da gövde kutusuyla (statik collider) ve tekerlek altı zeminle yoklanır.
    // Seçim: ikisi de şeridi boşaltmaya yetiyorsa zaten kaydığı taraf (|yanal| ≥ 0.4 m), ortadaysa sağ (banket); biri yetiyorsa o;
    // hiçbiri yetmiyorsa daha boş olan (bütçe boş mesafeyle sınırlı). Deterministik: gürültülü yanal işarete bağlı değil.
    private Vector3 ChooseShoveDirection(LanePath lane, out float budget)
    {
        budget = 0f;
        if (lane == null) { LastShoveReason = "şerit yok"; return Vector3.zero; }
        float s = lane.Project(transform.position, out float lat, out _);
        Vector3 right = Flat(lane.RightAt(s));
        float probe = maxShoveDistance + 1f;
        float freeR = FreeLateral(right, probe) - shoveProbeMargin, freeL = FreeLateral(-right, probe) - shoveProbeMargin;
        float needR = LateralToClear(lane, right), needL = LateralToClear(lane, -right);
        bool okR = freeR >= needR, okL = freeL >= needL;
        Vector3 pick;
        string why;
        if (okR && okL) { pick = lat <= -0.4f ? -right : right; why = "iki yan boş, " + (lat <= -0.4f ? "sol (zaten solda)" : "sağ"); }
        else if (okR) { pick = right; why = "sağ boş"; }
        else if (okL) { pick = -right; why = "sol boş"; }
        else { pick = freeR >= freeL ? right : -right; why = "iki yan dar, " + (freeR >= freeL ? "sağ" : "sol") + " daha boş"; }
        budget = Mathf.Max(0f, Mathf.Min(maxShoveDistance, pick == right ? freeR : freeL));
        LastShoveReason = why + " (boş sağ " + freeR.ToString("F1") + " / sol " + freeL.ToString("F1") + " m, gerek sağ " + needR.ToString("F1") + " / sol " + needL.ToString("F1") + " m, bütçe " + budget.ToString("F1") + " m)";
        return pick;
    }

    // Şeridi boşaltmak için away yönünde gereken yanal yol: 9 şerit örneğinin (köşeler dahil) en kötüsünden; kavisli yolda köşe
    // örneği merkezden daha içeride kalabilir (ölçüldü: merkez 3.66 m dışarıdayken ön köşe hâlâ şeritteydi, araç bekle-çarp
    // döngüsüne giriyordu). Şeride yakın olmayan (başka kot) örnekler sayılmaz.
    private float LateralToClear(LanePath lane, Vector3 away)
    {
        float half = lane.width * 0.5f;
        float need = 0f;
        foreach (var p in LaneSamples())
        {
            lane.Project(p, out _, out Vector3 on);
            if (Mathf.Abs(p.y - on.y) > 3f) continue;
            float along = Vector3.Dot(Horizontal(p - on), away);
            need = Mathf.Max(need, half - along + 0.3f);
        }
        return need;
    }

    // Minibüs dir yönünde distance kadar itilebilir mi (statik engel yok, dört tekerleğin altında zemin sürüyor). Hırsız kamyoneti
    // itmeden önce sorar: dayalı ya da uçurum kenarındaki minibüsü itmez.
    // İtme öncesi aranacak boşluk: taban pay + bu hızla gelen minibüsün temas kesilince fren yolu
    public float PushProbeFor(float speed) { return shovePushProbe + speed * speed / (2f * Mathf.Max(0.5f, shoveBrakeDecel)); }

    public bool CanBePushed(Vector3 dir, float distance, Rigidbody pusher = null)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return false;
        return FreeLateral(dir.normalized, distance, pusher) >= distance - 0.01f;
    }

    // Gövde kutusu dir yönünde kaç metre gidebilir: statik collider (kendi parçaları, zemin, hareketli nesneler hariç) ve
    // dört tekerleğin altındaki zemin (0.5 m adımlarla) kesilince biter.
    private float FreeLateral(Vector3 dir, float max, Rigidbody ignore = null)
    {
        float free = max;
        Vector3 center = transform.TransformPoint(new Vector3((bodyMin.x + bodyMax.x) * .5f, (bodyMin.y + .25f + bodyMax.y) * .5f, (bodyMin.z + bodyMax.z) * .5f));
        Vector3 half = new Vector3((bodyMax.x - bodyMin.x) * .5f, (bodyMax.y - bodyMin.y - .25f) * .5f, (bodyMax.z - bodyMin.z) * .5f);
        foreach (var h in Physics.BoxCastAll(center, half, dir, transform.rotation, max, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform) || GroundProbe.IsMovingGameObject(h.collider)) continue;
            if (ignore != null && h.collider.attachedRigidbody == ignore) continue; // itici gövdenin kendisi engel değil
            bool ground = false;
            foreach (var r in groundRoots) if (r != null && h.collider.transform.IsChildOf(r)) ground = true;
            if (ground) continue;
            free = Mathf.Min(free, h.distance);
        }
        for (float d = 0.5f; d <= free; d += 0.5f)
        {
            bool all = true;
            for (int i = 0; i < wheelPointsLocal.Length && all; i++)
                if (!GroundBelow(transform.TransformPoint(wheelPointsLocal[i]) + dir * d, out _)) all = false;
            if (!all) { free = Mathf.Min(free, d - 0.5f); break; }
        }
        return Mathf.Max(0f, free);
    }

    // ---------------------------------------------------------------------------------------------
    // Yük bağlama
    // ---------------------------------------------------------------------------------------------

    private bool InCargo(Vector3 world) { return InCargo(world, 0f); }

    private bool InCargo(Vector3 world, float margin)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return l.x >= cargoMin.x - margin && l.x <= cargoMax.x + margin && l.y >= cargoMin.y - margin && l.y <= cargoMax.y + margin
            && l.z >= cargoMin.z - margin && l.z <= cargoMax.z + margin;
    }

    private static bool IsCrewOrCharacter(Component c)
    {
        return IsCharacter(c) || c.GetComponentInParent<LaneFollower>() != null;
    }

    // Yalnız karakterler (oyuncu, bot, hasta, hırsız); araçlar değil. İtici tespiti (fiziksel itme) bunu kullanır: hırsız
    // kamyoneti / trafik aracı kinematik gövdesiyle park hâlindeki minibüsü itebilmeli.
    private static bool IsCharacter(Component c)
    {
        return c.GetComponentInParent<ThirdPersonPlayer>() != null || c.GetComponentInParent<StretcherHelperBot>() != null
            || c.GetComponentInParent<Patient>() != null || c.GetComponentInParent<Thief>() != null;
    }

    public void HoldCargo()
    {
        if (held.Count > 0) return;
        foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body == null || body == rb || body.transform.IsChildOf(transform) || IsCrewOrCharacter(body)) continue;
            if (!InCargo(body.position)) continue;
            var stretcher = body.GetComponent<StretcherController>();
            if (stretcher != null && (stretcher.CarrierCount > 0 || stretcher.Passenger != null)) continue; // taşınan ya da hastalı sedye bağlanmaz (review P3-2)
            var cone = body.GetComponent<TrafficCone>();
            if (cone != null && cone.IsHeld) continue; // elde taşınan koni bağlanmaz (review P3-2)
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
    // Yük bölümünde ayakta duran ekip (itilme sırasında)
    // ---------------------------------------------------------------------------------------------

    // İtilme başlarken yük bölümünde ayakta duran oyuncu / bot minibüse bağlanıp onunla taşınır (hareket, etkileşim ve collider'lar
    // kapalı: hareketli duvar / zemin collider'larıyla itişmez, havada kalmaz, duvardan geçmez). Minibüs durunca serbest bırakılır
    // ve itilme yönünde yere düşer (ragdoll). Zaten yerde (ragdoll) olan ekip fizikte kalır; oturan ekip zaten bağlıdır.
    private void HoldRiders()
    {
        if (riders.Count > 0) return;
        if (player != null && player.isActiveAndEnabled && !player.IsDown && !CrewInside && InCargo(player.transform.position))
        {
            if (player.HeldStretcher != null) player.HeldStretcher.Release(player);
            riders.Add(Attach(player.transform));
        }
        if (bot != null && bot.isActiveAndEnabled && !bot.IsDown && !CrewInside && InCargo(bot.transform.position))
        {
            bot.ReleaseHeld();
            riders.Add(Attach(bot.transform));
        }
    }

    private SeatedCrew Attach(Transform root)
    {
        var crew = new SeatedCrew { root = root, parent = root.parent, animator = root.GetComponent<Animator>() };
        DisableForSeat(crew);
        root.SetParent(transform, true);
        return crew;
    }

    private void ReleaseRiders()
    {
        if (riders.Count == 0) return;
        Vector3 dir = Horizontal(lastShoveVelocity);
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Flat(transform.forward);
        Vector3 knock = dir * riderKnockSpeed + Vector3.up * 1f;
        foreach (var crew in riders)
        {
            if (crew.root == null) continue;
            crew.root.SetParent(crew.parent, true);
            foreach (var c in crew.disabledColliders) if (c != null) c.enabled = true;
            foreach (var b in crew.disabled) if (b != null) b.enabled = true;
            if (player != null && crew.root == player.transform) { player.Teleport(player.transform.position, player.transform.rotation); player.KnockDown(knock); }
            else if (bot != null && crew.root == bot.transform) bot.HitByVehicle(null, knock);
            RidersKnocked++;
        }
        riders.Clear();
        Physics.SyncTransforms();
    }

    // ---------------------------------------------------------------------------------------------
    // Ekip: oturma ve inme
    // ---------------------------------------------------------------------------------------------

    // Sürüş girişi ve testler: oyuncu şoför, bot yolcu koltuğuna (withBot=false: yalnız oyuncu).
    public void SeatCrew() { SeatCrew(true); }

    public void SeatCrew(bool withBot)
    {
        if (CrewInside) return;
        if (State == VanState.Parked) SetDriving(); // park hâlinde oturtulursa (test / API) ekip inebilsin: TryExit yalnız Driving'de (review P3-1)
        CrewInside = true;
        HasExited = false;
        cameraYawPending = true;
        if (player != null) Seat(player.transform, driverSeat, 1);
        if (withBot && bot != null) Seat(bot.transform, passengerSeat, 2);
        if (orbitCamera != null)
        {
            savedDistance = orbitCamera.distance;
            savedPivot = orbitCamera.pivotHeight;
            savedPitch = orbitCamera.Pitch;
            savedEyeUp = orbitCamera.eyeUp;
            savedEyeForward = orbitCamera.eyeForward;
            orbitCamera.eyeForward = driveEyeForward;
            orbitCamera.distance = driveCameraDistance;
            orbitCamera.pivotHeight = driveCameraPivotHeight;
            orbitCamera.obstructionIgnoreRoot = transform;
            orbitCamera.Yaw = HeadingYaw;
            orbitCamera.eyeUp += driveEyeUp;
            lastCameraMode = orbitCamera.Mode;
            if (lastCameraMode == OrbitCamera.ViewMode.FirstPerson) orbitCamera.Pitch = driveFirstPersonPitch;
            lastHeadingYaw = HeadingYaw;
        }
        if (!testInput) EnableInput();
    }

    // Yeniden binme koşulu: minibüs park hâlinde (itilmiyor), ekip dışarıda, oyuncu ayakta ve etkin.
    public bool CanBoard()
    {
        return !CrewInside && State == VanState.Parked && player != null && player.isActiveAndEnabled && !player.IsDown;
    }

    // Oyuncu şoför kapısı noktasına yatay ≤ boardRange m'de mi (aynı kotta)?
    public bool PlayerAtDriverDoor()
    {
        if (player == null) return false;
        Vector3 d = player.transform.position - DriverDoorPoint;
        float dy = Mathf.Abs(d.y);
        d.y = 0f;
        return d.magnitude <= boardRange && dy < 1.5f;
    }

    // Bot da biner mi: yakın, etkin, yerde değil, hastalı sedye taşımıyor (boş sedyeyi bırakır).
    public bool BotCanBoard()
    {
        if (bot == null || !bot.isActiveAndEnabled || bot.IsDown) return false;
        if (bot.HeldStretcher != null && bot.HeldStretcher.Passenger != null) return false;
        Vector3 d = bot.transform.position - transform.position; d.y = 0f;
        return d.magnitude <= botBoardRange;
    }

    // E (PlayerInteraction.BoardVan): oyuncu şoför koltuğuna, uygunsa bot yolcu koltuğuna; sürüş yeniden başlar.
    public bool Board()
    {
        if (!CanBoard()) return false;
        bool withBot = BotCanBoard();
        if (withBot) bot.ReleaseHeld();
        SeatCrew(withBot);
        boardedAt = Time.time;
        Boardings++;
        GameNotice.Show(BoardedText);
        Boarded?.Invoke(this);
        return true;
    }

    // Yük bölümünde (koltukta değil) ayakta ya da yerde ekip var mı: trafik çarpmaz, korna çalıp bekler (TrafficVehicle okur).
    public bool CrewInCargo
    {
        get
        {
            if (player != null && player.isActiveAndEnabled && !player.transform.IsChildOf(transform))
            {
                Vector3 p = player.IsDown && player.knockdown != null ? player.knockdown.GroundPosition : player.transform.position;
                if (InCargo(p, 0.3f)) return true;
            }
            if (bot != null && bot.isActiveAndEnabled && !bot.transform.IsChildOf(transform) && InCargo(bot.GroundPosition, 0.3f)) return true;
            return false;
        }
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
        if (Time.time - boardedAt < exitLockoutSeconds) return false; // biniş basışı / çift E inişe dönüşmesin
        Vector3 hv = rb.isKinematic ? Vector3.zero : rb.linearVelocity; hv.y = 0f;
        if (State != VanState.Driving || Mathf.Abs(Speed) > 0.3f || hv.magnitude > 0.3f)
        {
            GameNotice.Show(NoExitWhileMovingText);
            return false;
        }
        return Exit();
    }

    public bool Exit()
    {
        if (!CrewInside) return false;
        // Her iki ekip üyesinin yeri önceden bulunur; biri sıkışırsa oturma ve görev durumu değişmez.
        var exitPoints = new List<Vector3>();
        foreach (var crew in seated)
        {
            if (crew.root == null) { exitPoints.Add(default); continue; }
            bool isPlayer = player != null && crew.root == player.transform;
            if (!ExitPoint(isPlayer, exitPoints, out Vector3 point))
            {
                GameNotice.Show(NoSafeExitText);
                return false;
            }
            exitPoints.Add(point);
        }
        Throttle = 0f; Steer = 0f;
        ClearTestInput();
        DisableInput();
        AlignToGroundNow();
        SetParked();
        UnseatCrew(exitPoints);
        CrewInside = false;
        HasExited = true;
        Exits++;
        GoodParking = EvaluateParking();
        if (sound != null && sound.engine != null) sound.engine.Stop();
        Exited?.Invoke(this);
        return true;
    }

    private void UnseatCrew(List<Vector3> exitPoints)
    {
        for (int i = 0; i < seated.Count; i++)
        {
            var crew = seated[i];
            if (crew.root == null) continue;
            crew.root.SetParent(crew.parent, true);
            SetSeated(crew.animator, 0);
            bool isPlayer = player != null && crew.root == player.transform;
            Vector3 point = exitPoints[i];
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
            orbitCamera.eyeUp = savedEyeUp;
            orbitCamera.eyeForward = savedEyeForward;
            if (orbitCamera.Mode == OrbitCamera.ViewMode.FirstPerson) orbitCamera.Pitch = savedPitch;
            orbitCamera.obstructionIgnoreRoot = null;
        }
        Physics.SyncTransforms();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
    private static Vector3 Horizontal(Vector3 v) { v.y = 0f; return v; }

    // İniş noktası: iki yan kapı ve arka seçenekler; her adayda yakın zemin ve boş kapsül zorunlu.
    private bool ExitPoint(bool driver, List<Vector3> used, out Vector3 point)
    {
        float seatZ = driverSeat != null ? driverSeat.localPosition.z : bodyMin.z + 1.6f;
        float side = bodyMax.x + 0.9f;
        var candidates = driver
            ? new[] { new Vector3(side, 0f, seatZ), new Vector3(side, 0f, bodyMax.z + 0.5f), new Vector3(0f, 0f, bodyMax.z + 2.2f), new Vector3(-side, 0f, seatZ) }
            : new[] { new Vector3(-side, 0f, seatZ), new Vector3(-side, 0f, bodyMax.z + 0.5f), new Vector3(0.9f, 0f, bodyMax.z + 2.2f), new Vector3(side, 0f, seatZ) };
        float baseY = transform.TransformPoint(new Vector3(0f, bodyMin.y, 0f)).y;
        foreach (var local in candidates)
        {
            Vector3 world = transform.TransformPoint(local);
            if (!GroundBelow(world + Vector3.up * 0.5f, out RaycastHit hit)) continue;
            Vector3 p = hit.point;
            if (p.y < baseY - 1.5f) continue;
            bool tooClose = false;
            foreach (var u in used) if (Vector3.Distance(u, p) < 1f) tooClose = true;
            if (tooClose) continue;
            if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.3f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
            point = p;
            return true;
        }
        point = default;
        return false;
    }

    // Test: hasarı sıfırlar (senaryolar arası).
    public void ResetDamageForTest() { Damage = 0f; Rams = 0; Impacts = 0; damagedBy.Clear(); }
}
