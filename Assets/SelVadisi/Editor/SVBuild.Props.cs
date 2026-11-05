using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public static partial class SVBuild
    {
        public struct PolePlan
        {
            public Vector3 pos;
            public float height, lean, leanDir;
        }

        static readonly List<List<PolePlan>> lines = new List<List<PolePlan>>();

        /// Called from Plan(): power lines (submerged poles become current obstacles).
        static void PlanPowerLines(System.Random r)
        {
            lines.Clear();
            // A: through the flooded west street (many leaning)
            var a = new List<PolePlan>();
            foreach (var p in poleLines[0])
            {
                float wl = Valley.WaterLevel(p.z);
                bool leans = r.NextDouble() < 0.45;
                a.Add(new PolePlan { pos = p, height = (wl - p.y) + 8.5f, lean = leans ? Rand(r, 4f, 13f) : Rand(r, 0f, 2f), leanDir = Rand(r, 0f, 360f) });
            }
            lines.Add(a);
            // B: high line crossing the valley downstream
            var b = new List<PolePlan>();
            float zb = -425f, cxb = Valley.CenterX(zb), wb = Valley.HalfWidth(zb);
            for (float x = cxb - wb - 90f; x <= cxb + wb + 90f; x += 42f)
            {
                float z = zb + Rand(r, -2f, 2f);
                float y = Valley.TerrainHeight(x, z);
                float wl = Valley.WaterLevel(z);
                b.Add(new PolePlan { pos = new Vector3(x, y, z), height = Mathf.Max(0f, wl - y) + 11f, lean = r.NextDouble() < 0.3 ? Rand(r, 3f, 9f) : 0f, leanDir = Rand(r, 0f, 360f) });
                if (wl - y > -0.5f) obstacles.Add(new FlowObstacle(x, z, 0.7f, 0.7f));
            }
            lines.Add(b);
            // C: along the east road on the dry terrace
            var c = new List<PolePlan>();
            for (float z = -330f; z <= 350f; z += 38f)
            {
                if (Mathf.Abs(z - Valley.BridgeZ) < 10f) continue;
                Vector3 p = RoadPoint(EastRoad, 1, z);
                p.x += 4.2f;
                if (NearStream(p.x, p.z, 7f) || !FreeSpot(p.x, p.z, 1.5f)) continue;
                p.y = Valley.TerrainHeight(p.x, p.z);
                c.Add(new PolePlan { pos = p, height = 9.5f, lean = Rand(r, 0f, 2.5f), leanDir = Rand(r, 0f, 360f) });
            }
            lines.Add(c);
        }

        static void BuildProps(ChunkSet cs)
        {
            var r = new System.Random(7070);
            foreach (var line in lines) PowerLine(cs, line, r);
            foreach (var l in lampSpots)
            {
                float wl = Valley.WaterLevel(l.z);
                Lamp(cs.At(l), l, -90f, (wl - l.y) + 4.8f);
            }
            RescuePad(cs, r);
            Fields(cs, r);
            StrandedDebris(cs, r);
            HouseJams(cs, r);
        }

        // ------------------------------------------------------------------ power lines

        static Vector3[] Pole(MB b, PolePlan p, Vector3 lineDir)
        {
            Quaternion lean = Quaternion.AngleAxis(p.lean, Quaternion.Euler(0f, p.leanDir, 0f) * Vector3.right);
            Vector3 up = lean * Vector3.up;
            Vector3 across = lean * Vector3.Cross(Vector3.up, lineDir).normalized;
            Color32 wood = H("6B4E35"), iron = H("43484B"), ceramic = H("7FA88C");
            Vector3 top = p.pos + up * p.height;
            b.Rod(p.pos - up * 1.2f, top, 0.18f, 7, wood, 0.13f);
            Vector3 arm = p.pos + up * (p.height - 0.75f);
            b.Rod(arm - across * 1.7f, arm + across * 1.7f, 0.075f, 4, wood);
            b.Rod(arm - across * 1.0f, arm - up * 0.9f, 0.03f, 4, iron);
            b.Rod(arm + across * 1.0f, arm - up * 0.9f, 0.03f, 4, iron);
            var pts = new Vector3[3];
            for (int k = -1; k <= 1; k++)
            {
                Vector3 ip = arm + across * (k * 1.45f) + up * 0.08f;
                if (k == 0) ip = top - up * 0.05f;
                b.Cyl(ip, 0.07f, 0.3f, 6, ceramic, 0.05f);
                pts[k + 1] = ip + up * 0.3f;
            }
            return pts;
        }

        static void PowerLine(ChunkSet cs, List<PolePlan> line, System.Random r)
        {
            Vector3[] prev = null;
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 dir = (line[Mathf.Min(i + 1, line.Count - 1)].pos - line[Mathf.Max(i - 1, 0)].pos);
                dir.y = 0f; dir.Normalize();
                var mb = cs.At(line[i].pos);
                var pts = Pole(mb, line[i], dir);
                if (i % 4 == 2)
                {
                    // transformer can
                    Quaternion lean = Quaternion.AngleAxis(line[i].lean, Quaternion.Euler(0f, line[i].leanDir, 0f) * Vector3.right);
                    Vector3 up = lean * Vector3.up;
                    Vector3 c = line[i].pos + up * (line[i].height - 2.6f) + lean * Vector3.Cross(Vector3.up, dir).normalized * 0.45f;
                    mb.Cyl(c, 0.38f, 1.1f, 8, H("8C9396"), 0.36f);
                }
                if (prev != null)
                {
                    float sag = Rand(r, 1.2f, 2.6f);
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 a = prev[k], b = pts[k];
                        Vector3 ab = b - a;
                        float len2 = ab.sqrMagnitude;
                        mb.WindFn = w =>
                        {
                            float t = Mathf.Clamp01(Vector3.Dot(w - a, ab) / len2);
                            return Mathf.Sin(t * Mathf.PI) * 0.55f;
                        };
                        var wire = new List<Vector3>();
                        const int N = 16;
                        for (int s = 0; s <= N; s++)
                        {
                            float t = (float)s / N;
                            wire.Add(Vector3.Lerp(a, b, t) + Vector3.down * (sag + k * 0.15f) * 4f * t * (1f - t));
                        }
                        mb.Tube(wire, 0.028f, 4, H("26292B"));
                        mb.WindFn = null;
                    }
                }
                prev = pts;
            }
        }

        // ------------------------------------------------------------------ rescue staging area

        static Vector3 Ground(float x, float z, float up = 0f) => new Vector3(x, Valley.TerrainHeight(x, z) + up, z);

        static void RescuePad(ChunkSet cs, System.Random r)
        {
            float ex = Valley.PadEdgeX;
            float zc = (Valley.PadZ0 + Valley.PadZ1) * 0.5f;
            var mb = cs.At(ex - 12f, zc);
            Color32 orange = H("F06A1E"), white = H("F2F2F0"), sand = H("B9A27A"), blue = H("2F6FB5");

            // sandbag wall along the water's edge, with a gap for the pump hoses
            for (float z = Valley.PadZ0 + 5f; z < Valley.PadZ1 - 5f; z += 0.62f)
            {
                if (Mathf.Abs(z - (zc - 4f)) < 1.6f) continue;
                for (int row = 0; row < 2; row++)
                {
                    float x = ex - 4.6f + (row == 1 ? 0.25f : 0f);
                    Vector3 g = Ground(x, z + (row == 1 ? 0.31f : 0f));
                    mb.Ico(g + Vector3.up * (0.16f + row * 0.28f), new Vector3(0.36f, 0.16f, 0.3f), MB.Jitter(sand, r, 0.07f), 1, 0.12f, r);
                }
            }

            // pump + hoses into the flood
            Vector3 pump = Ground(ex - 9f, zc - 4f);
            Vector3 pumpTop = pump + Vector3.up * 0.75f;
            var hoseColor = H("1F2123");
            for (int h = 0; h < 2; h++)
            {
                float off = h == 0 ? -0.25f : 0.3f;
                var pts = new List<Vector3>();
                Vector3[] ctrl =
                {
                    pumpTop + new Vector3(0.45f, -0.2f, off),
                    Ground(ex - 7.4f, zc - 4f + off, 0.1f),
                    Ground(ex - 5.6f, zc - 4f + off * 2f, 0.1f),
                    Ground(ex - 3.4f, zc - 4.2f + off * 3f, 0.1f),
                    Ground(ex - 1.0f, zc - 4.6f + off * 5f, 0.08f),
                    new Vector3(ex + 2.6f, Valley.WaterLevel(zc) - 0.3f, zc - 5.2f + off * 8f),
                    new Vector3(ex + 5f, Valley.WaterLevel(zc) - 1.0f, zc - 5.8f + off * 10f)
                };
                for (int i = 0; i < ctrl.Length - 1; i++)
                    for (int s = 0; s < 4; s++)
                    {
                        float t = s / 4f;
                        Vector3 p0 = ctrl[Mathf.Max(i - 1, 0)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(i + 2, ctrl.Length - 1)];
                        pts.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                    }
                pts.Add(ctrl[ctrl.Length - 1]);
                mb.Tube(pts, 0.085f, 6, hoseColor);
            }
            // discharge hose going inland
            mb.Tube(new List<Vector3> { pumpTop + new Vector3(-0.45f, -0.25f, 0f), Ground(ex - 11f, zc - 3.6f, 0.08f), Ground(ex - 15f, zc - 2f, 0.08f), Ground(ex - 19f, zc + 1f, 0.08f) }, 0.07f, 6, H("2E5FA8"));

            // traffic cones marking the work zone
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                Vector3 c = Ground(ex - 6f - t * 18f, Valley.PadZ1 - 4f);
                if (i % 2 == 0) TrafficCone(mb, c, orange, white);
                Vector3 c2 = Ground(ex - 6f - t * 18f, Valley.PadZ0 + 4f);
                if (i % 2 == 1) TrafficCone(mb, c2, orange, white);
            }
            for (int i = 0; i < 6; i++) TrafficCone(mb, Ground(ex - 5.6f, Valley.PadZ0 + 8f + i * 7.5f), orange, white);

            // life rings and vests
            for (int i = 0; i < 7; i++)
            {
                Vector3 c = Ground(ex - Rand(r, 6.5f, 13f), zc + Rand(r, -16f, 12f), 0.11f);
                LifeRing(mb, c, Quaternion.Euler(r.NextDouble() < 0.3 ? 70f : 0f, Rand(r, 0f, 360f), 0f));
            }
            for (int pile = 0; pile < 3; pile++)
            {
                Vector3 c = Ground(ex - 17f - pile * 2.4f, zc + 7f + pile * 1.6f);
                int n = r.Next(4, 8);
                for (int k = 0; k < n; k++)
                {
                    mb.Push(c + new Vector3(Rand(r, -0.2f, 0.2f), 0.08f + k * 0.13f, Rand(r, -0.2f, 0.2f)), Quaternion.Euler(0f, Rand(r, 0f, 360f), 0f));
                    mb.Box(new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.12f, 0.72f), MB.Jitter(H("F07A1C"), r, 0.05f), 0);
                    mb.Box(new Vector3(0f, 0.05f, -0.1f), new Vector3(0.64f, 0.04f, 0.08f), H("D9DADB"), 0);
                    mb.Box(new Vector3(0f, 0.06f, 0.12f), new Vector3(0.2f, 0.04f, 0.3f), H("2B2D30"), 0);
                    mb.Pop();
                }
            }

            // supply crates and water packs under a tarp
            Vector3 crates = Ground(ex - 25f, zc - 10f);
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < (i + j) % 3 + 1; k++)
                    {
                        Vector3 c = crates + new Vector3(i * 0.62f, k * 0.42f, j * 0.48f);
                        mb.BoxB(c.x, c.y, c.z, 0.58f, 0.4f, 0.44f, (i + j + k) % 3 == 0 ? H("E9EEF2") : blue);
                    }
            for (int i = 0; i < 6; i++)
            {
                Vector3 c = crates + new Vector3(-1.6f, 0f, i * 0.42f - 0.4f);
                mb.BoxB(c.x, c.y, c.z, 0.6f, 0.34f, 0.4f, H("A9CDEB"));
                mb.BoxB(c.x, c.y + 0.34f, c.z, 0.6f, 0.34f, 0.4f, H("A9CDEB"));
            }
            mb.Quad(crates + new Vector3(-2.2f, 1.0f, -0.8f), crates + new Vector3(-2.2f, 1.0f, 2.3f), crates + new Vector3(-0.4f, 0.95f, 2.3f), crates + new Vector3(-0.4f, 0.95f, -0.8f), H("2C67B8"), Vector3.up);

            // gazebo tent with a table
            Vector3 tent = Ground(ex - 19f, zc - 15f);
            Color32 tentC = H("2E6DB4");
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    mb.Rod(tent + new Vector3(i * 2f, -0.3f, j * 2f), tent + new Vector3(i * 2f, 2.4f, j * 2f), 0.04f, 5, H("D7D9DA"));
            Vector3 apex = tent + Vector3.up * 3.3f;
            for (int k = 0; k < 4; k++)
            {
                float a0 = k * 90f + 45f, a1 = a0 + 90f;
                Vector3 c0 = tent + Quaternion.Euler(0f, a0, 0f) * new Vector3(0f, 0f, 2.9f) + Vector3.up * 2.4f;
                Vector3 c1 = tent + Quaternion.Euler(0f, a1, 0f) * new Vector3(0f, 0f, 2.9f) + Vector3.up * 2.4f;
                Vector3 outN = Quaternion.Euler(0f, (a0 + a1) * 0.5f, 0f) * Vector3.forward + Vector3.up;
                mb.Tri(c0, apex, c1, k % 2 == 0 ? tentC : white, outN);
                mb.Tri(c0, apex, c1, MB.Shade(tentC, 0.7f), -outN);
                mb.Quad(c0, c1, c1 - Vector3.up * 0.3f, c0 - Vector3.up * 0.3f, tentC, outN - Vector3.up);
            }
            mb.BoxB(tent.x, tent.y + 0.72f, tent.z, 1.8f, 0.06f, 0.8f, H("E9E6DE"), 0);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    mb.Rod(tent + new Vector3(i * 0.8f, 0f, j * 0.35f), tent + new Vector3(i * 0.8f, 0.72f, j * 0.35f), 0.025f, 4, H("777B7E"));
            mb.BoxB(tent.x - 0.4f, tent.y + 0.78f, tent.z, 0.4f, 0.25f, 0.3f, H("2B2D30"));
            mb.BoxB(tent.x + 0.4f, tent.y + 0.78f, tent.z + 0.1f, 0.5f, 0.06f, 0.35f, H("F1EFE8"));

            // light tower
            Vector3 lt = Ground(ex - 12f, Valley.PadZ0 + 7f);
            mb.BoxB(lt.x, lt.y + 0.3f, lt.z, 1.4f, 1.0f, 2.4f, H("E8C33A"));
            for (int s = -1; s <= 1; s += 2) mb.Rod(lt + new Vector3(s * 0.8f, 0.35f, 0.7f), lt + new Vector3(s * 0.8f, 0.35f, 0.7f) + new Vector3(0f, 0f, 0.01f), 0.35f, 8, H("222426"));
            mb.Rod(lt + Vector3.up * 1.3f, lt + Vector3.up * 7.5f, 0.09f, 6, H("BFC3C5"));
            mb.Box(lt + Vector3.up * 7.6f, new Vector3(2.2f, 0.2f, 0.25f), H("3B3F42"), 0);
            for (int k = 0; k < 4; k++)
            {
                Vector3 lp = lt + new Vector3(-0.85f + k * 0.56f, 7.95f, -0.05f);
                mb.Box(lp, new Vector3(0.45f, 0.45f, 0.3f), H("3B3F42"), 0);
                mb.Emit = 1.4f;
                mb.Box(lp + new Vector3(0f, 0f, -0.16f), new Vector3(0.38f, 0.38f, 0.03f), H("FFF6DE"), 0);
                mb.Emit = 0f;
            }

            // rescue workers and evacuees
            Vector3 water = new Vector3(ex + 10f, Valley.WaterLevel(zc), zc);
            Vector3 helpPad = new Vector3(ex + 30f, Valley.WaterLevel(zc), zc + 10f);
            int padGroup = nextGroup++;
            void Pad(Vector3 p, float yaw, SelVadisi.Beh hint, int kind, int group)
            {
                personSpots.Add(new PersonSpot { pos = p, yaw = yaw, hint = hint, kind = kind, loc = SpotLoc.Pad, group = group, help = helpPad, water = water + new Vector3(0f, 0f, Rand(r, -15f, 15f)) });
            }
            // kneeling at the pump, fiddling with the valve
            {
                Vector3 toward = new Vector3(-1f, 0f, 0.15f).normalized;
                Vector3 kp = pump + toward * 0.8f; kp.y = Valley.TerrainHeight(kp.x, kp.z);
                var sp = new PersonSpot { pos = kp, yaw = Mathf.Atan2(-toward.x, -toward.z) * Mathf.Rad2Deg, hint = SelVadisi.Beh.Kneel, kind = 1, loc = SpotLoc.Pad, group = padGroup, hasKnob = true, knob = pump + toward * 0.32f + Vector3.up * 0.55f, help = helpPad, water = water };
                personSpots.Add(sp);
            }
            Pad(Ground(ex - 6.2f, zc + 2.6f), 95f, SelVadisi.Beh.Radio, 1, padGroup);            // team leader at the sandbags
            Pad(Ground(ex - 5.8f, zc - 9f), 80f, SelVadisi.Beh.Point, 1, padGroup);
            Pad(Ground(ex - 7.0f, zc - 10.2f), 70f, SelVadisi.Beh.Direct, 1, padGroup);
            Pad(Ground(ex - 18f, zc - 13f), 200f, SelVadisi.Beh.Talk, 1, nextGroup);            // briefing at the gazebo
            Pad(Ground(ex - 19.2f, zc - 13.6f), 30f, SelVadisi.Beh.Talk, 1, nextGroup);
            Pad(Ground(ex - 18.4f, zc - 14.6f), 330f, SelVadisi.Beh.Talk, 1, nextGroup++);
            // two workers walking lanes between the supply crates and the water's edge
            for (int i = 0; i < 2; i++)
            {
                Vector3 a = Ground(ex - 23f + i * 1.2f, zc - 6f + i * 3f), b = Ground(ex - 7.5f, zc + 6f + i * 4f);
                var sp = new PersonSpot { pos = a, yaw = 90f, hint = SelVadisi.Beh.Walk, kind = 1, loc = SpotLoc.Pad, group = padGroup, lane = new[] { a, Ground((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f + 1.5f), b }, help = helpPad, water = water };
                personSpots.Add(sp);
            }
            // an evacuated family waiting under blankets, and a medic with them
            int fam = nextGroup++;
            Pad(Ground(ex - 21.4f, zc + 3.8f), 90f, SelVadisi.Beh.Stand, 0, fam);
            Pad(Ground(ex - 23.8f, zc + 6.0f), 120f, SelVadisi.Beh.Stand, 3, fam);
            Pad(Ground(ex - 23.2f, zc + 7.2f), 80f, SelVadisi.Beh.Stand, 2, fam);
            Pad(Ground(ex - 20.8f, zc + 4.6f), 250f, SelVadisi.Beh.Comfort, 1, fam);
            Pad(Ground(ex - 15f, zc + 9f), 60f, SelVadisi.Beh.Stand, 0, -1);
            pumpPos = pump;
            beaconPos = Ground(ex - 27f, Valley.PadZ1 - 6f);
            mb.Rod(beaconPos, beaconPos + Vector3.up * 4.2f, 0.07f, 6, H("BFC3C5"));
            mb.Cyl(beaconPos, 0.35f, 0.3f, 8, H("3B3F42"));
        }

        static Vector3 pumpPos, beaconPos;

        static void TrafficCone(MB b, Vector3 p, Color32 orange, Color32 white)
        {
            b.BoxB(p.x, p.y, p.z, 0.42f, 0.04f, 0.42f, H("2B2D30"), 0);
            b.Cyl(p + Vector3.up * 0.04f, 0.17f, 0.3f, 8, orange, 0.12f, false);
            b.Cyl(p + Vector3.up * 0.34f, 0.12f, 0.12f, 8, white, 0.1f, false);
            b.Cone(p + Vector3.up * 0.46f, 0.1f, 0.3f, 8, orange);
        }

        static void LifeRing(MB b, Vector3 c, Quaternion rot)
        {
            const int N = 12;
            const float R = 0.32f, tube = 0.085f;
            b.Push(c, rot);
            for (int i = 0; i < N; i++)
            {
                float a0 = i * Mathf.PI * 2f / N, a1 = (i + 1) * Mathf.PI * 2f / N;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * R, 0f, Mathf.Sin(a0) * R), p1 = new Vector3(Mathf.Cos(a1) * R, 0f, Mathf.Sin(a1) * R);
                b.Rod(p0, p1, tube, 6, (i / 3) % 2 == 0 ? H("E84A2A") : H("F4F4F2"), -1f, true);
            }
            b.Pop();
        }

        // ------------------------------------------------------------------ downstream fields: greenhouses, hay, fences

        static void Fields(ChunkSet cs, System.Random r)
        {
            // greenhouses half-drowned on the west flood margin
            for (int i = 0; i < 9; i++)
            {
                float z = -575f + i * 22f;
                float x = Valley.CenterX(z) - Valley.HalfWidth(z) + 16f + (i % 2) * 2f;
                if (Valley.Depth(x, z) > 1.6f) x -= 8f;
                Greenhouse(cs.At(x, z), new Vector3(x, Valley.TerrainHeight(x, z), z), r, i % 3 == 1);
            }
            for (int i = 0; i < 6; i++)
            {
                float z = -540f + i * 24f;
                Vector3 p = RoadPoint(EastRoad, 1, z) + new Vector3(18f, 0f, 0f);
                p.y = Valley.TerrainHeight(p.x, p.z);
                if (Valley.Depth(p.x, p.z) > -0.5f) continue;
                Greenhouse(cs.At(p), p, r, false);
            }
            // hay bales and fences on the dry terraces
            for (int g = 0; g < 10; g++)
            {
                int side = g % 2 == 0 ? -1 : 1;
                float z = Rand(r, -600f, -380f);
                float e = Rand(r, 22f, 10f + Valley.ShelfWidth(z, side) - 6f);
                float x = Valley.SideX(z, side, e);
                var mb = cs.At(x, z);
                int n = r.Next(4, 9);
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = Ground(x + Rand(r, -6f, 6f), z + Rand(r, -6f, 6f));
                    if (Valley.Depth(p.x, p.z) > -0.4f) continue;
                    HayBale(mb, p, Rand(r, 0f, 360f), r);
                }
            }
            for (int side = -1; side <= 1; side += 2)
                for (float z = -620f; z < -360f; z += 3f)
                {
                    float e = 17f;
                    Vector3 p = new Vector3(Valley.SideX(z, side, e), 0f, z);
                    p.y = Valley.TerrainHeight(p.x, p.z);
                    Vector3 q = new Vector3(Valley.SideX(z + 3f, side, e), 0f, z + 3f);
                    q.y = Valley.TerrainHeight(q.x, q.z);
                    var mb = cs.At(p);
                    Color32 wood = H("8E6B47");
                    mb.Rod(p + Vector3.down * 0.3f, p + Vector3.up * 1.1f, 0.06f, 4, wood);
                    mb.Rod(p + Vector3.up * 0.95f, q + Vector3.up * 0.95f, 0.04f, 4, wood);
                    mb.Rod(p + Vector3.up * 0.5f, q + Vector3.up * 0.5f, 0.04f, 4, wood);
                }
        }

        static void Greenhouse(MB b, Vector3 p, System.Random r, bool torn)
        {
            float yaw = Rand(r, -6f, 6f);
            b.Push(p + Vector3.down * 0.2f, yaw);
            const float len = 18f, rad = 3.4f;
            Color32 film = H("D6E2E6"), frame = H("AEB4B6");
            const int arc = 8;
            for (float z = -len * 0.5f; z <= len * 0.5f + 0.01f; z += 2f)
                for (int k = 0; k < arc; k++)
                {
                    float a0 = Mathf.PI * k / arc, a1 = Mathf.PI * (k + 1) / arc;
                    b.Rod(new Vector3(Mathf.Cos(a0) * rad, Mathf.Sin(a0) * rad, z), new Vector3(Mathf.Cos(a1) * rad, Mathf.Sin(a1) * rad, z), 0.04f, 4, frame, -1f, false);
                }
            b.Gloss = 0.55f;
            for (float z = -len * 0.5f; z < len * 0.5f - 0.01f; z += 2f)
                for (int k = 0; k < arc; k++)
                {
                    if (torn && r.NextDouble() < 0.3) continue;
                    float a0 = Mathf.PI * k / arc, a1 = Mathf.PI * (k + 1) / arc;
                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * rad, Mathf.Sin(a0) * rad, z), p1 = new Vector3(Mathf.Cos(a1) * rad, Mathf.Sin(a1) * rad, z);
                    Vector3 outN = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), Mathf.Sin((a0 + a1) * 0.5f), 0f);
                    Color32 c = MB.Jitter(film, r, 0.04f);
                    b.Quad(p0, p1, p1 + Vector3.forward * 2f, p0 + Vector3.forward * 2f, c, outN);
                    b.Quad(p0, p1, p1 + Vector3.forward * 2f, p0 + Vector3.forward * 2f, MB.Shade(c, 0.8f), -outN);
                }
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < arc; k++)
                {
                    float a0 = Mathf.PI * k / arc, a1 = Mathf.PI * (k + 1) / arc;
                    Vector3 c0 = new Vector3(0f, 0f, s * len * 0.5f);
                    b.Tri(c0, new Vector3(Mathf.Cos(a0) * rad, Mathf.Sin(a0) * rad, s * len * 0.5f), new Vector3(Mathf.Cos(a1) * rad, Mathf.Sin(a1) * rad, s * len * 0.5f), film, new Vector3(0f, 0f, s));
                }
            b.Gloss = 0f;
            b.Pop();
        }

        static void HayBale(MB b, Vector3 p, float yaw, System.Random r)
        {
            Color32 straw = MB.Jitter(H("D8BC6A"), r, 0.06f);
            b.Push(p + Vector3.up * 0.72f, Quaternion.Euler(0f, yaw, 90f));
            b.Cyl(new Vector3(0f, -0.6f, 0f), 0.72f, 1.2f, 10, straw, -1f, true, true, 0f, H("C4A55A"));
            b.Pop();
        }

        // ------------------------------------------------------------------ static debris washed onto the banks

        static void StrandedDebris(ChunkSet cs, System.Random r)
        {
            Color32[] plastic = { H("2F6DB5"), H("D94436"), H("3E9E57"), H("E8C33A"), H("F0F0EE") };
            for (int i = 0; i < 160; i++)
            {
                float z = Rand(r, -560f, 420f);
                int side = r.NextDouble() < 0.5 ? -1 : 1;
                float e = Rand(r, -0.6f, 1.6f);
                float x = Valley.SideX(z, side, e);
                if (InPad(x, z, 4f) || NearRamp(x, z, 2f)) continue;
                Vector3 p = Ground(x, z);
                if (p.y < Valley.WaterLevel(z) - 0.3f) continue;
                var mb = cs.At(p);
                float yaw = Rand(r, 0f, 360f);
                switch (r.Next(6))
                {
                    case 0: // log / branch
                    {
                        Vector3 d = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Rand(r, 1.2f, 2.5f);
                        mb.Rod(p - d + Vector3.up * 0.12f, p + d + Vector3.up * 0.12f, Rand(r, 0.1f, 0.2f), 6, H("6A4B33"));
                        if (r.NextDouble() < 0.5) mb.Ico(p + d + Vector3.up * 0.3f, new Vector3(0.6f, 0.35f, 0.5f), H("5F7F3E"), 0, 0.25f, r, H("8A7448"));
                        break;
                    }
                    case 1: // planks
                        for (int k = 0; k < 3; k++) mb.BoxRot(p + new Vector3(Rand(r, -0.4f, 0.4f), k * 0.05f, Rand(r, -0.4f, 0.4f)), new Vector3(0.22f, 0.04f, Rand(r, 1.5f, 2.6f)), yaw + Rand(r, -25f, 25f), MB.Jitter(H("A78B63"), r, 0.08f), 0);
                        break;
                    case 2: // tire
                        mb.Push(p + Vector3.up * 0.1f, Quaternion.Euler(Rand(r, -10f, 10f), yaw, 0f));
                        for (int k = 0; k < 10; k++)
                        {
                            float a0 = k * Mathf.PI * 0.2f, a1 = (k + 1) * Mathf.PI * 0.2f;
                            mb.Rod(new Vector3(Mathf.Cos(a0) * 0.3f, 0f, Mathf.Sin(a0) * 0.3f), new Vector3(Mathf.Cos(a1) * 0.3f, 0f, Mathf.Sin(a1) * 0.3f), 0.1f, 5, H("202022"));
                        }
                        mb.Pop();
                        break;
                    case 3: // plastic crate / bin
                        mb.BoxRot(p, new Vector3(0.55f, 0.35f, 0.4f), yaw, Pick(r, plastic));
                        break;
                    case 4: // mud-covered bucket
                        mb.Cyl(p, 0.18f, 0.32f, 8, Pick(r, plastic), 0.22f, false);
                        break;
                    default: // pile of reeds / brush
                        mb.Ico(p + Vector3.up * 0.15f, new Vector3(1.0f, 0.3f, 0.7f), H("7A6A45"), 0, 0.3f, r, H("6F7F45"));
                        break;
                }
            }
        }

        static void HouseJams(ChunkSet cs, System.Random r)
        {
            foreach (var h in houses)
            {
                if (!h.flooded || r.NextDouble() > 0.3) continue;
                float wl = Valley.WaterLevel(h.pos.z);
                Vector3 up = h.pos + new Vector3(0f, 0f, h.Radius * 0.75f);
                up.y = wl + 0.05f;
                var mb = cs.At(up);
                for (int k = 0; k < r.Next(2, 5); k++)
                {
                    Vector3 c = up + new Vector3(Rand(r, -3f, 3f), Rand(r, -0.05f, 0.1f), Rand(r, 0f, 1.2f));
                    Vector3 d = Quaternion.Euler(0f, Rand(r, 60f, 120f), 0f) * Vector3.forward * Rand(r, 1.2f, 2.8f);
                    mb.Rod(c - d, c + d, Rand(r, 0.08f, 0.2f), 5, r.NextDouble() < 0.5 ? H("6A4B33") : H("A78B63"));
                }
                if (r.NextDouble() < 0.5) mb.Ico(up + new Vector3(Rand(r, -2f, 2f), 0.2f, 0.6f), new Vector3(0.9f, 0.35f, 0.6f), H("5F7F3E"), 0, 0.3f, r, H("7A6040"));
            }
        }
    }
}
