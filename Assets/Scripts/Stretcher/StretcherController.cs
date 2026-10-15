using System.Collections.Generic;
using UnityEngine;
namespace RescueTeam.Bolum1
{
    [DefaultExecutionOrder(100)]
    public sealed class StretcherController : MonoBehaviour
    {
        public CarryRules rules;
        public Transform frontHandle, rearHandle, patientAnchor;
        public Patient Passenger { get; private set; }
        public bool IsRaised { get; private set; }
        public int CarrierCount => carriers.Count;
        public float HandleSpan => Vector3.Distance(frontHandle.localPosition, rearHandle.localPosition);
        public Vector3 Velocity { get; private set; }
        readonly Dictionary<IStretcherCarrier, bool> carriers = new Dictionary<IStretcherCarrier, bool>();
        float releaseTime = -1;
        Vector3 previous;
        bool releasedFront;
        public bool Grab(IStretcherCarrier carrier, bool front)
        {
            if (carrier.HeldStretcher != null || carriers.ContainsKey(carrier)) return false;
            foreach (var pair in carriers) if (pair.Value == front && rules.requiredCarriers <= 2) return false;
            carriers.Add(carrier, front); carrier.HeldStretcher = this;
            if (carriers.Count >= rules.requiredCarriers) { IsRaised = true; releaseTime = -1; }
            else MissionHud.Notify(rules.RequiredMessage);
            return true;
        }
        public void Release(IStretcherCarrier carrier)
        {
            if (!carriers.TryGetValue(carrier, out bool front)) return;
            carriers.Remove(carrier); carrier.HeldStretcher = null;
            if (carriers.Count == 0) { LowerSafely(); return; }
            if (IsRaised && carriers.Count < rules.requiredCarriers && releaseTime < 0)
            { releaseTime = Time.time; releasedFront = front; }
        }
        public void ReleaseAll()
        {
            foreach (var carrier in carriers.Keys) carrier.HeldStretcher = null;
            carriers.Clear(); LowerSafely();
        }
        public bool TryPlacePatient(Patient patient, Vector3 playerPosition)
        {
            if (IsRaised || CarrierCount > 0 || Passenger || !patient.CanPickUp) return false;
            if (Vector3.Distance(patient.Position, patientAnchor.position) > 1.5f || Vector3.Distance(patient.Position, playerPosition) > 2) return false;
            Passenger = patient; patient.Attach(patientAnchor, PatientState.OnStretcher); return true;
        }
        public Patient TakePatient() { var result = Passenger; Passenger = null; return result; }
        void LowerSafely()
        {
            IsRaised = false; releaseTime = -1;
            Vector3 p = transform.position;
            if (CarrierActor.Ground(p, out var hit)) p.y = hit.point.y + 0.02f;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0, transform.eulerAngles.y, 0));
        }
        void LateUpdate()
        {
            if (releaseTime >= 0 && Time.time - releaseTime >= rules.safeReleaseWindow)
            {
                if (Passenger) { var p = TakePatient(); p.Fall(Velocity + transform.right * 1.8f + Vector3.down * 0.4f); }
                IsRaised = false; releaseTime = -1;
            }
            if (carriers.Count > 0 && (IsRaised || releaseTime >= 0))
            {
                Vector3 front = frontHandle.position, rear = rearHandle.position;
                foreach (var pair in carriers) { if (pair.Value) front = pair.Key.HandPosition; else rear = pair.Key.HandPosition; }
                if (releaseTime >= 0)
                {
                    Vector3 end = releasedFront ? front : rear;
                    if (CarrierActor.Ground(end, out var h)) end.y = h.point.y + 0.12f;
                    if (releasedFront) front = end; else rear = end;
                }
                Vector3 axis = front - rear;
                if (axis.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(axis.normalized, Vector3.up);
                    transform.position = (front + rear) * 0.5f - transform.TransformVector((frontHandle.localPosition + rearHandle.localPosition) * 0.5f);
                }
            }
            Velocity = (transform.position - previous) / Mathf.Max(Time.deltaTime, 0.001f); previous = transform.position;
        }
    }
}
