using UnityEngine;

// Ambulans sesleri: siren (hareket hâlinde), motor (hıza göre perde), kapı ve korna.
// Klipler SynthClips ile çalışma zamanında üretilir. Siren ve motor 3D, Doppler açık. AudioMixer yok.
public class AmbulanceAudio : MonoBehaviour
{
    public AudioSource siren, engine, door, horn;

    [Tooltip("Motor perdesi: durağanda bu, seyir hızında bu + pitchRange.")]
    public float engineIdlePitch = 0.7f;
    public float enginePitchRange = 0.8f;
    public float referenceSpeed = 12f;
    [Tooltip("İki korna arasındaki en kısa süre (sn).")]
    public float hornCooldown = 1.2f;

    private float nextHornTime;

    private void Awake()
    {
        siren = Ensure(siren, "Siren", SynthClips.Siren(), true, 0.55f);
        engine = Ensure(engine, "Engine", SynthClips.Engine(), true, 0.35f);
        door = Ensure(door, "Door", SynthClips.Door(), false, 0.8f);
        horn = Ensure(horn, "Horn", SynthClips.Horn(), false, 0.8f);
        engine.Play();
    }

    private AudioSource Ensure(AudioSource source, string label, AudioClip clip, bool loop, float volume)
    {
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 1f;   // 3D
        source.dopplerLevel = 1f;   // Doppler açık
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 6f;
        source.maxDistance = 250f;
        source.volume = volume;
        return source;
    }

    // Her fizik adımında: hareket hâlinde siren çalar, durakta susar; motor perdesi hıza göre.
    public void SetMotion(bool moving, float speed)
    {
        if (moving && !siren.isPlaying) siren.Play();
        else if (!moving && siren.isPlaying) siren.Stop();
        engine.pitch = engineIdlePitch + enginePitchRange * Mathf.Clamp01(speed / Mathf.Max(0.1f, referenceSpeed));
    }

    public void PlayDoor() { door.Play(); }

    // İki kısa korna (klip ikisini birden içerir). cooldown içinde tekrar çalmaz.
    public bool PlayHorn()
    {
        if (Time.time < nextHornTime) return false;
        nextHornTime = Time.time + hornCooldown;
        horn.Play();
        return true;
    }

    private void OnDestroy()
    {
        foreach (var s in new[] { siren, engine, door, horn })
            if (s != null && s.clip != null) Destroy(s.clip);
    }
}
