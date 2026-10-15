using UnityEngine;
namespace RescueTeam.Bolum1
{
    public sealed class SynthesizedAmbulanceAudio : MonoBehaviour
    {
        public AudioSource siren, engine, door, horn;
        public AudioClip SirenClip { get; private set; }
        public AudioClip EngineClip { get; private set; }
        public AudioClip DoorClip { get; private set; }
        public AudioClip HornClip { get; private set; }
        float nextHorn;
        void Awake()
        {
            SirenClip = Synthesize("Siren", 1.2f, 0); EngineClip = Synthesize("Engine", 1, 1);
            DoorClip = Synthesize("Door", 0.45f, 2); HornClip = Synthesize("Horn", 0.7f, 3);
            siren = Source(SirenClip, true, 0.22f); engine = Source(EngineClip, true, 0.16f);
            door = Source(DoorClip, false, 0.5f); horn = Source(HornClip, false, 0.45f);
            engine.Play();
        }
        AudioSource Source(AudioClip clip, bool loop, float volume)
        {
            var source = gameObject.AddComponent<AudioSource>(); source.clip = clip; source.loop = loop;
            source.playOnAwake = false; source.spatialBlend = 1; source.dopplerLevel = 1;
            source.minDistance = 5; source.maxDistance = 130; source.volume = volume; return source;
        }
        public static AudioClip Synthesize(string name, float seconds, int kind)
        {
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(rate * seconds)];
            var random = new System.Random(112);
            double phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate, sample;
                float frequency = kind == 0 ? (t < 0.6f ? 435 : 580) : kind == 1 ? 70 : 370;
                phase += frequency / rate;
                float wave = (float)(2 / System.Math.PI * System.Math.Asin(System.Math.Sin(phase * System.Math.PI * 2)));
                if (kind == 0) sample = wave * 0.5f;
                else if (kind == 1) sample = Mathf.Sin(2 * Mathf.PI * 70 * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 140 * t) * 0.1f;
                else if (kind == 2) sample = (float)(random.NextDouble() * 2 - 1) * Mathf.Exp(-t * 18) * 0.55f + Mathf.Sin(2 * Mathf.PI * 95 * t) * Mathf.Exp(-Mathf.Abs(t - 0.25f) * 40) * 0.5f;
                else sample = (t < 0.22f || (t > 0.36f && t < 0.58f)) ? wave * 0.6f : 0;
                float fade = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((seconds - t) / 0.01f);
                samples[i] = sample * fade;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
        public void Drive(float speed, bool moving)
        {
            if (moving && !siren.isPlaying) siren.Play(); else if (!moving && siren.isPlaying) siren.Stop();
            engine.pitch = 0.65f + speed / 12 * 0.7f;
        }
        public void Door() { door.Play(); }
        public void Horn() { if (Time.time >= nextHorn) { horn.Play(); nextHorn = Time.time + 0.8f; } }
        void OnDestroy() { Destroy(SirenClip); Destroy(EngineClip); Destroy(DoorClip); Destroy(HornClip); }
    }
}
