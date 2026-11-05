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
//   kamyonet şeritte park eder (döngü yok), hırsız iner, kalkar, kaybolur. (C3) gerçek duvar: minibüs fırlatılmaz. (C4) dışa dönük,
//   kenarda minibüs: düşmez. (D) minibüs şeritte: bekle-korna-çarp, ücretli. (E/E2) arkada araç: gövdeler kesişmez. (P1/P2) Codex review.
//   Görüntüler: Captures/Bolum1/thiefvan_*.png
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
        Application.targetFrameRate = 60; // kare başı ölçümler oyun hızında
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

        Line("İyi parkta minibüs: " + MinibusPose());
        Line("ZEMİN (yanal 3/5/7/9/11/13, durağa göre s): " + GroundProfile());
        yield return PhaseGoodPark();
        yield return PhaseBadPark();
        yield return PhaseBlocked();
        yield return PhaseRealWall();
        yield return PhaseEdge();
        yield return PhaseLaneBlock();
        yield return PhaseTrafficBehind();
        yield return PhaseReviewFindings();
    }

    private static string GroundProfile()
    {
        var sb = new StringBuilder();
        for (float ds = -25f; ds <= 20f; ds += 5f)
        {
            float s = Dir.StopS + ds;
            sb.Append("[" + ds.ToString("F0", Inv) + ":");
            foreach (float lat in new[] { 3f, 5f, 7f, 9f, 11f, 13f })
            {
                Vector3 p = east.PointAt(s) + east.RightAt(s) * lat;
                sb.Append(" " + (GroundProbe.Find(p + Vector3.up * 3f, 8f, out RaycastHit g) ? g.point.y.ToString("F1", Inv) : "-"));
            }
            sb.Append("] ");
        }
        return sb.ToString();
    }

    // Kamyonet gerçekten çizilebilir mi: etkin renderer'lar, geçerli malzeme / shader, gövde boyutunda sınırlar
    private static bool VanVisible(ThiefVan van, out string info)
    {
        info = "kamyonet yok";
        if (van == null) return false;
        var rs = van.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)).ToArray();
        bool mats = rs.Length > 0 && rs.All(r => r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader"));
        Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(van.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        bool near = b.Contains(van.transform.position + Vector3.up * 0.9f);
        info = rs.Length + " renderer, malzeme " + mats + ", sınır " + b.size.ToString("F1") + ", kökte " + near;
        return rs.Length >= 3 && mats && near && b.size.magnitude > 5f;
    }

    // Görüntü: HUD kanvası geçici kameraya bağlanır; bu kamera Camera.main olur ki minibüsün üstündeki uçan yazı doğru yere düşsün
    private static IEnumerator Shot(string name, Vector3 pos, Vector3 look, float fov = 45f)
    {
        var main = Camera.main;
        var go = new GameObject("ThiefVanVerify_Cam");
        var cam = go.AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        go.tag = "MainCamera";
        if (main != null) main.enabled = false;
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000f;
        go.transform.position = pos;
        go.transform.LookAt(look);
        Canvas canvas = Hud != null ? Hud.ScreenCanvas : null;
        if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f; }
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        yield return null;
        go.transform.position = pos;
        go.transform.LookAt(look);
        Canvas.ForceUpdateCanvases();
        cam.Render();
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
        if (main != null) main.enabled = true;
        Line("görüntü: " + OutDir + "/" + name + ".png");
    }

    // Kamyoneti yol tarafından (karşı şerit üstünden) gören kamera
    private static Vector3 RoadCam(Vector3 at, float back, float side, float up)
    {
        float s = east.Project(at);
        return at - Flat(east.TangentAt(s)).normalized * back - Flat(east.RightAt(s)).normalized * side + Vector3.up * up;
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
        public float minSpeed, maxSteer, spinStart = -1f, spinChange, minibusMoved, maxOpposingIntrusion = -99f, maxMinibusSpeed;
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
                if (spinStart < 0f) spinStart = spin;
                spinChange = v.Follower.ExternalWheelSpinTotal;
                // Karşı şerit: gövde köşesinin karşı şeridin (kendi şerit genişliği) içine en çok girişi (m; < 0 ise girmedi)
                var opposing = Traffic.westBound == v.Follower.lane ? Traffic.eastBound : Traffic.westBound;
                float halfW = v.Vehicle.bodySize.x * 0.5f;
                foreach (float z in new[] { -v.Follower.rearOverhang, v.Follower.frontOverhang })
                    foreach (float x in new[] { -halfW, halfW })
                    {
                        opposing.Project(v.transform.TransformPoint(new Vector3(x, 0.6f, z)), out float lat, out _);
                        maxOpposingIntrusion = Mathf.Max(maxOpposingIntrusion, opposing.width * 0.5f - Mathf.Abs(lat));
                    }
            }
            if (Minibus != null)
            {
                minibusMoved = Mathf.Max(minibusMoved, Flat(Minibus.transform.position - minibusStart).magnitude);
                if (Minibus.Body != null && !Minibus.Body.isKinematic) maxMinibusSpeed = Mathf.Max(maxMinibusSpeed, Minibus.Body.linearVelocity.magnitude);
            }
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
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.Follower.Distance > Dir.StopS - 6f || Time.time - t0 > 90f; });
        bool visibleApproach = VanVisible(van, out string visibleInfo);
        if (van != null) yield return Shot("thiefvan_a1_yaklasma", RoadCam(van.transform.position, 12f, 9f, 3.5f), van.transform.position + Vector3.up);
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.Phase == ThiefVan.VanPhase.Maneuvering || Time.time - t0 > 90f; });
        float approachT = Time.time - t0;
        float sStopInLane = van != null ? van.Follower.Distance : -1f;
        east.Project(van != null ? van.transform.position : Vector3.zero, out float latAtStop, out _);
        // Manevra ortası: direksiyon kırık, tekerlek açısı yakın plandan görünür
        float steerAtShot = 0f;
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.IsParked || (van.ReverseDistance > 2.2f && Mathf.Abs(van.Follower.CurrentSteerAngle) > 12f) || Time.time - t0 > 160f; });
        if (van != null && !van.IsParked)
        {
            steerAtShot = van.Follower.CurrentSteerAngle;
            yield return Shot("thiefvan_a2_geri_manevra", RoadCam(van.transform.position, 9f, 9f, 4f), van.transform.position + Vector3.up);
            Vector3 f = Flat(van.transform.forward).normalized, r = Vector3.Cross(Vector3.up, f);
            yield return Shot("thiefvan_a3_on_teker_direksiyon", van.transform.position + f * 3.9f - r * 3.2f + Vector3.up * 1.0f, van.transform.position + f * 1.7f - r * 0.9f + Vector3.up * 0.4f, 40f);
        }
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
        bool steered = smp.maxSteer > 15f && smp.spinChange > 360f;
        bool opposingClear = smp.maxOpposingIntrusion < 0.05f;
        // Tekerlek görseli: manevra boyunca açı ve dönüş uygulandı (trafik görsel hattı); ayrıca kök gövde kamyonet görünür kaldı
        bool unregistered = parked && !LaneOccupancy.Followers.Contains(van.Follower) && !van.ParkedInLane;
        bool untouched = van != null && van.Rams == 0 && Dir.RamCount == 0 && Dir.RamPenaltyTotal == 0 && smp.minibusMoved < 0.05f && Minibus.State == ServiceVanDriver.VanState.Parked && Minibus.Damage == 0f;
        Check("(A1) iyi park: kamyonet durağı " + (van != null ? (sStopInLane - Dir.StopS).ToString("F1", Inv) : "-") + " m geçip şeritte durur, GERİ GERİ direksiyon kırarak (|açı| > 15°, tekerlek döner) bankete girer, tam durakta şeride paralel park eder (s ± 0.6, gövde şerit dışı), şerit kaydından çıkar; minibüse dokunmaz, vuruş / ceza yok",
            parked && atStop && overshoot && reversed && steered && unregistered && untouched && opposingClear && visibleApproach,
            "görünür " + visibleApproach + " (" + visibleInfo + "), karşı şeride giriş " + smp.maxOpposingIntrusion.ToString("F2", Inv) + " m, görüntüde direksiyon " + steerAtShot.ToString("F0", Inv) + "°; park " + parked + " (" + (Time.time - t0).ToString("F1", Inv) + " sn; yaklaşma " + approachT.ToString("F1", Inv) + " sn) s " + s.ToString("F1", Inv) + " / durak " + Dir.StopS.ToString("F1", Inv) + ", yanal " + lat.ToString("F2", Inv) + " / " + Dir.ShoulderLateral.ToString("F2", Inv)
            + ", yön " + headDot.ToString("F3", Inv) + "; şeritte durdu s " + sStopInLane.ToString("F1", Inv) + " yanal " + latAtStop.ToString("F2", Inv) + "; adımlar [" + string.Join(",", smp.steps) + "], en düşük hız " + smp.minSpeed.ToString("F2", Inv) + " m/sn, geri yol " + (van != null ? van.ReverseDistance.ToString("F1", Inv) : "-") + " m, en çok direksiyon " + smp.maxSteer.ToString("F0", Inv) + "°, tekerlek dönüşü " + smp.spinChange.ToString("F0", Inv)
            + "°; kayıt dışı " + unregistered + ", minibüs kıpırdadı " + smp.minibusMoved.ToString("F2", Inv) + " m, vuruş " + (van != null ? van.Rams : -1) + ", ceza ₺" + Dir.RamPenaltyTotal + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        if (!parked) { Dir.ClearAll(); yield break; }
        yield return Shot("thiefvan_a4_park", RoadCam(van.transform.position, 8f, 10f, 4f), van.transform.position + Vector3.up);
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
        int seenRams = 0, shoves0 = Minibus.PhysicalShoves;
        float t0 = Time.time;
        Time.timeScale = 4f;
        // Her vuruşun ilk iki tanesinde uçan yazı görüntüsü alınır
        int shots = 0;
        while (true)
        {
            int target = seenRams;
            yield return (Func<bool>)(() =>
            {
                smp.Tick(van);
                if (van != null && van.Phase == ThiefVan.VanPhase.Maneuvering && van.Step == ThiefVan.ManeuverStep.Reverse && van.Rams == 0) occupiedAtStart |= van.SpotOccupied;
                return van == null || van.Rams > target || van.IsParked || Time.time - t0 > 200f;
            });
            if (van == null || van.IsParked || Time.time - t0 > 200f) break;
            seenRams = van.Rams;
            fines.Add(van.ManeuverLog.Count > 0 ? van.ManeuverLog[van.ManeuverLog.Count - 1] : "?");
            if (shots < 2)
            {
                shots++;
                float prevTs = Time.timeScale;
                Time.timeScale = 1f;
                yield return Seconds(0.3f);
                yield return Shot("thiefvan_b" + shots + "_vurus_ucan_yazi", RoadCam(Minibus.transform.position, 7f, 12f, 5f), Minibus.transform.position + Vector3.up * 1.5f);
                Time.timeScale = prevTs;
            }
        }
        yield return Until(() => Minibus.State == ServiceVanDriver.VanState.Parked, 15f, "minibüs itildikten sonra durur");
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        float s = -1f, lat = 0f;
        if (van != null) s = east.Project(van.transform.position, out lat, out _);
        bool atStop = parked && !van.ParkedInLane && Mathf.Abs(s - Dir.StopS) < 0.8f && Mathf.Abs(lat) - van.Vehicle.bodySize.x * 0.5f >= east.width * 0.5f - 0.1f;
        bool rammed = van != null && van.Rams >= 1 && Dir.RamCount >= van.Rams && Dir.RamLog.All(r => r.fine >= Dir.Active.ramPenaltyMin && r.fine <= Dir.Active.ramPenaltyMax && r.fine % 10 == 0) && Dir.RamPenaltyTotal == Dir.RamLog.Sum(r => r.fine) && van.RamPenaltyTotal <= Dir.RamPenaltyTotal;
        // Artan hız: ikinci vuruş ilkinden sert (son vuruş rota sonuna yakın, doğal olarak yavaş olabilir)
        bool escalates = Dir.RamLog.Count < 2 || Dir.RamLog[1].speed > Dir.RamLog[0].speed - 0.05f;
        bool cleared = van != null && !van.IsSpotOccupied() && smp.minibusMoved > 0.5f;
        float maxMinibusSpeed = smp.maxMinibusSpeed;
        bool minibusOk = Minibus.State == ServiceVanDriver.VanState.Parked && Minibus.TiltDeg < 10f && Minibus.FallRespawns == 0 && Minibus.PhysicalShoves >= 1;
        // Her fiziksel itme ücretli (ücretsiz çarpma yok); itme bütçesi: yeri boşaltmaya gereken kadar (durak dikdörtgeni + minibüs boyu)
        int shoveCount = Minibus.PhysicalShoves - shoves0;
        // Her fiziksel itme ücretli: temas titreşimi tek vuruşta birkaç itme açabilir (çift ceza kilidi), bu yüzden en az yarısı
        bool shovesCharged = shoveCount <= 2 * Dir.RamCount;
        bool pushBudget = smp.minibusMoved <= 7.5f;
        if (parked) yield return Shot("thiefvan_b3_kotu_park_son", RoadCam(van.transform.position, 12f, 11f, 6f), van.transform.position - Flat(van.transform.forward).normalized * 3f + Vector3.up);
        int floats = Hud != null ? Hud.PenaltyFloatingSpawned - floats0 : -1;
        bool hudOk = Hud != null && floats == Dir.RamCount && Hud.LastPenaltyLabel.StartsWith("-" + LootRegistry.MoneySymbol) && Hud.LastPenaltyLabel.EndsWith(MissionHud.PenaltySuffix)
            && Hud.MoneyText.text.Contains("-") && Hud.MoneyText.text.Contains(Dir.RamPenaltyTotal.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")));
        Check("(B1) kötü park (minibüs durağın üstünde, şerit dışı): kamyonet geri geri yanaşırken minibüse vurur, minibüs fiziksel itilir (PhysX), kamyonet ileri açılıp artan hızla yeniden vurur; her vuruşta ₺" + Dir.Active.ramPenaltyMin + "–" + Dir.Active.ramPenaltyMax + " ceza (hıza göre, 10'a yuvarlı), kayıt toplamı tutar; minibüsün üstünde uçan \"−₺X çarpma (kötü park)\", HUD bakiyesi eksiye düşer; yer boşalır, kamyonet durakta park eder; minibüs devrilmez / uçuruma düşmez",
            parked && atStop && occupiedAtStart && rammed && escalates && cleared && minibusOk && hudOk && shovesCharged && pushBudget && maxMinibusSpeed < 6f,
            "minibüs en yüksek hız " + maxMinibusSpeed.ToString("F1", Inv) + " m/sn, fiziksel itme " + shoveCount + " / ücretli vuruş " + Dir.RamCount + ", itme bütçesi ≤ 7.5 m: " + smp.minibusMoved.ToString("F2", Inv) + " m; şeridi kapatıyordu " + blocksLane + ", başta yer dolu " + occupiedAtStart + ", park " + parked + " (" + (Time.time - t0).ToString("F1", Inv) + " sn) s " + s.ToString("F1", Inv) + " / " + Dir.StopS.ToString("F1", Inv) + " yanal " + lat.ToString("F2", Inv) + " şeritte " + (van != null && van.ParkedInLane)
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

    // (C3) GERÇEK duvar: minibüsün arkasında statik kutu (kaya gibi); test bayrağı yok, fizik açık. Minibüs fırlatılmaz, kamyonet vazgeçer.
    private static IEnumerator PhaseRealWall()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        ParkMinibusOnStop();
        yield return Seconds(0.3f);
        Vector3 mb = Minibus.transform.position, tan = Flat(east.TangentAt(Dir.StopS)).normalized;
        float halfLen = 0f;
        foreach (var p in Minibus.Footprint()) halfLen = Mathf.Max(halfLen, Mathf.Abs(Vector3.Dot(Flat(p - mb), tan)));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ThiefVanVerify_Wall";
        wall.isStatic = true;
        wall.transform.position = mb - tan * (halfLen + 0.35f + 0.5f) + Vector3.up * 1.5f;
        wall.transform.rotation = Quaternion.LookRotation(tan, Vector3.up);
        wall.transform.localScale = new Vector3(6f, 3f, 1f);
        Physics.SyncTransforms();
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (C3)");
        var smp = new Sampler { minibusStart = mb };
        float t0 = Time.time, maxTilt = 0f;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => { smp.Tick(van); maxTilt = Mathf.Max(maxTilt, Minibus.TiltDeg); return van == null || van.IsParked || Time.time - t0 > 220f; });
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        bool thrown = smp.minibusMoved > 0.5f || maxTilt > 8f || Minibus.FallRespawns > 0;
        bool bounded = van != null && van.Rams >= 1 && van.Rams <= Dir.Active.maxBlockedRams + 2 && van.ManeuverTime <= Dir.Active.maxRamSeconds + 25f;
        Check("(C3) gerçek duvar (minibüsün arkasında statik engel, test bayrağı yok): kamyonet sınırlı vuruştan sonra vazgeçip şeritte park eder; minibüs sıkışıp fırlatılmaz (≤ 0.5 m, eğim ≤ 8°)",
            parked && van.ParkedInLane && bounded && !thrown,
            "park " + parked + " şeritte " + (van != null && van.ParkedInLane) + ", vuruş " + (van != null ? van.Rams : -1) + ", manevra " + (van != null ? van.ManeuverTime.ToString("F1", Inv) : "-") + " sn, ceza ₺" + Dir.RamPenaltyTotal + ", minibüs " + smp.minibusMoved.ToString("F2", Inv) + " m, en çok eğim " + maxTilt.ToString("F1", Inv) + "°, düşme " + Minibus.FallRespawns + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        UnityEngine.Object.Destroy(wall);
        Dir.ClearAll();
        Minibus.TeleportTo(zonePos, zoneRot, true);
        yield return Seconds(0.5f);
    }

    // (C4) Kenar: minibüsü banketin itme yönünde zemini kesilen yerine koy (varsa). Hiç uçurum düşmesi yok, kamyonet döngüye girmez.
    private static IEnumerator PhaseEdge()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        // Durak dikdörtgenini kapatan ve dışa döndürülmüş minibüs: en dış yanal, 25° dışa bakan
        float s = Dir.StopS - 1.5f;
        float bestLat = Dir.ShoulderLateral + 0.5f;
        for (float lat = bestLat; lat <= Dir.ShoulderLateral + 6f; lat += 0.25f)
        {
            Vector3 q = east.PointAt(s) + east.RightAt(s) * lat;
            if (!GroundProbe.Find(q + Vector3.up * 3f, 6f, out RaycastHit gh) || gh.normal.y < 0.85f || Mathf.Abs(gh.point.y - east.PointAt(s).y) > 1f) break;
            bestLat = lat;
        }
        Vector3 pos = east.PointAt(s) + east.RightAt(s) * Mathf.Max(Dir.ShoulderLateral + 0.5f, bestLat - 1.2f);
        if (GroundProbe.Find(pos + Vector3.up * 2f, 5f, out RaycastHit g)) pos.y = g.point.y;
        Minibus.ResetDamageForTest();
        int falls0 = Minibus.FallRespawns;
        Minibus.TeleportTo(pos, Quaternion.AngleAxis(25f, Vector3.up) * Quaternion.LookRotation(-east.TangentAt(s), Vector3.up), true);
        yield return Seconds(0.5f);
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (C4)");
        var smp = new Sampler { minibusStart = pos };
        float t0 = Time.time;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => { smp.Tick(van); return van == null || van.IsParked || Time.time - t0 > 220f; });
        Time.timeScale = 1f;
        yield return Until(() => Minibus.State == ServiceVanDriver.VanState.Parked, 15f, "minibüs durur (C4)");
        bool parked = van != null && van.IsParked;
        float mbY = Minibus.transform.position.y;
        Check("(C4) dışa dönük, banket kenarına yakın minibüs: kamyonet iter ya da vazgeçer ama minibüs uçuruma düşmez, yol kotundan ayrılmaz, kamyonet park eder (döngü yok)",
            parked && Minibus.FallRespawns == falls0 && Mathf.Abs(mbY - pos.y) < 1.2f && Minibus.TiltDeg < 12f,
            "park " + parked + " şeritte " + (van != null && van.ParkedInLane) + ", vuruş " + (van != null ? van.Rams : -1) + ", ceza ₺" + Dir.RamPenaltyTotal + "; yanal başlangıç " + east.PointAt(s).y.ToString("F1", Inv) + "/" + bestLat.ToString("F2", Inv) + ", minibüs " + smp.minibusMoved.ToString("F2", Inv) + " m → " + MinibusPose() + ", y " + mbY.ToString("F2", Inv) + " (başta " + pos.y.ToString("F2", Inv) + "), eğim " + Minibus.TiltDeg.ToString("F1", Inv) + "°, düşme " + (Minibus.FallRespawns - falls0) + ", " + Minibus.LastShoveReason + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        Dir.ClearAll();
        Minibus.TeleportTo(zonePos, zoneRot, true);
        yield return Seconds(0.5f);
    }

    // (D) Minibüs şeridi kapatıyor (durağın gerisinde, şerit ortasında): trafik kuralı (bekle-korna-çarp), vuruş ücretli ve uçan yazılı
    private static IEnumerator PhaseLaneBlock()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        float s = Dir.StopS - 22f;
        Vector3 pos = east.PointAt(s);
        if (GroundProbe.Find(pos + Vector3.up * 2f, 5f, out RaycastHit g)) pos.y = g.point.y;
        Minibus.ResetDamageForTest();
        Minibus.TeleportTo(pos, Quaternion.LookRotation(-east.TangentAt(s), Vector3.up), true);
        yield return Seconds(0.3f);
        int floats0 = Hud != null ? Hud.PenaltyFloatingSpawned : 0;
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (D)");
        float t0 = Time.time;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => van == null || van.IsParked || Time.time - t0 > 200f);
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        int floats = Hud != null ? Hud.PenaltyFloatingSpawned - floats0 : -1;
        Check("(D) minibüs şeridi kapatıyor: kamyonet durur, korna, çarpar; vuruş ₺50–250 yazılır (kaynak şerit), uçan yazı çıkar; sonra durağına gidip park eder; uyarı yok",
            parked && Dir.RamCount >= 1 && Dir.RamLog.Any(r => r.source == "şerit") && Dir.RamLog.All(r => r.fine >= 50 && r.fine <= 250) && floats == Dir.RamCount && vanWarnings.Count == 0,
            "park " + parked + " şeritte " + (van != null && van.ParkedInLane) + " (" + (Time.time - t0).ToString("F1", Inv) + " sn), vuruş " + Dir.RamCount + ": " + string.Join(" | ", Dir.RamLog.Select(r => r.speed.ToString("F1", Inv) + " m/sn → ₺" + r.fine + " [" + r.source + "]")) + ", uçan yazı " + floats + ", minibüs → " + MinibusPose() + ", hasar %" + Minibus.Damage.ToString("F0", Inv) + (van != null ? "; log: " + string.Join(" | ", van.ManeuverLog) : ""));
        Dir.ClearAll();
        Minibus.TeleportTo(zonePos, zoneRot, true);
        yield return Seconds(0.5f);
    }

    // (E) Arkada trafik: kamyonetin hemen arkasından araç gelir; geri manevra araca girmez, kamyonet ileri yayla bankete çıkar, araç geçer
    private static IEnumerator PhaseTrafficBehind()
    {
        Dir.ResetPenaltiesForTest();
        vanWarnings.Clear();
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (E)");
        float t0 = Time.time;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => van == null || van.Follower.Distance > 30f || Time.time - t0 > 30f);
        var car = Traffic.ForceSpawn(true, TrafficVehicleType.Sedan);
        if (car == null) throw new Exception("takip aracı çıkamadı (E)");
        bool overlap = false, carPassed = false, shotDone = false;
        var vanLog = van != null ? van.ManeuverLog : new List<string>();
        float carStopped = 0f, maxSteer = 0f;
        Func<bool> sample = () =>
        {
            if (van != null && car != null)
            {
                overlap |= BodiesOverlap(van, car);
                if (car.Follower.Distance > van.Follower.Distance + 3f) carPassed = true;
                if (car.Follower.Speed < 0.05f) carStopped += Time.deltaTime;
                if (van.Phase == ThiefVan.VanPhase.Maneuvering) maxSteer = Mathf.Max(maxSteer, Mathf.Abs(van.Follower.CurrentSteerAngle));
            }
            if (car == null) carPassed = true;
            return van == null || (van.IsParked && carPassed) || Time.time - t0 > 200f;
        };
        yield return (Func<bool>)(() => sample() || (van != null && van.Step == ThiefVan.ManeuverStep.Reverse && van.ReverseDistance > 3f));
        if (van != null && van.Step == ThiefVan.ManeuverStep.Reverse)
        {
            shotDone = true;
            yield return Shot("thiefvan_e1_arkada_arac", RoadCam(van.transform.position, 14f, 9f, 5f), van.transform.position - Flat(van.transform.forward).normalized * 3f + Vector3.up);
        }
        yield return sample;
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        float s = -1f, lat = 0f;
        if (van != null) s = east.Project(van.transform.position, out lat, out _);
        bool atStop = parked && !van.ParkedInLane && Mathf.Abs(s - Dir.StopS) < 0.8f && Mathf.Abs(lat - Dir.ShoulderLateral) < 0.5f;
        Check("(E) arkadan araç gelirken: kamyonet şeritte durur, geri rotası bankete kırılır, araca girmez (gövde kesişmez), gövde şeritten çıkınca araç geçer, kamyonet durağına park eder",
            parked && atStop && !overlap && carPassed && maxSteer > 10f && shotDone,
            "park " + parked + " s " + s.ToString("F1", Inv) + " yanal " + lat.ToString("F2", Inv) + " şeritte " + (van != null && van.ParkedInLane) + ", kesişme " + overlap + ", araç geçti " + carPassed + " (bekledi " + carStopped.ToString("F1", Inv) + " sn), direksiyon " + maxSteer.ToString("F0", Inv) + "°, görüntü " + shotDone + "; log: " + string.Join(" | ", vanLog));
        Dir.ClearAll();
        if (car != null) UnityEngine.Object.Destroy(car.gameObject);
        yield return Seconds(0.5f);

        // (E2) en kötü durum: geniş araç (kamyon) manevra başlarken asgari boşlukta arkada duruyor. Kesişme yok; kamyonet ya banket
        // durağına girer ya da (yanal boşluk yetmezse) şeritte park eder, sonra gider. İkisinde de uyarı yok.
        vanWarnings.Clear();
        van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (E2)");
        t0 = Time.time;
        Time.timeScale = 4f;
        yield return (Func<bool>)(() => van == null || van.Phase == ThiefVan.VanPhase.Maneuvering || Time.time - t0 > 90f);
        car = Traffic.ForceSpawn(true, TrafficVehicleType.PanelVan);
        if (car == null) throw new Exception("takip aracı çıkamadı (E2)");
        if (van != null) car.Follower.Place(east, van.Follower.RearS - 4f - car.Follower.frontOverhang, 0f);
        overlap = false;
        yield return (Func<bool>)(() =>
        {
            if (van != null && car != null) overlap |= BodiesOverlap(van, car);
            return van == null || van.IsParked || Time.time - t0 > 200f;
        });
        Time.timeScale = 1f;
        parked = van != null && van.IsParked;
        bool inLane = parked && van.ParkedInLane;
        var e2Log = van != null ? van.ManeuverLog : new List<string>();
        Time.timeScale = 4f;
        yield return (Func<bool>)(() =>
        {
            if (van != null && car != null) overlap |= BodiesOverlap(van, car);
            return van == null || car == null || Time.time - t0 > 200f;
        });
        Time.timeScale = 1f;
        Check("(E2) geniş araç asgari boşlukta arkadayken: gövdeler kesişmez, kamyonet park eder (bankette ya da araca değecekse şeritte), kalkar; uyarı yok",
            parked && !overlap && vanWarnings.Count == 0,
            "park " + parked + " şeritte " + inLane + ", kesişme " + overlap + ", uyarı " + vanWarnings.Count + "; log: " + string.Join(" | ", e2Log));
        Dir.ClearAll();
        if (car != null) UnityEngine.Object.Destroy(car.gameObject);
        yield return Seconds(0.5f);
    }

    // Codex review bulguları: P1 (LaneFollower'lı kamyonet ScanForPusher'dan elenmez), P2 (kinematik temas sürüşteki itki hasarını bastırmaz)
    private static IEnumerator PhaseReviewFindings()
    {
        // P1: kamyonet itmeyi kendisi başlatmaz (bayrak), minibüs yalnız ScanForPusher ile dinamikleşebilir
        Dir.ResetPenaltiesForTest();
        ParkMinibusOnStop();
        yield return Seconds(0.3f);
        ThiefVan.ImmovableMinibusForTest = true;
        int shoves0 = Minibus.PhysicalShoves;
        var van = Spawn(1);
        if (van == null) throw new Exception("kamyonet çıkamadı (P1)");
        float t0 = Time.time;
        Time.timeScale = 4f;
        Rigidbody vanBody = van.GetComponent<Rigidbody>();
        yield return (Func<bool>)(() => van == null || van.IsParked || Minibus.PhysicalShoves > shoves0 || Time.time - t0 > 120f);
        Time.timeScale = 1f;
        Check("(P1) ScanForPusher: LaneFollower'lı kamyonet gövdesi park hâlindeki minibüsü fiziksel itmeye geçirir (itici = kamyonet)",
            Minibus.PhysicalShoves > shoves0 && Minibus.LastPusher == vanBody,
            "fiziksel itme " + (Minibus.PhysicalShoves - shoves0) + ", itici kamyonet " + (Minibus.LastPusher == vanBody) + ", ceza ₺" + Dir.RamPenaltyTotal);
        ThiefVan.ImmovableMinibusForTest = false;
        Dir.ClearAll();
        yield return Seconds(0.5f);

        // P2: sürüşte (dinamik) minibüs hareketli kinematik gövdeye çarpar: temas lastRamContact'ı güncellese de itki hasarı yazılır
        float s = 60f;
        Vector3 tan = Flat(east.TangentAt(s)).normalized, pos = east.PointAt(s);
        if (GroundProbe.Find(pos + Vector3.up * 2f, 5f, out RaycastHit g)) pos.y = g.point.y;
        Minibus.ResetDamageForTest();
        Minibus.TeleportTo(pos, Quaternion.LookRotation(-tan, Vector3.up), false);
        yield return Seconds(0.7f);
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "ThiefVanVerify_KinematicBox";
        box.transform.localScale = new Vector3(3f, 2f, 1f);
        box.transform.rotation = Quaternion.LookRotation(tan, Vector3.up);
        box.transform.position = Minibus.transform.position + tan * 6f + Vector3.up * 1.1f;
        var boxBody = box.AddComponent<Rigidbody>();
        boxBody.isKinematic = true;
        box.AddComponent<DriveVerifyKinematicPusher>().velocity = -tan * 1f;
        Physics.SyncTransforms();
        int impacts0 = Minibus.Impacts;
        Minibus.Body.linearVelocity = tan * 9f;
        float t1 = Time.time;
        yield return (Func<bool>)(() => Minibus.Impacts > impacts0 || Time.time - t1 > 3f);
        Check("(P2) sürüşte hareketli kinematik gövdeye çarpan minibüse itki hasarı yazılır (temas bastırmaz)",
            Minibus.Impacts > impacts0 && Minibus.Damage > 0f,
            "darbe " + (Minibus.Impacts - impacts0) + ", hasar %" + Minibus.Damage.ToString("F1", Inv));
        UnityEngine.Object.Destroy(box);
        Minibus.ResetDamageForTest();
        Minibus.TeleportTo(zonePos, zoneRot, true);
        yield return Seconds(0.3f);
    }

    // Kamyonet gövdesi ile trafik aracı gövdesi kesişiyor mu (örnek noktalar, iki yönlü)
    private static bool BodiesOverlap(ThiefVan van, TrafficVehicle car)
    {
        float halfW = van.Vehicle.bodySize.x * 0.5f;
        foreach (float x in new[] { -halfW, 0f, halfW })
            foreach (float z in new[] { -van.Follower.rearOverhang, -1.2f, 1.2f, van.Follower.frontOverhang })
                if (car.Touches(van.transform.TransformPoint(new Vector3(x, 0.8f, z)), 0f)) return true;
        float carHalfW = car.bodySize.x * 0.5f;
        foreach (float x in new[] { -carHalfW, carHalfW })
            foreach (float z in new[] { -car.Follower.rearOverhang, car.Follower.frontOverhang })
                if (van.Vehicle.Touches(car.transform.TransformPoint(new Vector3(x, 0.8f, z)), 0f)) return true;
        return false;
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
