using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Yakın kamera saydamlığı kabul testi (CameraProximityFade, RescueTeam/CameraFadeLit). Desen: OfficeExpansionVerify.
// Batchmode (-nographics OLMADAN, -quit OLMADAN): Unity.exe -batchmode -projectPath ... -executeMethod CharacterFadeVerify.Run -logFile C:/.../x.log
// Başarıda "CHARACTERFADE VERIFY OK" ve exit 0, hatada exit 1. Rapor: Captures/TpSaydam/character_fade_report.txt, görüntüler aynı klasörde.
// SESSİZLİK: Bolum1AmbulanceAudioMute + Play boyunca AudioListener.volume = 0. Ofis kaydı ve PlayerPrefs test sonunda geri gelir.
// Sınananlar: uzakta opak ve kaynak malzeme, yakında saydam (piksel ölçümüyle), yumuşak geçiş, tekerlek sınırında etkisiz, FP'de etkisiz
// (baş gizleme ve gölge vekili çalışır), elde eşya saydamlaşır ve bırakınca döner, başka karakter etkilenmez, gölge kalır, malzeme asset'i
// değişmez, sahne geçişinde kopya sızmaz. Ofisin 5 odasında orta / köşe görüntüsü, garaj ve Bölüm 1 görüntüsü.
[InitializeOnLoad]
public static class CharacterFadeVerify
{
    private const string OutDir = "Captures/TpSaydam";
    private const string StageKey = "CharacterFadeVerify.Stage";
    private const string ReportKey = "CharacterFadeVerify.Report";
    private const string OkKey = "CharacterFadeVerify.Ok";
    private const string BatchKey = "CharacterFadeVerify.Batch";
    private const string SaveBackupKey = "CharacterFadeVerify.SaveBackup";
    private const string PrevModeKey = "CharacterFadeVerify.PrevMode";
    private const string PrevZoomKey = "CharacterFadeVerify.PrevZoom";
    private const int SmallW = 640, SmallH = 360;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;

    static CharacterFadeVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Kamera/Yakın saydamlık: Doğrula")]
    public static void RunMenu() { Begin(false); }

    public static void Run() { Begin(true); }

    // Build'de ayıklanma denetimi: küçük sahneyle Windows oyuncusu kurar (Temp altına). Editör logunda "Compiling shader RescueTeam/CameraFadeLit"
    // satırları kalan varyant sayılarını verir; 0 kalan geçiş varsa shader build'de çalışmaz. Dikkat: build Assets/Settings ve
    // GraphicsSettings dosyalarını yeniden yazar (prefilter alanları); denetimden sonra git restore ile geri alınır, commit edilmez.
    public static void BuildCheck()
    {
        var result = UnityEditor.Build.Reporting.BuildResult.Unknown;
        try
        {
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Temp/TpSaydamBuild/Check.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            result = build.summary.result;
            Debug.Log("CHARACTERFADE BUILD " + result + ", hata " + build.summary.totalErrors + ", süre " + build.summary.totalTime);
        }
        catch (Exception e) { Debug.LogError("CHARACTERFADE BUILD: " + e); }
        Debug.Log(result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? "CHARACTERFADE VERIFY OK (build)" : "CHARACTERFADE VERIFY FAILED (build)");
        EditorApplication.Exit(result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static string SavePath { get { return Path.Combine(Application.persistentDataPath, "office_garage_v1.json"); } }

    private static void Begin(bool batch)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        batchMode = batch;
        Line("CHARACTERFADE VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            EditorSceneManager.OpenScene(OfficeGaragePlayerSetup.ScenePath, OpenSceneMode.Single);
            var shader = Shader.Find(CameraProximityFade.ShaderName);
            string shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : "";
            Check("(0) shader bulunur, destekli, Resources altında (build'e girer); gerekli geçişler var", shader != null && shader.isSupported && shaderPath.Contains("/Resources/") && HasPasses(shader),
                shaderPath + ", desteklenir " + (shader != null && shader.isSupported));
            SessionState.SetInt(PrevModeKey, PlayerPrefs.HasKey(OrbitCamera.ViewModePrefsKey) ? PlayerPrefs.GetInt(OrbitCamera.ViewModePrefsKey) : -1);
            SessionState.SetFloat(PrevZoomKey, PlayerPrefs.HasKey(OrbitCamera.ZoomPrefsKey) ? PlayerPrefs.GetFloat(OrbitCamera.ZoomPrefsKey) : -1f);
            string backup = "";
            if (File.Exists(SavePath))
            {
                backup = SavePath + ".verify_bak";
                File.Copy(SavePath, backup, true);
                File.Delete(SavePath);
            }
            SessionState.SetString(SaveBackupKey, backup);
        }
        catch (Exception e)
        {
            Fail("edit aşaması: " + e);
            Finish();
            return;
        }
        OrbitCamera.IgnoreSavedMode = true;
        SessionState.SetString(ReportKey, report.ToString());
        SessionState.SetBool(OkKey, allOk);
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetString(StageKey, "play");
        Bolum1AmbulanceAudioMute.Apply();
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    private static bool HasPasses(Shader shader)
    {
        var names = new HashSet<string>();
        var data = ShaderUtil.GetShaderData(shader);
        for (int i = 0; i < data.GetSubshader(0).PassCount; i++) names.Add(data.GetSubshader(0).GetPass(i).Name);
        return names.Contains("ForwardLit") && names.Contains("ShadowCaster") && names.Contains("DepthOnly") && names.Contains("DepthNormals");
    }

    // =====================================================================================
    // Play altyapısı
    // =====================================================================================
    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition;
    private static bool savedAsync;

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
            savedAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // ilk çizimde yer tutucu shader görünmesin
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
        ShaderUtil.allowAsyncCompilation = savedAsync;
        OrbitCamera.IgnoreSavedMode = false;
        RestorePrefs();
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

    private static void RestoreSave()
    {
        string backup = SessionState.GetString(SaveBackupKey, "");
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) File.Move(backup, SavePath);
        }
        catch (Exception e) { Line("  UYARI: kayıt geri yüklenemedi: " + e.Message); }
        SessionState.EraseString(SaveBackupKey);
    }

    private static void Finish()
    {
        RestoreSave();
        Line(allOk ? "CHARACTERFADE VERIFY OK" : "CHARACTERFADE VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "character_fade_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "CHARACTERFADE VERIFY OK" : "CHARACTERFADE VERIFY FAILED");
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
    private static OrbitCamera orbit;
    private static CameraProximityFade fade;
    private static Renderer[] body;
    private static Dictionary<Renderer, Material[]> bodySource;
    private static readonly Dictionary<Material, string> assetBaseline = new Dictionary<Material, string>();

    private static IEnumerator Bind()
    {
        yield return null;
        player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
        orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
        if (player == null || orbit == null) throw new Exception("oyuncu ya da kamera yok");
        yield return Until(() => player.GetComponent<CameraProximityFade>() != null, 3f, "bileşen kuruldu");
        fade = player.GetComponent<CameraProximityFade>();
        body = player.GetComponentsInChildren<Renderer>().Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly).ToArray();
        bodySource = new Dictionary<Renderer, Material[]>();
        if (fade.CurrentFade <= 0f) foreach (var r in body) bodySource[r] = r.sharedMaterials;
    }

    private static IEnumerator Scenario()
    {
        yield return Bind();
        yield return Until(() => player.HasControl, 8f, "oyuncu hazır");
        BaselineAssets();
        ReportMaterials("Ofis-Garaj");

        // (1) Bileşen yalnız kameranın hedefinde
        var all = UnityEngine.Object.FindObjectsByType<CameraProximityFade>(FindObjectsSortMode.None);
        Check("(1) bileşen çalışma zamanında yalnız kameranın hedefine eklendi", all.Length == 1 && all[0].transform == orbit.target, "adet " + all.Length);

        // (2) Eşik eğrisi
        float d14 = fade.FadeForDistance(fade.fadeStartDistance), d07 = fade.FadeForDistance(fade.fadeEndDistance), d20 = fade.FadeForDistance(2f), mid = fade.FadeForDistance((fade.fadeStartDistance + fade.fadeEndDistance) * 0.5f);
        bool monotonic = true; float prev = -1f;
        for (float d = 3f; d >= 0.3f; d -= 0.02f) { float f = fade.FadeForDistance(d); if (f < prev - 1e-5f) monotonic = false; prev = f; }
        Check("(2) eşik eğrisi: başlangıçta 0, bitişte 1, arası yumuşak ve tek yönlü", d14 == 0f && d07 == 1f && d20 == 0f && mid > 0.3f && mid < 0.7f && monotonic,
            "başlangıç " + F(fade.fadeStartDistance) + " m → " + F(d14) + ", bitiş " + F(fade.fadeEndDistance) + " m → " + F(d07) + ", orta " + F(mid) + ", en az görünürlük " + F(fade.minVisibility));

        // (3) 5 oda: orta ve köşe (OfficeExpansionVerify ile aynı kadrajlar)
        var samples = new[]
        {
            new Sample("ana_ofis_orta", new Vector3(-2.4f, 0f, -2.6f), new Vector3(-2.4f, 1f, 1f)),
            new Sample("ana_ofis_kose", new Vector3(-3.9f, 0f, -4.6f), new Vector3(-1.1f, 0.8f, -0.1f)),
            new Sample("mutfak_orta", new Vector3(-7.3f, 0f, -1.9f), new Vector3(-7.3f, 1f, 1f)),
            new Sample("mutfak_kose", new Vector3(-5.3f, 0f, -0.3f), new Vector3(-8.2f, 0.7f, -3.0f)),
            new Sample("depo_orta", new Vector3(-2.4f, 0f, 3.2f), new Vector3(-4.6f, 1f, 3.2f)),
            new Sample("depo_kose", new Vector3(-1.0f, 0f, 1.7f), new Vector3(-3.3f, 0.8f, 4.0f)),
            new Sample("soyunma_orta", new Vector3(-7.3f, 0f, 3.2f), new Vector3(-9.6f, 1f, 3.2f)),
            new Sample("soyunma_kose", new Vector3(-7.3f, 0f, 1.7f), new Vector3(-7.6f, 0.8f, 4.2f)),
            new Sample("ring_orta", new Vector3(-8.6f, 0f, 8.6f), new Vector3(-4.0f, 1f, 8.6f)),
            new Sample("ring_kose", new Vector3(-8.6f, 0f, 6.0f), new Vector3(-4.6f, 0.9f, 8.8f)),
            new Sample("garaj_raf_dibi", new Vector3(3.4f, 0f, 2.2f), new Vector3(0f, 1.4f, 3.3f)),
            new Sample("garaj_minibus_arasi", new Vector3(5f, 0f, 2.6f), new Vector3(-1f, 1f, 2.6f)),
            new Sample("garaj_orta_a", new Vector3(12f, 0f, 9f), new Vector3(12f, 1f, 2f)),
            new Sample("garaj_orta_b", new Vector3(9f, 0f, -3f), new Vector3(2f, 1f, -3f)),
        };
        var bad = new List<string>(); int nearCount = 0, farCount = 0;
        foreach (var s in samples)
        {
            yield return Teleport(s.position, s.lookAt);
            yield return Seconds(1.6f);
            float dist = orbit.CurrentDistance, expected = fade.FadeForDistance(dist);
            bool far = expected <= 0f;
            bool ok = Mathf.Abs(fade.CurrentFade - expected) < 0.02f && (far ? BodyIsSource() : BodyIsFaded());
            if (far) farCount++; else nearCount++;
            if (!ok) bad.Add(s.name);
            Line("  TP " + s.name + ": uzaklık " + F(dist) + " m, saydamlık " + F(fade.CurrentFade) + ", görünürlük " + F(fade.Visibility) + ", kaynak malzeme " + BodyIsSource());
            yield return CaptureMain("tp_" + s.name);
        }
        Check("(3) ofis ve garajda saydamlık uzaklığa uyar, uzakta kaynak malzemeler", bad.Count == 0 && nearCount > 0 && farCount > 0, "yakın " + nearCount + ", uzak " + farCount + (bad.Count > 0 ? ", hatalı " + string.Join(",", bad) : ""));

        // (4) Oda ortası: bileşen hiçbir pikseli değiştirmez
        yield return Teleport(samples[0].position, samples[0].lookAt);
        yield return Seconds(1.6f);
        var withFade = RenderSmall();
        fade.enabled = false;
        var withoutFade = RenderSmall();
        fade.enabled = true;
        int midDiff = DiffCount(withFade, withoutFade);
        Check("(4) uzakta görüntü bileşenle ve bileşensiz birebir aynı", fade.CurrentFade == 0f && midDiff == 0 && BodyIsSource(), "uzaklık " + F(orbit.CurrentDistance) + " m, farklı piksel " + midDiff);
        // Tekerlekle en yakın (2.25 m): yine opak
        orbit.SetZoom(orbit.minZoom, true);
        yield return Seconds(0.4f);
        Check("(4b) tekerlekle en yakın serbest uzaklıkta opak", fade.CurrentFade == 0f && BodyIsSource(), "uzaklık " + F(orbit.CurrentDistance) + " m");
        orbit.SetZoom(1f, true);

        // (5) Uzaklık yavaşça azalınca görünen karakter yumuşakça azalır (distance yazılarak; engel yok)
        yield return Seconds(0.2f);
        float savedDistance = orbit.distance;
        var ramp = new List<string>(); var ratios = new List<float>(); float maxStep = 0f, last = fade.CurrentFade; int steps = 0;
        foreach (float target in new[] { 1.6f, 1.4f, 1.2f, 1.05f, 0.9f, 0.7f, 0.6f })
        {
            orbit.distance = target;
            float settle = Time.time + 0.5f;
            while (Time.time < settle)
            {
                yield return null;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(fade.CurrentFade - last) / Mathf.Max(0.0001f, Time.deltaTime));
                last = fade.CurrentFade; steps++;
            }
            if (target == 1.05f) yield return CaptureMain("tp_rampa_1_05m");
            if (target == 0.7f) yield return CaptureMain("tp_rampa_0_70m");
            // Karakter yaklaşınca büyür: ölçüt aynı kadrajdaki opak alana oran
            float shownFade = fade.CurrentFade, seen = Coverage();
            fade.enabled = false;
            float opaque = Coverage();
            fade.enabled = true;
            float ratio = seen / Mathf.Max(0.0001f, opaque);
            ratios.Add(ratio);
            ramp.Add(F(target) + "m:" + F(shownFade) + "/" + F(ratio));
            // Ölçüm için kapatılan bileşen sıfırdan toparlanır; hız sınırı burada da ölçülür
            last = 0f;
            float recover = Time.time + 0.3f;
            while (Time.time < recover)
            {
                yield return null;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(fade.CurrentFade - last) / Mathf.Max(0.0001f, Time.deltaTime));
                last = fade.CurrentFade; steps++;
            }
        }
        orbit.distance = savedDistance;
        yield return Seconds(1.2f);
        bool decreasing = ratios[1] > 0.95f && ratios[3] < ratios[1] && ratios[3] > 0.3f && ratios[5] < ratios[3] && ratios[5] < 0.35f;
        Check("(5) rampa: saydamlık hız sınırını aşmaz, görünen pay (opak alana oran) uzaklıkla azalır", maxStep <= fade.fadeSpeed + 0.01f && decreasing && fade.CurrentFade == 0f, "en büyük hız " + F(maxStep) + "/sn (" + steps + " kare), uzaklık:saydamlık/oran " + string.Join(" ", ramp));

        // (6) Köşede: piksel ölçümü (opak hâlin alanına oran), derinlik sıralaması için görsel
        yield return Teleport(samples[1].position, samples[1].lookAt);
        yield return Seconds(1.6f);
        float nearFaded = Coverage();
        fade.enabled = false;
        float nearOpaque = Coverage();
        yield return CaptureMain("tp_ana_ofis_kose_opak_karsilastirma");
        fade.enabled = true;
        yield return Seconds(0.4f);
        Check("(6) köşede karakter neredeyse görünmez: görüş açılır, opak hâlin alanına oran küçük", fade.CurrentFade >= 0.99f && nearOpaque > 0.2f && nearFaded < nearOpaque * 0.3f, "uzaklık " + F(orbit.CurrentDistance) + " m, opak alan " + F(nearOpaque) + ", saydam alan " + F(nearFaded) + ", görünürlük " + F(fade.Visibility));

        // (7) V: FP'de saydamlık yok, baş gizli, gölge vekili açık; dönünce saydamlık geri gelir, baş ölçeği eski
        var fpBody = player.GetComponent<FirstPersonBody>();
        Vector3 headScale = fpBody.Head != null ? fpBody.Head.localScale : Vector3.one;
        orbit.ToggleMode();
        float increase = 0f, lastFade = fade.CurrentFade;
        while (orbit.Blend < 1f)
        {
            yield return null;
            increase = Mathf.Max(increase, fade.CurrentFade - lastFade);
            lastFade = fade.CurrentFade;
        }
        yield return null; yield return null;
        bool fpOk = fade.CurrentFade == 0f && BodyIsSource() && fade.FadedRendererCount == 0;
        bool headHidden = fpBody.HeadHidden && fpBody.Head.localScale.x < headScale.x * 0.01f && fpBody.ShadowProxy != null && fpBody.ShadowProxy.enabled;
        yield return CaptureMain("fp_ana_ofis_kose");
        Check("(7) FP: saydamlık etkisiz (kaynak malzemeler), geçişte artmaz; baş gizli, gölge vekili açık", fpOk && headHidden && increase <= 0f, "saydamlık " + F(fade.CurrentFade) + ", baş gizli " + fpBody.HeadHidden + ", vekil " + (fpBody.ShadowProxy != null && fpBody.ShadowProxy.enabled) + ", geçişte en büyük artış " + F(increase));
        orbit.ToggleMode();
        yield return Until(() => orbit.Blend <= 0f, 2f, "TP'ye dönüş");
        yield return Seconds(0.6f);
        Check("(7b) TP'ye dönüş: saydamlık geri gelir, baş ölçeği ve gölge vekili eski hâlinde", fade.CurrentFade >= 0.99f && BodyIsFaded() && !fpBody.HeadHidden && Vector3.Distance(fpBody.Head.localScale, headScale) < 1e-4f && !fpBody.ShadowProxy.enabled, "saydamlık " + F(fade.CurrentFade) + ", baş gizli " + fpBody.HeadHidden);

        // (8) Elde eşya: saydamlaşır, shadowCastingMode'a (UpdateHandVisibility) dokunulmaz, bırakınca kaynak malzeme
        var bridge = player.GetComponent<CompanyPlayerBridge>();
        var item = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).OrderBy(i => i.HalfSize.magnitude).FirstOrDefault(i => i.HalfSize.magnitude < 0.7f);
        if (item == null) Fail("(8) taşınacak eşya yok");
        else
        {
            var itemRenderers = item.GetComponentsInChildren<Renderer>();
            var itemSource = itemRenderers.ToDictionary(r => r, r => r.sharedMaterials);
            var itemShadows = itemRenderers.ToDictionary(r => r, r => r.shadowCastingMode);
            bridge.Hold(item);
            yield return null; yield return null; yield return null;
            int faded = itemRenderers.Count(r => fade.IsFaded(r) || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly);
            bool lit = itemRenderers.All(r => !fade.IsFaded(r) || r.sharedMaterials.Any(m => m != null && m.shader.name == CameraProximityFade.ShaderName));
            yield return CaptureMain("tp_elde_esya_kose");
            string occluded = "elde gizli " + item.IsHandOccluded;
            bridge.DropHeld();
            bool dropped = bridge.HeldItem == null;
            if (dropped) yield return null;
            yield return null;
            bool restored = dropped && itemRenderers.All(r => itemSource[r].SequenceEqual(r.sharedMaterials)) && itemRenderers.All(r => r.shadowCastingMode == itemShadows[r]);
            Check("(8) elde eşya saydamlaşır, bırakınca malzeme ve gölge modu eski", faded == itemRenderers.Length && lit && restored, item.name + ": " + faded + "/" + itemRenderers.Length + " saydam, " + occluded + ", bırakıldı " + dropped + ", döndü " + restored);
        }

        // (9) Başka karakter (co-op'ta öbür oyuncu yerine): etkilenmez; kamera hedefi değişince eski hedef hemen döner
        var otherAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1SceneBuilder.CharacterPath);
        var other = UnityEngine.Object.Instantiate(otherAsset, player.transform.position + player.transform.right * 0.6f, player.transform.rotation);
        var otherRenderers = other.GetComponentsInChildren<Renderer>();
        var otherSource = otherRenderers.ToDictionary(r => r, r => r.sharedMaterials);
        yield return null; yield return null;
        bool otherClean = other.GetComponent<CameraProximityFade>() == null && otherRenderers.All(r => otherSource[r].SequenceEqual(r.sharedMaterials)) && fade.CurrentFade > 0f;
        Transform savedTarget = orbit.target;
        orbit.target = other.transform;
        yield return null; yield return null;
        bool retargetClean = fade.FadedRendererCount == 0 && BodyIsSource() && other.GetComponent<CameraProximityFade>() != null;
        orbit.target = savedTarget;
        yield return null;
        UnityEngine.Object.Destroy(other);
        yield return Seconds(0.5f);
        Check("(9) başka karakter saydamlaşmaz; kamera hedefi değişince eski hedef kaynak malzemeye döner", otherClean && retargetClean && fade.CurrentFade >= 0.99f, "öbürü temiz " + otherClean + ", hedef değişimi temiz " + retargetClean + ", geri dönünce saydamlık " + F(fade.CurrentFade));

        // (10) Sahne geçişi saydamken: kopya kalmaz, yeni sahnede bileşen kurulur, opak başlar
        Check("(10) geçiş öncesi kopyalar var", CloneCount() > 0, "kopya " + CloneCount());
        var oldPlayerMaterials = bodySource.Values.SelectMany(m => m).Where(m => m != null).Distinct().ToList();
        SceneManager.LoadScene(Bolum1SceneBuilder.ScenePath);
        yield return Until(() => SceneManager.GetActiveScene().path == Bolum1SceneBuilder.ScenePath, 120f, "Bölüm 1 yüklendi");
        yield return Bind();
        yield return Until(() => orbit.CurrentDistance > 0f, 5f, "Bölüm 1 kamera");
        Check("(10b) Ofis → Bölüm 1: kopya malzeme kalmadı, yeni oyuncuda bileşen var", CloneCount() == 0 || fade.CurrentFade > 0f, "kopya " + CloneCount() + ", saydamlık " + F(fade.CurrentFade) + ", uzaklık " + F(orbit.CurrentDistance));
        BaselineAssets();
        ReportMaterials("Bölüm 1");

        // (11) Bölüm 1 doğma yeri: arkadaki duvar kamerayı çeker, saydamlık çalışır; gölge düşer. Elde koni, kangal, valiz saydamlaşır.
        Vector3 spawnPos = player.transform.position;
        Quaternion spawnRot = player.transform.rotation;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "FadeVerify_Wall";
        wall.transform.localScale = new Vector3(8f, 6f, 0.4f);
        // Güneş yönü bilinmediği için 4 yönde dene: gölge kameranın gördüğü zemine bir yönde mutlaka düşer
        int bestShadow = 0; float arenaFade = 0f; float arenaDistance = 0f; float bestYaw = 0f;
        foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
        {
            PlaceWall(wall, spawnPos, yaw);
            yield return Seconds(0.6f);
            arenaFade = Mathf.Max(arenaFade, fade.CurrentFade);
            arenaDistance = orbit.CurrentDistance;
            var withShadow = RenderSmall();
            foreach (var r in body) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var noShadow = RenderSmall();
            foreach (var r in body) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            int d = DiffCount(withShadow, noShadow);
            if (d > bestShadow) { bestShadow = d; bestYaw = yaw; }
        }
        PlaceWall(wall, spawnPos, bestYaw);
        yield return Seconds(0.6f);
        yield return CaptureMain("tp_bolum1_duvar_dibi");
        Check("(11) Bölüm 1: duvar dibinde saydam ve gölge düşmeye devam eder", arenaFade >= 0.5f && BodyIsFaded() && bestShadow > 20, "saydamlık " + F(arenaFade) + ", uzaklık " + F(arenaDistance) + " m, gölgenin değiştirdiği piksel " + bestShadow + " (yön " + F(bestYaw) + ")");

        var coil = RopeCoil.All.Count > 0 ? RopeCoil.All[0] : null;
        var cone = TrafficCone.All.Count > 0 ? TrafficCone.All[0] : null;
        LootItem luggage = null;
        foreach (var l in LootItem.All) if (l != null && l.kind == LootKind.Luggage && l.carryable) { luggage = l; break; }
        var heldItems = new List<(string name, Func<bool> pick, Func<Renderer[]> renderers)>
        {
            ("kangal", () => { if (coil == null) return false; player.HeldCoil = coil; coil.PickUp(player); return true; }, () => coil.GetComponentsInChildren<Renderer>()),
            ("koni", () => { if (cone == null) return false; player.HeldCone = cone; cone.PickUp(player); return true; }, () => cone.target.GetComponentsInChildren<Renderer>()),
            ("valiz", () => { if (luggage == null) return false; player.HeldValuable = luggage; luggage.PickUp(player); return true; }, () => luggage.GetComponentsInChildren<Renderer>()),
        };
        foreach (var it in heldItems)
        {
            player.RespawnAt(spawnPos, spawnRot);
            PlaceWall(wall, spawnPos, bestYaw);
            yield return Seconds(0.4f);
            var held = it.renderers().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            var source = held.ToDictionary(r => r, r => r.sharedMaterials);
            if (!it.pick()) { Fail("(11c) " + it.name + ": sahnede yok"); continue; }
            yield return Seconds(0.3f);
            orbit.Yaw = bestYaw; orbit.Pitch = 20f;
            yield return Seconds(0.3f);
            int fadedCount = held.Count(r => fade.IsFaded(r));
            float itemFade = fade.CurrentFade;
            yield return CaptureMain("tp_bolum1_elde_" + it.name);
            player.RespawnAt(spawnPos, spawnRot);
            yield return null; yield return null;
            bool back = held.All(r => r == null || source[r].SequenceEqual(r.sharedMaterials));
            Check("(11c) elde " + it.name + " saydamlaşır, elden çıkınca kaynak malzeme", held.Length > 0 && fadedCount == held.Length && back, fadedCount + "/" + held.Length + " saydam, karakter saydamlığı " + F(itemFade) + ", döndü " + back);
        }

        PlaceWall(wall, spawnPos, bestYaw);
        wall.SetActive(false);
        yield return Seconds(1.6f);
        Check("(11b) Bölüm 1: duvar kalkınca opak ve kaynak malzeme", fade.CurrentFade == 0f && BodyIsSource(), "uzaklık " + F(orbit.CurrentDistance) + " m");
        yield return CaptureMain("tp_bolum1_acik");
        wall.SetActive(true);
        yield return Seconds(0.6f);

        // (12) Bölüm 1 → Ofis, saydamken: yine kopya kalmaz
        int cloneBefore = CloneCount();
        SceneManager.LoadScene(OfficeGaragePlayerSetup.ScenePath);
        yield return Until(() => SceneManager.GetActiveScene().path == OfficeGaragePlayerSetup.ScenePath, 120f, "Ofis yüklendi");
        yield return Bind();
        yield return Until(() => orbit.CurrentDistance > 0f, 5f, "ofis kamera");
        int cloneAfter = CloneCount();
        Check("(12) Bölüm 1 → Ofis: saydamken sahne değişti, kopya kalmadı", cloneBefore > 0 && (cloneAfter == 0 || fade.CurrentFade > 0f) && oldPlayerMaterials.All(m => m != null), "önce " + cloneBefore + ", sonra " + cloneAfter + ", saydamlık " + F(fade.CurrentFade));

        // (13) Asset'ler aynı: dosya özeti, shader, anahtarlar; hiçbir malzeme asset'i dither shader'ına geçmedi
        var changed = new List<string>();
        foreach (var pair in assetBaseline)
        {
            if (pair.Key == null) continue;
            string now = AssetFingerprint(pair.Key);
            if (now != pair.Value)
            {
                changed.Add(pair.Key.name);
                if (changed.Count == 1) Line("  ilk fark: " + pair.Value + " → " + now);
            }
        }
        int assetUsers = AssetDatabase.FindAssets("t:Material").Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat")).Select(AssetDatabase.LoadAssetAtPath<Material>).Count(m => m != null && m.shader != null && m.shader.name == CameraProximityFade.ShaderName);
        Check("(13) malzeme asset'leri değişmedi (" + assetBaseline.Count + "), diskte dither'lı malzeme yok", changed.Count == 0 && assetUsers == 0 && assetBaseline.Count > 0, "değişen " + string.Join(",", changed) + ", dither kullanan asset " + assetUsers);
    }

    private static void PlaceWall(GameObject wall, Vector3 at, float yaw)
    {
        orbit.Yaw = yaw; orbit.Pitch = 20f;
        wall.transform.SetPositionAndRotation(at + Vector3.up * 3f + Quaternion.Euler(0f, yaw, 0f) * Vector3.back * 0.9f, Quaternion.Euler(0f, yaw, 0f));
    }

    private struct Sample
    {
        public string name; public Vector3 position, lookAt;
        public Sample(string n, Vector3 p, Vector3 l) { name = n; position = p; lookAt = l; }
    }

    private static bool BodyIsSource()
    {
        foreach (var pair in bodySource)
            if (pair.Key != null && !pair.Value.SequenceEqual(pair.Key.sharedMaterials)) return false;
        return bodySource.Count > 0 && fade.FadedRendererCount == 0;
    }

    // Vücut renderer'larının en az bir malzemesi dither'lı (FP gölge vekili hariç)
    private static bool BodyIsFaded()
    {
        return body.Any(r => r != null && r.sharedMaterials.Any(m => m != null && m.shader.name == CameraProximityFade.ShaderName));
    }

    private static int CloneCount()
    {
        return Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.EndsWith(CameraProximityFade.CloneSuffix));
    }

    private static void BaselineAssets()
    {
        foreach (var r in body)
            foreach (var m in r.sharedMaterials)
                if (m != null && !assetBaseline.ContainsKey(m) && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))) assetBaseline[m] = AssetFingerprint(m);
    }

    private static string AssetFingerprint(Material m)
    {
        string path = AssetDatabase.GetAssetPath(m);
        string hash = "";
        if (File.Exists(path))
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
                hash = Convert.ToBase64String(md5.ComputeHash(stream));
        }
        // EditorUtility.IsDirty kullanılmaz: FBX malzemesi ilk çizimde (kopya yokken de) kirli görünüyor, ölçüldü. Asıl ölçüt dosya özeti ve değerler.
        return path + "|" + m.shader.name + "|" + string.Join(",", m.shaderKeywords.OrderBy(k => k)) + "|" + m.renderQueue + "|" + m.GetColor("_BaseColor") + "|" + hash;
    }

    // Saydamlaşabilecek malzemelerin shader ve anahtar kümeleri (shader'daki multi_compile_local listesinin dayanağı)
    private static void ReportMaterials(string scene)
    {
        var mats = new HashSet<Material>();
        void Add(GameObject go) { if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m); }
        Add(player.gameObject);
        foreach (var c in UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c.gameObject);
        foreach (var c in UnityEngine.Object.FindObjectsByType<LootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c.gameObject);
        foreach (var c in UnityEngine.Object.FindObjectsByType<TrafficCone>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c.gameObject);
        foreach (var c in UnityEngine.Object.FindObjectsByType<RopeCoil>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c.gameObject);
        foreach (var c in UnityEngine.Object.FindObjectsByType<Patient>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c.gameObject);
        // Saydamlık kuyruktan okunur (bileşenle aynı)
        foreach (var g in mats.GroupBy(m => m.shader.name + " kuyruk=" + m.renderQueue + " [" + string.Join(" ", m.shaderKeywords.OrderBy(k => k)) + "]").OrderBy(g => g.Key))
            Line("  malzeme (" + scene + "): " + g.Key + " ×" + g.Count() + " ör. " + g.First().name);
    }

    private static IEnumerator Teleport(Vector3 position, Vector3 lookAt)
    {
        Vector3 at = position;
        foreach (var h in Physics.RaycastAll(position + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (!h.collider.transform.IsChildOf(player.transform) && Mathf.Abs(h.point.y - position.y) < 0.6f) { at.y = h.point.y + 0.02f; break; }
        Vector3 flat = lookAt - at; flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f) flat = player.transform.forward;
        player.Teleport(at, Quaternion.LookRotation(flat.normalized, Vector3.up));
        Vector3 pivot = at + Vector3.up * orbit.pivotHeight;
        Vector3 dir = (lookAt - pivot).normalized;
        orbit.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        orbit.Pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        yield return null; yield return null; yield return null;
    }

    // Karakterin ekranda kapladığı pay (0..1): karakterli ve karaktersiz çizimin farkı (aynı kare, poz donuk)
    private static float Coverage()
    {
        var with = RenderSmall();
        foreach (var r in body) if (r != null) r.forceRenderingOff = true;
        var without = RenderSmall();
        foreach (var r in body) if (r != null) r.forceRenderingOff = false;
        return DiffCount(with, without) / (float)(SmallW * SmallH);
    }

    private static Color32[] RenderSmall()
    {
        var cam = Camera.main;
        var rt = new RenderTexture(SmallW, SmallH, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(SmallW, SmallH, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, SmallW, SmallH), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        var px = tex.GetPixels32();
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        return px;
    }

    private static int DiffCount(Color32[] a, Color32[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
            if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 12) n++;
        return n;
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
        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
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
    private static string F(float v) { return v.ToString("F2", Inv); }
}
