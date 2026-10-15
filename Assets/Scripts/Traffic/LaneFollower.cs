using UnityEngine;

namespace RescueTeam.Bolum1
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LaneFollower : MonoBehaviour
    {
        public LanePath lane;
        [Min(0.1f)] public float cruiseSpeed = 12, acceleration = 4, braking = 6;
        public Transform[] wheels = new Transform[0];
        public Vector3 wheelAxis = Vector3.right;
        [Min(0.05f)] public float wheelRadius = 0.36f;
        public float Distance { get; private set; }
        public float Speed { get; private set; }
        public float StopDistance { get; private set; } = float.PositiveInfinity;
        public bool AtStop => Distance >= StopDistance - 0.001f;
        public bool AtEnd => lane && Distance >= lane.Length - 0.001f;
        Rigidbody body;
        void Awake() { body = GetComponent<Rigidbody>(); body.isKinematic = true; }
        public void Initialize(LanePath path, float speed)
        {
            lane = path; cruiseSpeed = speed; Distance = 0; Speed = 0;
            body = GetComponent<Rigidbody>(); body.isKinematic = true;
            SetDistance(0);
        }
        public void StopAt(float s) { StopDistance = Mathf.Clamp(s, Distance, lane.Length); }
        public void Continue() { StopDistance = float.PositiveInfinity; }
        public void SetDistance(float s)
        {
            Distance = Mathf.Clamp(s, 0, lane.Length);
            Vector3 p = lane.Sample(Distance, out var tangent);
            body.position = p; body.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            transform.SetPositionAndRotation(body.position, body.rotation);
        }
        // Tahmin ve gerçek sürüş aynı integratörü kullanır; varış takvimi fren süresini de içerir.
        static void Integrate(ref float s, ref float v, float stop, float speed, float accel, float brake, float dt)
        {
            float remaining = stop - s;
            if (remaining <= 0.001f) { s = stop; v = 0; return; }
            float target = Mathf.Min(speed, Mathf.Sqrt(2 * brake * remaining));
            float next = Mathf.MoveTowards(v, target, (target < v ? brake : accel) * dt);
            float move = (v + next) * 0.5f * dt;
            if (move >= remaining || remaining < 0.003f) { s = stop; v = 0; }
            else { s += move; v = next; }
        }
        public float TravelTime(float stop, float dt = 0.02f)
        {
            float s = 0, v = 0, elapsed = 0;
            while (s < stop - 0.001f && elapsed < 600)
            { Integrate(ref s, ref v, stop, cruiseSpeed, acceleration, braking, dt); elapsed += dt; }
            return elapsed;
        }
        public void Tick(float dt, float speedFactor = 1)
        {
            float old = Distance, s = Distance, v = Speed;
            float cap = cruiseSpeed * Mathf.Clamp(speedFactor, 0.05f, 1);
            if (speedFactor < 1) v = Mathf.Min(v, cap);
            Integrate(ref s, ref v, Mathf.Min(lane.Length, StopDistance), cap, acceleration, braking, dt);
            Distance = s; Speed = v;
            Vector3 p = lane.Sample(s, out var tangent);
            body.MovePosition(p); body.MoveRotation(Quaternion.LookRotation(tangent, Vector3.up));
            float degrees = (s - old) / wheelRadius * Mathf.Rad2Deg;
            foreach (var wheel in wheels) if (wheel) wheel.Rotate(wheelAxis, degrees, Space.Self);
        }
    }
}
