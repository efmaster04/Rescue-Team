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

// Bölüm 1 trafik kabul testi. Batchmode (ekran görüntüsü için -nographics OLMADAN, -quit OLMADAN):
// Unity.exe -batchmode -projectPath ... -executeMethod Bolum1TrafficVerify.Run -logFile <dosya>
// Başarıda "TRAFFIC VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/Bolum1/traffic_verify_report.txt
//
// SESSİZLİK: batchmode'da Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0.
// Ses testleri sesi çalmaz, yalnız klip verisini ölçer.
// Ayarlar test içinde geçici kopyalarla değiştirilir (TrafficSettings, AmbulanceSchedule); asset dosyaları değişmez.
// Senaryo kare kare ilerleyen bir IEnumerator'dır (Bolum1AmbulanceVerify ile aynı desen); ölçümler her karede örneklenir.
[InitializeOnLoad]
public static class Bolum1TrafficVerify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "Bolum1TrafficVerify.Stage";
    private const string ReportKey = "Bolum1TrafficVerify.Report";
    private const string OkKey = "Bolum1TrafficVerify.Ok";
    private const string BatchKey = "Bolum1TrafficVerify.Batch";
    private const int Seed = 20261015;
    private const float SimSeconds = 300f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static Bolum1TrafficVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Bölüm 1/Trafik: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("TRAFFIC VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            CheckEditScene();
            CheckAudioClips();
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
        var names = new List<string> { Bolum1AmbulanceBuilder.LaneName, Bolum1TrafficBuilder.WestLaneName, Bolum1TrafficBuilder.DirectorName, Bolum1AmbulanceBuilder.MissionName };
        var missing = names.Where(n => GameObject.Find(n) == null).ToList();
        var director = GameObject.Find(Bolum1TrafficBuilder.DirectorName)?.GetComponent<TrafficDirector>();
        bool prefabs = Bolum1TrafficBuilder.Types.All(t => AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabPath(t)) != null);
        bool dirOk = director != null && director.settings != null && director.eastBound != null && director.westBound != null && director.prefabs.Length == 5 && director.prefabs.All(p => p != null) && director.mission != null;
        var cones = Bolum1TowTruck.ConeNames.Select(n => GameObject.Find(Bolum1TrafficBuilder.ConeControllerName(n))?.GetComponent<TrafficCone>()).ToList();
        bool conesOk = cones.All(c => c != null && c.road != null && c.target != null && c.target.name == c.name.Replace("TrafficCone_", ""));
        var player = GameObject.Find(Bolum1SceneBuilder.CharacterName)?.GetComponent<ThirdPersonPlayer>();
        bool playerOk = player != null && player.knockdown != null && player.knockdown.ragdollBodies.Length >= 10 && player.knockdown.ragdollBodies.All(b => b != null && b.isKinematic)
            && player.knockdown.ragdollColliders.All(c => c != null && !c.enabled);
        var s = Bolum1TrafficBuilder.Settings;
        Check("sahne: şeritler, trafik yönetmeni, 5 araç prefab'ı, ayar asset'i, koniler, oyuncu ragdoll'u",
            missing.Count == 0 && prefabs && dirOk && conesOk && playerOk && s != null,
            "eksik obje: " + (missing.Count == 0 ? "yok" : string.Join(",", missing)) + "; prefab'lar " + prefabs + "; yönetmen " + dirOk + "; koniler " + conesOk
            + "; oyuncu ragdoll " + (player != null && player.knockdown != null ? player.knockdown.ragdollBodies.Length + " gövde" : "YOK")
            + "; ayarlar " + (s != null ? "sakin " + s.calmSeconds.x + "–" + s.calmSeconds.y + ", dalga " + s.waveSize.x + "–" + s.waveSize.y + ", aralık " + s.waveGapSeconds.x + "–" + s.waveGapSeconds.y
            + ", tipler " + string.Join(" ", s.types.Select(e => e.type + ":" + e.weight.ToString(Inv) + "/" + e.cruiseSpeed.ToString(Inv) + "m/sn")) : "YOK"));
        foreach (var t in Bolum1TrafficBuilder.Types)
        {
            var v = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabPath(t)).GetComponent<TrafficVehicle>();
            var f = v.GetComponent<LaneFollower>();
            Line("  " + t + ": gövde " + V(v.bodySize) + ", tekerlek " + f.wheels.Length + ", renk varyantı " + v.paintColors.Length
                + ", model " + (File.Exists(Bolum1TrafficBuilder.ModelPath(t)) ? Bolum1TrafficBuilder.ModelPath(t) : "yer tutucu"));
        }
    }

    // (i) motor, korna, koni çarpma klipleri sessiz değil; kaynaklar 3D + Doppler.
    private static void CheckAudioClips()
    {
        var parts = new List<string>();
        bool ok = true;
        var clips = new List<AudioClip>();
        foreach (var t in Bolum1TrafficBuilder.Types)
        {
            var a = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabPath(t)).GetComponent<VehicleAudio>();
            var engine = SynthClips.TrafficEngine(a.engineBaseHz);
            var horn = SynthClips.TrafficHorn(a.hornLowHz, a.hornHighHz);
            clips.Add(engine); clips.Add(horn);
            float re = SynthClips.Rms(engine), rh = SynthClips.Rms(horn);
            bool src = a.engine != null && a.horn != null && a.engine.spatialBlend == 1f && a.horn.spatialBlend == 1f && a.engine.dopplerLevel > 0f && a.horn.dopplerLevel > 0f;
            ok &= re > 0.01f && rh > 0.01f && src;
            parts.Add(t + " motor " + a.engineBaseHz.ToString("F0", Inv) + " Hz RMS " + re.ToString("F3", Inv) + ", korna " + a.hornLowHz.ToString("F0", Inv) + "/" + a.hornHighHz.ToString("F0", Inv)
                + " Hz RMS " + rh.ToString("F3", Inv) + ", kaynak 3D/Doppler " + src);
        }
        var cone = SynthClips.ConeHit();
        clips.Add(cone);
        float rc = SynthClips.Rms(cone);
        var coneSrc = Bolum1TowTruck.ConeNames.Select(n => GameObject.Find(Bolum1TrafficBuilder.ConeControllerName(n)).GetComponent<TrafficCone>().hitSource).ToList();
        bool coneOk = rc > 0.01f && coneSrc.All(s => s != null && s.spatialBlend == 1f && s.dopplerLevel > 0f);
        ok &= coneOk;
        parts.Add("koni çarpma RMS " + rc.ToString("F3", Inv) + ", koni kaynakları 3D/Doppler " + coneOk);
        Check("(i) ses: motor, korna, koni çarpma RMS > 0.01; kaynaklar spatialBlend 1, dopplerLevel > 0", ok, string.Join("; ", parts));
        foreach (var c in clips) UnityEngine.Object.DestroyImmediate(c);
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
        if (Time.realtimeSinceStartup - realStart > 1800f) { Fail("zaman aşımı (30 dk)"); End(); return; }

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
        Line(allOk ? "TRAFFIC VERIFY OK" : "TRAFFIC VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "traffic_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "TRAFFIC VERIFY OK" : "TRAFFIC VERIFY FAILED");
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
    private static ThirdPersonPlayer player;
    private static ThirdPersonPlayer Player { get { return player; } }
    private static PlayerInteraction Interaction { get { return player.GetComponent<PlayerInteraction>(); } }
    private static StretcherHelperBot Bot { get { return UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>(); } }
    private static StretcherController Stretcher(int i) { return GameObject.Find(Bolum1AmbulanceBuilder.StretcherNames[i]).GetComponent<StretcherController>(); }
    private static TrafficCone Cone(int i) { var go = GameObject.Find(Bolum1TrafficBuilder.ConeControllerName(Bolum1TowTruck.ConeNames[i])); return go != null ? go.GetComponent<TrafficCone>() : null; }

    // ---- her kare örneklenen ölçümler ----
    private static int overlapFrames;
    private static string overlapInfo = "";
    private static float minFollowGap = float.PositiveInfinity;
    private static string minFollowInfo = "";
    private static int sampledFrames;

    private static void Sample()
    {
        if (Traffic == null) return;
        sampledFrames++;
        // (c) iki aracın collider'ları kesişmiyor (trafik + ambulans, bütün çiftler)
        var boxes = new List<BoxCollider>();
        foreach (var v in Traffic.Vehicles) if (v != null) boxes.Add(v.GetComponent<BoxCollider>());
        if (Mission != null) foreach (var a in Mission.Ambulances) if (a != null) boxes.Add(a.GetComponent<BoxCollider>());
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i]; var b = boxes[j];
                if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float dist) && dist > 0.01f)
                {
                    overlapFrames++;
                    if (overlapInfo.Length == 0) overlapInfo = Describe(a) + " ↔ " + Describe(b) + " " + dist.ToString("F2", Inv) + " m (t=" + Time.time.ToString("F1", Inv) + ")";
                }
            }
        foreach (var v in Traffic.Vehicles)
        {
            if (v == null || v.BlockerKind != LaneOccupancy.BlockerKind.Vehicle) continue;
            if (v.GapToLeader < minFollowGap)
            {
                minFollowGap = v.GapToLeader;
                var leader = LaneOccupancy.Leader(v.Follower);
                minFollowInfo = v.name + " → " + (leader != null ? leader.name : "?") + " (hız " + v.Follower.Speed.ToString("F1", Inv) + ")";
            }
        }
        if (yieldWatch != null) SampleYield();
    }

    private static string Describe(BoxCollider c)
    {
        var f = c.GetComponent<LaneFollower>();
        if (f == null || f.lane == null) return c.name;
        return c.name + " [" + f.lane.name + " s " + f.Distance.ToString("F1", Inv) + "/" + f.lane.Length.ToString("F0", Inv) + ", v " + f.Speed.ToString("F1", Inv) + ", " + V(c.transform.position) + "]";
    }

    // (f) şeritteki oyuncu: izlenen aracın yavaşlaması
    private static TrafficVehicle yieldWatch;
    private static int yieldFrames, hornAtStart = -1;
    private static float minYieldSpeed = float.MaxValue;

    private static void SampleYield()
    {
        var v = yieldWatch;
        if (v == null || Player.IsDown) return;
        if (!v.YieldingToCarrier) return;
        yieldFrames++;
        minYieldSpeed = Mathf.Min(minYieldSpeed, v.Follower.Speed);
    }

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static Vector3 parkPosition;
    private static GameObject mateGo;

    private static IEnumerator Scenario()
    {
        yield return null;
        player = GameObject.Find(Bolum1SceneBuilder.CharacterName).GetComponent<ThirdPersonPlayer>();
        Player.GetComponent<PlayerInputReader>().enabled = false;
        // İkinci test taşıyıcısı (takım arkadaşı): oyuncunun ayakta kopyası, girdi okuyucusu kapalı; şimdilik pasif.
        mateGo = UnityEngine.Object.Instantiate(Player.gameObject);
        mateGo.name = "TestMate";
        mateGo.GetComponent<PlayerInputReader>().enabled = false;
        mateGo.SetActive(false);

        var mission = Mission;
        var traffic = Traffic;
        var tourist = mission.patients.First(p => p.name == "Patient_Tourist");
        parkPosition = tourist.Position + new Vector3(-2.5f, 0f, -1.8f);
        ParkCarriersOffLane();
        mission.missLimit = 999; // test: görev kaçırma yüzünden bitmesin (geçici)

        yield return PhaseSimulation();
        yield return PhaseDeterminism();
        yield return PhaseAmbulanceQueue();
        yield return PhaseCone();
        yield return PhaseConeCarry();
        yield return PhasePlayerHit();
        yield return PhaseStretcherHit();

        Check("(c) hiçbir karede iki aracın collider'ları kesişmiyor; en küçük takip mesafesi ≥ 3.8 m",
            overlapFrames == 0 && minFollowGap >= 3.8f,
            sampledFrames + " kare örneklendi, kesişen kare " + overlapFrames + (overlapInfo.Length > 0 ? " (ilk: " + overlapInfo + ")" : "")
            + "; en küçük takip mesafesi (ön tampon → öndeki arka tampon) " + M(minFollowGap) + " " + minFollowInfo);
    }

    private static TrafficSettings TestSettings(bool natural)
    {
        var s = UnityEngine.Object.Instantiate(Bolum1TrafficBuilder.Settings);
        s.useFixedSeed = true;
        s.seed = Seed;
        if (!natural)
        {
            s.calmSeconds = new Vector2(99999f, 99999f); // doğal trafik yok, yalnız testin çıkardığı araçlar
            s.calmSingleVehicleChance = 0f;
        }
        return s;
    }

    private static AmbulanceSchedule TestSchedule(float first, float interval)
    {
        var a = ScriptableObject.CreateInstance<AmbulanceSchedule>();
        a.firstArrival = first; a.interval = interval; a.wait = 15f;
        return a;
    }

    // ---- (b) + (h: zamanlama) 5 dk sabit seed simülasyonu ----
    private static readonly List<float> ambulanceSpawns = new List<float>();
    private static List<string> firstRunSignature;

    private static IEnumerator PhaseSimulation()
    {
        var mission = Mission;
        var traffic = Traffic;
        var schedule = TestSchedule(40f, 60f);
        var ts = TestSettings(true);
        ambulanceSpawns.Clear();
        mission.AmbulanceSpawned += a => ambulanceSpawns.Add(mission.Elapsed);
        var eastSpawnTimes = new List<float>();
        traffic.VehicleSpawned += v => { if (v.Follower.lane == traffic.eastBound) eastSpawnTimes.Add(mission.Elapsed); };
        mission.Begin(schedule);
        traffic.Begin(ts);
        Line("PLAY (b): sabit seed " + Seed + ", " + SimSeconds + " sn; ambulans takvimi ilk varış 40, aralık 60, bekleme 15 (geçici kopya), yolculuk " + mission.TravelTime.ToString("F2", Inv) + " sn");
        Time.timeScale = 6f;

        // Genel görüntü: iki şeritte de, kaza tarafındaki kolda birbirine yakın araç varken
        bool captured = false;
        yield return (Func<bool>)(() => traffic.Elapsed >= SimSeconds - 10f || BothLanesVisible(out _, out _));
        if (BothLanesVisible(out TrafficVehicle e, out TrafficVehicle w))
        {
            Time.timeScale = 1f;
            Vector3 mid = (e.transform.position + w.transform.position) * 0.5f;
            yield return Capture("trafik_genel", mid + new Vector3(0f, 16f, 26f), mid, 50f);
            captured = true;
            Time.timeScale = 6f;
        }
        yield return (Func<bool>)(() => traffic.Elapsed >= SimSeconds);
        Time.timeScale = 1f;

        // Dağılım
        var log = traffic.SpawnLog.ToList();
        var phases = traffic.PhaseLog.ToList();
        var parts = new List<string>();
        bool ok = true;
        foreach (bool east in new[] { true, false })
        {
            string dir = east ? "EastBound" : "WestBound";
            var calms = phases.Where(p => p.east == east && !p.wave).Select(p => p.planned).ToList();
            var waves = phases.Where(p => p.east == east && p.wave).ToList();
            var spawns = log.Where(r => r.east == east).ToList();
            // Dalgaları gerçekleşen spawn'lardan grupla: dalga başlangıcından sonraki inWave spawn'ları
            var waveCounts = new List<int>();
            var gaps = new List<float>();
            var measuredCalm = new List<float>();
            for (int i = 0; i < waves.Count; i++)
            {
                float start = waves[i].start, end = i + 1 < waves.Count ? waves[i + 1].start : float.PositiveInfinity;
                var inWave = spawns.Where(r => r.inWave && r.time >= start - 0.001f && r.time < end).Select(r => r.time).ToList();
                if (inWave.Count == 0) continue;
                if (i + 1 < waves.Count || inWave.Count == (int)waves[i].planned) waveCounts.Add(inWave.Count);
                for (int k = 1; k < inWave.Count; k++) gaps.Add(inWave[k] - inWave[k - 1]);
                // Ölçülen sakin: bu dalganın son aracından bir sonraki dalganın ilk aracına
                if (i + 1 < waves.Count)
                {
                    var next = spawns.Where(r => r.inWave && r.time >= waves[i + 1].start - 0.001f).Select(r => r.time).FirstOrDefault();
                    if (next > 0f) measuredCalm.Add(next - inWave.Last());
                }
            }
            // Sakin aralıktaki tek araçlar (en fazla 1)
            int calmSingles = spawns.Count(r => !r.inWave && !r.forced);
            var calmIntervals = phases.Where(p => p.east == east && !p.wave).ToList();
            int maxSinglesPerCalm = 0;
            foreach (var c in calmIntervals)
            {
                var next = phases.Where(p => p.east == east && p.wave && p.start >= c.start).Select(p => p.start).DefaultIfEmpty(float.PositiveInfinity).First();
                maxSinglesPerCalm = Mathf.Max(maxSinglesPerCalm, spawns.Count(r => !r.inWave && r.time >= c.start && r.time < next));
            }
            bool dirOk = waves.Count >= 2 && calms.All(c => c >= 20f - 0.01f && c <= 40f + 0.01f) && waves.All(wv => wv.planned >= 3 && wv.planned <= 4)
                && waveCounts.All(n => n >= 3 && n <= 4) && gaps.All(g => g >= 2f - 0.03f) && maxSinglesPerCalm <= 1;
            ok &= dirOk;
            parts.Add(dir + ": " + spawns.Count + " araç, " + waves.Count + " dalga (planlanan boy " + string.Join(",", waves.Select(x => x.planned.ToString("F0", Inv)))
                + "; gerçekleşen " + string.Join(",", waveCounts) + "), sakin planlanan " + Range(calms) + " sn, ölçülen (dalga sonu → sonraki dalga) " + Range(measuredCalm)
                + " sn, dalga içi aralık " + Range(gaps) + " sn, sakin aralıktaki tek araç " + calmSingles + " (aralık başına en çok " + maxSinglesPerCalm + ")"
                + ", tipler " + string.Join(" ", Bolum1TrafficBuilder.Types.Select(t => t + ":" + spawns.Count(r => r.type == t))));
        }
        var allTypes = Bolum1TrafficBuilder.Types.All(t => log.Any(r => r.type == t));
        var paints = log.Where(r => TrafficSettings.IsPassengerCar(r.type)).Select(r => r.paint).Distinct().Count();
        Check("(b) 5 dk sabit seed: her iki yönde dalgalar; sakin 20–40 sn, dalga 3–4 araç, aralık ≥ 2 sn (2–4 + yer bekleme), sakinde en çok 1 araç; 5 tip görüldü",
            ok && allTypes, string.Join(" | ", parts) + "; 5 tip " + allTypes + ", binek renk varyantı " + paints + "/4; genel görüntü " + (captured ? "alındı" : "ALINAMADI"));

        // (h) ambulans: doğmadan önceki 20 sn'de EastBound spawn yok; varışlar plana ±0.5 sn
        var violations = new List<string>();
        foreach (float a in ambulanceSpawns)
            foreach (float t in eastSpawnTimes)
                if (t >= a - ts.ambulanceClearSeconds - 0.001f && t < a - 0.001f) violations.Add(t.ToString("F2", Inv) + " (ambulans " + a.ToString("F2", Inv) + ")");
        var arrivals = mission.ArrivalLog.ToList();
        float worst = 0f;
        for (int k = 0; k < arrivals.Count; k++) worst = Mathf.Max(worst, Mathf.Abs(arrivals[k] - schedule.ArrivalTime(k)));
        float closest = float.PositiveInfinity;
        foreach (float a in ambulanceSpawns)
            foreach (float t in eastSpawnTimes)
                if (t < a) closest = Mathf.Min(closest, a - t);
        Check("(h1) ambulans doğmadan önceki 20 sn içinde EastBound spawn yok; varışlar trafikle planlanan zamana ±0.5 sn",
            violations.Count == 0 && arrivals.Count >= 4 && worst <= 0.5f,
            "ambulans doğuşları " + string.Join(", ", ambulanceSpawns.Select(x => x.ToString("F1", Inv))) + "; ihlal " + (violations.Count == 0 ? "yok" : string.Join(", ", violations))
            + "; doğuştan önceki en yakın EastBound spawn " + (float.IsPositiveInfinity(closest) ? "-" : closest.ToString("F2", Inv) + " sn önce")
            + "; varışlar " + string.Join(", ", arrivals.Select(x => x.ToString("F2", Inv))) + " (en büyük sapma " + worst.ToString("F3", Inv) + " sn)");

        firstRunSignature = Signature(log, 60f);
    }

    private static List<string> Signature(List<TrafficDirector.SpawnRecord> log, float until)
    {
        return log.Where(r => r.time < until).Select(r => (r.east ? "E" : "W") + r.time.ToString("F2", Inv) + r.type + r.paint).ToList();
    }

    // Aynı seed ile ilk 60 sn tekrar: aynı spawn dizisi
    private static IEnumerator PhaseDeterminism()
    {
        var mission = Mission;
        var traffic = Traffic;
        mission.Begin(TestSchedule(40f, 60f));
        traffic.Begin(TestSettings(true));
        Time.timeScale = 6f;
        yield return (Func<bool>)(() => traffic.Elapsed >= 62f);
        Time.timeScale = 1f;
        var second = Signature(traffic.SpawnLog.ToList(), 60f);
        bool same = second.SequenceEqual(firstRunSignature);
        Check("(b2) deterministik test modu: aynı seed ile ilk 60 sn'nin spawn dizisi aynı", same && second.Count > 0,
            "1. koşu " + firstRunSignature.Count + " spawn, 2. koşu " + second.Count + " spawn; ilk 4: " + string.Join(" ", second.Take(4)));
        traffic.ClearVehicles();
    }

    private static bool BothLanesVisible(out TrafficVehicle east, out TrafficVehicle west)
    {
        east = west = null;
        var traffic = Traffic;
        foreach (var e in traffic.Vehicles)
        {
            if (e == null || e.Follower.lane != traffic.eastBound || e.transform.position.z < -8f) continue;
            foreach (var w in traffic.Vehicles)
            {
                if (w == null || w.Follower.lane != traffic.westBound || w.transform.position.z < -8f) continue;
                if (Vector3.Distance(e.transform.position, w.transform.position) < 30f) { east = e; west = w; return true; }
            }
        }
        return false;
    }

    // ---- (h) duraktaki ambulansın arkasında kuyruk ----
    private static IEnumerator PhaseAmbulanceQueue()
    {
        var mission = Mission;
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        var schedule = TestSchedule(25f, 400f);
        mission.Begin(schedule);
        Time.timeScale = 3f;
        yield return Until(() => mission.Ambulances.Count > 0, 40f, "ambulans doğdu");
        Time.timeScale = 1f;
        var amb = mission.Ambulances[0];
        // Ambulansın arkasından üç araç (ambulans doğduktan sonra; yer açıldıkça)
        var queue = new List<TrafficVehicle>();
        foreach (var t in new[] { TrafficVehicleType.Sedan, TrafficVehicleType.BoxTruck, TrafficVehicleType.PanelVan })
        {
            TrafficVehicle v = null;
            yield return Until(() => v != null || (v = traffic.ForceSpawn(true, t)) != null, 15f, "kuyruk aracı spawn " + t);
            queue.Add(v);
        }
        yield return Until(() => amb.Phase == AmbulancePhase.Waiting, 30f, "ambulans durakta");
        float arrival = mission.ArrivalLog.Count > 0 ? mission.ArrivalLog[0] : -1f;
        // Kuyruk oturur: hepsi durdu
        yield return Until(() => queue.All(v => v != null && v.Follower.Speed < 0.01f && v.BlockerKind == LaneOccupancy.BlockerKind.Vehicle), Mathf.Max(1f, amb.WaitRemaining - 0.5f), "kuyruk durdu");
        var lane = traffic.eastBound;
        var leader0 = LaneOccupancy.Leader(queue[0].Follower);
        float gapToAmb = queue[0].GapToLeader;
        string gaps = string.Join(", ", queue.Select(v => v.name + " " + M(v.GapToLeader)));
        float rearS = amb.Follower.RearS;
        Vector3 look = lane.PointAt(rearS - 10f);
        yield return Capture("trafik_ambulans_kuyruk", lane.PointAt(rearS + 4f) + lane.RightAt(rearS) * 11f + Vector3.up * 7f, look + Vector3.up * 1f, 55f);
        var queued = queue.Select(v => v.Follower.Distance).ToList();
        yield return Until(() => amb == null || amb.Phase == AmbulancePhase.Departing, 20f, "ambulans kalkış");
        float departed = Time.time;
        yield return Until(() => queue.Select((v, i) => v == null || v.Follower.Distance > queued[i] + 5f).All(x => x), 15f, "kuyruk çözüldü");
        float resolve = Time.time - departed;
        Check("(h2) ambulans duraktayken EastBound kuyruğu ambulansa girmiyor ve ambulans gidince çözülüyor",
            leader0 == amb.Follower && gapToAmb >= 3.8f && arrival >= 0f && Mathf.Abs(arrival - schedule.firstArrival) <= 0.5f,
            "ilk aracın lideri " + (leader0 != null ? leader0.name : "-") + ", boşluklar " + gaps + "; ambulans varışı " + S(arrival) + " (plan " + schedule.firstArrival
            + "); kalkıştan " + S(resolve) + " sonra son araç da ≥ 5 m ilerledi");
        Time.timeScale = 3f;
        yield return Until(() => queue.All(v => v == null), 60f, "kuyruk araçları şeridin sonunda kayboldu");
        Time.timeScale = 1f;
    }

    // ---- (d) koni şeridi kapatıyor; ~20 sn sonra çarpma; devrik koni kapatmıyor ----
    private static IEnumerator PhaseCone()
    {
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        var lane = traffic.westBound;
        var settings = traffic.Active;
        var cone = Cone(0);
        float coneS = lane.Length * 0.5f;
        PlaceUpright(cone, lane.PointAt(coneS));
        yield return Seconds(0.5f);
        bool closes = cone.ClosesLane(lane, settings.coneUprightMaxAngle, out float closedS);
        Vector3 coneStart = cone.BasePoint;

        var queue = new List<TrafficVehicle>();
        float stopTime = -1f, ramWait = -1f, ramEventTime = -1f;
        foreach (var t in new[] { TrafficVehicleType.Sedan, TrafficVehicleType.Pickup, TrafficVehicleType.BoxTruck })
        {
            TrafficVehicle v = null;
            yield return Until(() => v != null || (v = traffic.ForceSpawn(false, t)) != null, 15f, "koni kuyruğu spawn " + t);
            queue.Add(v);
            if (queue.Count == 1) v.ConeRammed += (x, c, waited) => { ramWait = waited; ramEventTime = Time.time; };
            yield return Seconds(2.5f);
        }
        var first = queue[0];
        Time.timeScale = 3f;
        yield return Until(() => first.WaitingCone == cone, 40f, "ilk araç koninin önünde durdu");
        stopTime = Time.time;
        float frontToCone = (closedS - cone.radius) - first.Follower.FrontS;
        Time.timeScale = 1f;
        yield return Until(() => queue.All(v => v.Follower.Speed < 0.01f), 20f, "koni kuyruğu durdu");
        Vector3 cs = lane.PointAt(coneS);
        yield return Capture("trafik_koni", lane.PointAt(coneS + 7f) - lane.RightAt(coneS) * 7f + Vector3.up * 5f, lane.PointAt(coneS - 9f) + Vector3.up * 0.8f, 55f);
        Time.timeScale = 4f;
        yield return Until(() => cone == null || cone.VehicleHits > 0, 40f, "araç koniye çarptı");
        float hitTime = cone != null ? cone.LastHitTime : Time.time;
        float hitDelay = hitTime - stopTime;
        float sAtHit = first.Follower.Distance;
        Time.timeScale = 1f;
        yield return Seconds(3f);
        float coneMoved = cone == null ? float.PositiveInfinity : Vector3.Distance(cone.BasePoint, coneStart);
        string coneState = cone == null ? "uçurumdan düşüp yok oldu" : "eğim " + cone.TiltAngle.ToString("F0", Inv) + "°, yolda " + cone.OnRoad();
        bool continued = first == null || first.Follower.Distance > sAtHit + 5f;
        Check("(d1) şeride konan dik koni aracı durduruyor; araç ~20 ± 1 sn sonra koniye çarpıp devam ediyor, koni ≥ 1 m savruluyor",
            closes && hitDelay >= settings.coneWaitSeconds - 1f && hitDelay <= settings.coneWaitSeconds + 1f && coneMoved >= 1f && continued && ramWait > 0f,
            "koni şeridi kapatıyor " + closes + " (s " + closedS.ToString("F1", Inv) + "); ön tampon koniye " + M(frontToCone) + " kala durdu; duruştan çarpmaya " + S(hitDelay)
            + " (bekleme sayacı " + S(ramWait) + "); koni " + (float.IsPositiveInfinity(coneMoved) ? "∞" : M(coneMoved)) + " savruldu (" + coneState + "); araç devam etti " + continued);
        Time.timeScale = 4f;
        yield return Until(() => queue.All(v => v == null), 60f, "koni kuyruğu kayboldu");
        Time.timeScale = 1f;

        // Devrik koni: şeridi kapatmaz, araç durmadan geçer
        var tipped = Cone(1);
        float tS = lane.Length * 0.45f;
        PlaceTipped(tipped, lane, tS);
        yield return Seconds(0.6f);
        bool tippedCloses = tipped.ClosesLane(lane, settings.coneUprightMaxAngle, out _);
        float tilt = tipped.TiltAngle;
        TrafficVehicle pass = null;
        yield return Until(() => pass != null || (pass = traffic.ForceSpawn(false, TrafficVehicleType.Hatchback)) != null, 10f, "devrik koni aracı");
        float minSpeed = float.MaxValue;
        bool sawCone = false;
        yield return (Func<bool>)(() =>
        {
            if (pass == null) return true;
            minSpeed = Mathf.Min(minSpeed, pass.Follower.Speed);
            sawCone |= pass.BlockerKind == LaneOccupancy.BlockerKind.Cone;
            return pass.Follower.FrontS > tS + 10f;
        });
        Check("(d2) devrik koni şeridi kapatmıyor", !tippedCloses && !sawCone && minSpeed > 1f,
            "eğim " + tilt.ToString("F0", Inv) + "°, kapatıyor " + tippedCloses + "; geçen aracın en düşük hızı " + minSpeed.ToString("F2", Inv) + " m/sn, koniyi engel gördü " + sawCone);
        Time.timeScale = 4f;
        yield return Until(() => pass == null, 40f, "devrik koni aracı kayboldu");
        Time.timeScale = 1f;
    }

    private static void PlaceUpright(TrafficCone cone, Vector3 ground)
    {
        cone.PlaceAt(ground, 0f);
    }

    private static void PlaceTipped(TrafficCone cone, LanePath lane, float s)
    {
        Vector3 p = lane.PointAt(s);
        Quaternion rot = Quaternion.FromToRotation(cone.localUp.normalized, lane.TangentAt(s));
        cone.PlaceWithPose(p + Vector3.up * (cone.radius + 0.02f) - lane.TangentAt(s) * cone.height * 0.4f, rot);
    }

    // ---- (e) koni E ile alınıp konuyor; elde 1 koni; koni–sedye kısıtı ----
    private static IEnumerator PhaseConeCarry()
    {
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        var lane = traffic.eastBound;
        float s0 = lane.Length * 0.3f;
        Vector3 basePt = lane.PointAt(s0);
        Vector3 fwd = Flat(lane.TangentAt(s0)).normalized;
        var c3 = Cone(2);
        var c4 = Cone(3);
        var bot = Bot;
        bot.assistEnabled = false;
        ParkBot();

        // a) koniyi al
        PlaceUpright(c3, basePt + fwd * 0.9f);
        PlaceUpright(c4, basePt + fwd * 6f);
        Player.Teleport(basePt + Vector3.up * 0.02f, Quaternion.LookRotation(fwd));
        yield return null; yield return null;
        string hintPick = Interaction.CurrentHint;
        Interaction.Interact();
        yield return null;
        bool picked = Player.HeldCone == c3 && c3.IsHeld;

        // b) yanında başka koni varken: ipucu "koy", E → elindekini koyar, ikinciyi almaz
        Player.Teleport(basePt + fwd * 5f + Vector3.up * 0.02f, Quaternion.LookRotation(-Vector3.Cross(Vector3.up, fwd)));
        yield return null; yield return null;
        string hintWithSecond = Interaction.CurrentHint;
        Vector3 expected = Player.transform.position + Player.transform.forward * Interaction.conePlaceDistance;
        Interaction.Interact();
        yield return Seconds(0.5f);
        bool placedOne = Player.HeldCone == null && !c3.IsHeld && !c4.IsHeld;
        float placeErr = Vector3.Distance(Flat(c3.BasePoint), Flat(expected));
        float placeTilt = c3.TiltAngle;
        bool onGround = GroundProbe.Find(c3.BasePoint + Vector3.up * 0.5f, 1f, out RaycastHit g) && Mathf.Abs(g.point.y - c3.BasePoint.y) < 0.05f;
        Check("(e1) koni E ile alınıyor ve baktığı yere dik konuyor; elde 1 koni (ikinci koni alınmıyor)",
            hintPick == PlayerInteraction.HintPickCone && picked && hintWithSecond == PlayerInteraction.HintPlaceCone && placedOne && placeErr < 0.15f && placeTilt < 3f && onGround,
            "ipucu \"" + hintPick + "\", alındı " + picked + "; ikinci koni yanında ipucu \"" + hintWithSecond + "\", E sonrası elde " + (Player.HeldCone != null ? Player.HeldCone.name : "yok")
            + "; konan koni hedefe " + M(placeErr) + ", eğim " + placeTilt.ToString("F1", Inv) + "°, zeminde " + onGround);

        // c) koni tutarken sedye tutulamaz
        var s1 = Stretcher(0);
        Player.Teleport(c3.BasePoint + fwd * 0.6f + Vector3.up * 0.02f, Quaternion.LookRotation(-fwd));
        yield return null; yield return null;
        Interaction.Interact();
        yield return null;
        bool holdingCone = Player.HeldCone == c3;
        Vector3 stretcherAt = basePt - fwd * 6f;
        s1.Teleport(stretcherAt, Quaternion.LookRotation(fwd));
        yield return Seconds(0.3f);
        TeleportPlayerToHandle(s1, true);
        Player.Teleport(Player.transform.position, Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd))); // yana bak: koni sedyenin üstüne konmasın
        yield return null; yield return null;
        string hintConeAtHandle = Interaction.CurrentHint;
        Interaction.Interact();
        yield return Seconds(0.3f);
        bool noGrabWithCone = Player.HeldStretcher == null && s1.CarrierCount == 0 && Player.HeldCone == null;

        // d) sedye tutarken koni alınamaz
        TeleportPlayerToHandle(s1, true);
        yield return null; yield return null;
        Interaction.Interact();
        yield return Seconds(0.3f);
        bool holdingStretcher = Player.HeldStretcher == s1;
        PlaceUpright(c4, Player.transform.position + Player.transform.forward * 0.7f);
        yield return Seconds(0.3f);
        string hintStretcherAtCone = Interaction.CurrentHint;
        Interaction.Interact();
        yield return Seconds(0.3f);
        bool noConeWithStretcher = Player.HeldCone == null && !c4.IsHeld && Player.HeldStretcher == null;
        Check("(e2) koni tutan sedye tutamıyor, sedye tutan koni alamıyor",
            holdingCone && hintConeAtHandle == PlayerInteraction.HintPlaceCone && noGrabWithCone && holdingStretcher && hintStretcherAtCone == PlayerInteraction.HintRelease && noConeWithStretcher,
            "koni elde, sap yanında ipucu \"" + hintConeAtHandle + "\" → E: sedye tutuldu mu " + !noGrabWithCone + "; sedye elde, koni yanında ipucu \"" + hintStretcherAtCone + "\" → E: koni alındı mı " + !noConeWithStretcher);
        // Koniler ve sedye şeritten kaldırılır
        foreach (var c in new[] { c3, c4 }) if (c != null) PlaceUpright(c, parkPosition + new Vector3(2f + Array.IndexOf(new[] { c3, c4 }, c) * 0.8f, 0f, 0f));
        yield return null;
    }

    // ---- (f) şeritteki oyuncu: korna, ≤ %40 hız; temasla ragdoll; arkadaş / bot kaldırıyor; kimse yoksa yerde ----
    private static IEnumerator PhasePlayerHit()
    {
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        var lane = traffic.eastBound;
        var bot = Bot;
        var kd = Player.knockdown;

        // f1: bot kapalı, şeritte duran oyuncu
        bot.assistEnabled = false;
        ParkBot();
        float s0 = lane.Length * 0.55f;
        yield return HitPlayerAt(lane, s0, TrafficVehicleType.Sedan, true);
        var hitCar = lastHitCar;
        bool ragdoll = Player.IsDown && kd.ragdollBodies.All(b => !b.isKinematic) && !Player.GetComponent<CharacterController>().enabled;
        int horns = hitCar != null ? hitCar.sound.HornPlays - hornAtStart : -1;
        float cruise = hitCar != null ? hitCar.Follower.cruiseSpeed : 12f;
        // Çarpan araç (yavaşlayarak) uzaklaşınca yerdeki ragdoll'un görüntüsü
        yield return Until(() => hitCar == null || Vector3.Distance(hitCar.transform.position, kd.GroundPosition) > 20f, 20f, "çarpan araç uzaklaştı");
        Vector3 hips = kd.GroundPosition;
        Vector3 t = Flat(lane.TangentAt(s0)).normalized;
        yield return Capture("trafik_dusme", hips - t * 3.5f + lane.RightAt(s0) * 2.5f + Vector3.up * 3.2f, hips, 50f);
        Check("(f1) şeritteki oyuncu: korna, hız ≤ %40 ve > 0; temasla oyuncu Ragdoll",
            horns >= 1 && yieldFrames > 0 && minYieldSpeed <= 0.4f * cruise + 0.05f && minYieldSpeed > 0f && ragdoll,
            "korna " + horns + " kez; yavaşlama " + yieldFrames + " kare, en düşük hız " + minYieldSpeed.ToString("F2", Inv) + " m/sn (sınır " + (0.4f * cruise).ToString("F2", Inv)
            + "); temas: düştü " + Player.IsDown + ", ragdoll gövdeleri dinamik " + ragdoll + "; kalça " + V(hips));
        yield return Seconds(1.0f);

        // f2: arkadaş (ikinci test taşıyıcısı) E basılı 2 sn. Önce çarpan araç uzaklaşsın (yavaşlayarak düşen oyuncunun yanından geçer).
        yield return Until(() => hitCar == null || Vector3.Distance(hitCar.transform.position, kd.GroundPosition) > 20f, 20f, "çarpan araç uzaklaştı");
        mateGo.SetActive(true);
        var mate = mateGo.GetComponent<ThirdPersonPlayer>();
        var mateI = mateGo.GetComponent<PlayerInteraction>();
        hips = kd.GroundPosition;
        Vector3 mp = hips + Vector3.Cross(Vector3.up, t) * 1.0f;
        if (GroundProbe.Find(mp + Vector3.up * 1.5f, 5f, out RaycastHit g)) mp.y = g.point.y + 0.02f;
        mate.Teleport(mp, Quaternion.LookRotation(Flat(hips - mp).normalized));
        yield return null; yield return null;
        string mateHint = mateI.CurrentHint;
        mateI.SetInteractHeld(true);
        float holdStart = Time.time;
        yield return Seconds(1.0f);
        float progressAt1 = kd.ReviveProgress;
        var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        yield return (Func<bool>)(() => !Player.IsDown || Time.time > holdStart + 4f);
        float mateTime = Player.IsDown ? -1f : Time.time - holdStart;
        string mateDiag = "arkadaş–kalça " + M(Vector3.Distance(Flat(mate.transform.position), Flat(kd.GroundPosition))) + ", dy " + (mate.transform.position.y - kd.GroundPosition.y).ToString("F2", Inv)
            + ", arkadaş ipucu şimdi \"" + mateI.CurrentHint + "\", kontrol " + mate.HasControl + ", ilerleme %" + (kd.ReviveProgress * 100f).ToString("F0", Inv);
        mateI.SetInteractHeld(false);
        yield return Seconds(0.3f);
        // Kalkış animasyonu (GetUp_FromBack) bitince kontrol geri gelir
        float waitControl = Time.time;
        yield return (Func<bool>)(() => Player.HasControl || Time.time - waitControl > 2.5f);
        bool stoodUp = !Player.IsDown && Player.HasControl && Player.GetComponent<CharacterController>().enabled && kd.ragdollBodies.All(b => b.isKinematic);
        mateGo.SetActive(false);
        Check("(f2) arkadaş (ikinci test taşıyıcısı) E basılı 2 sn ile kaldırıyor; oyuncu ayağa kalkıp kontrolü geri alıyor",
            mateHint == PlayerInteraction.HintRevive && Mathf.Abs(mateTime - kd.reviveSeconds) <= 0.25f && progressAt1 > 0.4f && progressAt1 < 0.6f && stoodUp,
            "arkadaş ipucu \"" + mateHint + "\"; 1 sn'de ilerleme %" + (progressAt1 * 100f).ToString("F0", Inv) + "; kalkma " + S(mateTime) + " (beklenen " + kd.reviveSeconds + "); ayakta, kontrol " + stoodUp + (mateTime < 0f ? "; " + mateDiag : ""));
        if (Player.IsDown) { bot.assistEnabled = true; yield return Until(() => !Player.IsDown, 8f, "toparlama"); }
        yield return Seconds(0.5f);

        // f3: bot açık → 3 sn sonra yanına ışınlanır, 2 sn'de kaldırır
        bot.assistEnabled = true;
        yield return HitPlayerAt(lane, s0 - 15f, TrafficVehicleType.Pickup, false);
        float downAt = kd.DownSince;
        Vector3 botBefore = bot.transform.position;
        yield return Until(() => kd.ReviveProgress > 0f || !Player.IsDown, 6f, "bot kaldırmaya başladı");
        float botStart = Time.time - downAt;
        float botDist = Vector3.Distance(Flat(bot.transform.position), Flat(kd.GroundPosition));
        yield return Until(() => !Player.IsDown, 6f, "bot kaldırdı");
        float botTotal = Time.time - downAt;
        Check("(f3) bot 3 sn sonra yanına ışınlanıp 2 sn'de kaldırıyor (toplam ~5 sn)",
            Mathf.Abs(botStart - bot.reviveDelay) <= 0.15f && Mathf.Abs(botTotal - (bot.reviveDelay + kd.reviveSeconds)) <= 0.25f && botDist <= kd.reviveRange,
            "kaldırma başlangıcı düşmeden " + S(botStart) + " sonra, bot kalçaya " + M(botDist) + "; kalkma düşmeden " + S(botTotal) + " sonra");
        yield return Seconds(0.5f);

        // f4: bot kapalı, kimse yok → 15 sn sonra hâlâ yerde
        bot.assistEnabled = false;
        ParkBot();
        yield return HitPlayerAt(lane, s0 - 30f, TrafficVehicleType.PanelVan, false);
        float down4 = Time.time;
        Time.timeScale = 3f;
        yield return Seconds(15f);
        Time.timeScale = 1f;
        bool stillDown = Player.IsDown && Time.time - down4 >= 15f;
        Check("(f4) kimse kaldırmazsa (bot kapalı) 15 sn sonra hâlâ yerde", stillDown, "15 sn sonra yerde " + Player.IsDown + ", ilerleme %" + (kd.ReviveProgress * 100f).ToString("F0", Inv));
        // Toparla: bot açılır, kaldırır
        bot.assistEnabled = true;
        yield return Until(() => !Player.IsDown, 8f, "toparlama: bot kaldırdı");
        Time.timeScale = 4f;
        yield return Until(() => traffic.Vehicles.Count == 0, 60f, "(f) araçları kayboldu");
        Time.timeScale = 1f;
    }

    private static TrafficVehicle lastHitCar;

    // Oyuncuyu şeritte s noktasına (gelen araca bakarak) koyar, EastBound'a araç çıkarır, düşmeyi bekler.
    private static IEnumerator HitPlayerAt(LanePath lane, float s, TrafficVehicleType type, bool watchYield)
    {
        // Şerit ortasının 0.4 m solunda (dağ tarafı): çarpma itkisi uçuruma değil karşı şeride doğru olsun
        Vector3 p = lane.PointAt(s) - lane.RightAt(s) * 0.4f + Vector3.up * 0.05f;
        Player.Teleport(p, Quaternion.LookRotation(-Flat(lane.TangentAt(s)).normalized));
        yield return Seconds(0.3f);
        TrafficVehicle v = null;
        yield return Until(() => v != null || (v = Traffic.ForceSpawn(true, type)) != null, 10f, "çarpacak araç " + type);
        lastHitCar = v;
        if (watchYield)
        {
            yieldWatch = v;
            yieldFrames = 0;
            minYieldSpeed = float.MaxValue;
            hornAtStart = v.sound.HornPlays;
        }
        Time.timeScale = 2f;
        yield return Until(() => Player.IsDown, 60f, "oyuncu düştü (" + type + ")");
        Time.timeScale = 1f;
        yieldWatch = null;
    }

    // ---- (g) sedye taşırken çarpılan taşıyıcı sedyeyi bırakıyor, hasta ragdoll ve savruluyor ----
    private static IEnumerator PhaseStretcherHit()
    {
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        var lane = traffic.eastBound;
        var bot = Bot;
        bot.assistEnabled = true;
        var tourist = Mission.patients.First(p => p.name == "Patient_Tourist");
        var s1 = Stretcher(0);
        yield return PutPatientOnStretcher(tourist, s1);
        // Sedye şeritte, boylamasına; oyuncu gelen araç tarafındaki (arka) sapta, bot öndeki sapta
        float s0 = lane.Length * 0.5f;
        Vector3 t = Flat(lane.TangentAt(s0)).normalized;
        Vector3 center = lane.PointAt(s0 + 1.3f);
        Vector3 anchorLocal = s1.transform.InverseTransformPoint(s1.patientAnchor.position);
        s1.Teleport(center, Quaternion.LookRotation(t));
        yield return Seconds(0.3f);
        TeleportPlayerToHandle(s1, false);
        yield return null; yield return null;
        Interaction.Interact();
        yield return Seconds(0.5f);
        bool raised = s1.IsRaised && Player.HeldStretcher == s1 && bot.HeldStretcher == s1 && s1.Passenger == tourist;
        if (!raised) throw new Exception("(g) sedye kalkmadı: " + s1.CurrentMode + ", oyuncu " + (Player.HeldStretcher != null) + ", bot " + (bot.HeldStretcher != null));

        Vector3 hitPos = Vector3.zero;
        float hitTime = -1f;
        bool releasedAtHit = false;
        TrafficVehicle v = null;
        yield return Until(() => v != null || (v = traffic.ForceSpawn(true, TrafficVehicleType.Sedan)) != null, 10f, "sedye aracı");
        v.HitPatient += (car, p) => { if (p == tourist && hitTime < 0f) { hitPos = p.Position; hitTime = Time.time; } };
        v.HitPlayer += (car, p) => { releasedAtHit = p.HeldStretcher == null && !s1.IsHeldBy(p); };
        Time.timeScale = 2f;
        yield return Until(() => Player.IsDown, 60f, "taşıyıcı çarpıldı");
        Time.timeScale = 1f;
        bool released = Player.HeldStretcher == null && !s1.IsHeldBy(Player);
        yield return Until(() => hitTime > 0f, 6f, "araç hastaya çarptı");
        bool patientRagdoll = tourist.State == PatientState.Ragdoll && s1.Passenger == null;
        yield return Seconds(1.5f);
        float flung = Vector3.Distance(tourist.Position, hitPos);
        Check("(g) sedye taşırken çarpılan taşıyıcı sedyeyi bırakıyor; hasta Ragdoll ve itkiyle ≥ 0.5 m savruluyor",
            released && releasedAtHit && patientRagdoll && flung >= 0.5f,
            "taşıyıcı düştü " + Player.IsDown + ", sedyeyi bıraktı " + released + " (çarpma anında " + releasedAtHit + "); hasta " + tourist.State + ", 1.5 sn'de " + M(flung) + " savruldu");
        // Şef kararı 6: bot da çarpılıp düşer (3 sn), GetUp_FromBack ile kalkar (1.6 sn), sonra oyuncuyu kaldırır (2 sn)
        var botG = Bot;
        yield return Until(() => !Player.IsDown, 15f, "bot oyuncuyu kaldırdı (bot yerde " + botG.IsDown + ", düşme " + botG.TimesDowned + ", kalkma " + botG.TimesStoodUp + ")");
    }

    // ---------- yardımcılar (Bolum1AmbulanceVerify ile aynı) ----------
    private static IEnumerator PutPatientOnStretcher(Patient p, StretcherController s)
    {
        // Kalkış animasyonu (GetUp_FromBack) sürüyorsa kontrol dönene kadar bekle
        float waitControl = Time.time;
        yield return (Func<bool>)(() => Player.HasControl || Time.time - waitControl > 3f);
        if (Player.HeldStretcher != null) { Interaction.Interact(); yield return Seconds(0.5f); }
        Vector3 hips = p.Position;
        Vector3 side = Vector3.back;
        Vector3 at = hips + side * 0.8f;
        if (GroundProbe.Find(new Vector3(at.x, hips.y + 1.5f, at.z), 5f, out RaycastHit g)) at.y = g.point.y;
        Vector3 anchorLocal = s.transform.InverseTransformPoint(s.patientAnchor.position);
        Quaternion rot = Quaternion.LookRotation(Vector3.right, Vector3.up);
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
            throw new Exception(p.name + " sedyeye alınamadı (ipucu \"" + hint + "\")");
    }

    private static void TeleportPlayerToHandle(StretcherController s, bool front)
    {
        Transform h = front ? s.handleFront : s.handleRear;
        Vector3 outward = Flat(h.position - s.transform.position).normalized;
        Vector3 p = h.position + outward * 0.55f;
        if (GroundProbe.Find(new Vector3(p.x, h.position.y + 1.5f, p.z), 5f, out RaycastHit g)) p.y = g.point.y + 0.02f;
        Player.Teleport(p, Quaternion.LookRotation(-outward));
    }

    private static void ParkCarriersOffLane()
    {
        if (Player.HeldStretcher != null) Interaction.Interact();
        Vector3 pp = parkPosition;
        if (GroundProbe.Find(pp + Vector3.up * 1.5f, 5f, out RaycastHit g)) pp.y = g.point.y + 0.02f;
        Player.Teleport(pp, Quaternion.LookRotation(Vector3.right));
        ParkBot();
    }

    private static void ParkBot()
    {
        var bot = Bot;
        if (bot.HeldStretcher != null) bot.ReleaseHeld();
        Vector3 bp = parkPosition + new Vector3(-1.2f, 0f, 0f);
        if (GroundProbe.Find(bp + Vector3.up * 1.5f, 5f, out RaycastHit g)) bp.y = g.point.y;
        bot.transform.position = bp;
    }

    private static IEnumerator Capture(string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("TrafficVerify_CaptureCam");
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = main != null ? main.backgroundColor : Color.gray;
        cam.farClipPlane = 2000f;
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = fov;
        go.transform.position = pos;
        go.transform.LookAt(look);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        // GPU Resident Drawer yeni kameranın görünürlük verisini bir sonraki render döngüsünde hazırlar.
        yield return null;
        yield return null;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
        Line("  görüntü: " + OutDir + "/" + name + ".png");
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
        Check("(a) log'da C# derleme hatası yok", clean, note);
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
    private static string M(float m) { return m.ToString("F2", Inv) + " m"; }
    private static string S(float s) { return s.ToString("F2", Inv) + " sn"; }
    private static string Range(List<float> xs) { return xs.Count == 0 ? "-" : xs.Min().ToString("F1", Inv) + "–" + xs.Max().ToString("F1", Inv) + " (ort " + xs.Average().ToString("F1", Inv) + ", n " + xs.Count + ")"; }
}
