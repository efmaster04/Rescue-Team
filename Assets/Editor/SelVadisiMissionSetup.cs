using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CompanySim;
using SelVadisi;
using SelVadisi.EditorBuild;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Sel Vadisi büyük haritasını oyunun saha görevine çevirir (Bölüm 2):
//  1. Harita yoksa SVBuild ile kodla üretilir (Assets/SelVadisi/SelVadisi_BuyukHarita.unity).
//  2. Haritanın serbest uçuş kamerası kaldırılır; Bölüm 1'deki oyuncu (TestCharacter_RescueCrew + OrbitCamera) olduğu gibi
//     kopyalanır (OfficeGaragePlayerSetup ile aynı yöntem), kurtarma alanındaki çadırın yanında doğar.
//  3. Yürünen yerlere (arazi, köprü, yapılar) MeshCollider eklenir; derin su SelVadisiMission'da yeniden doğmaya bağlanır.
//  4. Görev nesnesi (SelVadisiMission), koordinasyon masası işareti, işaret malzemesi ve gölge mesafesi ayarı kurulur.
//  5. Sahne Build Settings'e eklenir (OfficeGarage ilk sırada kalır); "selvadisi" işi oluşturulur ve kataloğa eklenir.
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod SelVadisiMissionSetup.ApplyBatch -logFile <dosya>
public static class SelVadisiMissionSetup
{
    public const string SceneName = "SelVadisi_BuyukHarita";
    public const string OfficeScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
    public const string JobPath = "Assets/_Project/Data/Jobs/selvadisi.asset";
    public const string AddonPath = "Assets/_Project/Resources/CompanyCatalogAddon.asset";
    public const string MissionDir = "Assets/SelVadisi/Gorev";
    public const string BeaconMaterialPath = MissionDir + "/SV_BildirimIsareti.mat";
    public const string PlayerName = "08_Player_RescueCrew";
    public const string CameraName = "Main Camera";
    public const string SpawnName = "PlayerSpawn";
    public const float FarClip = 1400f;
    private const string OutDir = "Captures/SelVadisi";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;

    [MenuItem("Rescue Team/Sel Vadisi/Saha Görevini Kur")]
    public static void ApplyMenu()
    {
        if (!MenuGuard()) return;
        if (!EditorUtility.DisplayDialog("Sel Vadisi", SVBuild.ScenePath + " saha görevine çevrilecek (oyuncu, çarpıştırıcılar, görev) ve kaydedilecek. Devam?", "Kur", "Vazgeç")) return;
        KeepingOpenScenes(() => Apply(false));
    }

    [MenuItem("Rescue Team/Sel Vadisi/Haritayı Yeniden Üret ve Görevi Kur")]
    public static void RebuildMenu()
    {
        if (!MenuGuard()) return;
        if (!EditorUtility.DisplayDialog("Sel Vadisi", "Harita kodla baştan üretilecek (yaklaşık 1 dakika) ve saha görevi kurulacak. Devam?", "Üret", "Vazgeç")) return;
        KeepingOpenScenes(() => Apply(true));
    }

    // Menüden çalıştırma: Play modunda reddedilir (üretim önce Generated klasörünü siler), kaydedilmemiş sahne değişiklikleri için önce sorulur.
    public static bool MenuGuard()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Sel Vadisi", "Önce Play modundan çıkın.", "Tamam");
            return false;
        }
        return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
    }

    // Kurulum harita sahnesini tek başına açar; bitince kullanıcının açık sahneleri geri yüklenir (adsız sahne yoksa).
    public static void KeepingOpenScenes(Action work)
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool restorable = setup != null && setup.Length > 0;
        if (restorable) foreach (var s in setup) if (string.IsNullOrEmpty(s.path)) restorable = false;
        try { work(); }
        finally { if (restorable) EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    public static void ApplyBatch()
    {
        try { Apply(Environment.GetEnvironmentVariable("SV_REBUILD") == "1"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("SelVadisiMissionSetup: " + e); EditorApplication.Exit(1); }
    }

    public static void Apply(bool rebuild)
    {
        report = new StringBuilder();
        Line("SEL VADISI MISSION SETUP " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        if (rebuild || !File.Exists(SVBuild.ScenePath))
        {
            Line("Harita üretiliyor (SVBuild.Build)");
            SVBuild.Build();
        }
        var scene = EditorSceneManager.OpenScene(SVBuild.ScenePath, OpenSceneMode.Single);

        // 1) Haritanın kendi kamerası (FlyCamera) ve önceki kurulum kalıntıları
        var oldRoots = new List<GameObject>();
        Camera oldCam = null;
        foreach (var fc in UnityEngine.Object.FindObjectsByType<FlyCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            oldCam = fc.GetComponent<Camera>();
            oldRoots.Add(fc.gameObject);
        }
        foreach (var tp in UnityEngine.Object.FindObjectsByType<ThirdPersonPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) oldRoots.Add(tp.gameObject);
        foreach (var oc in UnityEngine.Object.FindObjectsByType<OrbitCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (oldCam == null) oldCam = oc.GetComponent<Camera>();
            oldRoots.Add(oc.gameObject);
        }
        foreach (var n in new[] { SpawnName, "SelVadisi_Gorev", "KoordinasyonMasasi" })
        {
            var go = GameObject.Find(n);
            if (go != null) oldRoots.Add(go);
        }

        // 2) Doğma yeri: kurtarma alanındaki çadırın (masanın) güneybatısı, yüzü suya (+x)
        float ex = Valley.PadEdgeX, zc = (Valley.PadZ0 + Valley.PadZ1) * 0.5f;
        Vector3 desk = new Vector3(ex - 19f, Valley.TerrainHeight(ex - 19f, zc - 15f), zc - 15f);
        Vector3 spawn = new Vector3(ex - 22.5f, 0f, zc - 18.5f);
        spawn.y = Valley.TerrainHeight(spawn.x, spawn.z) + 0.05f;
        Quaternion spawnRot = Quaternion.Euler(0f, 70f, 0f);
        Line("Kurtarma alanı: masa " + V(desk) + ", doğma " + V(spawn));

        // 3) Yürünen yüzeyler: arazi ve yapılar MeshCollider alır (detaylar, doğa, su ve insanlar almaz)
        int colliders = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (Transform group in root.transform)
            {
                bool walk = group.name.StartsWith("Arazi") || group.name.StartsWith("Yapilar");
                if (!walk) continue;
                foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    colliders++;
                }
            }
        Line("MeshCollider eklendi: " + colliders);

        // 4) Bölüm 1'den oyuncu ve kamera kopyası (OfficeGaragePlayerSetup ile aynı)
        var b1 = EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Additive);
        GameObject srcCharacter = null; OrbitCamera srcOrbit = null;
        foreach (var root in b1.GetRootGameObjects())
        {
            if (root.name == Bolum1SceneBuilder.CharacterName) srcCharacter = root;
            var o = root.GetComponentInChildren<OrbitCamera>(true);
            if (o != null && srcOrbit == null) srcOrbit = o;
        }
        if (srcCharacter == null || srcOrbit == null) throw new Exception("Bölüm 1 sahnesinde karakter / OrbitCamera bulunamadı");
        SceneManager.SetActiveScene(scene);
        var character = UnityEngine.Object.Instantiate(srcCharacter);
        character.name = PlayerName;
        SceneManager.MoveGameObjectToScene(character, scene);
        var camGo = UnityEngine.Object.Instantiate(srcOrbit.gameObject);
        camGo.name = CameraName;
        camGo.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var copied = new List<Component>();
        copied.AddRange(character.GetComponentsInChildren<Component>(true));
        copied.AddRange(camGo.GetComponentsInChildren<Component>(true));
        int cleared = 0;
        foreach (var c in copied)
        {
            if (c == null) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool changed = false;
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var v = it.objectReferenceValue;
                var sceneOf = v is GameObject g ? g.scene : v is Component comp ? comp.gameObject.scene : default;
                if (sceneOf.IsValid() && sceneOf == b1) { it.objectReferenceValue = null; cleared++; changed = true; }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.CloseScene(b1, true);
        Line("Bölüm 1 kopyası: " + Names(character) + " | kamera: " + Names(camGo) + " | temizlenen Bölüm 1 referansı " + cleared);

        // 5) Eski kameraya işaret eden harita referansları (yağmur, şimşek…) yeni kameraya çevrilir, eskiler silinir
        var cam = camGo.GetComponent<Camera>();
        if (oldCam != null)
        {
            int repointed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (oldRoots.Contains(root) || root == character || root == camGo) continue;
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                        var v = it.objectReferenceValue;
                        if (v == oldCam) it.objectReferenceValue = cam;
                        else if (v == oldCam.gameObject) it.objectReferenceValue = camGo;
                        else if (v == oldCam.transform) it.objectReferenceValue = camGo.transform;
                        else continue;
                        changed = true; repointed++;
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            Line("Eski kamera referansı çevrildi: " + repointed);
        }
        foreach (var go in oldRoots) if (go != null) UnityEngine.Object.DestroyImmediate(go);

        // 6) Doğma noktası ve oyuncu bağları
        var marker = new GameObject(SpawnName);
        marker.transform.SetPositionAndRotation(spawn, spawnRot);
        SceneManager.MoveGameObjectToScene(marker, scene);
        character.transform.SetPositionAndRotation(spawn, spawnRot);
        var player = character.GetComponent<ThirdPersonPlayer>();
        var input = character.GetComponent<PlayerInputReader>();
        var interaction = character.GetComponent<PlayerInteraction>();
        var combat = character.GetComponent<PlayerCombat>();
        var orbit = camGo.GetComponent<OrbitCamera>();
        if (player == null || input == null || interaction == null || orbit == null) throw new Exception("Kopyada ThirdPersonPlayer / PlayerInputReader / PlayerInteraction / OrbitCamera eksik");
        player.cameraTransform = cam.transform;
        orbit.target = character.transform;
        input.player = player; input.interaction = interaction; input.orbitCamera = orbit; input.combat = combat;
        interaction.helper = null;
        float killY = Mathf.Min(Valley.WaterLevel(-760f), Valley.WaterLevel(760f)) - 30f;
        var fall = character.GetComponent<PlayerFallRespawn>();
        if (fall != null) { fall.player = player; fall.spawnPoint = marker.transform; fall.killBelowY = killY; }
        var health = character.GetComponent<PlayerHealth>();
        if (health != null) { health.player = player; health.respawnPoint = marker.transform; }
        if (combat != null) { combat.player = player; combat.killBelowY = killY; }
        if (character.GetComponent<FirstPersonBody>() == null) character.AddComponent<FirstPersonBody>();

        // 7) Kamera: ana kamera, tek dinleyici, büyük harita için uzak kırpma, haritanın kendi atmosfer hacmi kullanılır
        cam.tag = "MainCamera";
        cam.farClipPlane = FarClip;
        if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
        foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.gameObject != camGo) UnityEngine.Object.DestroyImmediate(l);
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        Line("Kamera: uzak " + cam.farClipPlane.ToString("F0", Inv) + " m, OrbitCamera uzaklık " + orbit.distance.ToString("F2", Inv));

        // 8) Görev nesnesi: koordinasyon masası, işaret malzemesi, gölge mesafesi (yalnız bu sahnede)
        Directory.CreateDirectory(MissionDir);
        var deskGo = new GameObject("KoordinasyonMasasi");
        deskGo.transform.position = desk;
        SceneManager.MoveGameObjectToScene(deskGo, scene);
        var missionGo = new GameObject("SelVadisi_Gorev");
        SceneManager.MoveGameObjectToScene(missionGo, scene);
        var mission = missionGo.AddComponent<SelVadisiMission>();
        mission.reportDesk = deskGo.transform;
        mission.player = player;
        mission.fallRespawn = fall;
        mission.beaconMaterial = BeaconMaterial();
        missionGo.AddComponent<SelVadisiShadowRange>();
        int roofGroups = CountGroups();
        mission.requiredReports = Mathf.Clamp(Mathf.RoundToInt(roofGroups * 0.25f), 4, 8);
        Line("Görev: çatı/balkon ailesi " + roofGroups + ", gereken bildirim " + mission.requiredReports);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Sahne kaydedilemedi: " + SVBuild.ScenePath);

        // 9) Build Settings: ofis ilk sahne kalır, sel sahnesi eklenir
        var list = new List<EditorBuildSettingsScene>();
        foreach (var s in EditorBuildSettings.scenes) if (s.path == OfficeScenePath) list.Add(s);
        if (list.Count == 0 && File.Exists(OfficeScenePath)) list.Add(new EditorBuildSettingsScene(OfficeScenePath, true));
        foreach (var s in EditorBuildSettings.scenes) if (s.path != OfficeScenePath && s.path != SVBuild.ScenePath) list.Add(s);
        list.Add(new EditorBuildSettingsScene(SVBuild.ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Line("Build Settings: " + string.Join(", ", list.ConvertAll(s => Path.GetFileNameWithoutExtension(s.path))));

        // 10) İş: "selvadisi" (saha sahnesi bu harita) ve katalog kaydı
        BuildJob();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "mission_setup_report.txt"), report.ToString());
        Debug.Log(report.ToString());
    }

    private static int CountGroups()
    {
        var anim = UnityEngine.Object.FindAnyObjectByType<PeopleAnimator>();
        if (anim == null) return 0;
        var keys = new HashSet<int>();
        for (int i = 0; i < anim.people.Count; i++)
        {
            var p = anim.people[i];
            if (p == null || p.root == null) continue;
            string[] parts = p.root.name.Split('_');
            if (parts.Length < 2 || (parts[1] != "Roof" && parts[1] != "Balcony")) continue;
            keys.Add(p.group >= 0 ? p.group : -1 - i);
        }
        return keys.Count;
    }

    private static Material BeaconMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BeaconMaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, BeaconMaterialPath);
        }
        var orange = new Color(1f, 0.42f, 0.08f);
        mat.SetColor("_BaseColor", orange);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", orange * 3.2f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void BuildJob()
    {
        var job = AssetDatabase.LoadAssetAtPath<JobDefinition>(JobPath);
        if (job == null) { job = ScriptableObject.CreateInstance<JobDefinition>(); AssetDatabase.CreateAsset(job, JobPath); }
        job.id = "selvadisi";
        job.title = "Sel Vadisi: Mahsur Kalanlar";
        job.client = "İl Afet Koordinasyon Merkezi";
        job.description = "Karadeniz'deki köyü sel bastı. Kurtarma alanına geçin, çatılarda ve balkonlarda mahsur kalan aileleri bulup telsizle bildirin, sonra koordinasyon masasına rapor verin.";
        job.category = "Kurtarma";
        job.location = "Sel Vadisi";
        job.specialTerms = "Akıntı çok güçlü: derin suya girmek yasak. Hızlı müdahale: raporu sahada " +
            SelVadisiMission.QuickResponseMinutes.ToString("0") + " dakika içinde verirseniz ek ödeme alırsınız.";
        job.distanceKm = 64f;
        job.payment = 1900;
        job.difficulty = 4;
        job.risk = 4;
        job.durationMinutes = 150;
        job.minimumReputation = 0;
        job.contractTitle = "Hızlı müdahale";
        job.contractBonus = 260;
        // Sözleşme koşulu aşınmayla değerlendirilir (CompanyRuntime). 64 km'lik yol tek başına %19,2 araç aşınması getirir;
        // varsayılan %10 sınırı hiç sağlanamazdı. Sınır: yol + QuickResponseMinutes dakikalık saha süresi.
        job.contractMaxEquipmentWear = 10f;
        job.contractMaxVehicleWear = QuickResponseVehicleWearLimit(job.distanceKm);
        // Süre koşulu ayrıca gerçek saha süresiyle ölçülür (aşınma yuvarlaması 15:20 gibi geç raporu kaçırıyordu).
        job.contractMaxFieldMinutes = SelVadisiMission.QuickResponseMinutes;
        // her iş takılı ve sabit bir modül ister (PreparationRules); boş kimlik hiçbir modülle eşleşmez ve çıkışı kilitler.
        // Kasa modülü yeni kayıtta zaten takılıdır; ip, sedye ve çanta (32 kg) 420 kg / 8 yuvaya sığar.
        job.requiredModule = "cargo";
        job.requiredEquipment = new[] { "rope" };
        job.recommendedEquipment = new[] { "stretcher", "case" };
        job.fieldScene = SceneName;
        EditorUtility.SetDirty(job);
        // the office catalog lives in OfficeGarage.unity: the job joins through the add-on asset instead (no scene edit)
        var addon = AssetDatabase.LoadAssetAtPath<CatalogAddon>(AddonPath);
        if (addon == null) throw new Exception("Ek katalog yok: " + AddonPath);
        var jobs = new List<JobDefinition>(addon.jobs ?? new JobDefinition[0]);
        jobs.RemoveAll(j => j == null || j.id == job.id);
        jobs.Add(job);
        addon.jobs = jobs.ToArray();
        EditorUtility.SetDirty(addon);
        Line("İş: " + job.id + " → " + job.fieldScene + ", ek katalogdaki işler: " + jobs.Count);
    }

    /// "Hızlı müdahale" sözleşmesinin araç aşınması sınırı (%): gidiş-dönüş yol + hedef saha süresi, yukarı yuvarlanır.
    public static float QuickResponseVehicleWearLimit(float distanceKm)
    {
        return Mathf.Ceil(distanceKm * 2f * FieldPayout.VehicleWearPerKm + SelVadisiMission.QuickResponseMinutes * FieldPayout.VehicleWearPerFieldMinute);
    }

    private static string Names(GameObject go)
    {
        var names = new List<string>();
        foreach (var c in go.GetComponents<Component>()) if (c != null) names.Add(c.GetType().Name);
        return string.Join(", ", names);
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
