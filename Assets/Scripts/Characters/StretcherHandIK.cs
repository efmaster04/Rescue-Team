using UnityEngine;

// Sedye ve ayak IK'sından sonra iki kemikli kol çözümü; kemik boyları değişmez.
// clips_meta.handle_grip: kök Blender (-x,z,-y), avuç kemiğin +Y yönünde 5 cm.
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(Animator))]
public sealed class StretcherHandIK : MonoBehaviour
{
    public float releaseSeconds = 0.2f;
    public float maximumReach = 0.305f;
    public float Weight { get; private set; }
    public float MaximumError { get; private set; }
    public int UnreachableHands { get; private set; }
    public float MaximumShoulderDistance { get; private set; }
    private Animator animator;
    private ICarrier carrier;
    private readonly Vector3[] lastTargets = new Vector3[2];
    public static Vector3 MarkerPoint(ICarrier c)
    {
        bool front = c.HeldStretcher != null && c.HeldStretcher.HoldsFront(c);
        return c.Root.position + Vector3.up * 0.665f + c.Root.forward * (front ? -0.19f : 0.252f);
    }
    private void Awake() { animator = GetComponent<Animator>(); carrier = GetComponent<ICarrier>(); }
    public Vector3 Palm(bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        return hand.position + hand.up * 0.05f;
    }
    private void LateUpdate()
    {
        if (carrier == null || !animator.enabled || !animator.isHuman) { Weight = 0f; return; }
        var s = carrier.HeldStretcher;
        bool carrying = s != null && s.IsRaised;
        Weight = carrying ? 1f : Mathf.MoveTowards(Weight, 0f, Time.deltaTime / Mathf.Max(0.01f, releaseSeconds));
        MaximumError = MaximumShoulderDistance = 0f;
        UnreachableHands = 0;
        if (Weight <= 0f) return;
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            if (carrying) lastTargets[i] = s.GripPoint(carrier, left);
            Solve(left, lastTargets[i], Weight);
            MaximumError = Mathf.Max(MaximumError, Vector3.Distance(Palm(left), lastTargets[i]));
        }
    }
    private void Solve(bool left, Vector3 palmTarget, float weight)
    {
        var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        Quaternion handRotation = hand.rotation;
        Vector3 target = palmTarget - hand.up * 0.05f;
        Vector3 delta = target - upper.position;
        float distance = delta.magnitude;
        MaximumShoulderDistance = Mathf.Max(MaximumShoulderDistance, distance);
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        if (distance > Mathf.Min(maximumReach, a + b - 0.001f) || distance < 0.001f)
        {
            // Ulaşılamayan hedefte klip korunur, doğrulama mesafeyi raporlar.
            UnreachableHands++;
            return;
        }
        Vector3 direction = delta / distance;
        Vector3 bend = Vector3.ProjectOnPlane(lower.position - upper.position, direction).normalized;
        if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(-transform.forward, direction).normalized;
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
