using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi
{
    /// Carries every floating object along the flood current: bobbing on the swell, tilting, spinning,
    /// sliding around houses and poles, bumping into each other, and re-entering upstream once it
    /// has left the valley downstream.
    public class DebrisDrift : MonoBehaviour
    {
        [System.Serializable]
        public class Item
        {
            public Transform t;
            public float radius = 0.6f;
            public float draft = 0.2f;
            public float bob = 0.06f;
            public float tilt = 5f;
            public float spin = 12f;
            public Vector3 baseEuler;
        }

        public List<Item> items = new List<Item>();
        [Range(0.2f, 3f)] public float speedScale = 1f;
        public float respawnZMin = 430f, respawnZMax = 560f;
        public float exitZ = -640f;

        Vector2[] pos, vel;
        float[] yaw, yawVel, phase, stuck;
        Quaternion[] baseRot;

        void Start()
        {
            int n = items.Count;
            pos = new Vector2[n]; vel = new Vector2[n];
            yaw = new float[n]; yawVel = new float[n]; phase = new float[n]; stuck = new float[n];
            baseRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                if (it.t == null) continue;
                Vector3 p = it.t.position;
                pos[i] = new Vector2(p.x, p.z);
                vel[i] = Valley.Flow(p.x, p.z) * speedScale;
                yaw[i] = it.t.eulerAngles.y;
                phase[i] = Random.value * 100f;
                baseRot[i] = Quaternion.Euler(it.baseEuler);
            }
        }

        void Respawn(int i)
        {
            var it = items[i];
            for (int tries = 0; tries < 20; tries++)
            {
                float z = Random.Range(respawnZMin, respawnZMax);
                float w = Valley.HalfWidth(z);
                float x = Valley.CenterX(z) + Random.Range(-0.65f, 0.65f) * w;
                if (Valley.Depth(x, z) > it.draft + 0.4f)
                {
                    pos[i] = new Vector2(x, z);
                    vel[i] = Valley.Flow(x, z) * speedScale;
                    stuck[i] = 0f;
                    return;
                }
            }
        }

        void Update()
        {
            if (pos == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float t = Time.timeSinceLevelLoad;
            int n = items.Count;

            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                if (it.t == null) continue;
                Vector2 p = pos[i];
                Vector2 f = Valley.Flow(p.x, p.y) * speedScale;

                float follow = 1f - Mathf.Exp(-dt * (1.4f / (0.4f + it.radius)));
                vel[i] = Vector2.Lerp(vel[i], f, follow);
                vel[i] += new Vector2(Mathf.Sin(t * 0.37f + phase[i] * 3f), Mathf.Cos(t * 0.29f + phase[i] * 5f)) * (0.12f * dt);

                // keep out of houses, poles and piers
                int count = Valley.ObstacleCount;
                for (int k = 0; k < count; k++)
                {
                    var o = Valley.GetObstacle(k);
                    float dz = p.y - o.z;
                    if (dz > 30f || dz < -30f) continue;
                    float dx = p.x - o.x;
                    float minD = o.r * 0.9f + it.radius;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < minD * minD)
                    {
                        float d = Mathf.Max(Mathf.Sqrt(d2), 1e-3f);
                        Vector2 nrm = new Vector2(dx / d, dz / d);
                        p = new Vector2(o.x, o.z) + nrm * minD;
                        float into = Vector2.Dot(vel[i], nrm);
                        if (into < 0f) vel[i] -= nrm * into;
                        yawVel[i] += 25f * Mathf.Sign(nrm.x * vel[i].y - nrm.y * vel[i].x) * dt * 10f;
                    }
                }

                p += vel[i] * dt;

                // run aground -> slide back toward the main current
                float depth = Valley.Depth(p.x, p.y);
                if (depth < it.draft + 0.15f)
                {
                    float cx = Valley.CenterX(p.y);
                    vel[i].x += Mathf.Sign(cx - p.x) * 1.5f * dt;
                    vel[i] *= 0.97f;
                }

                if (vel[i].sqrMagnitude < 0.01f) stuck[i] += dt; else stuck[i] = 0f;
                pos[i] = p;
                if (p.y < exitZ || p.x < Valley.MinX + 20f || p.x > Valley.MaxX - 20f || stuck[i] > 25f) Respawn(i);
            }

            // light separation so pieces jostle instead of overlapping
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    Vector2 d = pos[j] - pos[i];
                    float minD = items[i].radius + items[j].radius;
                    float d2 = d.sqrMagnitude;
                    if (d2 < minD * minD && d2 > 1e-6f)
                    {
                        float dist = Mathf.Sqrt(d2);
                        Vector2 push = d / dist * ((minD - dist) * 0.5f);
                        pos[i] -= push; pos[j] += push;
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                if (it.t == null) continue;
                Vector2 p = pos[i];
                float sp = vel[i].magnitude;
                yawVel[i] = Mathf.Lerp(yawVel[i], it.spin * Mathf.Sin(t * 0.21f + phase[i] * 7f) * (0.4f + sp * 0.4f), dt * 0.6f);
                yaw[i] += yawVel[i] * dt;
                float amp = Valley.WaveAmp * (0.6f + 0.3f * sp);
                float y = Valley.WaterLevel(p.y) + Valley.WaveHeight(p.x, p.y, t, amp) - it.draft
                        + Mathf.Sin(t * 1.7f + phase[i] * 11f) * it.bob;
                float pitch = Mathf.Sin(t * 1.3f + phase[i] * 5f) * it.tilt;
                float roll = Mathf.Cos(t * 1.1f + phase[i] * 3f) * it.tilt;
                it.t.SetPositionAndRotation(new Vector3(p.x, y, p.y), Quaternion.Euler(pitch, yaw[i], roll) * baseRot[i]);
            }
        }
    }
}
