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

// FP kamera kabul testi (kullanıcı kararları 1–4, 2026-10-28): V ile birinci ↔ üçüncü şahıs.
// Batchmode (ekran görüntüsü için -nographics OLMADAN, -quit OLMADAN):
// Unity.exe -batchmode -projectPath ... -executeMethod Bolum1FirstPersonVerify.Run -logFile <dosya>
// Başarıda "FIRSTPERSON VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/Bolum1/firstperson_verify_report.txt
// Görüntüler: Captures/Bolum1/fp_ucuncu_sahis.png, fp_birinci_sahis.png, fp_birinci_sahis_asagi.png
//
// SESSİZLİK: Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0.
// Ölçülenler: ilk açılış TP (PlayerPrefs anahtarı silinir), V (sanal klavye olayı, gerçek giriş yolu) → FP, geçiş süresi ~0.25 sn
// (iki yönde), FP'de kamera baş kemiğinde ve baş gizli / TP'de görünür, FP'de gövde yaw'ı kamerayla aynı ve A/D yana adım,
// TP'de A gövdeyi yürüme yönüne döndürür (eski davranış), FP'de AimPoint kameradan ileri, PlayerPrefs ile mod hatırlama
// (yeni OrbitCamera örneği kayıtlı modu yükler). Test sonunda PlayerPrefs eski değerine döner.
[InitializeOnLoad]
public static class Bolum1FirstPersonVerify
{
    private const string OutDir = "Temp/Bolum1Verify";
    private const string StageKey = "Bolum1FirstPersonVerify.Stage";
    private const string ReportKey = "Bolum1FirstPersonVerify.Report";
    private const string OkKey = "Bolum1FirstPersonVerify.Ok";
    private const string BatchKey = "Bolum1FirstPersonVerify.Batch";
    private const string PrevPrefsKey = "Bolum1FirstPersonVerify.PrevViewMode";
    private const string KeyboardName = "FpTestKeyboard";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static Bolum1FirstPersonVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    // Öbür doğrulamalar (ambulans, animasyon, trafik, bölüm) ve batchmode: kayıtlı FP seçimi yok sayılır, üçüncü şahısta başlanır;
    // yalnız bu doğrulama kayıtlı modu okur (hatırlama testi). Sahne yüklenmeden önce, OrbitCamera.Awake'ten önce çalışır.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void IgnoreSavedModeInTests()
    {
        bool mine = SessionState.GetString(StageKey, "") == "play";
        bool other = false;
        foreach (var k in new[] { "Bolum1AmbulanceVerify.Stage", "Bolum1AnimVerify.Stage", "Bolum1TrafficVerify.Stage", "Bolum1Verify.Stage" })
            other |= !string.IsNullOrEmpty(SessionState.GetString(k, ""));
        OrbitCamera.IgnoreSavedMode = !mine && (other || Application.isBatchMode);
    }

    [MenuItem("Rescue Team/Bölüm 1/Birinci Şahıs Kamera: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        Line("FIRSTPERSON VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            var player = GameObject.Find(Bolum1SceneBuilder.CharacterName);
            var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
            bool fpBodyInScene = player != null && player.GetComponent<FirstPersonBody>() != null;
            Check("(a) sahne: ana kamerada OrbitCamera, oyuncuda PlayerInputReader (FirstPersonBody sahnede yoksa OrbitCamera çalışma zamanında ekler)",
                orbit != null && player != null && player.GetComponent<PlayerInputReader>() != null && orbit.target == player.transform,
                "OrbitCamera " + (orbit != null) + ", oyuncu " + (player != null) + ", FirstPersonBody sahnede " + fpBodyInScene);
            // İlk açılış: kayıtlı mod yok → üçüncü şahıs. Eski değer test sonunda geri yazılır.
            int prev = PlayerPrefs.HasKey(OrbitCamera.ViewModePrefsKey) ? PlayerPrefs.GetInt(OrbitCamera.ViewModePrefsKey) : -1;
            SessionState.SetInt(PrevPrefsKey, prev);
            PlayerPrefs.DeleteKey(OrbitCamera.ViewModePrefsKey);
            PlayerPrefs.Save();
            Line("  PlayerPrefs " + OrbitCamera.ViewModePrefsKey + " önceki değer: " + (prev < 0 ? "yok" : prev.ToString()) + " (silindi; ilk açılış senaryosu)");
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
    // Play aşaması altyapısı (Bolum1AnimVerify deseni)
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
        RestorePrefs();
        Finish();
    }

    private static void RestorePrefs()
    {
        int prev = SessionState.GetInt(PrevPrefsKey, -1);
        if (prev < 0) PlayerPrefs.DeleteKey(OrbitCamera.ViewModePrefsKey);
        else PlayerPrefs.SetInt(OrbitCamera.ViewModePrefsKey, prev);
        PlayerPrefs.Save();
        SessionState.EraseInt(PrevPrefsKey);
    }

    private static void Finish()
    {
        Line(allOk ? "FIRSTPERSON VERIFY OK" : "FIRSTPERSON VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "firstperson_verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "FIRSTPERSON VERIFY OK" : "FIRSTPERSON VERIFY FAILED");
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

    // ---- her kare örnekleme: geçiş kareleri ve baş gizleme tutarlılığı ----
    private static OrbitCamera orbit;
    private static FirstPersonBody fpBody;
    private static ThirdPersonPlayer player;
    private static int blendMidFrames;      // 0 < Blend < 1 olan kare sayısı (yumuşak geçiş kanıtı)
    private static int headMismatchFrames;  // baş gizli durumu kameranın baştaki konumuyla uyuşmayan kare sayısı
    private static int sampledFrames;

    private static void Sample()
    {
        if (orbit == null || fpBody == null || player == null) return;
        sampledFrames++;
        if (orbit.Blend > 0f && orbit.Blend < 1f) blendMidFrames++;
        bool shouldHide = orbit.HeadInsideCamera && !player.IsDown;
        if (fpBody.HeadHidden != shouldHide) headMismatchFrames++;
    }

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static IEnumerator Scenario()
    {
        yield return null;
        var playerGo = GameObject.Find(Bolum1SceneBuilder.CharacterName);
        player = playerGo.GetComponent<ThirdPersonPlayer>();
        var reader = playerGo.GetComponent<PlayerInputReader>();
        var combat = playerGo.GetComponent<PlayerCombat>();
        var animator = playerGo.GetComponent<Animator>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        // Ambulans, trafik ve hırsızlar test boyunca gelmesin (geçici kopya ayarlar; asset dosyaları değişmez)
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
        if (ThiefDirector.Instance != null)
        {
            var t = UnityEngine.Object.Instantiate(Bolum1ThiefBuilder.Settings);
            t.firstWaveSeconds = 99999f; t.waveIntervalSeconds = 99999f;
            ThiefDirector.Instance.Begin(t);
        }
        yield return null; yield return null;
        fpBody = playerGo.GetComponent<FirstPersonBody>();
        var head = animator.GetBoneTransform(HumanBodyBones.Head);

        // (1) İlk açılış: üçüncü şahıs, baş görünür, kamera uzakta
        float camHead0 = Vector3.Distance(Camera.main.transform.position, head.position);
        Check("(1) ilk açılış (PlayerPrefs yok): üçüncü şahıs, Blend 0, baş görünür, kamera baştan ≥ 1 m, FirstPersonBody çalışma zamanında var",
            orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && orbit.Blend == 0f && fpBody != null && !fpBody.HeadHidden && head.lossyScale.x > 0.5f && camHead0 >= 1f && !player.FaceCameraYaw,
            "mod " + orbit.Mode + ", Blend " + orbit.Blend.ToString("F2", Inv) + ", baş ölçeği " + head.lossyScale.x.ToString("F3", Inv) + ", kamera–baş " + M(camHead0) + ", FirstPersonBody " + (fpBody != null) + ", FaceCameraYaw " + player.FaceCameraYaw);

        // Sanal klavye: editörde oyun görünümü odağı yokken olaylar oyuna gitsin (U4b deseni)
        var settings = InputSystem.settings;
        var savedBehavior = settings.editorInputBehaviorInPlayMode;
        var savedBackground = settings.backgroundBehavior;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var keyboard = InputSystem.AddDevice<Keyboard>(KeyboardName);
        bool savedReader = reader.enabled;
        reader.enabled = true;
        yield return null; yield return null;
        var action = reader.ViewToggleAction;
        Check("(2) V bağlaması: ViewToggle [" + PlayerInputReader.ViewTogglePath + "] etkin ve sanal klavyeye bağlı",
            action != null && action.enabled && action.bindings[0].effectivePath == PlayerInputReader.ViewTogglePath && action.controls.Count > 0,
            action != null ? action.name + " etkin " + action.enabled + ", kontrol " + (action.controls.Count > 0 ? action.controls[0].path : "yok") : "eylem yok");

        try
        {
            yield return Until(() => player.HasControl, 3f, "oyuncu hazır");
            orbit.Yaw = player.transform.eulerAngles.y;
            orbit.Pitch = 10f;

            // (3) V → birinci şahıs: gerçek giriş yolu, ~0.25 sn yumuşak geçiş
            int presses0 = reader.ViewTogglePresses, changes0 = orbit.ModeChanges;
            blendMidFrames = 0;
            float t0 = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            bool modeNow = orbit.Mode == OrbitCamera.ViewMode.FirstPerson && orbit.Blend < 1f;
            float t1 = -1f;
            yield return (Func<bool>)(() => { if (t1 < 0f && orbit.Blend >= 1f) t1 = Time.time; return t1 >= 0f || Time.time - t0 > 2f; });
            float measured = t1 >= 0f ? t1 - t0 : -1f;
            int midFramesToFp = blendMidFrames;
            Check("(3) V → birinci şahıs: basış sayıldı, mod hemen değişti, Blend 0→1 geçişi 0.2–0.35 sn ve en az 3 ara kare (yumuşak)",
                reader.ViewTogglePresses == presses0 + 1 && orbit.ModeChanges == changes0 + 1 && modeNow && measured >= 0.2f && measured <= 0.35f && midFramesToFp >= 3
                && Mathf.Abs(orbit.LastTransitionSeconds - orbit.transitionSeconds) <= 0.1f,
                "basış " + (reader.ViewTogglePresses - presses0) + ", mod " + orbit.Mode + ", ölçülen " + measured.ToString("F3", Inv) + " sn (OrbitCamera " + orbit.LastTransitionSeconds.ToString("F3", Inv) + " sn, hedef " + orbit.transitionSeconds.ToString("F2", Inv) + "), ara kare " + midFramesToFp);
            yield return null;

            // (4) FP: kamera baş kemiğinde, baş gizli, yakın kırpma küçük, PlayerPrefs = FP
            float camHead = Vector3.Distance(Camera.main.transform.position, head.position);
            float camEye = Vector3.Distance(Camera.main.transform.position, orbit.EyePoint());
            bool hidden = fpBody.HeadHidden && head.lossyScale.x < 0.01f && orbit.HeadInsideCamera;
            int prefs = PlayerPrefs.GetInt(OrbitCamera.ViewModePrefsKey, -1);
            bool shadow = fpBody.ShadowProxy != null && fpBody.ShadowProxy.enabled && fpBody.ShadowProxy.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            Check("(4) birinci şahıs: kamera baş kemiğinin ≤ 0.25 m yakınında (göz noktasında), baş gizli (ölçek < 0.01), gölge vekili açık, yakın kırpma ≤ 0.05, PlayerPrefs = 1",
                camHead <= 0.25f && camEye <= 0.01f && hidden && prefs == 1 && Camera.main.nearClipPlane <= 0.05f && shadow,
                "kamera–baş " + M(camHead) + ", kamera–göz " + M(camEye) + ", gizli " + fpBody.HeadHidden + " (ölçek " + head.lossyScale.x.ToString("F4", Inv) + "), gölge vekili " + shadow + ", yakın kırpma " + Camera.main.nearClipPlane.ToString("F3", Inv) + ", PlayerPrefs " + prefs);

            // (5) Gövde kamera yaw'ına döner: kamerayı 90° çevir, gövde izler
            orbit.Yaw += 90f;
            yield return Seconds(0.5f);
            float yawErr = Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.y, orbit.Yaw));
            float camYawErr = Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, orbit.Yaw));
            Check("(5) birinci şahıs: kamera 90° dönünce gövde yaw'ı kamerayla eşleşir (≤ 2°)", player.FaceCameraYaw && yawErr <= 2f && camYawErr <= 0.5f,
                "FaceCameraYaw " + player.FaceCameraYaw + ", gövde–kamera " + yawErr.ToString("F2", Inv) + "°, kamera–Yaw " + camYawErr.ToString("F2", Inv) + "°");

            // (6) D basılı: yana adım (hız kameranın sağına), gövde yine kameraya dönük
            Vector3 camRight = Flat(Camera.main.transform.right).normalized;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return Seconds(0.7f);
            Vector3 vel = Flat(player.HorizontalVelocity);
            float strafeAngle = vel.magnitude > 0.05f ? Vector3.Angle(vel, camRight) : 180f;
            float yawErrMoving = Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.y, orbit.Yaw));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Seconds(0.3f);
            Check("(6) birinci şahıs: D basılı → yana adım (hız kameranın sağına ≤ 20°, ≥ 0.5 m/sn), gövde kameraya dönük kalır (≤ 3°)",
                vel.magnitude >= 0.5f && strafeAngle <= 20f && yawErrMoving <= 3f,
                "hız " + vel.magnitude.ToString("F2", Inv) + " m/sn, sağa göre " + strafeAngle.ToString("F1", Inv) + "°, gövde–kamera " + yawErrMoving.ToString("F2", Inv) + "°");

            // (7) AimPoint: kameradan ileri, kendi gövdesine çarpmaz
            if (combat != null)
            {
                Vector3 aim = combat.AimPoint();
                Vector3 toAim = aim - Camera.main.transform.position;
                float aimAngle = Vector3.Angle(toAim, Camera.main.transform.forward);
                bool selfHit = combat.LastAimHit.StartsWith(playerGo.name);
                Check("(7) birinci şahıs: AimPoint kameranın merkez ışını üzerinde (≤ 1°), ≥ 1 m ileride, kendi gövdesine çarpmaz",
                    aimAngle <= 1f && toAim.magnitude >= 1f && !selfHit, "açı " + aimAngle.ToString("F2", Inv) + "°, uzaklık " + M(toAim.magnitude) + ", çarpan: " + combat.LastAimHit);
            }
            else Line("  (7) PlayerCombat yok, AimPoint ölçülmedi");

            // (8) Eğim sınırı ve görüntüler
            orbit.Pitch = 0f;
            yield return CaptureMain("fp_birinci_sahis");
            orbit.Pitch = 89f;
            yield return null;
            float clamped = orbit.Pitch;
            yield return CaptureMain("fp_birinci_sahis_asagi");
            Check("(8) birinci şahıs eğim sınırı: 89° istenince " + orbit.firstPersonMaxPitch.ToString("F0", Inv) + "°'ye kırpılır; baş hâlâ gizli",
                Mathf.Abs(clamped - orbit.firstPersonMaxPitch) < 0.01f && fpBody.HeadHidden, "eğim " + clamped.ToString("F1", Inv) + "°, gizli " + fpBody.HeadHidden);
            orbit.Pitch = 10f;

            // (9) V → üçüncü şahıs: geçiş ~0.25 sn, baş kamera çıkınca görünür, FaceCameraYaw kapalı, PlayerPrefs = 0
            int presses1 = reader.ViewTogglePresses;
            float t2 = Time.time, t3 = -1f;
            blendMidFrames = 0;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return (Func<bool>)(() => { if (t3 < 0f && orbit.Blend <= 0f) t3 = Time.time; return t3 >= 0f || Time.time - t2 > 2f; });
            float back = t3 >= 0f ? t3 - t2 : -1f;
            int midFramesToTp = blendMidFrames;
            yield return null;
            float camHeadTp = Vector3.Distance(Camera.main.transform.position, head.position);
            Check("(9) V → üçüncü şahıs: geçiş 0.2–0.35 sn (≥ 3 ara kare), baş görünür (ölçek 1), kamera baştan ≥ 1 m, FaceCameraYaw kapalı, yakın kırpma eski değer, PlayerPrefs = 0",
                reader.ViewTogglePresses == presses1 + 1 && orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && back >= 0.2f && back <= 0.35f && midFramesToTp >= 3
                && !fpBody.HeadHidden && Mathf.Abs(head.lossyScale.x - 1f) < 0.05f && camHeadTp >= 1f && !player.FaceCameraYaw && Camera.main.nearClipPlane > 0.05f
                && PlayerPrefs.GetInt(OrbitCamera.ViewModePrefsKey, -1) == 0 && (fpBody.ShadowProxy == null || !fpBody.ShadowProxy.enabled),
                "mod " + orbit.Mode + ", ölçülen " + back.ToString("F3", Inv) + " sn, ara kare " + midFramesToTp + ", baş ölçeği " + head.lossyScale.x.ToString("F3", Inv) + ", gizli " + fpBody.HeadHidden
                + ", kamera–baş " + M(camHeadTp) + ", FaceCameraYaw " + player.FaceCameraYaw + ", yakın kırpma " + Camera.main.nearClipPlane.ToString("F2", Inv) + ", PlayerPrefs " + PlayerPrefs.GetInt(OrbitCamera.ViewModePrefsKey, -1));
            yield return CaptureMain("fp_ucuncu_sahis");

            // (10) TP davranışı aynen: A basılı → gövde yürüme yönüne (kameranın soluna) döner, kameraya değil
            orbit.Yaw = player.transform.eulerAngles.y;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
            yield return Seconds(0.7f);
            Vector3 camLeft = -Flat(Camera.main.transform.right).normalized;
            float bodyToLeft = Vector3.Angle(Flat(player.transform.forward), camLeft);
            float velToLeft = Flat(player.HorizontalVelocity).magnitude > 0.05f ? Vector3.Angle(Flat(player.HorizontalVelocity), camLeft) : 180f;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Seconds(0.3f);
            Check("(10) üçüncü şahıs değişmedi: A basılı → gövde yürüme yönüne (kameranın soluna) döner (≤ 15°), hız o yöne",
                bodyToLeft <= 15f && velToLeft <= 20f, "gövde–sol " + bodyToLeft.ToString("F1", Inv) + "°, hız–sol " + velToLeft.ToString("F1", Inv) + "°");

            // (11) Hatırlama: FP seçilince yeni bir OrbitCamera örneği FP başlar; TP seçilince TP
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Until(() => orbit.Mode == OrbitCamera.ViewMode.FirstPerson && orbit.Blend >= 1f, 2f, "FP geçişi");
            var probeFp = NewOrbitProbe(player.transform);
            yield return null;
            var modeFp = probeFp.Mode;
            float blendFp = probeFp.Blend;
            UnityEngine.Object.Destroy(probeFp.gameObject);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Until(() => orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && orbit.Blend <= 0f, 2f, "TP geçişi");
            var probeTp = NewOrbitProbe(player.transform);
            yield return null;
            var modeTp = probeTp.Mode;
            UnityEngine.Object.Destroy(probeTp.gameObject);
            yield return null;
            Check("(11) son seçim hatırlanır: FP kayıtlıyken yeni OrbitCamera FP (Blend 1, geçişsiz) başlar, TP kayıtlıyken TP",
                modeFp == OrbitCamera.ViewMode.FirstPerson && blendFp >= 1f && modeTp == OrbitCamera.ViewMode.ThirdPerson && player.FaceCameraYaw == false,
                "FP kayıtlı → " + modeFp + " (Blend " + blendFp.ToString("F2", Inv) + "), TP kayıtlı → " + modeTp + ", FaceCameraYaw " + player.FaceCameraYaw);

            Check("(12) her karede baş gizliliği kameranın baştaki konumuyla tutarlı (" + sampledFrames + " kare)", headMismatchFrames == 0, "uyumsuz kare " + headMismatchFrames);
        }
        finally
        {
            player.SetMoveInput(Vector2.zero);
            reader.enabled = savedReader;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            settings.editorInputBehaviorInPlayMode = savedBehavior;
            settings.backgroundBehavior = savedBackground;
        }
    }

    // Kayıtlı modu yüklediğini sınamak için kamerasız, ikinci bir OrbitCamera (hedef oyuncu; bir kare sonra silinir)
    private static OrbitCamera NewOrbitProbe(Transform target)
    {
        var go = new GameObject("FpVerify_OrbitProbe");
        go.SetActive(false);
        var o = go.AddComponent<OrbitCamera>();
        o.target = target;
        go.SetActive(true);
        return o;
    }

    // Ana kameranın gördüğünü (OrbitCamera konumu, URP) dosyaya yazar; görüntü alınırken poz donar.
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
    private static string M(float m) { return m.ToString("F2", Inv) + " m"; }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
