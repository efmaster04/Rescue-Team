using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Bölüm 1 ambulans döngüsü kabul testi. Batchmode (ekran görüntüsü için -nographics OLMADAN):
// Unity.exe -batchmode -projectPath ... -executeMethod Bolum1AmbulanceVerify.Run -logFile <dosya>
// Başarıda "AMBULANCE VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/Bolum1/ambulance_verify_report.txt
//
// İki aşama: (1) edit modunda statik kontroller ve ses klipleri, (2) Play modunda senaryo (Animator ve fizik yalnız Play'de).
// Play'e girerken domain yeniden yüklendiği için ara durum SessionState'te taşınır (Bolum1Verify ile aynı desen).
// Zamanı kısaltmak için takvim test içinde geçici bir kopyayla değiştirilir (interval 30, wait 15, firstArrival 20);
// asset dosyası değişmez. Senaryo kare kare ilerleyen bir IEnumerator'dır; ölçümler her karede örneklenir.
[InitializeOnLoad]
public static class Bolum1AmbulanceVerify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "Bolum1AmbulanceVerify.Stage";
    private const string ReportKey = "Bolum1AmbulanceVerify.Report";
    private const string OkKey = "Bolum1AmbulanceVerify.Ok";
    private const string BatchKey = "Bolum1AmbulanceVerify.Batch";
    private const float TestInterval = 30f, TestWait = 15f, TestFirst = 20f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static Bolum1AmbulanceVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Bölüm 1/Ambulans: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("AMBULANCE VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            CheckEditScene();
            CheckAudioClips();
            Bolum1AmbulanceModelChecks.CheckAssets(Check);
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
        Bolum1AmbulanceAudioMute.Apply(); // SESSİZLİK: Play'e girmeden önce editör sesi kapalı
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    // ---------- edit aşaması ----------
    private static void CheckEditScene()
    {
        var names = new List<string> { Bolum1AmbulanceBuilder.LaneName, Bolum1AmbulanceBuilder.StopName, Bolum1AmbulanceBuilder.MissionName,
            Bolum1AmbulanceBuilder.HudName, Bolum1AmbulanceBuilder.BotName, Bolum1SceneBuilder.CharacterName };
        names.AddRange(Bolum1AmbulanceBuilder.StretcherNames);
        names.AddRange(Bolum1AmbulanceBuilder.PatientTypes.Select(t => "Patient_" + t));
        var missing = names.Where(n => GameObject.Find(n) == null).ToList();
        bool prefabs = File.Exists(Bolum1AmbulanceBuilder.AmbulancePrefabPath) && File.Exists(Bolum1AmbulanceBuilder.StretcherPrefabPath)
            && Bolum1AmbulanceBuilder.PatientTypes.All(t => File.Exists(Bolum1AmbulanceBuilder.PatientPrefabPath(t)));
        var schedule = Bolum1AmbulanceBuilder.Schedule;
        var rules = Bolum1AmbulanceBuilder.Rules;
        var player = GameObject.Find(Bolum1SceneBuilder.CharacterName);
        bool playerOk = player != null && player.GetComponent<CharacterController>() != null && player.GetComponent<ThirdPersonPlayer>() != null
            && player.GetComponent<FootGroundingIK>() != null && player.GetComponent<PlayerInputReader>() != null;
        Check("sahne: oyun objeleri, prefab'lar ve ayar asset'leri", missing.Count == 0 && prefabs && schedule != null && rules != null && playerOk,
            "eksik obje: " + (missing.Count == 0 ? "yok" : string.Join(",", missing)) + "; prefab'lar " + prefabs
            + "; takvim " + (schedule != null ? schedule.interval + "/" + schedule.wait + "/" + schedule.firstArrival : "YOK")
            + "; requiredCarriers " + (rules != null ? rules.requiredCarriers.ToString() : "YOK") + "; oyuncu bileşenleri " + playerOk);

        var stretchers = Bolum1AmbulanceBuilder.StretcherNames.Select(n => GameObject.Find(n)).ToList();
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
        Line("  sedyeler: " + string.Join(", ", stretchers.Select(s => s.name + " " + V(s.transform.position))) + " (çekici kökü " + V(truck.transform.position) + ")");
        foreach (var t in Bolum1AmbulanceBuilder.PatientTypes)
        {
            var p = GameObject.Find("Patient_" + t).GetComponent<Patient>();
            Line("  hasta " + t + " (" + p.displayName + "): kalça " + V(p.Position));
        }
        Line("  AmbulanceStop " + V(GameObject.Find(Bolum1AmbulanceBuilder.StopName).transform.position));
    }

    // (i) ses klipleri sessiz değil; siren ve motor kaynakları 3D + Doppler.
    private static void CheckAudioClips()
    {
        var clips = new[] { SynthClips.Siren(), SynthClips.Engine(), SynthClips.Door(), SynthClips.Horn() };
        var rms = clips.Select(SynthClips.Rms).ToArray();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulancePrefabPath).GetComponent<AmbulanceAudio>();
        bool spatial = prefab.siren.spatialBlend == 1f && prefab.engine.spatialBlend == 1f && prefab.siren.dopplerLevel > 0f && prefab.engine.dopplerLevel > 0f;
        Check("(i) ses: siren/motor/kapı/korna RMS > 0.01; siren ve motor spatialBlend 1, Doppler > 0", rms.All(r => r > 0.01f) && spatial,
            "RMS siren " + rms[0].ToString("F3", Inv) + ", motor " + rms[1].ToString("F3", Inv) + ", kapı " + rms[2].ToString("F3", Inv) + ", korna " + rms[3].ToString("F3", Inv)
            + "; siren " + prefab.siren.spatialBlend + "/" + prefab.siren.dopplerLevel + ", motor " + prefab.engine.spatialBlend + "/" + prefab.engine.dopplerLevel
            + "; siren klibi " + clips[0].length.ToString("F2", Inv) + " sn (435/580 Hz × 0.6 sn)");
        foreach (var c in clips) UnityEngine.Object.DestroyImmediate(c);
    }

    // ---------- Play aşaması ----------
    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition; // float (oyun zamanı) ya da Func<bool>

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
        if (Time.realtimeSinceStartup - realStart > 1500f) { Fail("zaman aşımı (25 dk)"); End(); return; }

        try
        {
            Sample();
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
        Line(allOk ? "AMBULANCE VERIFY OK" : "AMBULANCE VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "ambulance_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "AMBULANCE VERIFY OK" : "AMBULANCE VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    // Bekleme yardımcıları (yield return ile)
    private static float Seconds(float s) { return Time.time + s; }
    private static IEnumerator Until(Func<bool> cond, float timeoutGameSeconds, string what)
    {
        float end = Time.time + timeoutGameSeconds;
        yield return (Func<bool>)(() => cond() || Time.time >= end);
        if (!cond()) throw new Exception("beklenen olmadı: " + what);
    }

    // ---- sahne erişimi (yeniden yüklemeden sonra da çalışsın diye her seferinde bulunur) ----
    private static MissionDirector Mission { get { return MissionDirector.Instance; } }
    private static ThirdPersonPlayer Player { get { return UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>(); } }
    private static PlayerInteraction Interaction { get { return Player.GetComponent<PlayerInteraction>(); } }
    private static StretcherHelperBot Bot { get { return UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>(); } }
    private static StretcherController Stretcher(int i) { return GameObject.Find(Bolum1AmbulanceBuilder.StretcherNames[i]).GetComponent<StretcherController>(); }
    private static Patient PatientOf(string type) { return Mission.patients.First(p => p.name == "Patient_" + type); }

    // ---- her kare örneklenen ölçümler ----
    private class AmbRecord
    {
        public AmbulanceController amb;
        public float arrival = -1f, departure = -1f, despawnDist = -1f;
        public float stopError = -1f, maxLateral, minMovingSpeed = float.MaxValue;
        public int zeroSpeedFrames;
        public float doorL = -1f, doorR = -1f, step = -1f, closedBeforeDeparture = -1f;
        public string wheels = "";
        public float worstWheel = -1f;
        public float minYieldSpeed = float.MaxValue, maxYieldSpeedAfterSettle;
        public int yieldFrames, hornPlayingFrames, hornPlaysAtStart = -1, hornPlays;
        public float stopS;
        public int approachYieldFrames;
        public string approachYieldInfo = "";
    }

    private static readonly List<AmbRecord> records = new List<AmbRecord>();
    private static MissionDirector hooked;

    private static void Hook()
    {
        var m = Mission;
        if (m == null || m == hooked) return;
        hooked = m;
        m.AmbulanceSpawned += a =>
        {
            var r = new AmbRecord { amb = a, stopS = m.StopDistance };
            records.Add(r);
            a.Arrived += x => r.arrival = Mission.Elapsed;
            a.Departed += x => r.departure = Mission.Elapsed;
            a.Despawned += x =>
            {
                var end = GameObject.Find("TrafficDespawn_East");
                r.despawnDist = Vector3.Distance(x.transform.position, end.transform.position);
            };
        };
    }

    private static void Sample()
    {
        Hook();
        foreach (var r in records)
        {
            var a = r.amb;
            if (a == null) continue;
            var f = a.Follower;
            var lane = f.lane;
            if (a.Phase == AmbulancePhase.Waiting)
            {
                if (r.stopError < 0f)
                {
                    var stopT = GameObject.Find(Bolum1AmbulanceBuilder.StopName).transform;
                    r.stopError = Vector3.Distance(a.transform.position, stopT.position);
                    MeasureWheels(r);
                }
                if (a.WaitTime > 3f && a.WaitTime < 10f && r.doorL < 0f)
                {
                    r.doorL = a.DoorLeftAngle; r.doorR = a.DoorRightAngle; r.step = a.StepAngle;
                }
                if (a.WaitRemaining < 0.15f)
                    r.closedBeforeDeparture = Mathf.Max(r.closedBeforeDeparture, Mathf.Max(a.DoorLeftAngle, a.DoorRightAngle, a.StepAngle));
                continue;
            }
            lane.Project(a.transform.position, out float lateral, out _);
            r.maxLateral = Mathf.Max(r.maxLateral, Mathf.Abs(lateral));
            // Durak dışı: varıştan önce duraktan > 0.05 m uzakta, kalkıştan sonra duraktan > 1 m uzakta
            bool atStopArea = (a.Phase == AmbulancePhase.Approaching && f.Distance >= f.StopDistance - 0.05f)
                || (a.Phase == AmbulancePhase.Departing && f.Distance <= r.stopS + 1f);
            if (!atStopArea)
            {
                r.minMovingSpeed = Mathf.Min(r.minMovingSpeed, f.Speed);
                if (f.Speed <= 0f) r.zeroSpeedFrames++;
            }
            if (a.YieldingToCarrier && a.Phase == AmbulancePhase.Approaching)
            {
                r.approachYieldFrames++;
                if (r.approachYieldInfo.Length == 0)
                    r.approachYieldInfo = "t=" + Mission.Elapsed.ToString("F2", Inv) + " s=" + f.Distance.ToString("F1", Inv) + " taşıyıcılar: " + string.Join("; ", CarrierRegistry.All.Select(c => c.Root.name + " " + V(c.Root.position)));
            }
            if (a.YieldingToCarrier)
            {
                r.yieldFrames++;
                r.minYieldSpeed = Mathf.Min(r.minYieldSpeed, f.Speed);
                if (a.sound.horn.isPlaying) r.hornPlayingFrames++;
                if (r.hornPlaysAtStart < 0) r.hornPlaysAtStart = a.sound.HornPlays - 1;
                r.hornPlays = a.sound.HornPlays;
            }
        }
    }

    private static void MeasureWheels(AmbRecord r)
    {
        var f = r.amb.Follower;
        var road = GameObject.Find("SM_B1_Road").transform;
        var parts = new List<string>();
        float worst = 0f;
        foreach (var w in f.wheels)
        {
            float bottom = w.position.y - f.wheelRadius;
            RaycastHit hit = default;
            bool found = false;
            foreach (var h in Physics.RaycastAll(w.position + Vector3.up * 2f, Vector3.down, 6f).OrderBy(h => h.distance))
                if (h.transform.IsChildOf(road)) { hit = h; found = true; break; }
            if (!found) { worst = 99f; parts.Add(w.name + " yol yok"); continue; }
            float gap = bottom - hit.point.y;
            worst = Mathf.Max(worst, Mathf.Abs(gap));
            parts.Add(w.name.Replace("SM_Ambulance_Wheel_", "") + " " + Cm(gap));
        }
        r.worstWheel = worst;
        r.wheels = string.Join(", ", parts);
    }

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static Vector3 parkPosition;

    private static IEnumerator Scenario()
    {
        // Trafik test anahtarı: bu doğrulama yalnız ambulans döngüsünü ölçer; trafik kapalı (sahne yeniden yüklense de).
        TrafficDirector.SuppressedForTests = true;
        if (TrafficDirector.Instance != null) TrafficDirector.Instance.ClearVehicles();
        Line("PLAY: trafik kapalı (TrafficDirector.SuppressedForTests)");

        // Gerçek klip oynarken üç hastanın patikadaki yakın planı ve yüzey ölçümü.
        yield return null;
        Bolum1TestPatients.LayOnPath(Mission.patients); // D2: hastalar minibüste/kayada başlar; taşıma kontrolleri patikada yatar bekler
        yield return null;
        Vector3 patientsCenter = Mission.patients.Aggregate(Vector3.zero, (sum, p) => sum + p.Position) / Mission.patients.Length;
        yield return Capture("hastalar_yakin", patientsCenter + new Vector3(0f, 5f, -4f), patientsCenter, 52f, false);
        yield return Bolum1AmbulanceModelChecks.CheckPoses(Check);
        parkPosition = PatientOf("Tourist").Position + new Vector3(-2.5f, 0f, -1.8f);
        Player.GetComponent<PlayerInputReader>().enabled = false;
        ParkCarriersOffLane();
        records.Clear();
        Hook();
        var schedulePatients = Mission.patients;
        Mission.patients = new Patient[0];
        Mission.missLimit = 999;
        Mission.Begin(UnityEngine.Object.Instantiate(Bolum1AmbulanceBuilder.Schedule));
        Time.timeScale = 5f;
        yield return Until(() => records.Count >= 2 && records[1].arrival > 0f, 245f, "120/240 gerçek takvim");
        Time.timeScale = 1f;
        Check("(düzeltme 7) gerçek takvim 120/240 sn, bekleme 15 sn", Mathf.Abs(records[0].arrival - 120f) <= .5f && Mathf.Abs(records[1].arrival - 240f) <= .5f
            && Mathf.Abs(records[0].departure - records[0].arrival - 15f) <= .1f, records[0].arrival + "/" + records[1].arrival + " sn");
        Mission.patients = schedulePatients;
        Mission.missLimit = 3;
        records.Clear();
        var test = ScriptableObject.CreateInstance<AmbulanceSchedule>();
        test.interval = TestInterval; test.wait = TestWait; test.firstArrival = TestFirst;
        Hook();
        Player.GetComponent<PlayerInputReader>().enabled = false; // test girdisi klavye okuyucusu tarafından ezilmesin
        Mission.Begin(test);
        Line("PLAY: takvim interval " + TestInterval + ", wait " + TestWait + ", firstArrival " + TestFirst + " (geçici kopya; asset değişmedi), yolculuk süresi "
            + Mission.TravelTime.ToString("F2", Inv) + " sn, durak s " + Mission.StopDistance.ToString("F1", Inv) + " m");

        var tourist = PatientOf("Tourist");
        var civilian = PatientOf("Civilian");
        var elder = PatientOf("Elder");
        var s1 = Stretcher(0);
        var s2 = Stretcher(1);

        // (h) koni: şeride, durağın 25 m gerisine bir koni konur (çekici kasasındaki ilk koni).
        var cone = GameObject.Find("SM_TowTruck_Cone_01").GetComponent<Rigidbody>();
        var lane = Mission.lane;
        float coneS = Mission.StopDistance - 25f;
        Vector3 coneStart = lane.PointAt(coneS) + Vector3.up * 0.05f;
        cone.transform.SetPositionAndRotation(coneStart, Quaternion.identity);
        cone.position = coneStart;
        cone.rotation = Quaternion.identity;
        cone.linearVelocity = Vector3.zero;
        cone.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        yield return Seconds(0.5f);
        coneStart = cone.position;

        // ---- (e) / (g) taşıma ----
        var bot = Bot;
        // 1) Hastayı yerdeki sedyeye al
        yield return PutPatientOnStretcher(tourist, s1);
        bool placedOk = tourist.State == PatientState.OnStretcher && s1.Passenger == tourist;

        // 2) Tek taşıyıcı (bot kapalı): sedye kalkmaz, "İki kişi gerekli"
        bot.assistEnabled = false;
        GameNotice.Clear();
        TeleportPlayerToHandle(s1, true);
        yield return null;
        yield return null;
        string hintGrab = Interaction.CurrentHint;
        Interaction.Interact();
        yield return Seconds(0.6f);
        bool singleNotRaised = !s1.IsRaised && Player.HeldStretcher == s1;
        string singleNotice = GameNotice.LastMessage;
        string hintRelease = Interaction.CurrentHint;
        Interaction.Interact(); // bırak
        yield return Seconds(0.2f);

        // 3) Bot açık: oyuncu tutunca bot öbür uca ışınlanır, sedye kalkar
        bot.assistEnabled = true;
        TeleportPlayerToHandle(s1, true);
        yield return null;
        Interaction.Interact();
        yield return Seconds(0.6f);
        bool raised = s1.IsRaised && bot.HeldStretcher == s1 && Player.HeldStretcher == s1;
        float handleGap = Vector3.Distance(Flat(bot.transform.position), Flat(Player.transform.position));
        float anchorHeight = s1.patientAnchor.position.y - Player.transform.position.y;

        // 4) Yürü (römork modeli); taşıma ekran görüntüsü
        var orbit = Camera.main.GetComponent<OrbitCamera>();
        Vector3 fwd = Flat(s1.handleFront.position - s1.handleRear.position).normalized;
        orbit.Yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Player.SetMoveInput(new Vector2(0f, 1f));
        yield return Seconds(1.5f);
        Player.SetMoveInput(new Vector2(0.4f, 1f));
        yield return Seconds(1.0f);
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.3f);
        float trailerGap = Vector3.Distance(Flat(bot.HeldStretcher != null ? bot.GetHandPoint(s1.transform.position) : bot.transform.position), Flat(Player.GetHandPoint(s1.transform.position)));
        Vector3 mid = (Player.transform.position + bot.transform.position) * 0.5f;
        Vector3 sideDir = Vector3.Cross(Vector3.up, fwd);
        yield return Capture("ambulans_tasima", mid + Vector3.up * 2.2f - sideDir * 4.2f + fwd * 0.5f, mid + Vector3.up * 0.6f, 50f, false);
        Check("(e1) oyuncu + bot sedyeyi kaldırıyor; tek taşıyıcıyla kalkmıyor; hasta sedyeye alınıyor",
            placedOk && singleNotRaised && singleNotice == Bolum1AmbulanceBuilder.Rules.NotEnoughCarriersMessage && raised && hintGrab == PlayerInteraction.HintGrab && hintRelease == PlayerInteraction.HintRelease,
            "sedyeye alma " + placedOk + "; bot kapalı: kalktı mı " + !singleNotRaised + ", uyarı \"" + singleNotice + "\", ipuçları \"" + hintGrab + "\" / \"" + hintRelease
            + "\"; bot açık: kalktı " + raised + ", taşıyıcı arası " + M(handleGap) + ", PatientAnchor oyuncu tabanından " + M(anchorHeight)
            + " yukarıda; 2.5 sn yürüyüş sonrası el arası " + M(trailerGap) + " (sap aralığı " + M(s1.HandleSpan) + ")");

        // 5) (g) düşme: bir uç bırakılır (bot), öbürü tutuluyor → 0.5 sn sonra uç yere iner, hasta ragdoll
        Player.SetMoveInput(new Vector2(0f, 1f));
        yield return Seconds(0.3f);
        bot.ReleaseHeld();
        float dropAt = Time.time;
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(1.0f);
        bool ragdoll = tourist.State == PatientState.Ragdoll;
        float fallDist = Vector3.Distance(tourist.Position, s1.patientAnchor.position);
        float dropElapsed = Time.time - dropAt;
        bool tilted = s1.CurrentMode == StretcherController.Mode.Tilted;
        Interaction.Interact(); // oyuncu da bırakır
        yield return Seconds(1.5f);

        // 6) Ragdoll'daki hasta tekrar sedyeye alınır
        yield return PutPatientOnStretcher(tourist, s1);
        bool repicked = tourist.State == PatientState.OnStretcher;

        // 7) İki uç birlikte bırakılır (oyuncu E → bot da aynı anda): güvenli iniş, hasta sedyede kalır
        TeleportPlayerToHandle(s1, true);
        yield return null;
        Interaction.Interact();
        yield return Seconds(0.6f);
        bool raised2 = s1.IsRaised;
        Interaction.Interact();
        yield return Seconds(1.0f);
        bool safe = tourist.State == PatientState.OnStretcher && s1.Passenger == tourist && s1.CurrentMode == StretcherController.Mode.Resting;
        Check("(g) düşme: tek uç bırakılınca hasta 1 sn içinde Ragdoll ve PatientAnchor'dan ≥ 0.3 m; iki uç birlikte bırakılınca OnStretcher",
            ragdoll && fallDist >= 0.3f && tilted && repicked && raised2 && safe,
            "tek uç: " + dropElapsed.ToString("F2", Inv) + " sn sonra durum " + (ragdoll ? "Ragdoll" : "DEĞİL") + ", anchor uzaklığı " + M(fallDist)
            + ", sedye " + (tilted ? "eğik (bir uç yerde)" : "?") + "; ragdoll'dan tekrar sedyeye " + repicked + "; birlikte bırakma: kalkmıştı " + raised2 + ", sonra " + tourist.State);

        // 8) İkinci hasta ikinci sedyeye
        yield return PutPatientOnStretcher(civilian, s2);
        ParkCarriersOffLane();

        // ---- Ambulans 1: varış, kapılar, yükleme ----
        yield return Until(() => records.Count > 0 && records[0].amb != null && records[0].amb.LoadingOpen, 40f, "ambulans 1 yükleme açık");
        var amb1 = records[0].amb;
        yield return Seconds(1.2f);
        yield return CaptureStop(amb1);
        // D2 kararı 6: arka kapıları açık ekipli ambulans, içeride hemşire görünür
        {
            Vector3 back = -amb1.transform.forward;
            Vector3 rearC = amb1.transform.position + back * 3f;
            yield return Capture("d2_ambulans_ekip", rearC + back * 3.4f + amb1.transform.right * 0.8f + Vector3.up * 1.7f, amb1.transform.position + back * 0.4f + Vector3.up * 1.2f, 50f, false);
        }
        yield return LoadAtAmbulance(s1, amb1);
        bool slot1 = tourist.State == PatientState.InAmbulance && tourist.transform.IsChildOf(amb1.patientSlots[0].parent);
        yield return LoadAtAmbulance(s2, amb1);
        bool slot2 = civilian.State == PatientState.InAmbulance && civilian.transform.IsChildOf(amb1.patientSlots[1].parent);
        // Üçüncü hasta: ambulans dolu
        yield return PutPatientOnStretcher(elder, s1);
        GameNotice.Clear();
        yield return LoadAtAmbulance(s1, amb1, expectLoad: false);
        bool rejected = elder.State == PatientState.OnStretcher && GameNotice.LastMessage == "Ambulans dolu" && amb1.Passengers.Count == 2;
        ParkCarriersOffLane();
        Check("(e2) LoadPoint'te hasta PatientSlot_01'e, ikinci PatientSlot_02'ye geçiyor; 3. hasta reddediliyor (\"Ambulans dolu\")",
            slot1 && slot2 && rejected,
            "Turist → " + (slot1 ? "PatientSlot_01" : tourist.State.ToString()) + ", Sivil → " + (slot2 ? "PatientSlot_02" : civilian.State.ToString())
            + ", Yaşlı → " + elder.State + " (uyarı \"" + GameNotice.LastMessage + "\", ambulansta " + amb1.Passengers.Count + ")");

        // Ambulans 1 kalkış: Yaşlı (dolu olduğu için binemedi) +1
        yield return Until(() => records[0].departure > 0f, 20f, "ambulans 1 kalkış");
        yield return null;
        int elderAfter1 = elder.MissedAmbulances;
        bool othersUntouched = tourist.MissedAmbulances == 0 && civilian.MissedAmbulances == 0;

        // HUD ekran görüntüsü: oyuncu Yaşlı'nın yanında, sayaç ve kaçırma göstergeleri görünür
        Time.timeScale = 1f;
        // Oyuncu Yaşlı'nın 3 m batısında, doğuya (kalkan ambulansın arkasına) bakar; ambulans kameradan uzaklaşmış olsun.
        Vector3 hudPos = elder.Position + new Vector3(3f, 0f, 0.6f);
        if (GroundProbe.Find(hudPos + Vector3.up * 1.5f, 5f, out RaycastHit hg)) hudPos.y = hg.point.y + 0.02f;
        Player.Teleport(hudPos, Quaternion.LookRotation(Vector3.left));
        var hudOrbit = Camera.main.GetComponent<OrbitCamera>();
        hudOrbit.Yaw = -100f;
        hudOrbit.Pitch = 22f;
        float departed1 = records[0].departure;
        yield return (Func<bool>)(() => Mission.Elapsed >= departed1 + 2.5f);
        yield return CaptureHud();
        ParkCarriersOffLane();

        // Ambulans 1 şeridin sonunda kaybolur, hastalar Rescued
        yield return Until(() => records[0].despawnDist >= 0f, 40f, "ambulans 1 kayboldu");
        bool rescued = tourist.State == PatientState.Rescued && civilian.State == PatientState.Rescued && !tourist.gameObject.activeSelf;

        // ---- Ambulans 2: varış zamanı; kalkışta şeritte oyuncu (öncelik) ----
        Time.timeScale = 3f;
        yield return Until(() => records.Count > 1 && records[1].arrival > 0f, 40f, "ambulans 2 varış");
        yield return Until(() => records[1].amb.WaitRemaining < 2f, 20f, "ambulans 2 bekleme sonu");
        Time.timeScale = 1f;
        float laneS = Mission.StopDistance + 45f;
        Vector3 inLane = lane.PointAt(laneS);
        Player.Teleport(inLane + Vector3.up * 0.05f, Quaternion.LookRotation(-lane.TangentAt(laneS)));
        var amb2 = records[1].amb;
        yield return Until(() => !Player.HasControl || amb2 == null || amb2.Follower.Distance > laneS + 2f, 30f, "ambulans 2 oyuncuya ulaştı");
        bool knocked = !Player.HasControl;
        yield return Seconds(0.6f);
        lane.Project(Player.transform.position, out float playerLateral, out _);
        yield return Until(() => records[1].departure > 0f, 5f, "ambulans 2 kalkış");
        int elderAfter2 = elder.MissedAmbulances;

        // ---- Ambulans 3: kalkışta Yaşlı 3. kez kaçırır → MissionFailed ----
        Patient failedPatient = null;
        Mission.MissionFailed += p => failedPatient = p;
        ParkCarriersOffLane();
        Time.timeScale = 3f;
        yield return Until(() => failedPatient != null || (records.Count > 2 && records[2].departure > 0f), 60f, "ambulans 3 kalkış");
        Time.timeScale = 1f;
        yield return null;
        var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        string failText = hud.ResultText.text.Replace("\n", " | ");

        // ---- Kaydedilen ölçümler (b, c, d, h) ----
        ReportAmbulances(coneStart, cone, rescued, knocked, playerLateral, lane);

        Check("(f1) kaçırma: dolu olduğu için binemeyen hastanın sayacı artıyor; 3 ambulans ayrılınca MissionFailed",
            elderAfter1 == 1 && othersUntouched && elderAfter2 == 2 && failedPatient == elder && Mission.IsFinished && !Mission.Succeeded
            && failText.StartsWith("Görev başarısız: Yaşlı 3 ambulansı kaçırdı") && failText.Contains(MissionHud.RestartText),
            "Yaşlı sayacı 1. kalkış sonrası " + elderAfter1 + ", 2. sonrası " + elderAfter2 + ", 3. sonrası " + elder.MissedAmbulances
            + "; diğerleri 0 kaldı " + othersUntouched + "; MissionFailed(" + (failedPatient != null ? failedPatient.displayName : "-") + "); HUD \"" + failText + "\"");

        // ---- Yeniden başlat (R) ve başarı senaryosu ----
        var oldMission = Mission;
        Mission.RequestRestart();
        yield return Until(() => Mission != null && Mission != oldMission && Player != null, 30f, "sahne yeniden yüklendi");
        yield return Seconds(0.5f);
        records.Clear(); // yeni görev Sample() içinde bir kez bağlanır (Hook)
        Hook();
        bool succeeded = false;
        Mission.MissionSucceeded += () => succeeded = true;
        Player.GetComponent<PlayerInputReader>().enabled = false;
        Mission.Begin(test);
        tourist = PatientOf("Tourist"); civilian = PatientOf("Civilian"); elder = PatientOf("Elder");
        s1 = Stretcher(0); s2 = Stretcher(1);
        // D2: yeniden başlatılan sahnede hastalar başlangıç yerlerinde (minibüste 2, kayada 1); başarı gerçek akışla:
        // bagaj kapısı → çekme → sedye → ambulans 1; Yaşlı: kayada sırta alma → halatta bot çeker → yukarıda bırakma → sedye → ambulans 2.
        bool freshAfterRestart = elder.MissedAmbulances == 0 && elder.State == PatientState.LyingGround && elder.onLedge
            && tourist.State == PatientState.InVehicle && civilian.State == PatientState.InVehicle;
        var tg = MinivanTailgate.All[0];
        yield return D2Extract(tg, tourist);
        yield return D2Extract(tg, civilian);
        bool extracted = tourist.State == PatientState.LyingGround && civilian.State == PatientState.LyingGround;
        yield return PutPatientOnStretcher(tourist, s1);
        yield return PutPatientOnStretcher(civilian, s2);
        ParkCarriersOffLane();
        Time.timeScale = 2f;
        yield return Until(() => records.Count > 0 && records[0].amb != null && records[0].amb.LoadingOpen, 40f, "R sonrası ambulans 1");
        Time.timeScale = 1f;
        var r1 = records[0].amb;
        yield return LoadAtAmbulance(s1, r1);
        yield return LoadAtAmbulance(s2, r1);
        bool d2First = tourist.State == PatientState.InAmbulance && civilian.State == PatientState.InAmbulance;
        ParkCarriersOffLane();
        yield return D2RescueElder(elder);
        bool elderUp = elder.State == PatientState.LyingGround && !elder.onLedge;
        yield return PutPatientOnStretcher(elder, s1);
        ParkCarriersOffLane();
        Time.timeScale = 3f;
        yield return Until(() => records.Count > 1 && records[1].amb != null && records[1].amb.LoadingOpen, 60f, "R sonrası ambulans 2");
        Time.timeScale = 1f;
        yield return LoadAtAmbulance(s1, records[1].amb);
        yield return null;
        hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        string okText = hud.ResultText.text.Replace("\n", " | ");
        Check("(f2 / D2-d) R yeniden başlatıyor (hastalar minibüste ve kayada); gerçek akışla (çekme, halat, sırtta taşıma, sedye) 3 hasta yüklenince MissionSucceeded",
            freshAfterRestart && extracted && d2First && elderUp && succeeded && Mission.Succeeded && okText.StartsWith(MissionDirector.SuccessText),
            "yeniden başlatma sonrası başlangıç yerleşimi ve sayaçlar " + freshAfterRestart + "; minibüsten çekildi " + extracted + ", ambulans 1'e yüklendi " + d2First
            + "; Yaşlı halatla yukarıda " + elderUp + " (sayaç " + elder.MissedAmbulances + "); MissionSucceeded " + succeeded + "; HUD \"" + okText + "\"");
    }

    // D2: oyuncu hastanın çekme yerine geçer; kapı kapalıysa E ile açar, sonra E ile hastayı patikaya çeker.
    private static IEnumerator D2Extract(MinivanTailgate tg, Patient p)
    {
        Vector3 at = tg.PullerSpot(p, out Quaternion rot);
        Player.Teleport(at, rot);
        yield return null; yield return null;
        if (!tg.IsOpen)
        {
            if (Interaction.CurrentHint != PlayerInteraction.HintOpenTailgate) throw new Exception("bagaj kapısı ipucu yok: \"" + Interaction.CurrentHint + "\"");
            Interaction.Interact();
            yield return Seconds(1f);
            Player.Teleport(at, rot);
            yield return null; yield return null;
        }
        if (Interaction.CurrentHint != PlayerInteraction.HintExtract) throw new Exception("çekme ipucu yok: \"" + Interaction.CurrentHint + "\"");
        Interaction.Interact();
        yield return Until(() => p.State == PatientState.LyingGround, 5f, "çekme " + p.name);
        yield return Seconds(1.2f);
    }

    // D2: Yaşlı kayada; oyuncu halatın dibinde (kayada) yanına gelir, sırtına alır, halata bağlanır; bot kazıktan çeker;
    // tepede E ile bırakır. (İniş / boşken tırmanış Bolum1AnimVerify'de ölçülür.)
    private static IEnumerator D2RescueElder(Patient elder)
    {
        var rope = RopeAnchor.All[0];
        var bot = Bot;
        bot.assistEnabled = true;
        Vector3 toElder = Flat(elder.Position - rope.climbBottom.position);
        Vector3 near = rope.climbBottom.position + toElder.normalized * Mathf.Max(0f, toElder.magnitude - 0.9f);
        if (GroundProbe.Find(near + Vector3.up, 2f, out RaycastHit g)) near.y = g.point.y + 0.02f;
        Player.Teleport(near, Quaternion.LookRotation(toElder.normalized));
        yield return null; yield return null;
        if (Interaction.CurrentHint != PlayerInteraction.HintPiggyback) throw new Exception("sırta alma ipucu yok: \"" + Interaction.CurrentHint + "\"");
        Interaction.Interact();
        yield return Seconds(1.7f);
        if (Player.Piggyback != elder) throw new Exception("Yaşlı sırta alınamadı");
        yield return null;
        if (Interaction.CurrentHint != PlayerInteraction.HintAttachRope) throw new Exception("halat ipucu yok: \"" + Interaction.CurrentHint + "\"");
        Interaction.Interact();
        Time.timeScale = 3f;
        yield return Until(() => Player.Rope == null, 40f, "bot çekerek tepeye");
        Time.timeScale = 1f;
        yield return Seconds(0.3f);
        if (Interaction.CurrentHint != PlayerInteraction.HintPutDownBack) throw new Exception("bırakma ipucu yok: \"" + Interaction.CurrentHint + "\"");
        Interaction.Interact();
        yield return Until(() => elder.State == PatientState.LyingGround, 3f, "Yaşlı yere bırakıldı");
        yield return Seconds(0.8f);
    }

    // Sedyeyi hastanın yanına koyar, oyuncuyu yaklaştırır, E ile hastayı sedyeye alır.
    private static IEnumerator PutPatientOnStretcher(Patient p, StretcherController s)
    {
        if (Player.HeldStretcher != null) { Interaction.Interact(); yield return Seconds(0.5f); }
        Vector3 hips = p.Position;
        Vector3 side = Vector3.back; // Unity −Z = Blender +Y (dağ tarafı), uçurumdan uzak
        Vector3 at = hips + side * 0.8f;
        if (GroundProbe.Find(new Vector3(at.x, hips.y + 1.5f, at.z), 5f, out RaycastHit g)) at.y = g.point.y;
        // Sedye, hastaya paralel (baş yönü +X, hastaların yatış yönü)
        Vector3 forward = Vector3.right;
        Vector3 anchorLocal = s.transform.InverseTransformPoint(s.patientAnchor.position);
        Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
        s.Teleport(at - rot * new Vector3(anchorLocal.x, 0f, anchorLocal.z), rot);
        Vector3 pp = hips + side * 1.4f;
        if (GroundProbe.Find(new Vector3(pp.x, hips.y + 1.5f, pp.z), 5f, out g)) pp.y = g.point.y + 0.02f;
        Player.Teleport(pp, Quaternion.LookRotation(-side));
        yield return null;
        yield return null;
        string hint = Interaction.CurrentHint;
        Interaction.Interact();
        yield return null;
        if (p.State != PatientState.OnStretcher)
            throw new Exception(p.name + " sedyeye alınamadı (ipucu \"" + hint + "\", anchor-hasta " + M(Vector3.Distance(s.patientAnchor.position, hips)) + ", oyuncu-hasta " + M(Vector3.Distance(Player.transform.position, hips)) + ")");
    }

    private static void TeleportPlayerToHandle(StretcherController s, bool front)
    {
        Transform h = front ? s.handleFront : s.handleRear;
        Vector3 outward = Flat(h.position - s.transform.position).normalized;
        Vector3 p = h.position + outward * 0.55f;
        if (GroundProbe.Find(new Vector3(p.x, h.position.y + 1.5f, p.z), 5f, out RaycastHit g)) p.y = g.point.y + 0.02f;
        Player.Teleport(p, Quaternion.LookRotation(outward));
    }

    // Hastalı sedyeyi oyuncu + bot ile kaldırıp LoadPoint'e getirir (ışınlama), yüklemeyi bekler, bırakır.
    private static IEnumerator LoadAtAmbulance(StretcherController s, AmbulanceController amb, bool expectLoad = true)
    {
        var bot = Bot;
        bot.assistEnabled = true;
        TeleportPlayerToHandle(s, false); // arka sap: ön sap komşu sedyenin sapına yakın olabilir
        yield return null;
        Interaction.Interact();
        yield return Seconds(0.4f);
        if (!s.IsRaised) throw new Exception(s.name + " kalkmadı (oyuncunun tuttuğu: " + (Player.HeldStretcher != null ? Player.HeldStretcher.name : "yok") + ")");
        Vector3 back = -amb.transform.forward;
        Vector3 lp = amb.loadPoint.position;
        Vector3 pPos = lp + back * 1.4f;
        Vector3 bPos = lp - back * 1.3f;
        if (GroundProbe.Find(pPos + Vector3.up * 1.5f, 5f, out RaycastHit g)) pPos.y = g.point.y + 0.02f;
        if (GroundProbe.Find(bPos + Vector3.up * 1.5f, 5f, out g)) bPos.y = g.point.y;
        Player.Teleport(pPos, Quaternion.LookRotation(back));
        bot.transform.position = bPos;
        var patient = s.Passenger;
        if (expectLoad)
            yield return Until(() => s.Passenger == null, 3f, "yükleme (" + (patient != null ? patient.name : "-") + ")");
        else
            yield return Seconds(1.0f);
        yield return Seconds(expectLoad ? 0.7f : 0f); // yumuşak geçiş
        Interaction.Interact(); // bırak (bot da aynı anda)
        yield return Seconds(0.5f);
    }

    // Oyuncu ve botu şeritten (ve yükleme noktasından) uzağa, aşağıdaki patikaya taşır.
    private static void ParkCarriersOffLane()
    {
        if (Player.HeldStretcher != null) Interaction.Interact();
        // Patikada, hastaların ilk yerlerinin batısında (şeritten 9 m aşağıda)
        Vector3 pp = parkPosition;
        if (GroundProbe.Find(pp + Vector3.up * 1.5f, 5f, out RaycastHit g)) pp.y = g.point.y + 0.02f;
        Player.Teleport(pp, Quaternion.LookRotation(Vector3.right));
        var bot = Bot;
        Vector3 bp = pp + new Vector3(-1.2f, 0f, 0f);
        if (GroundProbe.Find(bp + Vector3.up * 1.5f, 5f, out g)) bp.y = g.point.y;
        bot.transform.position = bp;
    }

    private static void ReportAmbulances(Vector3 coneStart, Rigidbody cone, bool rescued, bool knocked, float playerLateral, LanePath lane)
    {
        var a1 = records[0];
        var a2 = records.Count > 1 ? records[1] : null;
        float plan1 = TestFirst, plan2 = TestFirst + TestInterval;
        bool arrivals = a2 != null && Mathf.Abs(a1.arrival - plan1) <= 0.5f && Mathf.Abs(a2.arrival - plan2) <= 0.5f;
        float wait1 = a1.departure - a1.arrival;
        float wait2 = a2 != null ? a2.departure - a2.arrival : -1f;
        bool stopOk = a1.stopError >= 0f && a1.stopError <= 0.3f && a2 != null && a2.stopError <= 0.3f;
        bool waitOk = Mathf.Abs(wait1 - TestWait) <= 0.2f && Mathf.Abs(wait2 - TestWait) <= 0.2f;
        bool despawn = a1.despawnDist >= 0f && a1.despawnDist < 0.5f;
        Check("(b) zamanlama: ilk iki varış ±0.5 sn, durma noktası AmbulanceStop'a ≤ 0.3 m, bekleme 15 ± 0.2 sn, TrafficDespawn_East'te kayboluyor",
            arrivals && stopOk && waitOk && despawn && rescued,
            "varış 1 " + S(a1.arrival) + " (plan " + plan1 + "), varış 2 " + (a2 != null ? S(a2.arrival) : "-") + " (plan " + plan2 + "); durak hatası "
            + Cm(a1.stopError) + " / " + (a2 != null ? Cm(a2.stopError) : "-") + "; bekleme " + S(wait1) + " / " + S(wait2)
            + "; yaklaşırken yavaşlama kare " + a1.approachYieldFrames + "/" + (a2 != null ? a2.approachYieldFrames.ToString() : "-") + (a2 != null && a2.approachYieldInfo.Length > 0 ? " (" + a2.approachYieldInfo + ")" : "") + "; kaybolma noktası TrafficDespawn_East'e " + M(a1.despawnDist) + ", içindeki hastalar Rescued " + rescued);

        float lat = records.Where(r => r.amb != null || r.departure > 0f).Max(r => r.maxLateral);
        bool wheelsOk = a1.worstWheel >= 0f && a1.worstWheel <= 0.05f;
        Check("(c) şerit takibi: yanal sapma ≤ 0.05 m; duraktayken 4 tekerlek altı yol yüzeyine ±5 cm",
            lat <= 0.05f && wheelsOk, "en büyük yanal sapma " + Cm(lat) + "; tekerlek tabanı − yol: " + a1.wheels);

        bool doors = Mathf.Abs(a1.doorL - 100f) <= 2f && Mathf.Abs(a1.doorR - 100f) <= 2f && Mathf.Abs(a1.step - 90f) <= 2f && a1.closedBeforeDeparture >= 0f && a1.closedBeforeDeparture <= 2f;
        Check("(d) duraktayken kapılar 100° ± 2°, basamak 90° ± 2°; kalkıştan önce kapalı", doors,
            "RL " + a1.doorL.ToString("F1", Inv) + "°, RR " + a1.doorR.ToString("F1", Inv) + "°, basamak " + a1.step.ToString("F1", Inv)
            + "°; son 0.15 sn'de en büyük açı " + a1.closedBeforeDeparture.ToString("F1", Inv) + "°");

        // İtilen koni uçurumdan düşüp yok olduysa (FallRespawnPlane) itilmiş sayılır
        float coneMoved = cone == null ? float.PositiveInfinity : Vector3.Distance(Flat(cone.position), Flat(coneStart));
        int zero = records.Sum(r => r.zeroSpeedFrames);
        float minMoving = records.Min(r => r.minMovingSpeed);
        float cruise = a1.amb != null ? a1.amb.Follower.cruiseSpeed : 12f;
        bool yieldOk = a2 != null && a2.yieldFrames > 0 && a2.minYieldSpeed <= 0.4f * cruise + 0.05f && a2.minYieldSpeed > 0f;
        bool horn = a2 != null && (a2.hornPlayingFrames > 0 || a2.hornPlays > a2.hornPlaysAtStart);
        bool pushed = knocked && Mathf.Abs(playerLateral) > lane.width * 0.5f;
        Check("(h) öncelik: koni itiliyor, hız durak dışında hiç 0 olmuyor; şeritteki oyuncu için hız ≤ %40 ama > 0, korna çalıyor",
            coneMoved > 0.5f && zero == 0 && minMoving > 0f && yieldOk && horn && pushed,
            "koni " + (cone == null ? "itilip uçurumdan düştü (yok oldu)" : M(coneMoved) + " itildi") + "; durak dışı en düşük hız " + minMoving.ToString("F2", Inv) + " m/sn (0 olan kare " + zero + ")"
            + (a2 != null ? "; oyuncu önündeyken " + a2.yieldFrames + " kare, en düşük hız " + a2.minYieldSpeed.ToString("F2", Inv) + " m/sn (sınır " + (0.4f * cruise).ToString("F2", Inv)
            + "), korna isPlaying " + a2.hornPlayingFrames + " kare, PlayHorn " + (a2.hornPlays - a2.hornPlaysAtStart) + " kez" : "")
            + "; temas: kontrol kaybı " + knocked + ", itildikten sonra şerit merkezine " + M(Mathf.Abs(playerLateral)) + " (şerit yarı genişliği " + M(lane.width * 0.5f) + ")");
    }

    // ---------- ekran görüntüleri ----------
    private static IEnumerator CaptureStop(AmbulanceController amb)
    {
        // Ambulansın arkasından ve vadi tarafından, yukarıdan: açık kapılar ve korkuluk boşluğu (rampa ağzı) görünsün.
        Vector3 back = -amb.transform.forward;
        Vector3 right = amb.transform.right;
        Vector3 rear = amb.transform.position + back * 3f;
        Vector3 camPos = rear + back * 11f + right * 1.5f + Vector3.up * 4.5f;
        return Capture("ambulans_durak", camPos, rear + back * 1.5f + right * 1.2f + Vector3.up * 0.5f, 55f, false);
    }

    private static IEnumerator CaptureHud()
    {
        var cam = Camera.main;
        var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        Line("  HUD: sayaç \"" + hud.TimerText.text + "\", telsiz \"" + hud.RadioLabel.text + "\", ipucu \"" + hud.HintText.text + "\"; göstergeler "
            + string.Join(", ", hud.Indicators.Select(i => i.patient.displayName + (i.Visible ? " görünür " : " gizli ") + i.patient.MissedAmbulances + "/3")));
        return Capture("ambulans_hud", cam.transform.position, cam.transform.position + cam.transform.forward, cam.fieldOfView, true);
    }

    private static IEnumerator Capture(string name, Vector3 pos, Vector3 look, float fov, bool withHud)
    {
        var go = new GameObject("AmbulanceVerify_CaptureCam");
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = main != null ? main.backgroundColor : Color.gray;
        cam.farClipPlane = 2000f;
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = fov;
        go.transform.position = pos;
        go.transform.LookAt(look);
        var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        if (hud != null) foreach (var i in hud.Indicators) i.FaceCamera(cam);
        Canvas canvas = withHud && hud != null ? hud.ScreenCanvas : null;
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
        }
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        // GPU Resident Drawer yeni kameranın görünürlük verisini bir sonraki
        // render döngüsünde hazırlar. Aynı karede Render statik mesh'leri atlayabilir.
        yield return null;
        yield return null;
        if (hud != null) foreach (var i in hud.Indicators) i.FaceCamera(cam);
        Canvas.ForceUpdateCanvases();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
        }
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
        Line("  görüntü: " + OutDir + "/" + name + ".png");
    }

    // ---------- (a) derleme hatası ----------
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
        // Aranan dize etikette yazılmaz; yoksa rapor log'a basılınca sonraki taramalar kendini bulur.
        Check("(a) log'da C# derleme hatası yok", clean, note);
    }

    // ---------- yardımcılar ----------
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
    private static string M(float m) { return m.ToString("F3", Inv) + " m"; }
    private static string Cm(float m) { return (m * 100f).ToString("F1", Inv) + " cm"; }
    private static string S(float s) { return s.ToString("F2", Inv) + " sn"; }
}
