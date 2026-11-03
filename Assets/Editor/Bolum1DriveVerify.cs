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
    private static readonly string[] OtherStageKeys =
    {
        "Bolum1AmbulanceVerify.Stage", "Bolum1AnimVerify.Stage", "Bolum1TrafficVerify.Stage", "Bolum1Verify.Stage",
        "Bolum1FirstPersonVerify.Stage", "MissionBridgeVerify.Stage", "OfficeGarageVerify.Stage",
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static Bolum1DriveVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    // Öbür doğrulamalar Play'deyken sürüş girişi atlanır (eski başlangıç); yalnız bu doğrulama ve normal oyun girişi yaşar.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void SkipIntroInOtherTests()
    {
        bool mine = SessionState.GetString(StageKey, "") == "play";
        bool other = OtherStageKeys.Any(k => !string.IsNullOrEmpty(SessionState.GetString(k, "")));
        DriveIntro.SkipForTests = !mine && other;
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
        yield return PhaseBadParkRam();
        yield return PhaseOncomingLane();
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
        Van.SetTestInput(1f, 0f);
        yield return Seconds(1.5f);
        bool movingNoExit = Mathf.Abs(Van.Speed) > 2f && !Van.TryExit() && GameNotice.LastMessage == ServiceVanDriver.NoExitWhileMovingText;
        Van.SetTestInput(0f, 0f);
        Check("(a2) hareket hâlindeyken E ile iniş reddedilir (uyarı)", movingNoExit && Van.CrewInside,
            "1.5 sn gazdan sonra hız " + Van.Speed.ToString("F1", Inv) + " m/sn → red " + movingNoExit + ", uyarı '" + GameNotice.LastMessage + "'");
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
            if (zone.Evaluate(Van.Footprint())) break; // iyi park: hamleyi tekrarlamaya gerek yok
        }
        Van.SetTestInput(-1f, 0f);
        yield return Until(() => Mathf.Abs(Van.Speed) < .05f, 5f, "fren");
        Van.SetTestInput(0f, 0f);
        yield return Seconds(.5f);
        bool good = zone.Evaluate(Van.Footprint());
        float yawErr = Vector3.Angle(Flat(Van.Forward), zoneFwd);
        float offCenter = Vector3.Distance(Flat(Van.transform.position), Flat(zone.transform.position));
        Check("(b1) betikli gaz / direksiyon minibüsü " + (s1 - s0).ToString("F0", Inv) + " m sürdü ve park alanına park etti (tamamen içeride, şerit dışında)",
            s1 - s0 > 50f && maxSpeed > 6f && good && Van.Damage < 5f,
            "şerit s " + s0.ToString("F1", Inv) + " → " + s1.ToString("F1", Inv) + " (" + laneTime.ToString("F1", Inv) + " sn, en yüksek hız " + maxSpeed.ToString("F1", Inv) + " m/sn), park: " + zone.LastReason
            + ", merkezden " + offCenter.ToString("F2", Inv) + " m, yaw sapması " + yawErr.ToString("F1", Inv) + "°, hasar %" + Van.Damage.ToString("F1", Inv) + " (darbe " + Van.Impacts + "), trafik " + traffic0 + "→" + Traffic.Vehicles.Count);

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

    // (c) Kötü park: minibüs EastBound'da şeridin ortasında; araç durur, korna, ~5 sn, çarpıp iter; hasar sonuca ulaşır.
    private static IEnumerator PhaseBadParkRam()
    {
        float vanS = 70f;
        Van.ResetDamageForTest();
        Van.TeleportTo(east.PointAt(vanS), Quaternion.LookRotation(-east.TangentAt(vanS), Vector3.up), true);
        yield return Seconds(.5f);
        bool blocks = Van.BlocksLane(east, out float edge, out float far, out _);
        var v = Traffic.ForceSpawn(true, TrafficVehicleType.Sedan);
        if (v == null) throw new Exception("EastBound'a Sedan çıkarılamadı");
        yield return Until(() => v == null || (v.BlockerKind == LaneOccupancy.BlockerKind.Obstacle && v.Follower.Speed <= .01f), 45f, "araç minibüsün gerisinde durur");
        float stopT = Time.time;
        float gap = v.GapToLeader;
        float stopDist = v.Follower.FrontS;
        yield return Until(() => v == null || v.ObstacleRams > 0, 10f, "araç çarpmaya karar verir");
        float waited = Time.time - stopT;
        int horns = v.ObstacleHorns;
        Vector3 before = Van.transform.position;
        yield return Until(() => Van.Rams > 0, 15f, "araç minibüse değer");
        yield return Until(() => Vector3.Distance(Flat(before), Flat(Van.transform.position)) > .5f, 15f, "minibüs itilir");
        float pushedAt = Time.time;
        yield return Until(() => v == null || v.Follower.Distance > far + 8f, 45f, "araç minibüsü geçip devam eder");
        yield return Until(() => Van.State == ServiceVanDriver.VanState.Parked, 15f, "minibüs yeniden durur");
        float pushed = Vector3.Distance(Flat(before), Flat(Van.transform.position));
        bool upright = Vector3.Angle(Van.transform.up, Vector3.up) < 12f;
        Check("(c1) şeritte bırakılan minibüs: araç " + (Bolum1TrafficBuilder.Settings.vanStopMargin).ToString(Inv) + " m geride durur, korna çalar, ~5 sn bekler, sonra çarpıp iter ve devam eder",
            blocks && Mathf.Abs(gap - Bolum1TrafficBuilder.Settings.vanStopMargin) < .8f && horns >= 2 && waited > 4.5f && waited < 6.5f && Van.Rams >= 1 && pushed > .5f && pushed < 30f && upright && (v == null || v.Follower.Distance > far + 8f),
            "kapatıyor " + blocks + " (s " + edge.ToString("F1", Inv) + "–" + far.ToString("F1", Inv) + "), boşluk " + gap.ToString("F2", Inv) + " m (ön tampon s " + stopDist.ToString("F1", Inv) + "), korna " + horns + ", bekleme " + waited.ToString("F2", Inv)
            + " sn, çarpma " + Van.Rams + ", itilme " + pushed.ToString("F2", Inv) + " m (" + (pushedAt - stopT).ToString("F1", Inv) + " sn'de), dik " + upright + ", araç " + (v == null ? "kayboldu" : "s " + v.Follower.Distance.ToString("F1", Inv)) + ", durum " + Van.State);

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
