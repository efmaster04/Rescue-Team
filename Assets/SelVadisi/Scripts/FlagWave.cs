using UnityEngine;

namespace SelVadisi
{
    /// Ripples a cloth mesh (flags on poles, sheets hung from balconies).
    /// Flags are attached along local x = 0 and fly toward +x; hanging sheets are attached at local y = 0 and hang toward -y.
    [RequireComponent(typeof(MeshFilter))]
    public class FlagWave : MonoBehaviour
    {
        public bool hanging;
        public float length = 2.4f;
        public float amplitude = 0.22f;
        public float waveLength = 1.7f;
        public float speed = 6f;
        public float phase;

        Mesh mesh;
        Vector3[] baseV, work;

        void Start()
        {
            var mf = GetComponent<MeshFilter>();
            mesh = Instantiate(mf.sharedMesh);
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            baseV = mesh.vertices;
            work = new Vector3[baseV.Length];
        }

        void Update()
        {
            if (mesh == null) return;
            float t = Time.time + phase;
            float k2 = 6.2832f / waveLength;
            for (int i = 0; i < baseV.Length; i++)
            {
                Vector3 v = baseV[i];
                float u = hanging ? -v.y : v.x;
                float side = hanging ? v.x : v.y;
                float k = Mathf.Clamp01(u / length);
                float d = Mathf.Sin(u * k2 - t * speed + side * 0.6f) * amplitude * k
                        + Mathf.Sin(u * 2.1f - t * speed * 0.63f) * amplitude * 0.45f * k;
                if (hanging) d *= 0.6f;
                work[i] = new Vector3(v.x, v.y - (hanging ? 0f : k * k * 0.06f), v.z + d);
            }
            mesh.vertices = work;
            mesh.RecalculateBounds();
        }
    }
}
