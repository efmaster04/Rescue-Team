using System.Collections.Generic;
using UnityEngine;

// Arazi: doğal tepe + kasaba düzlüğü + yol yarmaları + dere vadisi + parsel düzlemleri + tünel üstü.
// 5 m ızgara, 100 m'lik parçalar, yüz başına renk (faset).
namespace OfisHaritasi.EditorTools
{
    public class OHRect
    {
        public Vector3 c; public Vector3 ax, az; public float hx, hz; // merkez, eksenler, yarı boyutlar
        public OHRect(Vector3 center, Quaternion rot, float sizeX, float sizeZ)
        {
            c = center; ax = rot * Vector3.right; az = rot * Vector3.forward; ax.y = az.y = 0; ax.Normalize(); az.Normalize(); hx = sizeX * 0.5f; hz = sizeZ * 0.5f;
        }
        public bool Contains(float x, float z, float margin = 0)
        {
            float dx = x - c.x, dz = z - c.z;
            float u = dx * ax.x + dz * ax.z, v = dx * az.x + dz * az.z;
            return Mathf.Abs(u) <= hx + margin && Mathf.Abs(v) <= hz + margin;
        }
        public float Outside(float x, float z)
        {
            float dx = x - c.x, dz = z - c.z;
            float u = Mathf.Abs(dx * ax.x + dz * ax.z) - hx, v = Mathf.Abs(dx * az.x + dz * az.z) - hz;
            return new Vector2(Mathf.Max(u, 0), Mathf.Max(v, 0)).magnitude + Mathf.Min(Mathf.Max(u, v), 0);
        }
        public Vector3[] Corners()
        {
            return new[] { c - ax * hx - az * hz, c + ax * hx - az * hz, c + ax * hx + az * hz, c - ax * hx + az * hz };
        }
        public bool Overlaps(OHRect o, float margin)
        {
            var axes = new[] { ax, az, o.ax, o.az };
            var A = Corners(); var B = o.Corners();
            foreach (var a in axes)
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in A) { float d = p.x * a.x + p.z * a.z; amin = Mathf.Min(amin, d); amax = Mathf.Max(amax, d); }
                foreach (var p in B) { float d = p.x * a.x + p.z * a.z; bmin = Mathf.Min(bmin, d); bmax = Mathf.Max(bmax, d); }
                if (amax + margin < bmin || bmax + margin < amin) return false;
            }
            return true;
        }
    }

    public class OHRoadSeg { public Vector2 a, b; public float hwL, hwR, vergeTop; public float slope = 0.65f; }

    public class OHRoadIndex
    {
        public readonly List<OHRoadSeg> segs = new List<OHRoadSeg>();
        public readonly List<Rect> junctionBoxes = new List<Rect>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        const float G = 20f;
        static long Key(int i, int j) { return ((long)i << 32) ^ (uint)j; }

        public static OHRoadIndex Build(bool excludeTunnels)
        {
            var R = new OHRoadIndex();
            foreach (var e in OHLayout.Edges)
            {
                var p = e.prof;
                float s0 = 0, s1 = e.Length;
                if (excludeTunnels && e.portalS >= 0) { if (e.tunnelEnd) s1 = e.portalS + 0.01f; else s0 = e.portalS - 0.01f; }
                bool nearPortal = e.portalS >= 0;
                var ss = OHRoadMeshGen.Samples(s0, s1, 2f);
                for (int i = 0; i < ss.Count - 1; i++)
                {
                    var a = e.line.At(ss[i]); var b = e.line.At(ss[i + 1]);
                    float sl = 0.65f;
                    if (nearPortal && Mathf.Abs(ss[i] - e.portalS) < 80f) sl = 1.1f;
                    R.segs.Add(new OHRoadSeg { a = new Vector2(a.x, a.z), b = new Vector2(b.x, b.z), hwL = p.Half(true), hwR = p.Half(false), vergeTop = p.VergeTop, slope = sl });
                }
            }
            foreach (var j in OHLayout.Junctions)
            {
                float m = 0;
                foreach (var a in j.arms) if (a != null) m = Mathf.Max(m, a.start);
                R.junctionBoxes.Add(new Rect(j.pos.x - m, j.pos.y - m, 2 * m, 2 * m));
            }
            for (int i = 0; i < R.segs.Count; i++)
            {
                var s = R.segs[i];
                float x0 = Mathf.Min(s.a.x, s.b.x), x1 = Mathf.Max(s.a.x, s.b.x), z0 = Mathf.Min(s.a.y, s.b.y), z1 = Mathf.Max(s.a.y, s.b.y);
                for (int gx = Mathf.FloorToInt(x0 / G); gx <= Mathf.FloorToInt(x1 / G); gx++)
                    for (int gz = Mathf.FloorToInt(z0 / G); gz <= Mathf.FloorToInt(z1 / G); gz++)
                    {
                        List<int> l; long k = Key(gx, gz);
                        if (!R.grid.TryGetValue(k, out l)) { l = new List<int>(); R.grid[k] = l; }
                        l.Add(i);
                    }
            }
            return R;
        }

        // Noktanın yol ayak izine göre açıklığı (negatif = yolun içinde) ve o yolun bilgisi.
        public float Clearance(float x, float z, float radius, out OHRoadSeg nearest, out float lateralDist)
        {
            nearest = null; lateralDist = float.MaxValue;
            float best = float.MaxValue;
            int r = Mathf.CeilToInt(radius / G);
            int cx = Mathf.FloorToInt(x / G), cz = Mathf.FloorToInt(z / G);
            var q = new Vector2(x, z);
            for (int gx = cx - r; gx <= cx + r; gx++)
                for (int gz = cz - r; gz <= cz + r; gz++)
                {
                    List<int> l;
                    if (!grid.TryGetValue(Key(gx, gz), out l)) continue;
                    foreach (int i in l)
                    {
                        var s = segs[i];
                        Vector2 d = s.b - s.a; float L2 = d.sqrMagnitude; if (L2 < 1e-6f) continue;
                        float t = Vector2.Dot(q - s.a, d) / L2;
                        if (t < -0.02f || t > 1.02f) continue;
                        Vector2 pr = s.a + d * Mathf.Clamp01(t);
                        float dist = (q - pr).magnitude;
                        float cr = d.x * (q.y - s.a.y) - d.y * (q.x - s.a.x);
                        float hw = cr > 0 ? s.hwL : s.hwR; // çarpım pozitif: sol
                        float c = dist - hw;
                        if (c < best) { best = c; nearest = s; lateralDist = dist; }
                    }
                }
            foreach (var b in junctionBoxes)
            {
                float dx = Mathf.Max(b.xMin - x, 0, x - b.xMax), dz = Mathf.Max(b.yMin - z, 0, z - b.yMax);
                float c = Mathf.Sqrt(dx * dx + dz * dz);
                if (c <= 0) c = -1f;
                if (c < best) { best = c; nearest = null; }
            }
            return best;
        }
    }

    public class OHTerrain
    {
        public readonly OHRoadIndex roads;
        public readonly List<OHRect> lots = new List<OHRect>();
        public readonly List<OHRect> paved = new List<OHRect>();
        public Color legacyGrass = new Color(0.38f, 0.46f, 0.23f);

        public OHTerrain(OHRoadIndex roadsForClamp) { roads = roadsForClamp; }

        public static float LegacyW(float x, float z)
        {
            var r = OHLayout.LegacyZone;
            float dx = Mathf.Max(r.xMin - x, 0, x - r.xMax), dz = Mathf.Max(r.yMin - z, 0, z - r.yMax);
            return 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 10f);
        }

        public float CreekCarve(float x, float z)
        {
            float cx = OHLayout.CreekX(z);
            float dxdz = (OHLayout.CreekX(z + 1) - OHLayout.CreekX(z - 1)) * 0.5f;
            float d = Mathf.Abs(x - cx) / Mathf.Sqrt(1 + dxdz * dxdz);
            return OHLayout.CreekBed(z) + Mathf.Max(0, d - OHLayout.CreekHalfBed) * OHLayout.CreekBank;
        }

        public float Height(float x, float z, bool tunnelCell)
        {
            float h = OHLayout.Natural(x, z);
            float lw = LegacyW(x, z);
            if (lw > 0) h = Mathf.Lerp(h, OHLayout.LegacyGround, lw);
            bool inLot = false;
            foreach (var l in lots) if (l.Contains(x, z, 0.6f)) { inLot = true; break; }
            if (inLot) h = OHLayout.LotGround;

            // tünel üstü
            foreach (var t in OHLayout.Tunnels)
            {
                Vector3 d = new Vector3(x - t.portal.x, 0, z - t.portal.z);
                float u = Vector3.Dot(d, t.inward), w = Vector3.Dot(d, OHPolyline.Right(t.inward));
                if (u < -0.001f || u > t.length + 40f) continue;
                float wt = tunnelCell ? 1f : 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(7.5f, 17.5f, Mathf.Abs(w)));
                float wu = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(t.length + 15f, t.length + 40f, u));
                float target = OHLayout.RoadTop + 9.5f;
                if (h < target) h = Mathf.Lerp(h, target, wt * wu);
            }

            if (!tunnelCell)
            {
                OHRoadSeg seg; float lat;
                float c = roads.Clearance(x, z, 70f, out seg, out lat);
                if (c < 70f)
                {
                    float allowed;
                    float vt = seg != null ? seg.vergeTop : OHLayout.SidewalkTop;
                    float slope = seg != null ? seg.slope : 0.65f;
                    if (c <= 0.3f) allowed = OHLayout.RoadTop - 0.2f;
                    else if (c <= 7.5f) allowed = inLot ? 99f : vt - 0.045f;
                    else allowed = (inLot ? OHLayout.LotGround : vt - 0.045f) + (c - 7.5f) * slope;
                    h = Mathf.Min(h, allowed);
                }
            }
            h = Mathf.Min(h, CreekCarve(x, z));
            return h;
        }

        public bool InTunnelRect(float x, float z)
        {
            foreach (var t in OHLayout.Tunnels) if (t.rect.Contains(new Vector2(x, z))) return true;
            return false;
        }

        // ---------------------------------------------------------------- renk

        static Color Mix(Color a, Color b, float t) { return Color.Lerp(a, b, Mathf.Clamp01(t)); }

        public Color32 TriColor(Vector3 c, Vector3 n)
        {
            float x = c.x, z = c.z;
            Color grass = legacyGrass;
            Color lush = new Color(0.31f, 0.43f, 0.20f), dry = new Color(0.56f, 0.54f, 0.31f), forest = new Color(0.27f, 0.35f, 0.19f);
            Color soil = new Color(0.47f, 0.41f, 0.31f), rock = new Color(0.50f, 0.48f, 0.44f), mud = new Color(0.33f, 0.30f, 0.23f);
            float n1 = OHNoise.Fbm(x / 70f, z / 70f, 3), n2 = OHNoise.Fbm(x / 23f + 40, z / 23f - 7, 2);
            Color col = Mix(grass, n1 > 0 ? dry : lush, Mathf.Abs(n1) * 1.4f);
            float hh = c.y - OHLayout.TownGround;
            if (hh > 6f) col = Mix(col, forest, Mathf.InverseLerp(6f, 22f, hh) * (0.55f + 0.3f * n2));
            if (hh > 18f && n2 > 0.15f) col = Mix(col, dry, 0.35f);
            // tarlalar (doğu)
            if (x > 305f && x < OHLayout.EastLine(z) - 15f && hh < 4.5f && z > -330 && z < 200)
            {
                float ang = 12f * Mathf.Deg2Rad;
                float u = (x * Mathf.Cos(ang) + z * Mathf.Sin(ang)) / 46f, v = (-x * Mathf.Sin(ang) + z * Mathf.Cos(ang)) / 68f;
                float fu = Mathf.Floor(u), fv = Mathf.Floor(v);
                float hsh = OHNoise.Hash(fu, fv);
                float edge = Mathf.Min(Mathf.Min(u - fu, 1 - (u - fu)) * 46f, Mathf.Min(v - fv, 1 - (v - fv)) * 68f);
                if (edge > 1.5f)
                {
                    Color fc = hsh < 0.25f ? new Color(0.45f, 0.35f, 0.25f) : hsh < 0.5f ? new Color(0.43f, 0.52f, 0.22f) : hsh < 0.75f ? new Color(0.70f, 0.61f, 0.34f) : new Color(0.52f, 0.50f, 0.32f);
                    float stripe = Mathf.Repeat(v * 68f, 2.2f) < 1.1f ? 0.96f : 1.03f;
                    col = fc * stripe;
                }
            }
            // eğim
            float steep = 1f - n.y;
            if (steep > 0.18f) col = Mix(col, soil, Mathf.InverseLerp(0.18f, 0.32f, steep));
            if (steep > 0.35f) col = Mix(col, rock, Mathf.InverseLerp(0.35f, 0.5f, steep));
            // dere kıyısı
            float bed = OHLayout.CreekBed(z);
            if (Mathf.Abs(x - OHLayout.CreekX(z)) < 14f && c.y < bed + 1.6f) col = Mix(col, mud, Mathf.InverseLerp(bed + 1.6f, bed + 0.6f, c.y));
            // parsel içi bahçe
            foreach (var l in lots) if (l.Contains(x, z, 0)) { col = Mix(col, lush, 0.5f); break; }
            // eski ofis çevresi: orijinal çim tonu
            float lw = LegacyW(x, z);
            if (lw > 0) col = Mix(col, legacyGrass, lw);
            float j = 1f + (OHNoise.Hash(x * 0.37f, z * 0.71f) - 0.5f) * 0.07f;
            col *= j;
            col.a = 1;
            return col;
        }

        // ---------------------------------------------------------------- mesh

        public List<Mesh> BuildChunks(int cellsPerChunk = 20)
        {
            float C = OHLayout.Cell;
            int nx = Mathf.RoundToInt((OHLayout.TerMaxX - OHLayout.TerMinX) / C);
            int nz = Mathf.RoundToInt((OHLayout.TerMaxZ - OHLayout.TerMinZ) / C);
            var Hn = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++)
                    Hn[i, k] = Height(OHLayout.TerMinX + i * C, OHLayout.TerMinZ + k * C, false);
            var chunks = new List<Mesh>();
            for (int ci = 0; ci < nx; ci += cellsPerChunk)
                for (int ck = 0; ck < nz; ck += cellsPerChunk)
                {
                    var mb = new OHMeshBuilder(1, true, false);
                    for (int i = ci; i < Mathf.Min(nx, ci + cellsPerChunk); i++)
                        for (int k = ck; k < Mathf.Min(nz, ck + cellsPerChunk); k++)
                        {
                            float x0 = OHLayout.TerMinX + i * C, z0 = OHLayout.TerMinZ + k * C;
                            bool tun = InTunnelRect(x0 + C * 0.5f, z0 + C * 0.5f);
                            float h00, h10, h11, h01;
                            if (tun) { h00 = Height(x0, z0, true); h10 = Height(x0 + C, z0, true); h11 = Height(x0 + C, z0 + C, true); h01 = Height(x0, z0 + C, true); }
                            else { h00 = Hn[i, k]; h10 = Hn[i + 1, k]; h11 = Hn[i + 1, k + 1]; h01 = Hn[i, k + 1]; }
                            Vector3 p00 = new Vector3(x0, h00, z0), p10 = new Vector3(x0 + C, h10, z0), p11 = new Vector3(x0 + C, h11, z0 + C), p01 = new Vector3(x0, h01, z0 + C);
                            if (Mathf.Abs(h00 - h11) < Mathf.Abs(h10 - h01)) { Tri(mb, p00, p01, p11); Tri(mb, p00, p11, p10); }
                            else { Tri(mb, p00, p01, p10); Tri(mb, p10, p01, p11); }
                        }
                    if (!mb.Empty) chunks.Add(mb.ToMesh($"OH_Arazi_{ci / cellsPerChunk:00}_{ck / cellsPerChunk:00}"));
                }
            return chunks;
        }

        void Tri(OHMeshBuilder mb, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.y < 0) { var t = b; b = c; c = t; n = -n; }
            n.Normalize();
            mb.Tri(0, a, b, c, TriColor((a + b + c) / 3f, n));
        }

        public Mesh Water()
        {
            var mb = new OHMeshBuilder(1, false, false);
            var line = OHLayout.CreekLine();
            var uvs = new List<Vector2>();
            float acc = 0;
            var mesh = new Mesh { name = "OH_Dere_Su" };
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var tri = new List<int>();
            float half = OHLayout.CreekHalfBed + OHLayout.CreekWaterDepth / OHLayout.CreekBank + 0.25f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) acc += Vector3.Distance(line[i - 1], line[i]);
                Vector3 t = i < line.Count - 1 ? line[i + 1] - line[i] : line[i] - line[i - 1];
                t.y = 0; t.Normalize();
                Vector3 r = OHPolyline.Right(t);
                vs.Add(line[i] - r * half); vs.Add(line[i] + r * half);
                ns.Add(Vector3.up); ns.Add(Vector3.up);
                uvs.Add(new Vector2(acc, 0)); uvs.Add(new Vector2(acc, 1));
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    tri.Add(a); tri.Add(a + 2); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 3);
                }
            }
            mesh.SetVertices(vs); mesh.SetNormals(ns); mesh.SetUVs(0, uvs); mesh.SetTriangles(tri, 0);
            // üçgen yönünü kontrol et (yukarı bakmalı)
            var v0 = vs[0]; var v1 = vs[2]; var v2 = vs[1];
            if (Vector3.Cross(v1 - v0, v2 - v0).y < 0)
            {
                for (int i = 0; i < tri.Count; i += 3) { int tmp = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = tmp; }
                mesh.SetTriangles(tri, 0);
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
