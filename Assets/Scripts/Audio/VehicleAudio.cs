using UnityEngine;

// Trafik aracı sesleri (kullanıcı kararı 9): hıza göre perdesi değişen motor (kamyonda daha kalın) ve oyuncuya korna
// (tipe göre perde). Klipler SynthClips ile üretilir ve tipler arasında paylaşılır. 3D, Doppler açık. AudioMixer yok.
public class VehicleAudio : MonoBehaviour
{
    public AudioSource engine, horn;

    [Tooltip("Motor klibinin temel frekansı (Hz). Kamyonda daha düşük = daha kalın.")]
    public float engineBaseHz = 55f;
    [Tooltip("Korna akorunun iki tonu (Hz).")]
    public float hornLowHz = 410f, hornHighHz = 515f;
    [Tooltip("Motor perdesi: durağanda bu, referans hızda bu + pitchRange.")]
    public float engineIdlePitch = 0.7f;
    public float enginePitchRange = 0.8f;
    public float referenceSpeed = 12f;
    [Tooltip("İki korna arasındaki en kısa süre (sn).")]
    public float hornCooldown = 1.5f;

    // Kaç kez korna çalındı (test; batchmode'da isPlaying güvenilmez).
    public int HornPlays { get; private set; }
    // Korna çalındı (kaynak, konum): yakındaki karakterler irkilir (CrewAnimation). Ses çalmasından bağımsız.
    public static event System.Action<VehicleAudio, Vector3> HornPlayed;

    private float nextHornTime;

    private void Awake()
    {
        float hz = Mathf.Round(engineBaseHz);
        engine = Ensure(engine, SynthClips.Cached("TrafficEngine_" + hz, () => SynthClips.TrafficEngine(hz)), true, 0.3f);
        float lo = hornLowHz, hi = hornHighHz;
        horn = Ensure(horn, SynthClips.Cached("TrafficHorn_" + Mathf.RoundToInt(lo) + "_" + Mathf.RoundToInt(hi), () => SynthClips.TrafficHorn(lo, hi)), false, 0.8f);
        engine.Play();
    }

    private AudioSource Ensure(AudioSource source, AudioClip clip, bool loop, float volume)
    {
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 1f;   // 3D
        source.dopplerLevel = 1f;   // Doppler açık
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 5f;
        source.maxDistance = 200f;
        source.volume = volume;
        return source;
    }

    public void SetSpeed(float speed)
    {
        engine.pitch = engineIdlePitch + enginePitchRange * Mathf.Clamp01(speed / Mathf.Max(0.1f, referenceSpeed));
    }

    public bool PlayHorn()
    {
        if (Time.time < nextHornTime) return false;
        nextHornTime = Time.time + hornCooldown;
        horn.Play();
        HornPlays++;
        HornPlayed?.Invoke(this, transform.position);
        return true;
    }
}
