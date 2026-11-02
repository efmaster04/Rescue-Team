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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Görev köprüsü kabul testi (ofis → Bölüm 1 → ofis). Desen: Bolum1FirstPersonVerify.
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod MissionBridgeVerify.Run -logFile <dosya>
// Başarıda "MISSIONBRIDGE VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/MissionBridge/verify_report.txt (git dışı).
// SESSİZLİK: Bolum1AmbulanceAudioMute + Play boyunca AudioListener.volume = 0.
// KAYIT: Şirket kaydı geçici bir dosyaya yazılır (CompanyRuntime.SavePathOverride); oyuncunun gerçek kaydına ve izlenen
// hiçbir dosyaya dokunulmaz. Sahneler yalnız açılır, kaydedilmez.
// Sınananlar:
//  (a) veri: recovery → Bölüm 1, zorunlu halat + sedye, önerilen koni; diğer işlerde saha sahnesi yok; ek ekipman tanımları
//  (b) kilitli iş kabul edilmez, kartı "Yakında"; (c) recovery kabul edilir; halat/sedye eksikken yola çıkılamaz, tamamken çıkılır
//  (d) kararma + yükleme ekranı (iş adı, ekipman), Bölüm 1 açılır, yalnız yüklenen ekipman sahada
//  (e) görev zorla bitirilir: sonuç formülü, Enter ile ofise dönüş, ReturnFromField tam bir kez, rapor, kasa formüle göre
//  (f) Bölüm 1 köprüsüz açılınca tam ekipman
[InitializeOnLoad]
public static class MissionBridgeVerify
{
    private const string OutDir = "Captures/MissionBridge";
    private const string StageKey = "MissionBridgeVerify.Stage";
    private const string ReportKey = "MissionBridgeVerify.Report";
    private const string OkKey = "MissionBridgeVerify.Ok";
    private const string BatchKey = "MissionBridgeVerify.Batch";
    private const string FullCountsKey = "MissionBridgeVerify.FullCounts";
    private const string SavePathKey = "MissionBridgeVerify.SavePath";
    private const string OfficeScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
    private const string KeyboardName = "MissionBridgeTestKeyboard";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static MissionBridgeVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    // Sahne yüklenmeden önce: test kaydı geçici dosyaya.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void UseTempSave()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            CompanyRuntime.SavePathOverride = SessionState.GetString(SavePathKey, "");
    }

    [MenuItem("Rescue Team/Görev Köprüsü/Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        batchMode = batch;
        Line("MISSIONBRIDGE VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            // (a) veri
            var recovery = AssetDatabase.LoadAssetAtPath<JobDefinition>(MissionBridgeAssets.RecoveryJobPath);
            Check("(a) recovery işi Bölüm 1'e bağlı ve adı 'Uçurumda Mahsur Minibüs'",
                recovery != null && recovery.fieldScene == MissionBridgeAssets.FieldSceneName && recovery.title == "Uçurumda Mahsur Minibüs",
                recovery != null ? recovery.title + " → " + recovery.fieldScene : "yok");
            Check("(a) recovery: towing modülü, zorunlu halat + sedye, önerilen koni",
                recovery != null && recovery.requiredModule == "towing" && recovery.requiredEquipment.Contains("rope") &&
                recovery.requiredEquipment.Contains("stretcher") && recovery.recommendedEquipment.Contains("cone"),
                recovery != null ? recovery.requiredModule + " / " + string.Join(",", recovery.requiredEquipment) + " / " + string.Join(",", recovery.recommendedEquipment) : "");
            var otherJobs = AssetDatabase.FindAssets("t:JobDefinition", new[] { "Assets/_Project/Data/Jobs" })
                .Select(g => AssetDatabase.LoadAssetAtPath<JobDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(j => j != null && j.id != "recovery").ToList();
            Check("(a) diğer işlerde saha sahnesi yok (kilitli / Yakında)", otherJobs.Count > 0 && otherJobs.All(j => !j.HasFieldScene),
                string.Join(", ", otherJobs.Select(j => j.id)));
            var addon = Resources.Load<CatalogAddon>(CatalogAddon.ResourcePath);
            foreach (var spec in MissionBridgeAssets.Specs)
            {
                var def = addon != null ? addon.equipment.FirstOrDefault(d => d != null && d.id == spec.id) : null;
                var item = def != null && def.visualPrefab != null ? def.visualPrefab.GetComponent<CarryableItem>() : null;
                Check("(a) ek ekipman '" + spec.id + "': tanım + CarryableItem prefab'ı (" + (def != null ? def.displayName + ", ₺" + def.price + ", " + def.weightKg + " kg" : "-") + ")",
                    def != null && item != null && item.definitionId == spec.id && def.visualPrefab.GetComponent<Rigidbody>() != null &&
                    def.visualPrefab.GetComponentsInChildren<Renderer>(true).Length > 0,
                    def != null ? AssetDatabase.GetAssetPath(def.visualPrefab) : "addon " + (addon != null));
            }
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            Check("(a) ofis ve Bölüm 1 Build Settings'te", buildScenes.Contains(OfficeScenePath) && buildScenes.Contains(MissionBridgeAssets.FieldScenePath), string.Join(", ", buildScenes));

            // Bölüm 1'in tam ekipman sayısı (köprüsüz) — play'de karşılaştırılır
            EditorSceneManager.OpenScene(MissionBridgeAssets.FieldScenePath, OpenSceneMode.Single);
            var full = FieldEquipmentMap.Entries.Select(e => e.equipmentId + ":" + FieldEquipmentMap.SceneObjects(e).Count).ToArray();
            SessionState.SetString(FullCountsKey, string.Join(",", full));
            Line("  Bölüm 1 tam ekipman (sahne dosyası): " + string.Join(", ", full));

            EditorSceneManager.OpenScene(OfficeScenePath, OpenSceneMode.Single);
            string savePath = Path.Combine(Path.GetTempPath(), "MissionBridgeVerify_office_save.json");
            if (File.Exists(savePath)) File.Delete(savePath);
            SessionState.SetString(SavePathKey, savePath);
            Line("  test kaydı: " + savePath);
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
    // Play altyapısı (Bolum1FirstPersonVerify deseni)
    // =====================================================================================
    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition;
    private static Keyboard keyboard;
    private static bool settingsChanged;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackground;

    private static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.volume = 0f; // test boyunca hoparlörden ses çıkmaz
        if (scenario == null)
        {
            if (realStart < 0f) { realStart = Time.realtimeSinceStartup; return; }
            if (Time.frameCount < 3) return;
            report = new StringBuilder(SessionState.GetString(ReportKey, ""));
            allOk = SessionState.GetBool(OkKey, false);
            batchMode = SessionState.GetBool(BatchKey, true);
            scenario = Scenario();
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 900f) { Fail("zaman aşımı (15 dk)"); End(); return; }
        try
        {
            if (waitCondition is double until && Time.realtimeSinceStartup < until) return;
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
        scenario = null;
        if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
        if (settingsChanged)
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
            InputSystem.settings.backgroundBehavior = savedBackground;
            settingsChanged = false;
        }
        CompanyRuntime.SavePathOverride = null;
        string savePath = SessionState.GetString(SavePathKey, "");
        try { if (!string.IsNullOrEmpty(savePath) && File.Exists(savePath)) File.Delete(savePath); } catch { }
        Finish();
    }

    private static void Finish()
    {
        Line(allOk ? "MISSIONBRIDGE VERIFY OK" : "MISSIONBRIDGE VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "MISSIONBRIDGE VERIFY OK" : "MISSIONBRIDGE VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private static object RealSeconds(float s) { return (double)(Time.realtimeSinceStartup + s); }
    private static IEnumerator Until(Func<bool> cond, float timeoutRealSeconds, string what)
    {
        float end = Time.realtimeSinceStartup + timeoutRealSeconds;
        yield return (Func<bool>)(() => cond() || Time.realtimeSinceStartup >= end);
        if (!cond()) throw new Exception("beklenen olmadı (" + timeoutRealSeconds + " sn): " + what);
    }

    // =====================================================================================
    // Senaryo
    // =====================================================================================
    private static IEnumerator Scenario()
    {
        CompanyRuntime rt = null;
        yield return Until(() => (rt = Object.FindAnyObjectByType<CompanyRuntime>()) != null && rt.IsReady, 120f, "ofis CompanyRuntime hazır");
        string savePath = SessionState.GetString(SavePathKey, "");
        Check("ofis açıldı, şirket kaydı test dosyasında (gerçek kayda dokunulmuyor)",
            CompanyRuntime.SavePathOverride == savePath && File.Exists(savePath), savePath);
        Check("katalogda halat / sedye / koni var (CatalogAddon)", MissionBridgeAssets.Specs.All(s => rt.catalog.Item(s.id) != null),
            string.Join(", ", rt.catalog.equipment.Where(d => d != null).Select(d => d.id)));
        int startBalance = rt.Data.balance;

        // (b) kilitli iş
        var locked = rt.catalog.jobs.Where(j => j != null && !j.HasFieldScene).ToList();
        bool anyAccepted = false;
        foreach (var j in locked) if (rt.AcceptJob(j.id)) anyAccepted = true;
        Check("(b) sahnesi olmayan işler kabul edilmiyor", locked.Count > 0 && !anyAccepted && rt.Data.phase == JobPhase.None,
            locked.Count + " kilitli iş, faz " + rt.Data.phase);
        var ui = Object.FindAnyObjectByType<ComputerUI>();
        if (ui != null)
        {
            ui.Open("JobDesk");
            yield return null;
            var meta = FindText(ui.transform, "Job_" + locked[0].id, "JobMeta");
            Check("(b) İşler'de kilitli iş kartı 'Yakında'", meta != null && meta.text.Contains("Yakında"), meta != null ? meta.text : "kart bulunamadı");
            ui.Close();
        }
        else Fail("(b) ComputerUI yok");

        // (c) recovery kabul + hazırlık
        Check("(c) recovery işi kabul edildi", rt.AcceptJob("recovery") && rt.Data.phase == JobPhase.Accepted && rt.Data.activeJobId == "recovery", rt.Data.phase.ToString());
        // Towing modülü takılı ve bağlı (garajdaki elle montajın sonucu; test kısayolu)
        if (!rt.Data.ownedModules.Contains("towing")) rt.Data.ownedModules.Add("towing");
        rt.Data.installedModuleId = "towing";
        rt.Data.modulePackages.Clear();
        if (rt.manualModules != null) rt.manualModules.Initialize(rt); else rt.vehicle.SetModuleImmediate("towing");
        yield return null;
        // Araçtaki eski yük boşaltılır
        foreach (var old in rt.vehicle.LoadedItems.ToArray()) { old.gameObject.SetActive(false); Object.Destroy(old.gameObject); }
        yield return null;
        bool departEmpty = rt.Depart();
        var missing = rt.MissingPreparation();
        Check("(c) halat ve sedye yokken yola çıkılamıyor", !departEmpty && rt.Data.phase == JobPhase.Accepted &&
            missing.Contains("Ekipman: rope") && missing.Contains("Ekipman: stretcher"), string.Join(" | ", missing));
        var rope = Spawn(rt, "rope", 0);
        yield return null;
        bool departRopeOnly = rt.Depart();
        missing = rt.MissingPreparation();
        Check("(c) yalnız halat varken (sedye eksik) yola çıkılamıyor", !departRopeOnly && missing.Contains("Ekipman: stretcher") && !missing.Contains("Ekipman: rope"),
            string.Join(" | ", missing));
        var stretcher = Spawn(rt, "stretcher", 1);
        var cone = Spawn(rt, "cone", 2);
        yield return null;
        missing = rt.MissingPreparation();
        Line("  hazırlık eksikleri (halat + sedye + koni yüklü): " + (missing.Count == 0 ? "yok" : string.Join(" | ", missing)));
        var gearUids = new[] { rope.uid, stretcher.uid, cone.uid };
        bool departed = rt.Depart();
        Check("(c) halat + sedye + koni yüklüyken yola çıkıldı", departed && rt.Data.phase == JobPhase.Deployed, rt.Data.phase.ToString());
        if (!departed) yield break;

        // (d) geçiş ve Bölüm 1
        Check("(d) köprü oturumu açıldı, ekran kararıyor", MissionBridge.Active != null && MissionBridge.IsTransitioning,
            MissionBridge.Active != null ? MissionBridge.GearSummary(MissionBridge.Active) : "oturum yok");
        yield return Until(() => MissionBridgeRunner.Exists && MissionBridgeRunner.Instance.LoadingVisible, 10f, "yükleme ekranı");
        Check("(d) yükleme ekranı karanlıkta, iş adı + ekipman yazıyor",
            MissionBridgeRunner.Instance.FadeAlpha > .9f && MissionBridge.LastLoadingText.Contains("Uçurumda Mahsur Minibüs") &&
            MissionBridge.LastLoadingText.Contains("Kurtarma halatı") && MissionBridge.LastLoadingText.Contains("Sedye") && MissionBridge.LastLoadingText.Contains("Trafik konisi"),
            MissionBridge.LastLoadingText.Replace("\n", " / "));
        yield return Until(() => SceneManager.GetActiveScene().name == MissionBridgeAssets.FieldSceneName && !MissionBridge.IsTransitioning, 180f, "Bölüm 1 açıldı ve kararma bitti");
        yield return RealSeconds(0.5f);
        var tracker = FieldMissionTracker.Instance;
        var fullCounts = ParseCounts(SessionState.GetString(FullCountsKey, ""));
        var present = FieldEquipmentMap.Entries.ToDictionary(e => e.equipmentId, e => FieldEquipmentMap.SceneObjects(e).Count);
        Check("(d) Bölüm 1 açıldı, saha izleyicisi kuruldu, ekran açık", tracker != null && MissionBridgeRunner.Instance.FadeAlpha < .01f,
            "izleyici " + (tracker != null));
        Check("(d) sahada yalnız yüklenen ekipman: halat 1, sedye 1, koni 1",
            present["rope"] == 1 && present["stretcher"] == 1 && present["cone"] == 1,
            "sahada " + Fmt(present) + " (tam: " + Fmt(fullCounts) + ")");
        int expectedHidden = fullCounts.Sum(kv => kv.Value) - 3;
        Check("(d) yüklenmeyenler kaldırıldı ve eşlemeler doğru", tracker != null && tracker.HiddenCount == expectedHidden && tracker.MappedGear.Count == 3 &&
            gearUids.All(u => tracker.MappedGear.Any(m => m.gear.uid == u)),
            "kaldırılan " + (tracker != null ? tracker.HiddenCount : -1) + " / beklenen " + expectedHidden);
        int hiddenEquipValue = 0;
        foreach (var li in Object.FindObjectsByType<LootItem>(FindObjectsInactive.Include))
            if (li.kind == LootKind.Equipment && !li.gameObject.activeInHierarchy) hiddenEquipValue += li.TotalValue;
        int liveTotal = LootItem.All.Where(i => i != null).Sum(i => i.TotalValue);
        Check("(d) çalınma çubuğu toplamı kaldırılan ekipmanı saymıyor", LootRegistry.Instance != null && LootRegistry.Instance.TotalValue == liveTotal,
            "LootRegistry " + (LootRegistry.Instance != null ? LootRegistry.Instance.TotalValue : -1) + " = sahadaki " + liveTotal + " (kaldırılan ekipman ₺" + hiddenEquipValue + ")");

        // (e) olaylar + görev sonu
        var mission = MissionDirector.Instance;
        var reg = LootRegistry.Instance;
        var goods = LootItem.All.Where(i => i != null && i.kind == LootKind.Luggage).OrderBy(i => i.name).ToList();
        if (mission == null || reg == null || goods.Count < 3) throw new Exception("Bölüm 1 görev nesneleri eksik (valiz " + goods.Count + ")");
        reg.Secure(goods[0]);
        reg.Secure(goods[1]);
        int rescued = goods[0].TotalValue + goods[1].TotalValue;
        reg.ReportStolen(goods[2]);
        int stolen = goods[2].TotalValue;
        var coneMap = tracker.MappedGear.First(m => m.gear.definitionId == "cone");
        reg.ReportStolen(coneMap.go.GetComponent<LootItem>()); // koni kayboldu (hırsız kaçırdı)
        var patient = mission.patients.First(p => p != null);
        patient.AddMissedAmbulance();
        yield return null;
        Check("(e) görev henüz bitmedi (olaylar başarısızlık eşiğini aşmadı)", !mission.IsFinished, mission.ResultMessage);
        mission.ForceFinishForTest(true);
        yield return Until(() => tracker.ResultComputed, 10f, "saha sonucu hesaplandı");
        var s = MissionBridge.Active;
        var b = s.breakdown;
        int missed = mission.patients.Where(p => p != null).Sum(p => p.MissedAmbulances);
        int payment = s.jobPayment;
        int expBonus = Mathf.Min(payment, Mathf.RoundToInt(rescued * FieldPayout.RescueBonusShare));
        int expExpenses = Mathf.Min(payment, Mathf.RoundToInt(stolen * FieldPayout.StolenGoodsLiability) + missed * FieldPayout.MissedAmbulanceFine);
        float expEquipWear = FieldPayout.BaseEquipmentWear;
        float expVehicleWear = Mathf.Round((s.distanceKm * 2f * FieldPayout.VehicleWearPerKm + mission.Elapsed / 60f * FieldPayout.VehicleWearPerFieldMinute) * 10f) / 10f;
        int expPayout = payment + expBonus - expExpenses;
        Check("(e) formül: bonus = kurtarılan ₺" + rescued + " × " + FieldPayout.RescueBonusShare.ToString(Inv) + " = ₺" + expBonus,
            b.succeeded && b.bonus == expBonus, "bonus ₺" + b.bonus);
        Check("(e) formül: gider = çalınan ₺" + stolen + " × " + FieldPayout.StolenGoodsLiability.ToString(Inv) + " + " + missed + " kaçırılan × ₺" + FieldPayout.MissedAmbulanceFine + " = ₺" + expExpenses,
            b.expenses == expExpenses, "gider ₺" + b.expenses);
        Check("(e) aşınma: ekipman %" + expEquipWear.ToString(Inv) + ", araç %" + expVehicleWear.ToString(Inv),
            Mathf.Abs(b.equipmentWear - expEquipWear) < .01f && Mathf.Abs(b.vehicleWear - expVehicleWear) < .21f,
            "ekipman %" + b.equipmentWear.ToString(Inv) + ", araç %" + b.vehicleWear.ToString(Inv) + " (saha " + mission.Elapsed.ToString("F1", Inv) + " sn)");
        Check("(e) kaybolan koni consumedItemUids'te (yalnız o)", b.consumedItemUids.Count == 1 && b.consumedItemUids[0] == cone.uid,
            string.Join(",", b.consumedItemUids));
        Check("(e) sonuç paneli açık, Enter ile dönüş yazıyor", MissionBridgeRunner.Instance.EndPanelVisible && b.expectedPayout == expPayout,
            string.Join(" / ", MissionBridgeRunner.Instance.EndLines));

        // Enter (sanal klavye, gerçek giriş yolu) → ofis
        // Batchmode'da pencere odakta değil: girdi yine de oyuna gitsin (Bolum1FirstPersonVerify deseni; End'de geri alınır)
        var settings = InputSystem.settings;
        savedEditorBehavior = settings.editorInputBehaviorInPlayMode;
        savedBackground = settings.backgroundBehavior;
        settingsChanged = true;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>(KeyboardName);
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return Until(() => MissionBridge.IsTransitioning || MissionBridge.ReturnCalls > 0, 5f, "Enter ile dönüş başladı");
        yield return Until(() => SceneManager.GetActiveScene().name == "OfficeGarage" && !MissionBridge.IsTransitioning, 180f, "ofise dönüldü ve kararma bitti");
        yield return RealSeconds(1.5f);
        rt = Object.FindAnyObjectByType<CompanyRuntime>();
        Check("(e) ofiste ReturnFromField tam bir kez çağrıldı ve kabul edildi", MissionBridge.ReturnCalls == 1 && MissionBridge.LastReturnAccepted && MissionBridge.Active == null,
            "çağrı " + MissionBridge.ReturnCalls + ", kabul " + MissionBridge.LastReturnAccepted);
        Check("(e) rapor bekliyor; bonus/gider/aşınma rapora yazıldı",
            rt != null && rt.Data.phase == JobPhase.AwaitingReport && rt.Data.reportFieldBonus == expBonus && rt.Data.reportExpenses == expExpenses &&
            Mathf.Abs(rt.Data.reportEquipmentWear - expEquipWear) < .01f && rt.Data.pendingPayment == expPayout,
            rt != null ? "faz " + rt.Data.phase + ", bonus ₺" + rt.Data.reportFieldBonus + ", gider ₺" + rt.Data.reportExpenses + ", bekleyen ₺" + rt.Data.pendingPayment : "runtime yok");
        var loadedAfter = rt.vehicle.LoadedItems.ToList();
        Check("(e) kaybolan koni depodan düştü, halat ve sedye aşındı",
            !rt.Data.equipment.Any(r => r.uid == cone.uid) && loadedAfter.All(i => i.uid != cone.uid) &&
            loadedAfter.Where(i => i.uid == rope.uid || i.uid == stretcher.uid).Count() == 2 &&
            loadedAfter.Where(i => i.uid == rope.uid || i.uid == stretcher.uid).All(i => Mathf.Abs(i.durability - (100f - expEquipWear)) < .01f),
            "araçta: " + string.Join(", ", loadedAfter.Select(i => i.definitionId + " %" + i.durability.ToString("F1", Inv))));
        ui = Object.FindAnyObjectByType<ComputerUI>();
        if (ui != null)
        {
            ui.Open("Reports");
            yield return null;
            bool reportShown = ui.GetComponentsInChildren<Text>(true).Any(t => t.text == "Uçurumda Mahsur Minibüs");
            ui.Open("MailBox");
            yield return null;
            bool mailShown = ui.GetComponentsInChildren<Transform>(true).Any(t => t.name == "FieldReportMail");
            ui.Close();
            Check("(e) rapor İşler > Raporlar'da ve Posta'da görünüyor", reportShown && mailShown, "rapor " + reportShown + ", posta " + mailShown);
        }
        yield return RealSeconds(1f);
        Check("(e) tekrar çağrı yok (ReturnFromField hâlâ 1)", MissionBridge.ReturnCalls == 1, MissionBridge.ReturnCalls.ToString());
        int before = rt.Data.balance;
        bool filed = rt.FileReport();
        Check("(e) rapor onaylandı, kasa formüle göre +₺" + expPayout, filed && rt.Data.balance - before == expPayout && rt.Data.phase == JobPhase.None,
            "kasa ₺" + before + " → ₺" + rt.Data.balance + " (başlangıç ₺" + startBalance + ")");

        // (f) Bölüm 1 köprüsüz
        MissionBridge.ClearSession();
        SceneManager.LoadScene(MissionBridgeAssets.FieldSceneName);
        yield return Until(() => SceneManager.GetActiveScene().name == MissionBridgeAssets.FieldSceneName && MissionDirector.Instance != null, 180f, "Bölüm 1 köprüsüz açıldı");
        yield return RealSeconds(0.5f);
        var standalone = FieldEquipmentMap.Entries.ToDictionary(e => e.equipmentId, e => FieldEquipmentMap.SceneObjects(e).Count);
        Check("(f) köprüsüz Bölüm 1'de tam ekipman, izleyici yok",
            FieldMissionTracker.Instance == null && fullCounts.All(kv => standalone.TryGetValue(kv.Key, out int n) && n == kv.Value) && fullCounts.Values.Sum() > 3,
            "sahada " + Fmt(standalone) + " / tam " + Fmt(fullCounts));
    }

    private static CarryableItem Spawn(CompanyRuntime rt, string id, int slot)
    {
        var def = rt.catalog.Item(id);
        var go = Object.Instantiate(def.visualPrefab, rt.vehicle.transform.position + Vector3.up * 3f, Quaternion.identity);
        var item = go.GetComponent<CarryableItem>();
        item.uid = Guid.NewGuid().ToString("N");
        item.definitionId = id;
        item.durability = 100;
        rt.vehicle.RestoreCargo(item, slot);
        rt.WorldChanged();
        return item;
    }

    private static Text FindText(Transform root, string parentName, string childName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == parentName) { var c = t.Find(childName); if (c != null) return c.GetComponent<Text>(); }
        return null;
    }

    private static Dictionary<string, int> ParseCounts(string s)
    {
        var d = new Dictionary<string, int>();
        foreach (var part in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out int n)) d[kv[0]] = n;
        }
        return d;
    }

    private static string Fmt(Dictionary<string, int> d) { return string.Join(", ", d.Select(kv => kv.Key + " " + kv.Value)); }

    private static void Line(string s) { report.AppendLine(s); }
    private static void Fail(string s) { allOk = false; Line("FAIL " + s); }
    private static void Check(string what, bool ok, string detail)
    {
        if (!ok) allOk = false;
        Line((ok ? "OK   " : "FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
    }
}
