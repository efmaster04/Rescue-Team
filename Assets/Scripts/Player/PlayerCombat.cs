using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// U4 (kullanıcı kararı "Hırsızlar", 2026-10-19): oyuncu savunması. Girdi PlayerInputReader'dan gelir (sol tık, sağ tık, Q).
// Tuş düzeni (kullanıcı isteği 2026-11-05): sol tık yumruk, sağ tık itme (elde fırlatılabilir eşya varken nişan / fırlatma), Ctrl eğilme.
//  - Sol tık: Punch_Jab; hit_frame'de (kare 7) önündeki punchRange / punchAngle içindeki hırsız punchStunSeconds sersemler,
//    elindekini düşürür, punchKnockback kadar geri teper, punchDamage hasar. Bekleme punchCooldown (kısa).
//  - Koşarken (Shift + hareket) sol tık: Tackle_Dive; oyuncu tackleDistance öne kayar (CharacterController.Move: zemine ve duvara
//    saygılı), yolundaki hırsız tackleStunSeconds sersemler; dalış bitince Tackle_GetUp ile kalkar (Animator geçişi). Bekleme tackleCooldown.
//    Eğikken tackle yok (sol tık yumruktur).
//  - Sağ tık, eli boşken (ya da elde fırlatılamayan koni / ekipman): Push_Shove; hit_frame'de (kare 9) önündeki pushRange / pushAngle
//    içindeki hırsızlar pushKnockback geri savrulur, pushStunSeconds sersemler, elindekini düşürür. Bekleme pushCooldown.
//  - Sağ tık basılı, elde tek elle fırlatılabilir eşya varken: Throw_Aim (döngü; eşya sağ elde); bırakınca Throw_Release, release_frame'de
//    (kare 8) eşya fırlar (LootItem.Throw: fizik; hırsıza isabet sersemletir; boşluğa düşerse geri döner). Basışta karar verilir
//    (SecondaryInput): eşya varsa nişan niyeti, yoksa bir itme.
//  - Eğikken (ThirdPersonPlayer.IsCrouching) yumruk, itme ve fırlatma serbest; Animator'da kollar taban katmandan, bacaklar eğik.
//    U4c: nişan alırken HUD'da ekran ortasında nişangah, gövde kameraya döner; hedef kameranın merkez ışınının çarptığı nokta
//    (yoksa 20 m ilerisi), eşya throwSpeed ile o noktaya düşecek şekilde balistik fırlatılır (menzil dışı → 45°).
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
    [Header("Yumruk (sol tık; ThiefSettings'te yok, burada)")]
    [Tooltip("Yumruk menzili (m; itme menzili kadar) ve koni açısı (derece).")]
    public float punchRange = 1.4f, punchAngle = 70f;
    [Tooltip("İsabette hırsızın sersemleme süresi (sn); elindekini düşürür.")]
    public float punchStunSeconds = 1.2f;
    [Tooltip("İsabette hırsızın geri tepmesi (m; itmenin 1.5 m'sinden küçük).")]
    public float punchKnockback = 0.5f;
    [Tooltip("Yumruk hasarı (hırsız canı 100; itme 25).")]
    public float punchDamage = 15f;
    [Tooltip("Yumruklar arası bekleme (sn; klip 0.5 sn).")]
    public float punchCooldown = 0.45f;

    // Animator durumu (Bolum1AnimBuilder kurar; Punch_Jab klibi)
    public const string StatePunchJab = "PunchJab";

    // Klip olayları (clips_meta.json; 1 tabanlı kare → (kare−1)/30 sn)
    public const float PunchSeconds = 0.5f, PunchHitTime = 6f / 30f;
    public const float PushSeconds = 0.6f, PushHitTime = 8f / 30f;
    // Tackle_Dive: 28 kare / 0.9 sn, hit_frame 12 (0.37 sn), ground_frame 22 (0.7 sn). Kayma 0.2 → 0.7 sn (havada), isabet 0.1–0.75 sn.
    public const float TackleSeconds = 0.9f, TackleGetUpSeconds = 1.0f, TackleHitStart = 0.1f, TackleHitEnd = 0.75f, TackleSlideStart = 0.2f, TackleSlideSeconds = 0.5f;
    public const float ThrowReleaseSeconds = 0.5f, ThrowReleaseTime = 7f / 30f;
    public const float WhistleSeconds = 1.2f, WhistleBlowTime = 14f / 30f;

    public bool Aiming { get; private set; }
    // Nişan ya da atış penceresi: eşya sağ elde kalır (taşıma el IK'sı çekilir)
    public bool ThrowPose { get { return Aiming || (Busy && LastAction == "throw"); } }
    // Ağır sınıf: nişansız iki elle itiş sürüyor (CarryHandIK eşyayı öne kaydırır, PushHitTime'da eşya elden çıkar)
    public bool HeavyShoveActive { get; private set; }
    public float HeavyShoveStart { get; private set; } = -100f;
    public int HeavyShoves { get; private set; }
    public bool Busy { get; private set; }
    public float PunchReadyAt { get; private set; } = -100f;
    public float PushReadyAt { get; private set; } = -100f;
    public float TackleReadyAt { get; private set; } = -100f;
    public float WhistleReadyAt { get; private set; } = -100f;
    public float WhistleRemaining { get { return Mathf.Max(0f, WhistleReadyAt - Time.time); } }
    public bool WhistleReady { get { return Time.time >= WhistleReadyAt; } }
    public string LastAction { get; private set; } = "";
    // Ölçüm
    public int Punches { get; private set; }
    public int PunchHits { get; private set; }
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
    // Ofis-garaj: fırlatılan dış eşya (HeldProp) sayısı
    public int PropThrows { get; private set; }
    // U4b tanı: son saldırıda yüz hangi yöne çevrildi (kamera / hedef hırsız)
    public string LastFacing { get; private set; } = "";

    private readonly HashSet<Thief> tackleHit = new HashSet<Thief>();
    // Sağ tık basışında elde fırlatılabilir eşya vardı: tuş basılı kaldıkça nişan (meşgulken basıldıysa eylem bitince başlar)
    private bool aimIntent;

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
    // Ofis-garaj: elde dış eşya (HeldProp, CarryableItem köprüsü) de fırlatılır.
    public bool CanThrow { get { return CanAct && HasThrowable; } }
    // Elde tek elle fırlatılabilir eşya (valiz / hasta eşyası; garajda dış eşya) var mı (koni ve ekipman değil)
    public bool HasThrowable { get { return player != null && ((player.HeldValuable != null && player.HeldValuable.kind != LootKind.Equipment) || player.HeldProp != null); } }
    public bool CanWhistle { get { return CanAct && !Aiming && WhistleReady; } }

    private void Update()
    {
        // Nişan alırken eşya elden gittiyse (hırsız kaptı / düştü) ya da kontrol kaybolduysa nişan biter
        if (Aiming && (player == null || (player.HeldValuable == null && player.HeldProp == null) || player.IsDown || player.HeldStretcher != null))
        {
            Aiming = false;
            if (crew != null) crew.ReleaseAction();
        }
    }

    // Sol tık: koşuyorsa (eğik değilken) tackle, değilse yumruk.
    public bool Attack()
    {
        if (!CanAttack) { Rejected++; LastRejectReason = RejectReason(); return false; }
        bool running = !player.IsCrouching && player.RunHeld && player.HorizontalVelocity.magnitude > 1.5f;
        if (running && Time.time >= TackleReadyAt) { FaceForAttack(); StartCoroutine(TackleRoutine()); return true; }
        if (Time.time >= PunchReadyAt) { FaceForAttack(); StartCoroutine(PunchRoutine()); return true; }
        Rejected++;
        LastRejectReason = running ? "tackle beklemede" : "yumruk beklemede";
        return false;
    }

    // Sağ tık eli boşken: itme.
    public bool Push()
    {
        if (!CanAttack) { Rejected++; LastRejectReason = RejectReason(); return false; }
        if (Time.time < PushReadyAt) { Rejected++; LastRejectReason = "itme beklemede"; return false; }
        FaceForAttack();
        StartCoroutine(PushRoutine());
        return true;
    }

    // Sağ tık (girdi okuyucusu her kare): basışta elde fırlatılabilir eşya varsa nişan niyeti (basılı kaldıkça Throw_Aim, bırakınca
    // atış), yoksa bir itme. Basılı tutmak itmeyi tekrarlamaz.
    public void SecondaryInput(bool held, bool pressedThisFrame)
    {
        if (pressedThisFrame)
        {
            aimIntent = HasThrowable;
            if (!aimIntent) Push();
        }
        if (!held) aimIntent = false;
        SetThrowHeld(held && aimIntent);
    }

    // Test: beklemeleri sıfırla (önceki fazın düdük / tackle beklemesi yeni testi engellemesin)
    public void ResetCooldownsForTest()
    {
        PunchReadyAt = -100f;
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

    // Yumruk: sağ kolla düz vuruş; isabette kısa sersemletme + eşya düşer + küçük geri tepme.
    private IEnumerator PunchRoutine()
    {
        Busy = true;
        LastAction = "punch";
        Punches++;
        PunchReadyAt = Time.time + punchCooldown;
        player.SuspendControl(0.3f);
        if (crew != null) crew.PlayAction(StatePunchJab, PunchSeconds);
        yield return new WaitForSeconds(PunchHitTime);
        Vector3 fwd = Flat(player.transform.forward);
        bool hit = false;
        foreach (var t in Thief.All.ToArray())
        {
            if (t == null || !t.CanBeHit || !InCone(t.transform.position, punchRange, punchAngle)) continue;
            Vector3 away = Flat(t.transform.position - player.transform.position);
            if (away.sqrMagnitude < 1e-4f) away = fwd;
            if (t.Hit(punchDamage, punchStunSeconds, away.normalized * punchKnockback, "yumruk")) { PunchHits++; hit = true; }
        }
        if (hit) Play(SynthClips.Cached("Punch", SynthClips.Punch));
        yield return new WaitForSeconds(Mathf.Max(0f, PunchSeconds - PunchHitTime - 0.15f));
        Busy = false;
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
            // Ağır eşya nişan almaz, tek elle kaldırılamaz: basışta kucağın önüne iki elle itilir
            if (player.HeldClass == CarryClass.Heavy) { StartCoroutine(HeavyShoveRoutine()); return; }
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

    // Nişanı fırlatmadan iptal eder (UI açılması gibi durumlarda tuş bırakılmış sayılmasın).
    public void CancelAim()
    {
        aimIntent = false;
        if (!Aiming) return;
        Aiming = false;
        if (crew != null) crew.ReleaseAction();
    }

    private IEnumerator HeavyShoveRoutine()
    {
        Busy = true;
        LastAction = "shove-drop";
        HeavyShoves++;
        HeavyShoveActive = true;
        HeavyShoveStart = Time.time;
        player.SuspendControl(PushSeconds - 0.1f);
        // İki avuçla itiş klibi; eller eşyada kalır, eşya CarryHandIK ile öne kayar
        if (crew != null) crew.PlayAction(CrewAnimation.StatePushShove, PushSeconds);
        yield return new WaitForSeconds(PushHitTime);
        HeavyShoveActive = false;
        var rules = player.Rules;
        Vector3 fwd = Flat(player.transform.forward);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        Vector3 velocity = fwd.normalized * rules.heavyShoveSpeed + Vector3.up * rules.heavyShoveUpSpeed;
        var item = player.HeldValuable;
        var prop = player.HeldProp;
        if (item != null && item.kind != LootKind.Equipment)
        {
            player.HeldValuable = null;
            item.Throw(velocity, 0f, 0f, killBelowY);
            LastThrown = item;
            Throws++;
        }
        else if (prop != null)
        {
            player.HeldProp = null;
            prop.ThrowFrom(player, velocity);
            PropThrows++;
            Throws++;
        }
        Play(SynthClips.Cached("Shove", SynthClips.Shove));
        yield return new WaitForSeconds(Mathf.Max(0f, PushSeconds - PushHitTime - 0.1f));
        Busy = false;
    }

    // Eldeki eşyanın fırlatma hızı (nişanlı sınıflar; balistik ve nişan aynı değeri kullanır)
    public float CurrentThrowSpeed { get { return player != null && settings != null ? player.Rules.ThrowSpeed(player.HeldClass, settings.throwSpeed) : 0f; } }

    private IEnumerator ThrowRoutine()
    {
        Busy = true;
        LastAction = "throw";
        player.SuspendControl(0.4f);
        if (crew != null) crew.PlayAction(CrewAnimation.StateThrowRelease, ThrowReleaseSeconds);
        yield return new WaitForSeconds(ThrowReleaseTime);
        var item = player.HeldValuable;
        var prop = player.HeldProp;
        float throwSpeed = CurrentThrowSpeed;
        if (item != null && item.kind != LootKind.Equipment)
        {
            Vector3 target = AimPoint();
            Vector3 dir = BallisticDirection(item.Center, target, throwSpeed, out string arc);
            LastAimArc = arc;
            player.HeldValuable = null;
            item.Throw(dir * throwSpeed, settings.throwHitStunSeconds, settings.throwHitDamage, killBelowY);
            LastThrown = item;
            Throws++;
        }
        else if (prop != null)
        {
            // Ofis-garaj eşyası: aynı balistik hedef, fırlatmayı köprü yapar (fizik gövdesi eşyanın kendisinde)
            Vector3 target = AimPoint();
            Vector3 dir = BallisticDirection(prop.Center, target, throwSpeed, out string arc);
            LastAimArc = arc;
            player.HeldProp = null;
            prop.ThrowFrom(player, dir * throwSpeed);
            PropThrows++;
            Throws++;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, ThrowReleaseSeconds - ThrowReleaseTime - 0.05f));
        Busy = false;
    }

    // U4c (kullanıcı kararı 2): nişan alırken gövde kameranın baktığı yöne döner (FaceForAttack deseni, her kare; hareket dönüşünü ezer).
    // LateUpdate: ThirdPersonPlayer.Update'ten sonra, LootItem.LateUpdate'ten (sıra 60) önce → eşya elde doğru yerde kalır.
    private void LateUpdate()
    {
        if (!Aiming || player == null) return;
        Vector3 look = player.cameraTransform != null ? Flat(player.cameraTransform.forward) : Vector3.zero;
        if (look.sqrMagnitude < 1e-4f) return;
        player.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
    }

    // U4c tanı: son nişan hedefi (dünya noktası), ışının çarptığı şey ("<collider adı>" / "boşluk 20 m") ve yay ("alçak" / "45°")
    public Vector3 LastAimPoint { get; private set; }
    public string LastAimHit { get; private set; } = "";
    public string LastAimArc { get; private set; } = "";
    public const float AimRayLength = 60f, AimEmptyDistance = 20f;
    private static readonly RaycastHit[] aimHits = new RaycastHit[32];

    // U4c (kullanıcı kararı 2): hedef = kameranın MERKEZ ışınının çarptığı nokta (oyuncunun kendisi ve tetikleyiciler hariç;
    // çarpmazsa kameranın AimEmptyDistance m ilerisi). Eşya throwSpeed ile o noktaya düşecek şekilde fırlatılır (balistik, alçak yay);
    // menzil dışındaysa 45° (en uzak atış). Kamera yoksa oyuncu ileri.
    public Vector3 AimPoint()
    {
        Transform cam = player.cameraTransform;
        Vector3 origin = cam != null ? cam.position : player.transform.position + Vector3.up * 1.4f;
        Vector3 look = cam != null ? cam.forward : player.transform.forward;
        if (look.sqrMagnitude < 1e-6f) look = player.transform.forward;
        look.Normalize();
        int n = Physics.RaycastNonAlloc(origin, look, aimHits, AimRayLength, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        string hitName = "";
        Vector3 point = origin + look * AimEmptyDistance;
        for (int i = 0; i < n; i++)
        {
            var h = aimHits[i];
            if (h.collider == null || h.distance >= best) continue;
            if (h.collider.transform.IsChildOf(player.transform)) continue; // kendi gövdesi
            var held = player.HeldValuable;
            if (held != null && h.collider.transform.IsChildOf(held.transform)) continue;
            best = h.distance;
            point = h.point;
            hitName = h.collider.transform.root.name + "/" + h.collider.name;
        }
        LastAimHit = best < float.MaxValue ? hitName + " " + best.ToString("F1") + " m" : "boşluk " + AimEmptyDistance.ToString("F0") + " m";
        LastAimPoint = point;
        return point;
    }

    public Vector3 AimDirection()
    {
        Vector3 target = AimPoint();
        Vector3 from = player.transform.position + Vector3.up * 1.4f;
        var item = player.HeldValuable;
        if (item != null) from = item.Center;
        else if (player.HeldProp != null) from = player.HeldProp.Center;
        return BallisticDirection(from, target, CurrentThrowSpeed, out string arc);
    }

    // Başlangıç hızı v ile from → target balistik yön: alçak yay; çözüm yoksa hedefe doğru 45° (menzil dışı). arc: "alçak" / "45°".
    public static Vector3 BallisticDirection(Vector3 from, Vector3 target, float v, out string arc)
    {
        Vector3 to = target - from;
        Vector3 flat = Flat(to);
        float d = flat.magnitude, dy = to.y, g = -Physics.gravity.y;
        if (d < 0.1f) { arc = "dik"; return to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.up; }
        flat /= d;
        float disc = v * v * v * v - g * (g * d * d + 2f * dy * v * v);
        float angle;
        if (disc >= 0f) { angle = Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * d)); arc = "alçak"; }
        else { angle = 45f * Mathf.Deg2Rad; arc = "45°"; }
        return (flat * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
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
