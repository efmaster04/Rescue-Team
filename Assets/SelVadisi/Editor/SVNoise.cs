using UnityEngine;

namespace SelVadisi.EditorBuild
{
    /// Tileable procedural textures for the water and sky shaders.
    public static class SVNoise
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// Periodic value noise; u,v in [0,1), period = lattice cells per tile.
        static float Value(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Fade(x - x0), fy = Fade(y - y0);
            int xa = ((x0 % period) + period) % period, ya = ((y0 % period) + period) % period;
            int xb = (xa + 1) % period, yb = (ya + 1) % period;
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u, v, period << o, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static float Worley(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float best = 10f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                    float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 7);
                    float d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < best) best = d;
                }
            return Mathf.Sqrt(best);
        }

        static void Normalize(float[] a)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (float f in a) { mn = Mathf.Min(mn, f); mx = Mathf.Max(mx, f); }
            float s = 1f / Mathf.Max(mx - mn, 1e-5f);
            for (int i = 0; i < a.Length; i++) a[i] = (a[i] - mn) * s;
        }

        /// R: fbm (current lines), G: soft fbm (swirls, clouds), B: cellular foam bubbles, A: fine fbm.
        public static Texture2D MakeNoiseTexture(int n)
        {
            var r = new float[n * n]; var g = new float[n * n]; var b = new float[n * n]; var a = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (float)x / n, v = (float)y / n;
                    int i = y * n + x;
                    r[i] = Fbm(u, v, 6, 4, 11);
                    g[i] = Fbm(u, v, 4, 5, 23);
                    b[i] = 1f - Mathf.Clamp01(Worley(u, v, 14, 5) * 1.25f);
                    a[i] = Fbm(u, v, 16, 3, 41);
                }
            Normalize(r); Normalize(g); Normalize(b); Normalize(a);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
            {
                name = "SV_Noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32((byte)(r[i] * 255f), (byte)(g[i] * 255f), (byte)(b[i] * 255f), (byte)(a[i] * 255f));
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// R,G: wave slope (x,z) encoded 0..1, B: height.
        public static Texture2D MakeNormalTexture(int n)
        {
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (float)x / n, v = (float)y / n;
                    float w = 1f - Worley(u, v, 9, 77);
                    h[y * n + x] = Fbm(u, v, 5, 5, 59) * 0.7f + w * w * 0.3f;
                }
            Normalize(h);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
            {
                name = "SV_WaveNormals", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            var px = new Color32[n * n];
            const float k = 5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float hx = h[y * n + (x + 1) % n] - h[y * n + (x - 1 + n) % n];
                    float hz = h[((y + 1) % n) * n + x] - h[((y - 1 + n) % n) * n + x];
                    Vector3 nn = new Vector3(-hx * k, 1f, -hz * k).normalized;
                    px[y * n + x] = new Color32((byte)((nn.x * 0.5f + 0.5f) * 255f), (byte)((nn.z * 0.5f + 0.5f) * 255f), (byte)(h[y * n + x] * 255f), 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}
