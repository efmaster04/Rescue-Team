using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Sürüş girişi kabul testi (kullanıcı kararı 2026-11-04). Batchmode:
// Unity.exe -batchmode -projectPath ... -executeMethod Bolum1DriveVerify.Run -logFile <dosya>
// Başarıda "DRIVE VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/Bolum1/drive_verify_report.txt
//
// SESSİZLİK: Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0.
// Senaryo: (a) ekip minibüste yolda başlar; (b) betikli gaz / direksiyon minibüsü sürer, park alanına park eder, E ile iniş:
// ekip dışarıda, minibüs statik, NavMesh oyuk, ekipman içeride; (c) şeritte bırakılan minibüs: araç durur, ~5 sn bekler, çarpıp
// iter, hasar sonuca (FieldPayout.vehicleWear) ulaşır; (d) karşı şeride taşan minibüs: o şeridin aracı bekler.
// Öbür doğrulamalar (ambulans, animasyon, trafik, bölüm, FP, görev köprüsü, ofis-garaj) sürüş girişini ATLAR (DriveIntro.SkipForTests):
// eski başlangıç (minibüs parkta, ekip PlayerSpawn'da) korunur; bayrak sahne yüklenmeden önce burada ayarlanır.
[InitializeOnLoad]
public static class Bolum1DriveVerify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "Bolum1DriveVerify.Stage";
    private const string ReportKey = "Bolum1DriveVerify.Report";
    private const string OkKey = "Bolum1DriveVerify.Ok";
    private const string BatchKey = "Bolum1DriveVerify.Batch";
    // Editör menüsünden koşan öbür doğrulamalar (batchmode dışı). Batchmode'da liste gerekmez: sürüş girişi varsayılan atlanır.
    private static readonly string[] OtherStageKeys =
    {
        "Bolum1AmbulanceVerify.Stage", "Bolum1AnimVerify.Stage", "Bolum1TrafficVerify.Stage", "Bolum1Verify.Stage",
        "Bolum1FirstPersonVerify.Stage", "MissionBridgeVerify.Stage", "OfficeGarageVerify.Stage", "Bolum1StretcherSoloVerify.Stage",
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static Bolum1DriveVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
        // Edit modunda derleme sonrası bayat doğrulama anahtarları temizlenir: elle durdurulan / yarım kalan bir test bir sonraki
        // Play'de sürüş girişini sessizce atlamasın (review P2-5). Play'e girerken (test başlarken) dokunulmaz.
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !Application.isBatchMode)
        {
            foreach (var k in OtherStageKeys)
                if (!string.IsNullOrEmpty(SessionState.GetString(k, ""))) { Debug.Log("Bolum1DriveVerify: bayat doğrulama anahtarı temizlendi: " + k); SessionState.EraseString(k); }
            if (!string.IsNullOrEmpty(SessionState.GetString(StageKey, ""))) SessionState.EraseString(StageKey);
        }
    }

    // Sürüş girişi yalnız bu doğrulamada ve normal oyun girişinde yaşar. Batchmode'daki HER test (yeni yazılanlar dahil, anahtar
    // listesine girmeseler de) ve editörde koşan bilinen doğrulamalar girişi atlar (eski başlangıç: minibüs parkta, ekip PlayerSpawn'da).
    // Ölçüldü: Bolum1StretcherSoloVerify listede yokken ekip minibüste oturdu, oyuncu yürümedi, sedye kalkmadı (4 FAIL).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void SkipIntroInOtherTests()
    {
        bool mine = SessionState.GetString(StageKey, "") == "play";
        bool other = OtherStageKeys.Any(k => !string.IsNullOrEmpty(SessionState.GetString(k, "")));
        DriveIntro.SkipForTests = !mine && (Application.isBatchMode || other);
    }

    [MenuItem("Rescue Team/Bölüm 1/Sürüş Girişi: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("DRIVE VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            CheckEditScene();
        }
        catch (Exception e)
        {
            Fail("edit aşaması: " + e);
            batchMode = batch;
            Finish();
            return;
        }
        SessionState.SetString(ReportKey, report.ToString());
        SessionState.SetBool(OkKey, allOk);
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetString(StageKey, "play");
        Bolum1AmbulanceAudioMute.Apply(); // SESSİZLİK
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    // ---------- edit aşaması ----------
    private static void CheckEditScene()
    {
        var van = GameObject.Find(Bolum1SceneBuilder.ServiceVanName);
        var driver = van != null ? van.GetComponent<ServiceVanDriver>() : null;
        var rb = van != null ? van.GetComponent<Rigidbody>() : null;
        var obstacle = van != null ? van.GetComponent<NavMeshObstacle>() : null;
        var zoneGo = GameObject.Find(Bolum1DriveBuilder.ZoneName);
        var zone = zoneGo != null ? zoneGo.GetComponent<ParkingZone>() : null;
        var outline = zoneGo != null ? zoneGo.transform.Find(Bolum1DriveBuilder.OutlineName) : null;
        var intro = GameObject.Find(Bolum1DriveBuilder.IntroName)?.GetComponent<DriveIntro>();
        var player = GameObject.Find(Bolum1SceneBuilder.CharacterName)?.GetComponent<ThirdPersonPlayer>();
        bool driverOk = driver != null && driver.player == player && driver.bot != null && driver.orbitCamera != null && driver.parkingZone == zone
            && driver.driverSeat != null && driver.passengerSeat != null && driver.wheelPointsLocal.Length == 4 && driver.steeringWheel != null && driver.sound != null;
        bool physicsOk = rb != null && rb.isKinematic && !rb.useGravity && obstacle != null && obstacle.carving && obstacle.carveOnlyStationary;
        bool zoneOk = zone != null && zone.lanes.Length == 2 && zone.lanes.All(l => l != null) && zone.halfExtents.x > 1.3f && zone.halfExtents.z > 3.3f
            && outline != null && outline.GetComponent<MeshFilter>()?.sharedMesh != null && outline.GetComponent<MeshFilter>().sharedMesh.vertexCount > 16;
        bool introOk = intro != null && intro.van == driver && intro.lane != null && intro.mission != null && intro.thieves != null && intro.startS > 0f;
        Check("sahne: minibüste sürücü + kinematik Rigidbody + NavMeshObstacle (oyma), park alanı + sarı çerçeve, DriveIntro",
            driverOk && physicsOk && zoneOk && introOk,
            "sürücü " + driverOk + ", fizik " + physicsOk + ", alan " + zoneOk + (zone != null ? " (yarı " + zone.halfExtents.x.ToString("F2", Inv) + " × " + zone.halfExtents.z.ToString("F2", Inv) + ")" : "")
            + ", giriş " + introOk + (intro != null ? " (s " + intro.startS.ToString(Inv) + ")" : ""));

        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Bolum1AmbulanceBuilder.LocomotionControllerPath);
        var states = ac != null ? ac.layers[0].stateMachine.states.Select(s => s.state).ToList() : new List<AnimatorState>();
        var drive = states.FirstOrDefault(s => s.name == ServiceVanDriver.StateDriveSeated);
        var passenger = states.FirstOrDefault(s => s.name == ServiceVanDriver.StatePassengerSeated);
        bool param = ac != null && ac.parameters.Any(p => p.name == ServiceVanDriver.ParamSeated && p.type == AnimatorControllerParameterType.Int);
        bool clipsReal = drive != null && passenger != null && drive.motion != null && passenger.motion != null
            && drive.motion.name == Bolum1AnimBuilder.DriveSeatedClip && passenger.motion.name == Bolum1AnimBuilder.PassengerSeatedClip;
        bool loops = clipsReal && AnimationUtility.GetAnimationClipSettings((AnimationClip)drive.motion).loopTime && AnimationUtility.GetAnimationClipSettings((AnimationClip)passenger.motion).loopTime;
        Check("Animator: Drive_Seated / Passenger_Seated durumları gerçek döngü klipleriyle, Seated parametresi",
            param && clipsReal && loops,
            "parametre " + param + ", klipler " + (drive != null && drive.motion != null ? drive.motion.name : "YOK") + " / " + (passenger != null && passenger.motion != null ? passenger.motion.name : "YOK") + ", döngü " + loops);

        var s = Bolum1TrafficBuilder.Settings;
        Check("TrafficSettings: minibüs bekleme 5 sn, çarpma hızı / hasarı / itmesi > 0",
            s != null && Mathf.Approximately(s.vanWaitSeconds, 5f) && s.vanRamSpeed > 0f && s.vanRamDamage > 0f && s.vanRamPushSpeed > 0f,
            s != null ? "bekleme " + s.vanWaitSeconds.ToString(Inv) + " sn, pay " + s.vanStopMargin.ToString(Inv) + " m, çarpma " + s.vanRamSpeed.ToString(Inv) + " m/sn, hasar %" + s.vanRamDamage.ToString(Inv) + ", itme " + s.vanRamPushSpeed.ToString(Inv) + " m/sn" : "ayar YOK");
    }

    // ---------- Play aşaması ----------
    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition;

    private static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.volume = 0f; // test boyunca hoparlörden ses çıkmaz
        if (scenario == null)
        {
            if (realStart < 0f) { realStart = Time.realtimeSinceStartup; return; }
            if (Time.frameCount < 5) return;
            report = new StringBuilder(SessionState.GetString(ReportKey, ""));
            allOk = SessionState.GetBool(OkKey, false);
            batchMode = SessionState.GetBool(BatchKey, true);
            scenario = Scenario();
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 1200f) { Fail("zaman aşımı (20 dk)"); End(); return; }

        try
        {
            if (waitCondition is float until && Time.time < until) return;
            if (waitCondition is Func<bool> cond && !cond()) return;
            waitCondition = null;
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                waitCondition = top.Current;
                break;
            }
            if (stack.Count == 0) End();
        }
        catch (Exception e)
        {
            Fail("play aşaması: " + e);
            End();
        }
    }

    private static void End()
    {
        EditorApplication.update -= PlayTick;
        SessionState.EraseString(StageKey);
        Time.timeScale = 1f;
        scenario = null;
        Finish();
    }

    private static void Finish()
    {
        CheckCompileErrors();
        Line(allOk ? "DRIVE VERIFY OK" : "DRIVE VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "drive_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "DRIVE VERIFY OK" : "DRIVE VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private static float Seconds(float s) { return Time.time + s; }
    private static IEnumerator Until(Func<bool> cond, float timeoutGameSeconds, string what)
    {
        float end = Time.time + timeoutGameSeconds;
        yield return (Func<bool>)(() => cond() || Time.time >= end);
        if (!cond()) throw new Exception("beklenen olmadı: " + what);
    }

    // ---- sahne erişimi ----
    private static MissionDirector Mission { get { return MissionDirector.Instance; } }
    private static TrafficDirector Traffic { get { return TrafficDirector.Instance; } }
    private static ServiceVanDriver Van { get { return ServiceVanDriver.Instance; } }
    private static DriveIntro Intro { get { return DriveIntro.Instance; } }
    private static ThirdPersonPlayer player;
    private static StretcherHelperBot bot;
    private static LanePath east, west;
    private static ParkingZone zone;

    private static IEnumerator Scenario()
    {
        yield return null;
        player = GameObject.Find(Bolum1SceneBuilder.CharacterName).GetComponent<ThirdPersonPlayer>();
        bot = UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>();
        east = GameObject.Find(Bolum1AmbulanceBuilder.LaneName).GetComponent<LanePath>();
        west = GameObject.Find(Bolum1TrafficBuilder.WestLaneName).GetComponent<LanePath>();
        zone = GameObject.Find(Bolum1DriveBuilder.ZoneName).GetComponent<ParkingZone>();
        // Trafik ve hırsızlar denetimli: rastgele araç çıkmaz, araçlar ForceSpawn ile verilir
        ThiefDirector.SuppressedForTests = true;
        var settings = UnityEngine.Object.Instantiate(Bolum1TrafficBuilder.Settings);
        settings.useFixedSeed = true;
        settings.calmSeconds = new Vector2(99999f, 99999f);
        settings.calmSingleVehicleChance = 0f;
        Traffic.Begin(settings);
        // Ambulans bu testte gelmesin (görev saati inişten sonra işler; çarpma sahneleri EastBound'da)
        var schedule = UnityEngine.Object.Instantiate(Mission.schedule);
        schedule.firstArrival = 99999f;
        Mission.Begin(schedule);
        Line("PLAY: hırsızlar kapalı, trafik yalnız ForceSpawn, ambulans yok; minibüs " + V(Van.transform.position) + " durum " + Van.State);

        yield return PhaseStart();
        yield return PhaseDriveAndPark();
        yield return PhaseReboard();
        yield return PhaseFall();
        yield return PhaseParkingConsistency();
        yield return PhaseBadParkRam();
        yield return PhaseOncomingLane();
        yield return PhaseAmbulanceBlocked();
    }

    // (a) Başlangıç: ekip minibüste, minibüs EastBound'da s ≈ startS, görev saati ve hırsızlar durmuş.
    private static IEnumerator PhaseStart()
    {
        float elapsed0 = Mission.Elapsed;
        yield return Seconds(1f);
        float s = east.Project(Van.transform.position, out float lateral, out _);
        string seatDetail = "içeride " + Van.CrewInside + ", oyuncu çocuk " + player.transform.IsChildOf(Van.transform) + " kapalı " + !player.enabled + " CC kapalı " + !player.GetComponent<CharacterController>().enabled
            + ", bot çocuk " + bot.transform.IsChildOf(Van.transform) + " kapalı " + !bot.enabled + "; kesişen: " + string.Join(",", Van.OverlapReport()) + "; darbe " + Van.Impacts + " hasar %" + Van.Damage.ToString("F1", Inv);
        bool seated = Van.CrewInside && player.transform.IsChildOf(Van.transform) && bot.transform.IsChildOf(Van.transform) && !player.enabled && !bot.enabled
            && !player.GetComponent<CharacterController>().enabled;
        bool onLane = Mathf.Abs(lateral) < .6f && Mathf.Abs(s - Intro.ChosenStartS) < 1.5f && Vector3.Dot(Van.Forward, east.TangentAt(s)) > .95f;
        bool held = Mission.ClockHeld && Mathf.Approximately(Mission.Elapsed, elapsed0) && ThiefDirector.Instance != null && !ThiefDirector.Instance.enabled;
        bool cargo = Van.CargoHeld && Van.HeldCargoCount >= 7; // 2 sedye + 4 koni + kangal
        var animator = player.GetComponent<Animator>();
        bool seatedAnim = animator.GetInteger(ServiceVanDriver.ParamSeated) == 1 && animator.GetCurrentAnimatorStateInfo(0).IsName(ServiceVanDriver.StateDriveSeated)
            && bot.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName(ServiceVanDriver.StatePassengerSeated);
        Check("(a) ekip minibüste yolda başlar: oyuncu şoför / bot yolcu koltuğunda (oturma döngüsü), minibüs EastBound ortasında s ≈ " + Intro.ChosenStartS.ToString("F1", Inv) + " (istenen " + Intro.startS.ToString(Inv) + "), dinamik; görev saati ve hırsızlar durmuş; yük bağlı",
            seated && onLane && held && cargo && Van.State == ServiceVanDriver.VanState.Driving && !Van.Body.isKinematic && seatedAnim,
            "oturma " + seated + " (" + seatDetail + "; anim " + seatedAnim + "), şerit s " + s.ToString("F1", Inv) + " yanal " + lateral.ToString("F2", Inv) + " m → " + onLane + ", saat durdu " + held + " (Elapsed " + Mission.Elapsed.ToString("F2", Inv)
            + "), yük " + Van.HeldCargoCount + " parça, durum " + Van.State + ", kamera hedef " + (Van.orbitCamera != null ? Van.orbitCamera.target.name : "YOK") + " uzaklık " + (Van.orbitCamera != null ? Van.orbitCamera.distance.ToString("F1", Inv) : "-"));
        yield return CaptureSeats();
        Van.SetTestInput(1f, 0f);
        yield return Seconds(1.5f);
        bool movingNoExit = Mathf.Abs(Van.Speed) > 2f && !Van.TryExit() && GameNotice.LastMessage == ServiceVanDriver.NoExitWhileMovingText;
        Van.SetTestInput(0f, 0f);
        Check("(a2) hareket hâlindeyken E ile iniş reddedilir (uyarı)", movingNoExit && Van.CrewInside,
            "1.5 sn gazdan sonra hız " + Van.Speed.ToString("F1", Inv) + " m/sn → red " + movingNoExit + ", uyarı '" + GameNotice.LastMessage + "'");
        // Serbest imleç (editörde Esc): açık UI yokken sol tık yeniden kilitler ve o kare saldırı sayılmaz; UI açıkken dokunmaz
        var orbit = Van.orbitCamera;
        var reader = player.GetComponent<PlayerInputReader>();
        bool relocked = orbit.TryRelockCursor() && orbit.RelockedThisFrame && orbit.Relocks == 1;
        yield return null;
        bool cleared = !orbit.RelockedThisFrame;
        reader.InputBlocked = true;
        bool blockedNoRelock = !orbit.TryRelockCursor() && orbit.Relocks == 1;
        reader.InputBlocked = false;
        Check("(a3) serbest imleç: UI yokken sol tık imleci yeniden kilitler (o kare saldırı / fırlatma sayılmaz), UI açıkken kilitlemez",
            relocked && cleared && blockedNoRelock, "kilitlendi " + relocked + ", sonraki kare temiz " + cleared + ", UI açıkken red " + blockedNoRelock);
    }

    // (r) Yeniden binme (kullanıcı şikâyeti: inince bir daha binilemiyordu): kapı yakınında ipucu + vurgu, eli doluyken binme
    // önerilmez (önce bırak), E ile biner (bot yakınsa yolcu), sürer, kapılar sürerken kapalı, E ile iner; ikinci iniş görev saatini
    // yeniden başlatmaz, park sonucu yeni konuma göre; bot uzaktaysa oyuncu yalnız sürer.
    private static IEnumerator PhaseReboard()
    {
        var interaction = player.GetComponent<PlayerInteraction>();
        var highlight = player.GetComponent<InteractionHighlight>();
        var respawn = player.GetComponent<PlayerFallRespawn>();
        float exitTime0 = Intro.ExitTime;
        int exits0 = Van.Exits;
        float vanS = 60f;
        Van.TeleportTo(east.PointAt(vanS), Quaternion.LookRotation(-east.TangentAt(vanS), Vector3.up), true);
        yield return Seconds(.3f);
        bool doorsOpenParked = Van.RearDoorsOpen;
        IEnumerator ToDoor()
        {
            Vector3 at = Van.DriverDoorPoint;
            if (GroundProbe.Find(at + Vector3.up * 1f, 4f, out RaycastHit g)) at = g.point + Vector3.up * .02f;
            Vector3 face = Flat(Van.transform.position - at).normalized;
            player.Teleport(at, Quaternion.LookRotation(face, Vector3.up));
            yield return Seconds(.3f);
        }
        // 1) Kapı yakını: ipucu ve şoför kapısı vurgusu; uzakta ipucu yok
        yield return ToDoor();
        bool hintOk = interaction.CurrentHint == PlayerInteraction.HintBoardVan && highlight.Current == Van.DriverDoor && highlight.CopyCount > 0;
        string hintNear = interaction.CurrentHint;
        player.Teleport(Van.DriverDoorPoint + Flat(Van.Forward) * 6f + Vector3.up * .02f, player.transform.rotation);
        yield return Seconds(.3f);
        bool farNoHint = interaction.CurrentHint != PlayerInteraction.HintBoardVan;
        // 2) Eli dolu (koni): binme önerilmez; koniyi bırakınca önerilir
        // TrafficCone denetleyicisi görselin (ConeNames) üstünde değil ayrı objededir: görseli hedefleyen denetleyici aranır
        var coneVisuals = Bolum1ServiceVan.ConeNames.Select(n => GameObject.Find(n)).Where(c => c != null).Select(c => c.transform).ToList();
        var cone = TrafficCone.All.FirstOrDefault(c => c != null && !c.IsHeld && (coneVisuals.Contains(c.target) || coneVisuals.Contains(c.transform)));
        bool heldNoBoard = false;
        string hintHeld = "";
        if (cone != null)
        {
            player.HeldCone = cone;
            cone.PickUp(player);
            yield return ToDoor();
            hintHeld = interaction.CurrentHint;
            heldNoBoard = interaction.CurrentHint != PlayerInteraction.HintBoardVan && player.HeldCone == cone;
            player.HeldCone = null;
            // Koni uzağa (park alanına) konur: kapının önünde kalsaydı "Koniyi al" binmeden önce gelirdi (öncelik sırası)
            Vector3 coneAt = zone.transform.position;
            if (GroundProbe.Find(coneAt + Vector3.up * 1f, 6f, out RaycastHit cg)) coneAt = cg.point;
            cone.PlaceAt(coneAt, 0f);
            yield return Seconds(.3f);
            heldNoBoard &= interaction.CurrentHint == PlayerInteraction.HintBoardVan;
            hintHeld += "' → bırakınca '" + interaction.CurrentHint + "' (binilebilir " + Van.CanBoard() + ", kapıda " + Van.PlayerAtDriverDoor() + ", koni elde " + (player.HeldCone != null) + ", koni tutuluyor " + cone.IsHeld;
        }
        Check("(r1) şoför kapısının yanında 'E: Minibüse bin' ipucu + kapı vurgusu; uzakta yok; eli doluyken (koni) binme önerilmez, bırakınca önerilir",
            doorsOpenParked && hintOk && farNoHint && cone != null && heldNoBoard,
            "parkta kapılar açık " + doorsOpenParked + ", yakın ipucu '" + hintNear + "' vurgu " + (highlight.Current != null ? highlight.Current.name : "-") + " → " + hintOk + ", uzakta yok " + farNoHint + ", koniyle ipucu '" + hintHeld + "' → " + heldNoBoard);

        // 3) Bot yakın: E ile ikisi de biner; binişten hemen sonra E inişe dönüşmez; kapılar sürerken kapanır
        bot.transform.position = Van.transform.TransformPoint(new Vector3(0f, 0f, Van.bodyMax.z + 3f));
        if (GroundProbe.Find(bot.transform.position + Vector3.up * 1f, 4f, out RaycastHit bg)) bot.transform.position = bg.point + Vector3.up * .02f;
        yield return ToDoor();
        float elapsedBefore = Mission.Elapsed;
        interaction.Interact();
        bool boarded = Van.CrewInside && Van.Boardings == 1 && Van.BotSeated && Van.State == ServiceVanDriver.VanState.Driving && player.transform.IsChildOf(Van.transform)
            && bot.transform.IsChildOf(Van.transform) && !player.enabled && !respawn.enabled && GameNotice.LastMessage == ServiceVanDriver.BoardedText && !Mission.ClockHeld && Intro.Done;
        bool lockout = !Van.TryExit() && Van.CrewInside;
        yield return Until(() => Van.RearDoorsClosed, 2f, "arka kapılar kapanır");
        float doorAngle = Van.RearDoorOpenFraction;
        Vector3 driveFrom = Van.transform.position;
        // Şeridi izleyerek 12 m sür (bu kesimde korkuluk şeridin sağ kenarına bitişik: düz gaz korkuluğa çarpıyordu)
        float sFrom = east.Project(driveFrom);
        bool drove = false;
        yield return DriveTo(() => east.PointAt(Mathf.Min(east.Length, east.Project(Van.transform.position) + 8f)), 8f, 0f,
            () => { drove |= Van.Speed > 2f && Van.RearDoorsClosed && Van.CrewInside; return east.Project(Van.transform.position) >= sFrom + 12f; }, 15f, "yeniden sürüş");
        string driveDetail = DriveState(driveFrom);
        Van.SetTestInput(-1f, 0f);
        yield return Until(() => Mathf.Abs(Van.Speed) < .05f, 6f, "fren");
        Van.SetTestInput(0f, 0f);
        yield return Seconds(.5f);
        bool clockRan = Mission.Elapsed > elapsedBefore + 1f && !Mission.ClockHeld;
        Check("(r2) E ile yeniden biniş: oyuncu şoför, yakındaki bot yolcu, sürüş başlar, binişten hemen sonra E yok sayılır, arka kapılar sürerken kapalı, görev saati tutulmaz",
            boarded && lockout && drove && clockRan,
            "biniş " + boarded + " (içeride " + Van.CrewInside + ", bot " + Van.BotSeated + ", oyuncu yeniden doğma kapalı " + !respawn.enabled + ", bildirim '" + GameNotice.LastMessage + "'), E kilidi " + lockout + ", sürdü " + drove + " (kapı açıklığı " + doorAngle.ToString("F2", Inv) + "; " + driveDetail + "), saat işledi " + clockRan);

        // 4) İkinci iniş: görev yeniden başlamaz (ExitTime aynı), park değerlendirmesi yeni konuma göre (şeritte → kötü park), kapılar açılır
        bool exited = Van.TryExit();
        yield return Until(() => Van.RearDoorsOpen, 2f, "arka kapılar açılır");
        yield return Seconds(.5f);
        bool outAgain = exited && !Van.CrewInside && Van.Exits == exits0 + 1 && player.enabled && bot.enabled && respawn.enabled && !player.transform.IsChildOf(Van.transform) && !bot.transform.IsChildOf(Van.transform)
            && Van.State == ServiceVanDriver.VanState.Parked;
        bool noRestart = Mathf.Approximately(Intro.ExitTime, exitTime0) && Intro.Done && !Mission.ClockHeld && !Intro.Active;
        bool parkEval = !Van.GoodParking && Van.BlocksLane(east, out _, out _, out _) && GameNotice.LastMessage.StartsWith(Intro.badParkText);
        Check("(r3) ikinci iniş: ekip dışarıda, görev saati yeniden başlamaz (ilk iniş zamanı korunur), park sonucu yeni konuma göre (şeritte: kötü park), kapılar açık",
            outAgain && noRestart && parkEval && Van.RearDoorsOpen,
            "iniş " + outAgain + ", yeniden başlamadı " + noRestart + " (ilk iniş " + exitTime0.ToString("F1", Inv) + " → " + Intro.ExitTime.ToString("F1", Inv) + "), park " + parkEval + " ('" + GameNotice.LastMessage + "'), kapılar açık " + Van.RearDoorsOpen);

        // 5) Bot uzakta: oyuncu yalnız biner ve sürer
        Vector3 botFar = east.PointAt(Mathf.Max(0f, vanS - 40f)) + Vector3.up * .02f;
        bot.transform.position = botFar;
        yield return ToDoor();
        interaction.Interact();
        bool soloBoard = Van.CrewInside && !Van.BotSeated && !bot.transform.IsChildOf(Van.transform) && bot.enabled;
        yield return Seconds(.6f);
        Vector3 soloFrom = Van.transform.position;
        float soloS = east.Project(soloFrom);
        bool soloDrove = false;
        yield return DriveTo(() => east.PointAt(Mathf.Min(east.Length, east.Project(Van.transform.position) + 8f)), 6f, 0f,
            () => { soloDrove |= Van.Speed > 1.5f && Van.CrewInside; return east.Project(Van.transform.position) >= soloS + 8f; }, 15f, "yalnız sürüş");
        string soloDetail = DriveState(soloFrom);
        Van.SetTestInput(-1f, 0f);
        yield return Until(() => Mathf.Abs(Van.Speed) < .05f, 6f, "fren");
        Van.SetTestInput(0f, 0f);
        yield return Seconds(.4f);
        bool soloExit = Van.TryExit() && !Van.CrewInside && Vector3.Distance(bot.transform.position, botFar) < 1f;
        Check("(r4) bot uzaktayken oyuncu yalnız biner, sürer ve iner (bot yerinde kalır)", soloBoard && soloDrove && soloExit,
            "yalnız biniş " + soloBoard + ", sürdü " + soloDrove + " (" + soloDetail + "), iniş " + soloExit);
        // Temizlik: bot oyuncunun yanına
        bot.transform.position = player.transform.position + Flat(Van.Forward) * 1.5f;
        yield return null;
    }

    // Sürüş tanısı: hız, gaz, durum, fizik, yer değiştirme, kesişenler
    private static string DriveState(Vector3 from)
    {
        return "hız " + Van.Speed.ToString("F2", Inv) + " m/sn, gaz " + Van.Throttle.ToString("F1", Inv) + ", durum " + Van.State + ", kinematik " + Van.Body.isKinematic + ", rb hız " + V(Van.Body.linearVelocity)
            + ", yol " + Vector3.Distance(from, Van.transform.position).ToString("F2", Inv) + " m, darbe " + Van.Impacts + ", düşüyor " + Van.Falling + ", içeride " + Van.CrewInside + ", kesişen [" + string.Join(",", Van.OverlapReport()) + "]";
    }

    // (f) Zemin bitince: dört tekerlek de boştayken minibüs yerçekimiyle düşer; plan (fallBelowY) altına inince ekip ve yükle
    // başlangıç pozuna ışınlanır, sürüş sürer; oturan oyuncunun kendi yeniden doğması devreye girmez.
    private static IEnumerator PhaseFall()
    {
        var respawn = player.GetComponent<PlayerFallRespawn>();
        int playerRespawns0 = respawn.Respawns;
        float vanS = 60f;
        Vector3 start = east.PointAt(vanS);
        Quaternion rot = Quaternion.LookRotation(-east.TangentAt(vanS), Vector3.up);
        Van.TeleportTo(start, rot, true);
        yield return Seconds(.3f);
        Van.SeatCrew();
        start = Van.transform.position;
        rot = Van.transform.rotation;
        Van.SetFallRespawn(start, rot);
        int cargo0 = Van.HeldCargoCount; // (r1)'de bir koni park alanına kondu: sayı düşmeden önceki sayıyla karşılaştırılır
        string fallMessage = "";
        Action<ServiceVanDriver> onFall = v => fallMessage = GameNotice.LastMessage;
        Van.FallRespawned += onFall;
        float savedBelow = Van.fallBelowY;
        Van.fallBelowY = start.y + 15f; // sanal plan: başlangıcın 15 m üstü (gerçek plana düşürmek için uçurum geometrisi gerekmez)
        Van.TeleportTo(start + Vector3.up * 40f, rot, false);
        yield return Seconds(.8f);
        float vy = Van.Body.linearVelocity.y;
        bool falling = Van.Falling && vy < -3f && Van.transform.position.y < start.y + 39f && Van.CrewInside;
        yield return Until(() => Van.FallRespawns >= 1, 6f, "minibüs plan altına düşüp ışınlanır");
        yield return Seconds(.5f);
        Van.FallRespawned -= onFall;
        bool back = Vector3.Distance(Van.transform.position, start) < 1f && Van.State == ServiceVanDriver.VanState.Driving && Van.CrewInside && !Van.Falling
            && player.transform.IsChildOf(Van.transform) && bot.transform.IsChildOf(Van.transform) && Van.CargoHeld && Van.HeldCargoCount == cargo0 && cargo0 >= 6
            && respawn.Respawns == playerRespawns0 && !respawn.enabled && fallMessage.Contains("uçurum");
        Van.fallBelowY = savedBelow;
        Van.SetTestInput(1f, 0f);
        yield return Seconds(1f);
        bool drives = Van.Speed > 1f;
        Van.SetTestInput(-1f, 0f);
        yield return Until(() => Mathf.Abs(Van.Speed) < .05f, 6f, "fren");
        Van.SetTestInput(0f, 0f);
        yield return Seconds(.4f);
        bool exited = Van.TryExit();
        Check("(f) zemin bitince minibüs yerçekimiyle düşer; plan altına inince ekip ve yükle başlangıç şeridine ışınlanır, sürüş sürer; oturan oyuncunun kendi yeniden doğması çalışmaz",
            falling && back && drives && exited,
            "düşüyor " + falling + " (0.8 sn'de düşey hız " + vy.ToString("F1", Inv) + " m/sn), geri döndü " + back + " (ışınlama " + Van.FallRespawns + ", " + V(Van.transform.position) + ", yük " + Van.HeldCargoCount + "/" + cargo0 + ", oyuncu yeniden doğma " + (respawn.Respawns - playerRespawns0) + ", bildirim '" + fallMessage + "'), sürdü " + drives + ", indi " + exited);
        Van.TeleportTo(zone.transform.position, zone.transform.rotation, true);
        yield return null;
    }

    // (b) Betikli sürüş: şeridi izle, alana yanaş, park et, in.
    private static IEnumerator PhaseDriveAndPark()
    {
        float zoneS = east.Project(zone.transform.position);
        Vector3 zoneFwd = -zone.transform.forward; zoneFwd.y = 0f; zoneFwd.Normalize();
        float s0 = east.Project(Van.transform.position);
        float driveStart = Time.time;
        int traffic0 = Traffic.Vehicles.Count;
        // Şeridi izle: 8 m ilerideki şerit noktasına pure-pursuit
        yield return DriveTo(() => east.PointAt(Mathf.Min(east.Length, east.Project(Van.transform.position) + 8f)), 11f, 0f,
            () => east.Project(Van.transform.position) >= zoneS - 10f, 60f, "şerit sürüşü");
        float s1 = east.Project(Van.transform.position);
        float maxSpeed = Van.MaxSpeedReached;
        float laneTime = Time.time - driveStart;
        // Alana yanaş: cep yalnız alan boyunca var (öncesinde yol kenarı korkuluk / uçurum); şeritte alanın 10 m gerisine kadar gidilir,
        // sonra alan ekseninde merkezin 3 m ilerisi hedeflenir (yön hizalanır), merkeze 3 m kala merkeze yavaş yanaşılır.
        float Progress() { return Vector3.Dot(Flat(Van.transform.position - zone.transform.position), zoneFwd); }
        // Üç hamle (sürücünün yapacağı gibi): ileri eksenin 8 m ötesine nişanla merkezi 1.5 m geç → ekseni 8 m geriye nişanla geri gel
        // (yön hizalanır, yanal hata azalır) → ileri merkeze 0.3 m kala dur. Fren mesafesi alan ekseni boyunca kalan yol.
        string Pose() { east.Project(Van.transform.position, out float latE, out _); return "ilerleme " + Progress().ToString("F2", Inv) + " m, EastBound yanal " + latE.ToString("F2", Inv) + " m, yaw sapması " + Vector3.Angle(Flat(Van.Forward), zoneFwd).ToString("F1", Inv) + "°"; }
        // Giriş iki aşama: önce eksen üzerindeki merkeze yakın noktaya (yanal mesafe kapanır, ~28° giriş), merkeze 3 m kala
        // eksenin 8 m ötesine nişan (yön hizalanır); merkezi 1.5 m geçince dur.
        yield return DriveTo(() => zone.transform.position - zoneFwd * 1f, 3.5f, 0f, () => Progress() >= -3f, 40f, "alana giriş (yanal)");
        Line("  otopilot: yanal giriş sonrası " + Pose());
        yield return DriveTo(() => zone.transform.position + zoneFwd * 8f, 3f, 0f, () => Progress() >= 1.5f, 40f, "alana giriş",
            () => Mathf.Max(0f, 1.5f - Progress()));
        Line("  otopilot: alana giriş sonrası " + Pose());
        for (int attempt = 0; attempt < 3; attempt++)
        {
            yield return DriveTo(() => zone.transform.position - zoneFwd * 8f, 2f, 0f, () => Progress() <= -1.5f, 30f, "geri düzeltme",
                () => Mathf.Max(0f, Progress() + 1.5f), true);
            Line("  otopilot: geri düzeltme #" + (attempt + 1) + " sonrası " + Pose());
            yield return DriveTo(() => zone.transform.position + zoneFwd * 8f, 2f, 0f, () => Progress() >= -.3f, 30f, "park",
                () => Mathf.Max(0f, -.3f - Progress()));
            Line("  otopilot: park #" + (attempt + 1) + " sonrası " + Pose());
            Van.SetTestInput(-1f, 0f);
            yield return Until(() => Mathf.Abs(Van.Speed) < .05f, 5f, "fren");
            Van.SetTestInput(0f, 0f);
            yield return Seconds(.3f);
            if (zone.Evaluate(Van.Footprint(), Van.LaneSamples())) break; // iyi park: hamleyi tekrarlamaya gerek yok
        }
        Van.SetTestInput(-1f, 0f);
        yield return Until(() => Mathf.Abs(Van.Speed) < .05f, 5f, "fren");
        Van.SetTestInput(0f, 0f);
        yield return Seconds(.5f);
        bool good = zone.Evaluate(Van.Footprint(), Van.LaneSamples());
        float yawErr = Vector3.Angle(Flat(Van.Forward), zoneFwd);
        float offCenter = Vector3.Distance(Flat(Van.transform.position), Flat(zone.transform.position));
        Check("(b1) betikli gaz / direksiyon minibüsü " + (s1 - s0).ToString("F0", Inv) + " m sürdü ve park alanına park etti (tamamen içeride, şerit dışında)",
            s1 - s0 > 50f && maxSpeed > 6f && good && Van.Damage < 5f,
            "şerit s " + s0.ToString("F1", Inv) + " → " + s1.ToString("F1", Inv) + " (" + laneTime.ToString("F1", Inv) + " sn, en yüksek hız " + maxSpeed.ToString("F1", Inv) + " m/sn), park: " + zone.LastReason
            + ", merkezden " + offCenter.ToString("F2", Inv) + " m, yaw sapması " + yawErr.ToString("F1", Inv) + "°, hasar %" + Van.Damage.ToString("F1", Inv) + " (darbe " + Van.Impacts + "), trafik " + traffic0 + "→" + Traffic.Vehicles.Count);

        // Korkuluk/kaya duvarı kapıları ve arka inişi kapatırsa iki ekip üyesi de içeride kalır.
        var exitBlocks = new List<GameObject>();
        float seatZ = Van.driverSeat.localPosition.z;
        float side = Van.bodyMax.x + .9f;
        void AddBlock(Vector3 local)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "DriveVerify_RockWall";
            block.transform.position = Van.transform.TransformPoint(local + Vector3.up * 1f);
            block.transform.localScale = new Vector3(.8f, 2f, .8f);
            exitBlocks.Add(block);
        }
        // Sol kapı şoföre açıkken botun üç ayrı adayı kapalı; dördüncü aday şoföre ayrılmıştır.
        AddBlock(new Vector3(-side, 0f, seatZ));
        AddBlock(new Vector3(-side, 0f, Van.bodyMax.z + .5f));
        AddBlock(new Vector3(.9f, 0f, Van.bodyMax.z + 2.2f));
        Physics.SyncTransforms();
        float heldTime = Mission.Elapsed;
        bool botRejected = !Van.TryExit();
        Check("(b1a) şoföre yer varken botun inişi engelliyse iki kişi de içeride kalır",
            botRejected && Van.CrewInside && !Van.HasExited && Mission.ClockHeld && GameNotice.LastMessage == ServiceVanDriver.NoSafeExitText,
            "bot reddi " + botRejected + ", içeride " + Van.CrewInside + ", uyarı '" + GameNotice.LastMessage + "'");
        AddBlock(new Vector3(side, 0f, seatZ));
        AddBlock(new Vector3(side, 0f, Van.bodyMax.z + .5f));
        AddBlock(new Vector3(0f, 0f, Van.bodyMax.z + 2.2f));
        Physics.SyncTransforms();
        bool rejected = !Van.TryExit();
        Check("(b1a) korkuluk/kaya duvarı yanında güvenli iniş yok: ekip içeride, görev saati başlamaz, uyarı görünür",
            rejected && Van.CrewInside && !Van.HasExited && Van.State == ServiceVanDriver.VanState.Driving && Mission.ClockHeld
                && Mathf.Approximately(Mission.Elapsed, heldTime) && GameNotice.LastMessage == ServiceVanDriver.NoSafeExitText,
            "red " + rejected + ", içeride " + Van.CrewInside + ", saat tutuldu " + Mission.ClockHeld + ", uyarı '" + GameNotice.LastMessage + "'");
        foreach (var block in exitBlocks) UnityEngine.Object.Destroy(block);
        yield return null;
        Physics.SyncTransforms();
        // Engel açılınca aynı park pozundan güvenli iniş gerçekleşir.
        // İniş
        var stretcherParents = Bolum1AmbulanceBuilder.StretcherNames.Select(n => GameObject.Find(n)).ToList();
        bool exited = Van.TryExit();
        yield return Seconds(1.5f);
        bool crewOut = exited && !Van.CrewInside && Van.HasExited && player.enabled && bot.enabled && player.GetComponent<CharacterController>().enabled
            && !player.transform.IsChildOf(Van.transform) && !bot.transform.IsChildOf(Van.transform)
            && Vector3.Distance(player.transform.position, Van.transform.position) < 6f && Vector3.Distance(bot.transform.position, Van.transform.position) < 6f
            && Vector3.Distance(player.transform.position, bot.transform.position) > .8f && CarrierRegistry.All.Contains(player) && CarrierRegistry.All.Contains(bot)
            && player.GetComponent<Animator>().GetInteger(ServiceVanDriver.ParamSeated) == 0;
        bool vanStatic = Van.State == ServiceVanDriver.VanState.Parked && Van.Body.isKinematic && Van.navObstacle.enabled && !Van.CargoHeld;
        bool clockRuns = !Mission.ClockHeld && ThiefDirector.Instance.enabled && Intro.Done && Intro.GoodParking;
        float elapsedA = Mission.Elapsed;
        yield return Seconds(1f);
        clockRuns &= Mission.Elapsed > elapsedA + .5f;
        // Ekipman: sedyeler ve koniler yük bölümünde, ebeveynleri eski yerinde, koniler yine dinamik; kangal dolapta
        var cargo = new Bounds(new Vector3(0f, (Bolum1ServiceVan.CargoFloor + 2.49f) * .5f, 1.0f), new Vector3(1.9f, 2.49f - Bolum1ServiceVan.CargoFloor + .2f, 3.4f));
        bool InCargo(Vector3 w) => cargo.Contains(Van.transform.InverseTransformPoint(w));
        var stretchers = Bolum1AmbulanceBuilder.StretcherNames.Select(n => GameObject.Find(n)).ToList();
        bool stretchersOk = stretchers.All(st => st != null && InCargo(st.transform.position) && !st.transform.IsChildOf(Van.transform) && st.GetComponent<Rigidbody>().isKinematic);
        var cones = Bolum1ServiceVan.ConeNames.Select(n => GameObject.Find(n)).ToList();
        bool conesOk = cones.All(c => c != null && InCargo(c.transform.position) && !c.transform.IsChildOf(Van.transform) && !c.GetComponent<Rigidbody>().isKinematic);
        var coil = UnityEngine.Object.FindAnyObjectByType<RopeCoil>();
        bool coilOk = coil != null && coil.InLocker && !coil.transform.IsChildOf(Van.transform) && Vector3.Distance(coil.GripMid, Van.transform.TransformPoint(Bolum1ServiceVan.LockerCenter)) < .12f;
        var locker = UnityEngine.Object.FindAnyObjectByType<TowTruckLocker>();
        bool lockerOk = locker != null && Vector3.Distance(locker.DoorCenter, locker.DoorBounds.center) < .15f;
        Check("(b2) E ile iniş: ekip dışarıda (bileşenler açık, taşıyıcı kaydı), minibüs statik (kinematik, NavMeshObstacle, yük serbest), görev saati ve hırsızlar başladı, iyi park bildirildi",
            crewOut && vanStatic && clockRuns,
            "ekip " + crewOut + " (oyuncu " + V(player.transform.position) + ", bot " + V(bot.transform.position) + "), minibüs " + vanStatic + ", saat " + clockRuns + " (Elapsed " + Mission.Elapsed.ToString("F2", Inv) + "), bildirim '" + GameNotice.LastMessage + "'");
        Check("(b3) ekipman minibüste kaldı: 2 sedye zeminde (kinematik, eski ebeveyn), 4 koni raflarda (dinamik), kangal dolapta, dolap merkezi güncel",
            stretchersOk && conesOk && coilOk && lockerOk,
            "sedyeler " + stretchersOk + " " + string.Join(" ", stretchers.Select(st => st != null ? V(Van.transform.InverseTransformPoint(st.transform.position)) : "YOK")) + ", koniler " + conesOk + ", kangal " + coilOk + ", dolap " + lockerOk);
        // NavMesh: minibüsün altı oyuk, yanındaki yol yürünür
        yield return Seconds(1.5f);
        Vector3 center = Van.transform.TransformPoint(new Vector3(0f, 0f, (Van.bodyMin.z + Van.bodyMax.z) * .5f));
        bool carved = !NavMesh.SamplePosition(center, out _, .3f, NavMesh.AllAreas);
        Vector3 beside = east.PointAt(east.Project(Van.transform.position));
        bool roadOn = NavMesh.SamplePosition(beside, out _, .6f, NavMesh.AllAreas);
        Check("(b4) NavMesh: park hâlindeki minibüsün altı oyulmuş (gerçek konum), yanındaki yol yürünür", carved && roadOn, "oyuk " + carved + ", yol " + roadOn + " (" + V(beside) + ")");
    }

    // Virajdaki şerit gövdenin uzun kenar ortasına yaklaşabilir; dört köşe tek başına yeterli değildir.
    private static IEnumerator PhaseParkingConsistency()
    {
        var originalLanes = zone.lanes;
        var bendGo = new GameObject("DriveVerify_BentLane");
        var bend = bendGo.AddComponent<LanePath>();
        bend.width = .5f;
        // Alanın izin verdiği küçük açıyla park et; sentetik viraj köşelerden uzak, kenar ortasına yakındır.
        float yaw = 3f;
        Van.TeleportTo(zone.transform.position, zone.transform.rotation * Quaternion.Euler(0f, yaw, 0f), true);
        if (!zone.FullyInside(Van.Footprint()))
        {
            yaw = 1f;
            Van.TeleportTo(zone.transform.position, zone.transform.rotation * Quaternion.Euler(0f, yaw, 0f), true);
        }
        float midZ = (Van.bodyMin.z + Van.bodyMax.z) * .5f;
        Vector3 At(float x, float z) => Van.transform.TransformPoint(new Vector3(x, Van.bodyMin.y + .5f, z));
        bend.points = new[] { At(Van.bodyMax.x + 3f, Van.bodyMin.z - 1f), At(Van.bodyMax.x + .05f, midZ), At(Van.bodyMax.x + 3f, Van.bodyMax.z + 1f) };
        bend.Rebuild();
        zone.lanes = originalLanes.Concat(new[] { bend }).ToArray();
        bool cornersClear = zone.FullyInside(Van.Footprint()) && zone.OutOfLanes(Van.Footprint(), out _);
        bool middleBlocks = Van.BlocksLane(bend, out _, out _, out _);
        Van.SeatCrew();
        bool angledExited = Van.TryExit();
        Check("(b5) açılı parkta dört köşe temizken kenar ortası şeritte: kötü park ve trafik engeli",
            yaw > 0f && cornersClear && middleBlocks && angledExited && !Van.GoodParking,
            "açı " + yaw.ToString("F1", Inv) + "°, köşeler temiz " + cornersClear + ", şerit kapalı " + middleBlocks + ", iniş " + angledExited + ", iyi park " + Van.GoodParking);
        zone.lanes = originalLanes;
        UnityEngine.Object.Destroy(bendGo);
        Van.TeleportTo(zone.transform.position, zone.transform.rotation, true);
        Van.SeatCrew();
        bool insideExited = Van.TryExit();
        bool anyBlocked = originalLanes.Any(l => Van.BlocksLane(l, out _, out _, out _));
        Check("(b6) tam içeride park: iyi park ve kayıtlı iki trafik şeridi açık",
            insideExited && Van.GoodParking && !anyBlocked,
            "iniş " + insideExited + ", iyi park " + Van.GoodParking + ", şerit kapalı " + anyBlocked);
        yield return null;
    }

    // (c) Kötü park: minibüs EastBound'da şeridin ortasında; araç durur, korna, ~5 sn, çarpıp iter; hasar sonuca ulaşır.
    private static IEnumerator PhaseBadParkRam()
    {
        float vanS = 70f;
        Van.ResetDamageForTest();
        Van.TeleportTo(east.PointAt(vanS), Quaternion.LookRotation(-east.TangentAt(vanS), Vector3.up), true);
        yield return Seconds(.5f);
        bool blocks = Van.BlocksLane(east, out float edge, out float far, out _);
        // (c0) ekip yük bölümünde ayakta: itilirken minibüsle taşınır, durunca yere düşer (havada kalmaz, duvardan geçmez)
        Vector3 cargoP = Van.transform.TransformPoint(new Vector3(.3f, Bolum1ServiceVan.CargoFloor + .02f, .4f));
        Vector3 cargoB = Van.transform.TransformPoint(new Vector3(-.3f, Bolum1ServiceVan.CargoFloor + .02f, 1.6f));
        player.Teleport(cargoP, Quaternion.LookRotation(Flat(Van.Forward), Vector3.up));
        bot.transform.SetPositionAndRotation(cargoB, Quaternion.LookRotation(Flat(Van.Forward), Vector3.up));
        yield return Seconds(.5f);
        bool crewInCargo = !player.IsDown && !bot.IsDown && Van.InsideFootprint(player.transform.position, 0f) && Van.InsideFootprint(bot.transform.position, 0f) && Van.CrewInCargo;
        var v = Traffic.ForceSpawn(true, TrafficVehicleType.Sedan);
        if (v == null) throw new Exception("EastBound'a Sedan çıkarılamadı");
        yield return Until(() => v == null || (v.BlockerKind == LaneOccupancy.BlockerKind.Obstacle && v.Follower.Speed <= .01f), 45f, "araç minibüsün gerisinde durur");
        float gap = v.GapToLeader;
        float stopDist = v.Follower.FrontS;
        // (c0) yük bölümünde ekip varken: araç bekleme süresi geçse de çarpmaz, korna çalıp bekler; ekip çıkınca döngü sürer
        float holdFor = Bolum1TrafficBuilder.Settings.vanWaitSeconds + 3f;
        yield return Seconds(holdFor);
        bool heldOff = v != null && v.ObstacleRams == 0 && Van.Rams == 0 && v.Follower.Speed <= .01f && v.ObstacleHorns >= 2 && v.ObstacleWaitTime < 1f && Van.State == ServiceVanDriver.VanState.Parked;
        Check("(c0) yük bölümünde ekip (oyuncu + bot) varken şeridi kapatan minibüse araç " + holdFor.ToString("F0", Inv) + " sn geçse de çarpmaz: korna çalıp bekler (sayaç donuk)",
            crewInCargo && heldOff,
            "ekip yükte " + crewInCargo + ", çarpmadı " + heldOff + " (çarpma kararı " + (v != null ? v.ObstacleRams.ToString() : "-") + ", korna " + (v != null ? v.ObstacleHorns.ToString() : "-") + ", sayaç " + (v != null ? v.ObstacleWaitTime.ToString("F1", Inv) : "-") + " sn, minibüs çarpma " + Van.Rams + ")");
        // Ekip yükten çıkar (park alanına, itilme yolunun dışına): bekle-korna-çarp döngüsü sürer
        Vector3 awayP = zone.transform.position + Vector3.up * .02f;
        if (GroundProbe.Find(awayP + Vector3.up * 1f, 6f, out RaycastHit zg)) awayP = zg.point + Vector3.up * .02f;
        player.Teleport(awayP, Quaternion.LookRotation(Flat(Van.Forward), Vector3.up));
        bot.transform.position = awayP + Flat(Van.Forward) * 1.5f;
        yield return Seconds(.2f);
        float stopT = Time.time;
        yield return Until(() => v == null || v.ObstacleRams > 0, 10f, "araç çarpmaya karar verir");
        float waited = Time.time - stopT;
        int horns = v.ObstacleHorns;
        Vector3 before = Van.transform.position;
        yield return Until(() => Van.Rams > 0, 15f, "araç minibüse değer");
        yield return Until(() => Vector3.Distance(Flat(before), Flat(Van.transform.position)) > .5f, 15f, "minibüs itilir");
        float pushedAt = Time.time;
        Line("  itilme: " + Van.LastShoveReason);
        float passEnd = Time.time + 45f;
        string lastState = "";
        while (Time.time < passEnd && !(v == null || v.Follower.Distance > far + 8f)) { lastState = RamState(v); yield return null; }
        if (!(v == null || v.Follower.Distance > far + 8f)) throw new Exception("beklenen olmadı: araç minibüsü geçip devam eder — " + lastState);
        yield return Until(() => Van.State == ServiceVanDriver.VanState.Parked, 15f, "minibüs yeniden durur");
        float pushed = Vector3.Distance(Flat(before), Flat(Van.transform.position));
        bool upright = Vector3.Angle(Van.transform.up, Vector3.up) < 12f;
        bool clearedLane = !Van.BlocksLane(east, out _, out _, out _);
        // Statik engelle (korkuluk, kaya) kesişme yok; yük bölümündeki serbest ekipman (koni, sedye, kangal) gövde kutusunun içindedir, sayılmaz
        var foreign = Van.OverlapReport().Where(n => !n.StartsWith("ServiceVan_Cone") && !n.StartsWith("Stretcher") && !n.Contains("RopeCoil")).ToList();
        bool grounded = foreign.Count == 0;
        Check("(c1) şeritte bırakılan minibüs: araç " + (Bolum1TrafficBuilder.Settings.vanStopMargin).ToString(Inv) + " m geride durur, korna çalar, ~5 sn bekler, sonra çarpıp iter (sınırlı: 0.5–" + Van.maxShoveDistance.ToString(Inv) + " m, dik, şerit boşalır) ve devam eder",
            blocks && Mathf.Abs(gap - Bolum1TrafficBuilder.Settings.vanStopMargin) < .8f && horns >= 2 && waited > 4.5f && waited < 6.5f && Van.Rams == 1 && pushed > .5f && pushed <= Van.maxShoveDistance + .5f && upright && clearedLane && grounded && (v == null || v.Follower.Distance > far + 8f),
            "kapatıyor " + blocks + " (s " + edge.ToString("F1", Inv) + "–" + far.ToString("F1", Inv) + "), boşluk " + gap.ToString("F2", Inv) + " m (ön tampon s " + stopDist.ToString("F1", Inv) + "), korna " + horns + ", bekleme " + waited.ToString("F2", Inv)
            + " sn, çarpma " + Van.Rams + " (hasar %" + Van.Damage.ToString("F0", Inv) + "), itilme " + pushed.ToString("F2", Inv) + " m (" + (pushedAt - stopT).ToString("F1", Inv) + " sn'de; yön: " + Van.LastShoveReason + "), dik " + upright + ", şerit boşaldı " + clearedLane + ", statik kesişme yok " + grounded + (grounded ? "" : " [" + string.Join(",", foreign) + "]")
            + ", araç " + (v == null ? "kayboldu" : "s " + v.Follower.Distance.ToString("F1", Inv)) + ", durum " + Van.State);
        // Hasar sonuca ulaşır: saha izleyicisi (oturum) → FieldPayout
        var session = new MissionBridge.Session { jobId = "test", jobTitle = "Sürüş testi", fieldScene = "Bolum1_Ucurum", officeScene = "-", jobPayment = 1000, distanceKm = 10f };
        foreach (var id in new[] { "rope", "stretcher", "stretcher", "cone", "cone", "cone", "cone" })
            session.gear.Add(new MissionBridge.Gear { uid = id + session.gear.Count, definitionId = id, label = id, durability = 100f });
        var trackerGo = new GameObject("DriveVerify_Tracker");
        var tracker = trackerGo.AddComponent<FieldMissionTracker>();
        tracker.Setup(session);
        var inputs = tracker.Collect(Mission);
        var breakdown = FieldPayout.Compute(inputs);
        var plain = FieldPayout.Compute(new FieldPayout.Inputs { succeeded = true, jobPayment = 1000, distanceKm = 10f, fieldSeconds = inputs.fieldSeconds });
        UnityEngine.Object.Destroy(trackerGo);
        Check("(c2) hasar sonuca ulaşır: FieldMissionTracker.Collect.vanDamage = minibüs hasarı > 0, FieldPayout.vehicleWear o kadar artar, rapor satırında görünür",
            Van.Damage > 0f && Mathf.Approximately(inputs.vanDamage, Van.Damage) && breakdown.vehicleWear > plain.vehicleWear && Mathf.Abs(breakdown.vehicleWear - plain.vehicleWear - Mathf.Round(Van.Damage * 10f) / 10f) < .15f
            && FieldPayout.Lines(breakdown, 1000).Any(l => l.Contains("sürüş hasarı")),
            "hasar %" + Van.Damage.ToString("F1", Inv) + ", girdi %" + inputs.vanDamage.ToString("F1", Inv) + ", araç aşınması %" + breakdown.vehicleWear.ToString("F1", Inv) + " (hasarsız %" + plain.vehicleWear.ToString("F1", Inv) + "), satır: " + FieldPayout.Lines(breakdown, 1000)[3]);
        Traffic.ClearVehicles();
    }

    // (d) Karşı şeride taşan minibüs: WestBound aracı önünde bekler (durur, korna), süre dolunca o da çarpar.
    private static IEnumerator PhaseOncomingLane()
    {
        float vanS = 95f; // WestBound s (doğu ucundan)
        Van.ResetDamageForTest();
        // Minibüs merkezi WestBound ortasından 0.9 m EastBound tarafına kaymış: iki şeride de taşar (doğu yönüne giden minibüs)
        float toEast = Mathf.Sign(Vector3.Dot(east.PointAt(east.Project(west.PointAt(vanS))) - west.PointAt(vanS), west.RightAt(vanS)));
        Vector3 pos = west.PointAt(vanS) + west.RightAt(vanS) * (.9f * toEast);
        Van.TeleportTo(pos, Quaternion.LookRotation(west.TangentAt(vanS), Vector3.up), true); // kök +Z = batı teğeti → ileri = doğu
        yield return Seconds(.5f);
        bool blocksWest = Van.BlocksLane(west, out float edge, out float far, out _);
        bool blocksEast = Van.BlocksLane(east, out _, out _, out _);
        var v = Traffic.ForceSpawn(false, TrafficVehicleType.Hatchback);
        if (v == null) throw new Exception("WestBound'a Hatchback çıkarılamadı");
        yield return Until(() => v == null || (v.BlockerKind == LaneOccupancy.BlockerKind.Obstacle && v.Follower.Speed <= .01f), 45f, "karşı araç minibüsün önünde durur");
        float stopT = Time.time;
        float gap = v.GapToLeader;
        float frontS = v.Follower.FrontS;
        bool headOn = Vector3.Dot(Flat(v.transform.forward), Flat(Van.Forward)) < -.8f;
        yield return Seconds(2.5f);
        bool stillWaiting = v != null && v.Follower.Speed <= .01f && v.ObstacleRams == 0 && v.ObstacleHorns >= 1;
        Vector3 before = Van.transform.position;
        yield return Until(() => v == null || v.ObstacleRams > 0, 10f, "karşı araç çarpmaya karar verir");
        float waited = Time.time - stopT;
        yield return Until(() => Vector3.Distance(Flat(before), Flat(Van.transform.position)) > .3f, 20f, "minibüs karşı araçça itilir");
        yield return Until(() => Van.State == ServiceVanDriver.VanState.Parked, 20f, "minibüs yeniden durur");
        float pushed = Vector3.Distance(Flat(before), Flat(Van.transform.position));
        Check("(d) karşı şeride taşan minibüs: WestBound aracı önünde durur (karşıdan), korna çalar, 5 sn bekler, sonra çarpıp iter; EastBound şeridi de kapalı sayılır",
            blocksWest && blocksEast && headOn && frontS < edge && Mathf.Abs(gap - Bolum1TrafficBuilder.Settings.vanStopMargin) < .8f && stillWaiting && waited > 4.5f && waited < 6.5f && pushed > .3f && Van.Damage > 0f,
            "batı şeridi " + blocksWest + " (s " + edge.ToString("F1", Inv) + "–" + far.ToString("F1", Inv) + "), doğu şeridi " + blocksEast + ", karşıdan " + headOn + ", ön tampon s " + frontS.ToString("F1", Inv) + ", boşluk " + gap.ToString("F2", Inv)
            + " m, 2.5 sn sonra hâlâ bekliyor " + stillWaiting + ", bekleme " + waited.ToString("F2", Inv) + " sn, itilme " + pushed.ToString("F2", Inv) + " m, hasar %" + Van.Damage.ToString("F1", Inv));
        Traffic.ClearVehicles();
    }

    // Çarpma / itilme durumu (zaman aşımı teşhisi)
    private static string RamState(TrafficVehicle v)
    {
        float s = east.Project(Van.transform.position, out float lat, out _);
        bool blocks = Van.BlocksLane(east, out float e, out float f, out _);
        return "minibüs s " + s.ToString("F1", Inv) + " yanal " + lat.ToString("F2", Inv) + " m, durum " + Van.State + ", itilme " + Van.ShoveDistance.ToString("F2", Inv) + " m, kapatıyor " + blocks
            + (blocks ? " (" + e.ToString("F1", Inv) + "–" + f.ToString("F1", Inv) + ")" : "") + ", kesişen [" + string.Join(",", Van.OverlapReport()) + "], yön: " + Van.LastShoveReason
            + " | araç " + (v == null ? "yok" : "s " + v.Follower.Distance.ToString("F1", Inv) + " hız " + v.Follower.Speed.ToString("F1", Inv) + " engel " + v.BlockerKind + " çarpıyor " + (v.RammingObstacle != null) + " bekliyor " + (v.WaitingObstacle != null)
            + " bekleme " + v.ObstacleWaitTime.ToString("F1", Inv) + " çarpma kararı " + v.ObstacleRams + " durak " + (v.Follower.HasStop ? v.Follower.StopDistance.ToString("F1", Inv) : "-"));
    }

    // (e) Ambulans: şeridi kapatan minibüsün gerisinde durur, korna çalar, ÇARPMAZ (kurtarma aracı); görev saati işler (kötü park
    // zaman kaybettirir); minibüs çekilince devam edip durağa varır.
    private static IEnumerator PhaseAmbulanceBlocked()
    {
        float vanS = Mathf.Clamp(Mission.StopDistance - 25f, 20f, 80f);
        Van.ResetDamageForTest();
        Van.TeleportTo(east.PointAt(vanS), Quaternion.LookRotation(-east.TangentAt(vanS), Vector3.up), true);
        yield return Seconds(.5f);
        bool blocks = Van.BlocksLane(east, out float edge, out _, out _);
        Vector3 vanPos = Van.transform.position;
        var schedule = UnityEngine.Object.Instantiate(Mission.schedule);
        schedule.firstArrival = 0f;
        schedule.interval = 99999f;
        Mission.Begin(schedule);
        yield return Until(() => Mission.Ambulances.Count > 0, 5f, "ambulans doğar");
        var amb = Mission.Ambulances[0];
        yield return Until(() => amb == null || (amb.BlockingObstacle == (ILaneObstacle)Van && amb.Follower.Speed <= .01f), 60f, "ambulans minibüsün gerisinde durur");
        float gap = amb != null ? amb.GapToLeader : -1f;
        float frontS = amb != null ? amb.Follower.FrontS : -1f;
        float elapsed0 = Mission.Elapsed;
        float waitFor = Bolum1TrafficBuilder.Settings.vanWaitSeconds + 3f;
        yield return Seconds(waitFor);
        bool stillWaiting = amb != null && amb.Follower.Speed <= .01f && amb.BlockingObstacle == (ILaneObstacle)Van && amb.Phase == AmbulancePhase.Approaching;
        int horns = amb != null ? amb.ObstacleHorns : 0;
        float waited = amb != null ? amb.ObstacleWaitTime : 0f;
        bool noRam = Van.Rams == 0 && Van.Damage == 0f && Van.State == ServiceVanDriver.VanState.Parked && Vector3.Distance(vanPos, Van.transform.position) < .05f;
        bool clockRan = !Mission.ClockHeld && Mission.Elapsed - elapsed0 > waitFor - 1f;
        // Minibüs çekilir (park alanına): ambulans devam eder, durağa varır
        Van.TeleportTo(zone.transform.position, zone.transform.rotation, true);
        float moveT = Time.time;
        yield return Until(() => amb == null || amb.Phase == AmbulancePhase.Waiting, 60f, "ambulans durağa varır");
        bool arrived = amb != null && amb.Phase == AmbulancePhase.Waiting && amb.BlockingObstacle == null;
        Check("(e) ambulans şeridi kapatan minibüsün " + Bolum1TrafficBuilder.Settings.vanStopMargin.ToString(Inv) + " m gerisinde durur, korna çalar, 5 sn geçse de ÇARPMAZ (hasar / itilme yok), görev saati işler; minibüs çekilince durağa varır",
            blocks && Mathf.Abs(gap - Bolum1TrafficBuilder.Settings.vanStopMargin) < .8f && stillWaiting && horns >= 2 && noRam && clockRan && arrived,
            "kapatıyor " + blocks + " (kenar s " + edge.ToString("F1", Inv) + ", ambulans ön tampon s " + frontS.ToString("F1", Inv) + "), boşluk " + gap.ToString("F2", Inv) + " m, " + waitFor.ToString("F0", Inv) + " sn sonra hâlâ bekliyor " + stillWaiting
            + " (bekleme " + waited.ToString("F1", Inv) + " sn), korna " + horns + ", çarpma yok " + noRam + " (çarpma " + Van.Rams + ", hasar %" + Van.Damage.ToString("F0", Inv) + "), saat işledi " + clockRan + " (+" + (Mission.Elapsed - elapsed0).ToString("F1", Inv)
            + " sn), varış " + arrived + " (çekildikten " + (Time.time - moveT).ToString("F1", Inv) + " sn sonra)");
        var quiet = UnityEngine.Object.Instantiate(Mission.schedule);
        quiet.firstArrival = 99999f;
        Mission.Begin(quiet); // ambulans kaldırılır
    }

    // Koltuk / direksiyon / FP kamera görsel denetimi: TP ve FP (sürerken), dışarıdan ön camdan, yandan kesit (gövde gizli).
    private static IEnumerator CaptureSeats()
    {
        var orbit = Van.orbitCamera;
        yield return CaptureMain("drive_seat_tp");
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        yield return null; yield return null;
        yield return CaptureMain("drive_seat_fp");
        // FP'de ön panel yolu kapatmasın: ekranın sağ yarısında alttan yukarı ilk renk kırılması (panel üst kenarı) ≤ %40 yükseklikte
        float dash = MeasureDashCoverage();
        Check("(a1) birinci şahısta sürerken ön panel ekranın en fazla %40'ını kapatır (eğim " + orbit.Pitch.ToString("F0", Inv) + "°)", dash <= .4f && Mathf.Abs(orbit.Pitch - Van.driveFirstPersonPitch) < .5f,
            "panel üst kenarı ekran yüksekliğinin %" + (dash * 100f).ToString("F0", Inv) + "'inde, eğim " + orbit.Pitch.ToString("F1", Inv) + "°, göz yukarı " + orbit.eyeUp.ToString("F2", Inv) + " m");
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return null;
        Vector3 look = Van.driverSeat.position + Vector3.up * .75f;
        Vector3 eye = Van.transform.TransformPoint(new Vector3(.35f, 1.75f, Van.bodyMin.z - 2.4f));
        yield return CaptureFrom("drive_seat_windshield", eye, look, 45f);
        // Yandan kesit: gövde görseli geçici gizli (koltuk / direksiyon / karakter pozu ölçülür)
        var visual = Van.transform.Find(Bolum1ServiceVan.VisualName);
        var renderers = visual != null ? visual.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList() : new List<Renderer>();
        foreach (var r in renderers) r.enabled = false;
        eye = Van.transform.TransformPoint(new Vector3(Van.bodyMax.x + 3.2f, 1.6f, Van.driverSeat.localPosition.z));
        yield return CaptureFrom("drive_seat_side_cutaway", eye, Van.driverSeat.position + Vector3.up * .6f, 40f);
        foreach (var r in renderers) r.enabled = true;
        var head = player.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        Line("  koltuk ölçüleri: şoför kökü yerel " + V(Van.transform.InverseTransformPoint(player.transform.position)) + ", baş yerel " + (head != null ? V(Van.transform.InverseTransformPoint(head.position)) : "-")
            + ", göz yerel " + V(Van.transform.InverseTransformPoint(orbit.EyePoint())) + ", direksiyon yerel " + V(Van.transform.InverseTransformPoint(Van.steeringWheel.position)) + ", kabin tavanı yerel y " + (1.275f + 1.95f * .5f).ToString("F2", Inv));
    }

    private static IEnumerator CaptureMain(string name)
    {
        yield return null;
        var cam = Camera.main;
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        SaveRenderTexture(rt, name);
        Line("  görüntü: " + OutDir + "/" + name + ".png (ana kamera " + V(cam.transform.position) + ", mod " + Van.orbitCamera.Mode + ")");
    }

    private static IEnumerator CaptureFrom(string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("DriveVerify_CaptureCam");
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = main != null ? main.backgroundColor : Color.gray;
        cam.farClipPlane = 2000f;
        cam.nearClipPlane = .05f;
        cam.fieldOfView = fov;
        go.transform.position = pos;
        go.transform.LookAt(look);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        yield return null; yield return null; // GPU Resident Drawer yeni kameranın görünürlük verisini sonraki döngüde hazırlar
        cam.Render();
        cam.targetTexture = null;
        SaveRenderTexture(rt, name);
        UnityEngine.Object.Destroy(go);
        Line("  görüntü: " + OutDir + "/" + name + ".png (kamera " + V(pos) + ")");
    }

    // Ana kameradan küçük bir kare: x = %80 ve %90 sütunlarında alttan yukarı ilk belirgin renk kırılmasının (ön panel üst kenarı)
    // ekran yüksekliğine oranı (en kötü sütun). Panel düz renkli; yol / gökyüzü bariz farklı.
    private static float MeasureDashCoverage()
    {
        var cam = Camera.main;
        const int w = 640, h = 360;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        float worst = 0f;
        foreach (float x in new[] { .8f, .9f })
        {
            int col = (int)(x * w);
            Color baseC = tex.GetPixel(col, 2);
            int edge = h, run = 0;
            for (int y = 3; y < h; y++)
            {
                Color c = tex.GetPixel(col, y);
                float d = Mathf.Abs(c.r - baseC.r) + Mathf.Abs(c.g - baseC.g) + Mathf.Abs(c.b - baseC.b);
                if (d > .45f) { run++; if (run >= 4) { edge = y - 3; break; } }
                else run = 0;
            }
            worst = Mathf.Max(worst, edge / (float)h);
        }
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        return worst;
    }

    private static void SaveRenderTexture(RenderTexture rt, string name)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }

    // Pure-pursuit otopilot: hedefe dön, hız sınırı ve durma mesafesine göre gaz / fren. until verilirse o sağlanınca biter.
    // remaining: verilirse fren mesafesi bu uzaklığa göre (hedef noktaya değil); until ile birlikte kullanılır.
    // reverse: geri giderken hedefe arka uç nişanlanır (direksiyon işareti gerçek araçtaki gibi: sağa çevirince arka sağa savrulur).
    private static IEnumerator DriveTo(Func<Vector3> target, float speedLimit, float stopDistance, Func<bool> until, float timeout, string what, Func<float> remaining = null, bool reverse = false)
    {
        float end = Time.time + timeout;
        float stuckSince = -1f;
        int recoveries = 0;
        float dirSign = reverse ? -1f : 1f;
        while (Time.time < end)
        {
            if (until != null && until()) break;
            Vector3 to = Flat(target() - Van.transform.position);
            float dist = to.magnitude;
            if (until == null && dist <= stopDistance) break;
            float angle = Vector3.SignedAngle(Flat(Van.Forward) * dirSign, to.normalized, Vector3.up);
            // Geri giderken işaret ters: arka ucun sağa (hedefe) savrulması için direksiyon sola (ServiceVanDriver: yawRate ∝ steer·sign(hız))
            float steer = Mathf.Clamp(angle / 20f, -1f, 1f) * dirSign;
            float allowed = remaining != null ? Mathf.Min(speedLimit, Mathf.Sqrt(2f * 2.5f * remaining()) + .3f)
                : until != null ? speedLimit : Mathf.Min(speedLimit, Mathf.Sqrt(2f * 2.5f * Mathf.Max(0f, dist - stopDistance)) + .4f);
            if (Mathf.Abs(angle) > 35f) allowed = Mathf.Min(allowed, 3f);
            float speedAlong = Van.Speed * dirSign; // gidiş yönündeki hız (geri giderken pozitif)
            float throttle = (speedAlong > allowed + .3f ? -1f : speedAlong < allowed - .3f ? 1f : 0f) * dirSign;
            // Sıkışma (gaz verildiği hâlde 2 sn hız yok): ters yöne 1.5 sn, sonra yeniden dene
            if (throttle * dirSign > 0f && Mathf.Abs(Van.Speed) < .3f && dist > stopDistance + 1f) { if (stuckSince < 0f) stuckSince = Time.time; }
            else stuckSince = -1f;
            if (stuckSince >= 0f && Time.time - stuckSince > 2f)
            {
                recoveries++;
                Line("  otopilot: sıkıştı (" + what + ", " + V(Van.transform.position) + ", kesişen " + string.Join(",", Van.OverlapReport()) + "), geri alınıyor #" + recoveries);
                float back = Time.time + 1.5f;
                while (Time.time < back) { Van.SetTestInput(-dirSign, -steer); yield return null; }
                stuckSince = -1f;
                continue;
            }
            Van.SetTestInput(throttle, steer);
            yield return null;
        }
        Van.SetTestInput(0f, 0f);
        if (Time.time >= end) throw new Exception("zaman aşımı: " + what + " (minibüs " + V(Van.transform.position) + ", hız " + Van.Speed.ToString("F1", Inv) + ", kesişen " + string.Join(",", Van.OverlapReport()) + ")");
    }

    private static void CheckCompileErrors()
    {
        string log = Application.consoleLogPath;
        bool clean = true;
        string note = "log: " + log;
        try
        {
            using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                clean = !sr.ReadToEnd().Contains("error " + "CS");
        }
        catch (Exception e) { clean = false; note += " okunamadı: " + e.Message; }
        Check("log'da C# derleme hatası yok", clean, note);
    }

    private static void Check(string label, bool ok, string detail)
    {
        allOk &= ok;
        Line((ok ? "[OK]   " : "[FAIL] ") + label + " — " + detail);
        Debug.Log((ok ? "CHECK OK " : "CHECK FAIL ") + label + " — " + detail);
    }

    private static void Fail(string message)
    {
        allOk = false;
        Line("[FAIL] " + message);
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
