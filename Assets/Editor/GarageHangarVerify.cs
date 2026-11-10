using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Kurtarma Hangarı kabul testi (desen: OfficeExpansionVerify — SessionState'li edit → play, sanal klavye).
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod GarageHangarVerify.Run -logFile x.log
// Başarıda "GARAGEHANGAR VERIFY OK" ve exit 0. Rapor: Captures/Hangar/hangar_verify_report.txt
// Edit: hangar içinde çevre parçası kalmadı, geçiş boş, makara taşındı, modeller ve ofis eklemeleri var, pembe malzeme yok,
//       ışıklar ve sonda var, üretilen meshler okunamaz. Play: oyuncu garajdan geçişe ve hangara, oradan doğu kapısından dışarı
//       yürür; kapalı güney kapısı geçirmez; uyuyan köpek yaklaşınca uyanır, E ile sevilir.
[InitializeOnLoad]
public static class GarageHangarVerify
{
    private const string OutDir = "Captures/Hangar";
    private const string StageKey = "GarageHangarVerify.Stage", ReportKey = "GarageHangarVerify.Report", OkKey = "GarageHangarVerify.Ok",
                         BatchKey = "GarageHangarVerify.Batch", SaveBackupKey = "GarageHangarVerify.SaveBackup";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk, batchMode;

    static GarageHangarVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play") EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Ofis-Garaj/Doğrula (Kurtarma Hangarı)")]
    public static void RunMenu() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Begin(false); }
    public static void Run() { Begin(true); }

    private static string SavePath => Path.Combine(Application.persistentDataPath, "office_garage_v1.json");

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder(); allOk = true; batchMode = batch;
        Line("GARAGEHANGAR VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            EditorSceneManager.OpenScene(GarageHangarBuilder.ScenePath, OpenSceneMode.Single);
            EditChecks();
            string backup = "";
            if (File.Exists(SavePath)) { backup = SavePath + ".hangarverify_bak"; File.Copy(SavePath, backup, true); File.Delete(SavePath); }
            SessionState.SetString(SaveBackupKey, backup);
        }
        catch (Exception e) { Fail("edit aşaması: " + e); Finish(); return; }
        SessionState.SetString(ReportKey, report.ToString());
        SessionState.SetBool(OkKey, allOk);
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetString(StageKey, "play");
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    // ------------------------------------------------------------------ edit aşaması

    private static void EditChecks()
    {
        const float X0 = GarageHangarBuilder.X0, X1 = GarageHangarBuilder.X1, Z0 = GarageHangarBuilder.Z0, Z1 = GarageHangarBuilder.Z1;
        var root = GameObject.Find(GarageHangarBuilder.RootName);
        Check("(a) hangar kökü ve gizlenen parça listesi var", root != null && File.Exists(GarageHangarBuilder.HiddenList),
            root != null ? File.ReadAllLines(GarageHangarBuilder.HiddenList).Length + " gizli parça" : "kök yok");
        if (root == null) return;
        var inside = new Bounds(new Vector3((X0 + X1) * 0.5f, GarageHangarBuilder.H * 0.5f + 0.05f, (Z0 + Z1) * 0.5f), new Vector3(X1 - X0 - 0.04f, GarageHangarBuilder.H - 0.1f, Z1 - Z0 - 0.04f));
        var env = GameObject.Find(GarageHangarBuilder.EnvRoot);
        var leftovers = env.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && r.bounds.Intersects(inside) && r.bounds.max.y > 0.05f && r.bounds.size.x < 30f && r.bounds.size.z < 30f && r.bounds.min.x > X0 - 0.05f).Select(r => r.name).ToList();
        Check("(b) hangar içinde çevre parçası kalmadı (ağaç, çalı, kaya, konteyner)", leftovers.Count == 0, leftovers.Count + " " + string.Join(", ", leftovers.Take(5)));

        var east = GameObject.Find("00_WorldCollision/Garage_East")?.GetComponent<BoxCollider>();
        Check("(c) Garage_East geçişin güneyinde biter", east != null && Mathf.Abs(east.bounds.max.z - GarageHangarBuilder.PZ0) < 0.03f, east != null ? "z max " + east.bounds.max.z.ToString("F2", Inv) : "yok");
        var blockers = new List<string>();
        for (float x = 8.15f; x <= 10.6f; x += 0.1f)
            foreach (var c in Physics.OverlapCapsule(new Vector3(x, 0.32f, 3.1f), new Vector3(x, 1.5f, 3.1f), 0.24f, ~0, QueryTriggerInteraction.Ignore))
                if (!c.name.StartsWith("Ground") && !blockers.Contains(c.name)) blockers.Add(c.name);
        Check("(d) geçiş boyunca (x 8,15–10,6, z 3,1) oyuncu kapsülü boş", blockers.Count == 0, blockers.Count == 0 ? "boş" : string.Join(", ", blockers));
        var reel = GameObject.Find("11_WorkshopInterior/WallAirHoseReel");
        float reelZ = reel != null ? reel.GetComponentsInChildren<Renderer>().Select(r => r.bounds.center.z).Average() : 0f;
        Check("(e) hava hortumu makarası geçişin dışında (z≈4,45)", reel != null && reelZ > 4.2f, "z " + reelZ.ToString("F2", Inv));

        string[] wanted = { "Kurtarma_Vani", "Drone_Istasyonu", "Ceki_Demiri", "Bot_Romorku", "Kurtarma_Botu", "Drone", "Drone_Bakimda", "Drone_Hazir",
                            "Can_Simidi", "Agac_Kayisi", "Kurtarma_Askisi", "Megafon", "Telsiz_Paketi", "CanYelegi_Paketi", "Ip_Makarasi",
                            "Yiyecek_Icecek_Seti", "BENCH_DRILL_PRESS", "CORDLESS_DRILL", "DRILL_CHARGER", "DRILL_BIT_CASE", "ANGLE_GRINDER", "Findik (kopek)" };
        var names = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        var missing = wanted.Where(w => !names.Any(n => n.StartsWith(w))).ToList();
        Check("(f) hangar modelleri yerinde (konvoy, drone'lar, kurtarma takımı, yiyecek, aletler, köpek)", missing.Count == 0, missing.Count == 0 ? wanted.Length + " öğe" : "eksik " + string.Join(", ", missing));
        var dressing = Resources.Load<GameObject>(OfficeInteriorMood.DressingResource);
        string[] office = { "Dekor_LASER_PRINTER_MFP", "Dekor_PIZZA_BOARD", "Dekor_PIZZA", "Dekor_COLA_CAN", "Dekor_COLA_GLASS", "Dekor_CHOCOLATE", "Dekor_BONBON_RED", "Dekor_BONBON_GOLD" };
        var dn = dressing != null ? dressing.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList() : new List<string>();
        var offMissing = office.Where(o => !dn.Contains(o)).ToList();
        Check("(g) ofis dekorunda yazıcı ve yiyecek-içecekler var", dressing != null && offMissing.Count == 0, offMissing.Count == 0 ? office.Length + " öğe" : "eksik " + string.Join(", ", offMissing));

        var bad = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials.Select(m => (r, m)))
            .Where(p => p.m == null || p.m.shader == null || !p.m.shader.isSupported || p.m.shader.name.Contains("InternalError")).Select(p => p.r.name).Distinct().ToList();
        Check("(h) pembe/eksik malzeme yok", bad.Count == 0, bad.Count == 0 ? "tamam" : string.Join(", ", bad.Take(6)));
        var lights = root.GetComponentsInChildren<Light>(true);
        var probe = root.GetComponentInChildren<ReflectionProbe>();
        Check("(i) hangar ışıkları ve pişmiş yansıma sondası", lights.Length >= 10 && probe != null && probe.customBakedTexture != null, lights.Length + " ışık, sonda " + (probe != null && probe.customBakedTexture != null));
        var readable = root.transform.Find("Yapi").GetComponentsInChildren<MeshFilter>(true).Count(f => f.sharedMesh != null && f.sharedMesh.isReadable);
        Check("(j) üretilen hangar meshleri okunamaz (statik birleştirmeye girmez)", readable == 0, readable + " okunabilir");
    }

    // ------------------------------------------------------------------ play aşaması (OfficeExpansionVerify altyapısı)

    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition;

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
            scenario = Scenario(); stack.Clear(); stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 420f) { Fail("zaman aşımı (7 dk)"); End(); return; }
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
        catch (Exception e) { Fail("play aşaması: " + e); End(); }
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
        string backup = SessionState.GetString(SaveBackupKey, "");
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) File.Move(backup, SavePath);
        }
        catch (Exception e) { Line("  UYARI: kayıt geri yüklenemedi: " + e.Message); }
        SessionState.EraseString(SaveBackupKey);
        Line(allOk ? "GARAGEHANGAR VERIFY OK" : "GARAGEHANGAR VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "hangar_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private static float Seconds(float s) => Time.time + s;

    private static ThirdPersonPlayer player;
    private static CompanyPlayerBridge bridge;
    private static PlayerInputReader reader;
    private static OrbitCamera orbit;
    private static Keyboard keyboard;

    private static IEnumerator Scenario()
    {
        yield return null;
        player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
        bridge = player.GetComponent<CompanyPlayerBridge>();
        reader = player.GetComponent<PlayerInputReader>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        float wait = Time.time + 3f;
        yield return (Func<bool>)(() => player.HasControl || Time.time > wait);
        yield return Seconds(0.5f);

        var settings = InputSystem.settings;
        var savedBehavior = settings.editorInputBehaviorInPlayMode;
        var savedBackground = settings.backgroundBehavior;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("HangarTestKeyboard");
        keyboard.MakeCurrent();
        bool savedReader = reader.enabled;
        reader.enabled = true;
        yield return null;
        try
        {
            // statik birleştirme hangara dokunmadı
            var root = GameObject.Find(GarageHangarBuilder.RootName);
            int batched = root.transform.Find("Yapi").GetComponentsInChildren<MeshRenderer>().Count(r => r.isPartOfStaticBatch);
            Check("(1) hangar meshleri çalışma anında statik birleştirmeye girmedi", batched == 0, batched + " birleşik");

            // (5) köpek: ilk uykusu 12–30 sn sürer (KopekDavranisi.Start); yürüyüşlerden önce sınanır, yoksa kendiliğinden uyanmış olabilir
            var dog = UnityEngine.Object.FindAnyObjectByType<KopekDavranisi>();
            Check("(5a) köpek Fındık uyuyor (Lie_Sleep)", dog != null && dog.IsSleeping && dog.CurrentClip == "Lie_Sleep", dog != null ? dog.CurrentClip + ", oyun süresi " + Time.timeSinceLevelLoad.ToString("F1", Inv) + " sn" : "yok");
            if (dog != null)
            {
                Vector3 dp = dog.transform.position;
                yield return Teleport(dp + new Vector3(0f, 0f, -1.35f), dp + Vector3.up * 0.3f);
                float until = Time.time + 1.5f;
                yield return (Func<bool>)(() => !dog.IsSleeping || Time.time > until);
                Check("(5b) oyuncu yaklaşınca köpek uyanır", !dog.IsSleeping, "klip " + dog.CurrentClip);
                yield return Seconds(1.5f);
                bool focused = false;
                foreach (float dist in new[] { 1.0f, 1.3f, 0.8f })
                {
                    Vector3 to = dog.transform.position + new Vector3(0f, 0f, -dist);
                    yield return Teleport(to, dog.transform.position + Vector3.up * 0.35f);
                    if (bridge.FindTarget() == dog) { focused = true; break; }
                }
                string prompt = focused ? dog.Prompt(bridge) : "";
                int before = dog.Pets;
                if (focused) yield return Press(Key.E);
                yield return Seconds(0.3f);
                Check("(5c) köpeğe bakınca \"E — Fındık'ı sev\" çıkar, E ile sevilir", focused && dog.Pets == before + 1, "hedef " + focused + ", ipucu '" + prompt + "', sevme " + dog.Pets);
                yield return CaptureMain("hv_02_kopek");
            }

            // (2) garajdan geçişe ve hangara yürüyüş
            bool a = false, b = false;
            yield return Teleport(new Vector3(7.3f, 0f, 2.75f), new Vector3(9.6f, 1f, 3.0f));
            yield return WalkTo(new Vector3(8.7f, 0f, 2.95f), 6f, r => a = r);
            yield return WalkTo(new Vector3(10.7f, 0f, 3.05f), 8f, r => b = r);
            Check("(2) garajdan doğu duvarındaki geçişle hangara yürünür", a && b && player.transform.position.x > 9.8f, "varış " + a + "/" + b + ", konum " + V(player.transform.position));
            yield return CaptureMain("hv_01_gecisten_hangar");

            // (3) hangarı boydan boya geçip açık doğu kapısından dışarı
            bool c1 = false, c2 = false, c3 = false;
            yield return WalkTo(new Vector3(15.5f, 0f, -2.65f), 14f, r => c1 = r);
            yield return WalkTo(new Vector3(30.6f, 0f, -2.65f), 24f, r => c2 = r);
            yield return WalkTo(new Vector3(33.6f, 0f, -2.2f), 8f, r => c3 = r);
            Check("(3) hangar boyunca yürünür, doğu kapısından dışarı çıkılır", c1 && c2 && c3 && player.transform.position.x > 32.3f, "varış " + c1 + "/" + c2 + "/" + c3 + ", konum " + V(player.transform.position));

            // (4) kapalı güney sarmal kapısı geçirmez
            yield return Teleport(new Vector3(14.5f, 0f, -4.2f), new Vector3(14.5f, 1f, -7f));
            bool through = false;
            yield return WalkTo(new Vector3(14.5f, 0f, -6.8f), 3f, r => through = r);
            Check("(4) kapalı güney kapısından geçilmez", !through && player.transform.position.z > -5.25f, "konum " + V(player.transform.position));
        }
        finally
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.SetMoveInput(Vector2.zero);
            reader.enabled = savedReader;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            settings.editorInputBehaviorInPlayMode = savedBehavior;
            settings.backgroundBehavior = savedBackground;
        }
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private static IEnumerator WalkTo(Vector3 target, float timeout, Action<bool> result)
    {
        float end = Time.time + timeout;
        bool arrived = false;
        while (Time.time < end)
        {
            Vector3 d = target - player.transform.position; d.y = 0f;
            if (d.magnitude < 0.25f) { arrived = true; break; }
            orbit.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; orbit.Pitch = 15f;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return null;
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null; yield return null;
        if (!arrived) Line("  yürüyüş hedefe ulaşmadı: hedef " + V(target) + ", konum " + V(player.transform.position));
        result(arrived);
    }

    private static IEnumerator Teleport(Vector3 position, Vector3 lookAt)
    {
        Vector3 flat = lookAt - position; flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f) flat = player.transform.forward;
        player.Teleport(position + Vector3.up * 0.02f, Quaternion.LookRotation(flat.normalized, Vector3.up));
        Vector3 pivot = position + Vector3.up * orbit.pivotHeight;
        Vector3 dir = (lookAt - pivot).normalized;
        orbit.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        orbit.Pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        yield return null; yield return null; yield return null;
    }

    private static IEnumerator CaptureMain(string name)
    {
        yield return Seconds(0.8f);
        var cam = Camera.main;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevTarget;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
        Line("  görüntü: " + OutDir + "/" + name + ".png");
    }

    private static string V(Vector3 v) => "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")";
    private static void Check(string label, bool ok, string detail) { allOk &= ok; Line((ok ? "[OK]   " : "[FAIL] ") + label + " — " + detail); }
    private static void Fail(string s) { allOk = false; Line("[FAIL] " + s); }
    private static void Line(string s) { report.AppendLine(s); }
}
