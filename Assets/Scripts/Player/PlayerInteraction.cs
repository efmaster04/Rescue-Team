using UnityEngine;
namespace RescueTeam.Bolum1
{
    public sealed class PlayerInteraction : MonoBehaviour
    {
        public ThirdPersonPlayer player;
        public StretcherHelperBot helper;
        public StretcherController[] stretchers;
        public Patient[] patients;
        public string Hint { get; private set; }
        StretcherController candidate;
        Patient patient;
        bool front;
        void Update() { FindCandidate(); }
        void FindCandidate()
        {
            candidate = null; patient = null; Hint = "";
            if (player.HeldStretcher) { Hint = "E: Bırak"; return; }
            foreach (var p in patients)
            {
                if (!p || !p.CanPickUp || Vector3.Distance(p.Position, player.transform.position) > 2) continue;
                foreach (var s in stretchers)
                    if (!s.IsRaised && s.CarrierCount == 0 && !s.Passenger && Vector3.Distance(s.patientAnchor.position, p.Position) <= 1.5f)
                    { patient = p; candidate = s; Hint = "E: Hastayı sedyeye al"; return; }
            }
            float best = 1.5f;
            foreach (var s in stretchers)
                foreach (bool end in new[] { true, false })
                {
                    float d = Vector3.Distance(player.HandPosition, (end ? s.frontHandle : s.rearHandle).position);
                    if (d < best) { best = d; candidate = s; front = end; Hint = "E: Sedyeyi tut"; }
                }
        }
        public void Interact()
        {
            FindCandidate();
            if (player.HeldStretcher)
            {
                var held = player.HeldStretcher;
                held.Release(player);
                if (helper && helper.HeldStretcher == held) held.Release(helper);
                return;
            }
            if (!candidate) return;
            if (patient) { candidate.TryPlacePatient(patient, player.transform.position); return; }
            if (candidate.Grab(player, front) && helper && helper.isActiveAndEnabled && helper.assistanceEnabled) helper.Assist(player, candidate, front);
        }
    }
}
