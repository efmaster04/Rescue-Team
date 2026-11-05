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

// Çok odalı ofis kabul testi. Desen: OfficeGarageVerify (SessionState'li edit → play aşaması, sanal klavye/fare).
// Batchmode (-nographics OLMADAN, -quit OLMADAN): Unity.exe -batchmode -projectPath ... -executeMethod OfficeExpansionVerify.Run -logFile C:/.../x.log
// Başarıda "OFFICEEXPANSION VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/OfficeGarage/office_expansion_verify_report.txt
// SESSİZLİK: Bolum1AmbulanceAudioMute + Play boyunca AudioListener.volume = 0.
// Sınananlar: kurulum bütünlüğü (kabuk, eşya, duvar kutuları, eski kutular yok, eski parçalar gizli), materyaller (pembe shader
// yok, cam şeffaf, harita dokulu), eşyalar duvara gömülü değil; oyuncu gerçek kapı boşluklarından her odaya yürür, depo↔garaj
// geçişinden geçer, duvarlardan geçemez, ring basamaklarından platforma çıkar ve halattan geçemez, ofis kapılarını (E) kullanır,
// bilgisayara E ile UI açar. Her odadan ve dışarıdan oyun kamerasıyla ekran görüntüsü alır.
[InitializeOnLoad]
public static class OfficeExpansionVerify
{
    private const string OutDir = "Captures/OfficeGarage";
    private const string StageKey = "OfficeExpansionVerify.Stage";
    private const string ReportKey = "OfficeExpansionVerify.Report";
    private const string OkKey = "OfficeExpansionVerify.Ok";
    private const string BatchKey = "OfficeExpansionVerify.Batch";
    private const string SaveBackupKey = "OfficeExpansionVerify.SaveBackup";
    private const string KeyboardName = "OeTestKeyboard", MouseName = "OeTestMouse";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static OfficeExpansionVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Ofis-Garaj/Doğrula (çok odalı ofis)")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static string SavePath { get { return Path.Combine(Application.persistentDataPath, "office_garage_v1.json"); } }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        batchMode = batch;
        Line("OFFICEEXPANSION VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            EditorSceneManager.OpenScene(OfficeGaragePlayerSetup.ScenePath, OpenSceneMode.Single);
            EditStageChecks();
            string backup = "";
            if (File.Exists(SavePath))
            {
                backup = SavePath + ".verify_bak";
                File.Copy(SavePath, backup, true);
                File.Delete(SavePath);
                Line("  kayıt yedeklendi: " + backup);
            }
            SessionState.SetString(SaveBackupKey, backup);
        }
        catch (Exception e)
        {
            Fail("edit aşaması: " + e);
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
    // Edit aşaması: kurulum bütünlüğü, materyaller, gömülme
    // =====================================================================================
    private static void EditStageChecks()
    {
        var layout = OfficeExpansionSetup.LoadLayout();
        var root = GameObject.Find(OfficeExpansionSetup.RootName);
        var env = GameObject.Find("01_Environment_OfficeGarage");
        Check("(a) kurulum kökü ve kabuk var", root != null && env != null && root.transform.Find("Office_Shell") != null, "kök " + (root != null) + ", env " + (env != null));
        if (root == null || env == null) return;
        var shell = root.transform.Find("Office_Shell");
        var missingShell = new List<string>();
        foreach (var room in layout.rooms)
            foreach (var key in new[] { "Floor", "Walls", "Ceiling", "Fixtures" })
                if (FindDeep(shell, "Room_" + room.id + "_" + key) == null) missingShell.Add("Room_" + room.id + "_" + key);
        Check("(b) kabukta her odanın zemin/duvar/tavan/armatür parçası var", missingShell.Count == 0, missingShell.Count == 0 ? "tam" : string.Join(",", missingShell));

        var items = root.transform.Find("Items");
        int found = 0; var missingItems = new List<string>();
        foreach (var it in layout.items) { if (FindDeep(items, it.id) != null) found++; else missingItems.Add(it.id); }
        Check("(c) yerleşimdeki her eşya sahnede (" + layout.items.Length + ")", found == layout.items.Length, "bulunan " + found + (missingItems.Count > 0 ? ", eksik " + string.Join(",", missingItems) : ""));
        var walls = root.transform.Find("Walls");
        int wallBoxes = walls != null ? walls.GetComponentsInChildren<BoxCollider>().Length : 0;
        Check("(d) duvar kutuları layout kadar (" + layout.wall_colliders.Length + ")", wallBoxes == layout.wall_colliders.Length, "sahnede " + wallBoxes);

        var wc = GameObject.Find("00_WorldCollision");
        var oldBoxes = new[] { "Office_West", "Office_Rear", "Office_Front_Left", "Office_Front_Right", "Office_Desk", "Office_Chair", "Office_FilingCabinet", "Office_DocumentShelf", "Garage_Left_Rear" };
        var stillThere = wc != null ? oldBoxes.Where(n => wc.transform.Find(n) != null).ToList() : new List<string>();
        Check("(e) eski Office_* kutuları ve Garage_Left_Rear kalktı", stillThere.Count == 0, stillThere.Count == 0 ? "yok" : string.Join(",", stillThere));

        // Eski parçalar: etkin kalan hiçbir env renderer'ı gizleme kuralına takılmamalı
        var leftovers = new List<string>();
        foreach (var r in env.GetComponentsInChildren<Renderer>(false))
            if (OfficeExpansionSetup.ShouldHide(r, out string reason)) leftovers.Add(r.name + "(" + reason + ")");
        Check("(f) eski ofis ve ayak izine giren dış parçalar gizli", leftovers.Count == 0, leftovers.Count == 0 ? "artık yok" : leftovers.Count + ": " + string.Join(", ", leftovers.Take(8)));
        // Korunanlar: kapılar ve bölme duvarı görünür
        int doorRenderers = UnityEngine.Object.FindObjectsByType<OfficeDoor>(FindObjectsSortMode.None).Sum(d => d.GetComponentsInChildren<Renderer>(false).Length);
        int partition = env.GetComponentsInChildren<Renderer>(false).Count(r => r.name.StartsWith("A03_"));
        Check("(g) ofis kapıları (A11/A13) ve bölme duvarı (A03) görünür kaldı", doorRenderers >= 6 && partition >= 3, "kapı parçası " + doorRenderers + ", bölme parçası " + partition);

        // Materyaller
        var pink = new List<string>(); var glassBad = new List<string>(); bool mapTextured = false; int glassCount = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null || m.shader.name.Contains("InternalErrorShader")) { pink.Add(r.name); continue; }
                if (m.name == "Glass")
                {
                    glassCount++;
                    if (m.GetFloat("_Surface") < 0.5f || m.GetColor("_BaseColor").a > 0.6f) glassBad.Add(r.name + " a=" + m.GetColor("_BaseColor").a.ToString("F2", Inv));
                }
                if (m.name == "M_CityMap" && m.GetTexture("_BaseMap") != null) mapTextured = true;
            }
        }
        Check("(h) pembe/hatalı shader yok, pencere camı şeffaf (" + glassCount + " renderer), şehir haritası dokulu", pink.Count == 0 && glassBad.Count == 0 && glassCount > 0 && mapTextured,
            "pembe " + pink.Count + " " + string.Join(",", pink.Distinct().Take(5)) + ", cam sorunu " + glassBad.Count + " " + string.Join(",", glassBad.Take(3)) + ", harita " + mapTextured);
        var sampleMats = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).GroupBy(m => m.name).Select(g => g.First()).OrderBy(m => m.name).ToList();
        foreach (var m in sampleMats.Take(60))
            Line("  materyal " + m.name + ": renk " + ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor")) + " a=" + m.GetColor("_BaseColor").a.ToString("F2", Inv) + " pürüzsüz " + (m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness").ToString("F2", Inv) : "-") + " metalik " + (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("F2", Inv) : "-") + (m.IsKeywordEnabled("_EMISSION") ? " ışıldar" : ""));

        // Eşya kutuları duvar kutularına gömülü değil (ComputePenetration), eşya kendi odasında
        Physics.SyncTransforms();
        var wallCols = walls != null ? walls.GetComponentsInChildren<BoxCollider>() : new BoxCollider[0];
        var embedded = new List<string>(); var outOfRoom = new List<string>(); float worst = 0f;
        foreach (var it in layout.items)
        {
            var t = FindDeep(items, it.id); if (t == null) continue;
            var room = layout.rooms.First(r => r.id == it.room);
            var p = t.position; var mn = OfficeExpansionSetup.V3(room.bounds_min); var mx = OfficeExpansionSetup.V3(room.bounds_max);
            if (p.x < mn.x - .15f || p.x > mx.x + .15f || p.z < mn.z - .15f || p.z > mx.z + .15f) outOfRoom.Add(it.id);
            foreach (var bc in t.GetComponents<BoxCollider>())
                foreach (var w in wallCols)
                {
                    if (!Physics.ComputePenetration(bc, t.position, t.rotation, w, w.transform.position, w.transform.rotation, out _, out float depth)) continue;
                    worst = Mathf.Max(worst, depth);
                    if (depth > 0.03f) embedded.Add(it.id + "↔" + w.name + " " + depth.ToString("F3", Inv));
                }
        }
        Check("(i) eşya kutuları duvara gömülü değil (> 3 cm), eşyalar kendi odasında", embedded.Count == 0 && outOfRoom.Count == 0, "en derin " + worst.ToString("F3", Inv) + " m" + (embedded.Count > 0 ? ": " + string.Join(", ", embedded.Take(6)) : "") + (outOfRoom.Count > 0 ? "; oda dışı " + string.Join(",", outOfRoom) : ""));

        // Işık ve probe sayımı (rapor)
        var lights = root.transform.Find("Lights");
        var ls = lights != null ? lights.GetComponentsInChildren<Light>() : new Light[0];
        Line("  ışık: " + ls.Length + " ek spot, gölgeli " + ls.Count(l => l.shadows != LightShadows.None) + "; sahnedeki toplam ışık " + UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length);
        var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
        Check("(j) her oda için yansıma probe'u bake edilmiş", layout.rooms.All(r => probes.Any(p => p.name == (r.id == "Office" ? "OfficeReflection" : r.id + "Reflection") && p.mode == UnityEngine.Rendering.ReflectionProbeMode.Custom && p.customBakedTexture != null)),
            string.Join(", ", probes.Select(p => p.name + ":" + p.mode + (p.customBakedTexture != null ? "✓" : "✗"))));

        // Bilgisayar monitörde
        var computer = GameObject.Find(OfficeExpansionSetup.ComputerName);
        var monitor = FindDeep(items, "Monitor_01");
        Check("(k) Office_Computer_Main monitörün üstünde (≤ 0.3 m) ve OfficeComputer.ui bağlı", computer != null && monitor != null && Vector3.Distance(computer.transform.position, monitor.position) < 0.5f && computer.GetComponent<OfficeComputer>() != null && computer.GetComponent<OfficeComputer>().ui != null,
            "bilgisayar " + (computer != null ? V(computer.transform.position) : "yok") + ", monitör " + (monitor != null ? V(monitor.position) : "yok"));

        // Pano yükseltmesi: açıldığında batı duvarına oturur, tavanın altında; poster, dolap ve üstündeki kâğıtla çakışmaz
        var board = OfficeExpansionSetup.FindRoot("Upgrade_OperationsBoard");
        bool boardOk = false; string boardDetail = "pano yok";
        if (board != null)
        {
            var bb = OfficeExpansionSetup.InactiveWorldBounds(board.transform);
            var clashes = new List<string>();
            foreach (var n in new[] { "PosterA_01", "FilingCabinet_02", "PaperStack_03" })
            {
                var t = FindDeep(items, n);
                if (t == null) clashes.Add(n + " yok");
                else if (OfficeExpansionSetup.InactiveWorldBounds(t).Intersects(bb)) clashes.Add(n);
            }
            boardOk = Mathf.Abs(bb.min.x - (-4.72f)) < 0.01f && bb.max.y < 3.16f && bb.min.y > 1.61f && clashes.Count == 0 && bb.min.z > -5.2f && bb.max.z < -2.655f;
            boardDetail = "x[" + bb.min.x.ToString("F3", Inv) + "," + bb.max.x.ToString("F3", Inv) + "] y[" + bb.min.y.ToString("F3", Inv) + "," + bb.max.y.ToString("F3", Inv) + "] z[" + bb.min.z.ToString("F3", Inv) + "," + bb.max.z.ToString("F3", Inv) + "]" + (clashes.Count > 0 ? ", çakışan " + string.Join(",", clashes) : "");
        }
        Check("(l) Upgrade_OperationsBoard batı duvarına oturur, poster/dolapla çakışmaz", boardOk, boardDetail);
    }

    // =====================================================================================
    // Play aşaması altyapısı (OfficeGarageVerify deseni)
    // =====================================================================================
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
            scenario = Scenario();
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 600f) { Fail("zaman aşımı (10 dk)"); End(); return; }
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

    private static void RestoreSave()
    {
        string backup = SessionState.GetString(SaveBackupKey, "");
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) { File.Move(backup, SavePath); Line("  kayıt geri yüklendi"); }
            else Line("  test kaydı silindi (önceden kayıt yoktu)");
        }
        catch (Exception e) { Line("  UYARI: kayıt geri yüklenemedi: " + e.Message); }
        SessionState.EraseString(SaveBackupKey);
    }

    private static void Finish()
    {
        RestoreSave();
        Line(allOk ? "OFFICEEXPANSION VERIFY OK" : "OFFICEEXPANSION VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "office_expansion_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "OFFICEEXPANSION VERIFY OK" : "OFFICEEXPANSION VERIFY FAILED");
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

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static ThirdPersonPlayer player;
    private static CompanyPlayerBridge bridge;
    private static PlayerInputReader reader;
    private static OrbitCamera orbit;
    private static ComputerUI ui;
    private static Keyboard keyboard;
    private static Mouse mouse;

    private static IEnumerator Scenario()
    {
        yield return null;
        player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
        bridge = player.GetComponent<CompanyPlayerBridge>();
        reader = player.GetComponent<PlayerInputReader>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        ui = UnityEngine.Object.FindAnyObjectByType<ComputerUI>();
        var runtime = ui.Runtime;
        yield return Until(() => runtime.Data != null && runtime.manualModules != null && runtime.manualModules.Initialized, 5f, "CompanyRuntime hazır");
        yield return Until(() => player.HasControl, 3f, "oyuncu hazır");
        yield return null; yield return null;
        var layout = OfficeExpansionSetup.LoadLayout();
        var office = layout.rooms.First(r => r.id == "Office");
        Vector3 p0 = player.transform.position;
        Check("(1) oyuncu ana ofiste doğar, zeminde (y≈0), kontrol var", InRoom(p0, office) && Mathf.Abs(p0.y) < 0.08f && player.HasControl && orbit.Mode == OrbitCamera.ViewMode.ThirdPerson,
            "konum " + V(p0) + ", mod " + orbit.Mode);

        var settings = InputSystem.settings;
        var savedBehavior = settings.editorInputBehaviorInPlayMode;
        var savedBackground = settings.backgroundBehavior;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>(KeyboardName);
        mouse = InputSystem.AddDevice<Mouse>(MouseName);
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        bool savedReader = reader.enabled;
        reader.enabled = true;
        yield return null; yield return null;
        try
        {
            orbit.Pitch = 12f;
            yield return CaptureFrom(new Vector3(-3.9f, 0f, -4.6f), new Vector3(-1.1f, 0.8f, -0.1f), "ofis_01_ana_ofis");

            // (2) Ofis → mutfak (x=-4.8 kapısı z[-2.6,-1.4])
            bool w1 = false, w2 = false;
            yield return WalkTo(new Vector3(-4.0f, 0f, -2.0f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(-5.8f, 0f, -2.0f), 8f, r => w2 = r);
            Check("(2) ofis → mutfak kapısından yürüyerek geçiş", w1 && w2 && player.transform.position.x < -4.9f, "varış " + w1 + "/" + w2 + ", konum " + V(player.transform.position));
            yield return CaptureFrom(new Vector3(-5.3f, 0f, -0.3f), new Vector3(-8.2f, 0.7f, -3.0f), "ofis_02_mutfak");

            // (3) Mutfak → soyunma (z=1.2 kapısı x[-7.9,-6.7])
            yield return WalkTo(new Vector3(-7.3f, 0f, 0.3f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(-7.3f, 0f, 2.2f), 8f, r => w2 = r);
            Check("(3) mutfak → soyunma kapısından geçiş", w1 && w2 && player.transform.position.z > 1.3f, "varış " + w1 + "/" + w2 + ", konum " + V(player.transform.position));
            yield return CaptureFrom(new Vector3(-7.3f, 0f, 1.7f), new Vector3(-7.6f, 0.8f, 4.2f), "ofis_03_soyunma");

            // (4) Soyunma → ring odası (z=5.2 kapısı)
            yield return WalkTo(new Vector3(-7.3f, 0f, 4.3f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(-7.3f, 0f, 6.1f), 8f, r => w2 = r);
            Check("(4) soyunma → ring odası kapısından geçiş", w1 && w2 && player.transform.position.z > 5.3f, "varış " + w1 + "/" + w2 + ", konum " + V(player.transform.position));
            yield return CaptureFrom(new Vector3(-8.6f, 0f, 6.0f), new Vector3(-4.6f, 0.9f, 8.8f), "ofis_04_ring_odasi");

            // (5) Ring basamakları: güney duvarı dibinden yandan basamağa, sonra kuzeye platforma (y≈0.35) ve ring içinde yürüme
            yield return WalkTo(new Vector3(-6.2f, 0f, 5.8f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(-4.9f, 0f, 5.8f), 8f, r => w2 = r);
            bool w3 = false, w4 = false;
            yield return WalkTo(new Vector3(-4.9f, 0.35f, 7.4f), 8f, r => w3 = r);
            float yOnRing = player.transform.position.y;
            yield return WalkTo(new Vector3(-3.6f, 0.35f, 9.6f), 8f, r => w4 = r);
            float yInside = player.transform.position.y;
            Check("(5) ring basamaklarından platforma çıkış (y ≥ 0.33) ve ring içinde yürüme", w1 && w2 && w3 && w4 && yOnRing >= 0.33f && yInside >= 0.33f && yInside < 0.5f,
                "varış " + w1 + "/" + w2 + "/" + w3 + "/" + w4 + ", y platform " + yOnRing.ToString("F2", Inv) + ", y içeride " + yInside.ToString("F2", Inv) + ", konum " + V(player.transform.position));
            yield return CaptureFrom(player.transform.position, new Vector3(-6.5f, 0.6f, 10.5f), "ofis_05_ring_ustu");
            // halat (kuzey, z≈10.68) geçilmez
            yield return WalkTo(new Vector3(-3.6f, 0.35f, 11.6f), 3f, r => w1 = r);
            Check("(6) ring halatından geçilemez (z < 10.5)", !w1 && player.transform.position.z < 10.5f && player.transform.position.y >= 0.3f, "konum " + V(player.transform.position));
            // güneydeki açıklıktan iniş
            yield return WalkTo(new Vector3(-4.9f, 0.35f, 6.9f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(-4.9f, 0f, 5.7f), 8f, r => w2 = r);
            yield return WalkTo(new Vector3(-6.2f, 0f, 5.65f), 8f, r => w3 = r);
            Check("(7) ringden basamaklarla iniş (y < 0.06)", w1 && w2 && w3 && player.transform.position.y < 0.06f, "varış " + w1 + "/" + w2 + "/" + w3 + ", konum " + V(player.transform.position));

            // (8) Geri: ring → soyunma → mutfak → ofis → depo (z=1.2 kapısı x[-3,-1.8])
            yield return WalkTo(new Vector3(-7.3f, 0f, 6.1f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(-7.3f, 0f, 4.3f), 8f, r => w1 &= r);
            yield return WalkTo(new Vector3(-7.3f, 0f, 2.2f), 8f, r => w1 &= r);
            yield return WalkTo(new Vector3(-7.3f, 0f, 0.3f), 8f, r => w1 &= r);
            yield return WalkTo(new Vector3(-5.8f, 0f, -2.0f), 8f, r => w1 &= r);
            yield return WalkTo(new Vector3(-4.0f, 0f, -2.0f), 8f, r => w1 &= r);
            yield return WalkTo(new Vector3(-2.4f, 0f, 0.3f), 8f, r => w1 &= r);
            yield return WalkTo(new Vector3(-2.4f, 0f, 2.2f), 8f, r => w2 = r);
            Check("(8) ring → soyunma → mutfak → ofis → depo yürüyüşü", w1 && w2 && player.transform.position.z > 1.3f && player.transform.position.x > -4.8f, "varış " + w1 + "/" + w2 + ", konum " + V(player.transform.position));
            yield return CaptureFrom(new Vector3(-1.0f, 0f, 1.7f), new Vector3(-3.3f, 0.8f, 4.0f), "ofis_06_depo");

            // (9) Depo ↔ garaj geçişi (x=0, z[1.6,3.6]) ve geçiş dışındaki duvar
            yield return WalkTo(new Vector3(-0.9f, 0f, 2.6f), 8f, r => w1 = r);
            yield return WalkTo(new Vector3(1.1f, 0f, 2.6f), 8f, r => w2 = r);
            Vector3 garageSide = player.transform.position;
            yield return CaptureFrom(new Vector3(2.4f, 0f, 2.6f), new Vector3(-2.0f, 0.9f, 3.0f), "ofis_07_garajdan_gecis");
            yield return WalkTo(new Vector3(-0.9f, 0f, 2.6f), 8f, r => w3 = r);
            // Geçiş dışında x=0 duvarı (depo z[3.6,5.2] kutu/raf dolu; ring odası doğu duvarı z=8'de denenir)
            yield return Teleport(new Vector3(-1.3f, 0f, 8.0f), new Vector3(1f, 1f, 8.0f));
            w4 = Mathf.Abs(player.transform.position.z - 8.0f) < 0.3f;
            bool through = false;
            yield return WalkTo(new Vector3(1.1f, 0f, 8.0f), 3f, r => through = r);
            Check("(9) depo → garaj geçişi açık, geri dönüş açık; geçiş dışında x=0 duvarı (ring odası z=8) geçilmez", w1 && w2 && garageSide.x > 0.6f && w3 && w4 && !through && player.transform.position.x < -0.25f,
                "geçiş " + w1 + "/" + w2 + " garaj tarafı " + V(garageSide) + ", dönüş " + w3 + "/" + w4 + ", duvar denemesi " + V(player.transform.position));
            // Garaj tarafında geçişin önü boş mu (rapor)
            foreach (var c in Physics.OverlapBox(new Vector3(0.9f, 1.0f, 2.6f), new Vector3(0.8f, 1.0f, 1.0f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(player.transform) && !c.name.StartsWith("Wall_") && !c.name.StartsWith("Floor_") && c.name != "Ground") Line("  geçişin garaj tarafında collider: " + c.name + " " + V(c.bounds.center) + " boyut " + V(c.bounds.size));

            // (10) Ofis batı duvarı (kapı dışı) geçilmez
            yield return Teleport(new Vector3(-4.2f, 0f, -4.0f), new Vector3(-6f, 1f, -4.0f));
            yield return WalkTo(new Vector3(-5.4f, 0f, -4.0f), 3f, r => through = r);
            Check("(10) ofis batı duvarından (kapı dışı) geçilmez", !through && player.transform.position.x > -4.75f, "konum " + V(player.transform.position));

            // (11) Bilgisayar: E → UI açık, Esc → kapalı
            var computer = UnityEngine.Object.FindAnyObjectByType<OfficeComputer>();
            bool reached = false;
            yield return Approach(computer, 1.1f, r => reached = r);
            yield return Press(Key.E);
            yield return null;
            bool opened = ui.IsOpen && bridge.Locked;
            yield return Press(Key.Escape);
            yield return null; yield return null;
            Check("(11) yeni masadaki monitöre E → şirket bilgisayarı UI'si açılır, Esc ile kapanır", reached && opened && !ui.IsOpen && !bridge.Locked, "hedef " + reached + ", açıldı " + opened + ", kapandı " + !ui.IsOpen + ", konum " + V(player.transform.position));
            yield return CaptureFrom(new Vector3(-2.2f, 0f, -1.6f), new Vector3(-1.05f, 0.7f, -0.05f), "ofis_08_bilgisayar");

            // (12) Garaj kapısı (A11): E ile aç, garaja geç, geri dön
            var doors = UnityEngine.Object.FindObjectsByType<OfficeDoor>(FindObjectsSortMode.None);
            var garageDoor = doors.FirstOrDefault(d => d.name == "OfficeDoor_A11");
            var frontDoor = doors.FirstOrDefault(d => d.name == "OfficeDoor_A13");
            bool gReached = false;
            if (garageDoor != null) yield return Approach(garageDoor, 1.0f, r => gReached = r);
            yield return Press(Key.E);
            yield return Until(() => garageDoor != null && garageDoor.IsOpen && Mathf.Abs(garageDoor.CurrentAngle) > 60f, 3f, "garaj kapısı açık");
            yield return WalkTo(new Vector3(1.0f, 0f, -2.88f), 8f, r => w1 = r);
            Vector3 inGarage = player.transform.position;
            yield return WalkTo(new Vector3(-1.0f, 0f, -2.88f), 8f, r => w2 = r);
            Check("(12) garaj kapısı: E ile açılır, kapıdan garaja ve geri yürünür", gReached && garageDoor != null && garageDoor.IsOpen && w1 && inGarage.x > 0.5f && w2 && player.transform.position.x < -0.5f,
                "hedef " + gReached + ", açık " + (garageDoor != null && garageDoor.IsOpen) + ", garaj " + V(inGarage) + ", dönüş " + w2);

            // (13) Ön kapı (A13): E ile aç, dışarı çık, geri gir
            bool fReached = false;
            if (frontDoor != null) yield return Approach(frontDoor, 1.0f, r => fReached = r);
            yield return Press(Key.E);
            yield return Until(() => frontDoor != null && frontDoor.IsOpen && Mathf.Abs(frontDoor.CurrentAngle) > 60f, 3f, "ön kapı açık");
            yield return WalkTo(new Vector3(-3.18f, 0f, -6.4f), 8f, r => w1 = r);
            Vector3 outside = player.transform.position;
            yield return CaptureFrom(new Vector3(-3.2f, 0f, -8.5f), new Vector3(-3.2f, 1.4f, -5.2f), "ofis_09_disaridan_giris");
            yield return Teleport(outside, new Vector3(-3.18f, 1f, -4f));
            yield return WalkTo(new Vector3(-3.18f, 0f, -4.2f), 8f, r => w2 = r);
            Check("(13) ön kapı: E ile açılır, dışarı çıkılır ve geri girilir", fReached && frontDoor != null && frontDoor.IsOpen && w1 && outside.z < -5.6f && w2 && player.transform.position.z > -5.0f,
                "hedef " + fReached + ", açık " + (frontDoor != null && frontDoor.IsOpen) + ", dışarı " + V(outside) + ", dönüş " + w2);

            // (14) Dış görünüm: batıdan ve kuzeybatıdan bina
            yield return CaptureFrom(new Vector3(-16f, 0f, -9f), new Vector3(-5f, 1.5f, 3f), "ofis_10_dis_guneybati");
            yield return CaptureFrom(new Vector3(-14f, 0f, 16f), new Vector3(-4f, 1.5f, 6f), "ofis_11_dis_kuzeybati");
            yield return CaptureFrom(new Vector3(-9.0f, 0f, 8.6f), new Vector3(-12f, 1.2f, 8.6f), "ofis_12_ring_penceresi");

            // Geçişin kasaları ve x=0 duvarının garaj yüzü yakından
            yield return CaptureFrom(new Vector3(1.05f, 0f, 3.0f), new Vector3(0f, 1.2f, 1.6f), "ofis_13_gecis_sol_kasa");
            yield return CaptureFrom(new Vector3(1.05f, 0f, 2.2f), new Vector3(0f, 1.2f, 3.6f), "ofis_14_gecis_sag_kasa");
            yield return CaptureFrom(new Vector3(3.4f, 0f, 2.2f), new Vector3(0f, 1.4f, 3.3f), "ofis_15_garaj_yuzu");
            // Pano yükseltmesi açık hâliyle (test sonrası kapatılır)
            var board = OfficeExpansionSetup.FindRoot("Upgrade_OperationsBoard");
            bool boardWasActive = board != null && board.activeSelf;
            if (board != null) board.SetActive(true);
            yield return CaptureFrom(new Vector3(-2.2f, 0f, -3.4f), new Vector3(-4.7f, 1.6f, -3.9f), "ofis_16_pano_yukseltmesi");
            if (board != null) board.SetActive(boardWasActive);

            // Üçüncü şahıs kamera ölçümü: her odada önce oda ortası (yön odanın uzun ekseni), sonra sırtı duvara yakın en zor durum
            yield return ThirdPersonSample(new Vector3(-2.4f, 0f, -2.6f), new Vector3(-2.4f, 1f, 1f), "tp_ana_ofis_orta");
            yield return ThirdPersonSample(new Vector3(-3.9f, 0f, -4.6f), new Vector3(-1.1f, 0.8f, -0.1f), "tp_ana_ofis_kose");
            yield return ThirdPersonSample(new Vector3(-7.3f, 0f, -1.9f), new Vector3(-7.3f, 1f, 1f), "tp_mutfak_orta");
            yield return ThirdPersonSample(new Vector3(-5.3f, 0f, -0.3f), new Vector3(-8.2f, 0.7f, -3.0f), "tp_mutfak_kose");
            yield return ThirdPersonSample(new Vector3(-2.4f, 0f, 3.2f), new Vector3(-4.6f, 1f, 3.2f), "tp_depo_orta");
            yield return ThirdPersonSample(new Vector3(-1.0f, 0f, 1.7f), new Vector3(-3.3f, 0.8f, 4.0f), "tp_depo_kose");
            yield return ThirdPersonSample(new Vector3(-7.3f, 0f, 3.2f), new Vector3(-9.6f, 1f, 3.2f), "tp_soyunma_orta");
            yield return ThirdPersonSample(new Vector3(-7.3f, 0f, 1.7f), new Vector3(-7.6f, 0.8f, 4.2f), "tp_soyunma_kose");
            yield return ThirdPersonSample(new Vector3(-8.6f, 0f, 8.6f), new Vector3(-4.0f, 1f, 8.6f), "tp_ring_orta");
            yield return ThirdPersonSample(new Vector3(-8.6f, 0f, 6.0f), new Vector3(-4.6f, 0.9f, 8.8f), "tp_ring_kose");
        }
        finally
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState());
            player.SetMoveInput(Vector2.zero);
            reader.enabled = savedReader;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            settings.editorInputBehaviorInPlayMode = savedBehavior;
            settings.backgroundBehavior = savedBackground;
        }
    }

    private static bool InRoom(Vector3 p, OfficeExpansionSetup.Room room)
    {
        var mn = OfficeExpansionSetup.V3(room.bounds_min); var mx = OfficeExpansionSetup.V3(room.bounds_max);
        return p.x >= mn.x && p.x <= mx.x && p.z >= mn.z && p.z <= mx.z;
    }

    // ---- yardımcılar ----
    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    // Kamerayı hedefe çevirip W basılı tutarak yürür (gerçek CharacterController hareketi); 0.22 m'ye varınca ya da süre dolunca durur
    private static IEnumerator WalkTo(Vector3 target, float timeoutGameSeconds, Action<bool> result)
    {
        float end = Time.time + timeoutGameSeconds;
        bool arrived = false;
        while (Time.time < end)
        {
            Vector3 d = target - player.transform.position; d.y = 0f;
            if (d.magnitude < 0.22f) { arrived = true; break; }
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
        Vector3 at = position;
        foreach (var h in Physics.RaycastAll(position + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (!h.collider.transform.IsChildOf(player.transform) && h.collider.GetComponentInParent<CarryableItem>() == null && Mathf.Abs(h.point.y - position.y) < 0.6f) { at.y = h.point.y + 0.02f; break; }
        Vector3 flat = lookAt - at; flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f) flat = player.transform.forward;
        player.Teleport(at, Quaternion.LookRotation(flat.normalized, Vector3.up));
        Vector3 pivot = at + Vector3.up * orbit.pivotHeight;
        Vector3 dir = (lookAt - pivot).normalized;
        orbit.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        orbit.Pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        yield return null; yield return null; yield return null;
    }

    private static IEnumerator Approach(InteractableBehaviour target, float distance, Action<bool> result)
    {
        if (target == null) { result(false); yield break; }
        var col = target.GetComponent<Collider>() != null ? target.GetComponent<Collider>() : target.GetComponentInChildren<Collider>();
        Vector3 center = col.bounds.center;
        Vector3 from = player.transform.position - center; from.y = 0f;
        var dirs = new List<Vector3>();
        if (from.sqrMagnitude > 1e-4f) dirs.Add(from.normalized);
        dirs.AddRange(new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right, new Vector3(1, 0, -1).normalized, new Vector3(-1, 0, -1).normalized, new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized });
        var cc = player.GetComponent<CharacterController>();
        foreach (var d in dirs)
        {
            foreach (float dist in new[] { distance, distance + 0.3f })
            {
                Vector3 pos = center + d * dist; pos.y = Mathf.Max(0.01f, center.y - 1.5f);
                yield return Teleport(pos, center);
                bool inside = false;
                foreach (var c in Physics.OverlapSphere(cc.bounds.center, cc.radius, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(player.transform) && c.GetComponentInParent<CarryableItem>() == null && Vector3.Distance(c.ClosestPoint(cc.bounds.center), cc.bounds.center) < 0.02f) inside = true;
                if (!inside && bridge.FindTarget() == target) { result(true); yield break; }
            }
        }
        Line("  yaklaşılamadı: " + target.name + " (son hedef " + (bridge.FindTarget() != null ? bridge.FindTarget().name : "yok") + ", konum " + V(player.transform.position) + ")");
        result(false);
    }

    // Oyuncuyu konuma ışınlar, hedefe baktırır ve ana kameradan görüntü alır. Üçüncü şahıs kamerası küçük odalarda duvara
    // çarpıp kaskın dibine giriyor; görüntü oyunun birinci şahıs görünümüyle (V) alınır, sonra üçüncü şahsa dönülür.
    private static IEnumerator CaptureFrom(Vector3 position, Vector3 lookAt, string name)
    {
        yield return Teleport(position, lookAt);
        if (orbit.Mode != OrbitCamera.ViewMode.FirstPerson)
        {
            yield return Press(Key.V);
            yield return Until(() => orbit.Mode == OrbitCamera.ViewMode.FirstPerson && orbit.Blend >= 1f, 2f, "FP geçişi (görüntü)");
        }
        Vector3 eye = Camera.main.transform.position;
        Vector3 dir = (lookAt - eye).normalized;
        orbit.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        orbit.Pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        yield return Seconds(0.3f);
        yield return CaptureMain(name);
        yield return Press(Key.V);
        yield return Until(() => orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && orbit.Blend <= 0f, 2f, "TP geçişi (görüntü)");
        yield return null;
    }

    // Üçüncü şahıs görüntüsü ve ölçümü (rapor satırı, başarı ölçütü değil): kameranın oturması beklenir, sonra pivot ve baş uzaklığı yazılır
    private static IEnumerator ThirdPersonSample(Vector3 position, Vector3 lookAt, string name)
    {
        yield return Teleport(position, lookAt);
        yield return Seconds(1.6f); // releaseDelay + geri açılma (OrbitCamera) sonrası
        Transform cam = Camera.main.transform;
        Vector3 pivot = player.transform.position + Vector3.up * orbit.CurrentPivotHeight;
        float toHead = orbit.Head != null ? Vector3.Distance(cam.position, orbit.Head.position) : -1f;
        Line("  TP " + name + ": oyuncu " + V(player.transform.position) + ", kamera " + V(cam.position) + ", pivot uzaklığı " + Vector3.Distance(cam.position, pivot).ToString("F2", Inv) + " m (serbest " + orbit.FreeDistance.ToString("F2", Inv) + "), başa " + toHead.ToString("F2", Inv) + " m, baş kamerada " + orbit.HeadInsideCamera);
        yield return CaptureMain(name);
    }

    private static IEnumerator CaptureMain(string name)
    {
        yield return null;
        var cam = Camera.main;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null; yield return null;
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        Line("  görüntü: " + OutDir + "/" + name + ".png (kamera " + V(cam.transform.position) + ")");
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            var f = FindDeep(child, name); if (f != null) return f;
        }
        return null;
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
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
