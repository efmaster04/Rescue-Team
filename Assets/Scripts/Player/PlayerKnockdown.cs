using System;
using UnityEngine;

// Araç çarpınca düşme ve kaldırılma (kullanıcı kararları 5, 6). Oyun durumu burada; girdi PlayerInteraction'da.
//  - KnockDown: oyuncu ragdoll olur, çarpma yönünde itkiyle savrulur; kontrol yok.
//  - Yalnız bir TAKIM ARKADAŞI kaldırır: yanında (≤ reviveRange) E'yi reviveSeconds basılı tutar (TryRevive her kare).
//    Basmayı bırakırsa ilerleme sıfırlanır. Kimse kaldırmazsa yerde kalır (kendi kendine kalkma YOK).
//  - Kaldırılınca oyuncu kalçanın altındaki zeminde GetUp_FromBack ile (ragdoll pozundan yumuşak geçişle) kalkar;
//    animasyon bitince kontrolü geri alır. Animasyon sürücüsü (CrewAnimation) yoksa iskelet bağ pozuna döner.
// Ragdoll bileşenleri (kemik Rigidbody + collider + CharacterJoint) editor betiğiyle kurulur (Patient ile aynı desen).
public class PlayerKnockdown : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public Animator animator;
    [Tooltip("Kalça kemiği (Humanoid Hips).")]
    public Transform hips;
    public Rigidbody[] ragdollBodies = new Rigidbody[0];
    public Collider[] ragdollColliders = new Collider[0];
    [Tooltip("Düşerken kapatılan bileşenler (ayak IK'sı vb.).")]
    public Behaviour[] disableWhileDown = new Behaviour[0];
    [Tooltip("Kalkış animasyonu (GetUp_FromBack). Boşsa anında ayağa kalkar.")]
    public CrewAnimation crewAnimation;

    [Tooltip("Arkadaşın E'yi basılı tutması gereken süre (sn).")]
    [Min(0.1f)] public float reviveSeconds = 2f;
    [Tooltip("Kaldıran arkadaşın kalçaya en fazla yatay uzaklığı (m).")]
    public float reviveRange = 1.5f;

    public bool IsDown { get; private set; }
    public float DownSince { get; private set; } = -1f;
    public float ReviveProgress { get; private set; }  // 0..1
    public ICarrier Reviver { get; private set; }
    public int TimesDowned { get; private set; }
    public int TimesRevived { get; private set; }

    public event Action<PlayerKnockdown> Downed;
    public event Action<PlayerKnockdown> Revived;

    // Düşmüş oyuncunun yeri: kalça (ragdoll kökten ayrılır)
    public Vector3 GroundPosition { get { return IsDown && hips != null ? hips.position : transform.position; } }
    // Göğüs (kaldıran taraf Revive_Helper'da omuzlardan sarsar; bot buraya yakın diz çöker)
    public Vector3 ChestPosition
    {
        get
        {
            if (animator == null) return GroundPosition;
            var c = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (c == null) c = animator.GetBoneTransform(HumanBodyBones.Chest);
            return c != null ? c.position : GroundPosition;
        }
    }

    private Transform[] bones;
    private Vector3[] bonePositions;
    private Quaternion[] boneRotations;
    private float lastReviveTime = -100f;

    private void Awake()
    {
        if (player == null) player = GetComponent<ThirdPersonPlayer>();
        // Bağ pozu: kalkarken iskelet bu poza döner.
        bones = GetComponentsInChildren<Transform>(true);
        bonePositions = new Vector3[bones.Length];
        boneRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            bonePositions[i] = bones[i].localPosition;
            boneRotations[i] = bones[i].localRotation;
        }
        SetRagdoll(false);
    }

    public void KnockDown(Vector3 velocity)
    {
        if (IsDown) return;
        IsDown = true;
        DownSince = Time.time;
        ReviveProgress = 0f;
        Reviver = null;
        TimesDowned++;
        foreach (var b in disableWhileDown) if (b != null) b.enabled = false;
        SetRagdoll(true);
        foreach (var rb in ragdollBodies)
            if (rb != null) rb.linearVelocity = velocity;
        Downed?.Invoke(this);
    }

    // Arkadaş E'yi basılı tutuyor: her kare çağrılır. Kalkınca true döner.
    public bool TryRevive(ICarrier helper, float dt)
    {
        if (!IsDown || helper == null || ReferenceEquals(helper, player)) return false;
        if (!InReviveRange(helper)) return false;
        if (Reviver != helper) { Reviver = helper; ReviveProgress = 0f; }
        lastReviveTime = Time.time;
        ReviveProgress = Mathf.Min(1f, ReviveProgress + dt / reviveSeconds);
        if (ReviveProgress >= 1f)
        {
            StandUp();
            return true;
        }
        return false;
    }

    public bool InReviveRange(ICarrier helper)
    {
        if (helper == null || helper.Root == null) return false;
        Vector3 d = helper.Root.position - GroundPosition;
        float dy = Mathf.Abs(d.y);
        d.y = 0f;
        return d.magnitude <= reviveRange && dy <= 2f;
    }

    private void Update()
    {
        // Basma bırakıldı (bu kare kimse TryRevive çağırmadı): ilerleme sıfırlanır.
        if (IsDown && Reviver != null && Time.time - lastReviveTime > 0.15f)
        {
            Reviver = null;
            ReviveProgress = 0f;
        }
    }

    private void StandUp()
    {
        Vector3 at = hips != null ? hips.position : transform.position;
        Vector3 ground = new Vector3(at.x, transform.position.y, at.z);
        if (GroundProbe.Find(at + Vector3.up * 1.0f, 4f, out RaycastHit hit)) ground = hit.point + Vector3.up * 0.02f;
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        var pose = crewAnimation != null && animator != null ? CrewAnimation.Capture(animator) : null; // ragdoll pozu
        SetRagdoll(false);
        if (pose == null)
            for (int i = 1; i < bones.Length; i++)
                bones[i].SetLocalPositionAndRotation(bonePositions[i], boneRotations[i]);
        IsDown = false;
        ReviveProgress = 0f;
        Reviver = null;
        TimesRevived++;
        foreach (var b in disableWhileDown) if (b != null) b.enabled = true;
        if (player != null) player.Teleport(ground, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        else transform.position = ground;
        if (pose != null)
        {
            crewAnimation.BeginGetUp(pose, (p, r) => { if (player != null) player.Teleport(p, r); else transform.SetPositionAndRotation(p, r); });
            if (player != null) player.SuspendControl(crewAnimation.getUpSeconds); // kalkış bitince kontrol geri gelir
        }
        Revived?.Invoke(this);
    }

    // Yeniden doğma (FallRespawnPlane altına düşme): yerdeyse ragdoll kapanır, iskelet bağ pozuna döner, ayakta kalır.
    public void ResetStanding()
    {
        if (!IsDown) return;
        SetRagdoll(false);
        for (int i = 1; i < bones.Length; i++)
            bones[i].SetLocalPositionAndRotation(bonePositions[i], boneRotations[i]);
        IsDown = false;
        ReviveProgress = 0f;
        Reviver = null;
        foreach (var b in disableWhileDown) if (b != null) b.enabled = true;
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
        foreach (var c in ragdollColliders)
            if (c != null) c.enabled = on;
        if (animator != null)
            animator.enabled = !on;
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = !on;
    }
}
