using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Hasta durumları (kullanıcı kararı 7).
public enum PatientState { LyingGround, OnStretcher, Ragdoll, InAmbulance, Rescued }

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

    [Tooltip("Ragdoll kemik gövdeleri ve collider'ları (editor betiği kurar).")]
    public Rigidbody[] ragdollBodies = new Rigidbody[0];
    public Collider[] ragdollColliders = new Collider[0];

    public PatientState State { get; private set; } = PatientState.LyingGround;
    public int MissedAmbulances { get; private set; }

    public event Action<Patient> StateChanged;

    // Kalçanın dünya konumu (mesafe ölçümleri bunu kullanır).
    public Vector3 Position { get { return hips != null ? hips.position : transform.position; } }

    public bool CanBePickedUp { get { return State == PatientState.LyingGround || State == PatientState.Ragdoll; } }
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
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
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
    public void LieOn(Transform frame, PatientState newState, float blendTime)
    {
        if (blend != null) { StopCoroutine(blend); blend = null; }
        SetRagdoll(false);
        RestorePose();
        transform.SetParent(frame, true);
        LiePoseInFrame(out Vector3 targetPos, out Quaternion targetRot);
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
