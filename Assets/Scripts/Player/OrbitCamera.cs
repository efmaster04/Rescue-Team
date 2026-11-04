using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Fareyle dönen yörünge kamera (kullanıcı kararı 15). Hedefin arkasında durur, araya duvar girerse yaklaşır.
// İmleç: Start'ta kilitlenir. Editörde Esc imleci serbest bırakır (kullanıcı şikâyeti 2026-11-04: sürüş sırasında Esc'den sonra
// girdi "ölü" sanılıyordu); serbest imleçle ve açık UI yokken (PlayerInputReader.InputBlocked kapalı) oyun penceresine sol tık
// imleci yeniden kilitler. O tık saldırı / fırlatma sayılmaz (RelockedThisFrame; PlayerInputReader okur). Update -50: girdi
// okuyucusundan önce koşar.
// FP (kullanıcı kararı, 2026-10-28): V ile birinci şahıs ↔ üçüncü şahıs. Birinci şahısta kamera baş kemiğindeki göz noktasında
// durur, gövde kamera yönüne bakar (ThirdPersonPlayer.FaceCameraYaw), baş FirstPersonBody ile gizlenir. İki yönde de
// transitionSeconds süren yumuşak geçiş (konum SmoothStep ile karışır; dönüş iki modda da aynı Yaw/Pitch). Son seçim
// PlayerPrefs'te saklanır; ilk açılışta üçüncü şahıs. Yaw/Pitch/target/distance API'si korunur (doğrulamalar yazar).
// Kullanıcı istekleri (2026-11-05):
//  - Tekerlek yakınlaştırma (yalnız üçüncü şahıs): etkin uzaklık = distance × ZoomScale. distance / pivotHeight'ı başka kodlar
//    yazar (minibüs sürüşü 7.5 m, garaj); çarpan onların üstüne biner, onları değiştirmez. Seçim PlayerPrefs'te. UI açıkken
//    (PlayerInputReader.InputBlocked; garajda CompanyPlayerBridge yazar) ya da oyunda imleç serbestken tekerlek yok sayılır.
//  - Küçük / hareketli fizik nesneleri (koni, valiz, sedye, halat, balya, garaj kutuları, karakterler) engel sayılmaz: kamerayı
//    karakterin dibine çekmez. Duvar, kaya, minibüs gövdesi gibi büyük statik engeller yine engeldir (IsObstruction).
//  - Engele yaklaşınca hızlı ama yumuşak içeri çekilme (iki küre: geniş "önsezi" küresi erken ve yumuşak çeker, dar küre duvarın
//    içine girmeyi kesin engeller), engel kalkınca gecikmeli ve yavaş geri açılma. Gecikme engel kenarındaki ileri-geri titremeyi
//    keser: açılma ancak hedef releaseDelay boyunca kesintisiz uzaktaysa başlar, başlayınca hedefe yumuşakça gider.
[DefaultExecutionOrder(-50)]
public class OrbitCamera : MonoBehaviour
{
    public enum ViewMode { ThirdPerson = 0, FirstPerson = 1 }

    public const string ViewModePrefsKey = "Camera.ViewMode";
    public const string ZoomPrefsKey = "Camera.Zoom";

    public Transform target;
    [Tooltip("Hedefe uzaklık (m). Tekerlek çarpanı (ZoomScale) bunun üstüne biner; başka kodlar (sürüş, garaj) bunu yazar.")]
    public float distance = 4.5f;
    [Tooltip("Bakılan nokta, hedefin ayak hizasından bu kadar yukarıda (m).")]
    public float pivotHeight = 1.3f;
    [Tooltip("Fare hassasiyeti (derece / piksel).")]
    public float sensitivity = 0.12f;
    public float minPitch = -20f, maxPitch = 70f;

    [Header("Tekerlek yakınlaştırma (yalnız üçüncü şahıs)")]
    [Tooltip("Çarpan sınırları: yaya 4.5 m → 2.25–7.2 m, sürüşte 7.5 m → 3.75–12 m.")]
    public float minZoom = 0.5f, maxZoom = 1.6f;
    [Tooltip("Bir tekerlek çentiği çarpanı bu oranda değiştirir (yukarı = yaklaş).")]
    public float zoomStepRatio = 1.12f;
    [Tooltip("Çarpanın hedefe yumuşak geçiş süresi (sn).")]
    public float zoomSmoothTime = 0.12f;

    [Header("Engel (üçüncü şahıs)")]
    [Tooltip("Kesin sınır küresi (m): kamera bu kürenin değdiği yerden (eksi pay) öteye geçmez, duvarın içine girmez.")]
    public float collisionRadius = 0.2f;
    [Tooltip("Önsezi küresi (m): engel dar küreye değmeden çekilmeye başlanır, böylece içeri çekilme sıçramaz.")]
    public float anticipationRadius = 0.35f;
    [Tooltip("Engelden bırakılan pay (m).")]
    public float obstaclePadding = 0.1f;
    [Tooltip("Engel ne kadar yakın olursa olsun kamera pivottan en az bu kadar uzakta (m).")]
    public float minObstructedDistance = 0.6f;
    [Tooltip("İçeri çekilme zaman sabiti (sn; küçük = hızlı).")]
    public float pullInTime = 0.06f;
    [Tooltip("Engel kalkınca geri açılmadan önce beklenen süre (sn); kenarda titremeyi keser.")]
    public float releaseDelay = 0.25f;
    [Tooltip("Geri açılma yumuşatma süresi (sn) ve en yüksek hızı (m/sn).")]
    public float releaseTime = 0.35f, releaseMaxSpeed = 8f;
    [Tooltip("En büyük boyutu bundan küçük collider engel sayılmaz (m).")]
    public float smallObstacleSize = 0.5f;
    [Tooltip("Rigidbody'li (kinematik de olsa) ve en büyük boyutu bundan küçük nesne engel sayılmaz (m): koni, garaj kutuları.")]
    public float smallPropSize = 1.5f;

    [Header("Birinci şahıs (V)")]
    [Tooltip("Mod değişiminde kameranın gittiği süre (sn), iki yönde de.")]
    public float transitionSeconds = 0.25f;
    [Tooltip("Birinci şahısta eğim sınırı: yukarı (negatif) / aşağı (pozitif). Aşağı sınır gövdenin içini göstermeyecek kadar.")]
    public float firstPersonMinPitch = -70f, firstPersonMaxPitch = 80f;
    [Tooltip("Göz noktası: baş kemiğinden yukarı (m) ve kamera yönünde ileri (m). İleri pay, aşağı bakınca omuz yerine göğüs / kollar / kemerin görünmesini sağlar (ölçüldü: 0.11'de omuzlar kadrajı dolduruyordu).")]
    public float eyeUp = 0.07f, eyeForward = 0.16f;
    [Tooltip("Kamera baş kemiğine bundan yakınsa baş gizlenir (m).")]
    public float headHideRadius = 0.35f;
    [Tooltip("Birinci şahısta yakın kırpma düzlemi (m); eller ve sedye kameraya yakın olduğundan küçük tutulur. Üçüncü şahısta kameranın kendi değeri geri gelir.")]
    public float firstPersonNearClip = 0.03f;

    [Tooltip("Sürüş girişi: bu kökün altındaki collider'lar kamerayı yaklaştırmaz (oyuncu minibüsün içindeyken minibüsün kendisi engel sayılmaz). ServiceVanDriver yazar.")]
    public Transform obstructionIgnoreRoot;

    public float Yaw { get; set; }
    public float Pitch { get; set; } = 18f;

    // Testler (Bolum1FirstPersonVerify dışındaki doğrulamalar): kayıtlı seçim yok sayılır, üçüncü şahısta başlanır; böylece
    // editörde FP bırakılmış olması ambulans / animasyon görüntülerini değiştirmez. Sahne yüklenmeden önce ayarlanır.
    // Kayıtlı tekerlek çarpanı da yok sayılır (çarpan 1).
    public static bool IgnoreSavedMode;

    public ViewMode Mode { get; private set; } = ViewMode.ThirdPerson;
    // 0 = tamamen üçüncü şahıs, 1 = tamamen birinci şahıs (geçiş sırasında arada)
    public float Blend { get; private set; }
    public bool Transitioning { get { return Mode == ViewMode.FirstPerson ? Blend < 1f : Blend > 0f; } }
    // Bu karede kamera başın içinde mi (FirstPersonBody okur; LateUpdate 0'da hesaplanır, gövde 400'de uygular)
    public bool HeadInsideCamera { get; private set; }
    // Baş kemiği (hedefin Humanoid Animator'ından; yoksa pivot kullanılır)
    public Transform Head { get; private set; }
    // Tanı: son geçişin süresi (sn; mod değişiminden Blend'in hedefe varmasına geçen oyun zamanı)
    public float LastTransitionSeconds { get; private set; }
    public int ModeChanges { get; private set; }
    // İmleç bu karede sol tıkla yeniden kilitlendi (o tık oyuna saldırı olarak gitmez)
    public bool RelockedThisFrame { get { return relockFrame == Time.frameCount; } }
    public int Relocks { get; private set; }

    // Tekerlek: hedef çarpan (girdi yazar) ve yumuşatılmış çarpan (kameraya uygulanan)
    public float TargetZoom { get; private set; } = 1f;
    public float ZoomScale { get; private set; } = 1f;
    // Engelsiz üçüncü şahıs uzaklığı (distance × ZoomScale) ve kameranın pivottan gerçek uzaklığı (engel ve yumuşatma sonrası)
    public float FreeDistance { get { return distance * ZoomScale; } }
    public float CurrentDistance { get; private set; } = -1f;
    // Bu karede engel var mı, son engel hangisi (tanı)
    public bool Obstructed { get; private set; }
    public string LastObstacle { get; private set; } = "";
    // Tanı: kabul edilen / reddedilen (FP, UI açık) tekerlek olayları
    public int ZoomInputs { get; private set; }
    public int ZoomInputsBlocked { get; private set; }

    private static readonly RaycastHit[] hits = new RaycastHit[16];
    private static readonly List<OrbitCamera> instances = new List<OrbitCamera>();
    private int relockFrame = -1;
    private PlayerInputReader inputReader;
    private Camera cam;
    private float defaultNearClip = -1f;
    private float transitionStart = -1f;
    private Transform resolvedFor;
    private InputAction zoomAction;
    private float zoomVelocity;
    private float lengthVelocity;
    private float extendSince = -1f;
    private bool recovering, extending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instances.Clear(); }

    // Eşyayı taşıyanı izleyen kamera birinci şahısta mı (0..1, geçişte arada). Taşınan eşyalar FP'de görüşü kapatmayacak yere
    // kayar (RopeCoil, TrafficCone okur).
    public static float FirstPersonBlendFor(Transform holder)
    {
        if (holder == null) return 0f;
        for (int i = 0; i < instances.Count; i++)
        {
            var o = instances[i];
            if (o != null && o.target == holder) return o.Blend;
        }
        return 0f;
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        // Son seçim (ilk açılışta anahtar yok → üçüncü şahıs)
        Mode = !IgnoreSavedMode && PlayerPrefs.GetInt(ViewModePrefsKey, (int)ViewMode.ThirdPerson) == (int)ViewMode.FirstPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson;
        Blend = Mode == ViewMode.FirstPerson ? 1f : 0f;
        TargetZoom = IgnoreSavedMode ? 1f : Mathf.Clamp(PlayerPrefs.GetFloat(ZoomPrefsKey, 1f), minZoom, maxZoom);
        ZoomScale = TargetZoom;
        zoomAction = new InputAction("CameraZoom", InputActionType.Value, "<Mouse>/scroll/y");
    }

    private void OnEnable()
    {
        if (!instances.Contains(this)) instances.Add(this);
        if (zoomAction != null) zoomAction.Enable();
    }

    private void Start()
    {
        if (target != null) Yaw = target.eulerAngles.y;
        ResolveTarget();
        if (!Application.isBatchMode)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        instances.Remove(this);
        if (zoomAction != null) zoomAction.Disable();
        PlayerPrefs.Save();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDestroy()
    {
        if (zoomAction != null) zoomAction.Dispose();
    }

    private void Update()
    {
        ReadZoomInput();
        if (Application.isBatchMode || Cursor.lockState == CursorLockMode.Locked) return;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        TryRelockCursor();
    }

    // UI açık mı (bilgisayar ekranı / duraklatma menüsü; garajda köprü InputBlocked'ı yazar). Oyunda serbest imleç de UI sayılır
    // (Esc ya da açık pencere); batchmode'da imleç hiç kilitlenmez, orada yalnız InputBlocked bakılır.
    public bool UiOpen
    {
        get
        {
            if (inputReader == null && target != null) inputReader = target.GetComponent<PlayerInputReader>();
            if (inputReader == null) inputReader = FindAnyObjectByType<PlayerInputReader>();
            if (inputReader != null && inputReader.InputBlocked) return true;
            return !Application.isBatchMode && Cursor.lockState != CursorLockMode.Locked;
        }
    }

    // Bir tekerlek çentiğinin değeri: Input System varsayılanı (UniformAcrossAllPlatforms) ±1, platforma özgü ayarda Windows ±120.
    // Dokunmatik yüzeyin küçük değerleri böylece çentiğin kesri olur.
    public static float ScrollPerNotch
    {
        get { return InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange ? 120f : 1f; }
    }

    // Tekerlek: yalnız üçüncü şahısta (geçiş sırasında değil) ve UI kapalıyken.
    private void ReadZoomInput()
    {
        if (zoomAction == null) return;
        float raw = zoomAction.ReadValue<float>();
        if (Mathf.Abs(raw) < 0.01f) return;
        if (Mode != ViewMode.ThirdPerson || Blend > 0f || UiOpen) { ZoomInputsBlocked++; return; }
        float notches = raw / ScrollPerNotch;
        ZoomInputs++;
        Zoom(notches);
    }

    // Çentik kadar yakınlaş (pozitif) / uzaklaş (negatif). Seçim PlayerPrefs'e yazılır (diske OnDisable'da).
    public void Zoom(float notches)
    {
        float next = Mathf.Clamp(TargetZoom * Mathf.Pow(zoomStepRatio, -notches), minZoom, maxZoom);
        if (Mathf.Approximately(next, TargetZoom)) return;
        TargetZoom = next;
        PlayerPrefs.SetFloat(ZoomPrefsKey, TargetZoom);
    }

    // Çarpanı doğrudan ver (test / ayar menüsü); instant: yumuşatmasız. PlayerPrefs'e yazılmaz.
    public void SetZoom(float scale, bool instant)
    {
        TargetZoom = Mathf.Clamp(scale, minZoom, maxZoom);
        if (!instant) return;
        ZoomScale = TargetZoom;
        zoomVelocity = 0f;
    }

    // Serbest imleci oyun penceresine kilitler; açık UI varken (girdi kilitli) dokunmaz. true: kilitlendi.
    public bool TryRelockCursor()
    {
        if (inputReader == null) inputReader = FindAnyObjectByType<PlayerInputReader>();
        if (inputReader != null && inputReader.InputBlocked) return false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        relockFrame = Time.frameCount;
        Relocks++;
        return true;
    }

    // Hedef değişmişse baş kemiğini, gövde bileşenini ve yön kilidini yeniden bağla (sahnede bileşen eksikse çalışma zamanında kurulur).
    private void ResolveTarget()
    {
        if (target == resolvedFor) return;
        resolvedFor = target;
        Head = null;
        if (target == null) return;
        var animator = target.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman) Head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (animator != null && target.GetComponent<FirstPersonBody>() == null) target.gameObject.AddComponent<FirstPersonBody>();
        ApplyModeToTarget();
    }

    private void ApplyModeToTarget()
    {
        if (target == null) return;
        var player = target.GetComponent<ThirdPersonPlayer>();
        if (player != null) player.FaceCameraYaw = Mode == ViewMode.FirstPerson;
    }

    // V: mod değiştir (PlayerInputReader çağırır). Seçim hemen kaydedilir.
    public void ToggleMode() { SetMode(Mode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson, false); }

    public void SetMode(ViewMode mode, bool instant)
    {
        if (mode != Mode)
        {
            Mode = mode;
            ModeChanges++;
            transitionStart = Time.time;
            PlayerPrefs.SetInt(ViewModePrefsKey, (int)mode);
            PlayerPrefs.Save();
        }
        if (instant) { Blend = mode == ViewMode.FirstPerson ? 1f : 0f; transitionStart = -1f; }
        ApplyModeToTarget();
    }

    // Girdi: fare hareketi (piksel).
    public void Rotate(Vector2 delta)
    {
        Yaw += delta.x * sensitivity;
        Pitch = Mathf.Clamp(Pitch - delta.y * sensitivity, CurrentMinPitch, CurrentMaxPitch);
    }

    // Eğim sınırları geçiş boyunca iki mod arasında karışır (FP'den TP'ye dönerken eğim sıçramaz).
    public float CurrentMinPitch { get { return Mathf.Lerp(minPitch, firstPersonMinPitch, Blend); } }
    public float CurrentMaxPitch { get { return Mathf.Lerp(maxPitch, firstPersonMaxPitch, Blend); } }

    // Birinci şahıs göz noktası (baş kemiği + yukarı + kamera yönünde ileri; baş yoksa pivot yüksekliği + 0.3)
    public Vector3 EyePoint()
    {
        Vector3 flatForward = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        Vector3 head = Head != null ? Head.position : target.position + Vector3.up * (pivotHeight + 0.3f);
        return head + Vector3.up * eyeUp + flatForward * eyeForward;
    }

    // Collider kamerayı yaklaştıran engel mi? Hareketli / taşınan nesneler, karakterler, eşyalar ve küçük fizik nesneleri değil.
    // Gerekçe (kullanıcı isteği 2): küçük nesne karakterin önünü ancak biraz kapatır; onun yüzünden kameranın karakterin dibine
    // çekilmesi çok daha kötü bir görüntüdür. Yarı saydamlık yerine yok sayma seçildi: her URP malzemesine saydam kopya gerekmez,
    // kamera nesnenin içine düşerse arka yüz ayıklaması onu zaten görünmez kılar.
    public bool IsObstruction(Collider c)
    {
        if (c == null || GroundProbe.IsMovingGameObject(c)) return false;
        Transform t = c.transform;
        if (target != null && t.IsChildOf(target)) return false;
        if (obstructionIgnoreRoot != null && t.IsChildOf(obstructionIgnoreRoot)) return false;
        Vector3 size = c.bounds.size;
        float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        if (largest <= smallObstacleSize) return false;
        if (c.attachedRigidbody != null && largest <= smallPropSize) return false;
        if (c.GetComponentInParent<LootItem>() != null || c.GetComponentInParent<LooseBale>() != null || c.GetComponentInParent<RopeCoil>() != null) return false;
        var animator = c.GetComponentInParent<Animator>();
        if (animator != null && animator.isHuman) return false; // karakterler (CharacterController'sızlar da)
        return true;
    }

    // Pivottan dir yönünde radius'lu küre; ilk engelin uzaklığı (yoksa maxLength). Başlangıçta içinde kalınan collider sayılmaz.
    private float CastObstacle(Vector3 pivot, Vector3 dir, float radius, float maxLength, out Collider hitCollider)
    {
        hitCollider = null;
        float best = maxLength;
        int n = Physics.SphereCastNonAlloc(pivot, radius, dir, hits, maxLength, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].distance <= 0f || hits[i].distance >= best || !IsObstruction(hits[i].collider)) continue;
            best = hits[i].distance;
            hitCollider = hits[i].collider;
        }
        return best;
    }

    // Üçüncü şahıs kamera uzaklığı: engel hedefi (önsezi küresi) ve kesin sınır (dar küre); içeri hızlı, dışarı gecikmeli ve yavaş.
    private float ThirdPersonLength(Vector3 pivot, Vector3 dir, float dt)
    {
        float free = FreeDistance;
        float soft = CastObstacle(pivot, dir, anticipationRadius, free, out Collider softHit);
        float hard = CastObstacle(pivot, dir, collisionRadius, free, out Collider hardHit);
        bool blocked = softHit != null || hardHit != null;
        float hardLimit = hardHit != null ? Mathf.Max(minObstructedDistance, hard - obstaclePadding) : free;
        float goal = softHit != null ? Mathf.Max(minObstructedDistance, soft - obstaclePadding) : free;
        goal = Mathf.Min(goal, hardLimit);
        Obstructed = blocked;
        if (blocked) { LastObstacle = (hardHit != null ? hardHit : softHit).name; recovering = true; }

        float cur = CurrentDistance < 0f ? goal : CurrentDistance;
        if (goal < cur)
        {
            // İçeri: hızlı ama yumuşak (üstel, pullInTime). Kesin sınır en sonda ayrıca uygulanır.
            cur = Mathf.Lerp(cur, goal, dt > 0f ? 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, pullInTime)) : 0f);
            lengthVelocity = 0f;
            extendSince = -1f;
            extending = false;
        }
        else if (!recovering && goal - cur <= 0.25f)
        {
            cur = goal; // engel yok, küçük fark: tekerlek çarpanı zaten yumuşak
        }
        else
        {
            // Dışarı. Açılma başlamadıysa: hedef releaseDelay boyunca kesintisiz uzakta olmalı; engelliyken ≤ 5 cm'lik farklar sayacı
            // sıfırlar (engel kenarında hedef ileri-geri oynasa da kamera yerinde kalır). Engel hiç yoksa (ör. sürüşte distance
            // büyüdü) beklemeden yumuşakça açılır.
            if (!extending)
            {
                float deadband = blocked ? 0.05f : 0f;
                if (goal - cur > deadband) { if (extendSince < 0f) extendSince = Time.time; }
                else extendSince = -1f;
                if (extendSince >= 0f && (Time.time - extendSince >= releaseDelay || !recovering)) extending = true;
            }
            if (extending && dt > 0f) cur = Mathf.SmoothDamp(cur, goal, ref lengthVelocity, releaseTime, releaseMaxSpeed, dt);
            if (!blocked && cur >= goal - 0.002f) { cur = goal; recovering = false; extending = false; lengthVelocity = 0f; extendSince = -1f; }
        }
        return Mathf.Min(cur, hardLimit); // duvarın içine asla
    }

    private void LateUpdate()
    {
        if (target == null) return;
        ResolveTarget();
        float dt = Time.deltaTime;

        // Geçiş: Blend hedefe doğru sabit hızla gider (transitionSeconds'ta 0↔1)
        float goal = Mode == ViewMode.FirstPerson ? 1f : 0f;
        if (Blend != goal)
        {
            Blend = Mathf.MoveTowards(Blend, goal, dt / Mathf.Max(0.01f, transitionSeconds));
            if (Blend == goal && transitionStart >= 0f) { LastTransitionSeconds = Time.time - transitionStart; transitionStart = -1f; }
        }
        Pitch = Mathf.Clamp(Pitch, CurrentMinPitch, CurrentMaxPitch);
        TargetZoom = Mathf.Clamp(TargetZoom, minZoom, maxZoom);
        if (dt > 0f) ZoomScale = Mathf.SmoothDamp(ZoomScale, TargetZoom, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
        if (Mathf.Abs(ZoomScale - TargetZoom) < 0.0005f) { ZoomScale = TargetZoom; zoomVelocity = 0f; }

        Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
        Vector3 position;
        Vector3 tpPosition = Vector3.zero;
        if (Blend < 1f)
        {
            // Kamera ile hedef arasına giren büyük statik engel varsa yaklaş (IsObstruction: taşınan sedye, karakterler ve küçük
            // fizik nesneleri engel sayılmaz).
            Vector3 pivot = target.position + Vector3.up * pivotHeight;
            Vector3 dir = rot * Vector3.back;
            CurrentDistance = ThirdPersonLength(pivot, dir, dt);
            tpPosition = pivot + dir * CurrentDistance;
        }
        else
        {
            // Tam FP: engel yumuşatması sıfırlanır (TP'ye dönünce eski değerden başlamaz)
            CurrentDistance = -1f;
            recovering = false;
            extending = false;
            Obstructed = false;
        }
        if (Blend <= 0f) position = tpPosition;
        else if (Blend >= 1f) position = EyePoint();
        else position = Vector3.Lerp(tpPosition, EyePoint(), Mathf.SmoothStep(0f, 1f, Blend));
        transform.SetPositionAndRotation(position, rot);

        // Baş: kamera başa girdiği anda gizlenir, çıkınca görünür (FirstPersonBody uygular)
        Vector3 headCenter = Head != null ? Head.position + Vector3.up * eyeUp : EyePoint();
        HeadInsideCamera = Vector3.Distance(position, headCenter) < headHideRadius;

        // Yakın kırpma: başın içindeyken küçük (eller / sedye kesilmesin), dışındayken kameranın kendi değeri
        if (cam != null)
        {
            if (defaultNearClip < 0f) defaultNearClip = cam.nearClipPlane;
            cam.nearClipPlane = HeadInsideCamera ? Mathf.Min(defaultNearClip, firstPersonNearClip) : defaultNearClip;
        }
    }
}
