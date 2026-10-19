using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bölüm 1 gerçek trafik araçları + prosedürel animasyonlar + şef kararları 5–8 kabul testi.
// Batchmode (ekran görüntüsü için -nographics OLMADAN, -quit OLMADAN):
// Unity.exe -batchmode -projectPath ... -executeMethod Bolum1AnimVerify.Run -logFile <dosya>
// Başarıda "ANIM VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/Bolum1/anim_verify_report.txt
//
// SESSİZLİK: batchmode'da Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0.
// Ayarlar test içinde geçici kopyalarla değiştirilir (TrafficSettings, AmbulanceSchedule); asset dosyaları değişmez.
// Senaryo kare kare ilerleyen bir IEnumerator'dır (Bolum1TrafficVerify ile aynı desen).
[InitializeOnLoad]
public static partial class Bolum1AnimVerify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "Bolum1AnimVerify.Stage";
    private const string ReportKey = "Bolum1AnimVerify.Report";
    private const string OkKey = "Bolum1AnimVerify.Ok";
    private const string BatchKey = "Bolum1AnimVerify.Batch";
    private const int Seed = 20261016;
    private const float SimSeconds = 300f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static Bolum1AnimVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Bölüm 1/Araç + Animasyon: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("ANIM VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            CheckClips();
            CheckCarryClipMeta(); // D2 madde 7
            CheckControllers();
            CheckVehiclePrefabs();
            CheckScene();
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

    // =====================================================================================
    // Edit aşaması
    // =====================================================================================

    // (a) 11 klip Humanoid, Avatar geçerli (SK_RescueCrew_Blue'dan kopya), döngüler loop, kök gömülü.
    private static void CheckClips()
    {
        var parts = new List<string>();
        bool ok = true;
        foreach (var n in ProceduralAnimImportSettings.ClipNames)
        {
            var path = ProceduralAnimImportSettings.ClipPath(n);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            var clip = ProceduralAnimImportSettings.LoadClip(n);
            bool applied = imp != null && ProceduralAnimImportSettings.IsApplied(imp, out _);
            bool avatar = imp != null && imp.sourceAvatar != null && imp.sourceAvatar.isValid && imp.sourceAvatar.isHuman;
            bool loop = clip != null && clip.isLooping == ProceduralAnimImportSettings.IsLoop(n);
            bool human = clip != null && clip.isHumanMotion;
            bool c = applied && avatar && loop && human;
            ok &= c;
            // FBX'ler 30 kare/sn işaretli: Unity klip süresi yazılan süreyle aynı olmalı (durum hızı yaması yok).
            float authored = Bolum1AnimBuilder.MetaFloat(n, "duration_s", -1f);
            bool length = clip != null && Mathf.Abs(clip.length - authored) < 0.02f;
            c &= length;
            ok &= length;
            parts.Add(n + (c ? "" : "[HATA]") + " " + (clip != null ? clip.length.ToString("F2", Inv) + "/" + authored.ToString("F2", Inv) + " sn" : "-") + (clip != null && clip.isLooping ? " loop" : ""));
        }
        Check("(a) " + ProceduralAnimImportSettings.ClipNames.Length + " klip Humanoid, Avatar = SK_RescueCrew_Blue kopyası (geçerli), döngüler loop, kök pozda (Original; Jump: Feet)", ok,
            string.Join(", ", parts) + " (Unity/yazılan süre; FBX 30 kare/sn)");
    }

    private static void CheckControllers()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Bolum1AmbulanceBuilder.LocomotionControllerPath);
        var want = new[] { CrewAnimation.StateLocomotion, CrewAnimation.StateCarryStretcher, CrewAnimation.StateCarryStretcherFront, CrewAnimation.StateCarryCone, CrewAnimation.StatePickUp, CrewAnimation.StatePutDown,
            CrewAnimation.StateJump, CrewAnimation.StateStartled, CrewAnimation.StateGetUp, CrewAnimation.StateRevive, CrewAnimation.StateCelebrate };
        var have = ac != null ? ac.layers[0].stateMachine.states.Select(s => s.state.name).ToList() : new List<string>();
        var missing = want.Where(w => !have.Contains(w)).ToList();
        var patients = Bolum1AmbulanceBuilder.PatientTypes.Select(t => AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.PatientPrefabPath(t))).ToList();
        var pinfo = patients.Select(p =>
        {
            var pt = p.GetComponent<Patient>();
            var a = p.GetComponent<Animator>();
            var pac = a.runtimeAnimatorController as AnimatorController;
            string clips = pac != null ? string.Join("+", pac.animationClips.Select(c => c.name).Distinct()) : "-";
            return p.name + ": " + (pac != null ? pac.name : "-") + " (" + clips + "), el sallar " + pt.canWave;
        }).ToList();
        bool patientsOk = patients.All(p =>
        {
            var pt = p.GetComponent<Patient>();
            var pac = p.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
            if (pac == null) return false;
            var names = pac.animationClips.Select(c => c.name).ToList();
            return p.name.Contains("Civilian") ? !pt.canWave && names.Contains("Lying_Groan") && names.Any(n => n.Contains("ArmSling")) && pac.layers.Length == 2 && pac.layers[1].avatarMask != null
                : pt.canWave && names.Contains("Lying_Groan") && names.Contains("Lying_HelpWave");
        });
        Check("(a2) Animator'lar: ekip (" + want.Length + " durum) ve hastalar (Lying_HelpWave ↔ Lying_Groan; Civilian askılı kol + kolsuz maskeli Lying_Groan)",
            ac != null && missing.Count == 0 && patientsOk,
            (ac != null ? ac.name + ": " + string.Join(", ", have) : "AC yok") + (missing.Count > 0 ? " EKSİK " + string.Join(",", missing) : "") + "; " + string.Join("; ", pinfo));
    }

    // (d0) prefab'lar gerçek FBX'ten: mesh'ler Assets/Models/Vehicles/Traffic altında, yer tutucu kutu/silindir yok.
    private static void CheckVehiclePrefabs()
    {
        var parts = new List<string>();
        bool ok = true;
        foreach (var t in Bolum1TrafficBuilder.Types)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabPath(t));
            bool real = RealModel(go, out string info);
            var v = go.GetComponent<TrafficVehicle>();
            var f = go.GetComponent<LaneFollower>();
            bool paint = !TrafficSettings.IsPassengerCar(t) || (v.paintColors.Length == 4 && v.paintRenderers.Length > 0);
            bool hay = t != TrafficVehicleType.Pickup || (go.GetComponent<HayCargo>() != null && go.GetComponent<HayCargo>().bales.Length == 2 && go.GetComponent<HayCargo>().bales.All(b => b != null && b.GetComponent<Rigidbody>() == null));
            ok &= real && paint && hay && f.wheels.Length >= 4;
            parts.Add(t + ": " + info + ", tekerlek " + f.wheels.Length + " r " + f.wheelRadius.ToString("F2", Inv) + ", kutu " + V(v.bodySize)
                + (TrafficSettings.IsPassengerCar(t) ? ", boya " + string.Join("/", Bolum1TrafficBuilder.MetaPaintNames(t)) : "") + (t == TrafficVehicleType.Pickup ? ", balya bağlı " + hay : ""));
        }
        Check("(d0) 5 araç prefab'ı gerçek modelden (yer tutucu yok); binekte 4 boya varyantı; pikap balyaları araca bağlı", ok, string.Join("; ", parts));
    }

    private static bool RealModel(GameObject go, out string info)
    {
        var meshes = go.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).Where(m => m != null).ToList();
        int fromFbx = meshes.Count(m => AssetDatabase.GetAssetPath(m).StartsWith(Bolum1TrafficBuilder.ModelFolder));
        bool primitive = meshes.Any(m => m.name == "Cube" || m.name == "Cylinder");
        info = fromFbx + "/" + meshes.Count + " mesh FBX'ten";
        return meshes.Count > 0 && fromFbx == meshes.Count && !primitive;
    }

    private static void CheckScene()
    {
        var player = GameObject.Find(Bolum1SceneBuilder.CharacterName);
        var crew = player.GetComponent<CrewAnimation>();
        var respawn = player.GetComponent<PlayerFallRespawn>();
        var bot = UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>();
        bool ok = crew != null && crew.player != null && player.GetComponent<PlayerKnockdown>().crewAnimation == crew && respawn != null && respawn.spawnPoint != null
            && bot.crewAnimation != null && bot.ragdollBodies.Length >= 10 && bot.hips != null;
        Check("(a3) sahne: oyuncu ve botta CrewAnimation, bot ragdoll'u, oyuncu yeniden doğma (FallRespawnPlane → PlayerSpawn)", ok,
            "bot ragdoll " + bot.ragdollBodies.Length + " gövde, yeniden doğma y < " + (respawn != null ? respawn.killBelowY.ToString("F1", Inv) : "-"));
    }

    // =====================================================================================
    // Play aşaması altyapısı
    // =====================================================================================
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
        if (Time.realtimeSinceStartup - realStart > 2400f) { Fail("zaman aşımı (40 dk)"); End(); return; }

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
        Line(allOk ? "ANIM VERIFY OK" : "ANIM VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "anim_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "ANIM VERIFY OK" : "ANIM VERIFY FAILED");
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
    private static CrewAnimation PlayerAnim { get { return player.GetComponent<CrewAnimation>(); } }
    private static PlayerInteraction Interaction { get { return player.GetComponent<PlayerInteraction>(); } }
    private static StretcherHelperBot Bot { get { return UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>(); } }
    private static CrewAnimation BotAnim { get { return Bot.crewAnimation; } }
    private static StretcherController Stretcher(int i) { return GameObject.Find(Bolum1AmbulanceBuilder.StretcherNames[i]).GetComponent<StretcherController>(); }
    private static TrafficCone Cone(int i) { var go = GameObject.Find(Bolum1TrafficBuilder.ConeControllerName(Bolum1TowTruck.ConeNames[i])); return go != null ? go.GetComponent<TrafficCone>() : null; }
    private static Patient PatientNamed(string type) { return Mission.patients.First(p => p.name == "Patient_" + type); }

    // =====================================================================================
    // Her kare örneklenen ölçümler (araç simülasyonu, ambulans takibi)
    // =====================================================================================
    private static bool simActive;
    private static readonly HashSet<TrafficVehicle> seen = new HashSet<TrafficVehicle>();
    private static readonly Dictionary<TrafficVehicleType, int> typeCounts = new Dictionary<TrafficVehicleType, int>();
    private static readonly HashSet<int> paintsSeen = new HashSet<int>();
    private static int placeholderVehicles, paintMismatch;
    private static float worstWheel;
    private static string worstWheelInfo = "";
    private static int wheelSamples;
    private static int axisSamples, axisBad, senseSamples, senseBad;
    private static string axisInfo = "";

    private class WheelTrack { public Vector3 axleLocal, upLocal; public Vector3 prevUp; public float prevS; }
    private static readonly Dictionary<Transform, WheelTrack> wheelTracks = new Dictionary<Transform, WheelTrack>();

    private static void Sample()
    {
        if (simActive && Traffic != null) SampleVehicles();
        if (ambWatch != null) SampleAmbulance();
    }

    private static void SampleVehicles()
    {
        foreach (var v in Traffic.Vehicles)
        {
            if (v == null) continue;
            if (seen.Add(v))
            {
                typeCounts[v.type] = (typeCounts.TryGetValue(v.type, out int n) ? n : 0) + 1;
                if (!RealModel(v.gameObject, out _)) placeholderVehicles++;
                if (TrafficSettings.IsPassengerCar(v.type))
                {
                    paintsSeen.Add(v.PaintIndex);
                    var block = new MaterialPropertyBlock();
                    foreach (var r in v.paintRenderers)
                    {
                        r.GetPropertyBlock(block, v.paintMaterialIndex);
                        Color c = block.GetColor("_BaseColor");
                        Color want = v.paintColors[Mathf.Clamp(v.PaintIndex, 0, v.paintColors.Length - 1)];
                        if (Mathf.Abs(c.r - want.r) + Mathf.Abs(c.g - want.g) + Mathf.Abs(c.b - want.b) > 0.01f) paintMismatch++;
                    }
                }
            }
            if (Time.frameCount % 3 != 0) continue;
            var f = v.Follower;
            if (f.lane == null || f.Distance < f.axleHalfSpan + 0.5f || f.Distance > f.lane.Length - f.axleHalfSpan - 0.5f) continue;
            foreach (var w in f.wheels)
            {
                // (d) tekerlek tabanı yolda: merkez − yarıçap ↔ yol yüzeyi (SM_B1_Road)
                if (RoadBelow(w.position, out float roadY))
                {
                    float err = (w.position.y - f.wheelRadius) - roadY;
                    wheelSamples++;
                    if (Mathf.Abs(err) > Mathf.Abs(worstWheel)) { worstWheel = err; worstWheelInfo = v.name + "/" + w.name + " s " + f.Distance.ToString("F1", Inv) + " (" + f.lane.name + ")"; }
                }
                // tekerlek ekseni ve dönüş yönü (araç uzayında)
                Quaternion inv = Quaternion.Inverse(v.transform.rotation);
                if (!wheelTracks.TryGetValue(w, out WheelTrack tr))
                {
                    tr = new WheelTrack { axleLocal = Quaternion.Inverse(w.rotation) * v.transform.right, upLocal = Quaternion.Inverse(w.rotation) * v.transform.up, prevS = f.Distance };
                    tr.prevUp = inv * (w.rotation * tr.upLocal);
                    wheelTracks[w] = tr;
                    continue;
                }
                Vector3 axle = inv * (w.rotation * tr.axleLocal);
                axisSamples++;
                if (Vector3.Dot(axle, Vector3.right) < 0.999f) { axisBad++; if (axisInfo.Length == 0) axisInfo = v.name + "/" + w.name + " aks " + V(axle); }
                Vector3 up = inv * (w.rotation * tr.upLocal);
                float expected = (f.Distance - tr.prevS) / Mathf.Max(0.05f, f.wheelRadius) * Mathf.Rad2Deg;
                if (expected > 2f && expected < 90f)
                {
                    float got = Vector3.SignedAngle(tr.prevUp, up, Vector3.right);
                    senseSamples++;
                    if (got <= 0f || Mathf.Abs(got - expected) > expected * 0.1f + 0.5f) { senseBad++; if (axisInfo.Length == 0) axisInfo = v.name + "/" + w.name + " dönüş " + got.ToString("F1", Inv) + "° beklenen " + expected.ToString("F1", Inv) + "°"; }
                }
                tr.prevUp = up;
                tr.prevS = f.Distance;
            }
        }
    }

    private static bool RoadBelow(Vector3 p, out float y)
    {
        y = 0f;
        var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<LaneFollower>() != null) continue;
            if (!h.transform.name.StartsWith("SM_B1_Road") && (h.transform.parent == null || !h.transform.parent.name.StartsWith("SM_B1_Road"))) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        return best < float.MaxValue;
    }

    // ---- ambulans takibi (şef kararı 7) ----
    private static AmbulanceController ambWatch;
    private static TrafficVehicle ambLeader;
    private static float ambMinGap = float.PositiveInfinity;
    private static int ambOverlap, ambFollowFrames, hudChecks, hudBad;
    private static float est10 = -1f, maxEstimateLead;
    private static string hudInfo = "";

    private static void SampleAmbulance()
    {
        var a = ambWatch;
        if (a == null || ambLeader == null) return;
        var ba = a.GetComponent<BoxCollider>();
        var bb = ambLeader.GetComponent<BoxCollider>();
        if (Physics.ComputePenetration(ba, ba.transform.position, ba.transform.rotation, bb, bb.transform.position, bb.transform.rotation, out _, out float d) && d > 0.01f) ambOverlap++;
        if (a.Follower.lane == ambLeader.Follower.lane && ambLeader.Follower.Distance > a.Follower.Distance)
        {
            float gap = ambLeader.Follower.RearS - a.Follower.FrontS;
            ambMinGap = Mathf.Min(ambMinGap, gap);
            if (a.FollowingVehicle) ambFollowFrames++;
        }
        var m = Mission;
        if (a.Phase == AmbulancePhase.Approaching)
        {
            float est = m.EstimatedNextArrival;
            maxEstimateLead = Mathf.Max(maxEstimateLead, est - m.NextArrivalTime);
            if (est10 < 0f && est - m.Elapsed <= 10f) est10 = est;
            var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
            if (hud != null && est > m.NextArrivalTime + 2f && Time.frameCount % 5 == 0)
            {
                // HUD bu kareyi LateUpdate'te yazdı: önceki karenin tahminiyle ±1 sn
                int secs = Mathf.CeilToInt(Mathf.Max(0f, est - m.Elapsed));
                string want = "Ambulans: " + (secs / 60) + ":" + (secs % 60).ToString("00");
                int secsPlanned = Mathf.CeilToInt(Mathf.Max(0f, m.NextArrivalTime - m.Elapsed));
                string planned = "Ambulans: " + (secsPlanned / 60) + ":" + (secsPlanned % 60).ToString("00");
                string got = hud.TimerText.text;
                hudChecks++;
                bool near = got == want || Math.Abs(ParseTimer(got) - secs) <= 1;
                if (!near || got == planned) { hudBad++; if (hudInfo.Length == 0) hudInfo = "HUD \"" + got + "\", tahmin \"" + want + "\", plan \"" + planned + "\""; }
                else if (hudInfo.Length == 0) hudInfo = "örnek: HUD \"" + got + "\" (plan olsaydı \"" + planned + "\")";
            }
        }
    }

    private static int ParseTimer(string s)
    {
        try
        {
            var t = s.Replace("Ambulans: ", "").Split(':');
            return int.Parse(t[0], Inv) * 60 + int.Parse(t[1], Inv);
        }
        catch { return -999; }
    }

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static Vector3 parkPosition;
    private static GameObject mateGo;
    private static float refAnkle = -1f;

    private static IEnumerator Scenario()
    {
        yield return null;
        player = GameObject.Find(Bolum1SceneBuilder.CharacterName).GetComponent<ThirdPersonPlayer>();
        Player.GetComponent<PlayerInputReader>().enabled = false;
        var mission = Mission;
        var traffic = Traffic;
        mission.missLimit = 999;
        mission.Begin(TestSchedule(99999f, 99999f)); // ambulans gelmesin (ambulans testinde yeniden)
        traffic.Begin(TestSettings(false));           // doğal trafik yok
        // D2: minibüs, halat, sırtta taşıma (taze sahnede), sonra eski fazlar için hastalar patikaya
        yield return PhaseE2IndicatorInVehicle(); // E2: hastalar minibüsteyken gösterge
        yield return PhaseRescue();
        yield return PhaseU1(); // U1: halat yakınlığı/yönü, beyaz vurgu, sırttan bırakma
        Bolum1TestPatients.LayOnPath(mission.patients);
        var tourist = PatientNamed("Tourist");
        parkPosition = tourist.Position + new Vector3(-2.5f, 0f, -1.8f);
        ParkCarriersOffLane();
        yield return Seconds(1f);
        CheckE2IndicatorOutside(); // E2: dışarıdaki hastaların göstergesi başlarının üstünde
        refAnkle = AnkleHeight(Player.GetComponent<Animator>(), Player.transform.position);

        yield return PhaseCorrections();
        ParkCarriersOffLane();
        yield return PhasePatients();
        yield return PhaseStretcher();
        yield return PhaseE2Carry();
        yield return PhaseE3PatientOffset(); // E3: hasta ayak tarafına, ön taşıyıcının sırtına girmez
        yield return PhaseE4CarryGuard();    // E3/E4: bot dik banka tırmanmaz; oyuncu sedyeyi dik yüzeye çeviremez
        yield return PhaseE2RampStretcher();
        yield return PhaseE2Slope();
        yield return PhaseE2GlassRender();
        yield return PhaseCone();
        yield return PhaseJump();
        yield return PhaseHorn();
        yield return PhaseKnockdown();
        yield return PhaseBotHit();
        yield return PhaseRespawn();
        yield return PhaseVehicles();
        yield return PhaseBales();
        yield return PhaseAmbulanceFollow();
        yield return PhaseCelebrate();
    }

    private static TrafficSettings TestSettings(bool natural)
    {
        var s = UnityEngine.Object.Instantiate(Bolum1TrafficBuilder.Settings);
        s.useFixedSeed = true;
        s.seed = Seed;
        if (!natural)
        {
            s.calmSeconds = new Vector2(99999f, 99999f);
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

    // ---- (b7) hastalar: yerde Lying_HelpWave ↔ Lying_Groan (5–10 sn), Civilian askılı kol korunur ----
    private static IEnumerator PhasePatients()
    {
        var types = new[] { "Tourist", "Elder" };
        var waveSeen = types.ToDictionary(t => t, t => false);
        var groanSeen = types.ToDictionary(t => t, t => false);
        var switchTimes = types.ToDictionary(t => t, t => new List<float>());
        var lastWave = types.ToDictionary(t => t, t => PatientNamed(t).Waving);
        var civ = PatientNamed("Civilian");
        var civAnim = civ.animator;
        Transform[] arms = { civAnim.GetBoneTransform(HumanBodyBones.LeftUpperArm), civAnim.GetBoneTransform(HumanBodyBones.LeftLowerArm), civAnim.GetBoneTransform(HumanBodyBones.RightUpperArm), civAnim.GetBoneTransform(HumanBodyBones.RightLowerArm) };
        Transform[] body = { civAnim.GetBoneTransform(HumanBodyBones.Spine), civAnim.GetBoneTransform(HumanBodyBones.Head), civAnim.GetBoneTransform(HumanBodyBones.LeftUpperLeg) };
        var armStart = arms.Select(a => a.localRotation).ToArray();
        var bodyStart = body.Select(a => a.localRotation).ToArray();
        float armMax = 0f, bodyMax = 0f;
        bool civGroan = false;
        bool captured = false;
        float start = Time.time;
        Time.timeScale = 3f;
        yield return (Func<bool>)(() =>
        {
            foreach (var t in types)
            {
                var p = PatientNamed(t);
                var st = p.animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("HelpWave")) waveSeen[t] = true;
                if (st.IsName("Groan")) groanSeen[t] = true;
                if (p.Waving != lastWave[t]) { switchTimes[t].Add(Time.time); lastWave[t] = p.Waving; }
            }
            for (int i = 0; i < arms.Length; i++) armMax = Mathf.Max(armMax, Quaternion.Angle(armStart[i], arms[i].localRotation));
            for (int i = 0; i < body.Length; i++) bodyMax = Mathf.Max(bodyMax, Quaternion.Angle(bodyStart[i], body[i].localRotation));
            civGroan |= civAnim.layerCount > 1 && civAnim.GetCurrentAnimatorStateInfo(1).IsName("Groan") && civAnim.GetLayerWeight(1) > 0.99f;
            return Time.time - start > 32f;
        });
        Time.timeScale = 1f;
        // Görüntü: el sallayan bir hasta varken
        yield return (Func<bool>)(() => types.Any(t => PatientNamed(t).animator.GetCurrentAnimatorStateInfo(0).IsName("HelpWave")) || Time.time - start > 60f);
        var waver = types.Select(PatientNamed).FirstOrDefault(p => p.animator.GetCurrentAnimatorStateInfo(0).IsName("HelpWave"));
        if (waver != null)
        {
            yield return Seconds(0.6f);
            Vector3 c = Mission.patients.Aggregate(Vector3.zero, (acc, p) => acc + p.Position) / Mission.patients.Length;
            Vector3 w = waver.Position;
            Vector3 side = Vector3.Cross(Vector3.up, Flat(w - c).sqrMagnitude > 0.01f ? Flat(w - c).normalized : Vector3.forward);
            yield return Capture("hastalar_elsallama", w + Vector3.up * 2.2f + side * 2.6f + Flat(w - c).normalized * 1.2f, w + Vector3.up * 0.3f, 50f);
            captured = true;
        }
        var intervals = new List<float>();
        foreach (var t in types)
            for (int i = 1; i < switchTimes[t].Count; i++) intervals.Add(switchTimes[t][i] - switchTimes[t][i - 1]);
        bool intervalsOk = intervals.Count > 0 && intervals.All(x => x >= 5f - 0.05f && x <= 10f + 0.05f);
        Check("(b7) yerdeki hastalar Lying_HelpWave ↔ Lying_Groan (rastgele 5–10 sn); Civilian askılı kol korunuyor (Lying_Groan kolsuz maskeyle)",
            types.All(t => waveSeen[t] && groanSeen[t]) && intervalsOk && civGroan && armMax < 1f && bodyMax > 2f && captured,
            string.Join(", ", types.Select(t => t + " el sallama " + waveSeen[t] + "/inleme " + groanSeen[t])) + "; geçiş aralıkları " + RangeText(intervals) + " sn"
            + "; Civilian: üst katman Groan " + civGroan + ", kol kemiklerinin en büyük değişimi " + armMax.ToString("F2", Inv) + "°, gövde/kafa/bacak " + bodyMax.ToString("F1", Inv) + "°; görüntü " + captured);
    }

    // ---- (b1) sedye: PickUp → Carry_Stretcher_Walk (oyuncu + bot), bırakınca PutDown; hastalar sedyede Lying_Groan ----
    private static IEnumerator PhaseStretcher()
    {
        var bot = Bot;
        bot.assistEnabled = true;
        var tourist = PatientNamed("Tourist");
        var s1 = Stretcher(0);
        yield return PutPatientOnStretcher(tourist, s1);
        yield return Seconds(0.7f);
        bool groanOnStretcher = tourist.State == PatientState.OnStretcher && !tourist.Waving && tourist.animator.GetCurrentAnimatorStateInfo(0).IsName("Groan");
        // Sedyeyi açık patikaya döndür, oyuncu arka sapa
        TeleportPlayerToHandle(s1, false);
        yield return null; yield return null;
        Interaction.Interact();
        yield return null; yield return null;
        bool pickUp = PlayerAnim.InState(CrewAnimation.StatePickUp);
        bool botPickUp = BotAnim.InState(CrewAnimation.StatePickUp);
        yield return Seconds(1.2f);
        bool carryIdle = PlayerAnim.InState(CrewAnimation.StateCarryStretcher) && BotAnim.InState(CrewAnimation.StateCarryStretcherFront); // E2: bot ön uçta
        bool raised = s1.IsRaised;
        // Yürü: kameranın baktığı yöne (girdi kamera göreli) 2.6 sn; hız oturunca (0.8 sn) ayak kayması ölçülür
        var cam = Player.cameraTransform;
        Player.SetMoveInput(new Vector2(0f, 1f));
        var meter = new FootSlideMeter(Player.animator, Player.transform);
        float t0 = Time.time;
        bool walkState = false;
        bool capturedCarry = false;
        float maxSpeed = 0f;
        int guardBlocks0 = Player.CarryTurnBlocks + Player.CarryMoveBlocks; // E4: patikada yanlış engelleme ölçümü
        string guardReason = "";
        while (Time.time - t0 < 2.6f)
        {
            yield return null;
            if (Player.CarryBlockedNow && guardReason == "") guardReason = Player.LastCarryBlockReason + " @" + V(Player.transform.position);
            walkState |= PlayerAnim.InState(CrewAnimation.StateCarryStretcher) && Player.animator.GetFloat("Speed") > 1.0f;
            if (Time.time - t0 > 0.8f)
            {
                meter.Sample();
                maxSpeed = Mathf.Max(maxSpeed, Player.HorizontalVelocity.magnitude);
            }
            if (!capturedCarry && Time.time - t0 > 1.4f)
            {
                meter.Break();
                capturedCarry = true;
                Vector3 c = s1.transform.position;
                Vector3 fwd = Flat(Player.transform.forward).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                yield return Capture("anim_tasima", c + side * 4.2f + fwd * 2.2f + Vector3.up * 1.8f, c + Vector3.up * 0.5f, 50f);
            }
        }
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.6f);
        bool carryAfter = PlayerAnim.InState(CrewAnimation.StateCarryStretcher);
        Interaction.Interact(); // bırak (bot da bırakır)
        yield return null; yield return null;
        bool putDown = PlayerAnim.InState(CrewAnimation.StatePutDown);
        bool botPutDown = BotAnim.InState(CrewAnimation.StatePutDown);
        yield return Seconds(1.2f);
        bool backToLoco = PlayerAnim.InState(CrewAnimation.StateLocomotion);
        Check("(b1) sedye al → PickUp sonra Carry_Stretcher_Walk (oyuncu ve bot; hız 0'da yavaşlatılmış), bırak → PutDown → hareket; sedyedeki hasta Lying_Groan",
            pickUp && botPickUp && carryIdle && raised && walkState && carryAfter && putDown && botPutDown && backToLoco && groanOnStretcher && capturedCarry,
            "PickUp oyuncu/bot " + pickUp + "/" + botPickUp + "; taşıma beklemesi " + carryIdle + " (sedye kalktı " + raised + "); yürürken CarryStretcher " + walkState
            + "; durunca " + carryAfter + "; PutDown oyuncu/bot " + putDown + "/" + botPutDown + "; sonra Locomotion " + backToLoco + "; sedyedeki hasta Groan " + groanOnStretcher);
        CheckFootSlide("(b1s) sedye taşırken hız " + Player.carryStretcherSpeed.ToString("F1", Inv) + " m/sn ve ayak kayması", meter,
            Mathf.Abs(maxSpeed - Player.carryStretcherSpeed) < 0.15f, "en yüksek yatay hız " + maxSpeed.ToString("F2", Inv) + " m/sn; E4 yön kilidi engelleme "
            + (Player.CarryTurnBlocks + Player.CarryMoveBlocks - guardBlocks0) + " kare" + (guardReason != "" ? " (ilk: " + guardReason + ")" : ""));
        // hasta yere geri (sonraki testler için)
        yield return Seconds(0.5f);
    }

    // ---- (b2) koni: PickUp → Carry_OneHand_Walk, koyunca PutDown ----
    private static IEnumerator PhaseCone()
    {
        var bot = Bot;
        bot.assistEnabled = false;
        ParkBot();
        var lane = Traffic.eastBound;
        float s0 = lane.Length * 0.3f;
        Vector3 basePt = lane.PointAt(s0);
        Vector3 fwd = Flat(lane.TangentAt(s0)).normalized;
        var c3 = Cone(2);
        c3.PlaceAt(basePt + fwd * 0.9f, 0f);
        Player.Teleport(basePt + Vector3.up * 0.02f, Quaternion.LookRotation(fwd));
        yield return Seconds(0.3f);
        Interaction.Interact();
        yield return null; yield return null;
        bool pickUp = PlayerAnim.InState(CrewAnimation.StatePickUp) && Player.HeldCone == c3;
        yield return Seconds(1.2f);
        bool carry = PlayerAnim.InState(CrewAnimation.StateCarryCone);
        Player.SetMoveInput(new Vector2(0f, 1f));
        var meter = new FootSlideMeter(Player.animator, Player.transform);
        float maxSpeed = 0f;
        float tw = Time.time;
        while (Time.time - tw < 1.6f)
        {
            yield return null;
            if (Time.time - tw > 0.6f)
            {
                meter.Sample();
                maxSpeed = Mathf.Max(maxSpeed, Player.HorizontalVelocity.magnitude);
            }
        }
        bool walk = PlayerAnim.InState(CrewAnimation.StateCarryCone) && Player.animator.GetFloat("Speed") > 1.5f;
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.5f);
        Interaction.Interact();
        yield return null; yield return null;
        bool putDown = PlayerAnim.InState(CrewAnimation.StatePutDown) && Player.HeldCone == null;
        yield return Seconds(1.2f);
        bool loco = PlayerAnim.InState(CrewAnimation.StateLocomotion);
        Check("(b2) koni al → PickUp sonra Carry_OneHand_Walk; koy → PutDown → hareket", pickUp && carry && walk && putDown && loco,
            "PickUp " + pickUp + ", CarryCone (beklerken " + carry + ", yürürken " + walk + "), PutDown " + putDown + ", sonra Locomotion " + loco);
        CheckFootSlide("(b2s) koni taşırken hız " + Player.moveSpeed.ToString("F1", Inv) + " m/sn ve ayak kayması", meter,
            Mathf.Abs(maxSpeed - Player.moveSpeed) < 0.15f, "en yüksek yatay hız " + maxSpeed.ToString("F2", Inv) + " m/sn");
        c3.PlaceAt(parkPosition + new Vector3(2f, 0f, 0f), 0f);
    }

    // ---- (b3) zıplama ----
    private static IEnumerator PhaseJump()
    {
        ParkCarriersOffLane();
        yield return Seconds(0.5f);
        float y0 = Player.transform.position.y;
        Player.RequestJump();
        yield return null; yield return null;
        bool jump = PlayerAnim.InState(CrewAnimation.StateJump);
        float maxY = y0;
        float t0 = Time.time;
        yield return (Func<bool>)(() => { maxY = Mathf.Max(maxY, Player.transform.position.y); return Time.time - t0 > 1.2f; });
        bool back = PlayerAnim.InState(CrewAnimation.StateLocomotion);
        Check("(b3) Space → Jump (yükseklik fizikten, klip ayaklara göre)", jump && back && maxY - y0 > 0.5f,
            "Jump durumu " + jump + ", kök yükseldi " + M(maxY - y0) + ", sonra Locomotion " + back);
    }

    // ---- (b4) korna 6 m içinde → Startled; 6 m dışında ve taşırken yok ----
    private static IEnumerator PhaseHorn()
    {
        var traffic = Traffic;
        var lane = traffic.westBound;
        float s0 = lane.Length * 0.5f;
        // Oyuncu şeridin dışında, banket tarafında (aracın yolunda değil)
        Vector3 p = lane.PointAt(s0) - lane.RightAt(s0) * (lane.width * 0.5f + 1.6f);
        if (GroundProbe.Find(p + Vector3.up * 2f, 6f, out RaycastHit g)) p.y = g.point.y + 0.02f;
        Player.Teleport(p, Quaternion.LookRotation(Flat(lane.TangentAt(s0)).normalized));
        TrafficVehicle v = null;
        yield return Until(() => v != null || (v = traffic.ForceSpawn(false, TrafficVehicleType.Sedan)) != null, 10f, "korna aracı");
        Time.timeScale = 2f;
        // 6 m dışında korna: irkilme yok
        yield return (Func<bool>)(() => v == null || Vector3.Distance(v.transform.position, Player.transform.position) < 12f);
        Time.timeScale = 1f;
        int before = PlayerAnim.Startles;
        v.sound.PlayHorn();
        yield return null; yield return null;
        bool farNo = PlayerAnim.Startles == before && !PlayerAnim.InState(CrewAnimation.StateStartled);
        // İkinci araç: korna bekleme süresi dolmuş, 5 m içine gelince çalar
        TrafficVehicle vb = null;
        yield return Until(() => vb != null || (vb = traffic.ForceSpawn(false, TrafficVehicleType.PanelVan)) != null, 15f, "korna aracı (yakın)");
        Time.timeScale = 2f;
        yield return (Func<bool>)(() => vb == null || Vector3.Distance(vb.transform.position, Player.transform.position) < 5f);
        Time.timeScale = 1f;
        v = vb;
        float dist = v != null ? Vector3.Distance(v.transform.position, Player.transform.position) : -1f;
        v.sound.PlayHorn();
        yield return null; yield return null;
        bool startled = PlayerAnim.Startles == before + 1 && PlayerAnim.InState(CrewAnimation.StateStartled);
        yield return Seconds(0.25f);
        Vector3 pp = Player.transform.position;
        Vector3 vp = v != null ? v.transform.position : pp;
        Vector3 fwdL = Flat(lane.TangentAt(s0)).normalized;
        yield return Capture("anim_irkilme", pp - lane.RightAt(s0) * 3.4f - fwdL * 1.8f + Vector3.up * 1.6f, (pp * 0.7f + vp * 0.3f) + Vector3.up * 0.7f, 50f);
        yield return Seconds(1.2f);
        bool back = PlayerAnim.InState(CrewAnimation.StateLocomotion);
        // Taşırken: koni elde → irkilme atlanır
        var c4 = Cone(3);
        c4.PlaceAt(Player.transform.position + Player.transform.forward * 0.8f, 0f);
        yield return Seconds(0.3f);
        Interaction.Interact();
        yield return Seconds(1.3f);
        int b2 = PlayerAnim.Startles;
        TrafficVehicle v2 = null;
        yield return Until(() => v2 != null || (v2 = traffic.ForceSpawn(false, TrafficVehicleType.Hatchback)) != null, 10f, "korna aracı 2");
        Time.timeScale = 2f;
        yield return (Func<bool>)(() => v2 == null || Vector3.Distance(v2.transform.position, Player.transform.position) < 5f);
        Time.timeScale = 1f;
        if (v2 != null) { yield return Seconds(1.6f); v2.sound.PlayHorn(); }
        yield return null; yield return null;
        bool carryingNo = PlayerAnim.Startles == b2 && Player.HeldCone != null && !PlayerAnim.InState(CrewAnimation.StateStartled);
        Interaction.Interact(); // koniyi koy
        yield return Seconds(1.2f);
        c4.PlaceAt(parkPosition + new Vector3(2.8f, 0f, 0f), 0f);
        Check("(b4) korna 6 m içinde → Startled (tam beden); 6 m dışında ve elde koni varken irkilme yok (sade seçim: atla)",
            farNo && startled && back && carryingNo,
            "12 m'de korna → irkilme yok " + farNo + "; " + M(dist) + "'de korna → Startled " + startled + ", sonra Locomotion " + back + "; koni elde korna → irkilme yok " + carryingNo);
        Time.timeScale = 4f;
        yield return Until(() => traffic.Vehicles.Count == 0, 60f, "korna araçları kayboldu");
        Time.timeScale = 1f;
    }

    // ---- (b5)+(c) ragdoll → bot kaldırır (Revive_Helper) → GetUp_FromBack (yumuşak geçiş) → ayaklar zeminde, kontrol geri ----
    private static IEnumerator PhaseKnockdown()
    {
        var bot = Bot;
        bot.assistEnabled = true;
        ParkCarriersOffLane();
        // Düz yolda (trafik yok): ayakta referans ayak yüksekliği, sonra arkaya doğru itkiyle düşür
        var laneK = Traffic.eastBound;
        float sK = laneK.Length * 0.3f;
        Player.Teleport(laneK.PointAt(sK) + Vector3.up * 0.05f, Quaternion.LookRotation(Flat(laneK.TangentAt(sK)).normalized));
        yield return Seconds(1.0f);
        refAnkle = FootClearance(Player.GetComponent<Animator>());
        float refRoot = GroundError(Player.transform.position);
        var kd = Player.knockdown;
        Player.KnockDown(-Player.transform.forward * 3f + Vector3.up * 1.5f);
        yield return Seconds(0.1f);
        bool down = Player.IsDown;
        yield return Until(() => kd.ReviveProgress > 0f || !Player.IsDown, 6f, "bot kaldırmaya başladı");
        yield return Seconds(0.3f);
        bool botRevive = BotAnim.InState(CrewAnimation.StateRevive);
        Vector3 hips = kd.GroundPosition;
        Vector3 bp = bot.transform.position;
        Vector3 side = Vector3.Cross(Vector3.up, Flat(hips - bp).normalized);
        yield return Capture("anim_kaldirma", (hips + bp) * 0.5f + side * 3.0f + Vector3.up * 1.6f, (hips + bp) * 0.5f + Vector3.up * 0.3f, 50f);
        yield return Until(() => !Player.IsDown, 6f, "bot kaldırdı");
        float standT = Time.time;
        Vector3 hipsAtStand = kd.hips.position;
        yield return null;
        bool getUp = PlayerAnim.InState(CrewAnimation.StateGetUp);
        // Yumuşak geçiş: ilk karede kalça ragdoll yerinde (≤ 5 cm)
        float hipJump = Vector3.Distance(kd.hips.position, hipsAtStand);
        bool noControl = !Player.HasControl;
        float controlAt = -1f;
        EditorApplication.CallbackFunction watch = () => { if (controlAt < 0f && EditorApplication.isPlaying && Player.HasControl) controlAt = Time.time; };
        EditorApplication.update += watch;
        yield return Seconds(0.9f);
        bool botLeft = !BotAnim.InState(CrewAnimation.StateRevive);
        yield return (Func<bool>)(() => Player.HasControl || Time.time - standT > 4f);
        EditorApplication.update -= watch;
        float controlAfter = (controlAt > 0f ? controlAt : Time.time) - standT;
        yield return Seconds(0.5f);
        var anim = Player.GetComponent<Animator>();
        float ankle = FootClearance(anim);
        float rootErr = GroundError(Player.transform.position);
        var hb = anim.GetBoneTransform(HumanBodyBones.Hips);
        string legDiag = "kalça kökten " + M(hb.position.y - Player.transform.position.y) + ", sol/sağ ayak kökten " + M(anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y - Player.transform.position.y)
            + "/" + M(anim.GetBoneTransform(HumanBodyBones.RightFoot).position.y - Player.transform.position.y);
        bool loco = PlayerAnim.InState(CrewAnimation.StateLocomotion);
        float getUpLen = PlayerAnim.getUpSeconds;
        Check("(b5) düşen oyuncuyu bot kaldırıyor: botta Revive_Helper; kalkışta GetUp_FromBack, ragdoll pozundan yumuşak geçiş",
            down && botRevive && getUp && hipJump <= 0.05f && botLeft && loco,
            "düştü " + down + "; bot Revive_Helper " + botRevive + " (bota göğse " + M(Vector3.Distance(Flat(bp), Flat(kd.ChestPosition))) + "); kalkış GetUp " + getUp
            + ", ilk karede kalça sıçraması " + M(hipJump) + "; bot kaldırma bitince Revive_Helper'dan çıktı " + botLeft + "; sonra Locomotion " + loco);
        Check("(c) GetUp sonunda ayaklar zeminde (±5 cm) ve kontrol geri geliyor (kalkış süresince yok)",
            Mathf.Abs(ankle - refAnkle) <= 0.05f && Mathf.Abs(rootErr - refRoot) <= 0.05f && noControl && Mathf.Abs(controlAfter - getUpLen) <= 0.2f,
            "bileğin altındaki zemine yüksekliği " + M(ankle) + " (aynı yolda ayakta referans " + M(refAnkle) + ", fark " + ((ankle - refAnkle) * 100f).ToString("F1", Inv) + " cm), kök–zemin "
            + (rootErr * 100f).ToString("F1", Inv) + " cm (referans " + (refRoot * 100f).ToString("F1", Inv) + " cm); kalkış sırasında kontrol yok " + noControl + ", kontrol " + S(controlAfter) + " sonra geldi (klip " + S(getUpLen) + "); " + legDiag);

        // Arkadaş (ikinci oyuncu) kaldırırken de Revive_Helper
        bot.assistEnabled = false;
        ParkBot();
        Player.KnockDown(-Player.transform.forward * 2f + Vector3.up * 1f);
        yield return Seconds(1.5f);
        if (mateGo == null)
        {
            mateGo = UnityEngine.Object.Instantiate(Player.gameObject);
            mateGo.name = "TestMate";
            mateGo.GetComponent<PlayerInputReader>().enabled = false;
        }
        mateGo.SetActive(true);
        var mate = mateGo.GetComponent<ThirdPersonPlayer>();
        if (mate.IsDown) mate.knockdown.ResetStanding();
        var mateI = mateGo.GetComponent<PlayerInteraction>();
        var mateA = mateGo.GetComponent<CrewAnimation>();
        hips = kd.GroundPosition;
        Vector3 mp = kd.ChestPosition + Vector3.Cross(Vector3.up, Flat(kd.ChestPosition - hips).sqrMagnitude > 0.01f ? Flat(kd.ChestPosition - hips).normalized : Vector3.forward) * 0.6f;
        if (GroundProbe.Find(mp + Vector3.up * 1.5f, 5f, out RaycastHit g)) mp.y = g.point.y + 0.02f;
        mate.Teleport(mp, Quaternion.LookRotation(Flat(kd.ChestPosition - mp).normalized));
        yield return null; yield return null;
        // U1: arkadaşın ipucunda düşmüş oyuncu vurgulu
        var mateHl = mateGo.GetComponent<InteractionHighlight>();
        Check("(U1-2r) beyaz vurgu: kaldırma ipucunda düşmüş arkadaş vurgulu (skinned kopya ragdoll kemiklerini izler)",
            mateI.CurrentHint == PlayerInteraction.HintRevive && mateHl != null && mateHl.Current == kd.transform && mateHl.CopyCount > 0,
            "ipucu \"" + mateI.CurrentHint + "\", vurgu " + (mateHl != null && mateHl.Current != null ? mateHl.Current.name : "yok") + " (" + (mateHl != null ? mateHl.CopyCount : 0) + " kopya)");
        mateI.SetInteractHeld(true);
        yield return Seconds(0.8f);
        bool mateRevive = mateA.InState(CrewAnimation.StateRevive);
        yield return Until(() => !Player.IsDown, 4f, "arkadaş kaldırdı");
        mateI.SetInteractHeld(false);
        yield return Seconds(0.6f);
        bool mateBack = mateA.InState(CrewAnimation.StateLocomotion);
        mateGo.SetActive(false);
        yield return (Func<bool>)(() => Player.HasControl);
        Check("(b5b) arkadaş (ikinci oyuncu) E basılı tutarken Revive_Helper, bitince hareket", mateRevive && mateBack, "Revive_Helper " + mateRevive + ", sonra Locomotion " + mateBack);
        bot.assistEnabled = true;
    }

    // ---- (6) bot çarpılınca ragdoll, 3 sn sonra kendi GetUp_FromBack ile kalkar ----
    private static IEnumerator PhaseBotHit()
    {
        var traffic = Traffic;
        var lane = traffic.eastBound;
        var bot = Bot;
        bot.assistEnabled = true;
        ParkCarriersOffLane();
        float s0 = lane.Length * 0.55f;
        Vector3 p = lane.PointAt(s0) - lane.RightAt(s0) * 0.4f;
        bot.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-Flat(lane.TangentAt(s0)).normalized));
        yield return Seconds(0.2f);
        TrafficVehicle v = null;
        yield return Until(() => v != null || (v = traffic.ForceSpawn(true, TrafficVehicleType.Sedan)) != null, 10f, "bot aracı");
        int downs = bot.TimesDowned;
        Time.timeScale = 2f;
        yield return Until(() => bot.IsDown, 60f, "bot düştü");
        Time.timeScale = 1f;
        float downAt = bot.DownSince;
        bool ragdoll = bot.ragdollBodies.All(b => !b.isKinematic) && !bot.GetComponent<Animator>().enabled;
        Vector3 hips0 = bot.hips.position;
        yield return Seconds(1f);
        float flung = Vector3.Distance(bot.hips.position, hips0);
        bool stillDown = bot.IsDown;
        // Kalkış kabulünü ikinci bir çarpma veya korna ile yarışa sokma; ilk çarpma ve savrulma ölçüldü.
        traffic.ClearVehicles();
        yield return Until(() => !bot.IsDown, 5f, "bot kalktı");
        float upAfter = Time.time - downAt;
        yield return null;
        bool getUp = BotAnim.InState(CrewAnimation.StateGetUp);
        bool kinematic = bot.ragdollBodies.All(b => b.isKinematic);
        yield return Seconds(BotAnim.getUpSeconds + 0.4f);
        bool loco = BotAnim.InState(CrewAnimation.StateLocomotion);
        Check("(6) bot çarpılınca ragdoll olup düşüyor, 3 sn sonra kendi kendine GetUp_FromBack ile kalkıyor",
            bot.TimesDowned == downs + 1 && ragdoll && stillDown && Mathf.Abs(upAfter - bot.getUpDelay) <= 0.1f && getUp && kinematic && loco,
            "ragdoll " + ragdoll + ", 1 sn'de kalça " + M(flung) + " savruldu; kalkış düşmeden " + S(upAfter) + " sonra (beklenen " + S(bot.getUpDelay) + "), GetUp " + getUp + ", sonra Locomotion " + loco
            + " (Startled " + BotAnim.InState(CrewAnimation.StateStartled) + ", GetUp " + BotAnim.InState(CrewAnimation.StateGetUp) + ", araç " + traffic.Vehicles.Count + ")");
        Time.timeScale = 4f;
        yield return Until(() => traffic.Vehicles.Count == 0, 60f, "bot aracı kayboldu");
        Time.timeScale = 1f;
        ParkBot();
    }

    // ---- (8) yerdeki oyuncuyu araç iter; boşluğa düşen oyuncu PlayerSpawn'da yeniden doğar ----
    private static IEnumerator PhaseRespawn()
    {
        var traffic = Traffic;
        var lane = traffic.eastBound;
        var bot = Bot;
        bot.assistEnabled = false;
        ParkBot();
        var kd = Player.knockdown;
        var respawn = Player.GetComponent<PlayerFallRespawn>();
        // a) yerdeki oyuncu şeritte: araç durmaz (%40), gövdesiyle iter
        float s0 = lane.Length * 0.6f;
        Player.Teleport(lane.PointAt(s0) + Vector3.up * 0.05f, Quaternion.LookRotation(Flat(lane.TangentAt(s0)).normalized));
        yield return Seconds(0.2f);
        Player.KnockDown(Vector3.up * 0.5f);
        yield return Seconds(1.5f);
        Vector3 hips0 = kd.GroundPosition;
        TrafficVehicle v = null;
        yield return Until(() => v != null || (v = traffic.ForceSpawn(true, TrafficVehicleType.PanelVan)) != null, 10f, "itme aracı");
        Time.timeScale = 2f;
        float minSpeed = float.MaxValue;
        float pushStart = Time.time;
        yield return (Func<bool>)(() =>
        {
            if (v == null) return true;
            if (v.Follower.FrontS > lane.Project(hips0) - 3f) minSpeed = Mathf.Min(minSpeed, v.Follower.Speed);
            return v.Follower.RearS > lane.Project(kd.GroundPosition) + 2f || Time.time - pushStart > 60f;
        });
        Time.timeScale = 1f;
        float pushed = Vector3.Distance(Flat(kd.GroundPosition), Flat(hips0));
        bool stayedDown = Player.IsDown;
        Time.timeScale = 4f;
        yield return Until(() => traffic.Vehicles.Count == 0, 60f, "itme aracı kayboldu");
        Time.timeScale = 1f;

        // b) uçurum tarafı (EastBound'un sağı): yoldan dışarı doğru, altı boş (FallRespawnPlane'e kadar zemin yok) ilk nokta.
        //    Oyuncu oranın kenarında yerdeyken boşluğa itilir.
        if (Player.IsDown) kd.ResetStanding();
        int before = respawn.Respawns;
        Vector3 edge = Vector3.zero, outward = Vector3.zero;
        float voidAt = -1f;
        foreach (float sf in new[] { 0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f })
        {
            float s1 = lane.Length * sf;
            Vector3 o = Flat(lane.RightAt(s1)).normalized;
            Vector3 lastGround = Vector3.zero;
            bool haveGround = false;
            for (float d = 0f; d <= 90f; d += 1f)
            {
                Vector3 q = lane.PointAt(s1) + o * d + Vector3.up * 20f;
                bool hit = GroundProbe.Find(q, 20f - respawn.killBelowY + 20f, out RaycastHit h) && h.point.y > respawn.killBelowY;
                if (hit) { lastGround = h.point; haveGround = true; continue; }
                if (haveGround) { edge = lastGround + Vector3.up * 0.05f; outward = o; voidAt = d; }
                break;
            }
            if (voidAt > 0f) break;
        }
        if (voidAt < 0f) throw new Exception("(8) uçurum tarafında altı boş nokta bulunamadı");
        Player.Teleport(edge - outward * 0.3f, Quaternion.LookRotation(outward));
        yield return Seconds(0.2f);
        Player.KnockDown(outward * 5f + Vector3.up * 2f);
        Time.timeScale = 3f;
        float t0 = Time.time;
        float minY = float.MaxValue;
        yield return (Func<bool>)(() => { minY = Mathf.Min(minY, kd.GroundPosition.y); return respawn.Respawns > before || Time.time - t0 > 40f; });
        Time.timeScale = 1f;
        yield return Seconds(0.3f);
        float toSpawn = Vector3.Distance(Flat(Player.transform.position), Flat(respawn.spawnPoint.position));
        bool ok = respawn.Respawns == before + 1 && !Player.IsDown && Player.HasControl && toSpawn < 0.5f && Player.GetComponent<CharacterController>().enabled;
        Check("(8) araç yerdeki oyuncuyu itiyor (durmuyor); uçuruma düşen oyuncu FallRespawnPlane altında PlayerSpawn'da ayakta yeniden doğuyor",
            pushed >= 0.5f && stayedDown && minSpeed > 0f && ok,
            "yerdeki oyuncu " + M(pushed) + " itildi (aracın en düşük hızı " + (minSpeed < float.MaxValue ? minSpeed.ToString("F1", Inv) : "-") + " m/sn), yerde kaldı " + stayedDown
            + "; boşluk yolun " + voidAt.ToString("F0", Inv) + " m dışında (kenar y " + edge.y.ToString("F1", Inv) + "); düşüş: en alçak kalça y " + minY.ToString("F1", Inv) + " (düzlem " + respawn.killBelowY.ToString("F1", Inv) + "), yeniden doğma " + (respawn.Respawns - before)
            + ", PlayerSpawn'a " + M(toSpawn) + ", ayakta/kontrol " + (!Player.IsDown && Player.HasControl));
        ParkCarriersOffLane();
        Bot.assistEnabled = true;
    }

    // ---- (d) 5 dk sabit seed: gerçek 5 model, tekerlek tabanı yolda ±5 cm, 4 boya varyantı, tekerlek ekseni ----
    private static IEnumerator PhaseVehicles()
    {
        var mission = Mission;
        var traffic = Traffic;
        ParkCarriersOffLane();
        mission.Begin(TestSchedule(99999f, 99999f));
        traffic.Begin(TestSettings(true));
        seen.Clear(); typeCounts.Clear(); paintsSeen.Clear(); wheelTracks.Clear();
        simActive = true;
        Time.timeScale = 6f;
        bool captured = false;
        yield return (Func<bool>)(() => traffic.Elapsed >= SimSeconds - 10f || BothLanesVisible(out _, out _));
        if (BothLanesVisible(out TrafficVehicle e, out TrafficVehicle w))
        {
            Time.timeScale = 1f;
            Vector3 mid = (e.transform.position + w.transform.position) * 0.5f;
            Vector3 lt = Flat(traffic.eastBound.TangentAt(traffic.eastBound.Project(mid))).normalized;
            Vector3 right = traffic.eastBound.RightAt(traffic.eastBound.Project(mid));
            // Vadi tarafından (EastBound'un sağı), yükseltilmiş; dağ tarafındaki kayalar kadrajı kapatmasın
            yield return Capture("trafik_araclar", mid + right * 15f - lt * 6f + Vector3.up * 7f, mid + Vector3.up * 0.8f, 50f);
            captured = true;
            Time.timeScale = 6f;
        }
        yield return (Func<bool>)(() => traffic.Elapsed >= SimSeconds);
        Time.timeScale = 1f;
        simActive = false;
        bool allTypes = Bolum1TrafficBuilder.Types.All(t => typeCounts.ContainsKey(t));
        Check("(d) 5 dk sabit seed trafik: gerçek 5 model (yer tutucu yok), tekerlek tabanı yolda ±5 cm, binek 4 boya varyantının hepsi görüldü, tekerlekler aks ekseninde doğru yönde dönüyor",
            allTypes && placeholderVehicles == 0 && Mathf.Abs(worstWheel) <= 0.05f && wheelSamples > 100 && paintsSeen.Count == 4 && paintMismatch == 0
            && axisBad == 0 && senseBad == 0 && senseSamples > 50 && captured,
            seen.Count + " araç: " + string.Join(" ", Bolum1TrafficBuilder.Types.Select(t => t + ":" + (typeCounts.TryGetValue(t, out int n) ? n : 0))) + ", yer tutucu " + placeholderVehicles
            + "; tekerlek tabanı en büyük sapma " + (worstWheel * 100f).ToString("F1", Inv) + " cm (" + worstWheelInfo + ", " + wheelSamples + " örnek)"
            + "; boya varyantı " + paintsSeen.Count + "/4 (" + string.Join(",", paintsSeen.OrderBy(x => x)) + "), renk bloğu uyuşmazlığı " + paintMismatch
            + "; aks ekseni " + (axisSamples - axisBad) + "/" + axisSamples + ", dönüş yönü/miktarı " + (senseSamples - senseBad) + "/" + senseSamples + (axisInfo.Length > 0 ? " (" + axisInfo + ")" : "")
            + "; görüntü " + captured);
        traffic.Begin(TestSettings(false));
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
                if (Vector3.Distance(e.transform.position, w.transform.position) < 14f) { east = e; west = w; return true; }
            }
        }
        return false;
    }

    // ---- (5) pikap koniye çarpıp geçince balyalar kopar, Rigidbody (25 kg) olur ve savrulur; itilebilir; 60 sn / düzlem altında yok olur ----
    private static IEnumerator PhaseBales()
    {
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        var lane = traffic.westBound;
        var cone = Cone(0);
        if (cone == null) cone = Cone(1);
        float coneS = lane.Length * 0.5f;
        cone.PlaceAt(lane.PointAt(coneS), 0f);
        yield return Seconds(0.5f);
        TrafficVehicle v = null;
        yield return Until(() => v != null || (v = traffic.ForceSpawn(false, TrafficVehicleType.Pickup)) != null, 10f, "pikap");
        var cargo = v.GetComponent<HayCargo>();
        bool attachedAtStart = cargo.bales.All(b => b.parent != null && b.IsChildOf(v.transform) && b.GetComponent<Rigidbody>() == null);
        float ramAt = -1f;
        v.ConeRammed += (x, c, waited) => ramAt = Time.time;
        Time.timeScale = 4f;
        yield return Until(() => cargo.Detached, 80f, "balyalar koptu");
        Time.timeScale = 1f;
        float detachDelay = cargo.DetachTime - ramAt;
        var bales = cargo.Loose.ToList();
        bool physics = bales.Count == 2 && bales.All(b => b.Body != null && !b.Body.isKinematic && Mathf.Abs(b.Body.mass - 25f) < 0.01f && b.transform.parent == null);
        float launch = bales.Count > 0 ? bales.Max(b => b.Body.linearVelocity.magnitude) : 0f;
        var start = bales.Select(b => b.transform.position).ToList();
        yield return Seconds(2.5f);
        float flown = bales.Count > 0 ? bales.Select((b, i) => b != null ? Vector3.Distance(b.transform.position, start[i]) : 99f).Max() : 0f;
        // Oyuncu iter: yolda duran balyaya doğru yürür
        string baleDiag = string.Join(", ", bales.Select(b => b == null ? "yok" : V(b.transform.position) + " yol " + RoadBelow(b.transform.position, out _) + " zemin " + GroundProbe.Find(b.transform.position + Vector3.up * 0.2f, 1.5f, out _)));
        // Yerde duran (zemine oturmuş) balya: yolda ya da banket/patikada
        var target = bales.Where(b => b != null && GroundProbe.Find(b.transform.position + Vector3.up * 0.2f, 1.5f, out _))
            .OrderBy(b => RoadBelow(b.transform.position, out _) ? 0 : 1).FirstOrDefault();
        float pushedBy = 0f;
        int pushes = 0;
        bool hadTarget = target != null;
        if (hadTarget)
        {
            yield return Seconds(1.5f);
            Vector3 camF = Flat(Player.cameraTransform.forward).normalized;
            Vector3 bp = target.transform.position;
            Vector3 pp = bp - camF * 1.4f;
            if (GroundProbe.Find(pp + Vector3.up * 1.5f, 5f, out RaycastHit g)) pp.y = g.point.y + 0.02f;
            Player.Teleport(pp, Quaternion.LookRotation(camF));
            yield return Seconds(0.2f);
            Vector3 before = target.transform.position;
            Player.SetMoveInput(new Vector2(0f, 1f));
            yield return Seconds(2f);
            Player.SetMoveInput(Vector2.zero);
            yield return Seconds(0.5f);
            if (target != null) { pushedBy = Vector3.Distance(Flat(target.transform.position), Flat(before)); pushes = target.Pushes; }
        }
        ParkCarriersOffLane();
        // Ömür: 60 sn (ya da düzlem altı)
        var lifetimes = new List<string>();
        var alive = bales.Where(b => b != null).ToList();
        var lastY = alive.ToDictionary(b => b, b => b.transform.position.y);
        var spawnT = alive.ToDictionary(b => b, b => b.SpawnTime);
        bool lifeOk = true;
        Time.timeScale = 6f;
        float t0 = Time.time;
        yield return (Func<bool>)(() =>
        {
            foreach (var b in alive.ToList())
            {
                if (b != null) { lastY[b] = b.transform.position.y; continue; }
                float life = Time.time - spawnT[b];
                bool fell = lastY[b] < -50f;
                lifetimes.Add(fell ? "düzlem altı (" + life.ToString("F1", Inv) + " sn)" : life.ToString("F1", Inv) + " sn");
                if (!fell && Mathf.Abs(life - 60f) > 0.5f) lifeOk = false;
                alive.Remove(b);
            }
            return alive.Count == 0 || Time.time - t0 > 75f;
        });
        Time.timeScale = 1f;
        lifeOk &= alive.Count == 0;
        // Düzlem altı: yeni bir balya koparılıp düzlemin altına konur
        TrafficVehicle v2 = null;
        yield return Until(() => v2 != null || (v2 = traffic.ForceSpawn(false, TrafficVehicleType.Pickup)) != null, 10f, "pikap 2");
        var cargo2 = v2.GetComponent<HayCargo>();
        cargo2.Detach();
        var fallBale = cargo2.Loose[0];
        float killY = Player.GetComponent<PlayerFallRespawn>().killBelowY;
        fallBale.transform.position = new Vector3(fallBale.transform.position.x, killY - 1f, fallBale.transform.position.z);
        fallBale.Body.position = fallBale.transform.position;
        yield return Seconds(0.2f);
        bool killed = fallBale == null;
        Check("(5) pikap koniye çarpıp geçince 2 balya kopuyor (Rigidbody 25 kg, itkiyle savruluyor); yolda oyuncu itebiliyor; 60 sn sonra / düzlem altında yok oluyor",
            attachedAtStart && ramAt > 0f && detachDelay >= 0f && detachDelay <= 2.1f && physics && launch > 2f && flown > 1f && hadTarget && pushedBy > 0.3f && pushes > 0 && lifeOk && killed,
            "başta araca bağlı " + attachedAtStart + "; 20 sn kuralından " + S(detachDelay) + " sonra koptu; Rigidbody/25 kg " + physics + ", ilk hız " + launch.ToString("F1", Inv) + " m/sn, 2.5 sn'de " + M(flown)
            + "; oyuncu itti: " + (hadTarget ? M(pushedBy) + " (" + pushes + " temas)" : "yerde balya yok") + " [balyalar: " + baleDiag + "]" + "; ömürler " + string.Join(", ", lifetimes) + "; düzlem altına konan balya yok oldu " + killed);
        Time.timeScale = 4f;
        yield return Until(() => traffic.Vehicles.Count == 0, 80f, "pikaplar kayboldu");
        Time.timeScale = 1f;
        foreach (var b in LooseBale.All.ToList()) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
    }

    // ---- (7) ambulans aynı şeritteki aracın arkasında kalır (girmez); gecikmede HUD gerçek tahmini gösterir ----
    private static IEnumerator PhaseAmbulanceFollow()
    {
        var mission = Mission;
        var traffic = Traffic;
        traffic.Begin(TestSettings(false));
        ParkCarriersOffLane();
        var schedule = TestSchedule(45f, 99999f);
        mission.Begin(schedule);
        float spawnAt = mission.NextAmbulanceSpawnTime;
        Time.timeScale = 3f;
        yield return (Func<bool>)(() => mission.Elapsed >= spawnAt - 4f);
        Time.timeScale = 1f;
        TrafficVehicle truck = null;
        yield return Until(() => truck != null || (truck = traffic.ForceSpawn(true, TrafficVehicleType.BoxTruck)) != null, 5f, "ambulans önündeki kamyon");
        // Gecikme/HUD senaryosu kasıtlı yavaş lider ister; gerçek seyir hızı ayrı testte 10 m/sn doğrulanır.
        truck.Follower.cruiseSpeed = 7f;
        yield return Until(() => mission.Ambulances.Count > 0, 10f, "ambulans doğdu");
        ambWatch = mission.Ambulances[0];
        ambLeader = truck;
        ambMinGap = float.PositiveInfinity; ambOverlap = 0; ambFollowFrames = 0; hudChecks = 0; hudBad = 0; est10 = -1f; maxEstimateLead = 0f; hudInfo = "";
        Time.timeScale = 2f;
        yield return Until(() => ambWatch.Phase == AmbulancePhase.Waiting, 90f, "ambulans durağa vardı");
        Time.timeScale = 1f;
        float arrival = mission.ArrivalLog.Count > 0 ? mission.ArrivalLog[0] : -1f;
        float delay = arrival - schedule.firstArrival;
        float estErr = est10 > 0f ? arrival - est10 : float.NaN;
        var amb = ambWatch;
        ambWatch = null;
        Check("(7) ambulans aynı şeritteki aracı takip ediyor (girmiyor, en az boşluk ~4 m); varış gecikince HUD gerçek tahmini gösteriyor",
            ambOverlap == 0 && ambMinGap >= 3.8f && ambFollowFrames > 0 && delay > 2f && hudChecks > 0 && hudBad == 0 && !float.IsNaN(estErr) && Mathf.Abs(estErr) <= 2f,
            "test kamyonu önde (7 m/sn); ambulans–kamyon en küçük boşluk " + M(ambMinGap) + ", kesişen kare " + ambOverlap + ", takip " + ambFollowFrames + " kare; varış " + S(arrival)
            + " (plan " + S(schedule.firstArrival) + ", gecikme " + S(delay) + ", tahminin plandan en çok ilerisi " + S(maxEstimateLead) + "); HUD " + (hudChecks - hudBad) + "/" + hudChecks + " " + hudInfo
            + "; varıştan 10 sn önceki tahmin hatası " + (float.IsNaN(estErr) ? "-" : S(estErr)));
        // Ambulans gitsin, kamyon kaybolsun
        Time.timeScale = 4f;
        yield return Until(() => mission.Ambulances.Count == 0 && traffic.Vehicles.Count == 0, 120f, "ambulans ve kamyon kayboldu");
        Time.timeScale = 1f;
    }

    // ---- (b6) görev başarı → Celebrate (oyuncu ve bot) ----
    private static IEnumerator PhaseCelebrate()
    {
        var mission = Mission;
        ParkCarriersOffLane();
        Bot.transform.position = Player.transform.position + Player.transform.right * 1.2f;
        yield return Seconds(0.5f);
        // Hastalar ambulansta sayılır (yatma çerçevesi: geçici obje) ve görev başarı yolu çalışır.
        var frame = new GameObject("AnimVerify_AmbulanceFrame").transform;
        frame.position = parkPosition + Vector3.up * 30f;
        foreach (var p in mission.patients) p.LieOn(frame, PatientState.InAmbulance, 0f);
        var onLoaded = typeof(MissionDirector).GetMethod("OnPatientLoaded", BindingFlags.Instance | BindingFlags.NonPublic);
        onLoaded.Invoke(mission, new object[] { null, mission.patients[0] });
        yield return null; yield return null;
        bool cel = PlayerAnim.InState(CrewAnimation.StateCelebrate);
        bool botCel = BotAnim.InState(CrewAnimation.StateCelebrate);
        yield return Seconds(2.2f);
        bool back = PlayerAnim.InState(CrewAnimation.StateLocomotion);
        Check("(b6) görev başarı → Celebrate (oyuncu ve bot)", mission.Succeeded && cel && botCel && back,
            "görev başarılı " + mission.Succeeded + "; Celebrate oyuncu/bot " + cel + "/" + botCel + ", sonra Locomotion " + back);
    }

    // =====================================================================================
    // Yardımcılar
    // =====================================================================================
    private static float AnkleHeight(Animator a, Vector3 root)
    {
        float ground = root.y;
        if (GroundProbe.Find(root + Vector3.up * 1f, 3f, out RaycastHit g)) ground = g.point.y;
        float lf = a.GetBoneTransform(HumanBodyBones.LeftFoot).position.y, rf = a.GetBoneTransform(HumanBodyBones.RightFoot).position.y;
        return Mathf.Min(lf, rf) - ground;
    }

    // İki ayaktan alçak olanın bileğinin, kendi altındaki zemine yüksekliği (eğimden bağımsız).
    private static float FootClearance(Animator a)
    {
        float best = float.MaxValue;
        foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
        {
            Vector3 f = a.GetBoneTransform(b).position;
            if (GroundProbe.Find(f + Vector3.up * 0.6f, 2f, out RaycastHit g)) best = Mathf.Min(best, f.y - g.point.y);
        }
        return best;
    }

    private static float GroundError(Vector3 root)
    {
        if (GroundProbe.Find(root + Vector3.up * 1f, 3f, out RaycastHit g)) return root.y - g.point.y;
        return 99f;
    }

    private static IEnumerator PutPatientOnStretcher(Patient p, StretcherController s)
    {
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
        if (Player.IsDown) Player.knockdown.ResetStanding();
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
        var go = new GameObject("AnimVerify_CaptureCam");
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
        float prevScale = Time.timeScale;
        Time.timeScale = 0f; // görüntü alınırken poz donar
        yield return null;
        yield return null;
        cam.Render();
        Time.timeScale = prevScale;
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
        Check("(a0) log'da C# derleme hatası yok", clean, note);
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

    // ---- Taşırken ayak kayması ----
    // Ayak bileği kök yüksekliğine göre en alçak bandındayken (ContactBand) yere basıyor sayılır; her basışta bileğin
    // yatay dünya konumunun yayılımı (xz sınır kutusu köşegeni) bir "adım kayması"dır. Ölçümün başında ve
    // kesintilerde (Break) yarım kalan basışlar sayılmaz. Sınır: kaynak verify ile aynı, 3 cm/adım.
    private const float FootSlideLimit = 0.03f;

    private sealed class FootSlideMeter
    {
        private const float ContactBand = 0.015f;
        private readonly Transform root;
        private readonly Transform[] feet;
        private readonly List<Vector3>[] samples = { new List<Vector3>(), new List<Vector3>() };
        private readonly Animator animator;
        // Tanı: son kesintisiz ölçüm dilimindeki ortalama hız, yön sapması ve klibin gerçek oynatma hızı
        private float runT0 = -1f, runNt0, runT1, runNt1, runLen;
        private Vector3 runP0, runP1;
        public float MaxFacingError { get; private set; }

        public FootSlideMeter(Animator animator, Transform root)
        {
            this.root = root;
            this.animator = animator;
            feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
        }

        public void Sample()
        {
            for (int i = 0; i < 2; i++)
            {
                if (feet[i] == null) continue;
                Vector3 p = feet[i].position;
                samples[i].Add(new Vector3(p.x, p.y - root.position.y, p.z));
            }
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (runT0 < 0f) { runT0 = Time.time; runNt0 = info.normalizedTime; runP0 = root.position; }
            runT1 = Time.time; runNt1 = info.normalizedTime; runP1 = root.position; runLen = info.length;
            var move = Flat(runP1 - runP0);
            if (move.magnitude > 0.2f) MaxFacingError = Mathf.Max(MaxFacingError, Vector3.Angle(Flat(root.forward), move));
        }

        public void Break()
        {
            for (int i = 0; i < 2; i++) samples[i].Add(new Vector3(0f, float.NaN, 0f));
            runT0 = -1f;
        }

        public string Diagnostics()
        {
            float dt = runT1 - runT0;
            if (runT0 < 0f || dt <= 0f) return "tanı yok";
            float speed = Flat(runP1 - runP0).magnitude / dt;
            // info.length durumun (hız çarpanlı) bir döngü süresidir: oynatılan klip saniyesi / gerçek saniye
            float rate = (runNt1 - runNt0) * runLen / dt;
            return "ortalama hız " + speed.ToString("F2", Inv) + " m/sn, yön sapması en çok " + MaxFacingError.ToString("F0", Inv) + "°, döngü " + runLen.ToString("F2", Inv) + " sn, oran " + rate.ToString("F2", Inv);
        }

        public List<float> Slides()
        {
            var result = new List<float>();
            for (int i = 0; i < 2; i++)
            {
                var s = samples[i];
                var valid = s.Where(p => !float.IsNaN(p.y)).ToList();
                if (valid.Count < 3) continue;
                float lo = valid.Min(p => p.y);
                var seg = new List<Vector3>();
                bool started = false;
                foreach (var p in s)
                {
                    if (float.IsNaN(p.y)) { seg.Clear(); started = false; continue; }
                    if (p.y <= lo + ContactBand)
                    {
                        if (started) seg.Add(p);
                        continue;
                    }
                    if (seg.Count > 0) result.Add(Spread(seg));
                    seg.Clear();
                    started = true;
                }
            }
            return result;
        }

        private static float Spread(List<Vector3> seg)
        {
            float dx = seg.Max(p => p.x) - seg.Min(p => p.x);
            float dz = seg.Max(p => p.z) - seg.Min(p => p.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    private static void CheckFootSlide(string title, FootSlideMeter meter, bool speedOk, string speedText)
    {
        var slides = meter.Slides();
        slides.Sort();
        float worst = slides.Count > 0 ? slides[slides.Count - 1] : float.PositiveInfinity;
        float median = slides.Count > 0 ? slides[slides.Count / 2] : float.PositiveInfinity;
        Check(title + " < " + (FootSlideLimit * 100f).ToString("F0", Inv) + " cm/adım", speedOk && slides.Count >= 2 && worst < FootSlideLimit,
            speedText + " (" + meter.Diagnostics() + "); " + slides.Count + " basış, kayma ortanca " + (median * 100f).ToString("F1", Inv) + " cm, en kötü " + (worst * 100f).ToString("F1", Inv) + " cm");
    }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
    private static string M(float m) { return m.ToString("F2", Inv) + " m"; }
    private static string S(float s) { return s.ToString("F2", Inv) + " sn"; }
    private static string RangeText(List<float> xs) { return xs.Count == 0 ? "-" : xs.Min().ToString("F1", Inv) + "–" + xs.Max().ToString("F1", Inv) + " (n " + xs.Count + ")"; }
}
