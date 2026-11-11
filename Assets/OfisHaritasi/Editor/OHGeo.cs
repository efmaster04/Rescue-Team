using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfisHaritasi.EditorTools
{
    // Çok alt-mesh'li basit mesh kurucu. Düz yüzeyler için paylaşılmayan köşe kullanır (faset görünüm).
    public class OHMeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Color32> c = new List<Color32>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<List<int>> sub = new List<List<int>>();
        public bool useColors;
        public bool useUv;
        public float uvScale = 0.25f;

        public OHMeshBuilder(int subCount, bool colors = false, bool uvs = false)
        {
            for (int i = 0; i < subCount; i++) sub.Add(new List<int>());
            useColors = colors; useUv = uvs;
        }

        public int VertexCount { get { return v.Count; } }

        int Add(Vector3 p, Vector3 nrm, Color32 col)
        {
            v.Add(p); n.Add(nrm);
            if (useColors) c.Add(col);
            if (useUv) uv.Add(new Vector2(p.x * uvScale, p.z * uvScale));
            return v.Count - 1;
        }

        public void Tri(int s, Vector3 a, Vector3 b, Vector3 d, Color32 col = default(Color32))
        {
            Vector3 nrm = Vector3.Cross(b - a, d - a);
            if (nrm.sqrMagnitude < 1e-12f) return;
            nrm.Normalize();
            int i0 = Add(a, nrm, col), i1 = Add(b, nrm, col), i2 = Add(d, nrm, col);
            sub[s].Add(i0); sub[s].Add(i1); sub[s].Add(i2);
        }

        // Saat yönünde (yukarıdan bakınca) verilen dörtgen: a-b-c-d. Unity önyüzü saat yönündedir.
        public void Quad(int s, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color32 col = default(Color32))
        {
            Tri(s, a, b, cc, col);
            Tri(s, a, cc, d, col);
        }

        // Yatay yüzeyler için yönü otomatik düzelten üçgen (normal yukarı)
        public void TriUp(int s, Vector3 a, Vector3 b, Vector3 d, Color32 col = default(Color32))
        {
            Vector3 nrm = Vector3.Cross(b - a, d - a);
            if (nrm.y < 0) Tri(s, a, d, b, col); else Tri(s, a, b, d, col);
        }

        public void QuadUp(int s, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color32 col = default(Color32))
        {
            TriUp(s, a, b, cc, col);
            TriUp(s, a, cc, d, col);
        }

        // Dikey duvar: p0->p1 alt kenar, yukarı h; normal "outward" yönüne bakar
        public void Wall(int s, Vector3 p0, Vector3 p1, float y0, float y1, Vector3 outward, Color32 col = default(Color32))
        {
            Vector3 a = new Vector3(p0.x, y0, p0.z), b = new Vector3(p1.x, y0, p1.z);
            Vector3 cc = new Vector3(p1.x, y1, p1.z), d = new Vector3(p0.x, y1, p0.z);
            Vector3 nrm = Vector3.Cross(cc - a, b - a);
            if (Vector3.Dot(nrm, outward) < 0) Quad(s, a, d, cc, b, col); else Quad(s, a, b, cc, d, col);
        }

        public void Box(int s, Vector3 center, Vector3 size, Quaternion rot, Color32 col = default(Color32))
        {
            Vector3 h = size * 0.5f;
            Vector3[] p = new Vector3[8];
            for (int i = 0; i < 8; i++)
                p[i] = center + rot * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            // yüzler (dışa dönük)
            Face(s, p[0], p[2], p[3], p[1], col); // -z
            Face(s, p[4], p[5], p[7], p[6], col); // +z
            Face(s, p[0], p[4], p[6], p[2], col); // -x
            Face(s, p[1], p[3], p[7], p[5], col); // +x
            Face(s, p[2], p[6], p[7], p[3], col); // +y
            Face(s, p[0], p[1], p[5], p[4], col); // -y
        }

        void Face(int s, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color32 col)
        {
            Tri(s, a, b, cc, col); Tri(s, a, cc, d, col);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            if (useColors) m.SetColors(c);
            if (useUv) m.SetUVs(0, uv);
            int used = 0;
            foreach (var s in sub) if (s.Count > 0) used++;
            m.subMeshCount = sub.Count;
            for (int i = 0; i < sub.Count; i++) m.SetTriangles(sub[i], i, false);
            m.RecalculateBounds();
            return m;
        }

        public bool Empty { get { foreach (var s in sub) if (s.Count > 0) return false; return true; } }
    }

    // XZ düzleminde yay köşeli (fillet) çoklu çizgi
    public class OHPolyline
    {
        public readonly List<Vector3> pts = new List<Vector3>();
        public float[] cum;
        public float Length { get { return cum[cum.Length - 1]; } }

        public static OHPolyline Filleted(IList<Vector2> ctrl, float radius, float y, float step = 1f)
        {
            var raw = new List<Vector2>();
            raw.Add(ctrl[0]);
            for (int i = 1; i < ctrl.Count - 1; i++)
            {
                Vector2 a = ctrl[i - 1], b = ctrl[i], c = ctrl[i + 1];
                Vector2 d0 = (b - a).normalized, d1 = (c - b).normalized;
                float ang = Vector2.SignedAngle(d0, d1) * Mathf.Deg2Rad;
                if (Mathf.Abs(ang) < 1e-3f) { raw.Add(b); continue; }
                float r = radius;
                float t = r * Mathf.Tan(Mathf.Abs(ang) * 0.5f);
                float maxT = Mathf.Min((b - a).magnitude, (c - b).magnitude) * 0.48f;
                if (t > maxT) { t = maxT; r = t / Mathf.Tan(Mathf.Abs(ang) * 0.5f); }
                Vector2 p0 = b - d0 * t, p1 = b + d1 * t;
                Vector2 nrm = ang > 0 ? new Vector2(-d0.y, d0.x) : new Vector2(d0.y, -d0.x);
                Vector2 center = p0 + nrm * r;
                float a0 = Mathf.Atan2(p0.y - center.y, p0.x - center.x);
                int segs = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(ang) * r / 1.5f));
                for (int k = 0; k <= segs; k++)
                {
                    float aa = a0 + ang * k / segs;
                    raw.Add(center + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * r);
                }
            }
            raw.Add(ctrl[ctrl.Count - 1]);
            var pl = new OHPolyline();
            // yoğunlaştır
            pl.pts.Add(new Vector3(raw[0].x, y, raw[0].y));
            for (int i = 1; i < raw.Count; i++)
            {
                Vector2 a = raw[i - 1], b = raw[i];
                float L = (b - a).magnitude;
                if (L < 1e-4f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(L / step));
                for (int k = 1; k <= n; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                    pl.pts.Add(new Vector3(p.x, y, p.y));
                }
            }
            pl.Recalc();
            return pl;
        }

        public void Recalc()
        {
            cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        }

        int Seg(float s)
        {
            int lo = 0, hi = pts.Count - 2;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (cum[mid] <= s) lo = mid; else hi = mid - 1; }
            return lo;
        }

        public Vector3 At(float s)
        {
            s = Mathf.Clamp(s, 0, Length);
            int i = Seg(s);
            float t = (s - cum[i]) / Mathf.Max(1e-6f, cum[i + 1] - cum[i]);
            return Vector3.Lerp(pts[i], pts[i + 1], t);
        }

        public Vector3 Tangent(float s)
        {
            float a = Mathf.Max(0, s - 0.75f), b = Mathf.Min(Length, s + 0.75f);
            Vector3 d = At(b) - At(a); d.y = 0;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        public static Vector3 Right(Vector3 t) { return new Vector3(t.z, 0, -t.x); }

        public Vector3 Offset(float s, float off) { return At(s) + Right(Tangent(s)) * off; }

        // Noktayı çizgiye iz düşürür: s ve sağa pozitif yanal uzaklık
        public float Project(Vector3 p, out float lateral)
        {
            float best = float.MaxValue, bs = 0, bl = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = new Vector2(pts[i].x, pts[i].z), b = new Vector2(pts[i + 1].x, pts[i + 1].z), q = new Vector2(p.x, p.z);
                Vector2 d = b - a; float L2 = d.sqrMagnitude; if (L2 < 1e-9f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, d) / L2);
                Vector2 pr = a + d * t;
                float dist = (q - pr).sqrMagnitude;
                if (dist < best)
                {
                    best = dist; bs = cum[i] + t * Mathf.Sqrt(L2);
                    float cr = d.x * (q.y - a.y) - d.y * (q.x - a.x);
                    bl = -Mathf.Sign(cr) * Mathf.Sqrt(dist);
                }
            }
            lateral = bl;
            return bs;
        }
    }

    public static class OHNoise
    {
        public static float N(float x, float z) { return Mathf.PerlinNoise(x + 37.13f, z + 91.7f) * 2f - 1f; }
        public static float Fbm(float x, float z, int oct = 4)
        {
            float a = 0, amp = 0.5f, f = 1f;
            for (int i = 0; i < oct; i++) { a += N(x * f, z * f) * amp; f *= 2.03f; amp *= 0.5f; }
            return a;
        }
        public static float Hash(float x, float z)
        {
            float h = Mathf.Sin(x * 127.1f + z * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }
    }
}
