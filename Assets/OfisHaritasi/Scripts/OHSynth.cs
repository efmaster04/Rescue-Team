using UnityEngine;

// Ses dosyası gerektirmeyen basit sentez sesler: motor döngüsü ve korna.
namespace OfisHaritasi
{
    public static class OHSynth
    {
        const int Rate = 22050;

        // 1 sn'lik kesintisiz motor döngüsü (tam sayı periyot: başı ve sonu aynı faz)
        public static AudioClip Engine()
        {
            int n = Rate;
            var d = new float[n];
            var rnd = new System.Random(11);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 36f; // 36 Hz temel, 1 sn'de tam 36 periyot
                float x = 0.55f * Mathf.Sin(2 * Mathf.PI * f * t)
                        + 0.30f * Mathf.Sin(2 * Mathf.PI * f * 2 * t + 0.6f)
                        + 0.18f * Mathf.Sin(2 * Mathf.PI * f * 3 * t + 1.7f)
                        + 0.10f * Mathf.Sin(2 * Mathf.PI * f * 5 * t + 0.2f);
                float firing = 0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * 72f * t);
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.08f;
                d[i] = (x * firing + lp * 0.35f) * 0.45f;
            }
            // uçlarda kısa çapraz geçiş
            int fade = 400;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] = d[i] * k + d[n - fade + i] * (1 - k); }
            var clip = AudioClip.Create("OH_Motor", n - fade, 1, Rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(d, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }

        public static AudioClip Horn(bool heavy)
        {
            int n = (int)(Rate * 0.55f);
            var d = new float[n];
            float f1 = heavy ? 260f : 410f, f2 = heavy ? 330f : 515f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((0.55f - t) / 0.06f);
                float a = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f1 * t)) * 0.5f + Mathf.Sin(2 * Mathf.PI * f1 * 2 * t) * 0.2f;
                float b = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f2 * t)) * 0.5f;
                d[i] = (a + b) * 0.22f * env;
            }
            var clip = AudioClip.Create("OH_Korna", n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
