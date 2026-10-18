using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Hasta durumları (kullanıcı kararı 7; D2: InVehicle = minibüs koltuğunda, OnBack = kurtarıcının sırtında).
public enum PatientState { LyingGround, OnStretcher, Ragdoll, InAmbulance, Rescued, InVehicle, OnBack }

// Hasta: yerde, sedyede ya da ambulansta sırt üstü yatar; sedyeden düşerse ragdoll olur.
// Yatırma: model T-pozda (ya da tek kareli yatma klibiyle) ayakta tasarlanmıştır; objeyi döndürerek yatırırız
// (kök dönüşü yok). Kalça noktası bir "yatma çerçevesine" (anchor) oturur: çerçevenin +ileri ekseni baş yönü,
// +yukarı ekseni yüzün baktığı yön; sırt, çerçevenin hipsToBack kadar üstündeki kalçadan aşağıda yüzeye değer.
public class Patient : MonoBehaviour
{
    public static readonly List<Patient> All = new List<Patient>();

    [Tooltip("Arayüzde görünen ad (Turist, Sivil, Yaşlı).")]
    public string displayName = "Hasta";

    [Tooltip("Kalça kemiği (Humanoid Hips).")]
    public Transform hips;

    [Tooltip("Yatarken kalça noktasının sırt yüzeyine uzaklığı (m). patient_meta.json 'hips_to_back_m' ya da mesh ölçümü.")]
    public float hipsToBack = 0.12f;

    [Tooltip("Yatma pozu klibini oynatan Animator (klip yoksa kapalı kalır, model T-pozda durur).")]
    public Animator animator;
    public bool hasLiePose;

    [Header("Yatma animasyonu (Lying_HelpWave / Lying_Groan)")]
    [Tooltip("Yerde beklerken el sallama ile inleme arasında geçiş yapar mı (Civilian: askılı kol, yalnız inleme).")]
    public bool canWave = true;
    [Tooltip("Yerde el sallama / inleme geçişi aralığı (sn), bu aralıkta rastgele.")]
    public Vector2 waveSwitchSeconds = new Vector2(5f, 10f);
    public const string WaveParameter = "Wave";
    // Şu an el sallıyor mu (Animator parametresi; test ve hata ayıklama)
    public bool Waving { get; private set; }
    public int WaveSwitches { get; private set; }
    private float nextWaveSwitch = -1f;

    [Tooltip("Ragdoll kemik gövdeleri ve collider'ları (editor betiği kurar).")]
    public Rigidbody[] ragdollBodies = new Rigidbody[0];
    public Collider[] ragdollColliders = new Collider[0];

    [Header("D2: başlangıç ve sırtta taşıma")]
    [Tooltip("Görev başında minibüs koltuğunda oturuyor mu (builder koltuğa yerleştirir; Patient_SitDazed).")]
    public bool startSeated;
    [Tooltip("Kaya çıkıntısındaki hasta: yalnız bu hasta, yalnız çıkıntıdayken sırta alınabilir (D2 kararı 5).")]
    public bool onLedge;

    // Animator durum adları (Bolum1AnimBuilder kurar)
    public const string StateSit = "Sit", StateDraggedOut = "DraggedOut", StateRider = "Rider";

    public PatientState State { get; private set; } = PatientState.LyingGround;
    public int MissedAmbulances { get; private set; }

    public event Action<Patient> StateChanged;

    // Kalçanın dünya konumu (mesafe ölçümleri bunu kullanır).
    public Vector3 Position { get { return hips != null ? hips.position : transform.position; } }

    public bool CanBePickedUp { get { return State == PatientState.LyingGround || State == PatientState.Ragdoll; } }
    // Sırta alınabilir mi: yalnız çıkıntıdaki hasta, yerde yatarken (D2 kararı 5).
    public bool CanPiggyback { get { return onLedge && State == PatientState.LyingGround; } }
    // Minibüsten çekilirken (DraggedOut oynuyor)
    public bool BeingExtracted { get; private set; }
    public bool IsAboard { get { return State == PatientState.InAmbulance || State == PatientState.Rescued; } }

    // Yatma dönüşü (çerçeve uzayında): karakterin +Y'si (baş) çerçevenin +Z'sine, +Z'si (yüz) çerçevenin +Y'sine.
    public static readonly Quaternion LieRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

    private Transform[] bones;
    private Vector3[] bonePositions;
    private Quaternion[] boneRotations;
    private Vector3 hipsInRoot; // bağ pozunda kalçanın kök uzayındaki yeri
    private Coroutine blend;

    private void Awake()
    {
        // Bağ pozunu sakla: ragdoll'dan sonra hasta tekrar yatırılırken iskelet bu poza döner.
        bones = GetComponentsInChildren<Transform>(true);
        bonePositions = new Vector3[bones.Length];
        boneRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            bonePositions[i] = bones[i].localPosition;
            boneRotations[i] = bones[i].localRotation;
        }
        hipsInRoot = hips != null ? transform.InverseTransformPoint(hips.position) : new Vector3(0f, 0.75f, 0f);
        SetRagdoll(false);
        if (GetComponent<NpcBody>() == null) gameObject.AddComponent<NpcBody>();
        if (startSeated) State = PatientState.InVehicle;
    }

    private void Start()
    {
        if (State == PatientState.InVehicle) PlayPose(StateSit);
    }

    // Yatmayan pozlar (oturma, çekilme, sırtta) taban katmanda; Civilian'ın kolsuz inleme katmanı o sırada kapalı.
    private void PlayPose(string state)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
        animator.Play(state, 0, 0f);
    }

    // Yatma animasyonuna dönüş (Groan ya da Civilian: Lie + inleme katmanı)
    private void PlayLieAnimation()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        if (animator.layerCount > 1) { animator.SetLayerWeight(1, 1f); animator.Play("Lie", 0, 0f); }
        else { Waving = false; animator.SetBool(WaveParameter, false); animator.Play("Groan", 0, 0f); }
    }

    // ---- D2: minibüs koltuğu ----
    // Kök koltuk çerçevesinde verilen yerel poza oturur (builder hesaplar; Patient_SitDazed koltuk yüzeyi kök +0.262 m).
    public void SitIn(Transform parent, Vector3 localPosition, Quaternion localRotation)
    {
        if (blend != null) { StopCoroutine(blend); blend = null; }
        BeingExtracted = false;
        SetRagdoll(false);
        RestorePose();
        transform.SetParent(parent, false);
        transform.SetLocalPositionAndRotation(localPosition, localRotation);
        SetState(PatientState.InVehicle);
        PlayPose(StateSit);
    }

    // Arka kapıdan çekilme (D2 kararı 2): Patient_DraggedOut oynar (klipte kalça hipsShiftInRoot kadar geri gidip yatar);
    // kök aynı sürede koltuktan, klibin son pozundaki kalça yerdeki yatış noktasına (groundPoint + hipsToBack) gelecek
    // yere kayar (minibüs zemini ile patika arasındaki farkı kök karşılar). Sonda sırt üstü yerde (LyingGround).
    public void DragOut(Vector3 groundPoint, Vector3 headDirection, Vector3 groundNormal, float duration, Vector3 hipsShiftInRoot)
    {
        if (State != PatientState.InVehicle || BeingExtracted) return;
        BeingExtracted = true;
        if (blend != null) StopCoroutine(blend);
        blend = StartCoroutine(DragOutRoutine(groundPoint, headDirection, groundNormal, duration, hipsShiftInRoot));
    }

    private IEnumerator DragOutRoutine(Vector3 groundPoint, Vector3 headDirection, Vector3 groundNormal, float duration, Vector3 hipsShiftInRoot)
    {
        Vector3 hipsStart = transform.InverseTransformPoint(Position); // oturuşta kalça (kök uzayı)
        transform.SetParent(null, true);
        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        // Klip sonunda kök dik, yüz başın tersine bakar (hasta geriye, baş yönüne düşer)
        Vector3 face = Vector3.ProjectOnPlane(-headDirection, Vector3.up).normalized;
        Quaternion toRot = Quaternion.LookRotation(face, Vector3.up);
        Vector3 hipsEnd = hipsStart + hipsShiftInRoot;
        Vector3 toPos = groundPoint + groundNormal * hipsToBack - toRot * hipsEnd;
        PlayPose(StateDraggedOut);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, u), Quaternion.Slerp(fromRot, toRot, u));
            yield return null;
        }
        blend = null;
        BeingExtracted = false;
        LieOnGround(groundPoint, headDirection, groundNormal);
    }

    // ---- D2: sırtta taşıma ----
    // Kök, taşıyanın Spine1 altındaki PiggybackSocket'ine sıfır yerel dönüşümle bağlanır, Piggyback_Rider oynar.
    public void AttachToBack(Transform socket)
    {
        if (blend != null) { StopCoroutine(blend); blend = null; }
        SetRagdoll(false);
        RestorePose();
        transform.SetParent(socket, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        SetState(PatientState.OnBack);
        PlayPose(StateRider);
    }

    // Sırttan yere bırakılır (D2 kararı 5): artık patikada sayılır, bir daha sırta alınmaz.
    public void PutDownFromBack(Vector3 groundPoint, Vector3 headDirection, Vector3 groundNormal)
    {
        onLedge = false;
        LieOnGround(groundPoint, headDirection, groundNormal);
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    // Yerde: rastgele 5–10 sn'de bir el sallama ↔ inleme. Sedyede, ambulansta ve ragdoll'da el sallamaz (inleme).
    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        bool ground = canWave && State == PatientState.LyingGround;
        if (!ground)
        {
            SetWave(false);
            nextWaveSwitch = -1f;
            return;
        }
        if (nextWaveSwitch < 0f) nextWaveSwitch = Time.time + UnityEngine.Random.Range(waveSwitchSeconds.x, waveSwitchSeconds.y);
        if (Time.time >= nextWaveSwitch)
        {
            SetWave(!Waving);
            WaveSwitches++;
            nextWaveSwitch = Time.time + UnityEngine.Random.Range(waveSwitchSeconds.x, waveSwitchSeconds.y);
        }
    }

    private void SetWave(bool on)
    {
        Waving = on;
        if (canWave) animator.SetBool(WaveParameter, on);
    }
    private void OnDisable() { All.Remove(this); }

    // Verilen yatma çerçevesinde kökün yerel pozu (çerçeve uzayında).
    public void LiePoseInFrame(out Vector3 localPosition, out Quaternion localRotation)
    {
        localRotation = LieRotation;
        localPosition = Vector3.up * hipsToBack - LieRotation * hipsInRoot;
    }

    // Editör yardımcı: bağ pozu ölçüsüyle (Awake çalışmadan) çerçevedeki poz.
    public static void LiePoseInFrame(Vector3 hipsInRootSpace, float backOffset, out Vector3 localPosition, out Quaternion localRotation)
    {
        localRotation = LieRotation;
        localPosition = Vector3.up * backOffset - LieRotation * hipsInRootSpace;
    }

    // Hastayı bir yatma çerçevesine (sedye PatientAnchor'ı ya da ambulans PatientSlot'u) yatırır.
    // blendTime > 0 ise kısa bir yumuşak geçişle yerine kayar.
    // frameOffset: çerçeve yerelinde ek kaydırma (E3 kararı 1: sedyede ayak tarafına; +ileri = baş yönü).
    public void LieOn(Transform frame, PatientState newState, float blendTime, Vector3 frameOffset = default)
    {
        if (blend != null) { StopCoroutine(blend); blend = null; }
        SetRagdoll(false);
        RestorePose();
        transform.SetParent(frame, true);
        if (State == PatientState.InVehicle || State == PatientState.OnBack) PlayLieAnimation();
        LiePoseInFrame(out Vector3 targetPos, out Quaternion targetRot);
        targetPos += frameOffset;
        if (blendTime > 0f && isActiveAndEnabled)
            blend = StartCoroutine(BlendTo(targetPos, targetRot, blendTime));
        else
            transform.SetLocalPositionAndRotation(targetPos, targetRot);
        SetState(newState);
    }

    // Hastayı yere yatırır (görev başı). point: kalçanın altındaki zemin noktası, headDirection: baş yönü.
    public void LieOnGround(Vector3 point, Vector3 headDirection, Vector3 groundNormal)
    {
        if (blend != null) { StopCoroutine(blend); blend = null; }
        SetRagdoll(false);
        RestorePose();
        transform.SetParent(null, true);
        if (State == PatientState.InVehicle || State == PatientState.OnBack) PlayLieAnimation();
        Quaternion frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(headDirection, groundNormal).normalized, groundNormal);
        LiePoseInFrame(out Vector3 lp, out Quaternion lr);
        transform.SetPositionAndRotation(point + frame * lp, frame * lr);
        SetState(PatientState.LyingGround);
    }

    private IEnumerator BlendTo(Vector3 targetPos, Quaternion targetRot, float duration)
    {
        Vector3 fromPos = transform.localPosition;
        Quaternion fromRot = transform.localRotation;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.SetLocalPositionAndRotation(Vector3.Lerp(fromPos, targetPos, u), Quaternion.Slerp(fromRot, targetRot, u));
            yield return null;
        }
        transform.SetLocalPositionAndRotation(targetPos, targetRot);
        blend = null;
    }

    // Sedyeden düşme (kullanıcı kararı 11): hasta ragdoll olur, verilen hızla kayarak düşer.
    public void Fall(Vector3 velocity)
    {
        if (blend != null) { StopCoroutine(blend); blend = null; }
        transform.SetParent(null, true);
        if (State == PatientState.OnBack) onLedge = false; // sırttan düşen hasta yerden sedyeyle alınır
        SetRagdoll(true);
        foreach (var rb in ragdollBodies)
            if (rb != null) rb.linearVelocity = velocity;
        SetState(PatientState.Ragdoll);
    }

    // Trafik aracı çarptı (kullanıcı kararı 7): sedyedeki ya da yerdeki hasta ragdoll olur, araç itkisiyle savrulur.
    // Ceza yok; hasta mevcut kuralla (ragdoll'dan) tekrar sedyeye alınabilir.
    public void HitByVehicle(Vector3 velocity)
    {
        if (State != PatientState.LyingGround && State != PatientState.OnStretcher) return;
        if (State == PatientState.OnStretcher)
            foreach (var s in StretcherController.All)
                if (s != null && s.Passenger == this) s.TakePatient();
        Fall(velocity);
    }

    // Ambulans duraktan ayrılırken hasta ambulansta değilse çağrılır (kullanıcı kararı 5).
    public void AddMissedAmbulance()
    {
        MissedAmbulances++;
        StateChanged?.Invoke(this);
    }

    // Ambulans şeridin sonunda kaybolunca: hasta kurtarıldı, sahneden kalkar.
    public void MarkRescued()
    {
        transform.SetParent(null, true);
        SetState(PatientState.Rescued);
        gameObject.SetActive(false);
    }

    private void SetState(PatientState s)
    {
        State = s;
        var body = GetComponent<NpcBody>();
        if (body != null)
        {
            body.Refresh();
            if (s == PatientState.LyingGround) body.ResetPushOrigin();
        }
        StateChanged?.Invoke(this);
    }

    private void SetRagdoll(bool on)
    {
        foreach (var rb in ragdollBodies)
        {
            if (rb == null) continue;
            rb.isKinematic = !on;
            rb.useGravity = on;
            // Kinematikken interpolasyon kapalı: yoksa kemikler ebeveynle (sedye) birlikte hareket etmez, eski fizik pozunda kalır.
            rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
        foreach (var c in ragdollColliders)
            if (c != null) c.enabled = on;
        if (animator != null)
            animator.enabled = !on && hasLiePose && animator.runtimeAnimatorController != null;
    }

    // İskeleti saklanan bağ pozuna döndürür (kök hariç).
    private void RestorePose()
    {
        if (bones == null) return;
        for (int i = 1; i < bones.Length; i++)
            bones[i].SetLocalPositionAndRotation(bonePositions[i], boneRotations[i]);
    }
}
