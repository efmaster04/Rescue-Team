using System;
using System.Collections.Generic;
using UnityEngine;

// Yol tanımından (OHLayout) kavşak kollarını, şeritleri, kavşak içi bağlantıları,
// çakışma ve öncelik tablolarını hesaplar. Sonuç OHTrafficNetwork'e yazılır.
namespace OfisHaritasi.EditorTools
{
    public class OHNetResult
    {
        public List<OHLane> lanes = new List<OHLane>();
        public List<OHJunction> junctions = new List<OHJunction>();
        public List<int> sources = new List<int>(), sinks = new List<int>();
        // kenar başına ileri/geri şerit kimlikleri
        public Dictionary<int, int> fwd = new Dictionary<int, int>(), rev = new Dictionary<int, int>();
        public List<string> warnings = new List<string>();
    }

    public static class OHNetBuild
    {
        public const float CrosswalkStop = 5.2f;

        public static void Geometry()
        {
            OHLayout.Define();
            foreach (var e in OHLayout.Edges)
                e.line = OHPolyline.Filleted(e.ctrl, Mathf.Max(1f, e.fillet), OHLayout.RoadTop, 1f);

            // kollar
            foreach (var e in OHLayout.Edges)
            {
                if (e.jStart >= 0) Attach(OHLayout.Junctions[e.jStart], e, true);
                if (e.jEnd >= 0) Attach(OHLayout.Junctions[e.jEnd], e, false);
            }
            foreach (var j in OHLayout.Junctions) ComputeStarts(j);
            foreach (var e in OHLayout.Edges)
            {
                e.trimStart = 0; e.trimEnd = 0;
                if (e.jStart >= 0) { var a = ArmOf(OHLayout.Junctions[e.jStart], e, true); e.trimStart = a.start; e.stopStart = a.stop; }
                if (e.jEnd >= 0) { var a = ArmOf(OHLayout.Junctions[e.jEnd], e, false); e.trimEnd = a.start; e.stopEnd = a.stop; }
            }
            // tüneller
            foreach (var t in OHLayout.Tunnels)
            {
                var e = t.edge;
                float ps = t.atEnd ? e.Length - t.length : t.length;
                e.portalS = ps;
                e.portalIndex = OHLayout.Tunnels.IndexOf(t);
                t.portal = e.line.At(ps);
                Vector3 tan = e.line.Tangent(ps);
                t.inward = t.atEnd ? tan : -tan;
                // ızgaraya hizalı dikdörtgen
                Vector3 end = t.portal + t.inward * t.length;
                float half = 7.5f;
                if (Mathf.Abs(t.inward.x) > 0.9f)
                {
                    float x0 = Mathf.Min(t.portal.x, end.x), x1 = Mathf.Max(t.portal.x, end.x);
                    t.rect = Rect.MinMaxRect(x0, t.portal.z - half, x1, t.portal.z + half);
                }
                else
                {
                    float z0 = Mathf.Min(t.portal.z, end.z), z1 = Mathf.Max(t.portal.z, end.z);
                    t.rect = Rect.MinMaxRect(t.portal.x - half, z0, t.portal.x + half, z1);
                }
            }
        }

        static void Attach(OHJunctionDef j, OHEdgeDef e, bool atStart)
        {
            var pl = e.line;
            Vector3 d = atStart ? pl.pts[Mathf.Min(6, pl.pts.Count - 1)] - pl.pts[0] : pl.pts[Mathf.Max(0, pl.pts.Count - 7)] - pl.pts[pl.pts.Count - 1];
            d.y = 0; d.Normalize();
            int dir = -1;
            for (int k = 0; k < 4; k++) if (Vector3.Dot(d, OHLayout.DirVec(k)) > 0.999f) dir = k;
            if (dir < 0) throw new Exception($"{e.name} {j.name} kavşağına eksene hizalı gelmiyor: {d}");
            if (j.arms[dir] != null) throw new Exception($"{j.name} kavşağında {dir} kolu iki kez tanımlı");
            var p = e.prof;
            var arm = new OHArm { dir = dir, edge = e, atStart = atStart, curb = p.curb, raised = p.raised };
            if (atStart) { arm.left = p.cwL; arm.right = p.cwR; arm.swLeft = p.swL; arm.swRight = p.swR; }
            else { arm.left = p.cwR; arm.right = p.cwL; arm.swLeft = p.swR; arm.swRight = p.swL; }
            arm.stop = j.signalized ? CrosswalkStop : 1.0f;
            j.arms[dir] = arm;
        }

        public static OHArm ArmOf(OHJunctionDef j, OHEdgeDef e, bool atStart)
        {
            foreach (var a in j.arms) if (a != null && a.edge == e && a.atStart == atStart) return a;
            return null;
        }

        public static float CornerRadius(OHArm a, OHArm b)
        {
            float sw = Mathf.Min(a.swLeft, b.swRight) + Mathf.Max(a.curb, b.curb);
            return Mathf.Clamp(3.0f * sw, 4.2f, 7f);
        }

        static void ComputeStarts(OHJunctionDef j)
        {
            for (int d = 0; d < 4; d++)
            {
                var a = j.arms[d];
                if (a == null) continue;
                float s = 6f;
                var ccw = j.arms[(d + 1) % 4];
                var cw = j.arms[(d + 3) % 4];
                if (ccw != null) s = Mathf.Max(s, ccw.right + CornerRadius(a, ccw));
                if (cw != null) s = Mathf.Max(s, cw.left + CornerRadius(cw, a));
                // karşı kol yoksa (T) düz bordür: karşı tarafın genişliği kadar
                if (ccw == null && cw == null) s = Mathf.Max(s, 8f);
                a.start = s + 0.01f;
            }
        }

        // ---------------------------------------------------------------- şeritler

        public static OHNetResult Lanes()
        {
            var R = new OHNetResult();
            foreach (var e in OHLayout.Edges)
            {
                var p = e.prof;
                float a = e.S0 + (e.jStart >= 0 ? e.stopStart : 0);
                float b = e.S1 - (e.jEnd >= 0 ? e.stopEnd : 0);
                var f = MakeLane(R, e, a, b, +p.laneOff, p.speed);
                var r = MakeLane(R, e, b, a, -p.laneOff, p.speed);
                R.fwd[e.id] = f.id; R.rev[e.id] = r.id;
                f.weight = r.weight = e.weight;
                if (e.tunnelEnd) { f.sink = true; r.source = true; }
                if (e.tunnelStart) { f.source = true; r.sink = true; }
            }
            foreach (var l in R.lanes) { if (l.sink) R.sinks.Add(l.id); if (l.source) R.sources.Add(l.id); }

            foreach (var j in OHLayout.Junctions) Connect(R, j);
            return R;
        }

        static OHLane MakeLane(OHNetResult R, OHEdgeDef e, float from, float to, float off, float speed)
        {
            var pts = new List<Vector3>();
            float len = Mathf.Abs(to - from);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 2f));
            for (int i = 0; i <= n; i++)
            {
                float s = Mathf.Lerp(from, to, i / (float)n);
                pts.Add(e.line.Offset(s, off));
            }
            var l = new OHLane { id = R.lanes.Count, pts = pts.ToArray(), speedLimit = speed, edge = e.id };
            l.Rebuild();
            R.lanes.Add(l);
            return l;
        }

        static void Connect(OHNetResult R, OHJunctionDef j)
        {
            var J = new OHJunction { name = j.name, center = j.C, signalized = j.signalized, offset = j.id * 7.3f };
            int jIndex = R.junctions.Count;
            R.junctions.Add(J);
            var conns = new List<int>();
            var fromArm = new Dictionary<int, int>();
            var turnOf = new Dictionary<int, int>();
            for (int da = 0; da < 4; da++)
            {
                var A = j.arms[da];
                if (A == null) continue;
                int inLane = A.atStart ? R.rev[A.edge.id] : R.fwd[A.edge.id];
                var inL = R.lanes[inLane];
                for (int db = 0; db < 4; db++)
                {
                    if (db == da) continue;
                    var B = j.arms[db];
                    if (B == null) continue;
                    int outLane = B.atStart ? R.fwd[B.edge.id] : R.rev[B.edge.id];
                    var outL = R.lanes[outLane];
                    int rel = ((db - da) % 4 + 4) % 4;
                    int turn = rel == 2 ? 0 : rel == 3 ? 1 : 2; // 0 düz, 1 sol, 2 sağ
                    Vector3 p0 = inL.pts[inL.pts.Length - 1], p3 = outL.pts[0];
                    Vector3 d0 = (inL.pts[inL.pts.Length - 1] - inL.pts[inL.pts.Length - 2]).normalized;
                    Vector3 d3 = (outL.pts[1] - outL.pts[0]).normalized;
                    var pts = new List<Vector3>();
                    float dist = Vector3.Distance(p0, p3);
                    if (turn == 0)
                    {
                        int n = Mathf.Max(2, Mathf.CeilToInt(dist / 2f));
                        for (int i = 0; i <= n; i++) pts.Add(Vector3.Lerp(p0, p3, i / (float)n));
                    }
                    else if (turn == 2)
                    {
                        // sağa dönüş: köşe bordür yayıyla aynı merkezli yay (Bezier köşeyi kesip kaldırıma çıkarıyordu)
                        Vector3 C = j.C, a = A.A, nA = A.N;
                        float rc = CornerRadius(A, B);
                        Vector3 O = C + a * (B.right + rc) + nA * (A.left + rc);
                        float lin = Vector3.Dot(p0 - C, nA), lout = Vector3.Dot(p3 - C, a);
                        float rIn = (A.left + rc) - lin + 0.45f, rOut = (B.right + rc) - lout + 0.45f;
                        Vector3 pin = O - nA * rIn, pout = O - a * rOut;
                        pin.y = pout.y = p0.y;
                        int n1 = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p0, pin) / 2f));
                        for (int i = 0; i < n1; i++) pts.Add(Vector3.Lerp(p0, pin, i / (float)n1));
                        float f0 = Mathf.Atan2(-nA.z, -nA.x), f1 = Mathf.Atan2(-a.z, -a.x);
                        float df = Mathf.DeltaAngle(f0 * Mathf.Rad2Deg, f1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        int na = 12;
                        for (int i = 0; i <= na; i++)
                        {
                            float t = i / (float)na;
                            float f = f0 + df * t, rr = Mathf.Lerp(rIn, rOut, t);
                            pts.Add(new Vector3(O.x + Mathf.Cos(f) * rr, p0.y, O.z + Mathf.Sin(f) * rr));
                        }
                        int n2 = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(pout, p3) / 2f));
                        for (int i = 1; i <= n2; i++) pts.Add(Vector3.Lerp(pout, p3, i / (float)n2));
                    }
                    else
                    {
                        // iki doğrunun kesişimi: köşe
                        Vector3 k = LineIntersect(p0, d0, p3, d3);
                        float k0 = Vector3.Distance(p0, k), k3 = Vector3.Distance(p3, k);
                        Vector3 c1 = p0 + d0 * k0 * 0.56f, c2 = p3 - d3 * k3 * 0.56f;
                        int n = 14;
                        for (int i = 0; i <= n; i++)
                        {
                            float t = i / (float)n, u = 1 - t;
                            pts.Add(u * u * u * p0 + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * p3);
                        }
                    }
                    var c = new OHLane
                    {
                        id = R.lanes.Count, pts = pts.ToArray(), junction = jIndex, fromLane = inLane, toLane = outLane, turn = turn,
                        speedLimit = turn == 0 ? Mathf.Min(inL.speedLimit, outL.speedLimit) : turn == 2 ? 5.5f : 7f,
                        weight = outL.weight * (turn == 0 ? 1.3f : 1f),
                        signalGroup = j.signalized ? (da % 2 == 0 ? 0 : 1) : -1
                    };
                    c.Rebuild();
                    R.lanes.Add(c);
                    conns.Add(c.id);
                    fromArm[c.id] = da;
                    turnOf[c.id] = turn;
                    var nx = new List<int>(inL.next) { c.id };
                    inL.next = nx.ToArray();
                    c.next = new[] { outLane };
                }
            }
            J.connectors = conns.ToArray();

            // çakışmalar
            foreach (int a in conns)
            {
                var la = R.lanes[a];
                var list = new List<int>();
                foreach (int b in conns)
                {
                    if (a == b) continue;
                    var lb = R.lanes[b];
                    if (la.fromLane == lb.fromLane) continue;
                    if (la.toLane == lb.toLane || MinDistance(la.pts, lb.pts) < 2.7f) list.Add(b);
                }
                la.conflicts = list.ToArray();
            }

            // öncelik
            var major = new HashSet<int>();
            if (j.signalized) { }
            else if (j.major == "EW") { major.Add(0); major.Add(2); }
            else if (j.major == "NS") { major.Add(1); major.Add(3); }
            else
            {
                int best = 0;
                foreach (var A in j.arms) if (A != null) best = Mathf.Max(best, A.edge.prof.cls);
                for (int d = 0; d < 4; d++) if (j.arms[d] != null && j.arms[d].edge.prof.cls == best) major.Add(d);
            }
            foreach (int a in conns)
            {
                var la = R.lanes[a];
                int da = fromArm[a];
                var pIn = new List<int>(); var pC = new List<int>();
                foreach (int b in la.conflicts)
                {
                    var lb = R.lanes[b];
                    int db = fromArm[b];
                    bool give;
                    if (j.signalized)
                    {
                        // aynı fazda: sola dönen, karşıdan gelen düz/sağa yol verir
                        give = la.signalGroup == lb.signalGroup && la.turn == 1 && db == (da + 2) % 4 && lb.turn != 1;
                    }
                    else
                    {
                        bool aMaj = major.Contains(da), bMaj = major.Contains(db);
                        if (aMaj != bMaj) give = !aMaj;
                        else give = la.turn == 1 && db == (da + 2) % 4 && lb.turn != 1;
                        // eşit tali kollar (karşılıklı değil): sağdan gelene yol ver
                        if (!aMaj && !bMaj && db == (da + 1) % 4) give = true;
                    }
                    if (give) { pIn.Add(lb.fromLane); pC.Add(b); }
                }
                la.yields = pC.Count > 0;
                la.prioIn = pIn.ToArray();
                la.prioConn = pC.ToArray();
            }
        }

        static Vector3 LineIntersect(Vector3 p, Vector3 d, Vector3 q, Vector3 e)
        {
            // p + d t = q + e u  (XZ)
            float den = d.x * e.z - d.z * e.x;
            if (Mathf.Abs(den) < 1e-5f) return (p + q) * 0.5f;
            float t = ((q.x - p.x) * e.z - (q.z - p.z) * e.x) / den;
            var r = p + d * t; r.y = p.y;
            return r;
        }

        public static float MinDistance(Vector3[] a, Vector3[] b)
        {
            float best = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
                for (int k = 0; k < b.Length - 1; k++)
                {
                    Vector2 p = new Vector2(a[i].x, a[i].z), s0 = new Vector2(b[k].x, b[k].z), s1 = new Vector2(b[k + 1].x, b[k + 1].z);
                    Vector2 d = s1 - s0; float L2 = Mathf.Max(1e-6f, d.sqrMagnitude);
                    float t = Mathf.Clamp01(Vector2.Dot(p - s0, d) / L2);
                    best = Mathf.Min(best, (p - (s0 + d * t)).magnitude);
                }
            return best;
        }
    }
}
