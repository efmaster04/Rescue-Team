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

// Ofis-garaj kabul testi (kullanıcı kararı: ofis-garajda bizim karakter). Desen: Bolum1FirstPersonVerify.
// Batchmode (-nographics OLMADAN, -quit OLMADAN): Unity.exe -batchmode -projectPath ... -executeMethod OfficeGarageVerify.Run -logFile <dosya>
// Başarıda "OFFICEGARAGE VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/OfficeGarage/verify_report.txt (git dışı)
// SESSİZLİK: Bolum1AmbulanceAudioMute + Play boyunca AudioListener.volume = 0.
// Kayıt: arkadaşın kayıt dosyası (persistentDataPath/office_garage_v1.json) test öncesi yedeklenir, test sonunda geri gelir.
// Sınananlar: tek oyuncu ve bizim karakter (PlayerInteractor yerine CompanyPlayerBridge), V ile görünüm, bilgisayara E → UI açık ve
// hareket kilidi (Esc ile geri), E basılı tut ile cıvata, CarryableItem alma ve minibüs yuvasına yükleme / geri alma, sağ tıkla fırlatma
// ve sert çarpmada dayanıklılık düşüşü, ölçek gözlemleri (kapı / masa / bilgisayar yüksekliği, karakter boyu).
[InitializeOnLoad]
public static class OfficeGarageVerify
{
    private const string OutDir = "Captures/OfficeGarage";
    private const string StageKey = "OfficeGarageVerify.Stage";
    private const string ReportKey = "OfficeGarageVerify.Report";
    private const string OkKey = "OfficeGarageVerify.Ok";
    private const string BatchKey = "OfficeGarageVerify.Batch";
    private const string SaveBackupKey = "OfficeGarageVerify.SaveBackup";
    private const string KeyboardName = "OgTestKeyboard", MouseName = "OgTestMouse";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static OfficeGarageVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Ofis-Garaj/Doğrula (bizim karakter)")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static string SavePath { get { return Path.Combine(Application.persistentDataPath, "office_garage_v1.json"); } }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        batchMode = batch;
        Line("OFFICEGARAGE VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            EditorSceneManager.OpenScene(OfficeGaragePlayerSetup.ScenePath, OpenSceneMode.Single);
            var players = UnityEngine.Object.FindObjectsByType<ThirdPersonPlayer>(FindObjectsSortMode.None);
            var interactors = UnityEngine.Object.FindObjectsByType<PlayerInteractor>(FindObjectsSortMode.None);
            var bridges = UnityEngine.Object.FindObjectsByType<CompanyPlayerBridge>(FindObjectsSortMode.None);
            var ui = UnityEngine.Object.FindAnyObjectByType<ComputerUI>();
            var cam = Camera.main;
            var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
            var player = players.Length == 1 ? players[0] : null;
            Check("(a) sahne: tek ThirdPersonPlayer, tek PlayerInteractor ve o CompanyPlayerBridge (aynı nesnede), arkadaşın FPS oyuncusu yok",
                players.Length == 1 && interactors.Length == 1 && bridges.Length == 1 && interactors[0] == bridges[0] && player != null && bridges[0].gameObject == player.gameObject,
                "ThirdPersonPlayer " + players.Length + ", PlayerInteractor " + interactors.Length + " (" + string.Join(",", interactors.Select(i => i.GetType().Name)) + "), köprü " + bridges.Length);
            Check("(b) ana kamerada OrbitCamera hedefi oyuncu; ComputerUI.player köprü",
                orbit != null && player != null && orbit.target == player.transform && ui != null && bridges.Length == 1 && ui.player == bridges[0],
                "OrbitCamera " + (orbit != null) + ", ComputerUI.player " + (ui != null && ui.player != null ? ui.player.GetType().Name : "yok"));
            if (player != null)
            {
                string[] need = { "PlayerInputReader", "PlayerInteraction", "PlayerCombat", "PlayerKnockdown", "PlayerHealth", "PlayerFallRespawn", "CrewAnimation", "CharacterController", "Animator" };
                var have = player.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToList();
                var missing = need.Where(n => !have.Contains(n)).ToList();
                var cc = player.GetComponent<CharacterController>();
                var combat = player.GetComponent<PlayerCombat>();
                Check("(c) oyuncu kurulumu Bölüm 1 ile aynı (bileşenler tam, ölçek 1, CharacterController 1.5 m, fırlatma ayarı var)",
                    missing.Count == 0 && player.transform.lossyScale == Vector3.one && cc != null && Mathf.Abs(cc.height - 1.5f) < 0.01f && combat != null && combat.settings != null,
                    "eksik: " + (missing.Count == 0 ? "yok" : string.Join(",", missing)) + ", ölçek " + V(player.transform.lossyScale) + ", CC boy " + (cc != null ? cc.height.ToString("F2", Inv) : "-") + ", ThiefSettings " + (combat != null && combat.settings != null));
            }
            // Kayıt dosyası: yedekle ve sil (test taze oyunla, sahnedeki ekipmanla başlar)
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
        Line(allOk ? "OFFICEGARAGE VERIFY OK" : "OFFICEGARAGE VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "OFFICEGARAGE VERIFY OK" : "OFFICEGARAGE VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private static float Seconds(float s) { return Time.time + s; }
    // Koşul sağlanana ya da süre dolana dek bekler; hata fırlatmaz (koşulu Check okur)
    private static IEnumerator WaitFor(Func<bool> cond, float timeoutGameSeconds)
    {
        float end = Time.time + timeoutGameSeconds;
        yield return (Func<bool>)(() => cond() || Time.time >= end);
    }
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
    private static PlayerCombat combat;
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
        combat = player.GetComponent<PlayerCombat>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        ui = UnityEngine.Object.FindAnyObjectByType<ComputerUI>();
        var runtime = ui.Runtime;
        yield return Until(() => runtime.Data != null && runtime.manualModules != null && runtime.manualModules.Initialized, 5f, "CompanyRuntime hazır");
        yield return Until(() => player.HasControl, 3f, "oyuncu hazır");
        yield return null; yield return null;

        yield return VerifyCeilingThrow();

        // (1) Tek oyuncu, bizim karakter, üçüncü şahıs başlangıç
        var interactors = UnityEngine.Object.FindObjectsByType<PlayerInteractor>(FindObjectsSortMode.None);
        var animator = player.GetComponent<Animator>();
        Check("(1) çalışma zamanı: tek oyuncu (ThirdPersonPlayer), PlayerInteractor olarak yalnız köprü, Humanoid Animator, üçüncü şahıs başlangıç, kontrol var",
            UnityEngine.Object.FindObjectsByType<ThirdPersonPlayer>(FindObjectsSortMode.None).Length == 1 && interactors.Length == 1 && interactors[0] == bridge
            && animator != null && animator.isHuman && orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && player.HasControl && !bridge.Locked,
            "PlayerInteractor " + interactors.Length + ", mod " + orbit.Mode + ", kilit " + bridge.Locked + ", konum " + V(player.transform.position));

        // Sanal klavye / fare (U4b deseni)
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
            // (2) V: üçüncü ↔ birinci şahıs
            orbit.Yaw = player.transform.eulerAngles.y; orbit.Pitch = 10f;
            yield return null;
            yield return CaptureMain("og_ucuncu_sahis");
            int changes0 = orbit.ModeChanges;
            yield return Press(Key.V);
            yield return Until(() => orbit.Mode == OrbitCamera.ViewMode.FirstPerson && orbit.Blend >= 1f, 2f, "FP geçişi");
            var fpBody = player.GetComponent<FirstPersonBody>();
            bool fpOk = orbit.Mode == OrbitCamera.ViewMode.FirstPerson && orbit.Blend >= 1f && fpBody != null && fpBody.HeadHidden && player.FaceCameraYaw;
            yield return CaptureMain("og_birinci_sahis");
            yield return Press(Key.V);
            yield return Until(() => orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && orbit.Blend <= 0f, 2f, "TP geçişi");
            yield return null;
            Check("(2) V ile görünüm: FP'de baş gizli ve gövde kameraya dönük, tekrar V ile TP (baş görünür)",
                fpOk && orbit.ModeChanges == changes0 + 2 && orbit.Mode == OrbitCamera.ViewMode.ThirdPerson && fpBody != null && !fpBody.HeadHidden,
                "FP " + fpOk + ", mod değişimi " + (orbit.ModeChanges - changes0) + ", son mod " + orbit.Mode);

            // (3) Bilgisayar: E → UI açık, hareket / kamera durur, Esc → geri gelir
            var computer = UnityEngine.Object.FindAnyObjectByType<OfficeComputer>();
            bool reached = false;
            yield return Approach(computer, 1.15f, r => reached = r);
            int interactions0 = bridge.Interactions;
            yield return Press(Key.E);
            yield return null;
            bool opened = ui.IsOpen && bridge.Locked && reader.InputBlocked && bridge.InputLocked;
            Vector3 p0 = player.transform.position; float yaw0 = orbit.Yaw;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(40f, 0f) });
            yield return Seconds(0.5f);
            float movedLocked = Vector3.Distance(player.transform.position, p0);
            float yawLocked = Mathf.Abs(Mathf.DeltaAngle(orbit.Yaw, yaw0));
            int attacks0 = reader.AttackPresses;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            bool noAttack = reader.AttackPresses == attacks0 && !combat.Busy;
            Check("(3a) bilgisayara E: hedef OfficeComputer, UI açıldı, girdi kilidi (köprü + okuyucu); W basılıyken hareket yok, fare kamerayı döndürmez, sol tık saldırı yapmaz",
                reached && bridge.Interactions == interactions0 + 1 && opened && movedLocked < 0.05f && yawLocked < 0.5f && noAttack && player.HorizontalVelocity.magnitude < 0.05f,
                "hedef " + reached + ", etkileşim " + (bridge.Interactions - interactions0) + ", açık " + ui.IsOpen + ", kilit " + bridge.Locked + "/" + reader.InputBlocked + ", hareket " + M(movedLocked) + ", yaw " + yawLocked.ToString("F2", Inv) + "°, saldırı " + (reader.AttackPresses - attacks0) + ", imleç " + (Application.isBatchMode ? "batchmode (ayarlanmaz)" : Cursor.lockState.ToString()));
            yield return Press(Key.Escape);
            yield return null; yield return null;
            bool closed = !ui.IsOpen && !bridge.Locked && !reader.InputBlocked;
            p0 = player.transform.position; yaw0 = orbit.Yaw;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S)); // bilgisayardan geri geri uzaklaş
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(40f, 0f) });
            yield return Seconds(0.5f);
            float movedFree = Vector3.Distance(player.transform.position, p0);
            float yawFree = Mathf.Abs(Mathf.DeltaAngle(orbit.Yaw, yaw0));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Seconds(0.3f);
            Check("(3b) Esc ile kapanınca hareket ve kamera geri gelir (S ile ≥ 0.3 m, fare ile ≥ 2°)",
                closed && movedFree >= 0.3f && yawFree >= 2f, "kapalı " + closed + ", hareket " + M(movedFree) + ", yaw " + yawFree.ToString("F1", Inv) + "°");

            // (4) Cıvata: alet çantası elde, bağlantıyı ayır, cıvata 1'i E basılı tutarak sök ve tekrar sık, bağlantıyı tak
            var system = runtime.manualModules;
            var toolbox = runtime.EquipmentItems.FirstOrDefault(x => x.definitionId == "toolbox");
            bool tbReached = false;
            if (toolbox != null) yield return Approach(toolbox, 0.95f, r => tbReached = r);
            yield return Press(Key.E);
            yield return null;
            bool held = toolbox != null && bridge.HeldItem == toolbox && ReferenceEquals(player.HeldProp, bridge) && toolbox.transform.IsChildOf(player.transform);
            var crew = player.GetComponent<CrewAnimation>();
            yield return Seconds(0.3f);
            Check("(4a) alet çantası: hedeflenir, E ile ele alınır (köprü HeldItem, oyuncu HeldProp, eşya karakterin altında), tek el taşıma pozu (Carry 2)",
                tbReached && held && crew != null && crew.Carry == 2,
                "hedef " + tbReached + ", elde " + held + ", Carry " + (crew != null ? crew.Carry.ToString() : "-") + ", yer " + (toolbox != null ? V(toolbox.transform.position) : "-"));
            bool canWork = system.CanWork(bridge, out string workReason);
            var connector = GameObject.Find("ModuleConnector")?.GetComponent<ModuleFastener>();
            var bolt = GameObject.Find("ModuleBolt_1")?.GetComponent<ModuleFastener>();
            bool secured0 = system.IsSecured;
            Vector3 workPos = runtime.vehicle.transform.TransformPoint(new Vector3(.25f, .01f, 3.88f));
            bool connReached = false, boltReached = false;
            if (connector != null) yield return AimFrom(workPos, connector, r => connReached = r);
            yield return Press(Key.E);
            yield return null;
            bool disconnected = system.Current != null && !system.Current.record.connected;
            if (bolt != null) yield return AimFrom(workPos, bolt, r => boltReached = r);
            int bolts0 = system.Current != null ? system.Current.record.bolts : -1;
            yield return Hold(Key.E, 0.95f);
            yield return null;
            int bolts1 = system.Current != null ? system.Current.record.bolts : -1;
            bool loosened = bolts0 >= 0 && (bolts0 & 1) != 0 && (bolts1 & 1) == 0;
            // kısa basış sıkmaz (basılı tutma gerekir)
            yield return Press(Key.E);
            yield return Seconds(0.3f);
            int boltsShort = system.Current != null ? system.Current.record.bolts : -1;
            yield return Hold(Key.E, 0.95f);
            yield return null;
            int bolts2 = system.Current != null ? system.Current.record.bolts : -1;
            bool tightened = (bolts2 & 1) != 0 && boltsShort == bolts1;
            if (connector != null) yield return AimFrom(workPos, connector, r => connReached &= r);
            yield return Press(Key.E);
            yield return null;
            Check("(4b) modül cıvatası: bağlantıyı E ile ayır, cıvata 1'i E basılı tutarak sök ve tekrar sık (kısa basış sıkmaz), bağlantıyı tak → modül yine sabit",
                secured0 && canWork && connReached && boltReached && disconnected && loosened && tightened && system.IsSecured,
                "başta sabit " + secured0 + ", çalışılabilir " + canWork + " " + workReason + ", bağlantı hedefi " + connReached + ", cıvata hedefi " + boltReached + ", ayrıldı " + disconnected + ", cıvata " + bolts0 + "→" + bolts1 + "→" + boltsShort + "→" + bolts2 + ", sonda sabit " + system.IsSecured);

            // (5) Minibüs yük yuvası: arka kapılar açılır, alet çantası yüklenir, E ile geri alınır
            yield return EnsureHeld(toolbox);
            var vehicle = runtime.vehicle;
            var rearDoors = vehicle.doors.Where(d => d.name.StartsWith("Rear")).ToArray();
            yield return Teleport(new Vector3(4.65f, .01f, 3.70f), vehicle.transform.position); // kapıların önünden çekil
            foreach (var d in rearDoors) if (!d.IsOpen) d.Interact(bridge);
            yield return Until(() => vehicle.RearAccessOpen, 4f, "arka kapılar açık");
            var vehicleCollider = vehicle.GetComponent<Collider>() != null ? vehicle.GetComponent<Collider>() : vehicle.rearStepCollision;
            bool rearReached = false;
            yield return AimFrom(new Vector3(4.65f, .01f, 3.70f), vehicle, r => rearReached = r, vehicleCollider);
            string loadPrompt = bridge.LastPrompt;
            yield return Press(Key.E);
            yield return null;
            bool loaded = toolbox != null && toolbox.Loaded && toolbox.CargoSlot >= 0 && bridge.HeldItem == null && player.HeldProp == null && vehicle.LoadedItems.Contains(toolbox);
            yield return Seconds(0.2f);
            yield return AimFrom(new Vector3(4.65f, .01f, 3.70f), vehicle, r => rearReached &= r, vehicleCollider);
            yield return Press(Key.E);
            yield return null;
            bool unloaded = toolbox != null && !toolbox.Loaded && bridge.HeldItem == toolbox && !vehicle.LoadedItems.Contains(toolbox);
            Check("(5) minibüs yük yuvası: arka kapılar açık, araç hedeflenir (\"" + loadPrompt + "\"), E ile alet çantası yuvaya yüklenir (Loaded, yuva ≥ 0, el boş), E ile geri alınır",
                vehicle.RearAccessOpen && rearReached && loaded && unloaded && crew != null && crew.Carry == 2,
                "kapılar " + vehicle.RearAccessOpen + ", hedef " + rearReached + ", yüklendi " + loaded + ", geri alındı " + unloaded + ", Carry " + (crew != null ? crew.Carry.ToString() : "-"));

            // (6) Fırlatma: sağ tık basılı tut (nişan) ve bırak; eşya uçar, sert çarpınca dayanıklılık düşer
            yield return EnsureHeld(toolbox);
            Vector3 throwPos = new Vector3(6.30f, .01f, -3.15f);
            yield return Teleport(throwPos, new Vector3(6.30f, 0f, -4.6f));
            orbit.Pitch = 45f; // zemine, ~1.3 m ileri
            yield return null; yield return null;
            float dur0 = toolbox != null ? toolbox.durability : -1f;
            int hits0 = toolbox != null ? toolbox.ImpactHits : 0;
            int throws0 = combat.PropThrows;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            yield return Seconds(0.3f);
            bool aiming = combat.Aiming && crew != null && crew.Carry == 2;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return Until(() => combat.PropThrows == throws0 + 1, 2f, "fırlatma");
            yield return null;
            var body = toolbox != null ? toolbox.GetComponent<Rigidbody>() : null;
            bool flying = toolbox != null && bridge.HeldItem == null && player.HeldProp == null && toolbox.transform.parent == null && body != null && !body.isKinematic && toolbox.Thrown;
            float speed0 = body != null ? body.linearVelocity.magnitude : 0f;
            yield return Until(() => toolbox != null && toolbox.ImpactHits > hits0, 3f, "sert çarpma");
            float dur1 = toolbox != null ? toolbox.durability : -1f;
            yield return Until(() => toolbox != null && !toolbox.Thrown, 6f, "eşya durdu");
            Check("(6) fırlatma: sağ tık nişan (Throw_Aim, Carry 2), bırakınca eşya elden çıkıp uçar (kinematik değil, ebeveynsiz), sert çarpmada (≥ " + CarryableItem.ImpactDamageSpeed.ToString("F1", Inv) + " m/sn) dayanıklılık " + CarryableItem.ImpactDamage.ToString("F0", Inv) + " düşer, eşya durur",
                aiming && flying && speed0 > CarryableItem.ImpactDamageSpeed && toolbox != null && toolbox.ImpactHits > hits0 && Mathf.Abs(dur0 - dur1 - CarryableItem.ImpactDamage) < 0.01f && !toolbox.Thrown && toolbox.transform.position.y > -1f,
                "nişan " + aiming + ", uçuş " + flying + ", hız " + speed0.ToString("F1", Inv) + " m/sn, çarpma hızı " + (toolbox != null ? toolbox.LastImpactSpeed.ToString("F1", Inv) : "-") + ", dayanıklılık " + dur0.ToString("F1", Inv) + "→" + dur1.ToString("F1", Inv) + ", yay " + combat.LastAimArc + ", son yer " + (toolbox != null ? V(toolbox.transform.position) : "-"));

            // (7) Elde eşyayla E (hedef yokken) bırakır; F / R tuşları işlevsiz
            yield return Approach(toolbox, 0.95f, r => { });
            yield return Press(Key.E);
            yield return null;
            bool reHeld = bridge.HeldItem == toolbox;
            orbit.Yaw += 180f; yield return null; yield return null; // boşluğa bak
            yield return Press(Key.F);
            yield return Press(Key.R);
            yield return null;
            bool fIgnored = bridge.HeldItem == toolbox;
            var targetNow = bridge.FindTarget();
            yield return Press(Key.E);
            yield return null;
            bool dropped = targetNow == null ? bridge.HeldItem == null && !toolbox.Loaded : true;
            Check("(7) E al / bırak: eşya tekrar alınır, F ve R bir şey yapmaz, hedef yokken E bırakır",
                reHeld && fIgnored && dropped, "alındı " + reHeld + ", F/R yok sayıldı " + fIgnored + ", hedef " + (targetNow != null ? targetNow.name : "yok") + ", bırakıldı " + (bridge.HeldItem == null));

            // (8) Ölçek gözlemleri (rapor; başarı ölçütü değil)
            ScaleNotes(computer);
            yield return GarageRegression(runtime);
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

    private static CarryableItem TestCargo(CompanyRuntime runtime, List<GameObject> cleanup)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cleanup.Add(go);
        go.name = "GarageVerify_Cargo";
        go.transform.position = new Vector3(15, 1, 15);
        go.transform.localScale = Vector3.one * .12f;
        var item = go.AddComponent<CarryableItem>();
        item.definitionId = "toolbox";
        item.uid = Guid.NewGuid().ToString("N");
        return item;
    }

    private static IEnumerator VerifyCeilingThrow()
    {
        var ceiling = GarageCeiling.ColliderInstance;
        bool built = ceiling != null && ceiling.name == "GarageCeilingCollider" && !ceiling.isTrigger
            && ceiling.GetComponent<Rigidbody>() == null && ceiling.gameObject.layer == 0
            && Mathf.Abs(ceiling.bounds.min.y - GarageCeiling.UndersideY) < .01f;
        Check("(tavan) Play sahnesinde görünmez statik GarageCeilingCollider var", built,
            ceiling != null ? "alt yüz " + M(GarageCeiling.UndersideY) + ", boyut " + V(ceiling.size) : "collider yok");
        if (!built) yield break;

        // Her tavan kutusu ayrı denenir: farklı yükseklikteki tavanlar ayrı gruptadır, biri öbürünün altına inmemeli.
        float highestUnderside = float.MinValue;
        foreach (var box in GarageCeiling.Colliders) highestUnderside = Mathf.Max(highestUnderside, box.bounds.min.y);
        Check("(tavan) en yüksek kutu ColliderInstance, alt yüzü UndersideY",
            Mathf.Abs(highestUnderside - GarageCeiling.UndersideY) < .01f, GarageCeiling.Colliders.Count + " kutu, en yüksek alt yüz " + M(highestUnderside));
        foreach (var box in GarageCeiling.Colliders) yield return VerifyCeilingThrowUnder(box);
    }

    // Kutunun altında üstü bu kutuya açık, boş bir nokta bulur (kolon, duvar, ring vb. üstüne denk gelmesin).
    private static bool FindOpenPointUnder(BoxCollider ceiling, out Vector3 point)
    {
        Bounds roof = ceiling.bounds;
        float y = roof.min.y - 2f;
        for (float x = roof.min.x + .5f; x <= roof.max.x - .5f; x += .5f)
        {
            for (float z = roof.min.z + .5f; z <= roof.max.z - .5f; z += .5f)
            {
                var candidate = new Vector3(x, y, z);
                if (Physics.CheckBox(candidate, Vector3.one * .15f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(candidate, Vector3.up, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider == ceiling)
                {
                    point = candidate;
                    return true;
                }
            }
        }
        point = default;
        return false;
    }

    private static IEnumerator VerifyCeilingThrowUnder(BoxCollider ceiling)
    {
        float underside = ceiling.bounds.min.y;
        string label = "(tavan " + M(underside) + ") ";
        bool open = FindOpenPointUnder(ceiling, out Vector3 ahead);
        Check(label + "kutunun altında üstü açık boş nokta bulundu", open, open ? V(ahead) : "boş nokta yok");
        if (!open) yield break;

        // Ayrı bir CarryableItem, mevcut alet çantası ve kayıt akışını bozmaz.
        GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prop.name = "GarageVerify_CarryableItem_Ceiling";
        prop.transform.localScale = Vector3.one * .2f;
        prop.transform.position = ahead;
        var item = prop.AddComponent<CarryableItem>();
        var body = prop.GetComponent<Rigidbody>();
        try
        {
            item.Throw(ahead, Quaternion.identity, Vector3.up * 25f);
            float end = Time.time + 2f;
            float maximumY = prop.transform.position.y;
            bool descending = false;
            float previousY = maximumY;
            while (Time.time < end)
            {
                yield return null;
                float y = prop.transform.position.y;
                maximumY = Mathf.Max(maximumY, y);
                if (y < previousY - .005f && body.linearVelocity.y < 0f) descending = true;
                previousY = y;
            }
            Check(label + "CarryableItem 25 m/sn yukarı fırlatılınca 2 sn içinde tavanı aşmaz ve geri düşer",
                maximumY <= underside + .5f && descending && body.collisionDetectionMode != CollisionDetectionMode.Discrete,
                "en yüksek Y " + M(maximumY) + ", alt yüz " + M(underside) +
                ", iniş " + descending + ", çarpışma modu " + body.collisionDetectionMode);
        }
        finally { UnityEngine.Object.Destroy(prop); }
    }

    private static IEnumerator GarageRegression(CompanyRuntime runtime)
    {
        var vehicle = runtime.vehicle;
        var system = runtime.manualModules;
        var catalog = runtime.catalog;
        var jobs = catalog.jobs;
        var modules = catalog.modules;
        bool autoSave = runtime.autoSave;
        var cleanup = new List<GameObject>();
        var testJob = UnityEngine.Object.Instantiate(catalog.Job("recovery"));
        var testModule = UnityEngine.Object.Instantiate(catalog.Module("towing"));
        // Saha köprüsünü tetiklemeden kapının gerçek Depart çağrısını sınar; kaynak asset değişmez.
        testJob.fieldScene = "";
        testJob.requiredEquipment = new string[0];
        catalog.jobs = jobs.Select(j => j.id == testJob.id ? testJob : j).ToArray();
        catalog.modules = modules.Select(m => m.id == testModule.id ? testModule : m).ToArray();
        runtime.autoSave = false;
        try
        {
            if (bridge.HeldItem) bridge.TakeHeld().Drop(new Vector3(12, .3f, 12));
            yield return Teleport(new Vector3(6.3f, .02f, -3.2f), vehicle.transform.position);
            foreach (var door in vehicle.doors) if (door.IsOpen) door.Interact(bridge);
            yield return Until(() => vehicle.doors.All(d => d.IsClosed), 5f, "regresyon: araç kapıları kapalı");
            foreach (var item in vehicle.LoadedItems.ToArray()) item.Drop(new Vector3(12, .3f, 12));
            runtime.Data.installedModuleId = "";
            runtime.Data.modulePackages.Clear();
            system.Initialize(runtime);
            runtime.Data.activeJobId = testJob.id;
            runtime.Data.phase = JobPhase.Accepted;
            yield return null;
            var first = TestCargo(runtime, cleanup);
            bridge.Hold(first);
            bool loaded = vehicle.TryLoad(bridge);
            Check("(9a) modülsüz TryLoad", !system.IsSecured && vehicle.InstalledModuleId == "" && loaded && vehicle.LoadedItems.Contains(first) && bridge.HeldItem == null,
                "yüklendi " + loaded + ", yüklü " + vehicle.LoadedItems.Count());
            var missing = runtime.MissingPreparation();
            Check("(9c) modülsüz Depart reddedilir", !runtime.Depart() && runtime.Data.phase == JobPhase.Accepted && missing.Any(s => s.Contains("modül") || s.Contains("Modül")), string.Join(" | ", missing));

            var loose = TestCargo(runtime, cleanup);
            Vector3 cargoCenter = vehicle.transform.TransformPoint(vehicle.CargoBounds.center);
            loose.Drop(cargoCenter);
            float t0 = Time.time;
            yield return WaitFor(() => loose.Loaded, 2f);
            Check("(9b) yük hacmine bırakılan eşya fizik adımlarında sabitlenir", loose.Loaded && vehicle.LoadedItems.Contains(loose), "Loaded " + loose.Loaded + ", süre " + (Time.time - t0).ToString("F2", Inv) + " sn");
            var thrown = TestCargo(runtime, cleanup);
            thrown.Throw(cargoCenter, Quaternion.identity, vehicle.transform.forward * .3f);
            thrown.GetComponent<Rigidbody>().useGravity = false;
            t0 = Time.time;
            yield return WaitFor(() => thrown.Loaded, 2f);
            float thrownElapsed = Time.time - t0;
            Check("(9b) yük hacmindeki hareketli eşya (0.3 m/sn) 0.5 sn içinde sabitlenir", thrown.Loaded && !thrown.Thrown && vehicle.LoadedItems.Contains(thrown) && thrownElapsed < 1f, "Loaded " + thrown.Loaded + ", süre " + thrownElapsed.ToString("F2", Inv) + " sn");
            var outside = TestCargo(runtime, cleanup);
            outside.Drop(vehicle.transform.TransformPoint(vehicle.CargoBounds.max + Vector3.right));
            yield return Seconds(.6f);
            Check("(9b) hacim dışındaki eşya serbest kalır", !outside.Loaded, "Loaded " + outside.Loaded);

            var liftHeight = typeof(GarageLift).GetProperty("CarriageHeight");
            liftHeight.SetValue(vehicle.lift, .4f);
            var onLift = TestCargo(runtime, cleanup);
            onLift.Drop(cargoCenter);
            onLift.GetComponent<Rigidbody>().useGravity = false;
            yield return Seconds(.6f);
            bool stayedFree = !onLift.Loaded;
            liftHeight.SetValue(vehicle.lift, 0f);
            yield return WaitFor(() => onLift.Loaded, 2f);
            Check("(9b) lift yukarıdayken yükleme yok, indirilince yüklenir", stayedFree && onLift.Loaded, "serbest kaldı " + stayedFree + ", Loaded " + onLift.Loaded);

            while (vehicle.LoadedItems.Count() < vehicle.cargoSockets.Length)
            {
                var item = TestCargo(runtime, cleanup);
                bridge.Hold(item);
                if (!vehicle.TryLoad(bridge)) break;
            }
            var overflow = TestCargo(runtime, cleanup);
            int notices = 0;
            Action<string> notice = s => { if (s.Contains("boş yük noktası")) notices++; };
            runtime.Notice += notice;
            try { overflow.Drop(cargoCenter); yield return WaitFor(() => notices >= 1, 2f); yield return Seconds(.5f); }
            finally { runtime.Notice -= notice; }
            Check("(9b) modülsüz bütün soketler kullanılabilir; dolu araç reddi tek bildirim", vehicle.LoadedItems.Count() == vehicle.cargoSockets.Length && !overflow.Loaded && !overflow.GetComponent<Rigidbody>().isKinematic && notices == 1,
                "yüklü " + vehicle.LoadedItems.Count() + ", bildirim " + notices);
            overflow.Drop(new Vector3(12, .3f, 12));

            var package = system.Packages.First(p => p.record.moduleId == "towing");
            bridge.Hold(package.Item);
            bool staged = system.Stage(package, bridge);
            var wrench = runtime.EquipmentItems.First(i => i.definitionId == "toolbox" && !cleanup.Contains(i.gameObject));
            bridge.Hold(wrench);
            bool bolts = true;
            for (int i = 0; i < 4; i++) bolts &= system.ToggleBolt(i, bridge);
            bool connected = system.ToggleConnection(bridge);
            bridge.TakeHeld().Drop(new Vector3(12, .3f, 12));
            missing = runtime.MissingPreparation();
            Check("(9c) yüklü araçta modül takılır, yük korunur ve fazla yuva çıkışı engeller", staged && bolts && connected && system.IsSecured && vehicle.LoadedItems.Count() == vehicle.cargoSockets.Length && missing.Any(s => s.StartsWith("Fazla ekipman:")) && !runtime.Depart(), string.Join(" | ", missing));
            testModule.payloadKg = 1;
            Check("(9c) fazla ağırlık çıkışı engeller", runtime.MissingPreparation().Any(s => s.StartsWith("Fazla ağırlık:")) && !runtime.Depart(), string.Join(" | ", runtime.MissingPreparation()));
            testModule.payloadKg = modules.First(m => m.id == "towing").payloadKg;
            foreach (var item in vehicle.LoadedItems.Skip(1).ToArray()) item.Drop(new Vector3(12, .3f, 12));

            var boarding = UnityEngine.Object.FindAnyObjectByType<GarageBoardingDoor>();
            bool reached = false;
            if (boarding)
            {
                // Binme noktası hangi yandaysa (şoför kapısı) oyuncu o yanda, aracın 1 m dışında durur
                float side = Mathf.Sign(vehicle.transform.InverseTransformPoint(boarding.transform.position).x);
                Vector3 position = boarding.transform.position + vehicle.transform.right * side * 1f;
                position.y = .02f;
                yield return AimFrom(position, boarding, ok => reached = ok);
            }
            bool ready = runtime.MissingPreparation().Count == 0;
            string prompt = boarding ? boarding.Prompt(bridge) : "yok";
            if (reached) yield return Press(Key.E);
            Check("(9d) şoför kapısı E menzilinde; hazırlık tamken E → Deployed", boarding && reached && ready && prompt == "E — Minibüse bin (göreve çık)" && runtime.Data.phase == JobPhase.Deployed,
                prompt + ", hedef " + reached + ", faz " + runtime.Data.phase + ", eksik " + string.Join(" | ", runtime.MissingPreparation()));

            var meshObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(meshObject);
            meshObject.transform.position = new Vector3(15, 2, 15);
            var mesh = UnityEngine.Object.Instantiate(meshObject.GetComponent<MeshFilter>().sharedMesh);
            mesh.UploadMeshData(true);
            meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            var highlight = meshObject.AddComponent<InteractionHighlight>();
            int errors = 0;
            Application.LogCallback log = (message, trace, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning) errors++; };
            Application.logMessageReceived += log;
            try
            {
                typeof(InteractionHighlight).GetMethod("Build", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(highlight, new object[] { meshObject.transform });
                yield return null;
                Check("(9e) okunamayan mesh vurgusunda hata/uyarı yok, kaynak mesh çizilir", !mesh.isReadable && errors == 0 && highlight.CopyCount == 1 && highlight.Copies.First().GetComponent<MeshFilter>().sharedMesh == mesh,
                    "hata/uyarı " + errors + ", kopya " + highlight.CopyCount);
            }
            finally { Application.logMessageReceived -= log; UnityEngine.Object.Destroy(mesh); }
        }
        finally
        {
            catalog.jobs = jobs; catalog.modules = modules;
            // Test işi kayıtta kalmasın (otomatik kayıt geri açılıyor)
            runtime.Data.phase = JobPhase.None; runtime.Data.activeJobId = "";
            runtime.autoSave = autoSave;
            typeof(GarageLift).GetProperty("CarriageHeight").SetValue(vehicle.lift, 0f);
            foreach (var go in cleanup) if (go) UnityEngine.Object.Destroy(go);
            UnityEngine.Object.Destroy(testJob); UnityEngine.Object.Destroy(testModule);
        }
    }

    // Karakter boyu, kapı / masa / bilgisayar yükseklikleri: chibi oranla oynanış sorunu var mı (rapor)
    private static void ScaleNotes(OfficeComputer computer)
    {
        var cc = player.GetComponent<CharacterController>();
        var head = player.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        float headY = head != null ? head.position.y - player.transform.position.y : -1f;
        Line("  ölçek: CharacterController boy " + cc.height.ToString("F2", Inv) + " m, baş kemiği zeminden " + headY.ToString("F2", Inv) + " m, kamera pivotu " + orbit.pivotHeight.ToString("F2", Inv) + " m (arkadaşın FPS gözü 1.57 m, kapsül 1.72 m)");
        foreach (var door in UnityEngine.Object.FindObjectsByType<OfficeDoor>(FindObjectsSortMode.None))
        {
            var b = door.GetComponent<BoxCollider>();
            if (b != null) Line("  ölçek: ofis kapısı " + door.name + " kanat " + b.bounds.size.y.ToString("F2", Inv) + " m yüksek, " + Mathf.Max(b.bounds.size.x, b.bounds.size.z).ToString("F2", Inv) + " m geniş (kapsül çapı " + (cc.radius * 2f).ToString("F2", Inv) + ")");
        }
        if (computer != null)
        {
            var c = computer.GetComponentInChildren<Collider>();
            if (c != null) Line("  ölçek: bilgisayar collider merkezi zeminden " + c.bounds.center.y.ToString("F2", Inv) + " m (etkileşim ışını pivot " + orbit.pivotHeight.ToString("F2", Inv) + " m'den)");
        }
        foreach (var bench in UnityEngine.Object.FindObjectsByType<RepairWorkbench>(FindObjectsSortMode.None))
        {
            var c = bench.GetComponentInChildren<Collider>();
            if (c != null) Line("  ölçek: tezgâh " + bench.name + " üst yüzey " + c.bounds.max.y.ToString("F2", Inv) + " m");
        }
        foreach (var btn in UnityEngine.Object.FindObjectsByType<LiftButton>(FindObjectsSortMode.None))
            Line("  ölçek: lift düğmesi " + btn.name + " yükseklik " + btn.transform.position.y.ToString("F2", Inv) + " m");
    }

    // ---- yardımcılar ----
    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private static IEnumerator Hold(Key key, float seconds)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return Seconds(seconds);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    // Oyuncuyu konuma ışınlar, gövdeyi ve kamerayı (yaw/pitch, pivot → hedef) hedefe çevirir
    private static IEnumerator Teleport(Vector3 position, Vector3 lookAt)
    {
        Vector3 at = position;
        foreach (var h in Physics.RaycastAll(position + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
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

    private static IEnumerator AimFrom(Vector3 position, InteractableBehaviour target, Action<bool> result, Collider col = null)
    {
        if (col == null) col = target.GetComponent<Collider>();
        if (col == null) col = target.GetComponentInChildren<Collider>();
        Vector3 center = col.bounds.center;
        // Verilen yer; olmazsa hedeften aynı yönde başka uzaklıklar (bizim pivot 1.3 m, arkadaşın gözü 1.57 m: açı farkı)
        Vector3 away = position - center; away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
        away.Normalize();
        var tries = new List<Vector3> { position };
        foreach (float d in new[] { 0.7f, 0.9f, 1.15f, 1.4f, 1.7f }) { var p = center + away * d; p.y = position.y; tries.Add(p); }
        InteractableBehaviour found = null;
        foreach (var p in tries)
        {
            yield return Teleport(p, center);
            found = bridge.FindTarget();
            if (found == target) break;
            Line("  hedef uyuşmadı: " + target.name + " istendi, " + (found != null ? found.name : "yok") + " bulundu (konum " + V(player.transform.position) + ", hedef merkezi " + V(center) + ", boyut " + V(col.bounds.size) + ", eğim " + orbit.Pitch.ToString("F1", Inv) + "°); ışın: " + RayHits());
        }
        result(found == target);
    }

    // Tanı: köprünün ışını boyunca ilk çarpanlar
    private static string RayHits()
    {
        var cam = Camera.main.transform;
        Vector3 pivot = player.transform.position + Vector3.up * orbit.pivotHeight;
        Vector3 origin = cam.position + cam.forward * Mathf.Max(0f, Vector3.Dot(pivot - cam.position, cam.forward));
        var hits = Physics.RaycastAll(origin, cam.forward, 3f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Take(5);
        return "köken " + V(origin) + " → " + string.Join("; ", hits.Select(h => h.collider.name + " " + h.distance.ToString("F2", Inv)));
    }

    // Alet çantası elde değilse yeniden al (önceki adımın hatası sonraki adımı bozmasın)
    private static IEnumerator EnsureHeld(CarryableItem item)
    {
        if (item == null || bridge.HeldItem == item) yield break;
        if (bridge.HeldItem != null) { bridge.DropHeld(); yield return null; }
        bool ok = false;
        yield return Approach(item, 0.95f, r => ok = r);
        if (ok) yield return Press(Key.E);
        yield return null;
        Line("  alet çantası yeniden alındı: " + (bridge.HeldItem == item));
    }

    // Hedefin çevresinde 8 yönden yaklaşır; kapsül katı nesnenin içine girmeyen ve ışının hedefi bulduğu ilk yerde durur
    private static IEnumerator Approach(InteractableBehaviour target, float distance, Action<bool> result)
    {
        if (target == null) { result(false); yield break; }
        var col = target.GetComponent<Collider>() != null ? target.GetComponent<Collider>() : target.GetComponentInChildren<Collider>();
        Vector3 center = col.bounds.center;
        Vector3 from = player.transform.position - center; from.y = 0f;
        var dirs = new List<Vector3>();
        if (from.sqrMagnitude > 1e-4f) dirs.Add(from.normalized);
        dirs.AddRange(new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized, new Vector3(1, 0, -1).normalized, new Vector3(-1, 0, -1).normalized });
        var cc = player.GetComponent<CharacterController>();
        foreach (var d in dirs)
        {
            Vector3 pos = center + d * distance; pos.y = Mathf.Max(0.01f, center.y - 1.5f);
            yield return Teleport(pos, center);
            bool inside = false;
            foreach (var c in Physics.OverlapSphere(cc.bounds.center, cc.radius, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(player.transform) && c.GetComponentInParent<CarryableItem>() == null && Vector3.Distance(c.ClosestPoint(cc.bounds.center), cc.bounds.center) < 0.02f) inside = true;
            if (!inside && bridge.FindTarget() == target) { result(true); yield break; }
        }
        Line("  yaklaşılamadı: " + target.name + " (son hedef " + (bridge.FindTarget() != null ? bridge.FindTarget().name : "yok") + ")");
        result(false);
    }

    // Ana kameranın gördüğünü dosyaya yazar (URP); görüntü alınırken poz donar.
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
}
