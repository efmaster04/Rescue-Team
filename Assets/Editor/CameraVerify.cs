using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Kamera kabul testi (kullanıcı istekleri 2026-11-05, OrbitCamera):
//  1. Tekerlekle yakınlaştırma (yalnız TP): sınırlar, yumuşak geçiş, PlayerPrefs, UI açıkken / FP'de yok sayılır, minibüs sürüşünde
//     (ServiceVanDriver distance 7.5) çarpan olarak çalışır ve inince distance eski değerine döner.
//  2. Küçük / hareketli fizik nesneleri (küçük statik kutu, kinematik kutu, dinamik balya, gerçek valiz) kamerayı çekmez; büyük duvar çeker
//     ve kamera duvarın içine girmez.
//  3. Duvara yaklaşma / uzaklaşma: kare başı mesafe değişim hızı sınırlı, kamera duvara girmez, uzaklaşırken geri dönüş yok; engel
//     kenarında (yaw titrerken) uzaklık titremez.
//  4. Elde eşya (kangal, koni, valiz): TP ve FP'de ekran merkezi kutusunu kapatmaz (iki çizim farkıyla ölçülür); bırakınca ölçek eski.
// Batchmode (ekran görüntüsü için -nographics OLMADAN): Unity.exe -batchmode -projectPath ... -executeMethod CameraVerify.Run
// Başarıda "CAMERA VERIFY OK" ve exit 0. Rapor: Captures/Bolum1/camera_verify_report.txt. Görüntüler: Captures/Bolum1/kamera_sonra_*.png
// CaptureBefore: yalnız elde eşya görüntüleri ve ölçümleri (önek kamera_once_), denetim yok (değişiklik öncesi karşılaştırma için).
// SESSİZLİK: Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0. PlayerPrefs (mod, çarpan) test
// sonunda eski değerine döner.
[InitializeOnLoad]
public static class CameraVerify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "CameraVerify.Stage";
    private const string ReportKey = "CameraVerify.Report";
    private const string OkKey = "CameraVerify.Ok";
    private const string BatchKey = "CameraVerify.Batch";
    private const string PrefixKey = "CameraVerify.Prefix";
    private const string CaptureOnlyKey = "CameraVerify.CaptureOnly";
    private const string PrevModeKey = "CameraVerify.PrevMode";
    private const string PrevZoomKey = "CameraVerify.PrevZoom";
    private const string MouseName = "CamVerifyMouse";
    // Test alanı: bölümden uzakta, boş havada düz zemin (yalnız bu testin nesneleri)
    private static readonly Vector3 Arena = new Vector3(2000f, 100f, 2000f);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;
    private static string prefix;
    private static bool captureOnly;

    static CameraVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Bölüm 1/Kamera: Doğrula")]
    public static void RunMenu() { Begin(false, "kamera_sonra_", false); }

    public static void Run() { Begin(true, "kamera_sonra_", false); }
    public static void CaptureBefore() { Begin(true, "kamera_once_", true); }

    private static void Begin(bool batch, string pre, bool onlyCaptures)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("CAMERA VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + (onlyCaptures ? " (yalnız görüntü)" : ""));
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            // Kayıtlı seçimler test sonunda geri yazılır
            SessionState.SetInt(PrevModeKey, PlayerPrefs.HasKey(OrbitCamera.ViewModePrefsKey) ? PlayerPrefs.GetInt(OrbitCamera.ViewModePrefsKey) : -1);
            SessionState.SetFloat(PrevZoomKey, PlayerPrefs.HasKey(OrbitCamera.ZoomPrefsKey) ? PlayerPrefs.GetFloat(OrbitCamera.ZoomPrefsKey) : -1f);
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
        SessionState.SetString(PrefixKey, pre);
        SessionState.SetBool(CaptureOnlyKey, onlyCaptures);
        SessionState.SetString(StageKey, "play");
        Bolum1AmbulanceAudioMute.Apply(); // SESSİZLİK
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    // =====================================================================================
    // Play aşaması altyapısı (Bolum1FirstPersonVerify deseni)
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
            prefix = SessionState.GetString(PrefixKey, "kamera_");
            captureOnly = SessionState.GetBool(CaptureOnlyKey, false);
            scenario = Scenario();
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 600f) { Fail("zaman aşımı (10 dk)"); End(); return; }
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
        CleanupInput();
        RestorePrefs();
        if (frameRateChanged) { Application.targetFrameRate = savedFrameRate; QualitySettings.vSyncCount = savedVSync; frameRateChanged = false; }
        Finish();
    }

    private static void RestorePrefs()
    {
        int mode = SessionState.GetInt(PrevModeKey, -1);
        float zoom = SessionState.GetFloat(PrevZoomKey, -1f);
        if (mode < 0) PlayerPrefs.DeleteKey(OrbitCamera.ViewModePrefsKey); else PlayerPrefs.SetInt(OrbitCamera.ViewModePrefsKey, mode);
        if (zoom < 0f) PlayerPrefs.DeleteKey(OrbitCamera.ZoomPrefsKey); else PlayerPrefs.SetFloat(OrbitCamera.ZoomPrefsKey, zoom);
        PlayerPrefs.Save();
    }

    private static void Finish()
    {
        Line(allOk ? "CAMERA VERIFY OK" : "CAMERA VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "camera_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "CAMERA VERIFY OK" : "CAMERA VERIFY FAILED");
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

    // ---- her kare örnekleme (duvar ve titreme ölçümleri) ----
    private static bool recording;
    private static Collider recordWall;
    private static float prevDistance = -1f, prevDelta;
    private static float maxRate, maxStep;
    private static int reversals, insideFrames, recordedFrames;
    private static float minDistance, maxDistance;

    private static void StartRecording(Collider wall)
    {
        recording = true;
        recordWall = wall;
        prevDistance = -1f; prevDelta = 0f;
        maxRate = 0f; maxStep = 0f;
        reversals = 0; insideFrames = 0; recordedFrames = 0;
        minDistance = float.MaxValue; maxDistance = float.MinValue;
    }

    private static void Sample()
    {
        if (!recording || orbit == null || player == null) return;
        // Kameranın pivottan gerçek uzaklığı (OrbitCamera.LateUpdate bu karenin başında, önceki karede yazdı)
        Vector3 pivot = player.transform.position + Vector3.up * orbit.pivotHeight;
        float d = Vector3.Distance(orbit.transform.position, pivot);
        recordedFrames++;
        minDistance = Mathf.Min(minDistance, d);
        maxDistance = Mathf.Max(maxDistance, d);
        if (recordWall != null && Vector3.Distance(recordWall.ClosestPoint(orbit.transform.position), orbit.transform.position) < 0.1f) insideFrames++;
        if (prevDistance >= 0f)
        {
            float delta = d - prevDistance;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            maxStep = Mathf.Max(maxStep, Mathf.Abs(delta));
            maxRate = Mathf.Max(maxRate, Mathf.Abs(delta) / dt);
            if (Mathf.Abs(delta) > 0.003f)
            {
                if (prevDelta != 0f && Mathf.Sign(delta) != Mathf.Sign(prevDelta)) { reversals++; if (reversals <= 3) Line("    yön dönüşü: kare " + recordedFrames + ", önceki Δ " + prevDelta.ToString("F4", Inv) + ", Δ " + delta.ToString("F4", Inv) + ", uzaklık " + M(d) + ", engel " + orbit.Obstructed + ", oyuncu y " + player.transform.position.y.ToString("F3", Inv)); }
                prevDelta = delta;
            }
        }
        prevDistance = d;
    }

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static ThirdPersonPlayer player;
    private static OrbitCamera orbit;
    private static PlayerInputReader reader;
    private static Mouse mouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedBehavior;
    private static InputSettings.BackgroundBehavior savedBackground;
    private static readonly List<GameObject> spawned = new List<GameObject>();
    private static Vector3 startPos;
    private static Quaternion startRot;
    private static int savedFrameRate, savedVSync;
    private static bool frameRateChanged;

    private static IEnumerator Scenario()
    {
        yield return null;
        var playerGo = GameObject.Find(Bolum1SceneBuilder.CharacterName);
        player = playerGo.GetComponent<ThirdPersonPlayer>();
        reader = playerGo.GetComponent<PlayerInputReader>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        startPos = player.transform.position;
        startRot = player.transform.rotation;
        // Gerçek oyun kare hızı: batchmode sınırsız koşar (~2000 fps); kare başı ölçümler 60 fps'de anlamlı
        savedFrameRate = Application.targetFrameRate; savedVSync = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
        frameRateChanged = true;
        ThiefDirector.SuppressedForTests = true;
        var mission = MissionDirector.Instance;
        if (mission != null)
        {
            var a = ScriptableObject.CreateInstance<AmbulanceSchedule>();
            a.firstArrival = 99999f; a.interval = 99999f; a.wait = 15f;
            mission.missLimit = 999;
            mission.Begin(a);
        }
        if (TrafficDirector.Instance != null)
        {
            var s = UnityEngine.Object.Instantiate(Bolum1TrafficBuilder.Settings);
            s.useFixedSeed = true; s.seed = 1;
            s.calmSeconds = new Vector2(99999f, 99999f);
            s.calmSingleVehicleChance = 0f;
            TrafficDirector.Instance.Begin(s);
        }
        yield return null; yield return null;
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        orbit.SetZoom(1f, true);
        Line("PLAY: hırsız / ambulans / trafik kapalı; OrbitCamera distance " + orbit.distance.ToString("F2", Inv) + ", çarpan sınırları " + orbit.minZoom.ToString("F2", Inv) + "–" + orbit.maxZoom.ToString("F2", Inv));

        if (!captureOnly)
        {
            // Sanal fare: tekerlek olayları gerçek giriş yolundan (InputAction) gelir (U4b / FP deseni)
            var settings = InputSystem.settings;
            savedBehavior = settings.editorInputBehaviorInPlayMode;
            savedBackground = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>(MouseName);
            reader.enabled = true;
            yield return null; yield return null;

            yield return PhaseDriveZoom();
            yield return PhaseZoom();
            yield return PhaseSmallObstacles();
            yield return PhaseWall();
            yield return PhaseJitter();
            CleanupInput();
            foreach (var g in spawned) if (g != null) UnityEngine.Object.Destroy(g);
            spawned.Clear();
            orbit.SetZoom(1f, true);
            orbit.Pitch = 18f;
        }
        yield return PhaseHeldItems();
    }

    private static void CleanupInput()
    {
        recording = false;
        if (mouse != null)
        {
            InputSystem.RemoveDevice(mouse);
            mouse = null;
            InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
            InputSystem.settings.backgroundBehavior = savedBackground;
        }
    }

    // Tekerlek: bir çentik = OrbitCamera.ScrollPerNotch (varsayılan ayarda 1); bir kare basılı, sonra sıfır
    private static IEnumerator Scroll(float notches)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, OrbitCamera.ScrollPerNotch * notches) });
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    private static GameObject Box(string name, Vector3 center, Vector3 size, bool rigidbody, bool kinematic)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "CamVerify_" + name;
        go.transform.SetPositionAndRotation(center, Quaternion.identity);
        go.transform.localScale = size;
        if (rigidbody)
        {
            var b = go.AddComponent<Rigidbody>();
            b.useGravity = false; // kamera çizgisinde asılı kalsın (sonuç yalnız Rigidbody'nin varlığına bakar)
            b.isKinematic = kinematic;
        }
        spawned.Add(go);
        Physics.SyncTransforms();
        return go;
    }

    // Test alanına git: düz zemin, oyuncu +z'ye bakar, kamera arkada (-z), eğim 0
    private static IEnumerator GoToArena()
    {
        if (GameObject.Find("CamVerify_Ground") == null) Box("Ground", Arena - Vector3.up * 0.5f, new Vector3(80f, 1f, 80f), false, false);
        player.Teleport(Arena, Quaternion.identity);
        orbit.Yaw = 0f;
        orbit.Pitch = 0f;
        yield return Seconds(0.4f);
    }

    private static Vector3 Pivot { get { return player.transform.position + Vector3.up * orbit.pivotHeight; } }
    private static float CamDistance { get { return Vector3.Distance(orbit.transform.position, Pivot); } }

    // (1a) Sürüşte: ServiceVanDriver distance'ı 7.5 yapar; çarpan onun üstüne biner, tekerlek sürüşte de çalışır, inince distance geri gelir.
    private static IEnumerator PhaseDriveZoom()
    {
        var van = ServiceVanDriver.Instance;
        if (van == null) { Fail("(1a) ServiceVanDriver yok"); yield break; }
        float walkDistance = orbit.distance;
        orbit.SetZoom(0.8f, true);
        van.SeatCrew(false);
        yield return Seconds(1.5f);
        float driveDistance = orbit.distance;
        float free0 = orbit.FreeDistance, cur0 = orbit.CurrentDistance;
        int inputs0 = orbit.ZoomInputs;
        yield return Scroll(-2f); // 2 çentik uzaklaş
        yield return Seconds(1.2f);
        float zoomAfter = orbit.TargetZoom, free1 = orbit.FreeDistance, cur1 = orbit.CurrentDistance;
        bool distanceUntouched = Mathf.Approximately(orbit.distance, driveDistance);
        bool exited = van.Exit();
        yield return Seconds(0.3f);
        float expectZoom = 0.8f * orbit.zoomStepRatio * orbit.zoomStepRatio;
        Check("(1a) sürüşte tekerlek: distance minibüsün değeri (" + van.driveCameraDistance.ToString("F1", Inv) + ") kalır, etkin uzaklık = distance × çarpan; 2 çentik uzaklaşma çarpanı ×1.12² yapar; inince distance yaya değerine döner, çarpan korunur",
            Mathf.Approximately(driveDistance, van.driveCameraDistance) && Mathf.Abs(free0 - driveDistance * 0.8f) < 0.01f && distanceUntouched
            && orbit.ZoomInputs == inputs0 + 1 && Mathf.Abs(zoomAfter - expectZoom) < 0.005f && Mathf.Abs(free1 - driveDistance * expectZoom) < 0.02f
            && (Mathf.Abs(cur1 - free1) < 0.05f || orbit.Obstructed) && exited && Mathf.Approximately(orbit.distance, walkDistance) && Mathf.Abs(orbit.TargetZoom - expectZoom) < 0.005f,
            "sürüş distance " + driveDistance.ToString("F2", Inv) + ", çarpan 0.80 → etkin " + M(free0) + " (kamera " + M(cur0) + "); 2 çentik → çarpan " + zoomAfter.ToString("F3", Inv)
            + " (beklenen " + expectZoom.ToString("F3", Inv) + "), etkin " + M(free1) + ", kamera " + M(cur1) + (orbit.Obstructed ? " (engel: " + orbit.LastObstacle + ")" : "")
            + ", distance dokunulmadı " + distanceUntouched + "; iniş " + exited + " → distance " + orbit.distance.ToString("F2", Inv) + ", çarpan " + orbit.TargetZoom.ToString("F3", Inv));
        orbit.SetZoom(1f, true);
    }

    // (1b–1e) Yayan tekerlek: tek çentik yumuşak, sınırlar, PlayerPrefs, UI açıkken ve FP'de yok sayılır
    private static IEnumerator PhaseZoom()
    {
        yield return GoToArena();
        orbit.Pitch = 18f;
        yield return Seconds(0.3f);
        float d0 = orbit.CurrentDistance;
        // (1b) tek çentik yaklaş: hedef 1/1.12, en az 3 ara kare (yumuşak), sonunda kamera ≈ distance × çarpan
        StartRecording(null);
        yield return Scroll(1f);
        float target1 = orbit.TargetZoom;
        int framesToSettle = 0;
        float start = Time.time;
        yield return (Func<bool>)(() => { framesToSettle++; return Mathf.Abs(orbit.ZoomScale - orbit.TargetZoom) < 0.0005f || Time.time - start > 2f; });
        recording = false;
        yield return Seconds(0.1f);
        float d1 = orbit.CurrentDistance;
        Check("(1b) tekerlek 1 çentik yukarı: çarpan 1 → 1/1.12, geçiş yumuşak (≥ 3 kare, kare başı ≤ 0.25 m), kamera = 4.5 × çarpan, PlayerPrefs yazıldı",
            Mathf.Abs(target1 - 1f / orbit.zoomStepRatio) < 0.001f && framesToSettle >= 3 && maxStep <= 0.25f && Mathf.Abs(d1 - orbit.distance * target1) < 0.03f
            && Mathf.Abs(PlayerPrefs.GetFloat(OrbitCamera.ZoomPrefsKey, -1f) - target1) < 0.001f,
            "çarpan " + target1.ToString("F3", Inv) + ", " + framesToSettle + " karede oturdu, kare başı en büyük adım " + M(maxStep) + ", kamera " + M(d0) + " → " + M(d1)
            + ", PlayerPrefs " + PlayerPrefs.GetFloat(OrbitCamera.ZoomPrefsKey, -1f).ToString("F3", Inv));

        // (1c) sınırlar: 20 çentik yaklaş → minZoom, 30 çentik uzaklaş → maxZoom
        for (int i = 0; i < 20; i++) yield return Scroll(1f);
        yield return Seconds(0.8f);
        float zMin = orbit.TargetZoom, dMin = orbit.CurrentDistance;
        for (int i = 0; i < 30; i++) yield return Scroll(-1f);
        yield return Seconds(0.8f);
        float zMax = orbit.TargetZoom, dMax = orbit.CurrentDistance;
        Check("(1c) sınırlar: en yakın " + orbit.minZoom.ToString("F2", Inv) + " × 4.5 = " + M(orbit.minZoom * orbit.distance) + ", en uzak " + orbit.maxZoom.ToString("F2", Inv) + " × 4.5 = " + M(orbit.maxZoom * orbit.distance) + " (engelsiz alanda kamera tam oraya gider)",
            Mathf.Abs(zMin - orbit.minZoom) < 0.001f && Mathf.Abs(zMax - orbit.maxZoom) < 0.001f && Mathf.Abs(dMin - orbit.minZoom * orbit.distance) < 0.03f && Mathf.Abs(dMax - orbit.maxZoom * orbit.distance) < 0.03f,
            "20 yukarı → çarpan " + zMin.ToString("F3", Inv) + " kamera " + M(dMin) + "; 30 aşağı → çarpan " + zMax.ToString("F3", Inv) + " kamera " + M(dMax));

        // (1d) UI açık (PlayerInputReader.InputBlocked; garajda köprü yazar): tekerlek yok sayılır
        int blocked0 = orbit.ZoomInputsBlocked;
        reader.InputBlocked = true;
        yield return Scroll(3f);
        float zUi = orbit.TargetZoom;
        reader.InputBlocked = false;
        yield return null;
        // (1e) FP: tekerlek yok sayılır
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        yield return null;
        yield return Scroll(3f);
        float zFp = orbit.TargetZoom;
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return Seconds(0.3f);
        Check("(1d) UI açıkken (InputBlocked) ve birinci şahısta tekerlek çarpanı değiştirmez",
            Mathf.Abs(zUi - zMax) < 0.0001f && Mathf.Abs(zFp - zMax) < 0.0001f && orbit.ZoomInputsBlocked >= blocked0 + 2,
            "UI açık → çarpan " + zUi.ToString("F3", Inv) + ", FP → " + zFp.ToString("F3", Inv) + " (önce " + zMax.ToString("F3", Inv) + "), reddedilen olay " + (orbit.ZoomInputsBlocked - blocked0));

        // (1e) hatırlama: yeni OrbitCamera örneği kayıtlı çarpanı okur (IgnoreSavedMode testlerde açık, burada geçici kapatılır)
        bool savedIgnore = OrbitCamera.IgnoreSavedMode;
        OrbitCamera.IgnoreSavedMode = false;
        var go = new GameObject("CamVerify_OrbitProbe");
        go.SetActive(false);
        var probe = go.AddComponent<OrbitCamera>();
        go.SetActive(true);
        float probeZoom = probe.TargetZoom;
        OrbitCamera.IgnoreSavedMode = savedIgnore;
        UnityEngine.Object.Destroy(go);
        Check("(1e) çarpan PlayerPrefs'te kalır: yeni OrbitCamera kayıtlı çarpanla başlar", Mathf.Abs(probeZoom - zMax) < 0.001f,
            "kayıtlı " + PlayerPrefs.GetFloat(OrbitCamera.ZoomPrefsKey, -1f).ToString("F3", Inv) + ", yeni örnek " + probeZoom.ToString("F3", Inv));
        orbit.SetZoom(1f, true);
        yield return Seconds(0.2f);
    }

    // (2) Küçük / hareketli fizik nesneleri kamerayı çekmez; büyük duvar çeker ve kamera duvarın içine girmez.
    private static IEnumerator PhaseSmallObstacles()
    {
        yield return GoToArena();
        Vector3 p = Pivot;
        Vector3 back = Vector3.back; // eğim 0, yaw 0: kamera pivotun -z'sinde
        var a = Box("KucukStatik", p + back * 1.4f, Vector3.one * 0.45f, false, false);
        var b = Box("KinematikKutu", p + back * 2.2f, Vector3.one * 1.0f, true, true);
        var c = Box("DinamikBalya", p + back * 3.0f, new Vector3(1.2f, 0.6f, 0.6f), true, false);
        LootItem luggage = null;
        foreach (var l in LootItem.All) if (l != null && l.kind == LootKind.Luggage && l.carryable && !l.IsHeld) { luggage = l; break; }
        Vector3 lugPos = Vector3.zero; Quaternion lugRot = Quaternion.identity;
        if (luggage != null)
        {
            lugPos = luggage.transform.position; lugRot = luggage.transform.rotation;
            luggage.transform.position = p + back * 3.8f - (luggage.Center - luggage.transform.position);
            Physics.SyncTransforms();
        }
        yield return Seconds(0.6f);
        float d = CamDistance;
        bool obstructed = orbit.Obstructed;
        string lugInfo = luggage != null ? "valiz " + luggage.displayName + " 3.8 m'de" : "valiz YOK";
        Check("(2a) küçük / hareketli nesneler engel değil: 1.4 m'de 0.45 m statik kutu, 2.2 m'de 1 m kinematik kutu, 3.0 m'de dinamik balya, " + lugInfo + " → kamera 4.5 m'de kalır",
            !obstructed && Mathf.Abs(d - orbit.FreeDistance) < 0.05f && luggage != null,
            "kamera–pivot " + M(d) + " (engelsiz " + M(orbit.FreeDistance) + "), engel " + obstructed + (obstructed ? " (" + orbit.LastObstacle + ")" : ""));
        yield return CaptureMain(prefix + "kucuk_nesneler");
        UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b); UnityEngine.Object.Destroy(c);
        if (luggage != null) { luggage.transform.SetPositionAndRotation(lugPos, lugRot); Physics.SyncTransforms(); }
        yield return null;

        // (2b) büyük duvar (4 × 4 × 0.4 m, yüzü 2.8 m'de): kamera duvarın önüne gelir, içine girmez
        var wall = Box("Duvar", p + back * 3.0f, new Vector3(4f, 4f, 0.4f), false, false);
        yield return Seconds(0.8f);
        float dw = CamDistance;
        float gap = Vector3.Distance(wall.GetComponent<Collider>().ClosestPoint(orbit.transform.position), orbit.transform.position);
        Check("(2b) büyük statik duvar engeldir: kamera duvarın önünde (pivot–yüz 2.8 m), yüzeye ≥ 0.25 m",
            orbit.Obstructed && dw < 2.8f - 0.25f && gap >= 0.25f, "kamera–pivot " + M(dw) + ", duvara " + M(gap) + ", engel " + orbit.LastObstacle);
        UnityEngine.Object.Destroy(wall);
        yield return Seconds(0.8f);
    }

    // (3) Duvara yaklaşma / uzaklaşma: oyuncu 1.5 m/sn ile geri geri duvara gider, sonra uzaklaşır. Her kare kamera–pivot uzaklığı izlenir.
    private static IEnumerator PhaseWall()
    {
        yield return GoToArena();
        Vector3 start = Arena;
        // Duvar yüzü pivotun 7.8 m arkasında (engelsiz 4.5 m + küreler; başta engel yok)
        var wall = Box("YaklasmaDuvari", new Vector3(start.x, start.y + 2.5f, start.z - 8f), new Vector3(8f, 5f, 0.4f), false, false);
        var wallCol = wall.GetComponent<Collider>();
        // Önceki aşamanın geri açılması bitsin: kamera engelsiz uzaklıkta durgun
        yield return Until(() => !orbit.Obstructed && Mathf.Abs(orbit.CurrentDistance - orbit.FreeDistance) < 0.0005f, 5f, "kamera engelsiz uzaklıkta");
        yield return Seconds(0.2f);
        const float speed = 1.5f;
        float faceZ = start.z - 7.8f;
        float endZ = faceZ + 1.2f;      // pivot–yüz 1.2 m'ye kadar
        StartRecording(wallCol);
        Vector3 pos = start;
        while (pos.z > endZ)
        {
            pos.z = Mathf.Max(endZ, pos.z - speed * Time.deltaTime);
            player.Teleport(pos, Quaternion.identity);
            yield return null;
        }
        yield return Seconds(0.6f);
        recording = false;
        float inRate = maxRate, inStep = maxStep, inMin = minDistance;
        int inInside = insideFrames, inReversals = reversals, inFrames = recordedFrames;
        Check("(3a) duvara yaklaşma (1.5 m/sn): içeri çekilme yumuşak (en hızlı ≤ 4 m/sn, geri sıçrama yok), kamera hiçbir karede duvara girmez",
            inRate <= 4f && inReversals == 0 && inInside == 0 && inMin < 1.0f,
            inFrames + " kare, en hızlı değişim " + inRate.ToString("F2", Inv) + " m/sn (kare başı en büyük " + M(inStep) + "), yön dönüşü " + inReversals + ", duvara giren kare " + inInside + ", en yakın " + M(inMin));

        StartRecording(wallCol);
        while (pos.z < start.z)
        {
            pos.z = Mathf.Min(start.z, pos.z + speed * Time.deltaTime);
            player.Teleport(pos, Quaternion.identity);
            yield return null;
        }
        yield return Seconds(1.2f);
        recording = false;
        float outRate = maxRate, outStep = maxStep, outEnd = CamDistance;
        Check("(3b) duvardan uzaklaşma: geri açılma yavaş ve tek yönlü (en hızlı ≤ " + orbit.releaseMaxSpeed.ToString("F0", Inv) + " m/sn, yön dönüşü yok), duvara girmez, sonunda 4.5 m",
            outRate <= orbit.releaseMaxSpeed + 0.5f && reversals == 0 && insideFrames == 0 && Mathf.Abs(outEnd - orbit.FreeDistance) < 0.05f,
            recordedFrames + " kare, en hızlı " + outRate.ToString("F2", Inv) + " m/sn (kare başı " + M(outStep) + "), yön dönüşü " + reversals + ", duvara giren kare " + insideFrames + ", son " + M(outEnd));
        UnityEngine.Object.Destroy(wall);
        yield return null;
    }

    // (3c) Engel kenarında titreme: duvarın sol kenarı kamera çizgisinin e m sağında; yaw her kare ±0.6° oynar (3 m'de ±3 cm), böylece
    // küre kenara bir değer bir değmez. e = 0.2: dar küre (kesin sınır) titrer; e = 0.33: geniş küre (önsezi) titrer. Eski kod (tek dar
    // küre, yumuşatmasız) bu durumda kamerayı her kare 4.5 m ile ~2.8 m arasında zıplatıyordu.
    private static IEnumerator PhaseJitter()
    {
        foreach (float edge in new[] { 0.2f, 0.33f })
        {
            yield return GoToArena();
            Vector3 p = Pivot;
            var wall = Box("KenarDuvari", new Vector3(p.x + edge + 4f, p.y, p.z - 3.2f), new Vector3(8f, 5f, 0.4f), false, false);
            var col = wall.GetComponent<Collider>();
            float probeRadius = edge < 0.3f ? orbit.collisionRadius : orbit.anticipationRadius;
            yield return Seconds(0.3f);
            int touchFrames = 0, frames = 0;
            float t0 = Time.time;
            bool settled = false;
            while (Time.time - t0 < 2f)
            {
                orbit.Yaw = (frames % 2 == 0) ? 0.6f : -0.6f;
                frames++;
                Vector3 dir = Quaternion.Euler(orbit.Pitch, orbit.Yaw, 0f) * Vector3.back;
                if (col.Raycast(new Ray(p, dir), out _, 10f) || Physics.SphereCast(p, probeRadius, dir, out RaycastHit h, orbit.FreeDistance, ~0, QueryTriggerInteraction.Ignore) && h.collider == col) touchFrames++;
                // İlk 0.4 sn içeri çekilme; sonrası ölçülür
                if (!settled && Time.time - t0 > 0.4f) { settled = true; StartRecording(col); }
                yield return null;
            }
            recording = false;
            float range = maxDistance - minDistance;
            Check("(3c) engel kenarında titreme yok (kenar " + M(edge) + ", " + (edge < 0.3f ? "dar" : "geniş") + " küre " + touchFrames + "/" + frames + " karede değiyor): kamera uzaklığı ≤ 5 cm oynar, yön dönüşü ≤ 2, duvara girmez",
                touchFrames > frames / 5 && touchFrames < frames * 4 / 5 && range <= 0.05f && reversals <= 2 && insideFrames == 0,
                "uzaklık " + M(minDistance) + "–" + M(maxDistance) + " (aralık " + M(range) + "), yön dönüşü " + reversals + ", " + recordedFrames + " kare ölçüldü, duvara giren " + insideFrames);
            UnityEngine.Object.Destroy(wall);
            orbit.Yaw = 0f;
            yield return Seconds(0.5f);
        }
    }

    // ---- (4) Elde eşya: TP ve FP görüntüsü, eşyanın ekran payı (iki çizim farkı) ----
    private static IEnumerator PhaseHeldItems()
    {
        // Oyuncunun sahnedeki başlangıç yeri (CaptureBefore ile aynı kadraj)
        Vector3 spawnPos = startPos;
        Quaternion spawnRot = startRot;
        var coil = RopeCoil.All.Count > 0 ? RopeCoil.All[0] : null;
        TrafficCone cone = TrafficCone.All.Count > 0 ? TrafficCone.All[0] : null;
        // Bütün valiz / çantalar (ada göre sıralı; boyları farklı: Suitcase büyük, Bag küçük)
        var luggages = new List<LootItem>();
        foreach (var l in LootItem.All) if (l != null && l.kind == LootKind.Luggage && l.carryable) luggages.Add(l);
        luggages.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        Vector3 coilScale = coil != null ? coil.transform.localScale : Vector3.one;
        var items = new List<(string name, Func<bool> pick, Func<Renderer[]> renderers)>
        {
            ("kangal", () => { if (coil == null) return false; player.HeldCoil = coil; coil.PickUp(player); return true; }, () => coil.GetComponentsInChildren<Renderer>()),
            ("koni", () => { if (cone == null) return false; player.HeldCone = cone; cone.PickUp(player); return true; }, () => cone.target.GetComponentsInChildren<Renderer>()),
        };
        foreach (var l in luggages)
        {
            var lug = l;
            items.Add(("valiz_" + lug.name.Replace("SM_B1_Prop_", ""), () => { player.HeldValuable = lug; lug.PickUp(player); return true; }, () => lug.GetComponentsInChildren<Renderer>()));
        }
        if (luggages.Count == 0) Fail("(4) sahnede valiz yok");
        foreach (var it in items)
        {
            player.RespawnAt(spawnPos, spawnRot);
            yield return Seconds(0.3f);
            if (!it.pick()) { Fail("(4) " + it.name + ": sahnede yok"); continue; }
            yield return Seconds(0.8f);
            var rs = it.renderers();
            if (rs.Length > 0) { Bounds bb = rs[0].bounds; foreach (var r0 in rs) bb.Encapsulate(r0.bounds); Line("  " + it.name + ": " + rs[0].transform.root.name + "/" + rs[0].name + ", " + rs.Length + " renderer, sınır boyu " + V(bb.size) + ", oyuncuya göre merkez " + V(player.transform.InverseTransformPoint(bb.center))); }
            // TP
            orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
            orbit.Yaw = player.transform.eulerAngles.y;
            orbit.Pitch = 18f;
            yield return Seconds(0.2f);
            yield return Measure(it.name + " TP 18°", rs);
            float tp = measuredAhead;
            yield return CaptureMain(prefix + it.name + "_tp");
            // TP yakın ve alçak (en yakın çarpan, eğim 5°): omuz üstünden görünmesin
            orbit.SetZoom(orbit.minZoom, true);
            orbit.Pitch = 5f;
            yield return Seconds(0.2f);
            yield return Measure(it.name + " TP yakın 5°", rs);
            float tpNear = measuredAhead;
            orbit.SetZoom(1f, true);
            // FP: düz ve aşağı bakış
            orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
            orbit.Yaw = player.transform.eulerAngles.y;
            orbit.Pitch = 0f;
            yield return Seconds(0.2f);
            yield return Measure(it.name + " FP 0°", rs);
            float fp0 = measuredCenter;
            yield return CaptureMain(prefix + it.name + "_fp");
            orbit.Pitch = 30f;
            yield return Seconds(0.2f);
            yield return Measure(it.name + " FP 30°", rs);
            float fp30 = measuredCenter, fp30Total = measuredTotal;
            yield return CaptureMain(prefix + it.name + "_fp_asagi");
            if (!captureOnly)
                Check("(4) elde " + it.name + ": görüşü kapatmaz — TP (18° ve en yakın 5°) başın üstündeki ileri görüş bandı ≤ %2; FP merkez kutusu (yatay %40–60, dikey %35–65) 0° ≤ %2, 30° aşağı ≤ %10",
                    tp <= 0.02f && tpNear <= 0.02f && fp0 <= 0.02f && fp30 <= 0.10f,
                    "ileri görüş bandı TP %" + Pct(tp) + ", TP yakın %" + Pct(tpNear) + "; merkez kutusu FP 0° %" + Pct(fp0) + ", FP 30° %" + Pct(fp30) + " (tüm ekranın %" + Pct(fp30Total) + "'i)");
            orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
            orbit.Pitch = 18f;
            yield return Seconds(0.2f);
        }
        player.RespawnAt(spawnPos, spawnRot);
        yield return null;
        if (!captureOnly && coil != null)
            Check("(4b) kangal bırakılınca gerçek ölçeğine döner (FP küçültmesi yalnız elde)", (coil.transform.localScale - coilScale).sqrMagnitude < 1e-8f && !coil.IsHeld,
                "ölçek " + V(coil.transform.localScale) + " (başta " + V(coilScale) + "), durum " + coil.State);
    }

    // Eşyanın ekran payı: ana kamera iki kez çizilir (eşya açık / kapalı); farklı pikseller eşyanın kapattığı alandır (gövde arkasında
    // kalan kısım sayılmaz). Toplam pay ve merkez kutusu (yatay %40–60, dikey %35–65; nişangâhın çevresi) payı.
    // TP'de ekran merkezinde karakterin kendisi durur; görüşü asıl kapatan, başın üstündeki "ileri görüş" bandıdır (yatay %35–65,
    // aşağıdan %55–90: yol ve ufuk). TP için o band, FP için merkez kutusu sınanır. Satır 0 = görüntünün altı (GetPixels32).
    private static float measuredTotal, measuredCenter, measuredAhead;
    private static IEnumerator Measure(string label, Renderer[] rs)
    {
        var cam = Camera.main;
        const int W = 480, H = 270;
        // Poz donar; açma / kapama bir kare sonra çizime yansır (GPU Resident Drawer önbelleği)
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        var on = RenderSmall(cam, W, H);
        var states = new bool[rs.Length];
        for (int i = 0; i < rs.Length; i++) { states[i] = rs[i].enabled; rs[i].enabled = false; }
        yield return null;
        var off = RenderSmall(cam, W, H);
        for (int i = 0; i < rs.Length; i++) rs[i].enabled = states[i];
        Time.timeScale = prevScale;
        yield return null;
        int all = 0, mid = 0, midCount = 0, ahead = 0, aheadCount = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var a = on[y * W + x]; var b = off[y * W + x];
                bool diff = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 24;
                bool inMid = x >= W * 0.4f && x < W * 0.6f && y >= H * 0.35f && y < H * 0.65f;
                bool inAhead = x >= W * 0.35f && x < W * 0.65f && y >= H * 0.55f && y < H * 0.9f;
                if (inMid) midCount++;
                if (inAhead) aheadCount++;
                if (!diff) continue;
                all++;
                if (inMid) mid++;
                if (inAhead) ahead++;
            }
        measuredTotal = all / (float)(W * H);
        measuredCenter = mid / (float)Mathf.Max(1, midCount);
        measuredAhead = ahead / (float)Mathf.Max(1, aheadCount);
        Line("  ölçüm " + label + ": ekran payı %" + Pct(measuredTotal) + ", merkez kutusu payı %" + Pct(measuredCenter) + ", ileri görüş bandı %" + Pct(measuredAhead)
            + " (kamera " + V(cam.transform.position) + ", eğim " + orbit.Pitch.ToString("F0", Inv) + "°)");
    }

    private static Color32[] RenderSmall(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        var px = tex.GetPixels32();
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        return px;
    }

    // Ana kameranın gördüğünü dosyaya yazar; görüntü alınırken poz donar.
    private static IEnumerator CaptureMain(string name)
    {
        yield return null;
        var cam = Camera.main;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        yield return null;
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
        Line("  görüntü: " + OutDir + "/" + name + ".png (kamera " + V(cam.transform.position) + ", eğim " + cam.transform.eulerAngles.x.ToString("F0", Inv) + "°)");
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
    private static string Pct(float f) { return (f * 100f).ToString("F1", Inv); }
    private static string M(float m) { return m.ToString("F2", Inv) + " m"; }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
