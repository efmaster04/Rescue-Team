using System;
using System.Collections.Generic;
using UnityEngine;
namespace RescueTeam.Bolum1
{
    public enum AmbulancePhase { Approaching, Waiting, Departing, Finished }
    public sealed class AmbulanceController : MonoBehaviour
    {
        public LaneFollower follower;
        public SynthesizedAmbulanceAudio sound;
        public Transform doorLeft, doorRight, step, loadPoint;
        public Transform[] patientSlots;
        public Vector3 hingeAxis = Vector3.up, stepAxis = Vector3.right;
        public float leftOpenSign = -1, rightOpenSign = 1, stepOpenSign = 1;
        public Renderer[] flashLeft, flashRight;
        public AmbulancePhase Phase { get; private set; }
        public float WaitRemaining => Mathf.Max(0, wait - stoppedTime);
        public bool LoadingOpen => Phase == AmbulancePhase.Waiting && stoppedTime >= 0.8f && stoppedTime < wait - 1;
        public int Occupancy => passengers.Count;
        public float DoorAngle { get; private set; }
        public float StepAngle { get; private set; }
        public event Action<AmbulanceController> Arrived, Departed, Despawned;
        readonly List<Patient> passengers = new List<Patient>();
        Quaternion closedLeft, closedRight, closedStep;
        float wait, stoppedTime;
        bool warned;
        MaterialPropertyBlock flash;
        void Awake()
        {
            closedLeft = doorLeft.localRotation; closedRight = doorRight.localRotation; closedStep = step.localRotation;
            flash = new MaterialPropertyBlock();
        }
        public void Initialize(LanePath lane, Transform stop, AmbulanceSchedule schedule)
        {
            follower.Initialize(lane, schedule.cruiseSpeed); follower.StopAt(lane.Project(stop.position));
            wait = schedule.wait; Phase = AmbulancePhase.Approaching;
        }
        void FixedUpdate()
        {
            if (Phase == AmbulancePhase.Finished) return;
            if (Phase == AmbulancePhase.Waiting)
            {
                stoppedTime += Time.fixedDeltaTime;
                if (!warned && stoppedTime >= wait - 5) { warned = true; sound.Horn(); }
                float open = stoppedTime < wait - 1 ? Mathf.Clamp01(stoppedTime / 0.8f) : Mathf.Clamp01((wait - 0.2f - stoppedTime) / 0.8f);
                if (DoorAngle > 99 && open < 0.999f) sound.Door();
                SetDoors(open);
                if (stoppedTime >= wait)
                { SetDoors(0); Phase = AmbulancePhase.Departing; follower.Continue(); Departed?.Invoke(this); }
            }
            else
            {
                follower.Tick(Time.fixedDeltaTime, HasCarrierAhead() ? 0.4f : 1);
                if (Phase == AmbulancePhase.Approaching && follower.AtStop)
                { Phase = AmbulancePhase.Waiting; stoppedTime = 0; sound.Door(); Arrived?.Invoke(this); }
                else if (Phase == AmbulancePhase.Departing && follower.AtEnd)
                {
                    Phase = AmbulancePhase.Finished;
                    foreach (var p in passengers) p.Rescue();
                    Despawned?.Invoke(this); Destroy(gameObject);
                }
            }
            sound.Drive(follower.Speed, Phase != AmbulancePhase.Waiting && Phase != AmbulancePhase.Finished);
        }
        bool HasCarrierAhead()
        {
            bool found = false;
            foreach (var carrier in CarrierActor.Active)
            {
                Vector3 p = carrier.transform.position;
                float s = follower.lane.Project(p);
                Vector3 onLane = follower.lane.Sample(s, out _);
                float ahead = s - follower.Distance - 3;
                if (ahead < -1 || ahead > 12 || Mathf.Abs(p.y - onLane.y) > 2 || Vector3.ProjectOnPlane(p - onLane, Vector3.up).magnitude > 1.7f) continue;
                found = true;
                if (ahead < 0.7f && carrier is ThirdPersonPlayer player)
                {
                    float sign = Vector3.Dot(p - transform.position, transform.right) >= 0 ? 1 : -1;
                    player.KnockAside(transform.right * sign);
                }
            }
            if (found) sound.Horn();
            return found;
        }
        void OnCollisionEnter(Collision collision)
        {
            if (Phase == AmbulancePhase.Waiting) return;
            var player = collision.gameObject.GetComponentInParent<ThirdPersonPlayer>();
            if (player) player.KnockAside(transform.right * (Vector3.Dot(player.transform.position - transform.position, transform.right) >= 0 ? 1 : -1));
        }
        void SetDoors(float amount)
        {
            DoorAngle = 100 * amount; StepAngle = 90 * amount;
            doorLeft.localRotation = closedLeft * Quaternion.AngleAxis(DoorAngle * leftOpenSign, hingeAxis);
            doorRight.localRotation = closedRight * Quaternion.AngleAxis(DoorAngle * rightOpenSign, hingeAxis);
            step.localRotation = closedStep * Quaternion.AngleAxis(StepAngle * stepOpenSign, stepAxis);
        }
        public bool TryLoad(StretcherController stretcher)
        {
            if (!LoadingOpen || !stretcher.IsRaised || !stretcher.Passenger || Vector3.Distance(stretcher.patientAnchor.position, loadPoint.position) > 2.5f) return false;
            if (passengers.Count >= patientSlots.Length) { MissionHud.Notify("Ambulans dolu"); return false; }
            var patient = stretcher.TakePatient();
            patient.Attach(patientSlots[passengers.Count], PatientState.InAmbulance, true);
            passengers.Add(patient); MissionDirector.Instance?.PatientLoaded(); return true;
        }
        void Update()
        {
            bool left = Mathf.Repeat(Time.time * 2, 1) < 0.5f;
            Flash(flashLeft, left); Flash(flashRight, !left);
            if (LoadingOpen && MissionDirector.Instance)
                foreach (var stretcher in MissionDirector.Instance.stretchers) TryLoad(stretcher);
        }
        void Flash(Renderer[] renderers, bool lit)
        {
            flash.SetColor("_EmissionColor", lit ? new Color(0.02f, 0.2f, 1) * 6 : Color.black);
            foreach (var renderer in renderers) if (renderer) renderer.SetPropertyBlock(flash);
        }
    }
}
