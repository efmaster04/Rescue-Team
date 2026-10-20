using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// U4 (kullanıcı kararı "Hırsızlar", 2026-10-19): oyuncu savunması. Girdi PlayerInputReader'dan gelir (sol tık, sağ tık, Q).
//  - Sol tık: Push_Shove; hit_frame'de (kare 9) önündeki pushRange / pushAngle içindeki hırsızlar pushKnockback geri savrulur,
//    pushStunSeconds sersemler, elindekini düşürür. Bekleme pushCooldown.
//  - Koşarken (Shift + hareket) sol tık: Tackle_Dive; oyuncu tackleDistance öne kayar (CharacterController.Move: zemine ve duvara
//    saygılı), yolundaki hırsız tackleStunSeconds sersemler; dalış bitince Tackle_GetUp ile kalkar (Animator geçişi). Bekleme tackleCooldown.
//  - Sağ tık basılı: elinde tek elle taşınan eşya varsa Throw_Aim (döngü; eşya sağ elde); bırakınca Throw_Release, release_frame'de
//    (kare 8) eşya bakış yönüne throwSpeed ile fırlar (LootItem.Throw: fizik; hırsıza isabet sersemletir; boşluğa düşerse geri döner).
//  - Q: Whistle_Blow; blow_frames başında (kare 15) whistleRadius içindeki hırsızlar fearSeconds korkup kamyonete kaçar. Bekleme whistleCooldown.
//  - Sedye / sırtta hasta / kangal / halatta: saldırı ve düdük yok (en sade). Yerdeyken / kontrol yokken de yok.
public class PlayerCombat : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public ThiefSettings settings;
    public CrewAnimation crew;
    public AudioSource audioSource;
    [Tooltip("Bu yüksekliğin altı boşluk (FallRespawnPlane): fırlatılan eşya başlangıç yerine döner.")]
    public float killBelowY = -50f;

    // Klip olayları (clips_meta.json; 1 tabanlı kare → (kare−1)/30 sn)
    public const float PushSeconds = 0.6f, PushHitTime = 8f / 30f;
    // Tackle_Dive: 28 kare / 0.9 sn, hit_frame 12 (0.37 sn), ground_frame 22 (0.7 sn). Kayma 0.2 → 0.7 sn (havada), isabet 0.1–0.75 sn.
    public const float TackleSeconds = 0.9f, TackleGetUpSeconds = 1.0f, TackleHitStart = 0.1f, TackleHitEnd = 0.75f, TackleSlideStart = 0.2f, TackleSlideSeconds = 0.5f;
    public const float ThrowReleaseSeconds = 0.5f, ThrowReleaseTime = 7f / 30f;
    public const float WhistleSeconds = 1.2f, WhistleBlowTime = 14f / 30f;

    public bool Aiming { get; private set; }
    public bool Busy { get; private set; }
    public float PushReadyAt { get; private set; } = -100f;
    public float TackleReadyAt { get; private set; } = -100f;
    public float WhistleReadyAt { get; private set; } = -100f;
    public float WhistleRemaining { get { return Mathf.Max(0f, WhistleReadyAt - Time.time); } }
    public bool WhistleReady { get { return Time.time >= WhistleReadyAt; } }
    public string LastAction { get; private set; } = "";
    // Ölçüm
    public int Pushes { get; private set; }
    public int PushHits { get; private set; }
    public int Tackles { get; private set; }
    public int TackleHits { get; private set; }
    public int Throws { get; private set; }
    public int Whistles { get; private set; }
    public int WhistleHits { get; private set; }
    public int Rejected { get; private set; }
    public string LastRejectReason { get; private set; } = "";
    public LootItem LastThrown { get; private set; }
    // U4b tanı: son saldırıda yüz hangi yöne çevrildi (kamera / hedef hırsız)
    public string LastFacing { get; private set; } = "";

    private readonly HashSet<Thief> tackleHit = new HashSet<Thief>();

    private void Awake()
    {
        if (player == null) player = GetComponent<ThirdPersonPlayer>();
        if (crew == null) crew = GetComponent<CrewAnimation>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 60f;
        audioSource.volume = 0.8f;
    }

    // Saldırı / düdük yapılabilir mi: kontrol var, yerde değil, halatta değil, sedye / sırtta hasta / kangal yok, başka eylem sürmüyor.
    public bool CanAct
    {
        get
        {
            return player != null && settings != null && player.HasControl && !player.IsDown && player.Rope == null
                && player.HeldStretcher == null && player.Piggyback == null && player.HeldCoil == null && !Busy;
        }
    }
    public bool CanAttack { get { return CanAct && !Aiming; } }
    public bool CanThrow { get { return CanAct && player.HeldValuable != null && player.HeldValuable.kind != LootKind.Equipment; } }
    public bool CanWhistle { get { return CanAct && !Aiming && WhistleReady; } }

    private void Update()
    {
        // Nişan alırken eşya elden gittiyse (hırsız kaptı / düştü) ya da kontrol kaybolduysa nişan biter
        if (Aiming && (player == null || player.HeldValuable == null || player.IsDown || player.HeldStretcher != null))
        {
            Aiming = false;
            if (crew != null) crew.ReleaseAction();
        }
    }

    // Sol tık: koşuyorsa tackle, değilse itme.
    public bool Attack()
    {
        if (!CanAttack) { Rejected++; LastRejectReason = RejectReason(); return false; }
        bool running = player.RunHeld && player.HorizontalVelocity.magnitude > 1.5f;
        if (running && Time.time >= TackleReadyAt) { FaceForAttack(); StartCoroutine(TackleRoutine()); return true; }
        if (Time.time >= PushReadyAt) { FaceForAttack(); StartCoroutine(PushRoutine()); return true; }
        Rejected++;
        LastRejectReason = running ? "tackle beklemede" : "itme beklemede";
        return false;
    }

    // Test: beklemeleri sıfırla (önceki fazın düdük / tackle beklemesi yeni testi engellemesin)
    public void ResetCooldownsForTest()
    {
        PushReadyAt = -100f;
        TackleReadyAt = -100f;
        WhistleReadyAt = -100f;
    }

    private string RejectReason()
    {
        if (player == null) return "oyuncu yok";
        if (settings == null) return "ayar yok";
        if (!player.HasControl) return "kontrol yok";
        if (player.IsDown) return "yerde";
        if (player.Rope != null) return "halatta";
        if (player.HeldStretcher != null) return "sedye elde";
        if (player.Piggyback != null) return "sırtta hasta";
        if (player.HeldCoil != null) return "kangal elde";
        if (Busy) return "eylem sürüyor";
        if (Aiming) return "nişan alınıyor";
        return "?";
    }

    // U4b (kullanıcı testi: "hırsızlara vuramıyorum"): üçüncü şahıs karakter yürüdüğü yöne bakar, kameraya değil. Oyuncu durup
    // kamerayı hırsıza çevirince gövde başka yöne dönük kalıyor ve itme konisi (önündeki 1.4 m / 60°) boşa gidiyordu.
    // Saldırıda gövde önce kameranın baktığı yöne, kameranın 80° içinde ve itme menzili + 0.8 m'de bir hırsız varsa ona döner.
    private void FaceForAttack()
    {
        Vector3 look = player.cameraTransform != null ? Flat(player.cameraTransform.forward) : Flat(player.transform.forward);
        if (look.sqrMagnitude < 1e-4f) look = Flat(player.transform.forward);
        if (look.sqrMagnitude < 1e-4f) return;
        look.Normalize();
        Thief best = null;
        float bestScore = float.MaxValue;
        float range = settings.pushRange + 0.8f;
        foreach (var t in Thief.All)
        {
            if (t == null || !t.CanBeHit) continue;
            Vector3 d = Flat(t.transform.position - player.transform.position);
            float dist = d.magnitude;
            if (dist > range || dist < 1e-3f || Mathf.Abs(t.transform.position.y - player.transform.position.y) > 2f) continue;
            float ang = Vector3.Angle(look, d);
            if (ang > 80f) continue;
            float score = dist + ang * 0.01f;
            if (score < bestScore) { bestScore = score; best = t; }
        }
        Vector3 face = best != null ? Flat(best.transform.position - player.transform.position).normalized : look;
        LastFacing = best != null ? "hırsız " + best.name : "kamera";
        player.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
    }

    private IEnumerator PushRoutine()
    {
        Busy = true;
        LastAction = "push";
        Pushes++;
        PushReadyAt = Time.time + settings.pushCooldown;
        player.SuspendControl(0.45f);
        if (crew != null) crew.PlayAction(CrewAnimation.StatePushShove, PushSeconds);
        yield return new WaitForSeconds(PushHitTime);
        Vector3 fwd = Flat(player.transform.forward);
        foreach (var t in Thief.All.ToArray())
        {
            if (t == null || !t.CanBeHit || !InCone(t.transform.position, settings.pushRange, settings.pushAngle)) continue;
            Vector3 away = Flat(t.transform.position - player.transform.position);
            if (away.sqrMagnitude < 1e-4f) away = fwd;
            if (t.Hit(settings.pushDamage, settings.pushStunSeconds, away.normalized * settings.pushKnockback, "itme")) PushHits++;
        }
        Play(SynthClips.Cached("Shove", SynthClips.Shove));
        yield return new WaitForSeconds(Mathf.Max(0f, PushSeconds - PushHitTime - 0.1f));
        Busy = false;
    }

    private IEnumerator TackleRoutine()
    {
        Busy = true;
        LastAction = "tackle";
        Tackles++;
        TackleReadyAt = Time.time + settings.tackleCooldown;
        tackleHit.Clear();
        Vector3 fwd = Flat(player.transform.forward);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        // Kontrol dalış + kalkış boyunca yok. Kayma (zemine ve duvarlara saygılı, CharacterController.Move) klibin havada olduğu
        // pencereye hizalı: TackleSlideStart'ta başlar, ground_frame'de (0.7 sn) biter (U4b: önce 0'da başlıyordu; ayaklar yerdeyken
        // kayma "itme" gibi görünüyordu).
        player.SuspendControl(TackleSeconds + TackleGetUpSeconds);
        if (crew != null) crew.PlayAction(CrewAnimation.StateTackleDive, TackleSeconds + TackleGetUpSeconds);
        float t0 = Time.time;
        bool slideStarted = false;
        while (Time.time - t0 < TackleSeconds)
        {
            float u = Time.time - t0;
            if (!slideStarted && u >= TackleSlideStart)
            {
                slideStarted = true;
                player.Lunge(fwd * settings.tackleDistance, TackleSlideSeconds, TackleSeconds + TackleGetUpSeconds - u);
            }
            if (u >= TackleHitStart && u <= TackleHitEnd)
            {
                foreach (var t in Thief.All.ToArray())
                {
                    if (t == null || tackleHit.Contains(t) || !t.CanBeHit) continue;
                    Vector3 d = Flat(t.transform.position - player.transform.position);
                    bool inPath = d.magnitude <= settings.tackleHitRadius || (d.magnitude <= settings.tackleHitRadius + 0.6f && Vector3.Angle(fwd, d) <= 35f);
                    if (!inPath || Mathf.Abs(t.transform.position.y - player.transform.position.y) > 1.5f) continue;
                    tackleHit.Add(t);
                    if (t.Hit(settings.tackleDamage, settings.tackleStunSeconds, fwd * 0.8f, "tackle")) TackleHits++;
                }
            }
            yield return null;
        }
        // Tackle_GetUp Animator geçişiyle oynar (dive → getUp, çıkış zamanı)
        yield return new WaitForSeconds(TackleGetUpSeconds - 0.05f);
        Busy = false;
    }

    // Sağ tık basılı mı (her kare girdi okuyucusu verir): basınca nişan, bırakınca atış.
    public void SetThrowHeld(bool held)
    {
        if (held)
        {
            if (Aiming || !CanThrow) return;
            Aiming = true;
            LastAction = "aim";
            if (crew != null) crew.HoldAction(CrewAnimation.StateThrowAim);
        }
        else if (Aiming)
        {
            Aiming = false;
            StartCoroutine(ThrowRoutine());
        }
    }

    private IEnumerator ThrowRoutine()
    {
        Busy = true;
        LastAction = "throw";
        player.SuspendControl(0.4f);
        if (crew != null) crew.PlayAction(CrewAnimation.StateThrowRelease, ThrowReleaseSeconds);
        yield return new WaitForSeconds(ThrowReleaseTime);
        var item = player.HeldValuable;
        if (item != null && item.kind != LootKind.Equipment)
        {
            Vector3 dir = AimDirection();
            player.HeldValuable = null;
            item.Throw(dir * settings.throwSpeed, settings.throwHitStunSeconds, settings.throwHitDamage, killBelowY);
            LastThrown = item;
            Throws++;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, ThrowReleaseSeconds - ThrowReleaseTime - 0.05f));
        Busy = false;
    }

    // Bakış yönü: kamera ileri (kamera yoksa oyuncu ileri). Bakışın 20° içinde, ≤ 15 m'de bir hırsız varsa ona nişan alınır
    // (göğsüne, yerçekimi için alçak yay telafisi); yoksa bakış yönü, en az 5° yukarı.
    public Vector3 AimDirection()
    {
        Vector3 look = player.cameraTransform != null ? player.cameraTransform.forward : player.transform.forward;
        if (Flat(look).sqrMagnitude < 1e-4f) look = player.transform.forward;
        Vector3 from = player.transform.position + Vector3.up * 1.4f;
        var item = player.HeldValuable;
        if (item != null) from = item.Center;
        Thief best = null;
        float bestAngle = 20f;
        foreach (var t in Thief.All)
        {
            if (t == null || !t.CanBeHit) continue;
            Vector3 to = t.transform.position + Vector3.up * 0.9f - from;
            if (to.magnitude > 15f) continue;
            float a = Vector3.Angle(look, to);
            if (a < bestAngle) { bestAngle = a; best = t; }
        }
        if (best != null)
        {
            Vector3 to = best.transform.position + Vector3.up * 0.9f - from;
            float d = Flat(to).magnitude, dy = to.y, v = settings.throwSpeed, g = -Physics.gravity.y;
            float disc = v * v * v * v - g * (g * d * d + 2f * dy * v * v);
            float angle = disc >= 0f && d > 0.1f ? Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * d)) : Mathf.Atan2(dy, d) + 10f * Mathf.Deg2Rad;
            Vector3 flat = Flat(to).normalized;
            return (flat * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
        }
        Vector3 dir = look;
        Vector3 f = Flat(dir).normalized;
        float minUp = Mathf.Sin(5f * Mathf.Deg2Rad);
        if (dir.y < minUp) dir = (f * Mathf.Cos(5f * Mathf.Deg2Rad) + Vector3.up * minUp);
        return dir.normalized;
    }

    public bool Whistle()
    {
        if (!CanWhistle) { Rejected++; LastRejectReason = WhistleReady ? RejectReason() : "düdük beklemede"; return false; }
        StartCoroutine(WhistleRoutine());
        return true;
    }

    private IEnumerator WhistleRoutine()
    {
        Busy = true;
        LastAction = "whistle";
        Whistles++;
        WhistleReadyAt = Time.time + settings.whistleCooldown;
        player.SuspendControl(1.0f);
        if (crew != null) crew.PlayAction(CrewAnimation.StateWhistle, WhistleSeconds);
        yield return new WaitForSeconds(WhistleBlowTime);
        Play(SynthClips.Cached("Whistle", () => SynthClips.Whistle()));
        foreach (var t in Thief.All.ToArray())
        {
            if (t == null) continue;
            if (Vector3.Distance(t.transform.position, player.transform.position) > settings.whistleRadius) continue;
            if (t.Fear(settings.fearSeconds)) WhistleHits++;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, WhistleSeconds - WhistleBlowTime - 0.15f));
        Busy = false;
    }

    private bool InCone(Vector3 p, float range, float angle)
    {
        Vector3 d = Flat(p - player.transform.position);
        return d.magnitude <= range && Vector3.Angle(Flat(player.transform.forward), d) <= angle * 0.5f + 1e-3f && Mathf.Abs(p.y - player.transform.position.y) < 2f;
    }

    private void Play(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        audioSource.clip = clip;
        audioSource.Play();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
