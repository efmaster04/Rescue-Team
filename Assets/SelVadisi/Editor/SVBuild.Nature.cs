using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public static partial class SVBuild
    {
        static readonly Color32[] Conifer = { H("2F5A3A"), H("3A6B43"), H("2C5236"), H("41744A") };
        static readonly Color32[] Leafy = { H("5E8C3E"), H("6E9C46"), H("7FAA4F"), H("58833B"), H("8BB257") };
        static readonly Color32[] Autumn = { H("B9A23E"), H("C98B3A"), H("A8B14A") };
        static readonly Color32[] RockCols = { H("8E897E"), H("A29C90"), H("7B776E"), H("B1AB9F") };
        static readonly Color32 Bark = H("5C4433");

        static void BuildNature(ChunkSet cs)
        {
            var r = new System.Random(31337);
            Vector3 village = new Vector3(Valley.CenterX(0f), 0f, 0f);

            // ---- forests, orchards and lone trees on a jittered grid
            const float spacing = 8.5f;
            int planted = 0;
            for (float z = Valley.MinZ + 4f; z < Valley.MaxZ - 4f; z += spacing)
                for (float x = Valley.MinX + 4f; x < Valley.MaxX - 4f; x += spacing)
                {
                    float px = x + Rand(r, -2.8f, 2.8f), pz = z + Rand(r, -2.8f, 2.8f);
                    float e = Valley.EdgeDist(px, pz, out int side);
                    if (e < 2f) { r.NextDouble(); continue; }
                    float shelfEnd = 10f + Valley.ShelfWidth(pz, side);
                    float forest = Valley.S(-0.15f, 0.3f, Valley.Noise(px * 0.006f + 3f, pz * 0.006f - 7f));
                    float density;
                    if (e > shelfEnd + 4f) density = 0.08f + 0.66f * forest;      // hills
                    else if (e > 11f) density = 0.06f + 0.1f * forest;            // terraces
                    else density = 0.05f;                                          // bank slope
                    if (r.NextDouble() > density) continue;
                    if (InPlateau(px, pz, 3f) || InPad(px, pz, 4f) || NearRamp(px, pz, 4f) || Mathf.Abs(pz - Valley.BridgeZ) < 9f) continue;
                    if (NearStream(px, pz, 5f) || RoadDist(px, pz) < 4.5f || !FreeSpot(px, pz, 2.5f)) continue;
                    float y = Valley.TerrainHeight(px, pz);
                    if (y < Valley.WaterLevel(pz) + 0.4f) continue;
                    float slope = Mathf.Abs(Valley.TerrainHeight(px + 1.5f, pz) - Valley.TerrainHeight(px - 1.5f, pz)) + Mathf.Abs(Valley.TerrainHeight(px, pz + 1.5f) - Valley.TerrainHeight(px, pz - 1.5f));
                    if (slope > 4.2f) continue;
                    Vector3 p = new Vector3(px, y - 0.15f, pz);
                    bool near = (p - village).sqrMagnitude < 420f * 420f;
                    var mb = cs.At(p);
                    double roll = r.NextDouble();
                    if (e > shelfEnd + 4f)
                    {
                        float coniferChance = Mathf.Lerp(0.35f, 0.8f, Mathf.Clamp01((y - 20f) / 80f));
                        if (roll < coniferChance) TreeConifer(mb, p, Rand(r, 8f, 16f), r, near);
                        else TreeRound(mb, p, Rand(r, 6.5f, 11f), r, near);
                    }
                    else
                    {
                        if (roll < 0.22) TreePoplar(mb, p, Rand(r, 12f, 18f), r);
                        else if (roll < 0.75) TreeRound(mb, p, Rand(r, 5f, 9f), r, true);
                        else Bush(mb, p, Rand(r, 0.8f, 1.6f), r);
                    }
                    planted++;
                }

            // ---- trees around the dry houses (gardens) and in the flooded gardens
            foreach (var h in houses)
            {
                if (h.flooded) continue;
                var hr = new System.Random(h.seed ^ 0x5151);
                int n = hr.Next(0, 3);
                Quaternion q = Quaternion.Euler(0f, h.yaw, 0f);
                for (int i = 0; i < n; i++)
                {
                    Vector3 lp = new Vector3(Rand(hr, -1f, 1f) * (h.w * 0.5f + 3f), 0f, h.d * 0.5f + Rand(hr, 2.5f, 5f));
                    Vector3 p = h.pos + q * lp;
                    p.y = Valley.TerrainHeight(p.x, p.z) - 0.1f;
                    if (p.y < Valley.WaterLevel(p.z) + 0.3f) continue;
                    if (hr.NextDouble() < 0.3) TreePoplar(cs.At(p), p, Rand(hr, 11f, 16f), hr);
                    else TreeRound(cs.At(p), p, Rand(hr, 4.5f, 7.5f), hr, true, hr.NextDouble() < 0.4);
                }
            }
            foreach (var t in waterTrees)
            {
                if (r.NextDouble() < 0.25) TreePoplar(cs.At(t), t, Rand(r, 12f, 17f), r);
                else TreeRound(cs.At(t), t, Rand(r, 6f, 9.5f), r, true, r.NextDouble() < 0.35);
            }

            // ---- boulders: gorge, banks, slopes
            foreach (var g in gorgeRocks)
            {
                Vector3 p = new Vector3(g.x, Valley.TerrainHeight(g.x, g.z), g.z);
                Rock(cs.At(p), p, g.y, r);
            }
            for (int i = 0; i < 900; i++)
            {
                float x = Rand(r, Valley.MinX + 10f, Valley.MaxX - 10f), z = Rand(r, Valley.MinZ + 10f, Valley.MaxZ - 10f);
                float e = Valley.EdgeDist(x, z, out int side);
                if (e < -1f) continue;
                float slope = Mathf.Abs(Valley.TerrainHeight(x + 2f, z) - Valley.TerrainHeight(x - 2f, z)) + Mathf.Abs(Valley.TerrainHeight(x, z + 2f) - Valley.TerrainHeight(x, z - 2f));
                bool steep = slope > 3f;
                if (!steep && r.NextDouble() < 0.7) continue;
                if (InPlateau(x, z, 2f) || InPad(x, z, 2f) || NearRamp(x, z, 2f) || RoadDist(x, z) < 4f || !FreeSpot(x, z, 2f)) continue;
                Vector3 p = new Vector3(x, Valley.TerrainHeight(x, z), z);
                Rock(cs.At(p), p, steep ? Rand(r, 1.2f, 3.6f) : Rand(r, 0.5f, 1.6f), r);
            }
            // stream bed rocks
            for (int k = 2; k < Valley.Stream.Length - 2; k += 2)
            {
                Vector3 s = Valley.Stream[k];
                for (int j = 0; j < 2; j++)
                {
                    Vector3 p = s + new Vector3(Rand(r, -3.4f, 3.4f), 0f, Rand(r, -3.4f, 3.4f));
                    p.y = Valley.TerrainHeight(p.x, p.z);
                    Rock(cs.At(p), p, Rand(r, 0.5f, 1.3f), r);
                }
            }

            // ---- grass tufts and wild flowers on the terraces
            // reeds and shrubs along the flooded banks
            for (int i = 0; i < 2600; i++)
            {
                float z = Rand(r, -620f, 600f);
                int side = r.NextDouble() < 0.5 ? -1 : 1;
                float x = Valley.SideX(z, side, Rand(r, -0.5f, 9f));
                if (InPad(x, z, 1f) || NearRamp(x, z, 2f) || InPlateau(x, z, 0f) || RoadDist(x, z) < 3.5f || !FreeSpot(x, z, 0.6f) || Mathf.Abs(z - Valley.BridgeZ) < 8f) continue;
                Vector3 p = new Vector3(x, Valley.TerrainHeight(x, z), z);
                float above = p.y - Valley.WaterLevel(z);
                if (above < -0.3f) continue;
                var mb = cs.At(p);
                if (above < 1.4f) Reeds(mb, p, r);
                else if (r.NextDouble() < 0.35) Bush(mb, p, Rand(r, 0.5f, 1.1f), r);
                else Tuft(mb, p, r);
            }
            // meadow detail on the slopes around the village
            for (int i = 0; i < 7000; i++)
            {
                float z = Rand(r, -420f, 460f);
                int side = r.NextDouble() < 0.5 ? -1 : 1;
                float e = Rand(r, 10f + Valley.ShelfWidth(z, side), 10f + Valley.ShelfWidth(z, side) + 160f);
                float x = Valley.SideX(z, side, e);
                if (x < Valley.MinX + 5f || x > Valley.MaxX - 5f || NearStream(x, z, 3f) || RoadDist(x, z) < 3f || !FreeSpot(x, z, 0.3f)) continue;
                Vector3 p = new Vector3(x, Valley.TerrainHeight(x, z), z);
                float slope = Mathf.Abs(Valley.TerrainHeight(x + 1f, z) - p.y) + Mathf.Abs(Valley.TerrainHeight(x, z + 1f) - p.y);
                if (slope > 1.4f) continue;
                var mb = cs.At(p);
                double roll = r.NextDouble();
                if (roll < 0.12) Flower(mb, p, r);
                else if (roll < 0.17) Bush(mb, p, Rand(r, 0.5f, 1.0f), r);
                else Tuft(mb, p, r);
            }
            for (int i = 0; i < 9000; i++)
            {
                float z = Rand(r, -640f, 640f);
                int side = r.NextDouble() < 0.5 ? -1 : 1;
                float e = Rand(r, 1.5f, 10f + Valley.ShelfWidth(z, side) + 12f);
                float x = Valley.SideX(z, side, e);
                if (InPlateau(x, z, 0f) || InPad(x, z, 0f) || RoadDist(x, z) < 3.2f || !FreeSpot(x, z, 0.3f)) continue;
                Vector3 p = new Vector3(x, Valley.TerrainHeight(x, z), z);
                if (p.y < Valley.WaterLevel(z) + 0.5f) continue;
                var mb = cs.At(p);
                if (r.NextDouble() < 0.16) Flower(mb, p, r);
                else Tuft(mb, p, r);
            }
        }

        static System.Func<Vector3, float> HeightWind(Vector3 basePos, float h, float k = 1f)
            => w => Mathf.Clamp01((w.y - basePos.y) / h) * k;

        static void TreeConifer(MB b, Vector3 p, float h, System.Random r, bool near = true)
        {
            b.WindFn = HeightWind(p, h, 0.55f);
            b.Taper(p - Vector3.up * 0.4f, p + Vector3.up * (h * 0.45f), 0.14f + h * 0.012f, 0.14f + h * 0.012f, 0.05f, 0.05f, 6, Bark, false, false);
            b.Taper(p - Vector3.up * 0.2f, p + Vector3.up * 0.5f, 0.3f + h * 0.012f, 0.3f + h * 0.012f, 0.13f + h * 0.012f, 0.13f + h * 0.012f, 6, MB.Shade(Bark, 0.9f), false, false);
            Color32 g = Pick(r, Conifer);
            int tiers = near ? r.Next(4, 6) : r.Next(3, 5);
            float rot = Rand(r, 0f, 6.28f);
            for (int k = 0; k < tiers; k++)
            {
                float t = (float)k / tiers;
                float baseY = p.y + h * (0.16f + t * 0.66f);
                float rad = h * (0.31f - t * 0.2f) * Rand(r, 0.9f, 1.1f);
                float ch = h * (0.36f - t * 0.08f);
                Vector3 c = new Vector3(p.x + Rand(r, -0.05f, 0.05f) * h * 0.1f, baseY, p.z + Rand(r, -0.05f, 0.05f) * h * 0.1f);
                b.ConeAO(c, rad, ch, near ? 8 : 6, MB.Jitter(MB.Shade(g, 0.92f + t * 0.16f), r, 0.04f), rot + k * 0.55f, rad * 0.22f, 0.32f);
            }
            b.Cone(new Vector3(p.x, p.y + h * 0.86f, p.z), h * 0.05f, h * 0.18f, 5, MB.Shade(g, 1.15f), rot);
            b.WindFn = null;
        }

        static void TreeRound(MB b, Vector3 p, float h, System.Random r, bool detailed, bool fruit = false)
        {
            b.WindFn = HeightWind(p, h, 0.75f);
            Vector3 lean = new Vector3(Rand(r, -0.35f, 0.35f), 0f, Rand(r, -0.35f, 0.35f));
            Vector3 crown = p + Vector3.up * (h * 0.66f) + lean;
            float tr = 0.1f + h * 0.02f;
            b.Taper(p - Vector3.up * 0.3f, p + Vector3.up * 0.6f, tr * 1.7f, tr * 1.7f, tr, tr, 6, MB.Shade(Bark, 0.9f), false, false);
            b.Taper(p + Vector3.up * 0.55f, crown - Vector3.up * h * 0.12f, tr, tr, tr * 0.6f, tr * 0.6f, 6, Bark, false, false);
            Color32 g = Pick(r, Leafy);
            bool autumn = r.NextDouble() < 0.1;
            Color32 g2 = autumn ? Pick(r, Autumn) : MB.Shade(g, 1.08f);
            float R = h * 0.3f;
            int clusters = detailed ? r.Next(6, 9) : r.Next(4, 6);
            // branches to the main clusters
            var centres = new System.Collections.Generic.List<Vector3>();
            centres.Add(crown + Vector3.up * R * 0.15f);
            for (int k = 1; k < clusters; k++)
            {
                float ang = k * 2.399f + Rand(r, -0.3f, 0.3f);
                float el = Rand(r, -0.45f, 0.75f);
                Vector3 d = new Vector3(Mathf.Cos(ang) * Mathf.Sqrt(1f - el * el), el, Mathf.Sin(ang) * Mathf.Sqrt(1f - el * el));
                centres.Add(crown + Vector3.Scale(d, new Vector3(R * 0.72f, R * 0.55f, R * 0.72f)));
            }
            for (int k = 1; k < Mathf.Min(4, centres.Count); k++)
                b.Taper(crown - Vector3.up * h * 0.14f, Vector3.Lerp(crown, centres[k], 0.75f), tr * 0.55f, tr * 0.55f, tr * 0.2f, tr * 0.2f, 4, Bark, false, false);
            for (int k = 0; k < centres.Count; k++)
            {
                Vector3 off = centres[k] - crown;
                float s = R * (k == 0 ? 0.8f : Rand(r, 0.5f, 0.68f));
                float tint = 0.86f + 0.24f * Mathf.Clamp01(off.y / R + 0.5f);
                Color32 cc = MB.Shade(MB.Jitter((k % 3 == 1) ? g2 : g, r, 0.05f), tint);
                b.IcoAO(centres[k], new Vector3(s * 1.1f, s * 0.95f, s * 1.1f), cc, detailed ? 1 : 0, 0.14f, r, 0.4f, 0.12f);
            }
            if (fruit)
            {
                Color32 fc = r.NextDouble() < 0.5 ? H("D9473B") : H("E8B83A");
                for (int k = 0; k < 12; k++)
                {
                    Vector3 c = centres[r.Next(centres.Count)];
                    Vector3 d = new Vector3(Rand(r, -1f, 1f), Rand(r, -0.6f, 0.7f), Rand(r, -1f, 1f)).normalized * R * 0.62f;
                    b.Ico(c + d, new Vector3(0.12f, 0.12f, 0.12f), fc, 0, 0f, r);
                }
            }
            b.WindFn = null;
        }

        static void TreePoplar(MB b, Vector3 p, float h, System.Random r)
        {
            b.WindFn = HeightWind(p, h, 0.9f);
            b.Taper(p - Vector3.up * 0.3f, p + Vector3.up * h * 0.6f, 0.2f, 0.2f, 0.07f, 0.07f, 6, Bark, false, false);
            Color32 g = MB.Jitter(H("6A9443"), r, 0.07f);
            int n = 5;
            for (int k = 0; k < n; k++)
            {
                float t = (float)k / (n - 1);
                float y = Mathf.Lerp(0.3f, 0.86f, t) * h;
                float rad = h * Mathf.Lerp(0.12f, 0.06f, t) * Rand(r, 0.9f, 1.1f);
                Vector3 c = p + new Vector3(Rand(r, -0.15f, 0.15f), y, Rand(r, -0.15f, 0.15f));
                b.IcoAO(c, new Vector3(rad, h * 0.13f, rad), MB.Shade(g, 0.9f + t * 0.25f), 1, 0.12f, r, 0.35f, 0.1f);
            }
            b.WindFn = null;
        }

        static void Bush(MB b, Vector3 p, float s, System.Random r)
        {
            b.WindFn = HeightWind(p, s * 2f, 0.35f);
            Color32 g = Pick(r, Leafy);
            int n = r.Next(2, 5);
            for (int k = 0; k < n; k++)
                b.IcoAO(p + new Vector3(Rand(r, -s, s) * 0.6f, s * Rand(r, 0.35f, 0.65f), Rand(r, -s, s) * 0.6f), new Vector3(s, s * 0.78f, s) * Rand(r, 0.7f, 1f), MB.Jitter(g, r, 0.07f), 1, 0.18f, r, 0.45f, 0.1f);
            if (r.NextDouble() < 0.3)
                for (int k = 0; k < 6; k++)
                    b.Ico(p + new Vector3(Rand(r, -s, s) * 0.8f, s * Rand(r, 0.7f, 1.2f), Rand(r, -s, s) * 0.8f), Vector3.one * 0.07f, Pick(r, new[] { H("F4F2EE"), H("E8D24A"), H("D9473B") }), 0, 0f, r);
            b.WindFn = null;
        }

        static void Rock(MB b, Vector3 p, float s, System.Random r)
        {
            Color32 c = MB.Jitter(Pick(r, RockCols), r, 0.06f);
            float g = Mathf.Min(Mathf.Min(Valley.TerrainHeight(p.x + s, p.z), Valley.TerrainHeight(p.x - s, p.z)), Mathf.Min(Valley.TerrainHeight(p.x, p.z + s), Valley.TerrainHeight(p.x, p.z - s)));
            p.y = Mathf.Min(p.y, Mathf.Lerp(g, p.y, 0.45f));
            b.IcoAO(p + Vector3.up * s * 0.1f, new Vector3(s * Rand(r, 1.1f, 1.6f), s * Rand(r, 0.6f, 0.95f), s * Rand(r, 0.9f, 1.3f)), c, s > 1.2f ? 1 : 0, 0.22f, r, 0.35f, 0.12f);
            if (s > 1f && r.NextDouble() < 0.5) b.IcoAO(p + new Vector3(s * 0.4f, s * 0.55f, s * 0.1f), new Vector3(s * 0.55f, s * 0.14f, s * 0.5f), MB.Jitter(H("6E8F4A"), r, 0.08f), 0, 0.2f, r, 0.2f, 0.1f);
        }

        static void Tuft(MB b, Vector3 p, System.Random r)
        {
            Color32 g = MB.Jitter(Pick(r, new[] { H("7FAA4F"), H("8DB35A"), H("6E9A49"), H("9DBB63") }), r, 0.06f);
            float h = Rand(r, 0.35f, 0.75f);
            b.WindFn = HeightWind(p, h, 0.45f);
            for (int k = 0; k < 3; k++)
            {
                float a = Rand(r, 0f, Mathf.PI * 2f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 side = new Vector3(-d.z, 0f, d.x) * 0.07f;
                Vector3 tip = p + d * h * 0.35f + Vector3.up * h;
                b.Tri(p - side, tip, p + side, g, d);
                b.Tri(p - side, tip, p + side, MB.Shade(g, 0.85f), -d);
            }
            b.WindFn = null;
        }

        static void Reeds(MB b, Vector3 p, System.Random r)
        {
            Color32 g = MB.Jitter(Pick(r, new[] { H("7E9A4E"), H("8FA65A"), H("6E8C45"), H("A5A866") }), r, 0.06f);
            float h = Rand(r, 0.9f, 1.7f);
            b.WindFn = HeightWind(p, h, 0.6f);
            int n = r.Next(4, 8);
            for (int k = 0; k < n; k++)
            {
                float a = Rand(r, 0f, Mathf.PI * 2f);
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(r, 0f, 0.3f);
                Vector3 d = new Vector3(Mathf.Cos(a + 1.57f), 0f, Mathf.Sin(a + 1.57f));
                Vector3 bp = p + off, tip = bp + off * 0.6f + Vector3.up * h * Rand(r, 0.75f, 1.05f);
                b.Tri(bp - d * 0.035f, tip, bp + d * 0.035f, g, Vector3.Cross(d, Vector3.up));
                b.Tri(bp - d * 0.035f, tip, bp + d * 0.035f, MB.Shade(g, 0.8f), -Vector3.Cross(d, Vector3.up));
                if (r.NextDouble() < 0.35) b.Ico(tip - Vector3.up * 0.12f, new Vector3(0.035f, 0.11f, 0.035f), H("6B4A33"), 0, 0f, r);
            }
            b.WindFn = null;
        }

        static void Flower(MB b, Vector3 p, System.Random r)
        {
            Color32 c = Pick(r, new[] { H("F2E14C"), H("F4F2EE"), H("E0484A"), H("C77DD6"), H("F29B38") });
            b.WindFn = HeightWind(p, 0.5f, 0.4f);
            b.Rod(p, p + Vector3.up * 0.4f, 0.015f, 3, H("5E8C3E"), -1f, false);
            b.Ico(p + Vector3.up * 0.42f, new Vector3(0.08f, 0.05f, 0.08f), c, 0, 0f, r);
            b.WindFn = null;
        }
    }
}
