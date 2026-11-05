using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SelVadisi.EditorBuild
{
    /// Vertex-coloured low-poly mesh builder with a transform stack.
    /// Colours are sRGB; alpha carries the wind-sway weight, uv0 = (gloss, emission).
    public class MB
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Color32> C = new List<Color32>(4096);
        public readonly List<Vector2> U = new List<Vector2>(4096);
        public readonly List<int> T = new List<int>(8192);

        Matrix4x4 m = Matrix4x4.identity;
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();

        public float Gloss, Emit;
        public List<BoneWeight> W;               // optional skin weights (characters)
        public BoneWeight Weight;                // weight recorded for new vertices when W != null
        public float Wind;                       // constant sway weight (0..1)
        public System.Func<Vector3, float> WindFn; // optional sway weight from world position

        public int Count => V.Count;
        public Matrix4x4 Matrix => m;

        public void Push(Matrix4x4 local) { stack.Push(m); m = m * local; }
        public void Push(Vector3 pos, float yawDeg) => Push(Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one));
        public void Push(Vector3 pos, Quaternion rot) => Push(Matrix4x4.TRS(pos, rot, Vector3.one));
        public void Pop() { m = stack.Pop(); }

        public int Vert(Vector3 p, Color32 c)
        {
            Vector3 w = m.MultiplyPoint3x4(p);
            float wind = WindFn != null ? WindFn(w) : Wind;
            c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(wind * 255f), 0, 255);
            V.Add(w); C.Add(c); U.Add(new Vector2(Gloss, Emit));
            if (W != null) W.Add(Weight);
            return V.Count - 1;
        }

        public static BoneWeight Bone(int a) => new BoneWeight { boneIndex0 = a, weight0 = 1f };
        public static BoneWeight Bones(int a, int b, float tb)
        {
            tb = Mathf.Clamp01(tb);
            if (tb <= 0.001f) return Bone(a);
            if (tb >= 0.999f) return Bone(b);
            return new BoneWeight { boneIndex0 = a, weight0 = 1f - tb, boneIndex1 = b, weight1 = tb };
        }

        /// One cross-section of a lofted surface.
        public struct Ring
        {
            public Vector3 c, u, v;      // centre and in-plane unit axes
            public float rx, rz, n;      // radii along u / v, superellipse exponent (2 = ellipse)
            public Color32 col;
            public System.Func<float, Color32> colFn; // optional colour by angle (0 = +u, pi/2 = +v)
            public BoneWeight w;
            public float gloss;
            public float zOff;            // shifts the ring along v by zOff * rz on the +v side only (bellies, busts, calves)
            public bool wFnShoulder;      // character torso: blend outer vertices toward the shoulders
        }

        public static Vector3 RingPoint(Ring r, float a)
        {
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            float ex = 2f / Mathf.Max(0.5f, r.n);
            float x = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), ex) * r.rx;
            float z = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), ex) * r.rz;
            if (sa > 0f) z *= 1f + r.zOff * sa;
            return r.c + r.u * x + r.v * z;
        }

        /// Lofted tube through rings with shared vertices (smooth when the material uses smooth normals).
        public void Loft(IList<Ring> rings, int seg, bool capStart, bool capEnd)
        {
            var ang = new float[seg];
            for (int k = 0; k < seg; k++) ang[k] = k * Mathf.PI * 2f / seg;
            LoftA(rings, ang, capStart, capEnd);
        }

        /// Loft with a custom (sorted, 0..2pi) angle set shared by every ring, e.g. denser at the front of a torso
        /// so zips, plackets and open fronts painted by angle stay crisp.
        public void LoftA(IList<Ring> rings, float[] ang, bool capStart, bool capEnd)
        {
            int seg = ang.Length;
            int start = Count;
            float g0 = Gloss;
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i];
                Weight = r.w;
                Gloss = r.gloss;
                for (int k = 0; k < seg; k++)
                {
                    float a = ang[k];
                    Vert(RingPoint(r, a), r.colFn != null ? r.colFn(a) : r.col);
                }
            }
            for (int i = 0; i < rings.Count - 1; i++)
            {
                Vector3 axisPt = m.MultiplyPoint3x4((rings[i].c + rings[i + 1].c) * 0.5f);
                for (int k = 0; k < seg; k++)
                {
                    int a = start + i * seg + k, b = start + i * seg + (k + 1) % seg, c = a + seg, d = b + seg;
                    Vector3 cen = (V[a] + V[b] + V[c] + V[d]) * 0.25f;
                    Vector3 outN = cen - axisPt;
                    if (Vector3.Dot(Vector3.Cross(V[c] - V[a], V[d] - V[a]), outN) >= 0f) { Tri(a, c, d); Tri(a, d, b); }
                    else { Tri(a, d, c); Tri(a, b, d); }
                }
            }
            if (capStart) LoftCap(rings[0], start, seg, (rings[0].c - rings[1].c));
            if (capEnd) LoftCap(rings[rings.Count - 1], start + (rings.Count - 1) * seg, seg, (rings[rings.Count - 1].c - rings[rings.Count - 2].c));
            Gloss = g0;
        }

        void LoftCap(Ring r, int ringStart, int seg, Vector3 outward)
        {
            Weight = r.w;
            Gloss = r.gloss;
            int ci = Vert(r.c + outward.normalized * Mathf.Min(r.rx, r.rz) * 0.25f, r.col);
            Vector3 o = m.MultiplyVector(outward);
            for (int k = 0; k < seg; k++)
            {
                int a = ringStart + k, b = ringStart + (k + 1) % seg;
                if (Vector3.Dot(Vector3.Cross(V[a] - V[ci], V[b] - V[ci]), o) >= 0f) Tri(ci, a, b); else Tri(ci, b, a);
            }
        }

        /// Generic quad grid surface (wrapping in u), oriented away from 'inside'. pos(i, j) / col(i, j) in local space.
        public void Grid(int nu, int nv, bool wrapU, System.Func<int, int, Vector3> pos, System.Func<int, int, Color32> col, Vector3 inside)
        {
            int start = Count;
            int cu = wrapU ? nu : nu + 1;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i < cu; i++)
                    Vert(pos(i, j), col(i, j));
            Vector3 ins = m.MultiplyPoint3x4(inside);
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int i1 = wrapU ? (i + 1) % nu : i + 1;
                    int a = start + j * cu + i, b = start + j * cu + i1, c = start + (j + 1) * cu + i, d = start + (j + 1) * cu + i1;
                    Vector3 cen = (V[a] + V[b] + V[c] + V[d]) * 0.25f;
                    Vector3 outN = cen - ins;
                    if (Vector3.Dot(Vector3.Cross(V[c] - V[a], V[d] - V[a]), outN) >= 0f) { Tri(a, c, d); Tri(a, d, b); }
                    else { Tri(a, d, c); Tri(a, b, d); }
                }
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }

        /// Triangle; winding is fixed so the face points along 'n' (local space).
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = c; c = t; }
            int i = Vert(a, col); Vert(b, col); Vert(c, col);
            Tri(i, i + 1, i + 2);
        }

        /// Quad a-b-c-d (a cycle); winding is fixed so the face points along 'n' (local space).
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = d; d = t; }
            int i = Vert(a, col); Vert(b, col); Vert(c, col); Vert(d, col);
            Tri(i, i + 1, i + 2); Tri(i, i + 2, i + 3);
        }

        public void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, Vector3 center)
            => Quad(a, b, c, d, col, (a + b + c + d) * 0.25f - center);

        public const int PX = 1, NX = 2, PY = 4, NY = 8, PZ = 16, NZ = 32;

        /// Axis-aligned box in the current frame. skip = faces to omit (default: bottom).
        public void Box(Vector3 c, Vector3 s, Color32 col, int skip = NY) => Box(c, s, col, col, skip);

        public void Box(Vector3 c, Vector3 s, Color32 col, Color32 top, int skip = NY)
        {
            Vector3 h = s * 0.5f;
            float x0 = c.x - h.x, x1 = c.x + h.x, y0 = c.y - h.y, y1 = c.y + h.y, z0 = c.z - h.z, z1 = c.z + h.z;
            if ((skip & PX) == 0) Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), col, Vector3.right);
            if ((skip & NX) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), col, Vector3.left);
            if ((skip & PY) == 0) Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), top, Vector3.up);
            if ((skip & NY) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), col, Vector3.down);
            if ((skip & PZ) == 0) Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), col, Vector3.forward);
            if ((skip & NZ) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), col, Vector3.back);
        }

        /// Box with base at y (like a building block).
        public void BoxB(float x, float y, float z, float w, float h, float d, Color32 col, int skip = NY)
            => Box(new Vector3(x, y + h * 0.5f, z), new Vector3(w, h, d), col, skip);

        /// Box rotated about Y around its own centre (base at y).
        public void BoxRot(Vector3 basePos, Vector3 size, float yawDeg, Color32 col, int skip = NY)
        {
            Push(basePos, yawDeg);
            Box(new Vector3(0f, size.y * 0.5f, 0f), size, col, skip);
            Pop();
        }

        /// Prism / cylinder along local +Y with base centre c.
        public void Cyl(Vector3 c, float r, float h, int seg, Color32 col, float rTop = -1f, bool capTop = true, bool capBottom = false, float rot = 0f, Color32? capCol = null)
        {
            if (rTop < 0f) rTop = r;
            Color32 cc = capCol ?? col;
            Vector3 top = c + Vector3.up * h;
            for (int k = 0; k < seg; k++)
            {
                float a0 = rot + k * Mathf.PI * 2f / seg, a1 = rot + (k + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = c + d0 * r, b1 = c + d1 * r, t0 = top + d0 * rTop, t1 = top + d1 * rTop;
                Vector3 outN = (d0 + d1).normalized;
                if (rTop > 0.0001f) Quad(b0, t0, t1, b1, col, outN);
                else Tri(b0, top, b1, col, outN);
                if (capTop && rTop > 0.0001f) Tri(top, t0, t1, cc, Vector3.up);
                if (capBottom) Tri(c, b0, b1, cc, Vector3.down);
            }
        }

        public void Cone(Vector3 c, float r, float h, int seg, Color32 col, float rot = 0f) => Cyl(c, r, h, seg, col, 0f, false, false, rot);

        /// Prism between two points (poles, wires, hoses, beams).
        public void Rod(Vector3 a, Vector3 b, float r, int seg, Color32 col, float r2 = -1f, bool caps = true)
        {
            if (r2 < 0f) r2 = r;
            Vector3 ax = b - a;
            float len = ax.magnitude;
            if (len < 1e-5f) return;
            Vector3 dir = ax / len;
            Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(dir, u);
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Quad(a + d0 * r, b + d0 * r2, b + d1 * r2, a + d1 * r, col, d0 + d1);
                if (caps)
                {
                    Tri(a, a + d1 * r, a + d0 * r, col, -dir);
                    Tri(b, b + d0 * r2, b + d1 * r2, col, dir);
                }
            }
        }

        /// Tube along a polyline (shared rings, no caps).
        public void Tube(IList<Vector3> pts, float r, int seg, Color32 col)
        {
            if (pts.Count < 2) return;
            Vector3 prevU = Vector3.zero;
            int baseIdx = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 dir = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                if (i > 0 && i < pts.Count - 1) dir = (pts[i + 1] - pts[i - 1]).normalized;
                Vector3 u = i == 0 ? Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized
                                   : Vector3.ProjectOnPlane(prevU, dir).normalized;
                if (u.sqrMagnitude < 0.5f) u = Vector3.Cross(dir, Vector3.right).normalized;
                prevU = u;
                Vector3 v = Vector3.Cross(dir, u);
                int ring = Count;
                for (int k = 0; k < seg; k++)
                {
                    float a = k * Mathf.PI * 2f / seg;
                    Vert(pts[i] + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r, col);
                }
                if (i > 0)
                {
                    for (int k = 0; k < seg; k++)
                    {
                        int k1 = (k + 1) % seg;
                        int a0 = baseIdx + k, a1 = baseIdx + k1, b0 = ring + k, b1 = ring + k1;
                        // orient each quad outward from the axis
                        Vector3 c0 = (V[a0] + V[a1] + V[b0] + V[b1]) * 0.25f;
                        Vector3 axisPt = (m.MultiplyPoint3x4(pts[i]) + m.MultiplyPoint3x4(pts[i - 1])) * 0.5f;
                        Vector3 outN = c0 - axisPt;
                        if (Vector3.Dot(Vector3.Cross(V[b0] - V[a0], V[b1] - V[a0]), outN) >= 0f) { Tri(a0, b0, b1); Tri(a0, b1, a1); }
                        else { Tri(a0, b1, b0); Tri(a0, a1, b1); }
                    }
                }
                baseIdx = ring;
            }
        }

        // ---- icospheres (shared vertices: faceted look comes from the shader) ----
        static Vector3[] ico0V, ico1V;
        static int[] ico0T, ico1T;

        static void BuildIco()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            FixWinding(v, f);
            ico0V = v.ToArray(); ico0T = f.ToArray();
            var mid = new Dictionary<long, int>();
            var f2 = new List<int>();
            var v2 = new List<Vector3>(v);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                v2.Add(((v2[a] + v2[b]) * 0.5f).normalized);
                mid[key] = v2.Count - 1;
                return v2.Count - 1;
            }
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                f2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            FixWinding(v2, f2);
            ico1V = v2.ToArray(); ico1T = f2.ToArray();
        }

        static void FixWinding(List<Vector3> v, List<int> f)
        {
            for (int i = 0; i < f.Count; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { int tmp = f[i + 1]; f[i + 1] = f[i + 2]; f[i + 2] = tmp; }
            }
        }

        /// Low-poly blob (rocks, tree crowns, bushes). jitter displaces vertices radially.
        public void Ico(Vector3 c, Vector3 scale, Color32 col, int sub, float jitter, System.Random rng, Color32? col2 = null)
        {
            if (ico0V == null) BuildIco();
            Vector3[] vs = sub == 0 ? ico0V : ico1V;
            int[] ts = sub == 0 ? ico0T : ico1T;
            int start = Count;
            for (int i = 0; i < vs.Length; i++)
            {
                float j = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                Color32 cc = col;
                if (col2.HasValue && rng.NextDouble() < 0.4) cc = col2.Value;
                Vert(c + Vector3.Scale(vs[i], scale) * j, cc);
            }
            for (int i = 0; i < ts.Length; i += 3) Tri(start + ts[i], start + ts[i + 1], start + ts[i + 2]);
        }

        /// Tapered elliptical tube between a and c (limbs, torsos, trunks). Radii are along the frame's u/v axes;
        /// for a vertical axis u = x and v = z.
        public void Taper(Vector3 a, Vector3 c, float rx0, float rz0, float rx1, float rz1, int seg, Color32 col, bool capA = true, bool capB = true, Color32? capCol = null)
        {
            Vector3 ax = c - a;
            float len = ax.magnitude;
            if (len < 1e-5f) return;
            Vector3 dir = ax / len;
            Vector3 u = Vector3.ProjectOnPlane(Vector3.right, dir);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.ProjectOnPlane(Vector3.forward, dir);
            u.Normalize();
            Vector3 v = Vector3.Cross(u, dir).normalized;
            Color32 cc = capCol ?? col;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                Vector3 p0 = a + u * (Mathf.Cos(a0) * rx0) + v * (Mathf.Sin(a0) * rz0);
                Vector3 p1 = a + u * (Mathf.Cos(a1) * rx0) + v * (Mathf.Sin(a1) * rz0);
                Vector3 q0 = c + u * (Mathf.Cos(a0) * rx1) + v * (Mathf.Sin(a0) * rz1);
                Vector3 q1 = c + u * (Mathf.Cos(a1) * rx1) + v * (Mathf.Sin(a1) * rz1);
                Vector3 outN = (p0 + p1 + q0 + q1) * 0.25f - (a + c) * 0.5f;
                Quad(p0, q0, q1, p1, col, outN);
                if (capA) Tri(a, p1, p0, cc, -dir);
                if (capB) Tri(c, q0, q1, cc, dir);
            }
        }

        /// Multiplies vertex colours from 'start' to the end by f(worldPos) (baked ambient occlusion / weathering).
        public void ShadeRange(int start, System.Func<Vector3, float> f)
        {
            for (int i = start; i < V.Count; i++)
            {
                float k = f(V[i]);
                Color32 c = C[i];
                C[i] = new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);
            }
        }

        /// Icosphere blob with baked occlusion: underside darker, top lighter.
        public void IcoAO(Vector3 c, Vector3 scale, Color32 col, int sub, float jitter, System.Random rng, float ao, float topLight = 0.08f)
        {
            int start = Count;
            Ico(c, scale, col, sub, jitter, rng);
            Vector3 wc = m.MultiplyPoint3x4(c);
            for (int i = start; i < V.Count; i++)
            {
                Vector3 d = (V[i] - wc).normalized;
                float k = (1f - ao * (0.5f - 0.5f * d.y)) * (1f + topLight * Mathf.Max(0f, d.y));
                Color32 cc = C[i];
                C[i] = new Color32((byte)Mathf.Clamp(cc.r * k, 0, 255), (byte)Mathf.Clamp(cc.g * k, 0, 255), (byte)Mathf.Clamp(cc.b * k, 0, 255), cc.a);
            }
        }

        /// Cone whose base ring is darker than the tip (conifer tiers).
        public void ConeAO(Vector3 c, float r, float h, int seg, Color32 col, float rot, float droop, float baseDark)
        {
            Color32 dark = Shade(col, 1f - baseDark), tipC = Shade(col, 1.12f);
            Vector3 tip = c + Vector3.up * h;
            for (int k = 0; k < seg; k++)
            {
                float a0 = rot + k * Mathf.PI * 2f / seg, a1 = rot + (k + 1) * Mathf.PI * 2f / seg;
                float am = (a0 + a1) * 0.5f;
                Vector3 b0 = c + new Vector3(Mathf.Cos(a0) * r, -droop, Mathf.Sin(a0) * r);
                Vector3 b1 = c + new Vector3(Mathf.Cos(a1) * r, -droop, Mathf.Sin(a1) * r);
                Vector3 bm = c + new Vector3(Mathf.Cos(am) * r * 0.82f, 0.02f, Mathf.Sin(am) * r * 0.82f);
                Vector3 n0 = new Vector3(Mathf.Cos(am), 0.6f, Mathf.Sin(am));
                int i0 = Vert(b0, dark), i1 = Vert(bm, Shade(col, 1f - baseDark * 0.5f)), i2 = Vert(b1, dark), i3 = Vert(tip, tipC);
                // two faces per segment: a slight notch between branches gives a more natural silhouette
                if (Vector3.Dot(Vector3.Cross(V[i1] - V[i0], V[i3] - V[i0]), m.MultiplyVector(n0)) >= 0f) Tri(i0, i1, i3); else Tri(i0, i3, i1);
                if (Vector3.Dot(Vector3.Cross(V[i2] - V[i1], V[i3] - V[i1]), m.MultiplyVector(n0)) >= 0f) Tri(i1, i2, i3); else Tri(i1, i3, i2);
                // underside
                Vector3 hub = c + Vector3.up * (h * 0.12f);
                int j0 = Vert(b0, Shade(col, 0.55f)), j1 = Vert(b1, Shade(col, 0.55f)), j2 = Vert(hub, Shade(col, 0.45f));
                if (Vector3.Dot(Vector3.Cross(V[j1] - V[j0], V[j2] - V[j0]), Vector3.down) >= 0f) Tri(j0, j1, j2); else Tri(j0, j2, j1);
            }
        }

        public void Append(MB other)
        {
            int off = Count;
            if (W != null)
            {
                if (other.W != null) W.AddRange(other.W);
                else for (int i = 0; i < other.Count; i++) W.Add(Weight);
            }
            V.AddRange(other.V); C.AddRange(other.C); U.AddRange(other.U);
            for (int i = 0; i < other.T.Count; i++) T.Add(other.T[i] + off);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PackedVert
        {
            public Vector3 pos;
            public sbyte nx, ny, nz, nw;
            public Color32 col;
            public ushort u, v;
        }

        /// Compact vertex layout (24 bytes): float3 position, snorm8 normal, unorm8 colour, half2 uv.
        public Mesh ToMeshPacked(string name)
        {
            int n = V.Count;
            var nrm = new Vector3[n];
            for (int i = 0; i < T.Count; i += 3)
            {
                int a = T[i], b = T[i + 1], c = T[i + 2];
                Vector3 fn = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                nrm[a] += fn; nrm[b] += fn; nrm[c] += fn;
            }
            var data = new PackedVert[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 nn = nrm[i].sqrMagnitude > 1e-12f ? nrm[i].normalized : Vector3.up;
                data[i] = new PackedVert
                {
                    pos = V[i],
                    nx = (sbyte)Mathf.RoundToInt(nn.x * 127f), ny = (sbyte)Mathf.RoundToInt(nn.y * 127f), nz = (sbyte)Mathf.RoundToInt(nn.z * 127f), nw = 0,
                    col = C[i],
                    u = Mathf.FloatToHalf(U[i].x), v = Mathf.FloatToHalf(U[i].y)
                };
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertexBufferParams(n,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2));
            mesh.SetVertexBufferData(data, 0, 0, n);
            bool small = n < 65535;
            mesh.SetIndexBufferParams(T.Count, small ? IndexFormat.UInt16 : IndexFormat.UInt32);
            if (small)
            {
                var idx = new ushort[T.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = (ushort)T[i];
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length);
            }
            else mesh.SetIndexBufferData(T, 0, 0, T.Count);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, T.Count));
            mesh.RecalculateBounds();
            return mesh;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V);
            mesh.SetColors(C);
            mesh.SetUVs(0, U);
            mesh.SetTriangles(T, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- colour helpers ----
        public static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }
        public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));
        public static Color32 Jitter(Color32 c, System.Random rng, float amount)
        {
            float f = 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;
            return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);
        }
        public static Color32 Shade(Color32 c, float f)
            => new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);
    }

    /// Splits geometry into world-space chunks so Unity can cull it.
    public class ChunkSet
    {
        readonly Dictionary<long, MB> chunks = new Dictionary<long, MB>();
        readonly float size;
        public readonly string Name;

        public ChunkSet(string name, float size = 200f) { Name = name; this.size = size; }

        public MB At(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / size), cz = Mathf.FloorToInt(z / size);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (!chunks.TryGetValue(key, out MB mb)) { mb = new MB(); chunks[key] = mb; }
            return mb;
        }
        public MB At(Vector3 p) => At(p.x, p.z);

        public IEnumerable<KeyValuePair<long, MB>> All => chunks;
        public int VertexCount { get { int n = 0; foreach (var c in chunks.Values) n += c.Count; return n; } }
    }
}
