using UnityEngine;

// Fareyle dönen yörünge kamera (kullanıcı kararı 15). Hedefin arkasında durur, araya duvar girerse yaklaşır.
// FP (kullanıcı kararı, 2026-10-28): V ile birinci şahıs ↔ üçüncü şahıs. Birinci şahısta kamera baş kemiğindeki göz noktasında
// durur, gövde kamera yönüne bakar (ThirdPersonPlayer.FaceCameraYaw), baş FirstPersonBody ile gizlenir. İki yönde de
// transitionSeconds süren yumuşak geçiş (konum SmoothStep ile karışır; dönüş iki modda da aynı Yaw/Pitch). Son seçim
// PlayerPrefs'te saklanır; ilk açılışta üçüncü şahıs. Yaw/Pitch/target/distance API'si korunur (doğrulamalar yazar).
public class OrbitCamera : MonoBehaviour
{
    public enum ViewMode { ThirdPerson = 0, FirstPerson = 1 }

    public const string ViewModePrefsKey = "Camera.ViewMode";

    public Transform target;
    [Tooltip("Hedefe uzaklık (m).")]
    public float distance = 4.5f;
    [Tooltip("Bakılan nokta, hedefin ayak hizasından bu kadar yukarıda (m).")]
    public float pivotHeight = 1.3f;
    [Tooltip("Fare hassasiyeti (derece / piksel).")]
    public float sensitivity = 0.12f;
    public float minPitch = -20f, maxPitch = 70f;

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

    private static readonly RaycastHit[] hits = new RaycastHit[16];
    private Camera cam;
    private float defaultNearClip = -1f;
    private float transitionStart = -1f;
    private Transform resolvedFor;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        // Son seçim (ilk açılışta anahtar yok → üçüncü şahıs)
        Mode = !IgnoreSavedMode && PlayerPrefs.GetInt(ViewModePrefsKey, (int)ViewMode.ThirdPerson) == (int)ViewMode.FirstPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson;
        Blend = Mode == ViewMode.FirstPerson ? 1f : 0f;
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
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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

    private void LateUpdate()
    {
        if (target == null) return;
        ResolveTarget();

        // Geçiş: Blend hedefe doğru sabit hızla gider (transitionSeconds'ta 0↔1)
        float goal = Mode == ViewMode.FirstPerson ? 1f : 0f;
        if (Blend != goal)
        {
            Blend = Mathf.MoveTowards(Blend, goal, Time.deltaTime / Mathf.Max(0.01f, transitionSeconds));
            if (Blend == goal && transitionStart >= 0f) { LastTransitionSeconds = Time.time - transitionStart; transitionStart = -1f; }
        }
        Pitch = Mathf.Clamp(Pitch, CurrentMinPitch, CurrentMaxPitch);

        Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
        Vector3 position;
        Vector3 tpPosition = Vector3.zero;
        if (Blend < 1f)
        {
            Vector3 pivot = target.position + Vector3.up * pivotHeight;
            Vector3 dir = rot * Vector3.back;
            float length = distance;
            // Kamera ile hedef arasına giren statik engel varsa yaklaş (taşınan sedye, hastalar ve karakterler engel sayılmaz).
            int n = Physics.SphereCastNonAlloc(pivot, 0.2f, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance <= 0f || GroundProbe.IsMovingGameObject(hits[i].collider)) continue;
                if (obstructionIgnoreRoot != null && hits[i].collider.transform.IsChildOf(obstructionIgnoreRoot)) continue;
                length = Mathf.Min(length, Mathf.Max(0.6f, hits[i].distance - 0.1f));
            }
            tpPosition = pivot + dir * length;
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
