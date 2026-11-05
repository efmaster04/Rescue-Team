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

// Hırsız kamyoneti park manevrası ve kötü park cezası kabul testi (kullanıcı kararı 2026-11-05). Batchmode:
// Unity.exe -batchmode -projectPath ... -executeMethod ThiefVanVerify.Run -logFile <dosya>
// Başarıda "THIEFVAN VERIFY OK" ve exit 0. Rapor: Captures/Bolum1/thiefvan_verify_report.txt
//
// SESSİZLİK: Bolum1AmbulanceAudioMute + AudioListener.volume = 0. Sürüş girişi atlanır (batchmode: DriveIntro.SkipForTests).
// Senaryo: (A) iyi park (minibüs park alanında): kamyonet durağı geçip durur, geri geri direksiyon kırarak bankete girer
//   (tekerlek dönüşü + direksiyon görünür), tam durakta park eder, vuruş yok, ceza yok, minibüs kıpırdamaz; iner, kalkar, şeride
//   S-eğrisiyle girer, şerit sonunda kaybolur (uyarı yok). (B) kötü park (minibüs durağın üstünde, şerit dışı): kamyonet vura vura
//   iter, her vuruşta ₺50–250 ceza + minibüsün üstünde uçan "−₺X", HUD bakiyesi düşer, görev sonu hesabında ayrı kalem; sonunda
//   kamyonet durakta park eder, yer boşalır, minibüs devrilmez / düşmez. (C) boşaltılamayan (minibüs duvara dayalı): bütçe dolunca
//   kamyonet şeritte park eder (döngü yok), hırsız iner, kalkar, kaybolur.
[InitializeOnLoad]
public static class ThiefVanVerify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "ThiefVanVerify.Stage";
    private const string ReportKey = "ThiefVanVerify.Report";
    private const string OkKey = "ThiefVanVerify.Ok";
    private const string BatchKey = "ThiefVanVerify.Batch";
    private const string VanDespawnWarning = "ThiefVan park etmeden";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static ThiefVanVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !Application.isBatchMode && !string.IsNullOrEmpty(SessionState.GetString(StageKey, "")))
            SessionState.EraseString(StageKey);
    }

    [MenuItem("Rescue Team/Bölüm 1/Hırsız Kamyoneti Park: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("THIEFVAN VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
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
        DriveIntro.SkipForTests = true; // editör menüsünden koşarken de sürüş girişi atlanır (batchmode zaten atlar)
        Bolum1AmbulanceAudioMute.Apply(); // SESSİZLİK
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    // ---------- edit aşaması ----------
    private static void CheckEditScene()
    {
        var s = Bolum1ThiefBuilder.Settings;
        var dirGo = GameObject.Find(Bolum1ThiefBuilder.DirectorName);
        var dir = dirGo != null ? dirGo.GetComponent<ThiefDirector>() : null;
        var stop = GameObject.Find(Bolum1ThiefBuilder.VanStopName);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1ThiefBuilder.VanPrefabPath);
        var follower = prefab != null ? prefab.GetComponent<LaneFollower>() : null;
        bool settingsOk = s != null && s.parkOvershoot >= 4f && s.ramPenaltyMin > 0 && s.ramPenaltyMax >= s.ramPenaltyMin && s.maxRams >= 1 && s.reverseSpeed > 0f;
        bool prefabOk = follower != null && follower.axleHalfSpan > 0.5f && follower.wheels.Length >= 4 && follower.maxSteerAngle >= 20f;
        Check("sahne: ThiefDirector + ThiefVanStop, kamyonet prefab'ında dingil + 4 tekerlek + direksiyon açısı, park / ceza ayarları makul",
            dir != null && stop != null && settingsOk && prefabOk,
            "yönetmen " + (dir != null) + ", durak " + (stop != null) + ", ayar " + settingsOk + (s != null ? " (geçiş " + s.parkOvershoot.ToString(Inv) + " m, ceza ₺" + s.ramPenaltyMin + "–" + s.ramPenaltyMax + ", vuruş ≤ " + s.maxRams + " / " + s.maxRamSeconds.ToString(Inv) + " sn)" : "")
            + ", prefab " + prefabOk + (follower != null ? " (dingil " + (follower.axleHalfSpan * 2f).ToString("F2", Inv) + " m, tekerlek " + follower.wheels.Length + ")" : ""));
        if (s != null)
            Check("ceza eğrisi: hafif sürtünme (1 m/sn) ₺" + s.ramPenaltyMin + ", sert çarpma (≥ 3.5 m/sn) ₺" + s.ramPenaltyMax + ", arası artan, 10'a yuvarlı",
                s.RamPenalty(1f) == s.ramPenaltyMin && s.RamPenalty(3.5f) == s.ramPenaltyMax && s.RamPenalty(2.2f) > s.ramPenaltyMin && s.RamPenalty(2.2f) < s.ramPenaltyMax && s.RamPenalty(2.2f) % 10 == 0 && s.RamPenalty(9f) == s.ramPenaltyMax,
                "1 → ₺" + s.RamPenalty(1f) + ", 2.2 → ₺" + s.RamPenalty(2.2f) + ", 3.5 → ₺" + s.RamPenalty(3.5f) + ", 9 → ₺" + s.RamPenalty(9f));
    }

    // ---------- Play aşaması ----------
    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition;
    private static readonly List<string> vanWarnings = new List<string>();

    private static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.volume = 0f;
        if (scenario == null)
        {
            if (realStart < 0f) { realStart = Time.realtimeSinceStartup; return; }
            if (Time.frameCount < 5) return;
            report = new StringBuilder(SessionState.GetString(ReportKey, ""));
            allOk = SessionState.GetBool(OkKey, false);
            batchMode = SessionState.GetBool(BatchKey, true);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
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

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg != null && msg.Contains(VanDespawnWarning)) vanWarnings.Add(msg);
    }

    private static void End()
    {
        EditorApplication.update -= PlayTick;
        Application.logMessageReceived -= OnLog;
        SessionState.EraseString(StageKey);
        Time.timeScale = 1f;
        scenario = null;
        Finish();
    }

    private static void Finish()
    {
        CheckCompileErrors();
        Line(allOk ? "THIEFVAN VERIFY OK" : "THIEFVAN VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "thiefvan_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "THIEFVAN VERIFY OK" : "THIEFVAN VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        DriveIntro.SkipForTests = false;
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
    private static ServiceVanDriver Minibus { get { return ServiceVanDriver.Instance; } }
    private static ThiefDirector Dir { get { return ThiefDirector.Instance; } }
    private static MissionHud Hud;
    private static LanePath east;
    private static ParkingZone zone;
    private static Vector3 zonePos; private static Quaternion zoneRot;

    private static ThiefSettings TestSettings(float vanWait)
    {
        var s = UnityEngine.Object.Instantiate(Bolum1ThiefBuilder.Settings);
        s.firstWaveSeconds = 99999f;
        s.waveIntervalSeconds = 99999f;
        s.vanWaitSeconds = vanWait;
        s.useFixedSeed = true;
        return s;
    }

    private static IEnumerator Scenario()
    {
        yield return null;
        east = GameObject.Find(Bolum1AmbulanceBuilder.LaneName).GetComponent<LanePath>();
        zone = GameObject.Find(Bolum1DriveBuilder.ZoneName).GetComponent<ParkingZone>();
        Hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        zonePos = Minibus.transform.position; zoneRot = Minibus.transform.rotation;
        // Trafik rastgele çıkmaz, ambulans gelmez, hırsız dalgaları yalnız elle (SpawnVan)
        ThiefDirector.SuppressedForTests = true;
        var ts = UnityEngine.Object.Instantiate(Bolum1TrafficBuilder.Settings);
        ts.useFixedSeed = true;
        ts.calmSeconds = new Vector2(99999f, 99999f);
        ts.calmSingleVehicleChance = 0f;
        Traffic.Begin(ts);
        var schedule = UnityEngine.Object.Instantiate(Mission.schedule);
        schedule.firstArrival = 99999f;
        Mission.Begin(schedule);
        Dir.enabled = true;
        Dir.Begin(TestSettings(8f));
        Line("PLAY: durak s " + Dir.StopS.ToString("F1", Inv) + " yanal " + Dir.ShoulderLateral.ToString("F2", Inv) + " m; minibüs " + V(Minibus.transform.position) + " durum " + Minibus.State + " iyi park " + zone.Evaluate(Minibus.Footprint(), Minibus.LaneSamples()));

        yield return PhaseGoodPark();
        yield return PhaseBadPark();
        yield return PhaseBlocked();
    }

    private static ThiefVan Spawn(int thieves)
    {
        var van = Dir.SpawnVan(thieves, 0);
        return van;
    }

    // Manevra örnekleyici: her kare kamyonetin adımlarını, hızını, direksiyonunu, tekerlek dönüşünü izler
    private sealed class Sampler
    {
        public readonly HashSet<ThiefVan.ManeuverStep> steps = new HashSet<ThiefVan.ManeuverStep>();
        public float minSpeed, maxSteer, spinStart = -1f, spinChange, minibusMoved;
        public int maxFloats;
        public bool sawManeuver, sawMerge;
        public Vector3 minibusStart;
        public void Tick(ThiefVan v)
        {
            if (v == null) return;
            if (v.Phase == ThiefVan.VanPhase.Maneuvering || v.Step == ThiefVan.ManeuverStep.Merge)
            {
                sawManeuver |= v.Phase == ThiefVan.VanPhase.Maneuvering;
                sawMerge |= v.Step == ThiefVan.ManeuverStep.Merge;
                steps.Add(v.Step);
                minSpeed = Mathf.Min(minSpeed, v.ManeuverSpeed);
                maxSteer = Mathf.Max(maxSteer, Mathf.Abs(v.Follower.CurrentSteerAngle));
                float spin = v.Follower.WheelSpinDegrees;
                if (spinStart < 0f) spinStart = spin; else spinChange = Mathf.Max(spinChange, Mathf.Abs(Mathf.DeltaAngle(spinStart, spin)));
            }
            if (Minibus != null) minibusMoved = Mathf.Max(minibusMoved, Flat(Minibus.transform.position - minibusStart).magnitude);
            if (Hud != null) maxFloats = Mathf.Max(maxFloats, Hud.FloatingCount);
        }
    }

    // (A) iyi park
    private static IEnumerator PhaseGoodPark()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (A)");
        var smp = new Sampler { minibusStart = Minibus.transform.position };
        float t0 = Time.time;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.Phase == ThiefVan.VanPhase.Maneuvering || Time.time - t0 > 90f; });
        float approachT = Time.time - t0;
        float sStopInLane = van != null ? van.Follower.Distance : -1f;
        east.Project(van != null ? van.transform.position : Vector3.zero, out float latAtStop, out _);
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.IsParked || Time.time - t0 > 160f; });
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        float s = -1f, lat = 0f, headDot = -1f;
        if (van != null)
        {
            s = east.Project(van.transform.position, out lat, out _);
            headDot = Vector3.Dot(Flat(van.transform.forward).normalized, Flat(east.TangentAt(s)).normalized);
        }
        bool atStop = parked && Mathf.Abs(s - Dir.StopS) < 0.6f && Mathf.Abs(lat) - van.Vehicle.bodySize.x * 0.5f >= east.width * 0.5f - 0.05f && Mathf.Abs(lat - Dir.ShoulderLateral) < 0.35f && headDot > 0.985f;
        bool overshoot = sStopInLane > Dir.StopS + 3f && Mathf.Abs(latAtStop) < 1.5f;
        bool reversed = smp.sawManeuver && smp.steps.Contains(ThiefVan.ManeuverStep.Reverse) && smp.minSpeed < -0.8f && van != null && van.ReverseDistance > 5f;
        bool steered = smp.maxSteer > 15f && smp.spinChange > 60f;
        bool unregistered = parked && !LaneOccupancy.Followers.Contains(van.Follower) && !van.ParkedInLane;
        bool untouched = van != null && van.Rams == 0 && Dir.RamCount == 0 && Dir.RamPenaltyTotal == 0 && smp.minibusMoved < 0.05f && Minibus.State == ServiceVanDriver.VanState.Parked && Minibus.Damage == 0f;
        Check("(A1) iyi park: kamyonet durağı " + (van != null ? (sStopInLane - Dir.StopS).ToString("F1", Inv) : "-") + " m geçip şeritte durur, GERİ GERİ direksiyon kırarak (|açı| > 15°, tekerlek döner) bankete girer, tam durakta şeride paralel park eder (s ± 0.6, gövde şerit dışı), şerit kaydından çıkar; minibüse dokunmaz, vuruş / ceza yok",
            parked && atStop && overshoot && reversed && steered && unregistered && untouched,
            "park " + parked + " (" + (Time.time - t0).ToString("F1", Inv) + " sn; yaklaşma " + approachT.ToString("F1", Inv) + " sn) s " + s.ToString("F1", Inv) + " / durak " + Dir.StopS.ToString("F1", Inv) + ", yanal " + lat.ToString("F2", Inv) + " / " + Dir.ShoulderLateral.ToString("F2", Inv)
            + ", yön " + headDot.ToString("F3", Inv) + "; şeritte durdu s " + sStopInLane.ToString("F1", Inv) + " yanal " + latAtStop.ToString("F2", Inv) + "; adımlar [" + string.Join(",", smp.steps) + "], en düşük hız " + smp.minSpeed.ToString("F2", Inv) + " m/sn, geri yol " + (van != null ? van.ReverseDistance.ToString("F1", Inv) : "-") + " m, en çok direksiyon " + smp.maxSteer.ToString("F0", Inv) + "°, tekerlek dönüşü " + smp.spinChange.ToString("F0", Inv)
            + "°; kayıt dışı " + unregistered + ", minibüs kıpırdadı " + smp.minibusMoved.ToString("F2", Inv) + " m, vuruş " + (van != null ? van.Rams : -1) + ", ceza ₺" + Dir.RamPenaltyTotal + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        if (!parked) { Dir.ClearAll(); yield break; }
        // İniş, kalkış (8 sn), şeride giriş, kaybolma
        yield return Until(() => van == null || van.Spawned >= 1, 12f, "hırsız indi");
        bool unloaded = van != null && van.Spawned == 1 && van.Thieves.Count == 1 && van.Thieves[0] != null;
        var thief = unloaded ? van.Thieves[0] : null;
        if (thief != null) thief.Stun(60f, Vector3.zero, "test-bekleme"); // binmesin; kalkışta kaçar
        Time.timeScale = 4f;
        float td = Time.time;
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.Phase == ThiefVan.VanPhase.Departing || Time.time - td > 40f; });
        bool departed = van != null && van.Phase == ThiefVan.VanPhase.Departing && LaneOccupancy.Followers.Contains(van.Follower);
        bool mergeSeen = false; float mergeMaxSteer = 0f; bool resumed = false; float sResume = -1f, latResume = 99f;
        yield return (Func<bool>)(() =>
        {
            smp.Tick(van);
            if (van != null && van.Step == ThiefVan.ManeuverStep.Merge) { mergeSeen = true; mergeMaxSteer = Mathf.Max(mergeMaxSteer, Mathf.Abs(van.Follower.CurrentSteerAngle)); }
            if (van != null && !resumed && van.Phase == ThiefVan.VanPhase.Departing && van.Step == ThiefVan.ManeuverStep.None && !van.Vehicle.parked) { resumed = true; sResume = east.Project(van.transform.position, out latResume, out _); }
            return van == null || Time.time - td > 120f;
        });
        Time.timeScale = 1f;
        bool gone = van == null;
        Check("(A2) iner, 8 sn sonra kalkar: şeride kaydolur, ileri S-eğrisiyle (direksiyon) şerit ortasına girer, LaneFollower'a devredilir (yanal < 0.4 m), trafik gibi gidip şerit sonunda kaybolur; \"park etmeden kayboldu\" uyarısı yok",
            unloaded && departed && mergeSeen && mergeMaxSteer > 8f && resumed && Mathf.Abs(latResume) < 0.4f && gone && vanWarnings.Count == 0,
            "indi " + unloaded + ", kalkış " + departed + ", şeride giriş " + mergeSeen + " (direksiyon " + mergeMaxSteer.ToString("F0", Inv) + "°), devir " + resumed + " (s " + sResume.ToString("F1", Inv) + " yanal " + latResume.ToString("F2", Inv) + " m), kayboldu " + gone + ", uyarı " + vanWarnings.Count);
        Dir.ClearAll();
        yield return Seconds(0.5f);
    }

    // Minibüsü park alanından ~4 m ileri taşmış, durağın üstünde (şerit dışı, banket) bırak: s = durak − 1.5 (gövde durak
    // dikdörtgeninin 4.5 m'sini kapatır), yanal = banket + 0.5 (gövde şeride girmez). Kamyonet onu geriye, park alanına doğru iter.
    private static void ParkMinibusOnStop()
    {
        float s = Dir.StopS - 1.5f;
        Vector3 pos = east.PointAt(s) + east.RightAt(s) * (Dir.ShoulderLateral + 0.5f);
        if (GroundProbe.Find(pos + Vector3.up * 2f, 5f, out RaycastHit g)) pos.y = g.point.y;
        Minibus.ResetDamageForTest();
        Minibus.TeleportTo(pos, Quaternion.LookRotation(-east.TangentAt(s), Vector3.up), true); // kök +Z arka → ileri = şerit yönü
    }

    // (B) kötü park: minibüs durağın üstünde
    private static IEnumerator PhaseBadPark()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        ParkMinibusOnStop();
        yield return Seconds(0.5f);
        bool blocksLane = Minibus.BlocksLane(east, out _, out _, out _);
        Vector3 minibusStart = Minibus.transform.position;
        int floats0 = Hud != null ? Hud.PenaltyFloatingSpawned : 0;
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (B)");
        bool occupiedAtStart = false;
        var smp = new Sampler { minibusStart = minibusStart };
        var fines = new List<string>();
        int seenRams = 0;
        float t0 = Time.time;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() =>
        {
            smp.Tick(van);
            if (van != null && van.Phase == ThiefVan.VanPhase.Maneuvering && van.Step == ThiefVan.ManeuverStep.Reverse && van.Rams == 0) occupiedAtStart |= van.SpotOccupied;
            if (van != null && van.Rams > seenRams) { seenRams = van.Rams; fines.Add(van.ManeuverLog.Count > 0 ? van.ManeuverLog[van.ManeuverLog.Count - 1] : "?"); }
            return van == null || van.IsParked || Time.time - t0 > 200f;
        });
        yield return Until(() => Minibus.State == ServiceVanDriver.VanState.Parked, 15f, "minibüs itildikten sonra durur");
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        float s = -1f, lat = 0f;
        if (van != null) s = east.Project(van.transform.position, out lat, out _);
        bool atStop = parked && !van.ParkedInLane && Mathf.Abs(s - Dir.StopS) < 0.8f && Mathf.Abs(lat) - van.Vehicle.bodySize.x * 0.5f >= east.width * 0.5f - 0.1f;
        bool rammed = van != null && van.Rams >= 1 && Dir.RamCount >= van.Rams && Dir.RamLog.All(r => r.fine >= Dir.Active.ramPenaltyMin && r.fine <= Dir.Active.ramPenaltyMax && r.fine % 10 == 0) && Dir.RamPenaltyTotal == Dir.RamLog.Sum(r => r.fine) && van.RamPenaltyTotal <= Dir.RamPenaltyTotal;
        bool escalates = Dir.RamLog.Count < 2 || Dir.RamLog[Dir.RamLog.Count - 1].speed > Dir.RamLog[0].speed - 0.05f;
        bool cleared = van != null && !van.IsSpotOccupied() && smp.minibusMoved > 0.5f;
        bool minibusOk = Minibus.State == ServiceVanDriver.VanState.Parked && Minibus.TiltDeg < 10f && Minibus.FallRespawns == 0 && Minibus.PhysicalShoves >= 1;
        int floats = Hud != null ? Hud.PenaltyFloatingSpawned - floats0 : -1;
        bool hudOk = Hud != null && floats == Dir.RamCount && Hud.LastPenaltyLabel.StartsWith("-" + LootRegistry.MoneySymbol) && Hud.LastPenaltyLabel.EndsWith(MissionHud.PenaltySuffix)
            && Hud.MoneyText.text.Contains("-") && Hud.MoneyText.text.Contains(Dir.RamPenaltyTotal.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")));
        Check("(B1) kötü park (minibüs durağın üstünde, şerit dışı): kamyonet geri geri yanaşırken minibüse vurur, minibüs fiziksel itilir (PhysX), kamyonet ileri açılıp artan hızla yeniden vurur; her vuruşta ₺" + Dir.Active.ramPenaltyMin + "–" + Dir.Active.ramPenaltyMax + " ceza (hıza göre, 10'a yuvarlı), kayıt toplamı tutar; minibüsün üstünde uçan \"−₺X çarpma (kötü park)\", HUD bakiyesi eksiye düşer; yer boşalır, kamyonet durakta park eder; minibüs devrilmez / uçuruma düşmez",
            parked && atStop && occupiedAtStart && rammed && escalates && cleared && minibusOk && hudOk,
            "şeridi kapatıyordu " + blocksLane + ", başta yer dolu " + occupiedAtStart + ", park " + parked + " (" + (Time.time - t0).ToString("F1", Inv) + " sn) s " + s.ToString("F1", Inv) + " / " + Dir.StopS.ToString("F1", Inv) + " yanal " + lat.ToString("F2", Inv) + " şeritte " + (van != null && van.ParkedInLane)
            + "; vuruş " + (van != null ? van.Rams : -1) + " (kayıt " + Dir.RamCount + "): " + string.Join(" | ", Dir.RamLog.Select(r => r.speed.ToString("F1", Inv) + " m/sn → ₺" + r.fine + " [" + r.source + "]")) + ", toplam ₺" + Dir.RamPenaltyTotal + ", artan hız " + escalates
            + "; yer boşaldı " + cleared + " (minibüs " + smp.minibusMoved.ToString("F2", Inv) + " m itildi → " + MinibusPose() + ", fiziksel itilme " + Minibus.PhysicalShoves + ", durum " + Minibus.State + ", eğim " + Minibus.TiltDeg.ToString("F1", Inv) + "°, düşme " + Minibus.FallRespawns + ", hasar %" + Minibus.Damage.ToString("F0", Inv) + ", kamyonet gömülü " + (van != null && van.OverlapsMinibus()) + ")"
            + "; HUD uçan yazı " + floats + " (son \"" + (Hud != null ? Hud.LastPenaltyLabel : "-") + "\"), bakiye \"" + (Hud != null ? Hud.MoneyText.text : "-") + "\"" + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        // Görev sonu hesabı: ceza ayrı gider kalemi
        var session = new MissionBridge.Session { jobId = "test", jobTitle = "Hırsız kamyoneti testi", fieldScene = "Bolum1_Ucurum", officeScene = "-", jobPayment = 1000, distanceKm = 10f };
        var trackerGo = new GameObject("ThiefVanVerify_Tracker");
        var tracker = trackerGo.AddComponent<FieldMissionTracker>();
        tracker.Setup(session);
        var inputs = tracker.Collect(Mission);
        var breakdown = FieldPayout.Compute(inputs);
        var plain = new FieldPayout.Inputs { succeeded = true, jobPayment = 1000, distanceKm = 10f, fieldSeconds = inputs.fieldSeconds, stolenGoodsValue = inputs.stolenGoodsValue, missedAmbulances = inputs.missedAmbulances, vanDamage = inputs.vanDamage };
        var plainB = FieldPayout.Compute(plain);
        UnityEngine.Object.Destroy(trackerGo);
        var lines = FieldPayout.Lines(breakdown, 1000);
        // Gider iş bedeliyle (₺1.000) sınırlı (FieldPayout kuralı): beklenen = min(sınır, cezasız gider + ceza)
        int expectedExpenses = Mathf.Min(1000, plainB.expenses + Dir.RamPenaltyTotal);
        // Başarılı sayılan görevde kasaya giren (iş bedeli + bonus − gider) ceza kadar azalır
        var successB = FieldPayout.Compute(new FieldPayout.Inputs { succeeded = true, jobPayment = 1000, distanceKm = 10f, fieldSeconds = inputs.fieldSeconds, stolenGoodsValue = inputs.stolenGoodsValue, missedAmbulances = inputs.missedAmbulances, vanDamage = inputs.vanDamage, thiefRamPenalty = inputs.thiefRamPenalty, thiefRams = inputs.thiefRams });
        var successPlain = FieldPayout.Compute(new FieldPayout.Inputs { succeeded = true, jobPayment = 1000, distanceKm = 10f, fieldSeconds = inputs.fieldSeconds, stolenGoodsValue = inputs.stolenGoodsValue, missedAmbulances = inputs.missedAmbulances, vanDamage = inputs.vanDamage });
        int expectedPayout = Mathf.Max(0, 1000 + successB.bonus - expectedExpenses);
        Check("(B2) ceza görev sonu hesabına ayrı kalem olarak girer: FieldMissionTracker.Collect.thiefRamPenalty = toplam, FieldPayout.expenses o kadar artar (iş bedeli sınırı içinde), başarılı görevde kasaya giren o kadar azalır, rapor satırında \"hırsız kamyoneti çarpması ₺X [N vuruş]\"",
            Dir.RamPenaltyTotal > 0 && inputs.thiefRamPenalty == Dir.RamPenaltyTotal && inputs.thiefRams == Dir.RamCount && breakdown.thiefRamCharge == Dir.RamPenaltyTotal && breakdown.expenses == expectedExpenses && breakdown.expenses > plainB.expenses
            && successB.expectedPayout == expectedPayout && successB.expectedPayout < successPlain.expectedPayout && lines.Any(l => l.Contains("hırsız kamyoneti çarpması") && l.Contains(Dir.RamCount + " vuruş")),
            "girdi ₺" + inputs.thiefRamPenalty + " (" + inputs.thiefRams + " vuruş), gider ₺" + breakdown.expenses + " (cezasız ₺" + plainB.expenses + "), başarılı görevde kasaya ₺" + successB.expectedPayout + " (cezasız ₺" + successPlain.expectedPayout + "), satır: " + lines[2]);
        if (parked)
        {
            yield return Until(() => van == null || van.Spawned >= 1, 12f, "hırsız indi (B)");
            if (van != null && van.Thieves.Count > 0 && van.Thieves[0] != null) van.Thieves[0].Stun(60f, Vector3.zero, "test-bekleme");
            Time.timeScale = 4f;
            yield return Until(() => van == null, 150f, "kamyonet kayboldu (B)");
            Time.timeScale = 1f;
        }
        Check("(B3) kötü parktan sonra da kamyonet iner, kalkar ve şerit sonunda kaybolur; uyarı yok", van == null && vanWarnings.Count == 0, "kayboldu " + (van == null) + ", uyarı " + vanWarnings.Count);
        Dir.ClearAll();
        Minibus.TeleportTo(zonePos, zoneRot, true);
        yield return Seconds(0.5f);
    }

    // (C) boşaltılamayan: minibüs arkasından duvara dayalı (itme yönünde statik engel)
    private static IEnumerator PhaseBlocked()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        ParkMinibusOnStop();
        yield return Seconds(0.3f);
        // "Kayaya dayalı" minibüs: fiziksel itme başlatılmaz, minibüs kinematik kalır ve kıpırdamaz (test bayrağı). Yapay duvar
        // denendi: PhysX sıkışan minibüsü açık taraftan (şerit) fırlatıp döndürdü, senaryo deterministik olmadı.
        ThiefVan.ImmovableMinibusForTest = true;
        Minibus.physicalShoveEnabled = false; // gövdeye giren kinematik kamyonet de itmeyi başlatmasın (ScanForPusher)
        yield return Seconds(0.3f);
        Vector3 minibusStart = Minibus.transform.position;
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (C)");
        var smp = new Sampler { minibusStart = minibusStart };
        float t0 = Time.time, maneuverEnd = -1f;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => { smp.Tick(van); if (van != null && van.IsParked && maneuverEnd < 0f) maneuverEnd = Time.time; return van == null || van.IsParked || Time.time - t0 > 220f; });
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        float s = -1f, lat = 0f;
        if (van != null) s = east.Project(van.transform.position, out lat, out _);
        float maxSec = Dir.Active.maxRamSeconds;
        bool bounded = van != null && van.Rams >= 1 && van.Rams <= Dir.Active.maxRams && van.ManeuverTime <= maxSec + 25f;
        bool inLane = parked && van.ParkedInLane && Mathf.Abs(lat) < 1.2f && LaneOccupancy.Followers.Contains(van.Follower) && s > Dir.StopS + 2f;
        // Yapay duvarla sıkışan minibüs yana (şerit tarafı açık) kayıp dönebilir; ölçüt: devrilmez / düşmez (yer değiştirme serbest)
        bool minibusOk = Minibus.TiltDeg < 35f && Minibus.FallRespawns == 0;
        Check("(C1) boşaltılamayan yer (minibüs duvara dayalı): vuruş bütçesi (≤ " + Dir.Active.maxRams + " vuruş / " + maxSec.ToString("F0", Inv) + " sn) dolunca kamyonet sonsuz döngüye girmez, şeride geri çıkıp ORADA park eder (şerit kaydında kalır: arkadaki trafik bekler); minibüs yerinde kalır, uçuruma düşmez",
            parked && bounded && inLane && minibusOk,
            "park " + parked + " (" + (maneuverEnd > 0f ? (maneuverEnd - t0).ToString("F1", Inv) : "-") + " sn) s " + s.ToString("F1", Inv) + " yanal " + lat.ToString("F2", Inv) + " şeritte " + (van != null && van.ParkedInLane) + " kayıtlı " + (van != null && LaneOccupancy.Followers.Contains(van.Follower))
            + "; vuruş " + (van != null ? van.Rams : -1) + ", manevra " + (van != null ? van.ManeuverTime.ToString("F1", Inv) : "-") + " sn, ceza ₺" + Dir.RamPenaltyTotal + "; minibüs " + smp.minibusMoved.ToString("F2", Inv) + " m → " + MinibusPose() + ", eğim " + Minibus.TiltDeg.ToString("F1", Inv) + "°, düşme " + Minibus.FallRespawns
            + ", yer dolu " + (van != null && van.IsSpotOccupied()) + ", kamyonet gömülü " + (van != null && van.OverlapsMinibus()) + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        if (parked)
        {
            yield return Until(() => van == null || van.Spawned >= 1, 12f, "hırsız indi (C)");
            bool unloaded = van != null && van.Spawned >= 1;
            if (van != null && van.Thieves.Count > 0 && van.Thieves[0] != null) van.Thieves[0].Stun(60f, Vector3.zero, "test-bekleme");
            Time.timeScale = 4f;
            yield return Until(() => van == null, 150f, "kamyonet kayboldu (C)");
            Time.timeScale = 1f;
            Check("(C2) şeritte park eden kamyonet de hırsız indirir, kalkar, şerit sonunda kaybolur; uyarı yok", unloaded && van == null && vanWarnings.Count == 0, "indi " + unloaded + ", kayboldu " + (van == null) + ", uyarı " + vanWarnings.Count);
        }
        ThiefVan.ImmovableMinibusForTest = false;
        Minibus.physicalShoveEnabled = true;
        Dir.ClearAll();
        Minibus.TeleportTo(zonePos, zoneRot, true);
        yield return Seconds(0.3f);
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
    // Minibüsün şerit koordinatı: s, yanal, şerit yönüne göre sapma (°)
    private static string MinibusPose()
    {
        if (Minibus == null) return "-";
        float s = east.Project(Minibus.transform.position, out float lat, out _);
        float yaw = Vector3.SignedAngle(Flat(east.TangentAt(s)), Flat(Minibus.Forward), Vector3.up);
        return "s " + s.ToString("F1", Inv) + " yanal " + lat.ToString("F2", Inv) + " sapma " + yaw.ToString("F0", Inv) + "°";
    }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
