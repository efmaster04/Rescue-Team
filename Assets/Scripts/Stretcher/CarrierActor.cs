using System.Collections.Generic;
using UnityEngine;
namespace RescueTeam.Bolum1
{
    public interface IStretcherCarrier
    {
        Transform CarrierTransform { get; }
        Vector3 HandPosition { get; }
        StretcherController HeldStretcher { get; set; }
    }
    public abstract class CarrierActor : MonoBehaviour, IStretcherCarrier
    {
        public static readonly List<CarrierActor> Active = new List<CarrierActor>();
        public CarryRules rules;
        public Transform CarrierTransform => transform;
        public virtual Vector3 HandPosition => transform.position + Vector3.up * (rules ? rules.handHeight : 0.85f) + transform.forward * 0.3f;
        public StretcherController HeldStretcher { get; set; }
        protected virtual void OnEnable() { Active.Add(this); }
        protected virtual void OnDisable() { HeldStretcher?.Release(this); Active.Remove(this); }
        public static bool Ground(Vector3 at, out RaycastHit result)
        {
            result = default; float distance = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * 2, Vector3.down, 30, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<CarrierActor>() || hit.collider.GetComponentInParent<Patient>() || hit.collider.GetComponentInParent<StretcherController>() || hit.collider.GetComponentInParent<AmbulanceController>()) continue;
                if (hit.normal.y > 0.5f && hit.distance < distance) { result = hit; distance = hit.distance; }
            }
            return distance < float.PositiveInfinity;
        }
    }
}
