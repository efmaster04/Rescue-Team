using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CompanySim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Görev köprüsü: ofis → yola çık → saha (Bölüm 1) → sonuç → ofis.
//  1. Ofiste "Göreve çık" (CompanyRuntime.Depart) sahası olan bir iş için FieldDeparture yayınlar. Köprü minibüse
//     yüklenen eşyaları (uid, tanım, dayanıklılık) oturuma yazar, ekranı karartır, yükleme ekranında iş adını ve
//     ekipmanı gösterir, saha sahnesini açar.
//  2. Saha sahnesi açılınca (sceneLoaded, Start'lardan önce) FieldMissionTracker kurulur: yüklenmeyen ekipman kaldırılır,
//     olaylar sayılır. Görev bitince sonuç (FieldPayout) hesaplanır ve sonuç ekranında "Enter: Ofise dön" görünür.
//  3. Dönüşte ekran kararır, ofis sahnesi açılır, CompanyRuntime hazır olunca ReturnFromField TAM BİR KEZ çağrılır,
//     oturum kapanır. Rapor İşler > Raporlar'da (ve Posta'da duyuru olarak) görünür.
// Oturum yokken (Bölüm 1 editörde ya da doğrulama betiğinde doğrudan açılırsa) köprü hiçbir şeye dokunmaz: tam ekipman.
public static class MissionBridge
{
    [Serializable]
    public sealed class Gear
    {
        public string uid, definitionId, label;
        public float durability;
    }

    public sealed class Session
    {
        public string jobId, jobTitle, fieldScene, officeScene;
        public int jobPayment;
        public float distanceKm;
        public readonly List<Gear> gear = new List<Gear>();
        public FieldJobResult result;            // görev bitince dolar; R ile yeniden başlatılırsa sıfırlanır
        public FieldPayout.Breakdown breakdown;
        public bool delivered;
    }

    public const float FadeSeconds = 0.6f;
    public const float MinLoadingSeconds = 1.2f;
    public const float OfficeReadyTimeout = 15f;
    // Dönüş tuşu: R yeniden başlatma olarak kalır; Enter Bölüm 1'de başka bir işe bağlı değil.
    public static readonly Key[] ReturnKeys = { Key.Enter, Key.NumpadEnter };
    public const string ReturnHint = "Enter: Ofise dön";

    public static Session Active { get; private set; }
    public static Session LastSession { get; private set; }   // teslim edilen son oturum (doğrulama ve hata ayıklama)
    public static bool IsTransitioning { get; private set; }
    public static int ReturnCalls { get; private set; }       // ReturnFromField çağrı sayısı (doğrulama)
    public static bool LastReturnAccepted { get; private set; }
    public static string LastLoadingText { get; private set; } = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = null;
        LastSession = null;
        IsTransitioning = false;
        ReturnCalls = 0;
        LastReturnAccepted = false;
        LastLoadingText = "";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CompanyRuntime.FieldDeparture -= OnDeparture;
        CompanyRuntime.FieldDeparture += OnDeparture;
    }

    // Doğrulama: oturumu elle kapatır (Bölüm 1'i köprüsüz açmak için).
    public static void ClearSession() { Active = null; }

    // ---- 1. ofis → saha ----
    private static void OnDeparture(CompanyRuntime runtime, JobDefinition job)
    {
        if (IsTransitioning || runtime == null || job == null) return;
        if (!Application.CanStreamedLevelBeLoaded(job.fieldScene))
        {
            runtime.Notify("Saha sahnesi bulunamadı: " + job.fieldScene);
            Debug.LogError("MissionBridge: '" + job.fieldScene + "' Build Settings'te yok.");
            return;
        }
        var s = new Session
        {
            jobId = job.id, jobTitle = job.title, fieldScene = job.fieldScene,
            officeScene = runtime.gameObject.scene.name, jobPayment = job.payment, distanceKm = job.distanceKm,
        };
        if (runtime.vehicle != null)
            foreach (var item in runtime.vehicle.LoadedItems)
                s.gear.Add(new Gear { uid = item.uid, definitionId = item.definitionId, label = GearLabel(runtime, item.definitionId), durability = item.durability });
        Active = s;
        MissionBridgeRunner.Run(GoToField(s));
    }

    private static string GearLabel(CompanyRuntime runtime, string definitionId)
    {
        var def = runtime.catalog != null ? runtime.catalog.Item(definitionId) : null;
        if (def != null && !string.IsNullOrEmpty(def.displayName)) return def.displayName;
        var entry = FieldEquipmentMap.Find(definitionId);
        return entry != null ? entry.label : definitionId;
    }

    // "Halat, Sedye, Trafik konisi ×2"
    public static string GearSummary(Session s)
    {
        if (s == null || s.gear.Count == 0) return "Yüklenen ekipman yok";
        return "Yüklenen ekipman: " + string.Join(", ", s.gear.GroupBy(g => g.label)
            .Select(g => g.Count() > 1 ? g.Key + " ×" + g.Count() : g.Key));
    }

    private static IEnumerator GoToField(Session s)
    {
        IsTransitioning = true;
        var ui = MissionBridgeRunner.Instance;
        ui.HideEndPanel();
        yield return ui.Fade(1f, FadeSeconds);
        LastLoadingText = s.jobTitle + "\n" + GearSummary(s);
        ui.ShowLoading(s.jobTitle, GearSummary(s));
        float start = Time.realtimeSinceStartup;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        var op = SceneManager.LoadSceneAsync(s.fieldScene, LoadSceneMode.Single);
        while (op != null && !op.isDone) yield return null;
        while (Time.realtimeSinceStartup - start < MinLoadingSeconds) yield return null;
        ui.HideLoading();
        yield return ui.Fade(0f, FadeSeconds);
        IsTransitioning = false;
    }

    // ---- 2. saha sahnesi açıldı (Awake/OnEnable sonrası, Start öncesi) ----
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Active == null || mode != LoadSceneMode.Single) return;
        if (scene.name != Active.fieldScene) return;
        Active.result = null;       // yeniden başlatma (R) önceki sonucu siler
        Active.breakdown = null;
        if (MissionBridgeRunner.Exists) MissionBridgeRunner.Instance.HideEndPanel();
        var go = new GameObject("MissionBridge_FieldTracker");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<FieldMissionTracker>().Setup(Active);
    }

    // Saha görevi bitti: sonucu oturuma yazar (FieldMissionTracker çağırır).
    public static void SetResult(Session s, FieldPayout.Breakdown b)
    {
        if (s == null || b == null) return;
        s.breakdown = b;
        s.result = new FieldJobResult
        {
            resultId = Guid.NewGuid().ToString("N"), jobId = s.jobId, succeeded = b.succeeded,
            bonus = b.bonus, expenses = b.expenses, equipmentWear = b.equipmentWear, vehicleWear = b.vehicleWear,
            consumedItemUids = new List<string>(b.consumedItemUids),
        };
    }

    // ---- 3. saha → ofis ----
    public static bool ReturnToOffice()
    {
        if (Active == null || Active.result == null || Active.delivered || IsTransitioning) return false;
        MissionBridgeRunner.Run(GoToOffice(Active));
        return true;
    }

    private static IEnumerator GoToOffice(Session s)
    {
        IsTransitioning = true;
        var ui = MissionBridgeRunner.Instance;
        ui.HideEndPanel();
        yield return ui.Fade(1f, FadeSeconds);
        string body = (s.result.succeeded ? "Görev tamamlandı" : "Görev başarısız") + " · Ofise dönülüyor";
        LastLoadingText = s.jobTitle + "\n" + body;
        ui.ShowLoading(s.jobTitle, body);
        float start = Time.realtimeSinceStartup;
        Time.timeScale = 1f;
        var op = SceneManager.LoadSceneAsync(s.officeScene, LoadSceneMode.Single);
        while (op != null && !op.isDone) yield return null;
        // Ofis kaydı CompanyRuntime.Start'ta yüklenir; rapor ondan sonra işlenir.
        CompanyRuntime runtime = null;
        while (Time.realtimeSinceStartup - start < OfficeReadyTimeout)
        {
            runtime = UnityEngine.Object.FindAnyObjectByType<CompanyRuntime>();
            if (runtime != null && runtime.IsReady) break;
            yield return null;
        }
        if (runtime != null && runtime.IsReady && !s.delivered) TryDeliver(s, runtime);
        else if (!s.delivered)
        {
            // Oturum açık kalır; sonuç ofis hazır olunca yeniden denenir (ödeme kaybolmasın)
            Debug.LogError("MissionBridge: ofiste CompanyRuntime hazır olmadı; saha sonucu işlenemedi, yeniden denenecek.");
            MissionBridgeRunner.Run(RetryDelivery(s));
        }
        while (Time.realtimeSinceStartup - start < MinLoadingSeconds) yield return null;
        ui.HideLoading();
        yield return ui.Fade(0f, FadeSeconds);
        IsTransitioning = false;
    }

    // Teslimi dener; kabul de ret de kalıcı sayılır (oturum kapanır), ret bir kez loglanır.
    private static void TryDeliver(Session s, CompanyRuntime runtime)
    {
        s.delivered = true;
        ReturnCalls++;
        LastReturnAccepted = runtime.ReturnFromField(s.result);
        if (!LastReturnAccepted) Debug.LogError("MissionBridge: ReturnFromField sonucu reddetti (iş " + s.jobId + ", faz " + runtime.Data.phase + "); sonuç LastSession'da bırakıldı.");
        LastSession = s;
        if (Active == s) Active = null;
    }

    // Ofis hazır olana kadar saniyede bir teslimi yeniden dener (yeni bir oturum başlarsa vazgeçer).
    private static IEnumerator RetryDelivery(Session s)
    {
        while (Active == s && !s.delivered)
        {
            yield return new WaitForSecondsRealtime(1f);
            if (Active != s || s.delivered) yield break;
            var runtime = UnityEngine.Object.FindAnyObjectByType<CompanyRuntime>();
            if (runtime != null && runtime.IsReady && runtime.gameObject.scene.name == s.officeScene) TryDeliver(s, runtime);
        }
    }

    // Dönüş tuşuna basıldı mı (FieldMissionTracker okur).
    public static bool ReturnKeyPressed()
    {
        // Bütün klavyelere bakılır (Keyboard.current son kullanılan cihazdır; birden çok klavye olabilir)
        foreach (var device in InputSystem.devices)
        {
            if (!(device is Keyboard kb)) continue;
            foreach (var k in ReturnKeys) if (kb[k].wasPressedThisFrame) return true;
        }
        return false;
    }
}
