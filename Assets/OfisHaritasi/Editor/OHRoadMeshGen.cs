using System.Collections.Generic;
using UnityEngine;

// Yol kenarı, kavşak, köprü ve tünel mesh'leri.
namespace OfisHaritasi.EditorTools
{
    public static class OHSub
    {
        public const int Asfalt = 0, Bordur = 1, Kaldirim = 2, Cizgi = 3, Banket = 4, Park = 5, Beton = 6, TunelIc = 7, TunelIsik = 8, Count = 9;
    }

    public class OHRoadOverride { public bool left; public float x0, x1; public float curbTop = float.NaN, swTop = float.NaN; }

    public static class OHRoadMeshGen
    {
        const float Skirt = -0.62f;
        public static readonly Dictionary<string, List<OHRoadOverride>> Overrides = new Dictionary<string, List<OHRoadOverride>>
        {
            // ofis önü: eski beton döşeme (E03) ve garaj önündeki dolgu (E26) ile çakışmasın
            { "AnaCadde_3", new List<OHRoadOverride> {
                new OHRoadOverride { left = true, x0 = -4.9f, x1 = -0.5f, swTop = -0.076f },
                new OHRoadOverride { left = true, x0 = -0.5f, x1 = 8.8f, swTop = -0.076f, curbTop = -0.09f },
                new OHRoadOverride { left = true, x0 = 8.8f, x1 = 13.25f, swTop = -0.13f, curbTop = -0.13f } } }
        };

        static float CurbAt(OHEdgeDef e, bool left, float s, Vector3 p)
        {
            float top = OHLayout.CurbTop;
            var cuts = left ? e.cutsL : e.cutsR;
            foreach (var c in cuts) if (s >= c.x && s <= c.y) top = OHLayout.RoadTop + 0.03f;
            List<OHRoadOverride> ov;
            if (Overrides.TryGetValue(e.name, out ov))
                foreach (var o in ov) if (o.left == left && p.x >= o.x0 && p.x <= o.x1 && !float.IsNaN(o.curbTop)) top = o.curbTop;
            return top;
        }

        static float SwAt(OHEdgeDef e, bool left, Vector3 p)
        {
            float top = e.prof.VergeTop;
            List<OHRoadOverride> ov;
            if (Overrides.TryGetValue(e.name, out ov))
                foreach (var o in ov) if (o.left == left && p.x >= o.x0 && p.x <= o.x1 && !float.IsNaN(o.swTop)) top = o.swTop;
            return top;
        }

        public static List<float> Samples(float a, float b, float step)
        {
            var l = new List<float>();
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a) / step));
            for (int i = 0; i <= n; i++) l.Add(Mathf.Lerp(a, b, i / (float)n));
            return l;
        }

        // ---------------------------------------------------------------- yol kenarı

        public static Mesh Edge(OHEdgeDef e)
        {
            var mb = new OHMeshBuilder(OHSub.Count, false, true);
            var p = e.prof;
            var ss = Samples(e.S0, e.S1, 2f);
            float yR = OHLayout.RoadTop;
            for (int i = 0; i < ss.Count - 1; i++)
            {
                float s0 = ss[i], s1 = ss[i + 1];
                Vector3 c0 = e.line.At(s0), c1 = e.line.At(s1);
                Vector3 r0 = OHPolyline.Right(e.line.Tangent(s0)), r1 = OHPolyline.Right(e.line.Tangent(s1));
                System.Func<Vector3, Vector3, float, float, Vector3> P = (c, r, off, y) => { var q = c + r * off; q.y = y; return q; };

                // şerit alanı
                float park = p.parkR;
                float mainR = p.cwR - park;
                mb.QuadUp(OHSub.Asfalt, P(c0, r0, -p.cwL, yR), P(c0, r0, mainR, yR), P(c1, r1, mainR, yR), P(c1, r1, -p.cwL, yR));
                if (park > 0) mb.QuadUp(OHSub.Park, P(c0, r0, mainR, yR), P(c0, r0, p.cwR, yR), P(c1, r1, p.cwR, yR), P(c1, r1, mainR, yR));

                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    float sg = left ? -1 : 1;
                    float cw = left ? p.cwL : p.cwR;
                    float sw = left ? p.swL : p.swR;
                    Vector3 mid = (c0 + c1) * 0.5f;
                    if (p.raised)
                    {
                        float ct0 = CurbAt(e, left, s0, c0), ct1 = CurbAt(e, left, s1, c1);
                        float st0 = SwAt(e, left, c0), st1 = SwAt(e, left, c1);
                        // bordür üstü
                        mb.QuadUp(OHSub.Bordur, P(c0, r0, sg * cw, ct0), P(c0, r0, sg * (cw + p.curb), ct0), P(c1, r1, sg * (cw + p.curb), ct1), P(c1, r1, sg * cw, ct1));
                        // bordür yüzü (yola bakan)
                        Vector3 ia = P(c0, r0, sg * cw, 0), ib = P(c1, r1, sg * cw, 0);
                        WallVar(mb, OHSub.Bordur, ia, ib, yR - 0.02f, ct0, ct1, -r0 * sg);
                        // bordür arkası (kaldırım daha alçaksa)
                        Vector3 ba = P(c0, r0, sg * (cw + p.curb), 0), bb = P(c1, r1, sg * (cw + p.curb), 0);
                        if (ct0 > st0 + 0.005f || ct1 > st1 + 0.005f) WallVar2(mb, OHSub.Bordur, ba, bb, st0, st1, ct0, ct1, r0 * sg);
                        // kaldırım
                        float o0 = cw + p.curb, o1 = cw + p.curb + sw;
                        mb.QuadUp(OHSub.Kaldirim, P(c0, r0, sg * o0, st0), P(c0, r0, sg * o1, st0), P(c1, r1, sg * o1, st1), P(c1, r1, sg * o0, st1));
                        Vector3 oa = P(c0, r0, sg * o1, 0), ob = P(c1, r1, sg * o1, 0);
                        WallVar(mb, OHSub.Kaldirim, oa, ob, Skirt, st0, st1, r0 * sg);
                    }
                    else
                    {
                        float yS = OHLayout.ShoulderTop;
                        mb.QuadUp(OHSub.Banket, P(c0, r0, sg * cw, yS), P(c0, r0, sg * (cw + sw), yS), P(c1, r1, sg * (cw + sw), yS), P(c1, r1, sg * cw, yS));
                        Vector3 oa = P(c0, r0, sg * (cw + sw), 0), ob = P(c1, r1, sg * (cw + sw), 0);
                        mb.Wall(OHSub.Banket, oa, ob, Skirt, yS, r0 * sg);
                    }
                }
            }
            Markings(e, mb);
            return mb.ToMesh("OH_Yol_" + e.name);
        }

        static void WallVar(OHMeshBuilder mb, int sub, Vector3 a, Vector3 b, float y0, float ya, float yb, Vector3 outward)
        {
            Vector3 p0 = new Vector3(a.x, y0, a.z), p1 = new Vector3(b.x, y0, b.z), p2 = new Vector3(b.x, yb, b.z), p3 = new Vector3(a.x, ya, a.z);
            Vector3 nrm = Vector3.Cross(p2 - p0, p1 - p0);
            if (Vector3.Dot(nrm, outward) < 0) mb.Quad(sub, p0, p3, p2, p1); else mb.Quad(sub, p0, p1, p2, p3);
        }

        static void WallVar2(OHMeshBuilder mb, int sub, Vector3 a, Vector3 b, float ya0, float yb0, float ya1, float yb1, Vector3 outward)
        {
            Vector3 p0 = new Vector3(a.x, ya0, a.z), p1 = new Vector3(b.x, yb0, b.z), p2 = new Vector3(b.x, yb1, b.z), p3 = new Vector3(a.x, ya1, a.z);
            Vector3 nrm = Vector3.Cross(p2 - p0, p1 - p0);
            if (Vector3.Dot(nrm, outward) < 0) mb.Quad(sub, p0, p3, p2, p1); else mb.Quad(sub, p0, p1, p2, p3);
        }

        static void Strip(OHMeshBuilder mb, OHEdgeDef e, float sa, float sb, float off, float w, float y)
        {
            if (sb - sa < 0.05f) return;
            var ss = Samples(sa, sb, 2f);
            for (int i = 0; i < ss.Count - 1; i++)
            {
                Vector3 c0 = e.line.At(ss[i]), c1 = e.line.At(ss[i + 1]);
                Vector3 r0 = OHPolyline.Right(e.line.Tangent(ss[i])), r1 = OHPolyline.Right(e.line.Tangent(ss[i + 1]));
                Vector3 a = c0 + r0 * (off - w * 0.5f), b = c0 + r0 * (off + w * 0.5f), c = c1 + r1 * (off + w * 0.5f), d = c1 + r1 * (off - w * 0.5f);
                a.y = b.y = c.y = d.y = y;
                mb.QuadUp(OHSub.Cizgi, a, b, c, d);
            }
        }

        static void Markings(OHEdgeDef e, OHMeshBuilder mb)
        {
            var p = e.prof;
            float y = OHLayout.RoadTop + 0.006f;
            float a = e.S0 + (e.jStart >= 0 ? e.stopStart : 0), b = e.S1 - (e.jEnd >= 0 ? e.stopEnd : 0);
            float solidA = e.jStart >= 0 ? a + 14f : a, solidB = e.jEnd >= 0 ? b - 14f : b;
            if (e.portalS >= 0)
            {
                if (e.tunnelEnd) solidB = Mathf.Min(solidB, e.portalS - 20f);
                else solidA = Mathf.Max(solidA, e.portalS + 20f);
            }
            float dash = p.name == "Ana" ? 1.5f : 3f, period = p.name == "Ana" ? 8.2f : 8f;
            // orta çizgi
            if (solidA > a) Strip(mb, e, a, solidA, 0, 0.12f, y);
            if (solidB < b) Strip(mb, e, solidB, b, 0, 0.12f, y);
            float phase = p.name == "Ana" ? Mathf.Repeat(-3.3f - e.line.At(0).x - 0.75f, period) : 0;
            for (float s = solidA + phase; s + dash < solidB; s += period) Strip(mb, e, s, s + dash, 0, 0.12f, y);
            if (solidA >= solidB) Strip(mb, e, a, b, 0, 0.12f, y);
            if (p.edgeLines) { Strip(mb, e, e.S0 + 2f, e.S1 - 2f, -p.cwL + 0.2f, 0.1f, y); Strip(mb, e, e.S0 + 2f, e.S1 - 2f, p.cwR - 0.2f, 0.1f, y); }
            // durma çizgileri ve yaya geçitleri
            if (e.jStart >= 0)
            {
                var j = OHLayout.Junctions[e.jStart];
                bool line = j.signalized || !IsMajor(j, OHNetBuild.ArmOf(j, e, true));
                if (line) Stop(mb, e, e.S0 + e.stopStart, -p.cwL, 0, y);
                if (j.signalized) Zebra(mb, e, e.S0 + 0.8f, p, y);
            }
            if (e.jEnd >= 0)
            {
                var j = OHLayout.Junctions[e.jEnd];
                bool line = j.signalized || !IsMajor(j, OHNetBuild.ArmOf(j, e, false));
                if (line) Stop(mb, e, e.S1 - e.stopEnd, 0, p.cwR, y);
                if (j.signalized) Zebra(mb, e, e.S1 - 3.8f, p, y);
            }
        }

        public static bool IsMajor(OHJunctionDef j, OHArm a)
        {
            if (j.signalized) return true;
            if (j.major == "EW") return a.dir % 2 == 0;
            if (j.major == "NS") return a.dir % 2 == 1;
            int best = 0;
            foreach (var x in j.arms) if (x != null) best = Mathf.Max(best, x.edge.prof.cls);
            return a.edge.prof.cls == best;
        }

        static void Stop(OHMeshBuilder mb, OHEdgeDef e, float s, float o0, float o1, float y)
        {
            Vector3 c = e.line.At(s), r = OHPolyline.Right(e.line.Tangent(s)), t = e.line.Tangent(s);
            Vector3 a = c + r * (o0 + 0.15f) - t * 0.2f, b = c + r * (o1 - 0.15f) - t * 0.2f;
            Vector3 cc = b + t * 0.4f, d = a + t * 0.4f;
            a.y = b.y = cc.y = d.y = y;
            mb.QuadUp(OHSub.Cizgi, a, b, cc, d);
        }

        static void Zebra(OHMeshBuilder mb, OHEdgeDef e, float s, OHProfile p, float y)
        {
            Vector3 c = e.line.At(s + 1.5f), r = OHPolyline.Right(e.line.Tangent(s + 1.5f)), t = e.line.Tangent(s + 1.5f);
            for (float o = -p.cwL + 0.35f; o + 0.5f < p.cwR - 0.25f; o += 1.0f)
            {
                Vector3 a = c + r * o - t * 1.5f, b = c + r * (o + 0.5f) - t * 1.5f;
                Vector3 cc = b + t * 3f, d = a + t * 3f;
                a.y = b.y = cc.y = d.y = y;
                mb.QuadUp(OHSub.Cizgi, a, b, cc, d);
            }
        }

        // ---------------------------------------------------------------- kavşak

        public static Mesh Junction(OHJunctionDef j)
        {
            var mb = new OHMeshBuilder(OHSub.Count, false, true);
            var C = j.C;
            var arms = new List<OHArm>();
            for (int d = 0; d < 4; d++) if (j.arms[d] != null) arms.Add(j.arms[d]);
            var boundary = new List<Vector3>();
            float yR = OHLayout.RoadTop;
            for (int i = 0; i < arms.Count; i++)
            {
                var A = arms[i]; var B = arms[(i + 1) % arms.Count];
                Vector3 a = A.A, n = A.N;
                Vector3 sR = C + a * A.start - n * A.right, sL = C + a * A.start + n * A.left;
                boundary.Add(sR); boundary.Add(sL);
                int rel = ((B.dir - A.dir) % 4 + 4) % 4;
                bool raised = A.raised || B.raised;
                float curb = raised ? Mathf.Max(A.curb, B.curb, 0.2f) : 0f;
                float vTop = raised ? OHLayout.SidewalkTop : OHLayout.ShoulderTop;
                if (rel == 1)
                {
                    float R = OHNetBuild.CornerRadius(A, B);
                    Vector3 K = C + a * B.right + n * A.left;
                    Vector3 TA = K + a * R, TB = K + n * R, O = K + a * R + n * R;
                    var path = new List<Vector3>(); var nor = new List<Vector3>();
                    path.Add(sL); nor.Add(n);
                    if (A.start > B.right + R + 0.01f) { path.Add(TA); nor.Add(n); }
                    float f0 = Mathf.Atan2(-n.z, -n.x), f1 = Mathf.Atan2(-a.z, -a.x);
                    float df = Mathf.DeltaAngle(f0 * Mathf.Rad2Deg, f1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    int segs = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(df) * R / 0.8f));
                    for (int k = 1; k < segs; k++)
                    {
                        float f = f0 + df * k / segs;
                        Vector3 q = O + new Vector3(Mathf.Cos(f), 0, Mathf.Sin(f)) * R;
                        path.Add(q); nor.Add((O - q).normalized);
                    }
                    Vector3 sRB = C + B.A * B.start - B.N * B.right;
                    if (B.start > A.left + R + 0.01f) { path.Add(TB); nor.Add(a); }
                    path.Add(sRB); nor.Add(a);
                    // asfalt sınırı (ilk ve son nokta zaten kol uçlarında)
                    for (int k = 1; k < path.Count - 1; k++) boundary.Add(path[k]);
                    // bordür + kaldırım
                    Vector3 outA = sL + n * (curb + A.swLeft), outB = sRB + a * (curb + B.swRight);
                    Vector3 Q = C + a * (B.right + curb + B.swRight) + n * (A.left + curb + A.swLeft);
                    CornerVerge(mb, path, nor, curb, vTop, outA, Q, outB, O, R, raised, Mathf.Min(A.swLeft, B.swRight));
                }
                else if (rel == 2)
                {
                    Vector3 sRB = C + B.A * B.start - B.N * B.right;
                    var path = new List<Vector3> { sL, sRB };
                    var nor = new List<Vector3> { n, n };
                    StraightVerge(mb, path, nor, curb, vTop, A.swLeft, raised);
                }
            }
            // asfalt yelpazesi
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector3 p0 = boundary[i], p1 = boundary[(i + 1) % boundary.Count];
                p0.y = p1.y = yR;
                mb.TriUp(OHSub.Asfalt, C, p0, p1);
            }
            return mb.ToMesh("OH_Kavsak_" + j.name);
        }

        static void CurbAlong(OHMeshBuilder mb, List<Vector3> path, List<Vector3> nor, float curb)
        {
            float yR = OHLayout.RoadTop, ct = OHLayout.CurbTop;
            for (int k = 0; k < path.Count - 1; k++)
            {
                Vector3 a = path[k], b = path[k + 1];
                Vector3 a2 = a + nor[k] * curb, b2 = b + nor[k + 1] * curb;
                a.y = b.y = a2.y = b2.y = ct;
                mb.QuadUp(OHSub.Bordur, a, b, b2, a2);
                mb.Wall(OHSub.Bordur, path[k], path[k + 1], yR - 0.02f, ct, -(nor[k] + nor[k + 1]).normalized);
                mb.Wall(OHSub.Bordur, path[k] + nor[k] * curb, path[k + 1] + nor[k + 1] * curb, OHLayout.SidewalkTop, ct, (nor[k] + nor[k + 1]).normalized);
            }
        }

        static void CornerVerge(OHMeshBuilder mb, List<Vector3> path, List<Vector3> nor, float curb, float vTop, Vector3 outA, Vector3 Q, Vector3 outB, Vector3 O, float R, bool raised, float sw)
        {
            int sub = raised ? OHSub.Kaldirim : OHSub.Banket;
            if (raised && curb > 0) CurbAlong(mb, path, nor, curb);
            var inner = new List<Vector3>();
            for (int k = 0; k < path.Count; k++) { var q = path[k] + nor[k] * curb; q.y = vTop; inner.Add(q); }
            Q.y = outA.y = outB.y = vTop;
            bool qInside = (new Vector2(Q.x - O.x, Q.z - O.z)).magnitude < R - curb - 0.05f;
            if (qInside)
            {
                // Q'dan yelpaze
                var poly = new List<Vector3>(inner);
                poly.Add(outB); poly.Insert(0, outA);
                for (int k = 0; k < poly.Count - 1; k++) mb.TriUp(sub, Q, poly[k], poly[k + 1]);
                mb.Wall(sub, outA, Q, -0.62f, vTop, (outA - inner[0]).normalized);
                mb.Wall(sub, Q, outB, -0.62f, vTop, (outB - inner[inner.Count - 1]).normalized);
            }
            else
            {
                // sabit genişlikli bant
                var outer = new List<Vector3>();
                for (int k = 0; k < path.Count; k++) { var q = path[k] + nor[k] * (curb + sw); q.y = vTop; outer.Add(q); }
                for (int k = 0; k < inner.Count - 1; k++) mb.QuadUp(sub, inner[k], inner[k + 1], outer[k + 1], outer[k]);
                for (int k = 0; k < outer.Count - 1; k++) mb.Wall(sub, outer[k], outer[k + 1], -0.62f, vTop, (nor[k] + nor[k + 1]).normalized);
            }
        }

        static void StraightVerge(OHMeshBuilder mb, List<Vector3> path, List<Vector3> nor, float curb, float vTop, float sw, bool raised)
        {
            int sub = raised ? OHSub.Kaldirim : OHSub.Banket;
            if (raised && curb > 0) CurbAlong(mb, path, nor, curb);
            Vector3 a = path[0] + nor[0] * curb, b = path[1] + nor[1] * curb;
            Vector3 c = b + nor[1] * sw, d = a + nor[0] * sw;
            a.y = b.y = c.y = d.y = vTop;
            mb.QuadUp(sub, a, b, c, d);
            mb.Wall(sub, d, c, -0.62f, vTop, nor[0]);
        }

        // ---------------------------------------------------------------- köprü

        public class BridgeSpan { public OHEdgeDef e; public float s0, s1, sMid; }

        public static List<BridgeSpan> FindBridges()
        {
            var list = new List<BridgeSpan>();
            foreach (var e in OHLayout.Edges)
            {
                float start = -1;
                for (float s = e.S0; s <= e.S1; s += 0.5f)
                {
                    var p = e.line.At(s);
                    bool over = Mathf.Abs(p.x - OHLayout.CreekX(p.z)) < 9.2f;
                    if (over && start < 0) start = s;
                    if ((!over || s + 0.5f > e.S1) && start >= 0)
                    {
                        list.Add(new BridgeSpan { e = e, s0 = start, s1 = s, sMid = (start + s) * 0.5f });
                        start = -1;
                    }
                }
            }
            return list;
        }

        public static Mesh Bridge(BridgeSpan b)
        {
            var mb = new OHMeshBuilder(OHSub.Count, false, true);
            var e = b.e; var p = e.prof;
            float wl = p.Half(true), wr = p.Half(false);
            float s0 = b.s0 - 1.5f, s1 = b.s1 + 1.5f;
            var ss = Samples(s0, s1, 1f);
            float deckTop = OHLayout.RoadTop - 0.05f, deckBot = -0.95f;
            for (int i = 0; i < ss.Count - 1; i++)
            {
                Vector3 c0 = e.line.At(ss[i]), c1 = e.line.At(ss[i + 1]);
                Vector3 r0 = OHPolyline.Right(e.line.Tangent(ss[i])), r1 = OHPolyline.Right(e.line.Tangent(ss[i + 1]));
                Vector3 l0 = c0 - r0 * (wl + 0.3f), l1 = c1 - r1 * (wl + 0.3f), q0 = c0 + r0 * (wr + 0.3f), q1 = c1 + r1 * (wr + 0.3f);
                // alt yüz
                Vector3 a = l0, bb = q0, cc = q1, d = l1; a.y = bb.y = cc.y = d.y = deckBot;
                mb.Quad(OHSub.Beton, a, d, cc, bb);
                // yan yüzler ve korkuluk (parapet)
                float topRail = p.VergeTop + 0.95f;
                mb.Wall(OHSub.Beton, l0, l1, deckBot, topRail, -r0);
                mb.Wall(OHSub.Beton, q0, q1, deckBot, topRail, r0);
                Vector3 li0 = c0 - r0 * (wl + 0.05f), li1 = c1 - r1 * (wl + 0.05f), qi0 = c0 + r0 * (wr + 0.05f), qi1 = c1 + r1 * (wr + 0.05f);
                mb.Wall(OHSub.Beton, li0, li1, p.VergeTop, topRail, r0);
                mb.Wall(OHSub.Beton, qi0, qi1, p.VergeTop, topRail, -r0);
                Vector3 t0 = l0, t1 = l1, t2 = li1, t3 = li0; t0.y = t1.y = t2.y = t3.y = topRail; mb.QuadUp(OHSub.Beton, t0, t1, t2, t3);
                t0 = q0; t1 = q1; t2 = qi1; t3 = qi0; t0.y = t1.y = t2.y = t3.y = topRail; mb.QuadUp(OHSub.Beton, t0, t1, t2, t3);
                // sol-sağ korkuluk ile kaldırım arası dolgu (kaldırım kenarı 0.3 m dışarı uzar)
                Vector3 f0 = c0 - r0 * wl, f1 = c1 - r1 * wl; Vector3 g0 = li0, g1 = li1; f0.y = f1.y = g0.y = g1.y = p.VergeTop;
                mb.QuadUp(p.raised ? OHSub.Kaldirim : OHSub.Banket, f0, f1, g1, g0);
                f0 = c0 + r0 * wr; f1 = c1 + r1 * wr; g0 = qi0; g1 = qi1; f0.y = f1.y = g0.y = g1.y = p.VergeTop;
                mb.QuadUp(p.raised ? OHSub.Kaldirim : OHSub.Banket, f0, f1, g1, g0);
            }
            // korkuluk uç yüzleri ve ayaklar (dere yönüne paralel)
            foreach (float sEnd in new[] { s0, s1 })
            {
                Vector3 c = e.line.At(sEnd), r = OHPolyline.Right(e.line.Tangent(sEnd));
                Vector3 t = e.line.Tangent(sEnd);
                float sign = sEnd == s0 ? -1 : 1;
                mb.Wall(OHSub.Beton, c - r * (wl + 0.3f), c - r * (wl + 0.05f), p.VergeTop, p.VergeTop + 0.95f, t * sign);
                mb.Wall(OHSub.Beton, c + r * (wr + 0.05f), c + r * (wr + 0.3f), p.VergeTop, p.VergeTop + 0.95f, t * sign);
            }
            foreach (float sEnd in new[] { b.s0 + 0.6f, b.s1 - 0.6f })
            {
                Vector3 c = e.line.At(sEnd);
                float bed = OHLayout.CreekBed(c.z);
                Vector3 tan = e.line.Tangent(sEnd);
                // dere teğeti
                Vector3 ct = new Vector3(OHLayout.CreekX(c.z + 1f) - OHLayout.CreekX(c.z - 1f), 0, 2f).normalized;
                float cosA = Mathf.Max(0.5f, Mathf.Abs(Vector3.Dot(ct, OHPolyline.Right(tan))));
                float len = (wl + wr + 1.4f) / cosA;
                Vector3 center = c + OHPolyline.Right(tan) * ((wr - wl) * 0.5f);
                center.y = (deckBot + bed - 0.5f) * 0.5f;
                mb.Box(OHSub.Beton, center, new Vector3(1.0f, deckBot - (bed - 0.5f), len), Quaternion.LookRotation(ct));
            }
            return mb.ToMesh("OH_Kopru_" + e.name);
        }

        // ---------------------------------------------------------------- tünel

        public static Mesh Tunnel(OHTunnelDef t, System.Func<float, float, float> terrainTop)
        {
            var mb = new OHMeshBuilder(OHSub.Count, false, true);
            Vector3 f = t.inward, r = OHPolyline.Right(f);
            float y0 = OHLayout.RoadTop;
            float hw = t.halfWidth, h = t.height;
            Vector3 P = t.portal; P.y = 0;
            float len = t.length;
            // iç: yan duvarlar, tavan, arka duvar
            Vector3 a0 = P - r * hw, a1 = P - r * hw + f * len, b0 = P + r * hw, b1 = P + r * hw + f * len;
            mb.Wall(OHSub.TunelIc, a0, a1, y0 - 0.7f, y0 + h, r);
            mb.Wall(OHSub.TunelIc, b0, b1, y0 - 0.7f, y0 + h, -r);
            Vector3 c0 = a0, c1 = a1, c2 = b1, c3 = b0; c0.y = c1.y = c2.y = c3.y = y0 + h;
            mb.Quad(OHSub.TunelIc, c0, c3, c2, c1);
            mb.Tri(OHSub.TunelIc, c0, c1, c2); // tavan iki yüzlü olsun (alttan bakınca görünür)
            mb.Tri(OHSub.TunelIc, c0, c2, c3);
            Vector3 e0 = P + f * len - r * hw, e1 = P + f * len + r * hw;
            mb.Wall(OHSub.TunelIc, e0, e1, y0 - 0.7f, y0 + h, -f);
            // ışık bantları
            for (float s = 6f; s < len - 2f; s += 7f)
            {
                Vector3 cc = P + f * s;
                mb.Box(OHSub.TunelIsik, cc - r * (hw - 0.06f) + Vector3.up * (y0 + h - 0.9f), new Vector3(0.1f, 0.18f, 2.4f), Quaternion.LookRotation(f));
                mb.Box(OHSub.TunelIsik, cc + r * (hw - 0.06f) + Vector3.up * (y0 + h - 0.9f), new Vector3(0.1f, 0.18f, 2.4f), Quaternion.LookRotation(f));
            }
            // ağız duvarı: açıklık dışında arazi yüzüne kadar
            float wallHalf = 15f, thick = 1.4f;
            float step = 0.5f;
            Vector3 front = P - f * 1.1f, back = P + f * 0.3f;
            for (float u = -wallHalf; u < wallHalf - 1e-3f; u += step)
            {
                float u1 = u + step;
                float topU = TopAt(t, terrainTop, u), topU1 = TopAt(t, terrainTop, u1);
                float bot = y0 - 1.2f;
                bool inOpen = u >= -hw - 1e-3f && u1 <= hw + 1e-3f;
                float lo = inOpen ? y0 + h : bot;
                if (topU <= lo + 0.05f && topU1 <= lo + 0.05f) continue;
                Vector3 pa = front + r * u, pb = front + r * u1;
                WallVar(mb, OHSub.Beton, pa, pb, lo, Mathf.Max(lo, topU), Mathf.Max(lo, topU1), -f);
                Vector3 qa = back + r * u, qb = back + r * u1;
                WallVar(mb, OHSub.Beton, qa, qb, lo, Mathf.Max(lo, topU), Mathf.Max(lo, topU1), f);
                // üst kapak
                Vector3 t0 = pa, t1 = pb, t2 = qb, t3 = qa;
                t0.y = t3.y = Mathf.Max(lo, topU); t1.y = t2.y = Mathf.Max(lo, topU1);
                mb.QuadUp(OHSub.Beton, t0, t1, t2, t3);
                if (inOpen)
                {
                    // açıklığın tavan alt yüzü
                    Vector3 s0 = pa, s1 = pb, s2 = qb, s3 = qa; s0.y = s1.y = s2.y = s3.y = y0 + h;
                    mb.Quad(OHSub.Beton, s0, s3, s2, s1);
                }
            }
            // açıklık kenar yüzleri
            foreach (float sg in new[] { -1f, 1f })
            {
                Vector3 pa = front + r * (sg * hw), pb = back + r * (sg * hw);
                mb.Wall(OHSub.Beton, pa, pb, y0 - 0.7f, y0 + h, -r * sg);
            }
            // uç yüzleri
            foreach (float sg in new[] { -1f, 1f })
            {
                float top = TopAt(t, terrainTop, sg * wallHalf);
                if (top < y0) continue;
                mb.Wall(OHSub.Beton, front + r * (sg * wallHalf), back + r * (sg * wallHalf), y0 - 1.2f, top, r * sg);
            }
            return mb.ToMesh("OH_Tunel_" + t.name);
        }

        static float TopAt(OHTunnelDef t, System.Func<float, float, float> terrainTop, float u)
        {
            Vector3 r = OHPolyline.Right(t.inward);
            Vector3 p = t.portal + r * u;
            float inside = terrainTop(p.x + t.inward.x * 1.0f, p.z + t.inward.z * 1.0f);
            float before = terrainTop(p.x - t.inward.x * 1.6f, p.z - t.inward.z * 1.6f);
            float top = Mathf.Max(inside, before) + 0.35f;
            return Mathf.Min(top, OHLayout.RoadTop + 17f);
        }
    }
}
