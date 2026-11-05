using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public static partial class SVBuild
    {
        static void BuildLandmarks(ChunkSet structures, ChunkSet details)
        {
            var r = new System.Random(555);
            Bridge(structures, details, r);
            Plateau(structures, details, r);
            Ramp(structures, r);
            Mosque(structures, r);
            School(structures, details, r);
        }

        // ------------------------------------------------------------------ stone arch bridge

        static void Bridge(ChunkSet cs, ChunkSet details, System.Random r)
        {
            float z0 = Valley.BridgeZ;
            float wl = Valley.WaterLevel(z0);
            float deckY = wl + 6.6f;
            float cx = Valley.CenterX(z0), W = Valley.HalfWidth(z0);
            float xa = cx - W - 16f, xb = cx + W + 16f;
            const float halfW = 4.2f;
            var mb = cs.At(cx, z0);
            Color32 stone = H("B3AC9C"), stoneD = H("958E80"), ring = H("C9C2B2");

            float[] piers = BridgePierXs();
            var supports = new List<float> { cx - W - 2f };
            supports.AddRange(piers);
            supports.Add(cx + W + 2f);

            // piers with cutwaters
            foreach (float px in piers)
            {
                float floor = Valley.TerrainHeight(px, z0) - 1f;
                mb.BoxB(px, floor, z0, 3.4f, deckY - 0.6f - floor, halfW * 2f + 0.4f, stoneD, 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(px - 1.7f, floor, z0 + s * (halfW + 0.2f)), c = new Vector3(px + 1.7f, floor, z0 + s * (halfW + 0.2f));
                    Vector3 tip = new Vector3(px, floor, z0 + s * (halfW + 2.8f));
                    float topY = wl + 2.2f;
                    mb.Quad(a, a + Vector3.up * (topY - floor), tip + Vector3.up * (topY - floor), tip, stoneD, new Vector3(-1f, 0f, s));
                    mb.Quad(c, tip, tip + Vector3.up * (topY - floor), c + Vector3.up * (topY - floor), stoneD, new Vector3(1f, 0f, s));
                    mb.Tri(a + Vector3.up * (topY - floor), tip + Vector3.up * (topY - floor) + Vector3.up * 0f, c + Vector3.up * (topY - floor), stone, Vector3.up);
                }
            }

            // spans: spandrel walls + arch soffit + voussoir ring
            for (int k = 0; k < supports.Count - 1; k++)
            {
                float x0 = supports[k] + (k == 0 ? 0f : 1.7f), x1 = supports[k + 1] - (k == supports.Count - 2 ? 0f : 1.7f);
                float mid = (x0 + x1) * 0.5f, half = (x1 - x0) * 0.5f;
                float spring = wl - 0.5f, rise = Mathf.Min(half * 0.95f, deckY - 1.3f - spring);
                const int N = 14;
                for (int i = 0; i < N; i++)
                {
                    float xa0 = Mathf.Lerp(x0, x1, (float)i / N), xa1 = Mathf.Lerp(x0, x1, (float)(i + 1) / N);
                    float ya0 = spring + rise * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((xa0 - mid) / half, 2f)));
                    float ya1 = spring + rise * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((xa1 - mid) / half, 2f)));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float zf = z0 + s * halfW;
                        mb.Quad(new Vector3(xa0, ya0, zf), new Vector3(xa0, deckY - 0.5f, zf), new Vector3(xa1, deckY - 0.5f, zf), new Vector3(xa1, ya1, zf), MB.Jitter(stone, r, 0.04f), new Vector3(0f, 0f, s));
                        // voussoir ring
                        Vector3 n0 = new Vector3(xa0 - mid, (ya0 - spring) * half / Mathf.Max(rise, 0.1f), 0f).normalized;
                        Vector3 n1 = new Vector3(xa1 - mid, (ya1 - spring) * half / Mathf.Max(rise, 0.1f), 0f).normalized;
                        Vector3 v0 = new Vector3(xa0, ya0, zf + s * 0.06f), v1 = new Vector3(xa1, ya1, zf + s * 0.06f);
                        mb.Quad(v0, v0 + n0 * 0.7f, v1 + n1 * 0.7f, v1, (i & 1) == 0 ? ring : MB.Shade(ring, 0.93f), new Vector3(0f, 0f, s));
                    }
                    mb.Quad(new Vector3(xa0, ya0, z0 - halfW), new Vector3(xa1, ya1, z0 - halfW), new Vector3(xa1, ya1, z0 + halfW), new Vector3(xa0, ya0, z0 + halfW), stoneD, Vector3.down);
                }
            }

            // abutments into both banks
            foreach (var ab in new[] { new Vector2(xa, supports[0]), new Vector2(supports[supports.Count - 1], xb) })
                mb.BoxB((ab.x + ab.y) * 0.5f, wl - 1.5f, z0, ab.y - ab.x, deckY - 0.6f - (wl - 1.5f), halfW * 2f + 0.4f, stoneD, 0);

            // deck, road and parapets with lamps
            mb.BoxB((xa + xb) * 0.5f, deckY - 0.6f, z0, xb - xa, 0.6f, halfW * 2f + 0.3f, stone, MB.NY);
            mb.Quad(new Vector3(xa, deckY + 0.02f, z0 - halfW + 0.6f), new Vector3(xa, deckY + 0.02f, z0 + halfW - 0.6f), new Vector3(xb, deckY + 0.02f, z0 + halfW - 0.6f), new Vector3(xb, deckY + 0.02f, z0 - halfW + 0.6f), H("6E6C66"), Vector3.up);
            for (float x = xa; x < xb - 0.1f; x += 3f)
                mb.Quad(new Vector3(x, deckY + 0.04f, z0 - 0.08f), new Vector3(x, deckY + 0.04f, z0 + 0.08f), new Vector3(x + 1.5f, deckY + 0.04f, z0 + 0.08f), new Vector3(x + 1.5f, deckY + 0.04f, z0 - 0.08f), H("E2DFD5"), Vector3.up);
            for (int s = -1; s <= 1; s += 2)
            {
                for (float x = xa; x < xb - 0.1f; x += 2.5f)
                {
                    float len = Mathf.Min(2.5f, xb - x);
                    mb.BoxB(x + len * 0.5f, deckY, z0 + s * (halfW - 0.25f), len, 1.05f, 0.5f, MB.Jitter(stone, r, 0.05f), 0);
                    mb.BoxB(x + len * 0.5f, deckY + 1.05f, z0 + s * (halfW - 0.25f), len + 0.02f, 0.14f, 0.66f, ring, 0);
                }
                // sidewalk curb
                mb.BoxB((xa + xb) * 0.5f, deckY, z0 + s * (halfW - 0.85f), xb - xa, 0.18f, 0.7f, H("A7A397"), 0);
                for (float x = xa + 8f; x < xb - 4f; x += 22f) Lamp(details.At(x, z0), new Vector3(x, deckY + 1.19f, z0 + s * (halfW - 0.25f)), s > 0 ? 180f : 0f, 4.2f);
            }

            // debris jammed against the piers (upstream side)
            foreach (float px in piers)
            {
                var jr = new System.Random(r.Next());
                for (int i = 0; i < 6; i++)
                {
                    Vector3 c = new Vector3(px + Rand(jr, -3.2f, 3.2f), wl + 0.05f, z0 + halfW + 2.6f + Rand(jr, 0f, 1.8f));
                    float yaw = Rand(jr, -30f, 30f) * Mathf.Deg2Rad;
                    float len = Rand(jr, 2.5f, 5.5f);
                    Vector3 d = new Vector3(Mathf.Cos(yaw), Rand(jr, -0.1f, 0.15f), Mathf.Sin(yaw)) * len * 0.5f;
                    mb.Rod(c - d, c + d, Rand(jr, 0.14f, 0.26f), 6, H("6A4B33"));
                }
                for (int i = 0; i < 3; i++)
                    mb.Ico(new Vector3(px + Rand(jr, -2.5f, 2.5f), wl + 0.25f, z0 + halfW + 3.2f), new Vector3(0.9f, 0.45f, 0.8f), H("5F7F3E"), 0, 0.25f, jr, H("7A6040"));
            }

            // people watching from the bridge: small groups at the downstream parapet, irregular spacing
            float gx = cx - W * 0.7f + Rand(r, 0f, 4f);
            while (gx < cx + W * 0.65f)
            {
                int g = nextGroup++;
                int n = r.Next(2, 5);
                float sz = (g & 1) == 0 ? 1f : -1f;             // +1 upstream parapet, -1 downstream
                for (int i = 0; i < n; i++)
                {
                    float x = gx + i * Rand(r, 0.7f, 1.1f);
                    float fz = z0 + sz * (halfW - 0.5f);        // parapet face
                    personSpots.Add(new PersonSpot
                    {
                        pos = new Vector3(x, deckY + 0.18f, fz - sz * (0.30f + Rand(r, 0f, 0.35f))), yaw = (sz > 0f ? 0f : 180f) + Rand(r, -15f, 15f), hint = SelVadisi.Beh.Stand,
                        kind = 0, loc = SpotLoc.Bridge, group = g,
                        hasRail = true, bridge = true, railA = new Vector3(x - 0.5f, deckY + 0.18f, fz), railB = new Vector3(x + 0.5f, deckY + 0.18f, fz),
                        railN = sz * Vector3.forward, railTop = deckY + 1.19f,
                        help = new Vector3(Valley.PadEdgeX, Valley.PadY, (Valley.PadZ0 + Valley.PadZ1) * 0.5f),
                        water = new Vector3(x + Rand(r, -8f, 8f), wl, z0 + sz * (halfW + Rand(r, 6f, 30f)))
                    });
                }
                gx += n * 1.0f + Rand(r, 6f, 18f);
            }
        }

        static void Lamp(MB b, Vector3 basePos, float yaw, float height)
        {
            Color32 metal = H("3F4548");
            b.Cyl(basePos, 0.16f, 0.5f, 6, metal);
            b.Rod(basePos, basePos + Vector3.up * height, 0.07f, 6, metal, 0.05f);
            Vector3 top = basePos + Vector3.up * height;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            b.Rod(top, top + dir * 1.1f + Vector3.up * 0.15f, 0.04f, 5, metal);
            b.Box(top + dir * 1.15f + Vector3.up * 0.05f, new Vector3(0.45f, 0.18f, 0.45f), metal, 0);
            b.Emit = 0.35f;
            b.Box(top + dir * 1.15f - Vector3.up * 0.06f, new Vector3(0.36f, 0.06f, 0.36f), H("FFF1C8"), 0);
            b.Emit = 0f;
        }

        // ------------------------------------------------------------------ parking plateau

        static void Plateau(ChunkSet cs, ChunkSet details, System.Random r)
        {
            float x0 = Valley.PlateauX0, x1 = Valley.PlateauX1, z0 = Valley.PlateauZ0, z1 = Valley.PlateauZ1, y = Valley.PlateauY;
            var mb = cs.At((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            Color32 asphalt = H("5B5F61"), stripe = H("ECEAE2"), wallC = H("A39C8E");
            mb.BoxB((x0 + x1) * 0.5f, y - 0.4f, (z0 + z1) * 0.5f, x1 - x0, 0.48f, z1 - z0, asphalt, MB.NY);
            // parking bays on both long sides
            for (float z = z0 + 6f; z <= z1 - 6f; z += 2.7f)
            {
                foreach (float bx in new[] { x0 + 1.5f, x1 - 7.5f })
                    mb.Quad(new Vector3(bx, y + 0.1f, z - 0.06f), new Vector3(bx, y + 0.1f, z + 0.06f), new Vector3(bx + 6f, y + 0.1f, z + 0.06f), new Vector3(bx + 6f, y + 0.1f, z - 0.06f), stripe, Vector3.up);
            }
            for (float z = z0 + 4f; z < z1 - 4f; z += 4f)
                mb.Quad(new Vector3((x0 + x1) * 0.5f - 0.08f, y + 0.1f, z), new Vector3((x0 + x1) * 0.5f - 0.08f, y + 0.1f, z + 2f), new Vector3((x0 + x1) * 0.5f + 0.08f, y + 0.1f, z + 2f), new Vector3((x0 + x1) * 0.5f + 0.08f, y + 0.1f, z), H("E8C64A"), Vector3.up);

            // retaining walls along both long sides, following the height difference
            for (int s = 0; s < 2; s++)
            {
                float wx = s == 0 ? x1 : x0;
                float outX = s == 0 ? wx + 1.5f : wx - 1.5f;
                for (float z = z0 - 16f; z < z1 + 16f; z += 2f)
                {
                    float inside = Valley.TerrainHeight(s == 0 ? wx - 1f : wx + 1f, z + 1f);
                    float outside = Valley.TerrainHeight(outX, z + 1f);
                    float lo = Mathf.Min(inside, outside) - 0.8f, hi = Mathf.Max(inside, outside) + (inside >= outside - 0.2f ? 0.9f : 0.3f);
                    if (hi - lo < 1.8f) continue;
                    if (s == 0 && Mathf.Abs(z + 1f - Valley.RampTop.z) < 6.5f) continue; // ramp entrance
                    mb.BoxB(wx, lo, z + 1f, 0.9f, hi - lo, 2.02f, MB.Jitter(wallC, r, 0.06f), 0);
                    // stone courses
                    for (float yy = lo + 0.5f; yy < hi - 0.3f; yy += 0.55f)
                        mb.Box(new Vector3(wx + (s == 0 ? 0.47f : -0.47f), yy, z + 1f + Rand(r, -0.4f, 0.4f)), new Vector3(0.04f, 0.06f, 1.4f), MB.Shade(wallC, 0.82f), 0);
                    if (s == 0 && inside >= outside && z > z0 - 2f && z < z1 + 2f && Mathf.Abs(z + 1f - Valley.RampTop.z) > 5.5f)
                    {
                        // guard rail on top of the river-side wall
                        mb.Box(new Vector3(wx, hi + 0.75f, z + 1f), new Vector3(0.08f, 0.22f, 2.02f), H("C7CBCC"), 0);
                        mb.Box(new Vector3(wx, hi + 0.42f, z + 1f), new Vector3(0.1f, 0.84f, 0.1f), H("8A8F92"), 0);
                    }
                }
            }
            // lamps, benches, bins, kiosk
            var dm = details.At((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            for (float z = z0 + 8f; z < z1; z += 22f) Lamp(dm, new Vector3((x0 + x1) * 0.5f, y + 0.08f, z), 90f, 6f);
            for (int i = 0; i < 3; i++)
            {
                float z = z0 + 14f + i * 22f;
                Bench(dm, new Vector3(x1 - 2f, y + 0.08f, z), -90f);
                dm.Cyl(new Vector3(x1 - 2f, y + 0.08f, z + 2.4f), 0.28f, 0.85f, 8, H("2E6B3E"), 0.3f);
            }
            dm.BoxB(x0 + 4f, y + 0.08f, z1 - 7f, 3.2f, 2.6f, 2.6f, H("D9C9A3"));
            dm.BoxB(x0 + 4f, y + 2.68f, z1 - 7f, 3.8f, 0.18f, 3.2f, H("C24A3A"), 0);
            Awning(dm, x0 + 4f, y + 2.4f, z1 - 8.3f, 3.4f, H("C24A3A"));

            // emergency tents for evacuated families (assembly area)
            float midX = (x0 + x1) * 0.5f;
            int tentIdx = 0;
            for (float tz = z0 + 16f; tz < z1 - 10f; tz += 13f)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 tp = new Vector3(midX + s * 7.5f, y + 0.08f, tz + (s > 0 ? 5f : 0f));
                    EvacTent(dm, tp, s > 0 ? 90f : -90f, (tentIdx++ % 3) == 0 ? H("2E6DB4") : H("E9ECEE"));
                    if (r.NextDouble() < 0.7)
                    {
                        // a tent family of 2-3 in front of the tent
                        int g = nextGroup++;
                        int n = r.Next(2, 4);
                        Vector3 front = tp + new Vector3(-s * 2.6f, 0f, Rand(r, -0.6f, 0.6f));
                        for (int i = 0; i < n; i++)
                            personSpots.Add(new PersonSpot
                            {
                                pos = front + new Vector3(Rand(r, -0.4f, 0.4f), 0f, (i - (n - 1) * 0.5f) * Rand(r, 0.8f, 1.1f)),
                                yaw = s > 0 ? -90f : 90f, hint = SelVadisi.Beh.Stand,
                                kind = 0, loc = SpotLoc.Tent, group = g,
                                help = new Vector3(Valley.PlateauX1, y, tp.z), water = new Vector3(Valley.CenterX(tp.z), Valley.WaterLevel(tp.z), tp.z + Rand(r, -20f, 20f))
                            });
                    }
                }
            // water and blanket distribution table
            dm.BoxB(midX, y + 0.8f, z1 - 14f, 2.6f, 0.06f, 0.9f, H("E9E6DE"), 0);
            for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2)
                dm.Rod(new Vector3(midX + i * 1.2f, y + 0.08f, z1 - 14f + j * 0.4f), new Vector3(midX + i * 1.2f, y + 0.8f, z1 - 14f + j * 0.4f), 0.03f, 4, H("777B7E"));
            for (int k = 0; k < 5; k++) dm.BoxB(midX - 1f + k * 0.5f, y + 0.86f, z1 - 14f, 0.4f, 0.28f, 0.3f, k % 2 == 0 ? H("A9CDEB") : H("8E7B6A"));

            // evacuees along the plateau rail watching the flood (clusters), and on the benches
            for (int cl = 0; cl < 4; cl++)
            {
                float z = Mathf.Lerp(z0 + 8f, z1 - 8f, (cl + Rand(r, 0.1f, 0.9f)) / 4f);
                if (Mathf.Abs(z - Valley.RampTop.z) < 7f) z += 9f;
                int g = nextGroup++;
                int n = r.Next(2, 5);
                for (int i = 0; i < n; i++)
                {
                    float zz = z + (i - (n - 1) * 0.5f) * Rand(r, 0.7f, 1.1f);
                    personSpots.Add(new PersonSpot
                    {
                        pos = new Vector3(x1 - Rand(r, 1.2f, 2.2f), y + 0.08f, zz), yaw = 90f + Rand(r, -25f, 25f), hint = SelVadisi.Beh.Stand,
                        kind = 0, loc = SpotLoc.Plateau, group = g,
                        help = new Vector3(x1 + 20f, y, zz), water = new Vector3(Valley.CenterX(zz), Valley.WaterLevel(zz), zz + Rand(r, -25f, 25f))
                    });
                }
            }
            for (int i = 0; i < 3; i++)
            {
                float z = z0 + 14f + i * 22f;
                int g = nextGroup++;
                int n = r.Next(1, 3);
                for (int k = 0; k < n; k++)
                {
                    Vector3 seat = new Vector3(x1 - 2f, y + 0.08f, z + (k == 0 ? -0.4f : 0.4f));
                    personSpots.Add(new PersonSpot
                    {
                        pos = seat, yaw = 90f, hint = SelVadisi.Beh.Sit, kind = 0, loc = SpotLoc.Plateau, group = n > 1 ? g : -1,
                        hasSeat = true, seat = seat, seatFwd = Vector3.right, seatH = 0.48f,
                        help = new Vector3(x1 + 20f, y, z), water = new Vector3(Valley.CenterX(z), Valley.WaterLevel(z), z + Rand(r, -20f, 20f))
                    });
                }
            }
        }

        static void EvacTent(MB b, Vector3 p, float yaw, Color32 roof)
        {
            b.Push(p, yaw);
            const float w = 3.4f, len = 4.4f, wallH = 1.3f, ridge = 2.6f;
            Color32 wall = H("F1F2F0"), dark = H("4A5A6A");
            b.Box(new Vector3(0f, wallH * 0.5f, 0f), new Vector3(w, wallH, len), wall, MB.NY | MB.PY);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = new Vector3(s * (w * 0.5f + 0.15f), wallH - 0.08f, -len * 0.5f - 0.15f), c = new Vector3(s * (w * 0.5f + 0.15f), wallH - 0.08f, len * 0.5f + 0.15f);
                Vector3 ra = new Vector3(0f, ridge, -len * 0.5f - 0.15f), rc = new Vector3(0f, ridge, len * 0.5f + 0.15f);
                b.Quad(a, c, rc, ra, roof, new Vector3(s, 1.4f, 0f));
                b.Quad(a, c, rc, ra, MB.Shade(roof, 0.75f), new Vector3(-s, -1.4f, 0f));
                b.Tri(new Vector3(-w * 0.5f, wallH, s * len * 0.5f), new Vector3(w * 0.5f, wallH, s * len * 0.5f), new Vector3(0f, ridge - 0.05f, s * len * 0.5f), wall, new Vector3(0f, 0f, s));
            }
            b.Box(new Vector3(0f, 0.85f, -len * 0.5f - 0.02f), new Vector3(1.0f, 1.6f, 0.03f), dark, MB.PZ);
            for (int s = -1; s <= 1; s += 2) b.Rod(new Vector3(s * 0.4f, 0f, -len * 0.5f - 1.2f), new Vector3(0f, ridge, -len * 0.5f - 0.15f), 0.012f, 3, H("D7D9DA"), -1f, false);
            b.Pop();
        }

        static void Bench(MB b, Vector3 p, float yaw)
        {
            b.Push(p, yaw);
            Color32 wood = H("9C6B42"), iron = H("3B3F42");
            b.Box(new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.06f, 0.45f), wood, 0);
            b.Box(new Vector3(0f, 0.8f, 0.2f), new Vector3(1.6f, 0.3f, 0.05f), wood, 0);
            for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(s * 0.7f, 0.22f, 0f), new Vector3(0.06f, 0.45f, 0.45f), iron, 0);
            b.Pop();
        }

        // ------------------------------------------------------------------ cobblestone ramp into the flood

        static void Ramp(ChunkSet cs, System.Random r)
        {
            Vector3 a = Valley.RampTop, c = Valley.RampBottom;
            float L = Valley.RampLength;
            var mb = cs.At((a + c) * 0.5f);
            Vector3 flat = new Vector3(c.x - a.x, 0f, c.z - a.z).normalized;
            Vector3 perp = new Vector3(-flat.z, 0f, flat.x);
            Color32[] cobble = { H("8F8B84"), H("9C978E"), H("7F7B74"), H("A49E92"), H("8A8279") };
            Color32 wallC = H("A39C8E");
            const float seg = 2f;
            float hw = Valley.RampHalfWidth;
            for (float s = 0f; s < L - 0.01f; s += seg)
            {
                float s1 = Mathf.Min(L, s + seg);
                Vector3 p0 = Vector3.Lerp(a, c, s / L), p1 = Vector3.Lerp(a, c, s1 / L);
                Vector3 mid = (p0 + p1) * 0.5f;
                float len = Vector3.Distance(p0, p1);
                Quaternion rot = Quaternion.LookRotation(p1 - p0, Vector3.up);
                float ground = Mathf.Min(Valley.BaseHeight(mid.x, mid.z), Valley.TerrainHeight(mid.x, mid.z));
                for (int side = -1; side <= 1; side += 2)
                    ground = Mathf.Min(ground, Valley.BaseHeight(mid.x + perp.x * hw * side, mid.z + perp.z * hw * side));
                float depth = mid.y - (ground - 1.2f);
                mb.Push(mid, rot);
                // solid body
                mb.Box(new Vector3(0f, -depth * 0.5f - 0.1f, 0f), new Vector3(hw * 2f, depth, len + 0.04f), MB.Jitter(wallC, r, 0.04f), MB.PY);
                // cobbles
                for (float u = -hw + 0.27f; u < hw - 0.2f; u += 0.52f)
                    for (float v = -len * 0.5f + 0.26f; v < len * 0.5f; v += 0.52f)
                        mb.Box(new Vector3(u + Rand(r, -0.03f, 0.03f), -0.05f, v), new Vector3(0.47f, 0.12f + Rand(r, 0f, 0.05f), 0.47f), cobble[r.Next(cobble.Length)], MB.NY);
                // parapets
                for (int side = -1; side <= 1; side += 2)
                {
                    mb.Box(new Vector3(side * (hw + 0.25f), 0.35f, 0f), new Vector3(0.5f, 0.9f, len + 0.02f), MB.Jitter(wallC, r, 0.06f), MB.NY);
                    mb.Box(new Vector3(side * (hw + 0.25f), 0.84f, 0f), new Vector3(0.62f, 0.1f, len + 0.04f), H("C3BCAD"), MB.NY);
                }
                mb.Pop();
            }
        }

        // ------------------------------------------------------------------ mosque with minaret

        static void Mosque(ChunkSet cs, System.Random r)
        {
            Vector3 p = mosquePos;
            float yaw = 90f; // front (local -z) faces the river (west)
            var mb = cs.At(p);
            mb.Push(p, yaw);
            Color32 wall = H("EDE7DA"), lead = H("7F878D"), stone = H("BDB5A3"), gold = H("D4B048");
            mb.BoxB(0f, -3f, 0f, 17f, 3.6f, 17f, stone);
            mb.BoxB(0f, 0.6f, 0f, 15f, 8f, 15f, wall);
            mb.BoxB(0f, 8.6f, 0f, 15.6f, 0.5f, 15.6f, Trim, 0);
            // octagonal drum + dome + finial
            mb.Cyl(new Vector3(0f, 9.1f, 0f), 6.2f, 2.2f, 16, wall, -1f, false);
            mb.Cyl(new Vector3(0f, 11.3f, 0f), 6.5f, 0.3f, 16, Trim);
            const int ring = 8;
            for (int k = 0; k < ring; k++)
            {
                float t0 = (float)k / ring, t1 = (float)(k + 1) / ring;
                float r0 = 6.3f * Mathf.Cos(t0 * Mathf.PI * 0.5f), r1 = 6.3f * Mathf.Cos(t1 * Mathf.PI * 0.5f);
                float y0 = 11.6f + 5.6f * Mathf.Sin(t0 * Mathf.PI * 0.5f), y1 = 11.6f + 5.6f * Mathf.Sin(t1 * Mathf.PI * 0.5f);
                mb.Cyl(new Vector3(0f, y0, 0f), r0, y1 - y0, 16, MB.Shade(lead, 1f + k * 0.015f), r1, k == ring - 1);
            }
            mb.Rod(new Vector3(0f, 17.1f, 0f), new Vector3(0f, 18.8f, 0f), 0.08f, 6, gold);
            mb.Ico(new Vector3(0f, 18.2f, 0f), new Vector3(0.22f, 0.22f, 0.22f), gold, 0, 0f, r);
            // arched windows
            for (int side = 0; side < 4; side++)
            {
                mb.Push(Vector3.zero, side * 90f);
                for (int k = -2; k <= 2; k++)
                {
                    if (side == 0 && k == 0) continue;
                    mb.Gloss = 0.8f;
                    mb.Box(new Vector3(k * 2.6f, 4.2f, -7.55f), new Vector3(1.1f, 2.6f, 0.08f), H("2F4858"), MB.PZ);
                    mb.Gloss = 0f;
                    mb.Box(new Vector3(k * 2.6f, 4.2f, -7.52f), new Vector3(1.4f, 2.9f, 0.05f), Trim, MB.PZ);
                    mb.Push(new Vector3(k * 2.6f, 5.5f, -7.5f), Quaternion.Euler(-90f, 0f, 0f));
                    mb.Cyl(Vector3.zero, 0.7f, 0.04f, 10, Trim);
                    mb.Gloss = 0.8f;
                    mb.Cyl(new Vector3(0f, 0.04f, 0f), 0.55f, 0.06f, 10, H("2F4858"));
                    mb.Gloss = 0f;
                    mb.Pop();
                }
                mb.Pop();
            }
            // portico with columns
            mb.BoxB(0f, 0.6f, -10f, 15f, 0.25f, 5f, stone, 0);
            for (int k = -3; k <= 3; k += 2)
                mb.Cyl(new Vector3(k * 2.2f, 0.85f, -12f), 0.32f, 4.4f, 8, wall);
            mb.BoxB(0f, 5.2f, -10f, 15.4f, 0.7f, 5.4f, wall, 0);
            mb.BoxB(0f, 5.9f, -10f, 15.6f, 0.2f, 5.6f, Trim, 0);
            for (int k = -2; k <= 2; k++)
                mb.Cyl(new Vector3(k * 3f, 6.1f, -10f), 1.2f, 0.9f, 10, lead, 0.15f, true);
            mb.Box(new Vector3(0f, 2.6f, -7.55f), new Vector3(2f, 4f, 0.08f), H("6B4630"), MB.PZ);
            // minaret
            Vector3 m = new Vector3(8.6f, 0f, -8.6f);
            mb.BoxB(m.x, -1f, m.z, 3.4f, 9.6f, 3.4f, stone);
            mb.Cyl(m + Vector3.up * 8.6f, 1.45f, 1.2f, 12, wall, 1.25f);
            mb.Cyl(m + Vector3.up * 9.8f, 1.2f, 13f, 12, wall, 1.1f, false);
            mb.Cyl(m + Vector3.up * 22.8f, 2.0f, 0.35f, 12, Trim, -1f, true, true);
            for (int k = 0; k < 16; k++)
            {
                float ang = k * Mathf.PI * 2f / 16f;
                mb.Box(m + new Vector3(Mathf.Cos(ang) * 1.9f, 23.6f, Mathf.Sin(ang) * 1.9f), new Vector3(0.12f, 1.0f, 0.12f), Trim, 0);
            }
            mb.Cyl(m + Vector3.up * 24.1f, 2.0f, 0.18f, 12, Trim);
            mb.Cyl(m + Vector3.up * 23.15f, 1.0f, 4.0f, 12, wall, 0.95f, false);
            mb.Cyl(m + Vector3.up * 27.15f, 1.3f, 0.3f, 12, Trim);
            mb.Cone(m + Vector3.up * 27.45f, 1.15f, 6.5f, 12, lead);
            mb.Rod(m + Vector3.up * 33.6f, m + Vector3.up * 35.2f, 0.06f, 6, gold);
            mb.Ico(m + Vector3.up * 34.5f, new Vector3(0.2f, 0.2f, 0.2f), gold, 0, 0f, r);
            // courtyard wall and trees
            mb.Pop();
        }

        // ------------------------------------------------------------------ school with flag

        static void School(ChunkSet cs, ChunkSet details, System.Random r)
        {
            Vector3 p = schoolPos;
            float yaw = -90f; // front faces the river (east)
            var mb = cs.At(p);
            mb.Push(p, yaw);
            Color32 wall = H("EBD49A"), band = H("F4F0E6"), roof = H("8E9496"), stone = H("A8A090");
            float w = 32f, d = 12f, top = 0.8f + 2f * 3.4f;
            mb.BoxB(0f, -3f, 0f, w + 0.4f, 3.8f, d + 0.4f, stone);
            mb.BoxB(0f, 0.8f, 0f, w, top - 0.8f, d, wall);
            mb.BoxB(0f, 0.8f + 3.4f - 0.12f, 0f, w + 0.15f, 0.25f, d + 0.15f, band, 0);
            mb.BoxB(0f, top, 0f, w + 0.3f, 0.35f, d + 0.3f, roof, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.BoxB(0f, top + 0.35f, s * (d * 0.5f), w + 0.3f, 0.7f, 0.3f, wall, 0);
                mb.BoxB(s * (w * 0.5f), top + 0.35f, 0f, 0.3f, 0.7f, d, wall, 0);
            }
            for (int f = 0; f < 2; f++)
                for (float x = -w * 0.5f + 1.6f; x < w * 0.5f - 1f; x += 2.4f)
                {
                    if (f == 0 && Mathf.Abs(x) < 2.5f) continue;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        mb.Push(new Vector3(s < 0 ? x : -x, 0.8f + f * 3.4f + 1.8f, s * d * 0.5f), s < 0 ? 0f : 180f);
                        mb.Box(new Vector3(0f, 0f, -0.04f), new Vector3(1.7f, 1.8f, 0.08f), band, MB.PZ);
                        mb.Gloss = 0.85f;
                        mb.Quad(new Vector3(-0.72f, -0.78f, -0.085f), new Vector3(-0.72f, 0.78f, -0.085f), new Vector3(0.72f, 0.78f, -0.085f), new Vector3(0.72f, -0.78f, -0.085f), Glass, Vector3.back);
                        mb.Gloss = 0f;
                        mb.Box(new Vector3(0f, 0f, -0.1f), new Vector3(0.06f, 1.56f, 0.03f), band, MB.PZ);
                        mb.Pop();
                    }
                }
            // entrance canopy + doors + steps
            mb.BoxB(0f, 0.8f + 3.0f, -d * 0.5f - 1.6f, 6f, 0.25f, 3.2f, band, 0);
            for (int s = -1; s <= 1; s += 2) mb.Cyl(new Vector3(s * 2.6f, 0.8f, -d * 0.5f - 2.9f), 0.18f, 3.0f, 8, band);
            mb.Gloss = 0.7f;
            mb.Box(new Vector3(0f, 0.8f + 1.3f, -d * 0.5f - 0.05f), new Vector3(3f, 2.6f, 0.08f), H("3D5B6B"), MB.PZ);
            mb.Gloss = 0f;
            mb.BoxB(0f, -0.4f, -d * 0.5f - 1.8f, 6f, 1.2f, 3.6f, stone);
            mb.Pop();

            // yard: fence wall, basketball hoop, flag pole
            var dm = details.At(p);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 hoop = p + q * new Vector3(10f, 0f, -14f);
            hoop.y = Valley.TerrainHeight(hoop.x, hoop.z);
            dm.Rod(hoop, hoop + Vector3.up * 3.6f, 0.08f, 6, H("3B6EA8"));
            dm.BoxRot(hoop + Vector3.up * 3.3f + q * new Vector3(0f, 0f, -0.15f), new Vector3(1.8f, 1.1f, 0.06f), yaw, H("F2F2F0"));
            Vector3 flagBase = p + q * new Vector3(-8f, 0f, -13f);
            flagBase.y = Valley.TerrainHeight(flagBase.x, flagBase.z);
            dm.Cyl(flagBase, 0.5f, 0.4f, 8, stone);
            dm.Rod(flagBase, flagBase + Vector3.up * 10f, 0.07f, 6, H("D6D9DB"), 0.05f);
            dm.Ico(flagBase + Vector3.up * 10.1f, new Vector3(0.12f, 0.12f, 0.12f), H("D4B048"), 0, 0f, r);
            clothSpots.Add(new ClothSpot { pos = flagBase + Vector3.up * 9.9f + Vector3.right * 0.07f, yaw = 0f, flag = true, w = 2.7f, h = 1.8f, color = H("E30A17") });
            int schoolGroup = nextGroup++;
            for (int i = 0; i < 4; i++)
            {
                Vector3 sp = p + q * new Vector3(-2.2f + i * Rand(r, 0.8f, 1.2f), 0f, -d * 0.5f - Rand(r, 5f, 6f));
                sp.y = Valley.TerrainHeight(sp.x, sp.z);
                personSpots.Add(new PersonSpot { pos = sp, yaw = yaw + 180f + Rand(r, -30f, 30f), hint = SelVadisi.Beh.Stand, kind = i == 1 ? 2 : (i == 2 ? 3 : 0), loc = SpotLoc.Bank, group = schoolGroup, help = HelpSourceFor(sp), water = WaterNear(sp, r) });
            }
        }
    }
}
