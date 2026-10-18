using UnityEngine;

// E2 kararı 1: sedye taşıyan iki taşıyıcının iki eli de tuttuğu sapın iki borusuna kilitlenir (iki kemikli kol IK'sı).
// Animator ve ayak IK'sından, sedye konumlandıktan SONRA çalışır (LateUpdate 300); kemik boyları değişmez.
// clips_meta.json handle_grip: avuç = el kemiği + kemik yönünde (+Y) 5 cm; sap işareti kök yerelinde 0.665 m yükseklikte,
// ön taşıyıcıda 0.19 m arkada (Carry_Stretcher_Front_Walk). Arka taşıyıcıda meta 0.252 m önde der; ama Unity'deki
// Carry_Stretcher_Walk pozunda omuz geride kaldığından o noktaya kol ~0.33–0.36 m uzanmak zorunda kalıyordu (chibi kol
// zinciri 0.177 + 0.14 m). Sap 0.19 m öne alınınca hedef omuzdan ≤ 0.30 m kalır (ölçüm: Bolum1AnimVerify E2-1).
// Hedef yine de omuzdan maximumReach'ten uzaksa o el klip pozunda kalır (aşırı gerilmez) ve sayılır.
// IK yalnız taşıma döngüsündeyken (PickUp bittikten sonra) açılır; açılış ve kapanış blendSeconds içinde yumuşaktır.
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(Animator))]
public sealed class StretcherHandIK : MonoBehaviour
{
    public const float HandleHeight = 0.665f;
    public const float FrontHandleBehind = 0.19f;
    public const float RearHandleAhead = 0.19f;
    public const float PalmOffset = 0.05f;

    [Tooltip("IK açılma / kapanma süresi (sn).")]
    public float blendSeconds = 0.2f;
    [Tooltip("Omuz (üst kol) → el hedefi en çok bu kadar (m); daha uzaksa el klip pozunda kalır.")]
    public float maximumReach = 0.305f;

    public float Weight { get; private set; }
    // Son karede: ulaşılamayan el sayısı ve en uzak omuz → hedef mesafesi (doğrulama okur)
    public int UnreachableHands { get; private set; }
    public float MaximumShoulderDistance { get; private set; }

    private Animator animator;
    private ICarrier carrier;
    private readonly Vector3[] lastTargets = new Vector3[2];

    // Taşıyıcının sap noktası (sedye bu noktaya oturur): kök + yukarı 0.665 + ileri (arka uç) / geri (ön uç).
    public static Vector3 MarkerPoint(ICarrier c)
    {
        bool front = c.HeldStretcher != null && c.HeldStretcher.HoldsFront(c);
        return c.Root.position + Vector3.up * HandleHeight + c.Root.forward * MarkerAhead(front);
    }

    public static float MarkerAhead(bool front) { return front ? -FrontHandleBehind : RearHandleAhead; }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        carrier = GetComponent<ICarrier>();
    }

    public Vector3 Palm(bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        return hand.position + hand.up * PalmOffset;
    }

    private bool InCarryLoop()
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        return st.IsName(CrewAnimation.StateCarryStretcher) || st.IsName(CrewAnimation.StateCarryStretcherFront);
    }

    private void LateUpdate()
    {
        UnreachableHands = 0;
        MaximumShoulderDistance = 0f;
        if (carrier == null || !animator.enabled || !animator.isHuman) { Weight = 0f; return; }
        var s = carrier.HeldStretcher;
        bool carrying = s != null && s.IsRaised && InCarryLoop();
        Weight = Mathf.MoveTowards(Weight, carrying ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, blendSeconds));
        if (Weight <= 0f) return;
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            if (carrying) lastTargets[i] = s.GripPoint(carrier, left); // bırakınca son hedeften yumuşakça çıkar
            Solve(left, lastTargets[i], Weight);
        }
    }

    // İki kemikli analitik çözüm: dirsek mevcut bükülme düzleminde kalır, el kemiğinin dünya dönüşü korunur.
    private void Solve(bool left, Vector3 palmTarget, float weight)
    {
        var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        Quaternion handRotation = hand.rotation;
        Vector3 target = palmTarget - hand.up * PalmOffset;
        Vector3 delta = target - upper.position;
        float distance = delta.magnitude;
        MaximumShoulderDistance = Mathf.Max(MaximumShoulderDistance, distance);
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        if (distance > Mathf.Min(maximumReach, a + b - 0.001f) || distance < 0.001f)
        {
            UnreachableHands++; // klip pozu korunur
            return;
        }
        Vector3 direction = delta / distance;
        Vector3 bend = Vector3.ProjectOnPlane(lower.position - upper.position, direction);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-transform.forward, direction);
        bend.Normalize();
        float along = (a * a + distance * distance - b * b) / (2f * distance);
        Vector3 elbow = upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Quaternion upperStart = upper.rotation, lowerStart = lower.rotation;
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
        Quaternion upperSolved = upper.rotation, lowerSolved = lower.rotation;
        upper.rotation = Quaternion.Slerp(upperStart, upperSolved, weight);
        lower.rotation = Quaternion.Slerp(lowerStart, lowerSolved, weight);
        hand.rotation = handRotation;
    }
}
