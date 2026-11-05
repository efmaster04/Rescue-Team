using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi
{
    [System.Serializable]
    public struct FlowObstacle
    {
        public float x, z, r, strength;
        public FlowObstacle(float x, float z, float r, float strength) { this.x = x; this.z = z; this.r = r; this.strength = strength; }
    }

    /// <summary>
    /// Shared analytic description of the flooded valley: terrain height, flood water level and the current.
    /// Used by the editor generator (to build meshes and bake the flow texture) and by runtime scripts
    /// (floating debris), so both always agree.
    /// x = east, z = north (upstream), y = up. The flood flows toward -z.
    /// </summary>
    public static class Valley
    {
        public const float MinX = -520f, MaxX = 520f, MinZ = -760f, MaxZ = 760f;
        public const float WaterBase = 4f, WaterSlope = 0.0035f, Discharge = 270f;
        public const float WaveAmp = 0.07f;

        // ---- obstacles that bend the current (houses, poles, piers ...), sorted by z ----
        static FlowObstacle[] obs = new FlowObstacle[0];
        static float[] obsZ = new float[0];
        public static int ObstacleCount => obs.Length;
        public static FlowObstacle GetObstacle(int i) => obs[i];
        const float ObstacleWindow = 48f;

        public static void SetObstacles(IList<FlowObstacle> list)
        {
            var tmp = new List<FlowObstacle>(list);
            tmp.Sort((a, b) => a.z.CompareTo(b.z));
            obs = tmp.ToArray();
            obsZ = new float[obs.Length];
            for (int i = 0; i < obs.Length; i++) obsZ[i] = obs[i].z;
        }

        static int LowerBound(float z)
        {
            int lo = 0, hi = obsZ.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (obsZ[mid] < z) lo = mid + 1; else hi = mid; }
            return lo;
        }

        // ---- feature data (parking plateau, ramp, rescue pad, waterfall stream) ----
        public static readonly float PlateauX0, PlateauX1, PlateauY;
        public const float PlateauZ0 = 28f, PlateauZ1 = 102f;
        public static readonly Vector3 RampTop, RampBottom;
        public const float RampHalfWidth = 4.2f;
        public static readonly float PadX0, PadX1, PadY, PadEdgeX;
        public const float PadZ0 = -302f, PadZ1 = -248f;
        public const float BridgeZ = 380f;
        public static readonly Vector3[] Stream;
        static readonly float sMinX, sMaxX, sMinZ, sMaxZ;

        static Valley()
        {
            float xe = CenterX(65f) - HalfWidth(65f);
            PlateauX0 = xe - 54f; PlateauX1 = xe - 17f;
            PlateauY = WaterLevel(65f) + 10.8f;
            RampTop = new Vector3(PlateauX1 - 0.5f, PlateauY, 64f);
            Vector2 d = new Vector2(1f, -0.78f).normalized;
            const float L = 47f;
            float bz = RampTop.z + d.y * L;
            RampBottom = new Vector3(RampTop.x + d.x * L, WaterLevel(bz) - 1.4f, bz);

            PadEdgeX = CenterX(-275f) - HalfWidth(-275f);
            PadX0 = PadEdgeX - 30f; PadX1 = PadEdgeX + 3f;
            PadY = WaterLevel(-275f) + 1.7f;

            const int N = 110;
            Stream = new Vector3[N + 1];
            float prev = float.MaxValue;
            sMinX = sMinZ = float.MaxValue; sMaxX = sMaxZ = float.MinValue;
            for (int k = 0; k <= N; k++)
            {
                float e = 165f - k * (172f / N);
                float z = 138f + 9f * Mathf.Sin(e * 0.045f) + 3.5f * Mathf.Sin(e * 0.13f);
                float x = CenterX(z) + HalfWidth(z) + e;
                float h = BaseHeight(x, z) - 0.8f;
                float s = Mathf.Min(prev - 0.03f, h);
                s = Mathf.Max(s, WaterLevel(z) - 0.03f);
                prev = s;
                Stream[k] = new Vector3(x, s, z);
                sMinX = Mathf.Min(sMinX, x); sMaxX = Mathf.Max(sMaxX, x);
                sMinZ = Mathf.Min(sMinZ, z); sMaxZ = Mathf.Max(sMaxZ, z);
            }
        }

        // ---- basic helpers ----
        public static float S(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
        public static float Noise(float x, float z) => Mathf.PerlinNoise(x + 1000.37f, z + 1000.71f) * 2f - 1f;
        public static float Ridged(float x, float z) => 1f - Mathf.Abs(Noise(x + 311.1f, z - 97.3f));

        public static float WaterLevel(float z) => WaterBase + z * WaterSlope;

        public static float CenterX(float z)
        {
            float c = 30f * Mathf.Sin(z * 0.0045f + 0.4f) + 12f * Mathf.Sin(z * 0.0121f + 2.3f);
            c += 150f * S(560f, 800f, z);
            c -= 170f * S(-560f, -800f, z);
            return c;
        }
        public static float CenterDX(float z) => CenterX(z + 0.5f) - CenterX(z - 0.5f);
        public static Vector2 Downstream(float z) => new Vector2(-CenterDX(z), -1f).normalized;

        public static float HalfWidth(float z)
        {
            float w = 86f + 5f * Mathf.Sin(z * 0.023f + 1f);
            w = Mathf.Lerp(w, 42f, S(300f, 430f, z));
            w = Mathf.Lerp(w, 34f, S(600f, 720f, z));
            w = Mathf.Lerp(w, 118f, S(-300f, -430f, z));
            w = Mathf.Lerp(w, 52f, S(-600f, -720f, z));
            return w;
        }

        public static float ShelfWidth(float z, int side)
        {
            float w = side < 0 ? 58f : 42f;
            w = Mathf.Lerp(w, side < 0 ? 22f : 9f, S(320f, 440f, z));
            w = Mathf.Lerp(w, 7f, S(600f, 700f, z));
            w = Mathf.Lerp(w, side < 0 ? 78f : 62f, S(-320f, -430f, z));
            w = Mathf.Lerp(w, 12f, S(-600f, -720f, z));
            return w;
        }

        public static float BankHeight(float z, int side)
        {
            float b = 7.2f + 1.1f * Mathf.Sin(z * 0.017f + side * 1.3f);
            b = Mathf.Lerp(b, 9.5f, S(380f, 480f, z));
            b = Mathf.Lerp(b, 5.0f, S(-330f, -450f, z));
            return b;
        }

        /// Signed distance beyond the flood-plain edge (negative = inside the flooded plain).
        public static float EdgeDist(float x, float z, out int side)
        {
            float dx = x - CenterX(z);
            side = dx < 0f ? -1 : 1;
            return Mathf.Abs(dx) - HalfWidth(z);
        }
        public static float SideX(float z, int side, float e) => CenterX(z) + side * (HalfWidth(z) + e);

        // ---- terrain ----
        public static float BaseHeight(float x, float z)
        {
            float cx = CenterX(z), W = HalfWidth(z), wl = WaterLevel(z);
            float dx = x - cx;
            int side = dx < 0f ? -1 : 1;
            float a = Mathf.Abs(dx);
            float e = a - W;
            if (e < 0f)
            {
                float t = S(0f, W * 0.7f, -e);
                float depth = Mathf.Lerp(0.45f, 3.3f, t);
                float ch = Mathf.Clamp01(1f - a / 20f);
                depth += ch * ch * 2.6f;
                return wl - depth + Noise(x * 0.05f, z * 0.05f) * 0.3f;
            }
            const float bankW = 10f;
            float bankH = BankHeight(z, side);
            float h = wl - 0.45f + (bankH + 0.45f) * S(0f, bankW, e);
            float shelf = ShelfWidth(z, side);
            float se = e - bankW;
            if (se > 0f)
                h += Mathf.Min(se, shelf) * 0.05f + Noise(x * 0.03f + 7f, z * 0.03f) * 0.8f * S(0f, 8f, se);
            float he = se - shelf;
            if (he > 0f)
            {
                float hill = 0.3f * he + 0.001f * he * he;
                hill *= 0.72f + 0.55f * Ridged(x * 0.007f, z * 0.007f);
                hill += Noise(x * 0.021f, z * 0.021f) * 7f * S(0f, 30f, he);
                const float step = 9f;
                float q = hill / step, fq = Mathf.Floor(q), fr = q - fq;
                float terr = (fq + S(0.55f, 1f, fr)) * step;
                hill = Mathf.Lerp(hill, terr, 0.55f * S(10f, 40f, he));
                h += hill;
            }
            return h;
        }

        public static bool RampLocal(float x, float z, out float s, out float lat, out float y)
        {
            float ax = RampTop.x, az = RampTop.z;
            float bx = RampBottom.x - ax, bz = RampBottom.z - az;
            float L = Mathf.Sqrt(bx * bx + bz * bz);
            float dx = bx / L, dz = bz / L;
            float px = x - ax, pz = z - az;
            s = px * dx + pz * dz;
            lat = dx * pz - dz * px;
            y = Mathf.Lerp(RampTop.y, RampBottom.y, Mathf.Clamp01(s / L));
            return s > -1f && s < L + 1f;
        }
        public static float RampLength => new Vector2(RampBottom.x - RampTop.x, RampBottom.z - RampTop.z).magnitude;

        public static void StreamNearest(float x, float z, out float dist, out float y, out float along)
        {
            dist = float.MaxValue; y = 0f; along = 0f;
            float acc = 0f;
            for (int i = 0; i < Stream.Length - 1; i++)
            {
                Vector3 a = Stream[i], b = Stream[i + 1];
                float abx = b.x - a.x, abz = b.z - a.z;
                float len2 = abx * abx + abz * abz;
                float t = Mathf.Clamp01(((x - a.x) * abx + (z - a.z) * abz) / len2);
                float px = a.x + abx * t - x, pz = a.z + abz * t - z;
                float d = Mathf.Sqrt(px * px + pz * pz);
                float len = Mathf.Sqrt(len2);
                if (d < dist) { dist = d; y = Mathf.Lerp(a.y, b.y, t); along = acc + len * t; }
                acc += len;
            }
        }

        public static float TerrainHeight(float x, float z)
        {
            float h = BaseHeight(x, z);

            // parking plateau, with earth ramps at both ends for the road
            if (x > PlateauX0 && x < PlateauX1)
            {
                float dzOut = Mathf.Max(PlateauZ0 - z, Mathf.Max(z - PlateauZ1, 0f));
                float m = 1f - S(0f, 16f, dzOut);
                if (m > 0f) h = Mathf.Lerp(h, PlateauY - 0.05f, m);
            }

            // trench for the cobblestone ramp
            if (RampLocal(x, z, out float s, out float lat, out float ry))
            {
                float al = Mathf.Abs(lat);
                if (al < RampHalfWidth + 6f)
                {
                    float lim = ry - 0.35f;
                    float m = 1f - S(RampHalfWidth + 0.6f, RampHalfWidth + 6f, al);
                    if (h > lim) h = Mathf.Lerp(h, lim, m);
                }
            }

            // rescue staging pad (gravel, sloping into the water)
            if (z > PadZ0 - 14f && z < PadZ1 + 14f && x > PadX0 - 14f && x < PadX1 + 6f)
            {
                float e = PadEdgeX - x;
                float surf = e > 4f ? PadY : PadY - (4f - e) * 0.55f;
                float dOut = Mathf.Max(Mathf.Max(PadZ0 - z, z - PadZ1), Mathf.Max(PadX0 - x, 0f));
                float m = 1f - S(0f, 14f, dOut);
                if (x > PadX1) m *= 1f - S(PadX1, PadX1 + 6f, x);
                if (m > 0f) h = Mathf.Lerp(h, surf, m);
            }

            // waterfall stream channel
            if (x > sMinX - 12f && x < sMaxX + 12f && z > sMinZ - 12f && z < sMaxZ + 12f)
            {
                StreamNearest(x, z, out float dist, out float sy, out _);
                if (dist < 11f)
                {
                    float bed = sy - 0.7f;
                    float carve = Mathf.Lerp(bed, h, S(2.2f, 11f, dist));
                    if (carve < h) h = carve;
                }
            }
            return h;
        }

        public static float Depth(float x, float z) => WaterLevel(z) - TerrainHeight(x, z);

        // ---- current ----
        public static Vector2 BaseFlow(float x, float z)
        {
            float cx = CenterX(z), W = HalfWidth(z);
            Vector2 dir = Downstream(z);
            float n = (x - cx) / W;
            float baseSpeed = Discharge / (2f * W);
            float prof = Mathf.Max(0.12f, 1.3f - 0.95f * n * n);
            float ch = Mathf.Clamp01(1f - Mathf.Abs(x - cx) / 22f);
            prof += ch * ch * 0.5f;
            return dir * (baseSpeed * prof);
        }

        public static Vector2 Flow(float x, float z)
        {
            Vector2 U = BaseFlow(x, z);
            Vector2 v = U;
            float Ux = U.x, Uz = U.y;
            float sp = U.magnitude;
            Vector2 u = sp > 1e-4f ? U / sp : new Vector2(0f, -1f);
            for (int i = LowerBound(z - ObstacleWindow); i < obs.Length; i++)
            {
                FlowObstacle o = obs[i];
                if (o.z > z + ObstacleWindow) break;
                float rx = x - o.x, rz = z - o.z;
                float lim = o.r * 5f;
                float d2 = rx * rx + rz * rz;
                if (d2 > lim * lim) continue;
                float d = Mathf.Max(Mathf.Sqrt(d2), o.r * 0.999f);
                float sc = d / Mathf.Max(Mathf.Sqrt(d2), 1e-3f);
                rx *= sc; rz *= sc;
                float d4 = d * d * d * d;
                float A = (rx * rx - rz * rz) / d4, B = 2f * rx * rz / d4;
                float R2 = o.r * o.r;
                float px = -R2 * (Ux * A + Uz * B);
                float pz = -R2 * (Ux * B - Uz * A);
                float f = (1f - S(lim * 0.6f, lim, d)) * o.strength;
                v.x += px * f; v.y += pz * f;
                float along = rx * u.x + rz * u.y;
                if (along > 0f)
                {
                    float lat = Mathf.Abs(rx * u.y - rz * u.x);
                    float wake = Mathf.Exp(-(lat * lat) / (R2 * 0.8f)) * Mathf.Exp(-along / (o.r * 6f));
                    v -= U * (0.6f * wake * Mathf.Min(1f, o.strength));
                }
            }
            return v;
        }

        /// 0..1 amount of white water (used for the baked flow texture).
        public static float Foam(float x, float z, float depth, Vector2 flow)
        {
            Vector2 U = BaseFlow(x, z);
            float sp = U.magnitude;
            Vector2 u = sp > 1e-4f ? U / sp : new Vector2(0f, -1f);
            float foam = 0f;
            for (int i = LowerBound(z - ObstacleWindow); i < obs.Length; i++)
            {
                FlowObstacle o = obs[i];
                if (o.z > z + ObstacleWindow) break;
                float rx = x - o.x, rz = z - o.z;
                float d = Mathf.Sqrt(rx * rx + rz * rz);
                if (d > o.r * 6f) continue;
                float up = d > 1e-3f ? -(rx * u.x + rz * u.y) / d : 1f;
                float ring = Mathf.Exp(-Mathf.Max(0f, d - o.r) / (0.7f + o.r * 0.12f)) * (0.5f + 0.5f * up) * 0.9f;
                float along = rx * u.x + rz * u.y;
                float wake = 0f;
                if (along > 0f)
                {
                    float lat = Mathf.Abs(rx * u.y - rz * u.x);
                    wake = Mathf.Exp(-(lat * lat) / (o.r * o.r * 0.5f)) * Mathf.Exp(-along / (o.r * 4.5f)) * 0.5f;
                }
                foam = Mathf.Max(foam, Mathf.Max(ring, wake) * Mathf.Min(1.2f, o.strength));
            }
            float shore = depth < 0.45f ? Mathf.Pow(1f - Mathf.Max(0f, depth) / 0.45f, 2f) * 0.7f : 0f;
            float rapids = S(3.0f, 5.0f, flow.magnitude) * 0.6f;
            // plunge pool of the waterfall
            Vector3 end = Stream[Stream.Length - 1];
            float dp = new Vector2(x - end.x, z - end.z).magnitude;
            float plunge = Mathf.Exp(-dp / 9f) * 1.1f;
            return Mathf.Clamp01(Mathf.Max(Mathf.Max(foam, shore), Mathf.Max(rapids, plunge)));
        }

        /// Same swell as the water shader's vertex waves.
        public static float WaveHeight(float x, float z, float t, float amp)
        {
            return amp * (0.5f * Mathf.Sin(x * 0.21f + z * 0.13f + t * 1.3f)
                        + 0.35f * Mathf.Sin(-x * 0.17f + z * 0.29f + t * 1.7f)
                        + 0.25f * Mathf.Sin(x * 0.37f - z * 0.11f + t * 2.3f));
        }
    }
}
