using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public static partial class SVBuild
    {
        struct Opening
        {
            public float x0, x1, y0, y1, depth;
            public int kind; // 0 window, 1 door, 2 balcony door, 3 shop window, 4 small window
        }

        static readonly Color32 Wood = H("5A3D28"), Brass = H("C9A84A");
        static readonly Color32[] CurtainCols = { H("F2EEE4"), H("E9DCC2"), H("C9B6A0"), H("D99A8A"), H("9FB7CF"), H("E7D27E") };

        static void BuildHouses(ChunkSet structures, ChunkSet details)
        {
            foreach (var h in houses)
            {
                var r = new System.Random(h.seed);
                var pr = new System.Random(h.seed * 7 + 3);
                h.roofPeople = h.flooded && h.roof == 2 && pr.NextDouble() < 0.85;
                h.balconyPeople = !h.roofPeople && h.flooded && h.balconies && h.floors >= 2 && pr.NextDouble() < 0.45;
                h.bankPeople = !h.flooded && pr.NextDouble() < 0.14;
                var mb = structures.At(h.pos);
                int start = mb.Count;
                mb.Push(h.pos, h.yaw);
                House(mb, h, r);
                mb.Pop();
                // baked occlusion: darker toward the ground and under the eaves
                float baseY = h.pos.y, top = h.WallTop;
                mb.ShadeRange(start, w =>
                {
                    float ly = w.y - baseY;
                    float k = Mathf.Lerp(0.74f, 1f, Valley.S(0.3f, 2.8f, ly));
                    if (ly < top + 0.05f) k *= Mathf.Lerp(1f, 0.86f, Valley.S(top - 1.1f, top - 0.05f, ly));
                    return k;
                });

                var dm = details.At(h.pos);
                if (h.garden) GardenWall(dm, h, r);
                PlanHousePeople(h, r);
            }
        }

        // ------------------------------------------------------------------ house

        // local frame: x across width, z depth, front faces -z, origin at plinth base
        static void House(MB b, HouseSpec h, System.Random r)
        {
            float w = h.w, d = h.d, top = h.WallTop;
            Color32 plinth = MB.Shade(h.stone, 0.9f);

            // stone plinth with rusticated courses
            b.BoxB(0f, -3f, 0f, w + 0.36f, 3.8f, d + 0.36f, plinth);
            for (int k = 0; k < 2; k++)
            {
                float y = 0.25f + k * 0.28f;
                b.BoxB(0f, y, 0f, w + 0.38f, 0.025f, d + 0.38f, MB.Shade(plinth, 0.78f), 0);
            }
            b.BoxB(0f, 0.74f, 0f, w + 0.44f, 0.08f, d + 0.44f, MB.Shade(h.stone, 1.06f), 0);

            float doorX = h.shop ? w * 0.3f : (r.NextDouble() < 0.5 ? 0f : -w * 0.22f);
            float cumbaW = Mathf.Min(3.6f, w * 0.42f);

            // four facades, built floor by floor with real window openings
            for (int face = 0; face < 4; face++)
            {
                float len = face < 2 ? w : d;
                Vector3 fpos; float fyaw;
                switch (face)
                {
                    case 0: fpos = new Vector3(0f, 0f, -d * 0.5f); fyaw = 0f; break;
                    case 1: fpos = new Vector3(0f, 0f, d * 0.5f); fyaw = 180f; break;
                    case 2: fpos = new Vector3(w * 0.5f, 0f, 0f); fyaw = -90f; break;
                    default: fpos = new Vector3(-w * 0.5f, 0f, 0f); fyaw = 90f; break;
                }
                b.Push(fpos, fyaw);
                for (int f = 0; f < h.floors; f++)
                {
                    float yb = 0.8f + f * FH, yt = f == h.floors - 1 ? top : yb + FH;
                    var ops = new List<Opening>();
                    bool front = face == 0;
                    if (front && f == 0)
                    {
                        ops.Add(new Opening { x0 = doorX - 0.6f, x1 = doorX + 0.6f, y0 = yb, y1 = yb + 2.6f, depth = 0.22f, kind = 1 });
                        if (h.shop) ops.Add(new Opening { x0 = -w * 0.42f, x1 = -w * 0.42f + w * 0.5f, y0 = yb + 0.45f, y1 = yb + 2.45f, depth = 0.18f, kind = 3 });
                    }
                    int n = Mathf.Max(1, Mathf.FloorToInt(len / 3.2f));
                    for (int k = 0; k < n; k++)
                    {
                        float u = -len * 0.5f + (k + 0.5f) * len / n;
                        if (front && f == 0 && (Mathf.Abs(u - doorX) < 1.7f || (h.shop && u < -w * 0.42f + w * 0.5f + 0.7f))) continue;
                        if (front && f >= 1 && h.cumba && Mathf.Abs(u) < cumbaW * 0.5f + 0.75f) continue;
                        if (front && f >= 1 && h.balconies && Mathf.Abs(u) < 1.5f)
                            ops.Add(new Opening { x0 = u - 0.55f, x1 = u + 0.55f, y0 = yb + 0.05f, y1 = yb + 2.35f, depth = 0.2f, kind = 2 });
                        else
                        {
                            bool small = !front && f == 0 && face == 1 && r.NextDouble() < 0.3;
                            float ww = small ? 0.75f : 1.05f, wh = small ? 0.8f : 1.4f;
                            float y0 = small ? yb + 1.55f : yb + 0.95f;
                            ops.Add(new Opening { x0 = u - ww * 0.5f, x1 = u + ww * 0.5f, y0 = y0, y1 = y0 + wh, depth = 0.24f, kind = small ? 4 : 0 });
                        }
                    }
                    bool timberFloor = h.timber && f >= 1;
                    Color32 wallC = h.stoneHouse || (h.timber && f == 0) ? h.stone : h.wall;
                    Color32 reveal = MB.Shade(wallC, 0.86f);
                    Facade(b, len, yb, yt, ops, wallC, reveal);
                    foreach (var o in ops) DressOpening(b, h, o, r, f == 0);
                    if (timberFloor) Timber(b, len, yb, yt, ops);
                    if ((h.stoneHouse || (h.timber && f == 0)) && r.NextDouble() < 0.9) StoneFacing(b, len, yb, yt, ops, h.stone, r);
                }
                b.Pop();
            }

            // string courses between floors and a stepped cornice
            for (int f = 1; f < h.floors; f++)
            {
                Color32 band = h.timber ? Wood : Trim;
                b.BoxB(0f, 0.8f + f * FH - 0.1f, 0f, w + 0.14f, 0.2f, d + 0.14f, band, 0);
            }
            b.BoxB(0f, top - 0.26f, 0f, w + 0.12f, 0.12f, d + 0.12f, h.timber ? Wood : Trim, 0);
            b.BoxB(0f, top - 0.14f, 0f, w + 0.26f, 0.14f, d + 0.26f, h.timber ? Wood : Trim, 0);

            // corners: stone quoins, timber posts or plaster pilasters
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    if (h.quoins)
                    {
                        int k = 0;
                        for (float y = 0.85f; y < top - 0.35f; y += 0.42f, k++)
                        {
                            bool alt = (k & 1) == 0;
                            float a = alt ? 0.75f : 0.45f, c = alt ? 0.45f : 0.75f;
                            b.Box(new Vector3(cx * (w * 0.5f - a * 0.5f + 0.05f), y + 0.17f, cz * (d * 0.5f - c * 0.5f + 0.05f)), new Vector3(a, 0.34f, c), MB.Jitter(h.stone, r, 0.06f), 0);
                        }
                    }
                    else if (h.timber)
                        b.Box(new Vector3(cx * (w * 0.5f - 0.08f), (0.8f + FH + top) * 0.5f, cz * (d * 0.5f - 0.08f)), new Vector3(0.24f, top - 0.8f - FH, 0.24f), Wood, 0);
                    else
                        b.Box(new Vector3(cx * (w * 0.5f - 0.13f), (0.8f + top) * 0.5f, cz * (d * 0.5f - 0.13f)), new Vector3(0.34f, top - 0.8f, 0.34f), MB.Shade(h.wall, 1.07f), 0);
                }

            // entrance: steps, canopy with brackets, lamp, house number
            b.BoxB(doorX, -0.2f, -d * 0.5f - 0.75f, 2.0f, 0.75f, 1.5f, h.stone);
            b.BoxB(doorX, -0.2f, -d * 0.5f - 0.35f, 1.8f, 1.0f, 0.7f, MB.Shade(h.stone, 1.05f));
            b.BoxB(doorX, 0.8f + 2.85f, -d * 0.5f - 0.55f, 2.1f, 0.1f, 1.1f, h.timber ? Wood : Trim, 0);
            b.BoxB(doorX, 0.8f + 2.95f, -d * 0.5f - 0.5f, 2.0f, 0.12f, 1.0f, h.roofCol, 0);
            for (int s = -1; s <= 1; s += 2)
                b.Rod(new Vector3(doorX + s * 0.9f, 0.8f + 2.3f, -d * 0.5f), new Vector3(doorX + s * 0.9f, 0.8f + 2.84f, -d * 0.5f - 0.95f), 0.04f, 4, Iron);
            b.Box(new Vector3(doorX + 0.95f, 0.8f + 2.25f, -d * 0.5f - 0.1f), new Vector3(0.06f, 0.06f, 0.2f), Iron, 0);
            b.Emit = 0.9f;
            b.Box(new Vector3(doorX + 0.95f, 0.8f + 2.12f, -d * 0.5f - 0.2f), new Vector3(0.16f, 0.22f, 0.16f), H("FFE3A6"), 0);
            b.Emit = 0f;
            b.Box(new Vector3(doorX + 0.95f, 0.8f + 2.26f, -d * 0.5f - 0.2f), new Vector3(0.2f, 0.05f, 0.2f), Iron, 0);
            b.Box(new Vector3(doorX - 0.95f, 0.8f + 1.8f, -d * 0.5f - 0.015f), new Vector3(0.26f, 0.18f, 0.03f), H("F2F2EE"), MB.PZ);
            b.Box(new Vector3(doorX - 0.95f, 0.8f + 1.8f, -d * 0.5f - 0.03f), new Vector3(0.22f, 0.14f, 0.02f), H("1F4E9C"), MB.PZ);

            // shop sign + awnings
            if (h.shop)
            {
                b.Box(new Vector3(-w * 0.42f + w * 0.25f, 0.8f + 2.85f, -d * 0.5f - 0.08f), new Vector3(w * 0.55f, 0.5f, 0.12f), h.awningCol, MB.PZ);
                b.Box(new Vector3(-w * 0.42f + w * 0.25f, 0.8f + 2.85f, -d * 0.5f - 0.15f), new Vector3(w * 0.5f, 0.36f, 0.02f), H("F4F1EA"), MB.PZ);
            }
            if (h.awning) Awning(b, h.shop ? -w * 0.42f + w * 0.25f : doorX, 0.8f + 2.6f, -d * 0.5f, h.shop ? w * 0.6f : 2.6f, h.awningCol);

            // balconies, bay window, AC units, satellite dish, outer stairs
            for (int f = 1; f < h.floors; f++)
                if (h.balconies) Balcony(b, 0f, 0.8f + f * FH, -d * 0.5f, Mathf.Min(w * 0.62f, 5f), r, h.timber, h.balconyPeople && f == h.floors - 1);
            if (h.cumba) Cumba(b, h, cumbaW, r);
            for (int f = 1; f < h.floors; f++)
                if (r.NextDouble() < 0.35)
                {
                    float zz = Rand(r, -d * 0.3f, d * 0.3f);
                    float sx = r.NextDouble() < 0.5 ? -1f : 1f;
                    float y = 0.8f + f * FH + 2.55f;
                    b.Box(new Vector3(sx * (w * 0.5f + 0.17f), y, zz), new Vector3(0.32f, 0.55f, 0.85f), H("E9EAE6"), 0);
                    b.Push(new Vector3(sx * (w * 0.5f + 0.34f), y, zz + 0.12f), sx > 0 ? -90f : 90f);
                    b.Cyl(Vector3.zero, 0.2f, 0.01f, 10, H("9EA3A6"), -1f, true, false);
                    b.Pop();
                    b.Box(new Vector3(sx * (w * 0.5f + 0.2f), y - 0.33f, zz), new Vector3(0.38f, 0.04f, 0.9f), Iron, 0);
                    b.Rod(new Vector3(sx * (w * 0.5f + 0.05f), y - 0.25f, zz - 0.3f), new Vector3(sx * (w * 0.5f + 0.05f), y - 1.4f, zz - 0.3f), 0.015f, 4, H("D9D9D4"), -1f, false);
                }
            if (h.stairs) OuterStairs(b, h, r);

            // roof
            if (h.roof == 2) FlatRoofTop(b, h, r);
            else
            {
                bool alongX = w >= d;
                float rw = alongX ? w : d, rd = alongX ? d : w;
                if (!alongX) b.Push(Vector3.zero, 90f);
                float ridge = h.roof == 0 ? GableRoof(b, h, rw, rd, top, r) : HipRoof(b, h, rw, rd, top, r);
                if (!alongX) b.Pop();
                if (h.chimney) Chimney(b, h, Rand(r, -w * 0.3f, w * 0.3f), Rand(r, 0.5f, d * 0.22f), top, ridge, r);
                if (h.solar) SolarHeater(b, h, top, ridge, r);
            }
            if (r.NextDouble() < 0.3)
            {
                b.Push(new Vector3(w * 0.5f + 0.45f, top - 0.9f, -d * 0.25f), Quaternion.Euler(0f, 0f, -70f));
                b.Cyl(Vector3.zero, 0.38f, 0.06f, 10, H("E8E8E4"), 0.3f, true, true);
                b.Rod(new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.42f, 0f), 0.015f, 4, Iron);
                b.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.06f, 0.08f, 0.06f), H("3B3F42"), 0);
                b.Pop();
                b.Box(new Vector3(w * 0.5f + 0.15f, top - 0.9f, -d * 0.25f), new Vector3(0.3f, 0.05f, 0.05f), Iron, 0);
            }
        }

        // ------------------------------------------------------------------ facades with openings

        static List<float> Breaks(List<float> v, float lo, float hi)
        {
            v.Sort();
            var o = new List<float>();
            foreach (float f in v)
            {
                float c = Mathf.Clamp(f, lo, hi);
                if (o.Count == 0 || c - o[o.Count - 1] > 0.005f) o.Add(c);
            }
            return o;
        }

        /// Wall plane at z = 0 (outward -z) between yb and yt, x in [-len/2, len/2], with recessed openings.
        static void Facade(MB b, float len, float yb, float yt, List<Opening> ops, Color32 wall, Color32 reveal)
        {
            var xs = new List<float> { -len * 0.5f, len * 0.5f };
            var ys = new List<float> { yb, yt };
            foreach (var o in ops) { xs.Add(o.x0); xs.Add(o.x1); ys.Add(o.y0); ys.Add(o.y1); }
            xs = Breaks(xs, -len * 0.5f, len * 0.5f);
            ys = Breaks(ys, yb, yt);
            for (int i = 0; i < xs.Count - 1; i++)
                for (int j = 0; j < ys.Count - 1; j++)
                {
                    float cx = (xs[i] + xs[i + 1]) * 0.5f, cy = (ys[j] + ys[j + 1]) * 0.5f;
                    bool hole = false;
                    foreach (var o in ops) if (cx > o.x0 && cx < o.x1 && cy > o.y0 && cy < o.y1) { hole = true; break; }
                    if (hole) continue;
                    b.Quad(new Vector3(xs[i], ys[j], 0f), new Vector3(xs[i], ys[j + 1], 0f), new Vector3(xs[i + 1], ys[j + 1], 0f), new Vector3(xs[i + 1], ys[j], 0f), wall, Vector3.back);
                }
            foreach (var o in ops)
            {
                float D = o.depth;
                b.Quad(new Vector3(o.x0, o.y0, 0f), new Vector3(o.x0, o.y1, 0f), new Vector3(o.x0, o.y1, D), new Vector3(o.x0, o.y0, D), reveal, Vector3.right);
                b.Quad(new Vector3(o.x1, o.y0, 0f), new Vector3(o.x1, o.y1, 0f), new Vector3(o.x1, o.y1, D), new Vector3(o.x1, o.y0, D), reveal, Vector3.left);
                b.Quad(new Vector3(o.x0, o.y1, 0f), new Vector3(o.x1, o.y1, 0f), new Vector3(o.x1, o.y1, D), new Vector3(o.x0, o.y1, D), MB.Shade(reveal, 0.8f), Vector3.down);
                b.Quad(new Vector3(o.x0, o.y0, 0f), new Vector3(o.x1, o.y0, 0f), new Vector3(o.x1, o.y0, D), new Vector3(o.x0, o.y0, D), MB.Shade(reveal, 1.08f), Vector3.up);
            }
        }

        static void DressOpening(MB b, HouseSpec h, Opening o, System.Random r, bool groundFloor)
        {
            float w = o.x1 - o.x0, ht = o.y1 - o.y0, cx = (o.x0 + o.x1) * 0.5f, cy = (o.y0 + o.y1) * 0.5f, D = o.depth;
            Color32 frame = h.frame, arch = h.timber ? Wood : h.arch;
            const float aw = 0.11f;
            if (o.kind == 1)
            {
                // door leaf with raised panels and a glazed transom
                float leafTop = o.y0 + 2.2f;
                b.Box(new Vector3(cx, (o.y0 + leafTop) * 0.5f, D - 0.04f), new Vector3(w, leafTop - o.y0, 0.06f), h.door, MB.PZ);
                Color32 panel = MB.Shade(h.door, 1.15f);
                for (int i = -1; i <= 1; i += 2)
                {
                    foreach (float py in new[] { o.y0 + 0.6f, o.y0 + 1.6f })
                    {
                        float px = cx + i * w * 0.22f, hw = w * 0.17f;
                        b.Quad(new Vector3(px - hw, py - 0.4f, D - 0.075f), new Vector3(px - hw, py + 0.4f, D - 0.075f), new Vector3(px + hw, py + 0.4f, D - 0.075f), new Vector3(px + hw, py - 0.4f, D - 0.075f), panel, Vector3.back);
                    }
                }
                b.Box(new Vector3(cx + w * 0.36f, o.y0 + 1.05f, D - 0.1f), new Vector3(0.04f, 0.14f, 0.04f), Brass, 0);
                b.Box(new Vector3(cx, leafTop + 0.03f, D - 0.05f), new Vector3(w, 0.06f, 0.08f), frame, MB.PZ);
                b.Gloss = 0.85f;
                b.Quad(new Vector3(o.x0, leafTop, D - 0.02f), new Vector3(o.x0, o.y1, D - 0.02f), new Vector3(o.x1, o.y1, D - 0.02f), new Vector3(o.x1, leafTop, D - 0.02f), Glass, Vector3.back);
                b.Gloss = 0f;
                b.Box(new Vector3(cx, (leafTop + o.y1) * 0.5f, D - 0.05f), new Vector3(0.04f, o.y1 - leafTop, 0.05f), frame, MB.PZ);
                Architrave(b, o, arch, aw + 0.03f, false);
                return;
            }
            if (o.kind == 3)
            {
                b.Gloss = 0.8f;
                b.Quad(new Vector3(o.x0, o.y0, D - 0.02f), new Vector3(o.x0, o.y1, D - 0.02f), new Vector3(o.x1, o.y1, D - 0.02f), new Vector3(o.x1, o.y0, D - 0.02f), H("3D5B6B"), Vector3.back);
                b.Gloss = 0f;
                FrameBars(b, o, frame, 0.07f);
                for (float x = o.x0 + w / 3f; x < o.x1 - 0.1f; x += w / 3f) b.Box(new Vector3(x, cy, D - 0.06f), new Vector3(0.06f, ht, 0.06f), frame, MB.PZ);
                // goods on shelves behind the glass
                for (int k = 0; k < 6; k++)
                    b.Box(new Vector3(Mathf.Lerp(o.x0 + 0.3f, o.x1 - 0.3f, k / 5f), o.y0 + 0.35f, D + 0.15f), new Vector3(0.3f, 0.35f, 0.2f), Pick(r, new[] { H("D9473B"), H("E3B93A"), H("3B6FD1"), H("4E9A57") }), 0);
                Architrave(b, o, arch, aw, false);
                return;
            }

            // window / balcony door glass, curtains, inner frame
            b.Gloss = 0.88f;
            Color32 glass = r.NextDouble() < 0.18 ? H("47677A") : (r.NextDouble() < 0.5 ? Glass : H("29404F"));
            b.Quad(new Vector3(o.x0, o.y0, D - 0.01f), new Vector3(o.x0, o.y1, D - 0.01f), new Vector3(o.x1, o.y1, D - 0.01f), new Vector3(o.x1, o.y0, D - 0.01f), glass, Vector3.back);
            b.Gloss = 0f;
            if (r.NextDouble() < 0.45)
            {
                Color32 cc = Pick(r, CurtainCols);
                float cwid = w * Rand(r, 0.18f, 0.32f);
                b.Quad(new Vector3(o.x0, o.y0 + 0.05f, D - 0.03f), new Vector3(o.x0, o.y1, D - 0.03f), new Vector3(o.x0 + cwid, o.y1, D - 0.03f), new Vector3(o.x0 + cwid, o.y0 + 0.05f, D - 0.03f), cc, Vector3.back);
                b.Quad(new Vector3(o.x1 - cwid, o.y0 + 0.05f, D - 0.03f), new Vector3(o.x1 - cwid, o.y1, D - 0.03f), new Vector3(o.x1, o.y1, D - 0.03f), new Vector3(o.x1, o.y0 + 0.05f, D - 0.03f), cc, Vector3.back);
            }
            else if (r.NextDouble() < 0.3)
            {
                float bl = ht * Rand(r, 0.2f, 0.5f);
                b.Quad(new Vector3(o.x0, o.y1 - bl, D - 0.03f), new Vector3(o.x0, o.y1, D - 0.03f), new Vector3(o.x1, o.y1, D - 0.03f), new Vector3(o.x1, o.y1 - bl, D - 0.03f), H("E9E4D6"), Vector3.back);
            }
            FrameBars(b, o, frame, 0.055f);
            b.Box(new Vector3(cx, cy, D - 0.05f), new Vector3(0.045f, ht, 0.05f), frame, MB.PZ);
            if (o.kind == 2)
            {
                b.Box(new Vector3(cx, o.y0 + 0.28f, D - 0.05f), new Vector3(w, 0.5f, 0.05f), frame, MB.PZ);
                b.Box(new Vector3(cx, o.y1 - 0.45f, D - 0.05f), new Vector3(w, 0.045f, 0.05f), frame, MB.PZ);
            }
            else b.Box(new Vector3(cx, o.y0 + ht * 0.68f, D - 0.05f), new Vector3(w, 0.045f, 0.05f), frame, MB.PZ);

            Architrave(b, o, arch, aw, o.kind != 2);
            // lintel
            if (h.lintel == 0)
            {
                b.Box(new Vector3(cx, o.y1 + aw + 0.05f, -0.07f), new Vector3(w + 0.44f, 0.1f, 0.14f), arch, MB.PZ);
                b.Box(new Vector3(cx, o.y1 + aw + 0.13f, -0.09f), new Vector3(w + 0.52f, 0.06f, 0.18f), arch, MB.PZ);
            }
            else if (h.lintel == 1)
            {
                // flat arch of wedge stones with a keystone
                int n = 7;
                for (int k = 0; k < n; k++)
                {
                    float t = (k + 0.5f) / n;
                    float x = Mathf.Lerp(o.x0 - 0.1f, o.x1 + 0.1f, t);
                    float tilt = (t - 0.5f) * -40f;
                    b.Push(new Vector3(x, o.y1 + aw + 0.14f, -0.03f), Quaternion.Euler(0f, 0f, tilt));
                    bool key = k == n / 2;
                    b.Box(Vector3.zero, new Vector3((w + 0.2f) / n - 0.02f, key ? 0.36f : 0.26f, key ? 0.1f : 0.06f), key ? MB.Shade(arch, 1.1f) : MB.Jitter(arch, r, 0.06f), MB.PZ);
                    b.Pop();
                }
            }
            if (o.kind == 2) return;

            // sill
            b.Box(new Vector3(cx, o.y0 - 0.035f, -0.05f), new Vector3(w + 0.34f, 0.07f, 0.27f), MB.Shade(h.stone, 1.12f), 0);
            b.Box(new Vector3(cx, o.y0 - 0.1f, -0.03f), new Vector3(w + 0.1f, 0.06f, 0.06f), MB.Shade(h.stone, 0.9f), MB.PZ);

            // shutters, grille, flower box, rain streaks
            bool grille = groundFloor && r.NextDouble() < 0.35;
            if (grille)
            {
                for (float x = o.x0 + 0.13f; x < o.x1 - 0.06f; x += 0.14f)
                    b.Box(new Vector3(x, cy, 0.05f), new Vector3(0.022f, ht, 0.022f), Iron, MB.NY | MB.PY);
                foreach (float y in new[] { o.y0 + 0.2f, o.y1 - 0.2f })
                    b.Box(new Vector3(cx, y, 0.05f), new Vector3(w, 0.03f, 0.03f), Iron, 0);
            }
            if (h.shutters && o.kind == 0)
            {
                Color32 sc = h.shutter;
                bool oneClosed = r.NextDouble() < 0.12;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (oneClosed && s > 0)
                    {
                        b.Box(new Vector3(cx + w * 0.25f, cy, 0.04f), new Vector3(w * 0.5f, ht, 0.04f), sc, 0);
                        continue;
                    }
                    float sxp = s < 0 ? o.x0 - aw - 0.27f : o.x1 + aw + 0.27f;
                    b.Box(new Vector3(sxp, cy, -0.035f), new Vector3(0.52f, ht + 0.06f, 0.05f), sc, MB.PZ);
                    Color32 slat = MB.Shade(sc, 0.8f);
                    for (float y = o.y0 + 0.12f; y < o.y1 - 0.08f; y += 0.13f)
                        b.Quad(new Vector3(sxp - 0.21f, y - 0.017f, -0.062f), new Vector3(sxp - 0.21f, y + 0.017f, -0.062f), new Vector3(sxp + 0.21f, y + 0.017f, -0.062f), new Vector3(sxp + 0.21f, y - 0.017f, -0.062f), slat, Vector3.back);
                    b.Box(new Vector3(sxp, cy, -0.068f), new Vector3(0.42f, 0.07f, 0.02f), MB.Shade(sc, 1.1f), MB.PZ);
                }
            }
            if (!grille && r.NextDouble() < 0.14)
            {
                b.Box(new Vector3(cx, o.y0 + 0.07f, -0.3f), new Vector3(w, 0.24f, 0.24f), H("8E5A3C"), 0);
                var fr = new System.Random(r.Next());
                for (int i = 0; i < 4; i++)
                    b.IcoAO(new Vector3(o.x0 + 0.15f + i * (w - 0.3f) / 3f, o.y0 + 0.28f, -0.3f), new Vector3(0.17f, 0.15f, 0.14f), Pick(fr, new[] { H("D94A5B"), H("E8D24A"), H("F0F0F0"), H("C25ABF"), H("E87E3A") }), 0, 0.2f, fr, 0.3f);
                for (int i = 0; i < 3; i++)
                    b.IcoAO(new Vector3(o.x0 + 0.25f + i * (w - 0.5f) / 2f, o.y0 + 0.2f, -0.36f), new Vector3(0.2f, 0.12f, 0.12f), H("4F8A3C"), 0, 0.2f, fr, 0.3f);
            }
            if (r.NextDouble() < 0.35)
            {
                float sl = Rand(r, 0.4f, 1.1f);
                Color32 streak = MB.Shade(h.timber ? h.wall : (h.stoneHouse ? h.stone : h.wall), 0.86f);
                b.Quad(new Vector3(cx - 0.18f, o.y0 - 0.12f - sl, -0.004f), new Vector3(cx - 0.18f, o.y0 - 0.12f, -0.004f), new Vector3(cx + 0.18f, o.y0 - 0.12f, -0.004f), new Vector3(cx + 0.18f, o.y0 - 0.12f - sl, -0.004f), streak, Vector3.back);
            }
        }

        static void FrameBars(MB b, Opening o, Color32 frame, float bw)
        {
            float ht = o.y1 - o.y0, w = o.x1 - o.x0, cx = (o.x0 + o.x1) * 0.5f, cy = (o.y0 + o.y1) * 0.5f, z = o.depth - 0.045f;
            b.Box(new Vector3(o.x0 + bw * 0.5f, cy, z), new Vector3(bw, ht, 0.07f), frame, MB.PZ);
            b.Box(new Vector3(o.x1 - bw * 0.5f, cy, z), new Vector3(bw, ht, 0.07f), frame, MB.PZ);
            b.Box(new Vector3(cx, o.y1 - bw * 0.5f, z), new Vector3(w, bw, 0.07f), frame, MB.PZ);
            b.Box(new Vector3(cx, o.y0 + bw * 0.5f, z), new Vector3(w, bw, 0.07f), frame, MB.PZ);
        }

        static void Architrave(MB b, Opening o, Color32 arch, float aw, bool bottom)
        {
            float ht = o.y1 - o.y0, w = o.x1 - o.x0, cx = (o.x0 + o.x1) * 0.5f, cy = (o.y0 + o.y1) * 0.5f;
            b.Box(new Vector3(o.x0 - aw * 0.5f, cy + aw * 0.5f, -0.025f), new Vector3(aw, ht + aw, 0.05f), arch, MB.PZ);
            b.Box(new Vector3(o.x1 + aw * 0.5f, cy + aw * 0.5f, -0.025f), new Vector3(aw, ht + aw, 0.05f), arch, MB.PZ);
            b.Box(new Vector3(cx, o.y1 + aw * 0.5f, -0.025f), new Vector3(w, aw, 0.05f), arch, MB.PZ);
        }

        /// Half-timbered ("çatma") overlay for Black Sea style upper floors.
        static void Timber(MB b, float len, float yb, float yt, List<Opening> ops)
        {
            const float t = 0.14f, z = -0.03f;
            Color32 wood = Wood;
            // posts at the ends and between openings
            var posts = new List<float> { -len * 0.5f + 0.1f, len * 0.5f - 0.1f };
            var sorted = new List<Opening>(ops);
            sorted.Sort((a, c) => a.x0.CompareTo(c.x0));
            for (int i = 0; i < sorted.Count - 1; i++) posts.Add((sorted[i].x1 + sorted[i + 1].x0) * 0.5f);
            foreach (var o in sorted) { posts.Add(o.x0 - 0.2f); posts.Add(o.x1 + 0.2f); }
            posts.Sort();
            foreach (float x in posts) b.Box(new Vector3(x, (yb + yt) * 0.5f, z), new Vector3(t, yt - yb, 0.06f), wood, MB.PZ);
            b.Box(new Vector3(0f, yb + 0.08f, z - 0.005f), new Vector3(len, 0.16f, 0.07f), wood, MB.PZ);
            b.Box(new Vector3(0f, yt - 0.08f, z - 0.005f), new Vector3(len, 0.16f, 0.07f), wood, MB.PZ);
            float sill = yb + 0.95f;
            // sill-level rail between openings and braces in the panels below it
            for (int i = 0; i < posts.Count - 1; i++)
            {
                float a = posts[i], c = posts[i + 1];
                if (c - a < 0.35f) continue;
                bool underWindow = false, doorway = false;
                foreach (var o in ops)
                    if (c > o.x0 && a < o.x1)
                    {
                        if (o.y0 < sill) doorway = true;
                        if ((a + c) * 0.5f > o.x0 && (a + c) * 0.5f < o.x1) underWindow = true;
                    }
                if (doorway) continue;
                b.Box(new Vector3((a + c) * 0.5f, sill - 0.06f, z - 0.002f), new Vector3(c - a, 0.12f, 0.065f), wood, MB.PZ);
                if (!underWindow && c - a > 0.6f)
                {
                    Vector3 p0 = new Vector3(a + 0.05f, yb + 0.16f, z - 0.01f), p1 = new Vector3(c - 0.05f, sill - 0.12f, z - 0.01f);
                    b.Rod(p0, p1, 0.055f, 4, wood);
                    Vector3 q0 = new Vector3(a + 0.05f, sill + 0.1f, z - 0.01f), q1 = new Vector3(c - 0.05f, yt - 0.18f, z - 0.01f);
                    b.Rod(new Vector3(q0.x, q1.y, q0.z), new Vector3(q1.x, q0.y, q1.z), 0.055f, 4, wood);
                }
            }
        }

        /// Irregular stone facing on solid wall areas (stone houses and stone ground floors).
        static void StoneFacing(MB b, float len, float yb, float yt, List<Opening> ops, Color32 stone, System.Random r)
        {
            for (float y = yb + 0.18f; y < yt - 0.15f; y += 0.36f)
            {
                float x = -len * 0.5f + Rand(r, 0f, 0.3f);
                while (x < len * 0.5f - 0.2f)
                {
                    float sw = Rand(r, 0.35f, 0.8f);
                    float cx = x + sw * 0.5f;
                    bool hit = false;
                    foreach (var o in ops) if (cx > o.x0 - 0.25f - sw * 0.5f && cx < o.x1 + 0.25f + sw * 0.5f && y > o.y0 - 0.3f && y < o.y1 + 0.35f) { hit = true; break; }
                    if (!hit && r.NextDouble() < 0.55)
                    {
                        float hw = (sw - 0.05f) * 0.5f;
                        b.Quad(new Vector3(cx - hw, y - 0.15f, -0.012f), new Vector3(cx - hw, y + 0.15f, -0.012f), new Vector3(cx + hw, y + 0.15f, -0.012f), new Vector3(cx + hw, y - 0.15f, -0.012f), MB.Jitter(StoneCols[r.Next(StoneCols.Length)], r, 0.08f), Vector3.back);
                    }
                    x += sw;
                }
            }
        }

        // ------------------------------------------------------------------ balconies, bay windows, stairs, awnings

        static void Balcony(MB b, float x, float y, float zWall, float width, System.Random r, bool wooden, bool occupied = false)
        {
            float depth = 1.3f;
            float z = zWall - depth * 0.5f;
            Color32 rail = wooden ? Wood : Iron;
            b.Box(new Vector3(x, y - 0.08f, z), new Vector3(width, 0.18f, depth), H("C9C3B5"), 0);
            b.Box(new Vector3(x, y - 0.19f, zWall - depth + 0.04f), new Vector3(width + 0.04f, 0.06f, 0.08f), MB.Shade(H("C9C3B5"), 0.85f), 0);
            for (float px = x - width * 0.5f + 0.4f; px < x + width * 0.5f; px += Mathf.Max(0.6f, width * 0.5f - 0.4f))
                b.Rod(new Vector3(px, y - 0.95f, zWall), new Vector3(px, y - 0.18f, zWall - depth + 0.25f), 0.05f, 4, MB.Shade(H("C9C3B5"), 0.8f));
            float fz = zWall - depth + 0.05f;
            b.Box(new Vector3(x, y + 1.0f, fz), new Vector3(width, 0.07f, 0.09f), wooden ? Wood : H("2B2E30"), 0);
            b.Box(new Vector3(x, y + 0.12f, fz), new Vector3(width, 0.05f, 0.05f), rail, 0);
            if (wooden)
            {
                for (float px = -width * 0.5f; px <= width * 0.5f + 0.01f; px += 0.16f)
                    b.Box(new Vector3(x + px, y + 0.56f, fz), new Vector3(0.09f, 0.88f, 0.03f), MB.Shade(Wood, 1.15f), MB.NY | MB.PY);
            }
            else
            {
                for (float px = -width * 0.5f; px <= width * 0.5f + 0.01f; px += 0.14f)
                    b.Box(new Vector3(x + px, y + 0.56f, fz), new Vector3(0.025f, 0.88f, 0.025f), rail, MB.NY | MB.PY);
                for (float px = -width * 0.5f + 0.42f; px < width * 0.5f; px += 0.84f)
                {
                    // scroll ornaments
                    b.Box(new Vector3(x + px, y + 0.56f, fz), new Vector3(0.3f, 0.025f, 0.025f), rail, 0);
                    b.Box(new Vector3(x + px, y + 0.56f, fz), new Vector3(0.025f, 0.3f, 0.026f), rail, 0);
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                b.Box(new Vector3(x + s * width * 0.5f, y + 1.0f, zWall - depth * 0.5f), new Vector3(0.07f, 0.07f, depth), wooden ? Wood : H("2B2E30"), 0);
                for (float pz = 0.25f; pz < depth; pz += 0.16f)
                    b.Box(new Vector3(x + s * width * 0.5f, y + 0.56f, zWall - pz), new Vector3(0.025f, 0.88f, 0.025f), rail, MB.NY | MB.PY);
            }
            if (r.NextDouble() < 0.55)
            {
                var pr = new System.Random(r.Next());
                for (int i = 0; i < 3; i++)
                {
                    if (occupied && i == 1) continue;                      // people stand here: pots only at the ends
                    float px = x + (i - 1) * width * (occupied ? 0.44f : 0.36f);
                    b.Cyl(new Vector3(px, y + 0.01f, fz + 0.38f), 0.16f, 0.3f, 8, H("B5653D"), 0.2f);
                    b.IcoAO(new Vector3(px, y + 0.48f, fz + 0.38f), new Vector3(0.26f, 0.28f, 0.26f), Pick(pr, Leafy), 1, 0.22f, pr, 0.35f);
                    if (pr.NextDouble() < 0.6) b.IcoAO(new Vector3(px + 0.06f, y + 0.66f, fz + 0.3f), new Vector3(0.08f, 0.08f, 0.08f), Pick(pr, new[] { H("D94A5B"), H("F2E14C"), H("F0F0F0") }), 0, 0f, pr, 0f);
                }
            }
            if (r.NextDouble() < 0.3 && !occupied)
            {
                // little table and chair
                b.Cyl(new Vector3(x + width * 0.25f, y + 0.01f, zWall - 0.6f), 0.03f, 0.7f, 5, Iron);
                b.Cyl(new Vector3(x + width * 0.25f, y + 0.7f, zWall - 0.6f), 0.3f, 0.04f, 10, H("E8E6DE"));
                b.BoxB(x - width * 0.2f, y + 0.01f, zWall - 0.55f, 0.42f, 0.45f, 0.42f, H("6B4A33"), 0);
                b.BoxB(x - width * 0.2f, y + 0.45f, zWall - 0.36f, 0.42f, 0.5f, 0.05f, H("6B4A33"), 0);
            }
        }

        /// Traditional overhanging bay window (cumba) on the upper floors.
        static void Cumba(MB b, HouseSpec h, float cw, System.Random r)
        {
            float d = h.d, top = h.WallTop;
            const float cd = 0.95f;
            float y0 = 0.8f + FH - 0.1f, y1 = top - 0.3f;
            float zf = -d * 0.5f - cd;
            Color32 wall = h.timber ? h.wall : MB.Shade(h.wall, 1.04f);
            b.BoxB(0f, y0 - 0.22f, -d * 0.5f - cd * 0.5f, cw + 0.25f, 0.22f, cd + 0.1f, h.timber ? Wood : Trim, 0);
            for (float x = -cw * 0.5f + 0.2f; x <= cw * 0.5f - 0.15f; x += Mathf.Max(0.5f, (cw - 0.4f) / 2f))
                b.Rod(new Vector3(x, y0 - 1.2f, -d * 0.5f), new Vector3(x, y0 - 0.2f, zf + 0.12f), 0.07f, 4, Wood);
            for (int fl = 1; fl < h.floors; fl++)
            {
                float yb = fl == 1 ? y0 : 0.8f + fl * FH, yt = fl == h.floors - 1 ? y1 : 0.8f + (fl + 1) * FH;
                // front
                b.Push(new Vector3(0f, 0f, zf), 0f);
                var ops = new List<Opening>();
                for (int k = 0; k < 2; k++)
                {
                    float u = -cw * 0.5f + (k + 0.5f) * cw / 2f;
                    ops.Add(new Opening { x0 = u - 0.48f, x1 = u + 0.48f, y0 = yb + 0.95f, y1 = yb + 2.3f, depth = 0.16f, kind = 0 });
                }
                Facade(b, cw, yb, yt, ops, wall, MB.Shade(wall, 0.85f));
                foreach (var o in ops) DressOpening(b, h, o, r, false);
                if (h.timber) Timber(b, cw, yb, yt, ops);
                b.Pop();
                // sides
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Push(new Vector3(s * cw * 0.5f, 0f, -d * 0.5f - cd * 0.5f), s > 0 ? -90f : 90f);
                    var sops = new List<Opening> { new Opening { x0 = -0.3f, x1 = 0.3f, y0 = yb + 1.05f, y1 = yb + 2.2f, depth = 0.14f, kind = 4 } };
                    Facade(b, cd, yb, yt, sops, wall, MB.Shade(wall, 0.85f));
                    foreach (var o in sops) DressOpening(b, h, o, r, false);
                    b.Pop();
                }
            }
            // its own little tiled roof
            float ry0 = y1 + 0.75f, ry1 = y1 + 0.12f;
            System.Func<float, float, Vector3> P = (u, t) => new Vector3(u, Mathf.Lerp(ry1, ry0, t), Mathf.Lerp(zf - 0.35f, -d * 0.5f, t));
            b.BoxB(0f, y1 - 0.05f, -d * 0.5f - cd * 0.5f, cw + 0.1f, 0.2f, cd + 0.05f, h.timber ? Wood : Trim, 0);
            RoofPlane(b, P, new Vector3(0f, cd + 0.35f, -(ry0 - ry1)), t => -cw * 0.5f - 0.3f, t => cw * 0.5f + 0.3f, cd + 0.4f, h.roofCol, r);
            b.Quad(P(-cw * 0.5f - 0.3f, 0f) + Vector3.down * 0.12f, P(cw * 0.5f + 0.3f, 0f) + Vector3.down * 0.12f, P(cw * 0.5f + 0.3f, 1f) + Vector3.down * 0.12f, P(-cw * 0.5f - 0.3f, 1f) + Vector3.down * 0.12f, H("D9D2C3"), Vector3.down);
            for (int s = -1; s <= 1; s += 2)
                b.Tri(new Vector3(s * (cw * 0.5f + 0.02f), y1 + 0.12f, zf), new Vector3(s * (cw * 0.5f + 0.02f), y1 + 0.12f, -d * 0.5f), new Vector3(s * (cw * 0.5f + 0.02f), y1 + 0.75f, -d * 0.5f), wall, new Vector3(s, 0f, 0f));
        }

        static void Awning(MB b, float x, float y, float zWall, float width, Color32 col)
        {
            const float depth = 1.5f, drop = 0.6f;
            int stripes = Mathf.Max(4, Mathf.RoundToInt(width / 0.35f));
            for (int i = 0; i < stripes; i++)
            {
                float x0 = x - width * 0.5f + i * width / stripes, x1 = x0 + width / stripes;
                Color32 c = (i & 1) == 0 ? col : H("F2EEE6");
                Vector3 a = new Vector3(x0, y, zWall - 0.02f), bb = new Vector3(x1, y, zWall - 0.02f);
                Vector3 c2 = new Vector3(x1, y - drop, zWall - depth), d2 = new Vector3(x0, y - drop, zWall - depth);
                b.Quad(a, bb, c2, d2, c, new Vector3(0f, 1f, -0.4f));
                b.Quad(a, bb, c2, d2, MB.Shade(c, 0.75f), new Vector3(0f, -1f, 0.4f));
                // scalloped valance
                Vector3 m = (c2 + d2) * 0.5f + Vector3.down * 0.3f;
                b.Tri(d2, c2, m, c, Vector3.back);
                b.Tri(d2, c2, m, MB.Shade(c, 0.75f), Vector3.forward);
            }
            for (int s = -1; s <= 1; s += 2)
                b.Rod(new Vector3(x + s * width * 0.5f, y - 0.05f, zWall), new Vector3(x + s * width * 0.5f, y - drop, zWall - depth), 0.02f, 4, Iron);
        }

        static void OuterStairs(MB b, HouseSpec h, System.Random r)
        {
            float w = h.w, d = h.d;
            int steps = 14;
            float rise = FH / steps, run = (d - 1.6f) / steps;
            for (int k = 0; k < steps; k++)
            {
                float y = 0.8f + (k + 1) * rise;
                float z = -d * 0.5f + 0.8f + k * run;
                b.BoxB(w * 0.5f + 0.7f, -1f, z + run * 0.5f, 1.3f, y + 1f, run, h.stone);
                b.BoxB(w * 0.5f + 0.7f, y - 0.03f, z + run * 0.5f + 0.02f, 1.34f, 0.05f, run + 0.04f, MB.Shade(h.stone, 1.12f), 0);
            }
            b.BoxB(w * 0.5f + 0.7f, 0.8f + FH - 0.2f, d * 0.5f - 0.6f, 1.3f, 0.2f, 1.2f, h.stone, 0);
            for (float t = 0f; t <= 1.001f; t += 0.125f)
            {
                Vector3 p = new Vector3(w * 0.5f + 1.32f, Mathf.Lerp(1.0f, 0.8f + FH, t), Mathf.Lerp(-d * 0.5f + 0.8f, d * 0.5f - 0.8f, t));
                b.Rod(p, p + Vector3.up * 0.95f, 0.02f, 4, Iron);
            }
            b.Rod(new Vector3(w * 0.5f + 1.32f, 1.95f, -d * 0.5f + 0.8f), new Vector3(w * 0.5f + 1.32f, 0.8f + FH + 0.95f, d * 0.5f - 0.8f), 0.035f, 5, Iron);
        }

        // ------------------------------------------------------------------ roofs

        /// Shingled roof plane: P(u, t) with t = 0 at eave, 1 at ridge. umin/umax(t) clip hips.
        static void RoofPlane(MB b, System.Func<float, float, Vector3> P, Vector3 normal, System.Func<float, float> umin, System.Func<float, float> umax,
                              float slopeLen, Color32 tile, System.Random r, bool ribs = true)
        {
            int bands = Mathf.Max(3, Mathf.CeilToInt(slopeLen / 0.42f));
            Vector3 nrm = normal.normalized;
            Vector3 lift = nrm * 0.07f;
            Vector3 downSlope = (P(0.5f * (umin(0f) + umax(0f)), 0f) - P(0.5f * (umin(0f) + umax(0f)), 1f)).normalized;
            Vector3 lipHint = downSlope + Vector3.down * 0.3f;
            Color32 dark = MB.Shade(tile, 0.72f);
            for (int k = 0; k < bands; k++)
            {
                float t0 = (float)k / bands, t1 = (float)(k + 1) / bands;
                float a0 = umin(t0), a1 = umax(t0), c0 = umin(t1), c1 = umax(t1);
                if (a1 - a0 < 0.02f && c1 - c0 < 0.02f) continue;
                Color32 col = MB.Jitter((k & 1) == 0 ? tile : MB.Shade(tile, 0.95f), r, 0.035f);
                if (r.NextDouble() < 0.06) col = MB.Shade(col, 0.82f);
                Vector3 p00 = P(a0, t0) + lift, p01 = P(a1, t0) + lift, p10 = P(c0, t1), p11 = P(c1, t1);
                b.Quad(p00, p01, p11, p10, col, nrm);
                b.Quad(P(a0, t0), P(a1, t0), p01, p00, dark, lipHint);
            }
            if (!ribs) return;
            // barrel-tile ribs running down the slope
            float umn = umin(0f), umx = umax(0f);
            Color32 ribCol = MB.Shade(tile, 1.1f);
            Vector3 sideDir = (P(umn + 1f, 0f) - P(umn, 0f)).normalized;
            Vector3 side = sideDir * 0.11f;
            Vector3 up = nrm * 0.12f;
            for (float u = umn + 0.24f; u < umx - 0.1f; u += 0.46f)
            {
                float tmax = 1f;
                for (float t = 0f; t <= 1f; t += 0.02f) if (u < umin(t) + 0.05f || u > umax(t) - 0.05f) { tmax = t; break; }
                if (tmax < 0.06f) continue;
                Vector3 e0 = P(u, 0f) + lift * 0.6f, e1 = P(u, tmax) + lift * 0.6f;
                Color32 rc = MB.Jitter(ribCol, r, 0.04f);
                Vector3 m0 = e0 + up * 0.75f + side * 0.0f;
                b.Quad(e0 - side, e1 - side, e1 + up, e0 + up, rc, nrm - sideDir);
                b.Quad(e0 + up, e1 + up, e1 + side, e0 + side, MB.Shade(rc, 0.88f), nrm + sideDir);
                b.Tri(e0 - side, e0 + up, e0 + side, MB.Shade(rc, 0.7f), downSlope);
                _ = m0;
            }
        }

        static void Rafters(MB b, float W, float D, float he, float top, float wallHalfZ, float wallHalfX, bool hip)
        {
            Color32 wood = H("6B4A33");
            for (int side = -1; side <= 1; side += 2)
            {
                for (float x = -wallHalfX + 0.3f; x < wallHalfX - 0.1f; x += 0.65f)
                    b.Rod(new Vector3(x, top - 0.27f, side * wallHalfZ), new Vector3(x, he - 0.27f, side * (D * 0.5f - 0.08f)), 0.05f, 4, wood, -1f, true);
                if (hip)
                    for (float z = -wallHalfZ + 0.3f; z < wallHalfZ - 0.1f; z += 0.65f)
                        b.Rod(new Vector3(side * wallHalfX, top - 0.27f, z), new Vector3(side * (W * 0.5f - 0.08f), he - 0.27f, z), 0.05f, 4, wood, -1f, true);
            }
        }

        static void Gutters(MB b, HouseSpec h, float W, float D, float he, float wallHalfX, float wallHalfZ)
        {
            Color32 g = h.gutter;
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (D * 0.5f + 0.06f);
                b.Rod(new Vector3(-W * 0.5f, he - 0.2f, z), new Vector3(W * 0.5f, he - 0.2f, z), 0.07f, 6, g);
                // downpipes at both ends
                for (int e = -1; e <= 1; e += 2)
                {
                    float x = e * (W * 0.5f - 0.25f);
                    Vector3 a = new Vector3(x, he - 0.22f, z), m = new Vector3(e * (wallHalfX - 0.25f), he - 0.7f, side * (wallHalfZ + 0.08f));
                    b.Rod(a, m, 0.045f, 5, g);
                    b.Rod(m, new Vector3(m.x, 0.35f, m.z), 0.045f, 5, g);
                    b.Rod(new Vector3(m.x, 0.35f, m.z), new Vector3(m.x, 0.15f, m.z + side * 0.2f), 0.045f, 5, g);
                    for (float y = 1.5f; y < he - 0.8f; y += 1.6f) b.Box(new Vector3(m.x, y, m.z - side * 0.04f), new Vector3(0.12f, 0.04f, 0.1f), MB.Shade(g, 0.8f), 0);
                }
            }
        }

        /// Ridge along local x. Returns the ridge height.
        static float GableRoof(MB b, HouseSpec h, float w, float d, float top, System.Random r)
        {
            const float ov = 0.65f, s = 0.5f;
            float W = w + 2f * ov;
            float hr = top + d * 0.5f * s, he = top - ov * s;
            float zr = 0f, ze = d * 0.5f + ov;
            float slopeLen = Mathf.Sqrt(ze * ze + (hr - he) * (hr - he));
            Color32 tile = h.roofCol;
            Color32 wall = h.stoneHouse ? h.stone : h.wall;
            // gable walls with a round attic vent
            for (int e = -1; e <= 1; e += 2)
            {
                b.Tri(new Vector3(e * w * 0.5f, top, -d * 0.5f), new Vector3(e * w * 0.5f, top, d * 0.5f), new Vector3(e * w * 0.5f, hr, 0f), wall, new Vector3(e, 0f, 0f));
                b.Push(new Vector3(e * (w * 0.5f + 0.01f), top + (hr - top) * 0.4f, 0f), Quaternion.Euler(0f, 0f, -90f * e));
                b.Cyl(Vector3.zero, 0.4f, 0.05f, 12, h.timber ? Wood : Trim);
                b.Gloss = 0.8f;
                b.Cyl(new Vector3(0f, 0.05f, 0f), 0.3f, 0.02f, 12, Glass);
                b.Gloss = 0f;
                b.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.6f, 0.02f, 0.04f), h.frame, 0);
                b.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.04f, 0.02f, 0.6f), h.frame, 0);
                b.Pop();
            }
            for (int sd = -1; sd <= 1; sd += 2)
            {
                int side = sd;
                System.Func<float, float, Vector3> P = (u, t) => new Vector3(u, Mathf.Lerp(he, hr, t), side * Mathf.Lerp(ze, zr, t));
                Vector3 n = new Vector3(0f, ze, side * (hr - he));
                RoofPlane(b, P, n, t => -W * 0.5f, t => W * 0.5f, slopeLen, tile, r);
                Vector3 dn = Vector3.down * 0.18f;
                b.Quad(P(-W * 0.5f, 0f) + dn, P(W * 0.5f, 0f) + dn, P(W * 0.5f, 1f) + dn, P(-W * 0.5f, 1f) + dn, H("D9D2C3"), -n);
                b.Quad(P(-W * 0.5f, 0f) + dn, P(W * 0.5f, 0f) + dn, P(W * 0.5f, 0f), P(-W * 0.5f, 0f), MB.Shade(tile, 0.75f), new Vector3(0f, 0f, side));
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 a = P(e * W * 0.5f, 0f), c = P(e * W * 0.5f, 1f);
                    b.Quad(a + dn, c + dn, c + Vector3.up * 0.12f, a + Vector3.up * 0.12f, h.timber ? Wood : Trim, new Vector3(e, 0f, 0f));
                    b.Quad(a + dn, c + dn, c + Vector3.up * 0.12f, a + Vector3.up * 0.12f, MB.Shade(h.timber ? Wood : Trim, 0.8f), new Vector3(-e, 0f, 0f));
                }
            }
            b.Rod(new Vector3(-W * 0.5f - 0.05f, hr + 0.06f, 0f), new Vector3(W * 0.5f + 0.05f, hr + 0.06f, 0f), 0.18f, 7, MB.Shade(tile, 0.88f));
            for (int e = -1; e <= 1; e += 2) b.IcoAO(new Vector3(e * (W * 0.5f + 0.05f), hr + 0.1f, 0f), new Vector3(0.2f, 0.2f, 0.2f), MB.Shade(tile, 0.85f), 0, 0f, r, 0.2f);
            Rafters(b, W, 2f * ze, he, top, d * 0.5f, w * 0.5f, false);
            Gutters(b, h, W, 2f * ze, he, w * 0.5f, d * 0.5f);
            return hr;
        }

        static float HipRoof(MB b, HouseSpec h, float w, float d, float top, System.Random r)
        {
            const float ov = 0.65f, s = 0.55f;
            float W = w + 2f * ov, D = d + 2f * ov;
            float he = top - ov * s;
            float hr = he + D * 0.5f * s;
            float lr = Mathf.Max(0f, W - D) * 0.5f;
            float slopeLen = Mathf.Sqrt(D * D * 0.25f + (hr - he) * (hr - he));
            Color32 tile = h.roofCol, cap = MB.Shade(tile, 0.86f);
            Vector3 dn = Vector3.down * 0.18f;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                int side = sd;
                System.Func<float, float, Vector3> P = (u, t) => new Vector3(u, Mathf.Lerp(he, hr, t), side * Mathf.Lerp(D * 0.5f, 0f, t));
                Vector3 n = new Vector3(0f, D * 0.5f, side * (hr - he));
                RoofPlane(b, P, n, t => -Mathf.Lerp(W * 0.5f, lr, t), t => Mathf.Lerp(W * 0.5f, lr, t), slopeLen, tile, r);
                System.Func<float, float, Vector3> Q = (u, t) => new Vector3(side * Mathf.Lerp(W * 0.5f, lr, t), Mathf.Lerp(he, hr, t), u);
                Vector3 n2 = new Vector3(side * (hr - he), (W * 0.5f - lr), 0f);
                RoofPlane(b, Q, n2, t => -D * 0.5f * (1f - t), t => D * 0.5f * (1f - t), Mathf.Sqrt((W * 0.5f - lr) * (W * 0.5f - lr) + (hr - he) * (hr - he)), tile, r);
                b.Quad(new Vector3(-W * 0.5f, he, side * D * 0.5f) + dn, new Vector3(W * 0.5f, he, side * D * 0.5f) + dn,
                       new Vector3(W * 0.5f, he, side * D * 0.5f), new Vector3(-W * 0.5f, he, side * D * 0.5f), MB.Shade(tile, 0.75f), new Vector3(0f, 0f, side));
                b.Quad(new Vector3(side * W * 0.5f, he, -D * 0.5f) + dn, new Vector3(side * W * 0.5f, he, D * 0.5f) + dn,
                       new Vector3(side * W * 0.5f, he, D * 0.5f), new Vector3(side * W * 0.5f, he, -D * 0.5f), MB.Shade(tile, 0.75f), new Vector3(side, 0f, 0f));
                for (int e = -1; e <= 1; e += 2)
                    b.Rod(new Vector3(e * W * 0.5f, he + 0.07f, side * D * 0.5f), new Vector3(e * lr, hr + 0.07f, 0f), 0.14f, 6, cap);
            }
            b.Quad(new Vector3(-W * 0.5f, he, -D * 0.5f) + dn, new Vector3(W * 0.5f, he, -D * 0.5f) + dn, new Vector3(W * 0.5f, he, D * 0.5f) + dn, new Vector3(-W * 0.5f, he, D * 0.5f) + dn, H("D9D2C3"), Vector3.down);
            if (lr > 0.01f) b.Rod(new Vector3(-lr - 0.1f, hr + 0.07f, 0f), new Vector3(lr + 0.1f, hr + 0.07f, 0f), 0.17f, 7, cap);
            b.IcoAO(new Vector3(-lr, hr + 0.12f, 0f), new Vector3(0.2f, 0.2f, 0.2f), cap, 0, 0f, r, 0.2f);
            if (lr > 0.01f) b.IcoAO(new Vector3(lr, hr + 0.12f, 0f), new Vector3(0.2f, 0.2f, 0.2f), cap, 0, 0f, r, 0.2f);
            Rafters(b, W, D, he, top, d * 0.5f, w * 0.5f, true);
            Gutters(b, h, W, D, he, w * 0.5f, d * 0.5f);
            return hr;
        }

        static void Chimney(MB b, HouseSpec h, float x, float z, float top, float ridge, System.Random r)
        {
            Color32 body = h.stoneHouse ? H("B5654A") : (r.NextDouble() < 0.5 ? h.stone : MB.Shade(h.wall, 0.95f));
            float y1 = ridge + 1.0f;
            b.BoxB(x, top - 0.5f, z, 0.7f, y1 - top + 0.5f, 0.7f, body);
            b.BoxB(x, y1 - 0.5f, z, 0.82f, 0.12f, 0.82f, MB.Shade(body, 1.1f), 0);
            b.BoxB(x, y1, z, 0.86f, 0.1f, 0.86f, Trim, 0);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    b.BoxB(x + i * 0.32f, y1 + 0.1f, z + j * 0.32f, 0.1f, 0.32f, 0.1f, body, 0);
            b.Cone(new Vector3(x, y1 + 0.42f, z), 0.68f, 0.38f, 4, h.roofCol, Mathf.PI * 0.25f);
            b.BoxB(x, y1 + 0.38f, z, 0.92f, 0.05f, 0.92f, MB.Shade(h.roofCol, 0.8f), 0);
        }

        static void FlatRoofTop(MB b, HouseSpec h, System.Random r)
        {
            float w = h.w, d = h.d, top = h.WallTop;
            Color32 wall = h.stoneHouse ? h.stone : h.wall;
            b.BoxB(0f, top - 0.05f, 0f, w + 0.3f, 0.3f, d + 0.3f, FlatRoof, 0);
            float py = top + 0.25f;
            // terrace tiles
            Color32 tileA = H("A9A396"), tileB = H("9E978A");
            for (float x = -w * 0.5f + 0.3f; x < w * 0.5f - 0.3f; x += 0.9f)
                b.Quad(new Vector3(x, py + 0.005f, -d * 0.5f + 0.3f), new Vector3(x, py + 0.005f, d * 0.5f - 0.3f), new Vector3(x + 0.04f, py + 0.005f, d * 0.5f - 0.3f), new Vector3(x + 0.04f, py + 0.005f, -d * 0.5f + 0.3f), tileB, Vector3.up);
            for (float z = -d * 0.5f + 0.3f; z < d * 0.5f - 0.3f; z += 0.9f)
                b.Quad(new Vector3(-w * 0.5f + 0.3f, py + 0.006f, z), new Vector3(-w * 0.5f + 0.3f, py + 0.006f, z + 0.04f), new Vector3(w * 0.5f - 0.3f, py + 0.006f, z + 0.04f), new Vector3(w * 0.5f - 0.3f, py + 0.006f, z), tileB, Vector3.up);
            b.Quad(new Vector3(-w * 0.5f + 0.3f, py + 0.002f, -d * 0.5f + 0.3f), new Vector3(-w * 0.5f + 0.3f, py + 0.002f, d * 0.5f - 0.3f), new Vector3(w * 0.5f - 0.3f, py + 0.002f, d * 0.5f - 0.3f), new Vector3(w * 0.5f - 0.3f, py + 0.002f, -d * 0.5f + 0.3f), tileA, Vector3.up);
            bool railing = r.NextDouble() < 0.35;
            for (int s = -1; s <= 1; s += 2)
            {
                if (railing)
                {
                    b.BoxB(0f, py - 0.1f, s * (d * 0.5f - 0.05f), w + 0.3f, 0.3f, 0.3f, wall, 0);
                    b.BoxB(s * (w * 0.5f - 0.05f), py - 0.1f, 0f, 0.3f, 0.3f, d - 0.3f, wall, 0);
                    for (float x = -w * 0.5f; x <= w * 0.5f; x += 0.3f) b.Cyl(new Vector3(x, py + 0.2f, s * (d * 0.5f - 0.05f)), 0.06f, 0.6f, 6, Trim, 0.045f);
                    for (float z = -d * 0.5f + 0.3f; z <= d * 0.5f - 0.3f; z += 0.3f) b.Cyl(new Vector3(s * (w * 0.5f - 0.05f), py + 0.2f, z), 0.06f, 0.6f, 6, Trim, 0.045f);
                    b.BoxB(0f, py + 0.8f, s * (d * 0.5f - 0.05f), w + 0.4f, 0.12f, 0.36f, Trim, 0);
                    b.BoxB(s * (w * 0.5f - 0.05f), py + 0.8f, 0f, 0.36f, 0.12f, d - 0.3f, Trim, 0);
                }
                else
                {
                    b.BoxB(0f, py - 0.1f, s * (d * 0.5f - 0.05f), w + 0.3f, 1.0f, 0.3f, wall, 0);
                    b.BoxB(s * (w * 0.5f - 0.05f), py - 0.1f, 0f, 0.3f, 1.0f, d - 0.3f, wall, 0);
                    b.BoxB(0f, py + 0.9f, s * (d * 0.5f - 0.05f), w + 0.45f, 0.1f, 0.42f, Trim, 0);
                    b.BoxB(s * (w * 0.5f - 0.05f), py + 0.9f, 0f, 0.42f, 0.1f, d - 0.3f, Trim, 0);
                }
            }
            // stair hut with door and window
            float hx = w * 0.5f - 1.7f, hz = d * 0.5f - 1.7f;
            b.BoxB(hx, py - 0.05f, hz, 2.6f, 2.4f, 2.6f, wall, 0);
            b.BoxB(hx, py + 2.35f, hz, 2.9f, 0.15f, 2.9f, Trim, 0);
            b.Box(new Vector3(hx - 0.4f, py + 1.0f, hz - 1.32f), new Vector3(0.9f, 2.0f, 0.06f), h.door, MB.PZ);
            b.Box(new Vector3(hx - 0.4f, py + 1.0f, hz - 1.3f), new Vector3(1.05f, 2.12f, 0.04f), Trim, MB.PZ);
            b.Gloss = 0.85f;
            b.Box(new Vector3(hx - 1.32f, py + 1.5f, hz), new Vector3(0.06f, 0.55f, 0.7f), Glass, MB.PX);
            b.Gloss = 0f;
            // water tank on a steel stand
            Color32 tank = r.NextDouble() < 0.5 ? H("2B2D30") : H("3D78B8");
            float tx = -w * 0.5f + 1.5f, tz = d * 0.5f - 1.4f;
            for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) b.Rod(new Vector3(tx + i * 0.5f, py, tz + j * 0.5f), new Vector3(tx + i * 0.5f, py + 0.45f, tz + j * 0.5f), 0.035f, 4, Iron);
            b.BoxB(tx, py + 0.4f, tz, 1.25f, 0.06f, 1.25f, Iron, 0);
            b.Cyl(new Vector3(tx, py + 0.46f, tz), 0.6f, 1.25f, 12, tank, 0.58f);
            for (int k = 1; k < 4; k++) b.Cyl(new Vector3(tx, py + 0.46f + k * 0.3f, tz), 0.615f, 0.05f, 12, MB.Shade(tank, 0.8f), -1f, false);
            b.Cyl(new Vector3(tx, py + 1.71f, tz), 0.25f, 0.08f, 10, MB.Shade(tank, 1.2f));
            if (h.solar)
            {
                b.Push(new Vector3(-w * 0.5f + 1.6f, py + 0.4f, -d * 0.5f + 1.5f), Quaternion.Euler(-35f, 0f, 0f));
                SolarPanel(b);
                b.Pop();
                b.Rod(new Vector3(-w * 0.5f + 0.55f, py + 1.15f, -d * 0.5f + 1.95f), new Vector3(-w * 0.5f + 2.65f, py + 1.15f, -d * 0.5f + 1.95f), 0.25f, 10, H("E6E6E2"));
                for (int i = -1; i <= 1; i += 2) b.Rod(new Vector3(-w * 0.5f + 1.6f + i * 0.9f, py, -d * 0.5f + 1.95f), new Vector3(-w * 0.5f + 1.6f + i * 0.9f, py + 1.0f, -d * 0.5f + 1.95f), 0.03f, 4, Iron);
            }
            if (r.NextDouble() < 0.4)
            {
                float y = py + 1.6f;
                b.Rod(new Vector3(-w * 0.3f, py, -0.5f), new Vector3(-w * 0.3f, y, -0.5f), 0.03f, 4, Iron);
                b.Rod(new Vector3(w * 0.15f, py, -0.5f), new Vector3(w * 0.15f, y, -0.5f), 0.03f, 4, Iron);
                b.Rod(new Vector3(-w * 0.3f, y, -0.5f), new Vector3(w * 0.15f, y, -0.5f), 0.012f, 3, Iron, -1f, false);
                Quaternion hq = Quaternion.Euler(0f, h.yaw, 0f);
                for (float x = -w * 0.3f + 0.3f; x < w * 0.15f - 0.3f; x += 0.62f)
                {
                    bool towel = r.NextDouble() < 0.5;
                    clothSpots.Add(new ClothSpot
                    {
                        pos = h.pos + hq * new Vector3(x, y - 0.02f, -0.5f), yaw = h.yaw, flag = false,
                        color = Pick(r, new[] { H("E9E4D8"), H("D45050"), H("4E7FC4"), H("F2C94C"), H("7DBF7A"), H("F2F2EE"), H("C77DB5") }),
                        w = towel ? 0.48f : 0.38f, h = towel ? 0.62f : 0.5f
                    });
                    b.Box(new Vector3(x, y + 0.01f, -0.5f), new Vector3(0.03f, 0.05f, 0.02f), Pick(r, new[] { H("E8C33A"), H("3E9E57"), H("D94436") }), 0);
                }
            }
            // a bedsheet with "YARDIM" (help) painted on it, laid out behind the people for helicopters
            var sr = new System.Random(h.seed * 31 + 7);
            float free = (w * 0.5f - 3.1f) - (-w * 0.5f + 2.3f);
            if (h.flooded && free > 3.0f && sr.NextDouble() < 0.45) HelpSheet(b, new Vector3((w * 0.5f - 3.1f - w * 0.5f + 2.3f) * 0.5f - 0.0f, py + 0.012f, d * 0.5f - 1.75f), Mathf.Min(free - 0.2f, 3.2f), sr);
            // potted plants on the parapet corners
            var pr = new System.Random(r.Next());
            for (int k = 0; k < 2; k++)
            {
                Vector3 pp = new Vector3(k == 0 ? -w * 0.5f + 0.7f : w * 0.5f - 0.7f, py, -d * 0.5f + 0.7f);
                b.Cyl(pp, 0.25f, 0.45f, 8, H("B5653D"), 0.3f);
                b.IcoAO(pp + Vector3.up * 0.8f, new Vector3(0.4f, 0.45f, 0.4f), Pick(pr, Leafy), 1, 0.2f, pr, 0.35f);
            }
        }

        static readonly string[] HelpFont =
        {
            // 5x7 block letters Y A R D I M (rows top to bottom)
            "10001100010101000100001000010000100", "01110100011000111111100011000110001", "11110100011000111110101001001010001",
            "11110100011000110001100011000111110", "01110001000010000100001000010001110", "10001110111010110101100011000110001",
        };

        /// A white sheet on the terrace with YARDIM painted in red brush strokes; letters read from the front of the house.
        static void HelpSheet(MB b, Vector3 c, float width, System.Random r)
        {
            float h = width * 0.32f;
            Color32 sheet = Pick(r, new[] { H("EDEBE4"), H("E9E4D8"), H("F2EFE8") }), paint = Pick(r, new[] { H("C8372D"), H("B8262B"), H("D9302B") });
            // the sheet: a slightly rumpled 6x3 grid
            var pts = new Vector3[7, 4];
            for (int i = 0; i <= 6; i++)
                for (int j = 0; j <= 3; j++)
                    pts[i, j] = c + new Vector3((i / 6f - 0.5f) * width, ((i + j) % 2 == 0 ? 0.012f : 0f) + Rand(r, 0f, 0.01f), (j / 3f - 0.5f) * h);
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 3; j++)
                    b.Quad(pts[i, j], pts[i, j + 1], pts[i + 1, j + 1], pts[i + 1, j], MB.Shade(sheet, (i + j) % 2 == 0 ? 1f : 0.94f), Vector3.up);
            // letters: 6 glyphs of 5x7 cells, 1 cell spacing
            float cell = Mathf.Min(width * 0.9f / 35f, h * 0.8f / 7f);
            float x0 = c.x - 17f * cell, z0 = c.z + 3.5f * cell;
            for (int g = 0; g < HelpFont.Length; g++)
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (HelpFont[g][row * 5 + col] != '1') continue;
                        // read from the front of the house (-z): left to right along +x, letter tops toward +z
                        float x = x0 + (g * 6 + col) * cell, z = z0 - row * cell;
                        Vector3 p = new Vector3(x, c.y + 0.022f, z);
                        float jx = Rand(r, -0.08f, 0.08f) * cell, jz = Rand(r, -0.08f, 0.08f) * cell;
                        b.Quad(p + new Vector3(jx, 0f, jz), p + new Vector3(jx, 0f, -cell * 1.04f), p + new Vector3(cell * 1.04f, 0f, -cell * 1.04f), p + new Vector3(cell * 1.04f + jx, 0f, jz), paint, Vector3.up);
                    }
        }

        static void SolarPanel(MB b)
        {
            b.Box(new Vector3(0f, -0.06f, 0f), new Vector3(2.1f, 0.06f, 1.35f), H("B9BDC0"), 0);
            b.Gloss = 0.75f;
            b.Box(Vector3.zero, new Vector3(2.0f, 0.06f, 1.25f), H("1F3448"), 0);
            b.Gloss = 0f;
            for (float x = -0.75f; x <= 0.76f; x += 0.5f) b.Box(new Vector3(x, 0.035f, 0f), new Vector3(0.02f, 0.01f, 1.25f), H("8FA3B5"), 0);
            b.Box(new Vector3(0f, 0.035f, 0f), new Vector3(2.0f, 0.01f, 0.02f), H("8FA3B5"), 0);
        }

        static void SolarHeater(MB b, HouseSpec h, float top, float ridge, System.Random r)
        {
            float x = Rand(r, -h.w * 0.25f, h.w * 0.25f);
            const float t = 0.45f;
            bool alongX = h.w >= h.d;
            float halfD = (alongX ? h.d : h.w) * 0.5f + 0.65f;
            float y = Mathf.Lerp(top - 0.35f, ridge, t) + 0.3f;
            float zz = -halfD * (1f - t);
            Vector3 pos = alongX ? new Vector3(x, y, zz) : new Vector3(zz, y, x);
            float yaw = alongX ? 0f : 90f;
            b.Push(pos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-48f, 0f, 0f));
            SolarPanel(b);
            b.Pop();
            Vector3 tp = alongX ? new Vector3(x, y + 0.75f, zz + 0.55f) : new Vector3(zz + 0.55f, y + 0.75f, x);
            Vector3 ax = alongX ? Vector3.right : Vector3.forward;
            b.Rod(tp - ax * 1.05f, tp + ax * 1.05f, 0.27f, 10, H("E9E9E5"));
            b.Rod(tp - ax * 1.1f, tp - ax * 1.04f, 0.29f, 10, H("A9ADB0"));
            b.Rod(tp + ax * 1.04f, tp + ax * 1.1f, 0.29f, 10, H("A9ADB0"));
            Vector3 fw = alongX ? Vector3.forward : Vector3.right;
            for (int i = -1; i <= 1; i += 2) b.Rod(tp + ax * (i * 0.8f), tp + ax * (i * 0.8f) - Vector3.up * 0.75f - fw * 0.4f, 0.025f, 4, Iron);
        }

        static void GardenWall(MB b, HouseSpec h, System.Random r)
        {
            float gw = h.w * 0.5f + Rand(r, 2f, 3.5f), gd = h.d * 0.5f + Rand(r, 2f, 3.5f);
            bool stoneWall = r.NextDouble() < 0.55;
            float hgt = stoneWall ? 1.25f : 1.1f;
            Quaternion q = Quaternion.Euler(0f, h.yaw, 0f);
            Vector3[] corners = { new Vector3(-gw, 0f, -gd), new Vector3(gw, 0f, -gd), new Vector3(gw, 0f, gd), new Vector3(-gw, 0f, gd) };
            Color32 wood = H("B08A5C");
            for (int e = 0; e < 4; e++)
            {
                Vector3 a = corners[e], c = corners[(e + 1) % 4];
                float len = Vector3.Distance(a, c);
                int segs = Mathf.CeilToInt(len / 1.6f);
                for (int k = 0; k < segs; k++)
                {
                    float t0 = (float)k / segs, t1 = (float)(k + 1) / segs;
                    float tm = (t0 + t1) * 0.5f;
                    if (e == 0 && Mathf.Abs(tm - 0.5f) < 0.12f) continue; // gate
                    Vector3 wp = h.pos + q * Vector3.Lerp(a, c, tm);
                    float gy = Valley.TerrainHeight(wp.x, wp.z);
                    float yaw = h.yaw + (e % 2 == 0 ? 0f : 90f);
                    if (stoneWall)
                    {
                        b.BoxRot(new Vector3(wp.x, gy - 0.4f, wp.z), new Vector3(len / segs + 0.05f, hgt + 0.4f, 0.45f), yaw, MB.Jitter(h.stone, r, 0.07f));
                        b.BoxRot(new Vector3(wp.x, gy + hgt, wp.z), new Vector3(len / segs + 0.07f, 0.1f, 0.55f), yaw, MB.Shade(h.stone, 1.1f));
                    }
                    else
                    {
                        Vector3 pa = h.pos + q * Vector3.Lerp(a, c, t0), pb = h.pos + q * Vector3.Lerp(a, c, t1);
                        float ya = Valley.TerrainHeight(pa.x, pa.z), yb = Valley.TerrainHeight(pb.x, pb.z);
                        b.Rod(new Vector3(pa.x, ya - 0.3f, pa.z), new Vector3(pa.x, ya + hgt + 0.1f, pa.z), 0.06f, 4, MB.Shade(wood, 0.8f));
                        b.Rod(new Vector3(pa.x, ya + 0.75f, pa.z), new Vector3(pb.x, yb + 0.75f, pb.z), 0.035f, 4, wood);
                        b.Rod(new Vector3(pa.x, ya + 0.35f, pa.z), new Vector3(pb.x, yb + 0.35f, pb.z), 0.035f, 4, wood);
                        for (float t = 0.08f; t < 1f; t += 0.16f)
                        {
                            Vector3 pp = Vector3.Lerp(pa, pb, t);
                            float yy = Mathf.Lerp(ya, yb, t);
                            b.BoxRot(new Vector3(pp.x, yy - 0.3f, pp.z), new Vector3(0.09f, hgt + 0.3f, 0.04f), yaw, MB.Jitter(wood, r, 0.08f));
                            b.Push(new Vector3(pp.x, yy + hgt, pp.z), yaw);
                            b.Cone(Vector3.zero, 0.065f, 0.1f, 4, MB.Jitter(wood, r, 0.08f), Mathf.PI * 0.25f);
                            b.Pop();
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ people on houses

        static int nextGroup;

        static Vector3 HelpSourceFor(Vector3 p)
        {
            var pad = new Vector3(Valley.PadEdgeX - 12f, Valley.PadY, (Valley.PadZ0 + Valley.PadZ1) * 0.5f);
            var plateau = new Vector3(Valley.PlateauX1, Valley.PlateauY, 65f);
            var bridge = new Vector3(Valley.CenterX(Valley.BridgeZ), Valley.WaterLevel(Valley.BridgeZ) + 6f, Valley.BridgeZ);
            Vector3 best = pad; float bd = (p - pad).sqrMagnitude;
            if ((p - plateau).sqrMagnitude < bd) { best = plateau; bd = (p - plateau).sqrMagnitude; }
            if ((p - bridge).sqrMagnitude < bd) best = bridge;
            return best;
        }

        static Vector3 WaterNear(Vector3 p, System.Random r)
        {
            float cx = Valley.CenterX(p.z);
            float x = Mathf.Lerp(p.x, cx, 0.55f) + Rand(r, -6f, 6f), z = p.z + Rand(r, -12f, 12f);
            return new Vector3(x, Valley.WaterLevel(z), z);
        }

        static void PlanHousePeople(HouseSpec h, System.Random r)
        {
            Quaternion q = Quaternion.Euler(0f, h.yaw, 0f);
            if (h.roofPeople)
            {
                int n = r.Next(3, 7);
                float py = h.WallTop + 0.25f;
                int g = nextGroup++;
                // usable terrace: clear of the stair hut, water tank and solar panel
                float x0 = -h.w * 0.5f + 3.0f, x1 = h.w * 0.5f - 3.3f;
                Vector3 c = h.pos + q * new Vector3((x0 + x1) * 0.5f, py, 0f);
                Vector3 help = HelpSourceFor(h.pos);
                for (int i = 0; i < n; i++)
                {
                    personSpots.Add(new PersonSpot
                    {
                        pos = c, yaw = h.yaw + 180f, hint = SelVadisi.Beh.Stand,
                        kind = 0, loc = SpotLoc.Roof, group = g,
                        frameC = c, frameYaw = h.yaw, halfW = Mathf.Max(0.6f, (x1 - x0) * 0.5f), halfD = h.d * 0.5f - 0.75f,
                        help = help, water = WaterNear(h.pos, r)
                    });
                }
                if (r.NextDouble() < 0.5)
                    clothSpots.Add(new ClothSpot { pos = h.pos + q * new Vector3(Rand(r, -h.w * 0.3f, h.w * 0.1f), py + 1.0f, -h.d * 0.5f - 0.2f), yaw = h.yaw, flag = false, color = Pick(r, new[] { H("F4F1EA"), H("E9D9D0"), H("D84C3E"), H("F4F1EA") }), w = 1.3f, h = 1.7f });
            }
            else if (h.balconyPeople)
            {
                int f = h.floors - 1;
                float by = 0.8f + f * FH;
                int n = r.Next(1, 4);
                int g = nextGroup++;
                float bw = Mathf.Min(h.w * 0.62f, 5f);
                float railZ = -h.d * 0.5f - 1.3f + 0.05f;
                Vector3 help = HelpSourceFor(h.pos);
                for (int i = 0; i < n; i++)
                {
                    float x = n == 1 ? Rand(r, -0.5f, 0.5f) : Mathf.Lerp(-bw * 0.26f, bw * 0.26f, i / (float)(n - 1)) + Rand(r, -0.08f, 0.08f);
                    Vector3 lp = new Vector3(x, by + 0.02f, railZ + 0.42f + Rand(r, 0f, 0.08f));
                    personSpots.Add(new PersonSpot
                    {
                        pos = h.pos + q * lp, yaw = h.yaw + 180f + Rand(r, -15f, 15f), hint = SelVadisi.Beh.Stand,
                        kind = 0, loc = SpotLoc.Balcony, group = g,
                        hasRail = true, railA = h.pos + q * new Vector3(-bw * 0.5f, by, railZ), railB = h.pos + q * new Vector3(bw * 0.5f, by, railZ),
                        railN = q * Vector3.back, railTop = by + 1.035f,
                        help = help, water = WaterNear(h.pos, r)
                    });
                }
                if (r.NextDouble() < 0.55)
                    clothSpots.Add(new ClothSpot { pos = h.pos + q * new Vector3(Rand(r, -1.5f, 1.5f), by + 1.02f, -h.d * 0.5f - 1.27f), yaw = h.yaw, flag = false, color = Pick(r, new[] { H("F4F1EA"), H("F4F1EA"), H("E4B44A"), H("6C9BD2") }), w = 1.2f, h = 1.6f });
            }
            else if (h.bankPeople)
            {
                Vector3 lp = new Vector3(Rand(r, -2f, 2f), 0f, -h.d * 0.5f - Rand(r, 2.4f, 4f));
                Vector3 wp = h.pos + q * lp;
                wp.y = Valley.TerrainHeight(wp.x, wp.z);
                int g = nextGroup++;
                int n = r.NextDouble() < 0.45 ? 1 : r.Next(2, 4);
                Vector3 side = q * Vector3.right;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = wp + side * ((i - (n - 1) * 0.5f) * Rand(r, 0.75f, 1.1f));
                    p.y = Valley.TerrainHeight(p.x, p.z);
                    personSpots.Add(new PersonSpot { pos = p, yaw = h.yaw + 180f + Rand(r, -25f, 25f), hint = SelVadisi.Beh.Stand, kind = 0, loc = SpotLoc.Bank, group = n > 1 ? g : -1, help = HelpSourceFor(p), water = WaterNear(p, r) });
                }
            }
        }
    }
}
