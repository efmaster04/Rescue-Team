using UnityEngine;

namespace RescueTeam.Bolum1
{
    public sealed class LanePath : MonoBehaviour
    {
        public Vector3[] points = new Vector3[0];
        float[] distances;
        public float Length { get { EnsureCache(); return distances.Length == 0 ? 0 : distances[distances.Length - 1]; } }
        public void Rebuild() { distances = null; EnsureCache(); }
        void EnsureCache()
        {
            if (distances != null && distances.Length == points.Length) return;
            distances = new float[points.Length];
            for (int i = 1; i < points.Length; i++) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }
        public Vector3 Sample(float s, out Vector3 tangent)
        {
            EnsureCache();
            tangent = Vector3.forward;
            if (points.Length < 2) return transform.position;
            s = Mathf.Clamp(s, 0, Length);
            int i = 1;
            while (i < points.Length - 1 && distances[i] < s) i++;
            tangent = (points[i] - points[i - 1]).normalized;
            return Vector3.Lerp(points[i - 1], points[i], Mathf.InverseLerp(distances[i - 1], distances[i], s));
        }
        public float Project(Vector3 point)
        {
            EnsureCache();
            float best = float.PositiveInfinity, result = 0;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 edge = points[i] - points[i - 1];
                float t = Mathf.Clamp01(Vector3.Dot(point - points[i - 1], edge) / Mathf.Max(edge.sqrMagnitude, 0.00001f));
                float d = (point - (points[i - 1] + edge * t)).sqrMagnitude;
                if (d < best) { best = d; result = distances[i - 1] + edge.magnitude * t; }
            }
            return result;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1], points[i]);
        }
    }
}
