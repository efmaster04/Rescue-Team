using UnityEngine;

namespace SelVadisi
{
    /// Tiny looping motions: rotating beacons, a vibrating pump engine, bobbing objects, swaying poles.
    public class SimpleMotion : MonoBehaviour
    {
        public enum Kind { Spin, Shake, Bob, Sway }
        public Kind kind = Kind.Spin;
        public Vector3 axis = Vector3.up;
        public float speed = 180f;
        public float amplitude = 0.01f;
        public float phase;

        Vector3 p0;
        Quaternion r0;

        void Start()
        {
            p0 = transform.localPosition;
            r0 = transform.localRotation;
        }

        void Update()
        {
            float t = Time.time + phase;
            switch (kind)
            {
                case Kind.Spin:
                    transform.localRotation = r0 * Quaternion.AngleAxis(t * speed, axis);
                    break;
                case Kind.Shake:
                    transform.localPosition = p0 + new Vector3(
                        Mathf.Sin(t * speed * 1.13f), Mathf.Sin(t * speed * 1.71f + 1f), Mathf.Sin(t * speed * 0.97f + 2f)) * amplitude;
                    break;
                case Kind.Bob:
                    transform.localPosition = p0 + axis * (Mathf.Sin(t * speed) * amplitude);
                    break;
                case Kind.Sway:
                    transform.localRotation = r0 * Quaternion.AngleAxis(Mathf.Sin(t * speed) * amplitude, axis);
                    break;
            }
        }
    }
}
