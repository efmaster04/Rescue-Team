using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public class HouseSpec
    {
        public Vector3 pos;
        public float yaw, w, d;
        public int floors, roof, side, row, seed; // roof: 0 gable, 1 hip, 2 flat terrace
        public bool flooded, quoins, balconies, shutters, shop, stairs, garden, solar, chimney, stoneHouse, awning, cumba, timber;
        public bool roofPeople, balconyPeople, bankPeople;   // decided before the house is built (people need clear space)
        public int lintel; // 0 cornice, 1 keystone arch, 2 plain
        public Color32 wall, roofCol, shutter, door, stone, awningCol, frame, arch, gutter;
        public float WallTop => 0.8f + floors * SVBuild.FH;
        public float Radius => 0.5f * Mathf.Sqrt(w * w + d * d);
    }

    public enum SpotLoc { Roof, Balcony, Plateau, Tent, Pad, Bridge, Bank }

    /// Where a person stands and what is around them. Spots spawned together share a group (a household, a tent family,
    /// a cluster of neighbours) and are staged together by the cast.
    public struct PersonSpot
    {
        public Vector3 pos;
        public float yaw;
        public SelVadisi.Beh hint;          // behaviour suggested by the planner (Stand = no preference)
        public int kind;                    // 0 villager, 1 rescue worker, 2 child, 3 woman (headscarf likely)
        public SpotLoc loc;
        public int group;                   // -1 = alone
        // roof frame (households): centre on the terrace floor, yaw, usable half extents
        public Vector3 frameC; public float frameYaw, halfW, halfD;
        // anchors
        public bool hasRail; public Vector3 railA, railB, railN; public float railTop; public bool bridge;
        public bool hasSeat; public Vector3 seat, seatFwd; public float seatH;
        public bool hasKnob; public Vector3 knob;
        public Vector3[] lane;
        public Vector3 help;                // where help comes from (rescue pad, bank, bridge)
        public Vector3 water;               // a point on the water this person watches
    }

    public struct ClothSpot
    {
        public Vector3 pos;
        public float yaw;
        public bool flag;
        public Color32 color;
        public float w, h;
    }

    public struct Circle { public float x, z, r; public Circle(float x, float z, float r) { this.x = x; this.z = z; this.r = r; } }

    public static partial class SVBuild
    {
        public const float FH = 3f;
        public const float MosqueZ = -62f, SchoolZ = -150f;
        static Vector3 mosquePos, schoolPos;

        static readonly List<HouseSpec> houses = new List<HouseSpec>();
        static readonly List<FlowObstacle> obstacles = new List<FlowObstacle>();
        static readonly List<PersonSpot> personSpots = new List<PersonSpot>();
        static readonly List<ClothSpot> clothSpots = new List<ClothSpot>();
        static readonly List<Circle> reserved = new List<Circle>();
        static readonly List<Vector3> waterTrees = new List<Vector3>();
        static readonly List<Vector3> gorgeRocks = new List<Vector3>();
        static readonly List<List<Vector3>> poleLines = new List<List<Vector3>>();
        static readonly List<Vector3> lampSpots = new List<Vector3>();

        // palette
        static readonly Color32[] WallCols = {
            H("3E7DC9"), H("E6C24D"), H("DE8F62"), H("9FC9AF"), H("EDE3CC"), H("CDA2C0"),
            H("D9705F"), H("8FBADB"), H("F2EBDD"), H("B7D07A"), H("E8A87C"), H("C9B08A"), H("4FA3A0"), H("F0D6A0") };
        static readonly Color32[] RoofCols = { H("C55A3B"), H("B84F34"), H("D06C47"), H("A9462F"), H("C4643E"), H("B9573A") };
        static readonly Color32[] StoneCols = { H("A39C8E"), H("8F887B"), H("B8B0A0"), H("9A9384"), H("ADA595") };
        static readonly Color32[] ShutterCols = { H("7B5539"), H("4E7B52"), H("3D6E8F"), H("E8E4DA"), H("8B3A2E"), H("5C6B3A") };
        static readonly Color32[] DoorCols = { H("6B4630"), H("5A3B2A"), H("3F5E7A"), H("7A2E26"), H("4B5A3A") };
        static readonly Color32[] AwningCols = { H("C8372D"), H("2F6DB0"), H("2E8A4F"), H("E39A2B") };
        static readonly Color32 Trim = H("F3EFE6"), Glass = H("2F4858"), Iron = H("34383B"), FlatRoof = H("8E9496"), Concrete = H("B9B5AC");

        // ------------------------------------------------------------------ planning

        static bool FreeSpot(float x, float z, float r)
        {
            foreach (var c in reserved)
            {
                float dx = x - c.x, dz = z - c.z, rr = r + c.r;
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            return true;
        }

        static bool NearRamp(float x, float z, float margin)
        {
            Valley.RampLocal(x, z, out float s, out float lat, out _);
            return Mathf.Abs(lat) < Valley.RampHalfWidth + margin && s > -margin && s < Valley.RampLength + margin;
        }

        static bool InPlateau(float x, float z, float m) =>
            x > Valley.PlateauX0 - m && x < Valley.PlateauX1 + m && z > Valley.PlateauZ0 - 16f - m && z < Valley.PlateauZ1 + 16f + m;

        static bool InPad(float x, float z, float m) =>
            x > Valley.PadX0 - m && x < Valley.PadX1 + m && z > Valley.PadZ0 - m && z < Valley.PadZ1 + m;

        static bool NearStream(float x, float z, float m)
        {
            Valley.StreamNearest(x, z, out float d, out _, out _);
            return d < m;
        }

        static void Plan()
        {
            houses.Clear(); obstacles.Clear(); personSpots.Clear(); clothSpots.Clear(); reserved.Clear(); nextGroup = 0;
            waterTrees.Clear(); gorgeRocks.Clear(); poleLines.Clear(); lampSpots.Clear();
            var r = new System.Random(4242);

            mosquePos = new Vector3(Valley.SideX(MosqueZ, 1, 24f), 0f, MosqueZ);
            mosquePos.y = MinHeight(mosquePos.x, mosquePos.z, 9f, 9f, 0f);
            schoolPos = new Vector3(Valley.SideX(SchoolZ, -1, 33f), 0f, SchoolZ);
            schoolPos.y = MinHeight(schoolPos.x, schoolPos.z, 17f, 8f, 0f);
            reserved.Add(new Circle(mosquePos.x, mosquePos.z, 17f));
            reserved.Add(new Circle(schoolPos.x, schoolPos.z, 26f));

            // ---- flooded village: three rows each side of the old river channel
            float[] rows = { 28f, 50f, 70f };
            for (int side = -1; side <= 1; side += 2)
                for (int ri = 0; ri < rows.Length; ri++)
                    for (float z = -236f + ri * 7f + (side > 0 ? 4f : 0f); z <= 238f; z += 20.5f + (float)r.NextDouble() * 3f)
                    {
                        if (r.NextDouble() < 0.16) continue;
                        float zz = z + Rand(r, -2.5f, 2.5f);
                        float x = Valley.CenterX(zz) + side * (rows[ri] + Rand(r, -2f, 2f));
                        if (NearRamp(x, zz, 12f)) continue;
                        var h = NewHouse(r, x, zz, side, true, ri);
                        if (!FreeSpot(x, zz, h.Radius + 1.5f)) continue;
                        AddHouse(h);
                    }

            // ---- dry houses on the terraces above the flood
            for (int side = -1; side <= 1; side += 2)
                for (int pass = 0; pass < 2; pass++)
                    for (float z = -560f + pass * 9f; z <= 620f; z += 17f + (float)r.NextDouble() * 9f)
                    {
                        bool village = z > -330f && z < 340f;
                        if (!village && r.NextDouble() < 0.72) continue;
                        if (z > 440f && side > 0) continue;
                        float shelf = Valley.ShelfWidth(z, side);
                        float minE = pass == 0 ? 21f : 36f, maxE = pass == 0 ? 31f : 10f + shelf - 7f;
                        if (maxE < minE) continue;
                        float e = Rand(r, minE, maxE);
                        float x = Valley.SideX(z, side, e);
                        if (InPlateau(x, z, 8f) || InPad(x, z, 10f) || Mathf.Abs(z - Valley.BridgeZ) < 20f || NearStream(x, z, 16f)) continue;
                        var h = NewHouse(r, x, z, side, false, pass);
                        if (!FreeSpot(x, z, h.Radius + 3f)) continue;
                        if (!FitsOnLand(h)) continue;
                        AddHouse(h);
                    }

            // ---- power line through the flooded west street, street lamps along the east street
            var lineA = new List<Vector3>();
            for (float z = -250f; z <= 250f; z += 34f + (float)r.NextDouble() * 6f)
            {
                float x = Valley.CenterX(z) - 39f + Rand(r, -1.5f, 1.5f);
                if (NearRamp(x, z, 4f)) continue;
                lineA.Add(new Vector3(x, Valley.TerrainHeight(x, z), z));
                obstacles.Add(new FlowObstacle(x, z, 0.7f, 0.7f));
            }
            poleLines.Add(lineA);
            for (float z = -230f; z <= 230f; z += 30f)
            {
                float x = Valley.CenterX(z) + 39f;
                if (!FreeSpot(x, z, 2f)) continue;
                lampSpots.Add(new Vector3(x, Valley.TerrainHeight(x, z), z));
                obstacles.Add(new FlowObstacle(x, z, 0.5f, 0.5f));
            }

            // ---- trees standing in the flooded gardens
            for (int i = 0; i < 420 && waterTrees.Count < 80; i++)
            {
                float z = Rand(r, -240f, 240f);
                int side = r.NextDouble() < 0.5 ? -1 : 1;
                float x = Valley.CenterX(z) + side * Rand(24f, 80f, r);
                if (!FreeSpot(x, z, 3.5f) || NearRamp(x, z, 3f)) continue;
                if (Valley.Depth(x, z) < 0.2f) continue;
                waterTrees.Add(new Vector3(x, Valley.TerrainHeight(x, z), z));
                reserved.Add(new Circle(x, z, 2.5f));
                obstacles.Add(new FlowObstacle(x, z, 1.1f, 0.8f));
            }

            // ---- boulders in the upstream gorge (rapids)
            for (int i = 0; i < 200 && gorgeRocks.Count < 34; i++)
            {
                float z = Rand(r, 430f, 640f);
                float x = Valley.CenterX(z) + Rand(r, -0.8f, 0.8f) * Valley.HalfWidth(z);
                float s = Rand(r, 1.4f, 3.4f);
                if (!FreeSpot(x, z, s + 2f)) continue;
                gorgeRocks.Add(new Vector3(x, s, z));
                reserved.Add(new Circle(x, z, s));
                obstacles.Add(new FlowObstacle(x, z, s * 0.9f, 1f));
            }

            // ---- bridge piers
            foreach (float px in BridgePierXs()) obstacles.Add(new FlowObstacle(px, Valley.BridgeZ, 2.6f, 1.3f));

            // ---- submerged end of the ramp
            float L = Valley.RampLength;
            for (float s = 0f; s <= L; s += 3.5f)
            {
                float t = s / L;
                Vector3 p = Vector3.Lerp(Valley.RampTop, Valley.RampBottom, t);
                if (Valley.Depth(p.x, p.z) > -0.5f) obstacles.Add(new FlowObstacle(p.x, p.z, Valley.RampHalfWidth + 0.8f, 0.9f));
            }

            PlanPowerLines(r);
        }

        static float Rand(float a, float b, System.Random r) => Rand(r, a, b);

        static float MinHeight(float x, float z, float hw, float hd, float yaw)
        {
            float mn = Valley.TerrainHeight(x, z);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                {
                    Vector3 p = new Vector3(x, 0f, z) + q * new Vector3(i * hw, 0f, j * hd);
                    mn = Mathf.Min(mn, Valley.TerrainHeight(p.x, p.z));
                }
            return mn;
        }

        static bool FitsOnLand(HouseSpec h)
        {
            Quaternion q = Quaternion.Euler(0f, h.yaw, 0f);
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    Vector3 p = h.pos + q * new Vector3(i * (h.w * 0.5f + 1f), 0f, j * (h.d * 0.5f + 1f));
                    float y = Valley.TerrainHeight(p.x, p.z);
                    if (Valley.WaterLevel(p.z) - y > -0.6f) return false;
                    mn = Mathf.Min(mn, y); mx = Mathf.Max(mx, y);
                }
            return mx - mn < 2.6f;
        }

        static HouseSpec NewHouse(System.Random r, float x, float z, int side, bool flooded, int row)
        {
            var h = new HouseSpec { side = side, row = row, flooded = flooded, seed = r.Next() };
            h.w = Rand(r, 8.5f, 12.5f);
            h.d = Rand(r, 8f, 11f);
            if (flooded) h.floors = row == 2 ? r.Next(1, 4) : r.Next(2, 4);
            else h.floors = r.Next(1, 4);
            if (h.floors == 1 && r.NextDouble() < 0.5) h.floors = 2;
            float flatChance = flooded ? (row < 2 ? 0.45f : 0.25f) : 0.18f;
            h.roof = r.NextDouble() < flatChance ? 2 : (r.NextDouble() < 0.5 ? 0 : 1);
            h.yaw = side * 90f + Rand(r, -11f, 11f);
            h.wall = Pick(r, WallCols);
            h.roofCol = Pick(r, RoofCols);
            h.stone = Pick(r, StoneCols);
            h.shutter = Pick(r, ShutterCols);
            h.door = Pick(r, DoorCols);
            h.awningCol = Pick(r, AwningCols);
            h.stoneHouse = r.NextDouble() < 0.12;
            h.quoins = h.stoneHouse || r.NextDouble() < 0.4;
            h.balconies = r.NextDouble() < 0.65;
            h.shutters = r.NextDouble() < 0.6;
            h.shop = !flooded && row == 0 && r.NextDouble() < 0.3;
            h.awning = h.shop || (flooded && r.NextDouble() < 0.12);
            h.stairs = r.NextDouble() < 0.18 && h.floors >= 2;
            h.garden = r.NextDouble() < (flooded ? 0.4 : 0.55);
            h.solar = r.NextDouble() < 0.45;
            h.chimney = r.NextDouble() < 0.6;
            h.timber = !h.stoneHouse && h.floors >= 2 && r.NextDouble() < 0.2;
            h.cumba = h.floors >= 2 && h.w >= 9.5f && r.NextDouble() < 0.3;
            if (h.cumba) h.balconies = false;
            if (h.timber) { h.wall = r.NextDouble() < 0.7 ? H("EFE6D2") : H("E8DCC0"); h.quoins = false; }
            h.lintel = h.stoneHouse ? 1 : r.Next(3);
            h.frame = r.NextDouble() < 0.7 ? H("F4F1EA") : (r.NextDouble() < 0.5 ? H("7A5234") : H("5E6B5A"));
            h.arch = h.stoneHouse || r.NextDouble() < 0.3 ? MB.Shade(h.stone, 1.08f) : Trim;
            h.gutter = r.NextDouble() < 0.7 ? H("8D9396") : H("A0643A");
            h.pos = new Vector3(x, 0f, z);
            h.pos.y = MinHeight(x, z, h.w * 0.5f, h.d * 0.5f, h.yaw) - (flooded ? 0.1f : 0.25f);
            return h;
        }

        static void AddHouse(HouseSpec h)
        {
            houses.Add(h);
            reserved.Add(new Circle(h.pos.x, h.pos.z, h.Radius + (h.garden ? 2.5f : 0.8f)));
            if (Valley.Depth(h.pos.x, h.pos.z) > -0.3f)
                obstacles.Add(new FlowObstacle(h.pos.x, h.pos.z, h.Radius * 0.82f, 1f));
        }

        public static float[] BridgePierXs()
        {
            float cx = Valley.CenterX(Valley.BridgeZ), W = Valley.HalfWidth(Valley.BridgeZ);
            float x0 = cx - W - 2f, x1 = cx + W + 2f;
            int spans = 6;
            var xs = new float[spans - 1];
            for (int k = 1; k < spans; k++) xs[k - 1] = Mathf.Lerp(x0, x1, (float)k / spans);
            return xs;
        }

        // ------------------------------------------------------------------ terrain

        static readonly Color32 G1 = H("76A04E"), G2 = H("92B463"), G3 = H("648C46"), GDry = H("A9AE6C");
        static readonly Color32 R1 = H("8E897E"), R2 = H("A7A196"), R3 = H("736F67");
        static readonly Color32 Mud = H("7D6447"), MudDeep = H("5E4A37"), Gravel = H("A29C90"), FieldA = H("8FAE58"), FieldB = H("B9A65E");

        static Color32 TerrainColor(float x, float y, float z, float ny)
        {
            float wl = Valley.WaterLevel(z);
            float above = y - wl;
            float n1 = Valley.Noise(x * 0.035f, z * 0.035f) * 0.5f + 0.5f;
            float n2 = Valley.Noise(x * 0.11f + 50f, z * 0.11f) * 0.5f + 0.5f;
            float n3 = Valley.Noise(x * 0.012f - 20f, z * 0.012f + 9f) * 0.5f + 0.5f;
            Color32 grass = MB.Lerp(MB.Lerp(G1, G2, n1), G3, n2 * 0.45f);
            grass = MB.Lerp(grass, GDry, Mathf.Clamp01((y - 70f) / 90f) * 0.6f * n3);
            Color32 rock = MB.Lerp(MB.Lerp(R1, R2, n2), R3, n1 * 0.6f);
            float rockAmt = Valley.S(0.78f, 0.6f, ny);
            Color32 c = MB.Lerp(grass, rock, rockAmt);

            // fields on the wide downstream terraces
            Valley.EdgeDist(x, z, out int side);
            float e = Valley.EdgeDist(x, z, out side);
            if (z < -360f && e > 12f && e < 10f + Valley.ShelfWidth(z, side) && ny > 0.9f)
            {
                float stripe = Mathf.Repeat((z + side * x * 0.15f) * 0.22f, 1f);
                Color32 field = (Mathf.FloorToInt(z / 38f + x / 51f) & 1) == 0 ? FieldA : FieldB;
                c = MB.Lerp(c, MB.Shade(field, stripe < 0.5f ? 1f : 0.9f), 0.85f);
            }

            if (InPad(x, z, -1f)) c = MB.Lerp(c, Gravel, 0.9f);
            if (x > Valley.PlateauX0 && x < Valley.PlateauX1 && z > Valley.PlateauZ0 && z < Valley.PlateauZ1) c = H("8A877F");

            float mud = 1f - Valley.S(0.3f, 1.9f, above);
            c = MB.Lerp(c, Mud, mud);
            if (above < -0.2f) c = MB.Lerp(Mud, MudDeep, Mathf.Clamp01(-above / 2f));
            return c;
        }

        static void BuildTerrain(Transform parent)
        {
            const float cell = 4f;
            int nx = Mathf.RoundToInt((Valley.MaxX - Valley.MinX) / cell);
            int nz = Mathf.RoundToInt((Valley.MaxZ - Valley.MinZ) / cell);
            int sx = nx + 1;
            var P = new Vector3[sx * (nz + 1)];
            var rj = new System.Random(77);
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = Valley.MinX + i * cell, z = Valley.MinZ + j * cell;
                    if (i > 0 && i < nx && j > 0 && j < nz) { x += Rand(rj, -1.1f, 1.1f); z += Rand(rj, -1.1f, 1.1f); }
                    else { rj.NextDouble(); rj.NextDouble(); }
                    P[j * sx + i] = new Vector3(x, Valley.TerrainHeight(x, z), z);
                }
            var C = new Color32[P.Length];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    Vector3 dx = P[j * sx + Mathf.Min(i + 1, nx)] - P[j * sx + Mathf.Max(i - 1, 0)];
                    Vector3 dz = P[Mathf.Min(j + 1, nz) * sx + i] - P[Mathf.Max(j - 1, 0) * sx + i];
                    Vector3 n = Vector3.Cross(dz, dx).normalized;
                    if (n.y < 0f) n = -n;
                    Vector3 p = P[j * sx + i];
                    C[j * sx + i] = MB.Jitter(TerrainColor(p.x, p.y, p.z, n.y), rj, 0.035f);
                }

            const int chunk = 50;
            for (int cj = 0; cj < nz; cj += chunk)
                for (int ci = 0; ci < nx; ci += chunk)
                {
                    int i1 = Mathf.Min(ci + chunk, nx), j1 = Mathf.Min(cj + chunk, nz);
                    var mb = new MB();
                    int w = i1 - ci + 1;
                    for (int j = cj; j <= j1; j++)
                        for (int i = ci; i <= i1; i++)
                            mb.Vert(P[j * sx + i], C[j * sx + i]);
                    for (int j = 0; j < j1 - cj; j++)
                        for (int i = 0; i < i1 - ci; i++)
                        {
                            int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
                            if (((i + ci + j + cj) & 1) == 0) { mb.Tri(a, c, d); mb.Tri(a, d, b); }
                            else { mb.Tri(a, c, b); mb.Tri(b, c, d); }
                        }
                    string name = $"Arazi_{ci / chunk}_{cj / chunk}";
                    var mesh = SaveMesh(mb.ToMeshPacked(name), name);
                    MeshObject(name, parent, mesh, matStatic, true, true);
                }
        }

        // ------------------------------------------------------------------ flood water + stream

        static void BuildWater(Transform parent)
        {
            const float cell = 2.5f;
            int nx = Mathf.RoundToInt((Valley.MaxX - Valley.MinX) / cell);
            int nz = Mathf.RoundToInt((Valley.MaxZ - Valley.MinZ) / cell);
            int sx = nx + 1;
            var hgt = new float[sx * (nz + 1)];
            for (int j = 0; j <= nz; j++)
            {
                float z = Valley.MinZ + j * cell;
                float cx = Valley.CenterX(z), hw = Valley.HalfWidth(z);
                for (int i = 0; i <= nx; i++)
                {
                    float x = Valley.MinX + i * cell;
                    hgt[j * sx + i] = Mathf.Abs(x - cx) > hw + 45f ? 999f : Valley.TerrainHeight(x, z);
                }
            }
            const int chunk = 64;
            var map = new int[(chunk + 1) * (chunk + 1)];
            for (int cj = 0; cj < nz; cj += chunk)
                for (int ci = 0; ci < nx; ci += chunk)
                {
                    int i1 = Mathf.Min(ci + chunk, nx), j1 = Mathf.Min(cj + chunk, nz);
                    var mb = new MB();
                    for (int k = 0; k < map.Length; k++) map[k] = -1;
                    for (int j = cj; j < j1; j++)
                    {
                        float z = Valley.MinZ + j * cell;
                        float wl = Mathf.Max(Valley.WaterLevel(z), Valley.WaterLevel(z + cell));
                        for (int i = ci; i < i1; i++)
                        {
                            float mn = Mathf.Min(Mathf.Min(hgt[j * sx + i], hgt[j * sx + i + 1]), Mathf.Min(hgt[(j + 1) * sx + i], hgt[(j + 1) * sx + i + 1]));
                            if (mn > wl + 0.2f) continue;
                            int a = WaterVert(mb, map, chunk, i - ci, j - cj, ci, cj, cell);
                            int b = WaterVert(mb, map, chunk, i - ci + 1, j - cj, ci, cj, cell);
                            int c = WaterVert(mb, map, chunk, i - ci, j - cj + 1, ci, cj, cell);
                            int d = WaterVert(mb, map, chunk, i - ci + 1, j - cj + 1, ci, cj, cell);
                            mb.Tri(a, c, d); mb.Tri(a, d, b);
                        }
                    }
                    if (mb.Count == 0) continue;
                    string name = $"Sel_{ci / chunk}_{cj / chunk}";
                    var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(mb.V);
                    mesh.SetTriangles(mb.T, 0, true);
                    var bounds = mesh.bounds; bounds.Expand(new Vector3(0f, 1f, 0f)); mesh.bounds = bounds;
                    SaveMesh(mesh, name);
                    MeshObject(name, parent, mesh, matWater, false, false);
                }

            // mountain stream + waterfall ribbon
            var s = new MB();
            var pts = Valley.Stream;
            float acc = 0f;
            int prevL = -1, prevR = -1;
            for (int k = 0; k < pts.Length; k++)
            {
                Vector3 p = pts[k];
                Vector3 a = pts[Mathf.Max(k - 1, 0)], b = pts[Mathf.Min(k + 1, pts.Length - 1)];
                Vector3 dir = b - a; dir.y = 0f; dir.Normalize();
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                if (k > 0) acc += Vector3.Distance(pts[k - 1], p);
                float horiz = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                float steep = Mathf.Clamp01(((a.y - b.y) / Mathf.Max(horiz, 0.01f) - 0.3f) * 1.3f);
                float hw = Mathf.Lerp(1.8f, 2.8f, (float)k / pts.Length);
                var col = new Color32((byte)(steep * 255f), 0, 0, 0);
                s.Gloss = 0f; s.Emit = 0f;
                int l = s.Vert(p + perp * hw + Vector3.up * 0.08f, col);
                int rr = s.Vert(p - perp * hw + Vector3.up * 0.08f, col);
                s.U[l] = new Vector2(0f, acc); s.U[rr] = new Vector2(1f, acc);
                if (prevL >= 0) { s.Tri(prevL, l, rr); s.Tri(prevL, rr, prevR); }
                prevL = l; prevR = rr;
            }
            var sm = s.ToMesh("Dere_Selale");
            SaveMesh(sm, "Dere_Selale");
            MeshObject("Dere ve Selale", parent, sm, matFall, false, false);
        }

        static int WaterVert(MB mb, int[] map, int chunk, int li, int lj, int ci, int cj, float cell)
        {
            int key = lj * (chunk + 1) + li;
            if (map[key] >= 0) return map[key];
            float x = Valley.MinX + (ci + li) * cell, z = Valley.MinZ + (cj + lj) * cell;
            mb.V.Add(new Vector3(x, Valley.WaterLevel(z), z));
            mb.C.Add(new Color32(0, 0, 0, 0));
            mb.U.Add(Vector2.zero);
            map[key] = mb.V.Count - 1;
            return map[key];
        }

        // ------------------------------------------------------------------ roads

        static readonly Vector2[] WestRoad = {
            new Vector2(-760, 6), new Vector2(-640, 10), new Vector2(-520, 14), new Vector2(-420, 15), new Vector2(-330, 16),
            new Vector2(-200, 15), new Vector2(-60, 15), new Vector2(0, 15), new Vector2(16, 18), new Vector2(30, 34),
            new Vector2(45, 38), new Vector2(85, 38), new Vector2(100, 34), new Vector2(116, 18), new Vector2(200, 15),
            new Vector2(300, 15), new Vector2(380, 13), new Vector2(470, 14), new Vector2(560, 12), new Vector2(640, 9), new Vector2(760, 6) };
        static readonly Vector2[] EastRoad = {
            new Vector2(-760, 6), new Vector2(-640, 9), new Vector2(-520, 12), new Vector2(-400, 13), new Vector2(-200, 13),
            new Vector2(0, 13), new Vector2(300, 13), new Vector2(380, 13), new Vector2(480, 12), new Vector2(600, 8), new Vector2(760, 5) };

        public static Vector3 RoadPoint(Vector2[] ctrl, int side, float z)
        {
            float e = ctrl[ctrl.Length - 1].y;
            for (int i = 0; i < ctrl.Length - 1; i++)
                if (z >= ctrl[i].x && z <= ctrl[i + 1].x) { e = Mathf.Lerp(ctrl[i].y, ctrl[i + 1].y, Mathf.InverseLerp(ctrl[i].x, ctrl[i + 1].x, z)); break; }
            return new Vector3(Valley.SideX(z, side, e), 0f, z);
        }

        static float RoadDist(float x, float z)
        {
            float d = float.MaxValue;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = RoadPoint(side < 0 ? WestRoad : EastRoad, side, z);
                d = Mathf.Min(d, Mathf.Abs(p.x - x));
            }
            return d;
        }

        static void BuildRoads(ChunkSet cs)
        {
            var r = new System.Random(901);
            Road(cs, -1, WestRoad, 5.6f, H("A88F69"), false, r);
            Road(cs, 1, EastRoad, 6.4f, H("67686A"), true, r);
        }

        static float RoadY(float x, float z, float yc, bool culvert)
        {
            float y = culvert ? Valley.BaseHeight(x, z) : Valley.TerrainHeight(x, z);
            return Mathf.Max(y, yc - 0.3f) + 0.2f;
        }

        static void Road(ChunkSet cs, int side, Vector2[] ctrl, float width, Color32 col, bool asphalt, System.Random r)
        {
            const float step = 2.5f;
            var centers = new List<Vector3>();
            for (float z = Valley.MinZ; z <= Valley.MaxZ; z += step) centers.Add(RoadPoint(ctrl, side, z));
            Vector3 pl = Vector3.zero, pr = Vector3.zero, pc = Vector3.zero;
            bool havePrev = false;
            Color32 rut = MB.Shade(col, 0.85f), line = H("ECEAE2");
            for (int i = 0; i < centers.Count; i++)
            {
                Vector3 c = centers[i];
                Vector3 nb = centers[Mathf.Min(i + 1, centers.Count - 1)] - centers[Mathf.Max(i - 1, 0)];
                nb.y = 0f; nb.Normalize();
                Vector3 perp = new Vector3(-nb.z, 0f, nb.x);
                bool inLot = c.x > Valley.PlateauX0 + 0.5f && c.x < Valley.PlateauX1 - 0.5f && c.z > Valley.PlateauZ0 && c.z < Valley.PlateauZ1;
                bool bridge = Mathf.Abs(c.z - Valley.BridgeZ) < 4.5f && false;
                Valley.StreamNearest(c.x, c.z, out float sd, out _, out _);
                bool culvert = sd < 10f;
                float yc = (culvert ? Valley.BaseHeight(c.x, c.z) : Valley.TerrainHeight(c.x, c.z));
                Vector3 L = c + perp * (width * 0.5f), R = c - perp * (width * 0.5f);
                L.y = RoadY(L.x, L.z, yc, culvert); R.y = RoadY(R.x, R.z, yc, culvert); c.y = Mathf.Max(yc + 0.2f, (L.y + R.y) * 0.5f);
                if (havePrev && !inLot && !bridge)
                {
                    var mb = cs.At(c);
                    mb.Quad(pl, L, R, pr, MB.Jitter(col, r, 0.04f), Vector3.up);
                    if (asphalt)
                    {
                        if ((i & 1) == 0) RoadStrip(mb, pc, c, 0f, 0.09f, line);
                    }
                    else
                    {
                        RoadStrip(mb, pc, c, width * 0.22f, 0.35f, rut);
                        RoadStrip(mb, pc, c, -width * 0.22f, 0.35f, rut);
                    }
                    if (culvert)
                    {
                        mb.Push(Vector3.zero, 0f);
                        Vector3 seg = c - pc;
                        float yaw = Mathf.Atan2(seg.x, seg.z) * Mathf.Rad2Deg;
                        foreach (float off in new[] { width * 0.5f + 0.3f, -width * 0.5f - 0.3f })
                        {
                            Vector3 m = (pc + c) * 0.5f + perp * off;
                            mb.BoxRot(new Vector3(m.x, m.y - 2.5f, m.z), new Vector3(0.5f, 3.4f, step + 0.2f), yaw, StoneCols[1]);
                        }
                        mb.Pop();
                    }
                }
                pl = L; pr = R; pc = c; havePrev = true;
            }
        }

        static void RoadStrip(MB mb, Vector3 a, Vector3 b, float offset, float halfW, Color32 col)
        {
            Vector3 d = b - a; d.y = 0f; d.Normalize();
            Vector3 p = new Vector3(-d.z, 0f, d.x);
            Vector3 a0 = a + p * (offset - halfW) + Vector3.up * 0.025f, a1 = a + p * (offset + halfW) + Vector3.up * 0.025f;
            Vector3 b0 = b + p * (offset - halfW) + Vector3.up * 0.025f, b1 = b + p * (offset + halfW) + Vector3.up * 0.025f;
            mb.Quad(a0, b0, b1, a1, col, Vector3.up);
        }
    }
}
