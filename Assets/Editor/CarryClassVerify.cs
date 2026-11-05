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

// Taşıma revizyonu kabul testi (kullanıcı isteği 2026-11-05). Her sınıf için: doğru Animator durumu, hız, koşu kısıtı, avuç → eşya
// yüzeyi (< 5 cm), eşya gövdeye / başa girmiyor, zıplama, FP'de görünürlük; TP ve FP duruş + yürüyüş görüntüleri.
// Batchmode (ekran görüntüsü için -nographics OLMADAN):
//   -executeMethod CarryClassVerify.RunField   Bölüm 1: koni, çanta, valiz, kangal + yana adım
//   -executeMethod CarryClassVerify.RunGarage  Ofis-garaj: garaj eşyaları + modül paketi
// Başarıda "CARRY VERIFY OK" ve exit 0. Rapor: Captures/Carry/verify_<sahne>.txt, görüntüler: Captures/Carry/<eşya>_<kadraj>.png
// SESSİZLİK: Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0.
[InitializeOnLoad]
public static class CarryClassVerify
{
    private const string OutDir = "Captures/Carry";
    private const string StageKey = "CarryClassVerify.Stage", ReportKey = "CarryClassVerify.Report", OkKey = "CarryClassVerify.Ok", BatchKey = "CarryClassVerify.Batch", ModeKey = "CarryClassVerify.Mode", SaveBackupKey = "CarryClassVerify.SaveBackup";
    private const float GripLimit = 0.05f;      // avuç → eşya yüzeyi (m)
    private const float BodyLimit = 0.03f;      // eşya → gövde kapsülü (m)
    private const float HeadLimit = 0.03f;      // eşya → baş küresi (m)
    private const float HeadRadius = 0.235f;    // baş + kask küresi (m; merkez Head kemiği + 0.20 yukarı)

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;
    private static bool garage;

    private static ThirdPersonPlayer player;
    private static CarryHandIK ik;
    private static CrewAnimation crew;
    private static OrbitCamera orbit;
    private static Vector3 startPos;
    private static Quaternion startRot;
    private static readonly List<string> table = new List<string>();

    static CarryClassVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    public static void RunField() { Begin(true, false); }
    public static void RunGarage() { Begin(true, true); }
    [MenuItem("Rescue Team/Taşıma/Doğrula (Bölüm 1)")] public static void RunFieldMenu() { Begin(false, false); }
    [MenuItem("Rescue Team/Taşıma/Doğrula (Garaj)")] public static void RunGarageMenu() { Begin(false, true); }

    private static string SavePath { get { return Path.Combine(Application.persistentDataPath, "office_garage_v1.json"); } }

    private static void Begin(bool batch, bool inGarage)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        batchMode = batch;
        garage = inGarage;
        Line("CARRY VERIFY " + (inGarage ? "garaj " : "saha ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            string backup = "";
            if (inGarage)
            {
                EditorSceneManager.OpenScene(OfficeGaragePlayerSetup.ScenePath, OpenSceneMode.Single);
                // Oyuncunun kaydı testten etkilenmez (OfficeGarageVerify deseni)
                if (File.Exists(SavePath))
                {
                    backup = SavePath + ".carryverify_bak";
                    File.Copy(SavePath, backup, true);
                    File.Delete(SavePath);
                }
            }
            else EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
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
        SessionState.SetBool(ModeKey, inGarage);
        SessionState.SetString(StageKey, "play");
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
            garage = SessionState.GetBool(ModeKey, false);
            scenario = Scenario();
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 900f) { Fail("zaman aşımı (15 dk)"); End(); return; }
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
        ThiefDirector.SuppressedForTests = false;
        string backup = SessionState.GetString(SaveBackupKey, "");
        if (garage)
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) File.Move(backup, SavePath);
            }
            catch (Exception e) { Line("kayıt geri yüklenemedi: " + e.Message); }
        }
        SessionState.EraseString(SaveBackupKey);
        Finish();
    }

    private static void Finish()
    {
        if (table.Count > 0)
        {
            Line("TABLO (eşya | sınıf | boyut (elde) kg | yürüme | koşu):");
            foreach (var t in table) Line("  " + t);
        }
        Line(allOk ? "CARRY VERIFY OK" : "CARRY VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, garage ? "verify_garaj.txt" : "verify_saha.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "CARRY VERIFY OK" : "CARRY VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private static float Seconds(float s) { return Time.time + s; }

    private static IEnumerator Scenario()
    {
        yield return null;
        player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
        ik = player.GetComponent<CarryHandIK>();
        crew = player.GetComponent<CrewAnimation>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60; // batchmode sınırsız koşar; kare başı ölçümler 60 fps'de anlamlı
        Check("(0) kurulum: CarryHandIK ThirdPersonPlayer'a eklendi, LowerBody katmanı Crouch'un altında", ik != null && crew != null
            && player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName) >= 1 && player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName) < player.animator.GetLayerIndex(ThirdPersonPlayer.CrouchLayerName),
            "katmanlar " + player.animator.layerCount);
        if (garage) yield return GarageScenario();
        else yield return FieldScenario();
    }

    // =====================================================================================
    // Bölüm 1: koni, çanta / valiz, kangal, yana adım
    // =====================================================================================
    private static IEnumerator FieldScenario()
    {
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
        startPos = player.transform.position;
        startRot = player.transform.rotation;
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);

        var cone = TrafficCone.All.Count > 0 ? TrafficCone.All[0] : null;
        if (cone == null) Fail("sahnede koni yok");
        else yield return TestItem("koni", CarryClass.Light, () => { player.HeldCone = cone; cone.PickUp(player); }, () => { player.HeldCone = null; cone.PlaceWithPose(startPos + player.transform.right * 3f + Vector3.up * 0.1f, Quaternion.identity); },
            () => cone.target.GetComponentsInChildren<Renderer>());

        foreach (var l in LootItem.All.Where(x => x != null && x.carryable && (x.kind == LootKind.Luggage || x.kind == LootKind.PatientItem)).OrderBy(x => x.name).ToList())
        {
            var item = l;
            if (!item.CanPickUp) { Line("  atlandı (alınamaz): " + item.name); continue; }
            Vector3 at = item.transform.position;
            yield return TestItem(item.name.Replace("SM_B1_Prop_", ""), item.CarryClass, () => { player.HeldValuable = item; item.PickUp(player); }, () => { player.HeldValuable = null; item.Drop(startPos + player.transform.right * 3f, 0f); },
                () => item.GetComponentsInChildren<Renderer>());
        }

        yield return CoilScenario();
        yield return StrafeScenario();
    }

    // Kangal: Shift'te Carry_Coil_Run hızı, zıplama yok, eller kangalda
    private static IEnumerator CoilScenario()
    {
        var coil = RopeCoil.All.Count > 0 ? RopeCoil.All[0] : null;
        if (coil == null) { Fail("sahnede kangal yok"); yield break; }
        player.RespawnAt(startPos, startRot);
        yield return Seconds(0.3f);
        player.HeldCoil = coil;
        coil.PickUp(player);
        yield return Seconds(1.3f);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.5f);
        float walk = player.HorizontalVelocity.magnitude;
        player.RespawnAt(startPos, startRot);
        player.HeldCoil = coil;
        coil.PickUp(player);
        yield return Seconds(0.5f);
        player.SetRunInput(true);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.5f);
        Vector3 p0 = player.transform.position;
        float t0 = Time.time;
        float maxGrip = 0f;
        while (Time.time - t0 < 0.9f)
        {
            maxGrip = Mathf.Max(maxGrip, coil.GripErrorLeft, coil.GripErrorRight);
            yield return null;
        }
        float run = Flat(player.transform.position - p0).magnitude / (Time.time - t0);
        yield return CaptureMain("kangal_kosu_tp");
        bool state = crew.InState(CrewAnimation.StateCarryCoil);
        player.SetRunInput(false);
        player.SetMoveInput(Vector2.zero);
        float y0 = player.transform.position.y;
        yield return Seconds(0.3f);
        player.RequestJump();
        yield return Seconds(0.3f);
        bool jumped = player.transform.position.y - y0 > 0.1f;
        Check("(kangal) Shift ile koşu " + player.Rules.coilRunSpeed.ToString("F1", Inv) + " m/sn, yürüyüş 2.4, zıplama yok, eller kangalda (≤ 3 cm)",
            state && Mathf.Abs(run - player.Rules.coilRunSpeed) <= 0.12f && Mathf.Abs(walk - player.moveSpeed) <= 0.15f && !jumped && maxGrip <= 0.03f,
            "koşu " + run.ToString("F2", Inv) + ", yürüyüş " + walk.ToString("F2", Inv) + ", zıpladı " + jumped + ", tutuş " + Cm(maxGrip) + ", durum " + state);
        player.RespawnAt(startPos, startRot);
    }

    // FP'de A/D ve S: LowerBody ağırlığı ve klipler; TP'de öne yürürken ağırlık 0
    private static IEnumerator StrafeScenario()
    {
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return Seconds(0.2f);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.8f);
        float tpWeight = crew.LowerBodyWeight;
        player.SetMoveInput(Vector2.zero);
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        yield return Seconds(0.4f);
        int layer = player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName);
        player.SetMoveInput(new Vector2(1f, 0f));
        yield return Seconds(0.6f);
        float strafeW = crew.LowerBodyWeight;
        string strafeClip = TopClip(layer);
        float strafeSpeed = player.HorizontalVelocity.magnitude;
        player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.4f);
        float idleW = crew.LowerBodyWeight;
        player.RespawnAt(startPos, startRot);
        yield return Seconds(0.2f);
        player.SetMoveInput(new Vector2(0f, -1f));
        yield return Seconds(0.6f);
        float backW = crew.LowerBodyWeight;
        string backClip = TopClip(layer);
        player.SetMoveInput(Vector2.zero);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        Check("(yana adım) FP'de D → Strafe_Right, S → Walk_Back (ağırlık ~1); durunca ve TP'de öne yürürken 0",
            strafeW > 0.9f && strafeClip == "Strafe_Right" && backW > 0.9f && backClip == "Walk_Back" && idleW < 0.05f && tpWeight < 0.05f && strafeSpeed > 2f,
            "D ağırlık " + strafeW.ToString("F2", Inv) + " " + strafeClip + ", hız " + strafeSpeed.ToString("F2", Inv) + "; S " + backW.ToString("F2", Inv) + " " + backClip + "; dur " + idleW.ToString("F2", Inv) + ", TP " + tpWeight.ToString("F2", Inv));
        player.RespawnAt(startPos, startRot);
    }

    private static string TopClip(int layer)
    {
        var infos = player.animator.GetCurrentAnimatorClipInfo(layer);
        return infos.Length == 0 ? "-" : infos.OrderByDescending(c => c.weight).First().clip.name;
    }

    // =====================================================================================
    // Ofis-garaj: eşyalar ve modül paketi
    // =====================================================================================
    private static IEnumerator GarageScenario()
    {
        var ui = UnityEngine.Object.FindAnyObjectByType<ComputerUI>();
        var runtime = ui.Runtime;
        float t0 = Time.time;
        yield return (Func<bool>)(() => (runtime.Data != null && runtime.manualModules != null && runtime.manualModules.Initialized && player.HasControl) || Time.time - t0 > 6f);
        var bridge = player.GetComponent<CompanyPlayerBridge>();
        // Garaj zemininde yürüme için boş yer: minibüsün arkası
        Vector3 back = runtime.vehicle.transform.TransformPoint(new Vector3(0f, 0.02f, 6.5f));
        startRot = Quaternion.LookRotation(Flat(runtime.vehicle.transform.right), Vector3.up);
        if (Physics.Raycast(back + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) back.y = hit.point.y + 0.02f;
        startPos = back;
        player.Teleport(startPos, startRot);
        yield return Seconds(0.2f);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        var catalog = runtime.catalog;
        var cases = new (string id, CarryClass want)[]
        {
            ("grease", CarryClass.Light), ("cone", CarryClass.Light),
            ("bin", CarryClass.Medium), ("absorbent", CarryClass.Medium), ("case", CarryClass.Medium), ("fuel", CarryClass.Medium), ("toolbox", CarryClass.Medium), ("rope", CarryClass.Medium),
            ("parts", CarryClass.Heavy), ("tire", CarryClass.Heavy), ("jack", CarryClass.Heavy), ("stretcher", CarryClass.Heavy),
        };
        foreach (var c in cases)
        {
            var def = catalog.Item(c.id);
            if (def == null || def.visualPrefab == null) { Fail("katalogda yok: " + c.id); continue; }
            var go = UnityEngine.Object.Instantiate(def.visualPrefab, startPos + Vector3.up * 20f, Quaternion.identity);
            var item = go.GetComponent<CarryableItem>();
            item.definitionId = c.id;
            item.uid = "carryverify_" + c.id;
            yield return null;
            yield return TestItem(c.id, c.want, () => bridge.Hold(item), () => { bridge.TakeHeld(); UnityEngine.Object.Destroy(go); }, () => go.GetComponentsInChildren<Renderer>());
            yield return null;
        }
        // Modül paketi: ağırlığı katalogdaki modülden; paket yoksa prefab'dan
        var package = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).FirstOrDefault(x => x.GetComponent<ManualModulePackage>() != null);
        var moduleDef = catalog.modules != null && catalog.modules.Length > 0 ? catalog.modules[0] : null;
        Line("  modül: paket sahnede " + (package != null ? package.name : "yok") + ", katalog " + (moduleDef != null ? moduleDef.id + " " + moduleDef.weightKg.ToString("F0", Inv) + " kg" : "yok"));
        if (package != null && moduleDef != null)
        {
            string savedDef = package.definitionId;
            package.definitionId = "module:" + moduleDef.id;
            Vector3 savedPos = package.transform.position; Quaternion savedRot = package.transform.rotation; var savedParent = package.transform.parent;
            yield return TestItem("modul", CarryClass.Heavy, () => bridge.Hold(package), () => { bridge.TakeHeld(); package.Drop(savedPos); package.transform.SetParent(savedParent, true); package.definitionId = savedDef; }, () => package.GetComponentsInChildren<Renderer>());
        }
    }

    // =====================================================================================
    // Tek eşya sınaması
    // =====================================================================================
    private static IEnumerator TestItem(string label, CarryClass want, Action pick, Action release, Func<Renderer[]> renderers)
    {
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return Seconds(0.3f);
        pick();
        yield return null; yield return null;
        // PickUp klibi + geçiş
        yield return Seconds(1.3f);

        CarryClass cls = player.HeldClass;
        string wantState = cls == CarryClass.Light ? CrewAnimation.StateCarryCone : cls == CarryClass.Medium ? CrewAnimation.StateCarryTwoHand : CrewAnimation.StateCarryHeavy;
        bool state = crew.InState(wantState);
        var worst = new Worst();
        float t0 = Time.time;
        while (Time.time - t0 < 0.5f) { worst.Sample(); yield return null; }
        yield return CaptureFrame(label + "_tp_durus", 18f);

        // Yürüyüş: hız, tutuş, gövde
        float expectWalk = player.Rules.Speed(cls, false, player.moveSpeed, player.runSpeed);
        float expectRun = player.Rules.Speed(cls, true, player.moveSpeed, player.runSpeed);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.45f);
        Vector3 p0 = player.transform.position;
        t0 = Time.time;
        while (Time.time - t0 < 0.8f) { worst.Sample(); yield return null; }
        float walk = Flat(player.transform.position - p0).magnitude / (Time.time - t0);
        yield return CaptureFrame(label + "_tp_yuruyus", 18f);
        float ikWeight = ik.Weight;
        player.SetMoveInput(Vector2.zero);

        // Koşu
        player.Teleport(startPos, startRot);
        yield return Seconds(0.25f);
        player.SetRunInput(true);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.45f);
        p0 = player.transform.position;
        t0 = Time.time;
        while (Time.time - t0 < 0.7f) { worst.Sample(); yield return null; }
        float run = Flat(player.transform.position - p0).magnitude / (Time.time - t0);
        bool runClipOk = cls == CarryClass.Heavy || player.animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name.EndsWith("_Run") && c.weight > 0.8f);
        player.SetRunInput(false);
        player.SetMoveInput(Vector2.zero);
        player.Teleport(startPos, startRot);
        yield return Seconds(0.35f);

        // Zıplama: ağırda yok, hafif / ortada kollar eşyada kalır
        float y0 = player.transform.position.y;
        int jumps0 = crew.CarriedJumps;
        player.RequestJump();
        float maxY = y0, airGrip = 0f;
        t0 = Time.time;
        bool loopInAir = true;
        while (Time.time - t0 < 0.5f)
        {
            maxY = Mathf.Max(maxY, player.transform.position.y);
            airGrip = Mathf.Max(airGrip, ik.GripErrorLeft, ik.GripErrorRight);
            loopInAir &= crew.InState(wantState);
            yield return null;
        }
        bool jumpOk = cls == CarryClass.Heavy ? maxY - y0 < 0.05f && crew.CarriedJumps == jumps0 : maxY - y0 > 0.4f && crew.CarriedJumps == jumps0 + 1 && loopInAir && airGrip < GripLimit;
        yield return Seconds(0.5f);

        // Eğilme: ağırda yok
        player.SetCrouchInput(true);
        yield return Seconds(0.15f);
        bool crouchOk = cls == CarryClass.Heavy ? !player.IsCrouching : player.IsCrouching;
        player.SetCrouchInput(false);
        yield return Seconds(0.3f);

        // FP: duruş (0° ve 45° aşağı) ve yürüyüş
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        orbit.Pitch = 0f;
        yield return Seconds(0.3f);
        var rs = renderers();
        float view0 = VisibleFraction(rs);
        bool center0 = CoversScreenCenter(rs);
        yield return CaptureMain(label + "_fp_durus");
        orbit.Pitch = 45f;
        yield return Seconds(0.2f);
        float view45 = VisibleFraction(rs);
        yield return CaptureMain(label + "_fp_asagi");
        var fpWorst = new Worst();
        t0 = Time.time;
        while (Time.time - t0 < 0.2f) { fpWorst.Sample(); yield return null; }
        orbit.Pitch = 25f;
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.45f);
        t0 = Time.time;
        while (Time.time - t0 < 0.4f) { fpWorst.Sample(); yield return null; }
        yield return CaptureMain(label + "_fp_yuruyus");
        player.SetMoveInput(Vector2.zero);
        orbit.Pitch = 15f;
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        player.Teleport(startPos, startRot);
        yield return Seconds(0.2f);

        Vector3 size = ik.LastSize;
        table.Add(label + " | " + cls + " | " + V(size) + " | " + expectWalk.ToString("F1", Inv) + " | " + (cls == CarryClass.Heavy ? "yok" : expectRun.ToString("F1", Inv)));
        Check("(" + label + ") sınıf " + want + ", durum " + wantState,
            cls == want && state && ikWeight > 0.95f, "sınıf " + cls + ", durum " + state + ", IK " + ikWeight.ToString("F2", Inv) + ", boyut " + V(size) + (ik.SideGrip ? ", yan tutuş" : ", alt tutuş"));
        Check("(" + label + ") hız: yürüme " + expectWalk.ToString("F1", Inv) + ", koşu " + expectRun.ToString("F1", Inv),
            Mathf.Abs(walk - expectWalk) <= 0.12f && Mathf.Abs(run - expectRun) <= 0.12f && runClipOk,
            "yürüme " + walk.ToString("F2", Inv) + ", koşu " + run.ToString("F2", Inv) + ", koşu klibi " + runClipOk);
        Check("(" + label + ") avuç → eşya < " + Cm(GripLimit) + ", gövdeye < " + Cm(BodyLimit) + ", başa < " + Cm(HeadLimit),
            worst.grip < GripLimit && fpWorst.grip < GripLimit && worst.body < BodyLimit && fpWorst.body < BodyLimit && worst.head < HeadLimit && fpWorst.head < HeadLimit,
            "TP tutuş " + Cm(worst.grip) + ", gövde " + Cm(worst.body) + ", baş " + Cm(worst.head) + "; FP tutuş " + Cm(fpWorst.grip) + ", gövde " + Cm(fpWorst.body) + ", baş " + Cm(fpWorst.head) + "; erişime çekilen el " + worst.clamped + " kare");
        Check("(" + label + ") zıplama ve eğilme kuralı", jumpOk && crouchOk, "yükselme " + (maxY - y0).ToString("F2", Inv) + " m, döngüde kaldı " + loopInAir + ", havada tutuş " + Cm(airGrip) + ", eğilme " + crouchOk);
        Check("(" + label + ") FP: 45° aşağı bakınca eşya görünür; düz bakışta ekran merkezini kapatmaz",
            view45 > 0.02f && !center0, "görünür oran 0° %" + (view0 * 100f).ToString("F0", Inv) + ", 45° %" + (view45 * 100f).ToString("F0", Inv) + ", merkez " + center0);

        release();
        yield return null;
    }

    // Tutuş, gövde ve baş ölçümü (son LateUpdate pozundan)
    private class Worst
    {
        public float grip, body, head;
        public int clamped;
        public void Sample()
        {
            grip = Mathf.Max(grip, ik.GripErrorLeft, ik.GripErrorRight);
            body = Mathf.Max(body, ik.BodyOverlap);
            if (ik.ClampedHands > 0) clamped++;
            var h = player.animator.GetBoneTransform(HumanBodyBones.Head);
            if (h == null) return;
            // Baş küresi: Head kemiği + 20 cm yukarı (kask dahil); kutu içine girme derinliği
            Vector3 c = h.position + Vector3.up * 0.20f;
            Vector3 local = Quaternion.Inverse(ik.LastRotation) * (c - ik.LastCenter);
            Vector3 e = ik.LastSize * 0.5f;
            Vector3 q = new Vector3(Mathf.Clamp(local.x, -e.x, e.x), Mathf.Clamp(local.y, -e.y, e.y), Mathf.Clamp(local.z, -e.z, e.z));
            head = Mathf.Max(head, HeadRadius - Vector3.Distance(local, q));
        }
    }

    // Eşya sınırlarının 8 köşesi + merkezinden ekrana düşen oran
    private static float VisibleFraction(Renderer[] rs)
    {
        var cam = Camera.main;
        int n = 0, seen = 0;
        foreach (var r in rs)
        {
            if (r == null || !r.enabled) continue;
            Bounds b = r.bounds;
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = i == 8 ? b.center : b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 v = cam.WorldToViewportPoint(p);
                n++;
                if (v.z > cam.nearClipPlane && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f) seen++;
            }
        }
        return n > 0 ? (float)seen / n : 0f;
    }

    private static bool CoversScreenCenter(Renderer[] rs)
    {
        var cam = Camera.main;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        foreach (var r in rs)
            if (r != null && r.enabled && r.bounds.IntersectRay(ray)) return true;
        return false;
    }

    private static IEnumerator CaptureFrame(string name, float pitch)
    {
        orbit.Yaw = player.transform.eulerAngles.y + 150f; // önden-yandan: eller ve eşya görünsün
        orbit.Pitch = pitch;
        yield return Seconds(0.05f);
        yield return CaptureMain(name);
        orbit.Yaw = player.transform.eulerAngles.y;
    }

    // Ana kameranın gördüğünü dosyaya yazar; görüntü alınırken poz donar (Bolum1FirstPersonVerify deseni).
    private static IEnumerator CaptureMain(string name)
    {
        yield return null;
        var cam = Camera.main;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        yield return null;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }

    private static void Check(string label, bool ok, string detail)
    {
        allOk &= ok;
        Line((ok ? "[OK]   " : "[FAIL] ") + label + " — " + detail);
    }

    private static void Fail(string message)
    {
        allOk = false;
        Line("[FAIL] " + message);
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string Cm(float m) { return (m * 100f).ToString("F1", Inv) + " cm"; }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
