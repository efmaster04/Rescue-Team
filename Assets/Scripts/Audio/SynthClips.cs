using UnityEngine;

// Geçici sesler (kullanıcı kararı 17): çalışma zamanında koddan üretilen AudioClip'ler. Dosya yok, indirme yok.
// Gerçek ses dosyaları gelince yalnız AmbulanceAudio'daki klip kaynağı değişir.
public static class SynthClips
{
    public const int SampleRate = 44100;

    // (a) Avrupa tipi iki tonlu siren: 435 Hz ve 580 Hz, her ton 0.6 sn, yumuşatılmış kare dalga.
    // Her tonun süresi tam periyot sayısına denk gelir (435·0.6 = 261, 580·0.6 = 348); döngü tıkırtısız birleşir.
    public static AudioClip Siren(float lowHz = 435f, float highHz = 580f, float toneSeconds = 0.6f)
    {
        int toneSamples = Mathf.RoundToInt(toneSeconds * SampleRate);
        var data = new float[toneSamples * 2];
        double phase = 0.0;
        for (int i = 0; i < data.Length; i++)
        {
            float f = i < toneSamples ? lowHz : highHz;
            phase += f / SampleRate;
            float sine = Mathf.Sin((float)(phase % 1.0) * 2f * Mathf.PI);
            // tanh ile yumuşatılmış kare dalga (sert köşeler yok, kulak tırmalamaz)
            float square = (float)System.Math.Tanh(4.0 * sine) / (float)System.Math.Tanh(4.0);
            data[i] = 0.45f * square;
        }
        return Make("Synth_Siren", data);
    }

    // (b) Motor: alçak uğultu (55 Hz temel + harmonikler, hafif titreşim). 1 sn döngü; perde hızla AudioSource.pitch'ten değişir.
    public static AudioClip Engine(float baseHz = 55f)
    {
        var data = new float[SampleRate];
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / SampleRate;
            float w = 2f * Mathf.PI * baseHz * t;
            float s = 0.5f * Mathf.Sin(w) + 0.25f * Mathf.Sin(2f * w + 0.3f) + 0.15f * Mathf.Sin(3f * w + 1.1f) + 0.08f * Mathf.Sin(5f * w);
            float flutter = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 11f * t); // silindir vuruntusu hissi
            data[i] = 0.5f * s * flutter;
        }
        return Make("Synth_Engine", data);
    }

    // (c) Kapı: kısa gürültü patlaması (mandal/menteşe) + tok vuruş (kapanma).
    public static AudioClip Door()
    {
        int n = Mathf.RoundToInt(0.5f * SampleRate);
        var data = new float[n];
        var rng = new System.Random(17);
        float lowpass = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowpass += 0.25f * (noise - lowpass); // hafif alçak geçiren: daha mat bir hışırtı
            float burst = lowpass * Mathf.Exp(-t * 22f) * 0.9f;
            float tt = t - 0.06f;
            float thud = tt > 0f ? Mathf.Sin(2f * Mathf.PI * 68f * tt) * Mathf.Exp(-tt * 16f) * 0.8f : 0f;
            data[i] = Mathf.Clamp(burst + thud, -1f, 1f) * Fade(i, n, 0.004f);
        }
        return Make("Synth_Door", data);
    }

    // (d) Korna: iki kısa korna (iki tonlu akor, yumuşatılmış testere dalga).
    public static AudioClip Horn(float honkSeconds = 0.22f, float gapSeconds = 0.12f)
    {
        int honk = Mathf.RoundToInt(honkSeconds * SampleRate);
        int gap = Mathf.RoundToInt(gapSeconds * SampleRate);
        int n = honk * 2 + gap + Mathf.RoundToInt(0.05f * SampleRate);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            int local = i < honk ? i : i - honk - gap;
            if (local < 0 || local >= honk) continue;
            float t = (float)i / SampleRate;
            float env = Mathf.Clamp01(local / (0.01f * SampleRate)) * Mathf.Clamp01((honk - local) / (0.02f * SampleRate));
            float a = SoftSaw(t * 410f), b = SoftSaw(t * 515f);
            data[i] = 0.35f * (a + b) * env;
        }
        return Make("Synth_Horn", data);
    }

    // RMS (karekök ortalama kare): klibin sessiz olmadığını doğrulamak için.
    public static float Rms(AudioClip clip)
    {
        var data = new float[clip.samples * clip.channels];
        clip.GetData(data, 0);
        double sum = 0.0;
        foreach (float v in data) sum += v * v;
        return data.Length > 0 ? (float)System.Math.Sqrt(sum / data.Length) : 0f;
    }

    private static float SoftSaw(float cycles)
    {
        // İlk birkaç harmonikle testere dalga (köşeleri yumuşak)
        float x = 2f * Mathf.PI * cycles;
        return (Mathf.Sin(x) + 0.5f * Mathf.Sin(2f * x) + 0.33f * Mathf.Sin(3f * x) + 0.25f * Mathf.Sin(4f * x)) * 0.5f;
    }

    private static float Fade(int i, int n, float seconds)
    {
        float f = seconds * SampleRate;
        return Mathf.Clamp01(i / f) * Mathf.Clamp01((n - 1 - i) / f);
    }

    private static AudioClip Make(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
