using UnityEngine;
namespace RescueTeam.Bolum1
{
    [DefaultExecutionOrder(50)]
    public sealed class StretcherHelperBot : CarrierActor
    {
        public bool assistanceEnabled = true;
        public Animator animator;
        IStretcherCarrier leader;
        Vector3 axis;
        bool leaderFront;
        Vector3 previous;
        public override Vector3 HandPosition => transform.position + Vector3.up * (rules ? rules.handHeight : 0.85f);
        public void Assist(IStretcherCarrier carrier, StretcherController stretcher, bool front)
        {
            if (!assistanceEnabled || !isActiveAndEnabled) return;
            leader = carrier; leaderFront = front;
            axis = stretcher.transform.forward * (front ? -1 : 1);
            Vector3 p = carrier.HandPosition + axis * stretcher.HandleSpan;
            transform.position = p - Vector3.up * rules.handHeight;
            stretcher.Grab(this, !front); Follow(); previous = transform.position;
        }
        void Follow()
        {
            if (leader == null || !HeldStretcher) return;
            Vector3 delta = Vector3.ProjectOnPlane(HandPosition - leader.HandPosition, Vector3.up);
            if (delta.sqrMagnitude > 0.01f) axis = delta.normalized;
            Vector3 target = leader.HandPosition + axis * HeldStretcher.HandleSpan - Vector3.up * rules.handHeight;
            if (Ground(target, out var hit)) target.y = hit.point.y;
            transform.position = target;
            transform.rotation = Quaternion.LookRotation(leaderFront ? -axis : axis, Vector3.up);
        }
        void LateUpdate()
        {
            if (HeldStretcher && (!assistanceEnabled || leader == null || leader.HeldStretcher != HeldStretcher)) HeldStretcher.Release(this);
            Follow();
            if (animator) animator.SetFloat("Speed", Vector3.Distance(previous, transform.position) / Mathf.Max(Time.deltaTime, 0.001f), 0.1f, Time.deltaTime);
            previous = transform.position;
        }
    }
}
