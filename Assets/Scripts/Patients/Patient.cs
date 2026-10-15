using System.Collections;
using UnityEngine;
namespace RescueTeam.Bolum1
{
    public enum PatientState { LyingGround, OnStretcher, Ragdoll, InAmbulance, Rescued }
    public sealed class Patient : MonoBehaviour
    {
        public string displayName;
        public Transform hips;
        public float hipsToBack = 0.12f;
        public Rigidbody[] ragdollBodies;
        public Collider[] ragdollColliders;
        public Animator animator;
        public PatientState State { get; private set; } = PatientState.LyingGround;
        public int MissedCount { get; private set; }
        public Vector3 Position => hips ? hips.position : transform.position;
        public bool CanPickUp => State == PatientState.LyingGround || State == PatientState.Ragdoll;
        Vector3[] bindPositions;
        Quaternion[] bindRotations;
        Transform[] bones;
        void Awake()
        {
            bones = GetComponentsInChildren<Transform>();
            bindPositions = new Vector3[bones.Length]; bindRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) { bindPositions[i] = bones[i].localPosition; bindRotations[i] = bones[i].localRotation; }
            SetRagdoll(false);
        }
        void SetRagdoll(bool value)
        {
            if (animator) animator.enabled = !value && animator.runtimeAnimatorController;
            foreach (var body in ragdollBodies) { body.isKinematic = !value; body.useGravity = value; }
            foreach (var c in ragdollColliders) c.enabled = value;
        }
        public void Attach(Transform anchor, PatientState state, bool smooth = false)
        {
            StopAllCoroutines(); SetRagdoll(false);
            for (int i = 1; i < bones.Length; i++) { bones[i].localPosition = bindPositions[i]; bones[i].localRotation = bindRotations[i]; }
            State = state;
            transform.SetParent(anchor, true);
            if (smooth) StartCoroutine(Settle());
            else { transform.localPosition = Vector3.up * hipsToBack; transform.localRotation = Quaternion.identity; }
        }
        IEnumerator Settle()
        {
            Vector3 from = transform.localPosition; Quaternion rotation = transform.localRotation;
            for (float t = 0; t < 0.45f; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0, 1, t / 0.45f);
                transform.localPosition = Vector3.Lerp(from, Vector3.up * hipsToBack, u);
                transform.localRotation = Quaternion.Slerp(rotation, Quaternion.identity, u);
                yield return null;
            }
            transform.localPosition = Vector3.up * hipsToBack; transform.localRotation = Quaternion.identity;
        }
        public void Fall(Vector3 velocity)
        {
            StopAllCoroutines(); transform.SetParent(null, true); State = PatientState.Ragdoll; SetRagdoll(true);
            foreach (var body in ragdollBodies) body.linearVelocity = velocity;
        }
        public int MissAmbulance()
        {
            if (State != PatientState.InAmbulance && State != PatientState.Rescued) MissedCount++;
            return MissedCount;
        }
        public void Rescue() { State = PatientState.Rescued; transform.SetParent(null); gameObject.SetActive(false); }
    }
}
