using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Bölüm 1 ambulans döngüsü ve hasta taşıma: asset'leri, prefab'ları ve sahne kurulumunu yapar.
//
// Yer tutucu kuralı: Ambulans, sedye ve hasta modelleri başka işlerde üretiliyor. Model dosyaları (SÖZLEŞME yolları)
// VARSA onlar, YOKSA aynı ad ve ölçülerde kutu yer tutucular kullanılır. Hasta yer tutucusu: RescueCrew FBX'i
// gri materyalle (aynı mixamorig iskeleti, aynı Avatar akışı). Model dosyaları gelince builder'ı yeniden çalıştırmak yeterli.
//
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod Bolum1AmbulanceBuilder.BuildBatch -logFile <dosya>
// (Bolum1SceneBuilder.Build önce BuildAssets'i çağırır, sahneyi baştan kurar, sonunda SetupScene'i çağırır.)
public static class Bolum1AmbulanceBuilder
{
    // ---- SÖZLEŞME yolları ----
    public const string AmbulanceModelPath = "Assets/Models/Vehicles/Ambulance/SM_Ambulance.fbx";
    public const string AmbulanceMetaPath = "Assets/Models/Vehicles/Ambulance/ambulance_meta.json";
    public const string StretcherModelPath = "Assets/Models/Props/Stretcher/SM_Stretcher.fbx";
    public const string PatientFolder = "Assets/Models/Characters/Patient/";
    public const string PatientMetaPath = PatientFolder + "patient_meta.json";
    public const string LiePosePath = PatientFolder + "Patient_LiePose.fbx";
    public const string LiePoseArmSlingPath = PatientFolder + "Patient_LiePose_ArmSling.fbx";
    public const string PlaceholderPatientModel = "Assets/Models/Characters/RescueCrew/SK_RescueCrew_Blue.fbx";
    public const string BotModelPath = "Assets/Models/Characters/RescueCrew/SK_RescueCrew_Green.fbx";

    // ---- Çıktılar ----
    public const string PrefabFolder = "Assets/Prefabs/Bolum1";
    public const string MaterialFolder = PrefabFolder + "/Materials";
    public const string AnimationFolder = PrefabFolder + "/Animation";
    public const string SettingsFolder = "Assets/Settings/Bolum1";
    public const string AmbulancePrefabPath = PrefabFolder + "/Ambulance.prefab";
    public const string StretcherPrefabPath = PrefabFolder + "/Stretcher.prefab";
    public const string SchedulePath = SettingsFolder + "/AmbulanceSchedule.asset";
    public const string CarryRulesPath = SettingsFolder + "/CarryRules.asset";
    public const string LocomotionControllerPath = AnimationFolder + "/AC_Crew_Locomotion.controller";
    public const string ReportPath = "Captures/Bolum1/ambulance_build_report.txt";

    public static readonly string[] PatientTypes = { "Tourist", "Civilian", "Elder" };
    public static readonly string[] PatientNames = { "Turist", "Sivil", "Yaşlı" };
    public static string PatientPrefabPath(string type) { return PrefabFolder + "/Patient_" + type + ".prefab"; }
    public static string PatientModelPath(string type) { return PatientFolder + "SK_Patient_" + type + ".fbx"; }

    // ---- Sahne obje adları ----
    public const string GameplayRootName = "Bolum1_Gameplay";
    public const string LaneName = "TrafficLane_EastBound";
    public const string StopName = "AmbulanceStop";
    public const string MissionName = "Bolum1_Mission";
    public const string HudName = "Bolum1_HUD";
    public const string BotName = "StretcherHelperBot";
    public static readonly string[] StretcherNames = { "Stretcher_01", "Stretcher_02" };

    public const float StopRearOffset = 4f;     // arka tampon, rampa ağzının bu kadar doğusunda (m)
    public const float PlayerMoveSpeed = 2.4f;
    public const float WalkThreshold = 1.2f, RunThreshold = 3.0f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report = new StringBuilder();

    // =====================================================================================
    // Giriş noktaları
    // =====================================================================================

    [MenuItem("Rescue Team/Bölüm 1/Ambulans: Asset ve Sahneyi Kur")]
    public static void BuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Bölüm 1", Bolum1SceneBuilder.ScenePath + " yeniden kurulacak. Devam?", "Kur", "Vazgeç"))
            return;
        Bolum1SceneBuilder.Build();
    }

    public static void BuildBatch()
    {
        try
        {
            Bolum1SceneBuilder.Build();
            bool assetsOk = true;
            Bolum1AmbulanceModelChecks.CheckAssets((label, ok, detail) =>
            {
                assetsOk &= ok;
                Debug.Log((ok ? "CHECK OK " : "CHECK FAIL ") + label + " — " + detail);
            });
            if (!assetsOk) throw new Exception("Gerçek model asset kabulü başarısız; CHECK FAIL satırlarına bakın.");
            Debug.Log("AMBULANCE BUILD OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("AMBULANCE BUILD FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // =====================================================================================
    // Asset'ler ve prefab'lar
    // =====================================================================================

    public static void BuildAssets()
    {
        // İlk import sırasından bağımsız olarak T-poz Avatar'ı hazırken klipleri tekrar bağla.
        foreach (string path in PatientTypes.Select(PatientModelPath).Concat(new[] { LiePosePath, LiePoseArmSlingPath }))
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new Exception("Hasta FBX bulunamadı: " + path);
            Bolum1ImportSettings.Apply(importer);
            importer.SaveAndReimport();
        }
        report = new StringBuilder();
        Line("AMBULANCE BUILD " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(AnimationFolder);
        EnsureFolder(SettingsFolder);

        // ScriptableObject'ler: yoksa varsayılanla oluştur, varsa (kullanıcı ayarı olabilir) dokunma.
        if (AssetDatabase.LoadAssetAtPath<AmbulanceSchedule>(SchedulePath) == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AmbulanceSchedule>(), SchedulePath);
        if (AssetDatabase.LoadAssetAtPath<CarryRules>(CarryRulesPath) == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<CarryRules>(), CarryRulesPath);
        AssetDatabase.SaveAssets();

        BuildLocomotionController();
        BuildAmbulancePrefab();
        BuildStretcherPrefab();
        for (int i = 0; i < PatientTypes.Length; i++)
            BuildPatientPrefab(PatientTypes[i], PatientNames[i]);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static AmbulanceSchedule Schedule { get { return AssetDatabase.LoadAssetAtPath<AmbulanceSchedule>(SchedulePath); } }
    public static CarryRules Rules { get { return AssetDatabase.LoadAssetAtPath<CarryRules>(CarryRulesPath); } }

    // ---- Karakter hareket Animator'ı: Speed ile bekleme / yürüme / koşma blend'i ----
    private static void BuildLocomotionController()
    {
        var idle = FirstClip("Assets/Animations/RescueCrew/Controllers/AC_RescueCrew_Idle.controller");
        var walk = FirstClip("Assets/Animations/RescueCrew/Controllers/AC_RescueCrew_Walking.controller");
        var run = FirstClip("Assets/Animations/RescueCrew/Controllers/AC_RescueCrew_Running.controller");
        if (idle == null || walk == null || run == null)
            throw new Exception("RescueCrew Idle/Walking/Running klipleri bulunamadı");

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(LocomotionControllerPath) != null)
            AssetDatabase.DeleteAsset(LocomotionControllerPath);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(LocomotionControllerPath);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var state = ac.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, WalkThreshold);
        tree.AddChild(run, RunThreshold);
        ac.layers[0].stateMachine.defaultState = state;
        // Prosedürel animasyonlar: taşıma, alma/bırakma, zıplama, irkilme, kalkış, kaldırma, kutlama
        Bolum1AnimBuilder.AddCrewStates(ac, state, PlayerMoveSpeed);
        EditorUtility.SetDirty(ac);
        Line("Animator: " + LocomotionControllerPath + " (Speed: " + idle.name + " 0, " + walk.name + " " + WalkThreshold.ToString(Inv) + ", " + run.name + " " + RunThreshold.ToString(Inv) + " m/sn)");
    }

    private static AnimationClip FirstClip(string controllerPath)
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        return ac != null ? ac.animationClips.FirstOrDefault() : null;
    }

    // ---- Ambulans ----
    private static void BuildAmbulancePrefab()
    {
        var root = new GameObject("Ambulance");
        try
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(AmbulanceModelPath);
            GameObject model;
            if (fbx != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = "SM_Ambulance";
                model.transform.SetParent(root.transform, false); // FBX kök dönüşü (eksen dönüşümü) korunur; ön = +Z
                Line("Ambulans modeli: " + AmbulanceModelPath);
            }
            else
            {
                model = BuildAmbulancePlaceholder();
                model.transform.SetParent(root.transform, false);
                Line("Ambulans modeli YOK → yer tutucu (6.0 × 2.0 × 2.65 m kutular)");
            }

            Transform Find(string n)
            {
                var t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                if (t == null) throw new Exception("Ambulans parçası eksik: " + n);
                return t;
            }

            // Gövde kutusu (kök uzayında); alt kenar alçak konileri de itsin diye 0.15 m.
            Bounds body = LocalBounds(root.transform, Find("SM_Ambulance_Body"));
            float bottom = 0.15f;
            var center = new Vector3(body.center.x, (bottom + body.max.y) * 0.5f, body.center.z);
            var size = new Vector3(body.size.x, body.max.y - bottom, body.size.z);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var box = root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            if (fbx != null)
            {
                // Aynalar/tampon tüm mesh bounds'unu büyütür; ana gövde collider'ı
                // gerçek kabin genişliği ve arka açıklık düzleminde biter.
                var data = Bolum1Coordinates.LoadJson(AmbulanceMetaPath);
                float rear = -Convert.ToSingle(Bolum1Coordinates.Obj(data, "rear_opening")["rear_plane_y_m"]);
                float width = Convert.ToSingle(Bolum1Coordinates.Obj(data, "measurement")["width_m"]);
                box.center = new Vector3(center.x, center.y, (body.max.z + rear) * 0.5f);
                box.size = new Vector3(width, size.y, body.max.z - rear);
            }

            var follower = root.AddComponent<LaneFollower>();
            // Şerit doluluk servisi (trafik takibi) için tampon uzaklıkları: gövde collider'ından.
            follower.cruiseSpeed = 12f;
            follower.acceleration = 4f;
            follower.braking = 6f;
            follower.wheels = new[] { "FL", "FR", "RL", "RR" }.Select(w => Find("SM_Ambulance_Wheel_" + w)).ToArray();
            follower.wheelRadius = WheelRadius(follower.wheels[0]);

            follower.frontOverhang = box.center.z + box.size.z * 0.5f;
            follower.rearOverhang = -(box.center.z - box.size.z * 0.5f);

            var audio = root.AddComponent<AmbulanceAudio>();
            audio.siren = Find("SirenPoint").gameObject.AddComponent<AudioSource>();
            audio.engine = root.AddComponent<AudioSource>();
            audio.door = root.AddComponent<AudioSource>();
            audio.horn = root.AddComponent<AudioSource>();
            foreach (var s in new[] { audio.siren, audio.engine, audio.door, audio.horn })
            {
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.dopplerLevel = 1f;
            }

            var amb = root.AddComponent<AmbulanceController>();
            amb.sound = audio;
            amb.bodyCenter = center;
            amb.bodySize = size;
            amb.doorLeft = Find("SM_Ambulance_Door_RL");
            amb.doorRight = Find("SM_Ambulance_Door_RR");
            amb.rearStep = Find("SM_Ambulance_Step_Rear");
            amb.loadPoint = Find("LoadPoint");
            amb.patientSlots = new[] { LieFrame(Find("PatientSlot_01"), root.transform), LieFrame(Find("PatientSlot_02"), root.transform) };
            if (fbx != null)
            {
                float surface = Convert.ToSingle(Bolum1Coordinates.Obj(Bolum1Coordinates.LoadJson(AmbulanceMetaPath), "beds")["surface_z_m"]);
                foreach (var frame in amb.patientSlots)
                {
                    Vector3 p = root.transform.InverseTransformPoint(frame.position);
                    p.y = surface;
                    frame.position = root.transform.TransformPoint(p);
                }
            }
            amb.flashLeft = Renderers(Find("SM_Ambulance_Lightbar")).Concat(Renderers(Find("SM_Ambulance_Light_RearL"))).ToArray();
            amb.flashRight = Renderers(Find("SM_Ambulance_Light_RearR")).ToArray();

            // Kapı / basamak eksenleri: meta varsa oradan (Blender → Unity), yoksa varsayılan (dikey menteşe, yatay basamak).
            // İşaret, parçanın dışa (araç arkasına) doğru açılacağı yönden seçilir.
            var meta = File.Exists(AmbulanceMetaPath) ? Bolum1Coordinates.LoadJson(AmbulanceMetaPath) : null;
            amb.doorLeftAxis = HingeAxis(meta, "SM_Ambulance_Door_RL", Vector3.up, out float l);
            amb.doorRightAxis = HingeAxis(meta, "SM_Ambulance_Door_RR", Vector3.up, out float r);
            amb.stepAxis = HingeAxis(meta, "SM_Ambulance_Step_Rear", Vector3.right, out float st);
            l = l > 0f ? l : 100f; r = r > 0f ? r : 100f; st = st > 0f ? st : 90f;
            amb.doorLeftOpenAngle = OutwardSign(root.transform, amb.doorLeft, amb.doorLeftAxis, l) * l;
            amb.doorRightOpenAngle = OutwardSign(root.transform, amb.doorRight, amb.doorRightAxis, r) * r;
            amb.stepOpenAngle = OutwardSign(root.transform, amb.rearStep, amb.stepAxis, st) * st;
            Line("Ambulans: gövde kutusu merkez " + V(center) + " boyut " + V(size) + ", tekerlek yarıçapı " + follower.wheelRadius.ToString("F3", Inv)
                + ", açılar RL " + amb.doorLeftOpenAngle.ToString("F0", Inv) + "° RR " + amb.doorRightOpenAngle.ToString("F0", Inv)
                + "° basamak " + amb.stepOpenAngle.ToString("F0", Inv) + "°" + (meta != null ? " (meta)" : " (varsayılan eksen)"));

            PrefabUtility.SaveAsPrefabAsset(root, AmbulancePrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // Yer tutucu ambulans: kök SM_Ambulance (pivot = tekerlek dikdörtgeninin merkezi, yerde), ön +Z.
    // 6.0 m boy (z −3..+3), 2.0 m en, 2.65 m yükseklik (çakar dahil). Arka çift kapı z = −3'te; gövdenin içi boş.
    private static GameObject BuildAmbulancePlaceholder()
    {
        var white = Mat("M_Placeholder_AmbulanceBody", new Color(0.93f, 0.93f, 0.9f));
        var red = Mat("M_Placeholder_AmbulanceStripe", new Color(0.8f, 0.12f, 0.1f));
        var glass = Mat("M_Placeholder_AmbulanceGlass", new Color(0.18f, 0.24f, 0.3f), 0.9f);
        var tire = Mat("M_Placeholder_Tire", new Color(0.12f, 0.12f, 0.13f));
        var flash = Mat("M_Placeholder_AmbulanceFlasher", new Color(0.2f, 0.35f, 0.9f), 0.6f, true);
        var interior = Mat("M_Placeholder_AmbulanceInterior", new Color(0.55f, 0.57f, 0.6f));

        var model = new GameObject("SM_Ambulance");
        var bodyT = Empty("SM_Ambulance_Body", model.transform, Vector3.zero);
        Box("Floor", bodyT, new Vector3(0f, 0.425f, 0.03f), new Vector3(2f, 0.15f, 5.94f), white);
        Box("Roof", bodyT, new Vector3(0f, 2.35f, 0.03f), new Vector3(2f, 0.1f, 5.94f), white);
        Box("WallL", bodyT, new Vector3(-0.96f, 1.4f, 0.03f), new Vector3(0.08f, 1.8f, 5.94f), white);
        Box("WallR", bodyT, new Vector3(0.96f, 1.4f, 0.03f), new Vector3(0.08f, 1.8f, 5.94f), white);
        Box("Front", bodyT, new Vector3(0f, 1.4f, 2.95f), new Vector3(2f, 1.8f, 0.1f), white);
        Box("StripeL", bodyT, new Vector3(-1.005f, 1.25f, 0.03f), new Vector3(0.01f, 0.18f, 5.9f), red);
        Box("StripeR", bodyT, new Vector3(1.005f, 1.25f, 0.03f), new Vector3(0.01f, 0.18f, 5.9f), red);
        var glassT = Empty("SM_Ambulance_Glass", model.transform, Vector3.zero);
        Box("Windshield", glassT, new Vector3(0f, 1.85f, 3.005f), new Vector3(1.7f, 0.65f, 0.02f), glass);
        var interiorT = Empty("SM_Ambulance_Interior", model.transform, Vector3.zero);
        Box("Bench", interiorT, new Vector3(0f, 0.6f, 1.6f), new Vector3(1.8f, 0.2f, 0.5f), interior);

        // Arka çift kapı: pivot dış dikey menteşede.
        var rl = Empty("SM_Ambulance_Door_RL", model.transform, new Vector3(-0.98f, 1.3f, -2.97f));
        Box("Geo", rl, new Vector3(0.49f, 0f, 0f), new Vector3(0.96f, 1.8f, 0.06f), white);
        var rr = Empty("SM_Ambulance_Door_RR", model.transform, new Vector3(0.98f, 1.3f, -2.97f));
        Box("Geo", rr, new Vector3(-0.49f, 0f, 0f), new Vector3(0.96f, 1.8f, 0.06f), white);
        // Basamak: katlıyken menteşeden aşağı sarkar, açılınca arkaya yatay uzanır.
        var step = Empty("SM_Ambulance_Step_Rear", model.transform, new Vector3(0f, 0.36f, -3.0f));
        Box("Geo", step, new Vector3(0f, -0.15f, -0.02f), new Vector3(1.3f, 0.3f, 0.04f), interior);

        foreach (var w in new[] { ("FL", -0.9f, 2f), ("FR", 0.9f, 2f), ("RL", -0.9f, -2f), ("RR", 0.9f, -2f) })
        {
            var wt = Empty("SM_Ambulance_Wheel_" + w.Item1, model.transform, new Vector3(w.Item2, 0.38f, w.Item3));
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.name = "Geo";
            cyl.transform.SetParent(wt, false);
            cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // silindir ekseni X (aks)
            cyl.transform.localScale = new Vector3(0.76f, 0.12f, 0.76f);
            cyl.GetComponent<MeshRenderer>().sharedMaterial = tire;
            // Dönüş görünsün diye merkezden kaçık bir jant işareti
            Box("Cap", wt, new Vector3(w.Item2 < 0 ? -0.125f : 0.125f, 0.16f, 0f), new Vector3(0.01f, 0.14f, 0.14f), white);
        }

        Box("SM_Ambulance_Lightbar", model.transform, new Vector3(0f, 2.525f, 1.9f), new Vector3(1.3f, 0.25f, 0.35f), flash);
        Box("SM_Ambulance_Light_RearL", model.transform, new Vector3(-0.85f, 2.22f, -3.0f), new Vector3(0.2f, 0.16f, 0.06f), flash);
        Box("SM_Ambulance_Light_RearR", model.transform, new Vector3(0.85f, 2.22f, -3.0f), new Vector3(0.2f, 0.16f, 0.06f), flash);

        // Boş objeler: yatan hastanın kalça noktası (+ileri = baş yönü), yükleme noktası, siren.
        Empty("PatientSlot_01", model.transform, new Vector3(-0.45f, 0.5f, -0.9f));
        Empty("PatientSlot_02", model.transform, new Vector3(0.45f, 0.5f, -0.9f));
        Empty("LoadPoint", model.transform, new Vector3(0f, 0f, -4.2f));
        Empty("SirenPoint", model.transform, new Vector3(0f, 2.5f, 1.9f));
        return model;
    }

    // ---- Sedye ----
    private static void BuildStretcherPrefab()
    {
        var root = new GameObject("Stretcher");
        try
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(StretcherModelPath);
            GameObject model;
            if (fbx != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = "SM_Stretcher";
                model.transform.SetParent(root.transform, false);
                Line("Sedye modeli: " + StretcherModelPath);
            }
            else
            {
                model = BuildStretcherPlaceholder();
                model.transform.SetParent(root.transform, false);
                Line("Sedye modeli YOK → yer tutucu (2.10 × 0.55 m, kanvas 0.12 m)");
            }
            Transform Find(string n)
            {
                var t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                if (t == null) throw new Exception("Sedye parçası eksik: " + n);
                return t;
            }

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var b = LocalBounds(root.transform, model.transform);
            var box = root.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;

            var s = root.AddComponent<StretcherController>();
            s.rules = Rules;
            s.handleFront = Find("Handle_Front");
            s.handleRear = Find("Handle_Rear");
            // Yatma çerçevesi: baş yönü = ön sap tarafı, yukarı = sedyenin yukarısı.
            var anchor = Find("PatientAnchor");
            var frame = new GameObject("LieFrame").transform;
            frame.SetParent(anchor, false);
            Vector3 headDir = s.handleFront.position - s.handleRear.position;
            frame.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(headDir, root.transform.up).normalized, root.transform.up);
            if (fbx != null)
            {
                float surface = Convert.ToSingle(Bolum1Coordinates.Obj(Bolum1Coordinates.LoadJson("Assets/Models/Props/Stretcher/stretcher_meta.json"), "dimensions")["canvas_centre_height_m"]);
                Vector3 p = root.transform.InverseTransformPoint(frame.position);
                p.y = surface;
                frame.position = root.transform.TransformPoint(p);
            }
            s.patientAnchor = frame;
            s.colliders = new Collider[] { box };
            Line("Sedye: collider " + V(b.center) + " / " + V(b.size) + ", sap aralığı " + headDir.magnitude.ToString("F2", Inv) + " m");
            PrefabUtility.SaveAsPrefabAsset(root, StretcherPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // Yer tutucu sedye: kök SM_Stretcher (pivot ayakların ortası, yerde), ön +Z. 2.10 × 0.55 m, kanvas yerden 0.12 m.
    private static GameObject BuildStretcherPlaceholder()
    {
        var canvas = Mat("M_Placeholder_StretcherCanvas", new Color(0.85f, 0.42f, 0.15f));
        var metal = Mat("M_Placeholder_StretcherFrame", new Color(0.7f, 0.72f, 0.75f), 0.7f);
        var model = new GameObject("SM_Stretcher");
        var mesh = Empty("SM_Stretcher_Mesh", model.transform, Vector3.zero);
        Box("Canvas", mesh, new Vector3(0f, 0.12f, 0f), new Vector3(0.47f, 0.02f, 1.85f), canvas);
        Box("PoleL", mesh, new Vector3(-0.255f, 0.125f, 0f), new Vector3(0.04f, 0.04f, 2.1f), metal);
        Box("PoleR", mesh, new Vector3(0.255f, 0.125f, 0f), new Vector3(0.04f, 0.04f, 2.1f), metal);
        foreach (float z in new[] { -0.75f, 0.75f })
            foreach (float x in new[] { -0.22f, 0.22f })
                Box("Leg", mesh, new Vector3(x, 0.05f, z), new Vector3(0.03f, 0.1f, 0.03f), metal);
        Empty("Handle_Front", model.transform, new Vector3(0f, 0.125f, 1.05f));
        Empty("Handle_Rear", model.transform, new Vector3(0f, 0.125f, -1.05f));
        Empty("PatientAnchor", model.transform, new Vector3(0f, 0.13f, 0.05f));
        return model;
    }

    // ---- Hastalar ----
    private static void BuildPatientPrefab(string type, string displayName)
    {
        string modelPath = PatientModelPath(type);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        bool placeholder = fbx == null;
        if (placeholder) fbx = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPatientModel);
        if (fbx == null) throw new Exception("Hasta modeli ve yer tutucu yok: " + modelPath);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        try
        {
            // Düz prefab olarak kaydedilsin (yer tutucudan gerçek modele geçişte taban değişebilir).
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Patient_" + type;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            if (animator == null) throw new Exception("Hasta modelinde Animator yok: " + modelPath);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) throw new Exception("Hasta modelinde Humanoid Hips yok: " + modelPath);

            if (placeholder)
            {
                var gray = Mat("M_Placeholder_Patient", new Color(0.62f, 0.62f, 0.64f));
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.sharedMaterials = Enumerable.Repeat(gray, smr.sharedMaterials.Length).ToArray();
            }

            var p = go.AddComponent<Patient>();
            p.displayName = displayName;
            p.hips = hips;
            p.animator = animator;
            p.hipsToBack = HipsToBack(go, hips, type, out string source);

            // Yatma pozu klibi (Civilian: kol askılı). Yoksa Animator kapalı, model T-pozda kalır.
            string clipPath = type == "Civilian" ? LiePoseArmSlingPath : LiePosePath;
            var clip = AssetDatabase.LoadAllAssetsAtPath(clipPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (clip != null)
            {
                string acPath = AnimationFolder + "/AC_" + Path.GetFileNameWithoutExtension(clipPath) + ".controller";
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(acPath);
                if (ac == null)
                {
                    ac = AnimatorController.CreateAnimatorControllerAtPath(acPath);
                    ac.layers[0].stateMachine.AddState("Lie").motion = clip;
                }
                foreach (var state in ac.layers[0].stateMachine.states)
                    if (state.state.name == "Lie") state.state.motion = clip;
                // Prosedürel yatma klipleri (Lying_HelpWave / Lying_Groan); Civilian: askılı kol + maskeli inleme
                animator.runtimeAnimatorController = Bolum1AnimBuilder.PatientController(type, clip, out p.canWave);
                animator.enabled = true;
                p.hasLiePose = true;
            }
            else
            {
                animator.runtimeAnimatorController = null;
                animator.enabled = false;
                p.hasLiePose = false;
            }

            BuildRagdoll(animator, 50f, out p.ragdollBodies, out p.ragdollColliders);
            Line("Hasta " + type + " (" + displayName + "): " + (placeholder ? "yer tutucu " + PlaceholderPatientModel + " (gri)" : modelPath)
                + ", yatma klibi " + (clip != null ? clip.name : "YOK (T-poz, Animator kapalı)")
                + ", hipsToBack " + p.hipsToBack.ToString("F3", Inv) + " m (" + source + "), ragdoll " + p.ragdollBodies.Length + " gövde");
            PrefabUtility.SaveAsPrefabAsset(go, PatientPrefabPath(type));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // Kalçadan sırt yüzeyine uzaklık: patient_meta.json "hips_to_back_m" ya da mesh sınırı.
    private static float HipsToBack(GameObject character, Transform hips, string type, out string source)
    {
        if (File.Exists(PatientMetaPath))
        {
            var meta = Bolum1Coordinates.LoadJson(PatientMetaPath);
            if (meta.TryGetValue("hips_to_back_m", out object offsets) && offsets is Dictionary<string, object> variants && variants.TryGetValue(type, out object offset))
            {
                source = "patient_meta.json";
                return Convert.ToSingle(offset);
            }
        }
        // Bağ pozunda (T-poz, yüz +Z): kalça hizasından göğse kadar olan bantta en arka (−Z) köşe.
        Vector3 h = character.transform.InverseTransformPoint(hips.position);
        float minZ = float.MaxValue;
        var baked = new Mesh();
        foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.BakeMesh(baked);
            var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (var v in baked.vertices)
            {
                Vector3 p = character.transform.InverseTransformPoint(m.MultiplyPoint3x4(v));
                if (p.y < h.y - 0.1f || p.y > h.y + 0.45f || Mathf.Abs(p.x - h.x) > 0.2f) continue;
                minZ = Mathf.Min(minZ, p.z);
            }
        }
        UnityEngine.Object.DestroyImmediate(baked);
        source = "mesh ölçümü";
        return minZ < float.MaxValue ? Mathf.Max(0.02f, h.z - minZ) : 0.12f;
    }

    private static bool FindNumber(object node, string key, out double value)
    {
        value = 0;
        if (node is Dictionary<string, object> d)
        {
            if (d.TryGetValue(key, out object v) && v is double dv) { value = dv; return true; }
            foreach (var kv in d) if (FindNumber(kv.Value, key, out value)) return true;
        }
        else if (node is List<object> l)
            foreach (var x in l) if (FindNumber(x, key, out value)) return true;
        return false;
    }

    // Ragdoll: kemiklere collider + Rigidbody + CharacterJoint (Ragdoll Wizard'ın sade karşılığı).
    // Varsayılan kapalı (kinematik, collider'lar kapalı); Patient.Fall ve PlayerKnockdown açar (trafik builder'ı da kullanır).
    public static void BuildRagdoll(Animator a, float totalMass, out Rigidbody[] ragdollBodies, out Collider[] ragdollColliders)
    {
        var bodies = new List<Rigidbody>();
        var colliders = new List<Collider>();
        Transform B(HumanBodyBones b) { return a.GetBoneTransform(b); }
        Transform chest = B(HumanBodyBones.UpperChest);
        if (chest == null) chest = B(HumanBodyBones.Chest);
        if (chest == null) chest = B(HumanBodyBones.Spine);

        Rigidbody AddBody(Transform bone, float massFraction, Rigidbody parent)
        {
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = totalMass * massFraction;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None; // ragdoll açılınca Patient Interpolate yapar;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            if (parent != null)
            {
                var j = bone.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent;
                j.axis = Vector3.right;
                j.swingAxis = Vector3.forward;
                j.lowTwistLimit = new SoftJointLimit { limit = -30f };
                j.highTwistLimit = new SoftJointLimit { limit = 40f };
                j.swing1Limit = new SoftJointLimit { limit = 40f };
                j.swing2Limit = new SoftJointLimit { limit = 30f };
                j.enableProjection = true;
            }
            bodies.Add(rb);
            return rb;
        }

        void Capsule(Transform bone, Transform toward, float radiusWorld)
        {
            var c = bone.gameObject.AddComponent<CapsuleCollider>();
            Vector3 local = bone.InverseTransformPoint(toward.position);
            int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            c.direction = axis;
            c.center = local * 0.5f;
            c.radius = radiusWorld / scale;
            c.height = local.magnitude + c.radius * 2f;
            c.enabled = false;
            colliders.Add(c);
        }

        void BoxOn(Transform bone, Vector3 centerWorldOffset, Vector3 sizeWorld)
        {
            var c = bone.gameObject.AddComponent<BoxCollider>();
            c.center = bone.InverseTransformPoint(bone.position + centerWorldOffset);
            Vector3 sz = bone.InverseTransformVector(sizeWorld);
            c.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            c.enabled = false;
            colliders.Add(c);
        }

        var hips = B(HumanBodyBones.Hips);
        var head = B(HumanBodyBones.Head);
        var rbHips = AddBody(hips, 0.18f, null);
        BoxOn(hips, Vector3.up * 0.04f, new Vector3(0.30f, 0.18f, 0.2f));
        var rbChest = AddBody(chest, 0.24f, rbHips);
        float torso = Vector3.Distance(head.position, hips.position);
        BoxOn(chest, (head.position - chest.position) * 0.4f, new Vector3(0.32f, Mathf.Max(0.15f, torso * 0.45f), 0.2f));
        AddBody(head, 0.1f, rbChest);
        var sc = head.gameObject.AddComponent<SphereCollider>();
        sc.radius = 0.16f / Mathf.Max(1e-4f, head.lossyScale.x);
        sc.center = head.InverseTransformPoint(head.position + Vector3.up * 0.12f);
        sc.enabled = false;
        colliders.Add(sc);

        foreach (var side in new[] { (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                                     (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand) })
        {
            var up = AddBody(B(side.Item1), 0.03f, rbChest);
            Capsule(B(side.Item1), B(side.Item2), 0.05f);
            AddBody(B(side.Item2), 0.02f, up);
            Capsule(B(side.Item2), B(side.Item3), 0.045f);
        }
        foreach (var side in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                                     (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) })
        {
            var up = AddBody(B(side.Item1), 0.1f, rbHips);
            Capsule(B(side.Item1), B(side.Item2), 0.07f);
            AddBody(B(side.Item2), 0.06f, up);
            Capsule(B(side.Item2), B(side.Item3), 0.055f);
        }
        ragdollBodies = bodies.ToArray();
        ragdollColliders = colliders.ToArray();
    }

    // =====================================================================================
    // Sahne kurulumu (Bolum1SceneBuilder.Build sonunda çağrılır)
    // =====================================================================================

    public static void SetupScene(Dictionary<string, object> markers, GameObject minivan, GameObject truck, GameObject character)
    {
        Physics.SyncTransforms();
        var markerData = Bolum1Coordinates.Obj(markers, "markers");
        var root = new GameObject(GameplayRootName).transform;
        var rules = Rules;

        // 1) Şerit
        var laneGo = new GameObject(LaneName);
        laneGo.transform.SetParent(root, false);
        var lane = laneGo.AddComponent<LanePath>();
        lane.points = ((List<object>)Bolum1Coordinates.Obj(markerData, "TrafficLane_EastBound")["polyline"])
            .Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToArray();
        lane.width = 4f;
        lane.Rebuild();
        Line("Şerit " + LaneName + ": " + lane.points.Length + " nokta, " + lane.Length.ToString("F1", Inv) + " m");

        // 2) Durak: korkuluk boşluğu (rampa ağzı) raycast'le bulunur; arka tampon ağzın ~4 m doğusunda.
        var ambPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmbulancePrefabPath).GetComponent<AmbulanceController>();
        float rearOverhang = ambPrefab.RearOverhang;
        FindGuardrailGap(lane, out float gapStart, out float gapEnd);
        float mouth = (gapStart + gapEnd) * 0.5f;
        float rearS = Mathf.Max(mouth + StopRearOffset, gapEnd + 0.5f);
        float stopS = rearS + rearOverhang;
        var stopGo = new GameObject(StopName);
        stopGo.transform.SetParent(root, false);
        stopGo.transform.SetPositionAndRotation(lane.PointAt(stopS), Quaternion.LookRotation(lane.TangentAt(stopS), Vector3.up));
        var stop = stopGo.AddComponent<AmbulanceStop>();
        stop.rearOverhang = rearOverhang;
        stop.vehicleLength = ambPrefab.bodySize.z;
        Vector3 gA = lane.PointAt(gapStart), gB = lane.PointAt(gapEnd);
        Line("Korkuluk boşluğu (rampa ağzı): s " + gapStart.ToString("F1", Inv) + "–" + gapEnd.ToString("F1", Inv) + " m, Blender X "
            + (-gA.x).ToString("F1", Inv) + "…" + (-gB.x).ToString("F1", Inv) + " (" + (gapEnd - gapStart).ToString("F1", Inv) + " m)");
        Line("AmbulanceStop: Unity " + V(stopGo.transform.position) + " (s " + stopS.ToString("F1", Inv) + "), arka tampon s " + rearS.ToString("F1", Inv)
            + " = ağız ortası + " + (rearS - mouth).ToString("F1", Inv) + " m, boşluğun doğu ucundan " + (rearS - gapEnd).ToString("F1", Inv) + " m doğuda");

        // 3) Hastalar
        var patients = PlacePatients(markers, minivan, root);

        // 4) Sedyeler: çekicinin kasasında
        PlaceStretchers(truck, root);

        // 5) Oyuncu ve 6) test botu
        var cam = Camera.main;
        var player = SetupPlayer(character, markerData, lane, cam, rules);
        var bot = SetupBot(player.transform, rules, root);
        var interaction = player.GetComponent<PlayerInteraction>();
        interaction.helper = bot;

        // 7) Görev ve HUD
        var missionGo = new GameObject(MissionName);
        missionGo.transform.SetParent(root, false);
        var mission = missionGo.AddComponent<MissionDirector>();
        mission.schedule = Schedule;
        mission.ambulancePrefab = ambPrefab;
        mission.lane = lane;
        mission.ambulanceStop = stopGo.transform;
        mission.patients = patients;

        var hudGo = new GameObject(HudName);
        hudGo.transform.SetParent(root, false);
        var hud = hudGo.AddComponent<MissionHud>();
        hud.mission = mission;
        hud.interaction = interaction;

        WriteReport();
    }

    // Şeridin sağında (vadi tarafı) korkuluk olmayan aralık: Blender X −45…−25, ağız ortası −35'e en yakın aralık.
    private static void FindGuardrailGap(LanePath lane, out float start, out float end)
    {
        var guard = GameObject.Find("SM_B1_Guardrail").transform;
        var runs = new List<Vector2>();
        float runStart = -1f, last = -1f;
        for (float s = 0f; s <= lane.Length; s += 0.25f)
        {
            Vector3 p = lane.PointAt(s);
            float bx = -p.x, by = -p.z;
            if (bx < -45f || bx > -25f || by > 10f) { Close(); continue; } // by > 10: virajın uzak (dağ) kolu
            bool guarded = false;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.5f, lane.RightAt(s), 7f))
                if (h.transform.IsChildOf(guard)) { guarded = true; break; }
            if (!guarded) { if (runStart < 0f) runStart = s; last = s; }
            else Close();
        }
        Close();
        if (runs.Count == 0) throw new Exception("Korkuluk boşluğu bulunamadı (Blender X −45…−25)");
        var best = runs.OrderBy(r => Mathf.Abs(-lane.PointAt((r.x + r.y) * 0.5f).x + 35f)).First();
        start = best.x;
        end = best.y;

        void Close()
        {
            if (runStart >= 0f && last - runStart >= 1f) runs.Add(new Vector2(runStart, last));
            runStart = -1f;
        }
    }

    // Hastalar: patikada, minibüsün batısında (rampaya doğru), sırt üstü. Konum raycast'le seçilir:
    // patika zemini, birbirinden ≥ 1.5 m, uçurum kenarından ≥ 1.5 m, minibüse/çantalara/kayalara değmez.
    private static Patient[] PlacePatients(Dictionary<string, object> markers, GameObject minivan, Transform parent)
    {
        var groundRoot = GameObject.Find("SM_B1_Ground").transform;
        var path = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["path"]).Select(Bolum1Coordinates.Vec3).OrderBy(v => v.x).ToList();
        Vector3 vanBlender = Bolum1Coordinates.Vec3(Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "MinivanPlacement"), "position");
        var chosen = new List<Vector3>();
        var result = new List<Patient>();

        for (int i = 0; i < PatientTypes.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PatientPrefabPath(PatientTypes[i]));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var patient = go.GetComponent<Patient>();
            Vector3 hipsInRoot = go.transform.InverseTransformPoint(patient.hips.position);

            bool placed = false;
            // Minibüsün batısından rampaya doğru tara: X (Blender) −2 → −10.5, patika orta çizgisi ± 1 m.
            for (float bx = Mathf.Min(vanBlender.x - 6f, -2f); bx >= -10.5f && !placed; bx -= 0.25f)
            {
                Vector3 center = PathCenter(path, bx);
                foreach (float off in new[] { 0f, 0.5f, -0.5f, 1f, -1f })
                {
                    Vector3 u = Bolum1Coordinates.ToUnity(new Vector3(bx, center.y + off, center.z));
                    if (!TryLyingSpot(u, groundRoot, minivan.transform, chosen, out Vector3 point, out Vector3 normal, out float cliff)) continue;
                    // Baş batıya (rampaya doğru): Blender −X = Unity +X
                    Patient.LiePoseInFrame(hipsInRoot, patient.hipsToBack, out Vector3 lp, out Quaternion lr);
                    Quaternion frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.right, normal).normalized, normal);
                    go.transform.SetPositionAndRotation(point + frame * lp, frame * lr);
                    chosen.Add(point);
                    placed = true;
                    Line("Hasta " + PatientTypes[i] + ": kalça altı zemin Unity " + V(point) + " (Blender X " + (-point.x).ToString("F2", Inv)
                        + ", Y " + (-point.z).ToString("F2", Inv) + "), uçurum kenarına " + (cliff > 3.5f ? "> 3.5" : cliff.ToString("F2", Inv)) + " m");
                    break;
                }
            }
            if (!placed) throw new Exception("Hasta için uygun yer bulunamadı: " + PatientTypes[i]);
            go.name = "Patient_" + PatientTypes[i];
            result.Add(patient);
        }
        for (int a = 0; a < chosen.Count; a++)
            for (int b = a + 1; b < chosen.Count; b++)
                Line("  hasta arası " + PatientTypes[a] + "–" + PatientTypes[b] + ": " + Vector3.Distance(chosen[a], chosen[b]).ToString("F2", Inv) + " m");
        return result.ToArray();
    }

    private static Vector3 PathCenter(List<Vector3> path, float bx)
    {
        for (int i = 0; i + 1 < path.Count; i++)
            if (bx >= path[i].x && bx <= path[i + 1].x)
                return Vector3.Lerp(path[i], path[i + 1], Mathf.InverseLerp(path[i].x, path[i + 1].x, bx));
        return bx < path[0].x ? path[0] : path[path.Count - 1];
    }

    // Yatan bir hasta için yer uygun mu? Gövde boyunca (baş +X) 1.6 × 0.6 m'lik ayak izi kontrol edilir.
    private static bool TryLyingSpot(Vector3 u, Transform groundRoot, Transform minivan, List<Vector3> chosen, out Vector3 point, out Vector3 normal, out float cliffClearance)
    {
        point = default; normal = Vector3.up; cliffClearance = 0f;
        if (!RayDown(u, groundRoot, out RaycastHit hit) || hit.normal.y < 0.9f) return false;
        point = hit.point;
        normal = hit.normal;
        foreach (var c in chosen)
            if (Vector3.Distance(c, point) < 2.5f) return false; // gövdeler boy boyu dizilir: 2.5 m kalça aralığı ≥ 1.5 m

        Vector3 along = Vector3.right, side = Vector3.forward;
        foreach (float a in new[] { -0.85f, 0f, 0.75f })
            foreach (float s in new[] { -0.3f, 0f, 0.3f })
            {
                if (!RayDown(point + along * a + side * s, groundRoot, out RaycastHit h)) return false;
                if (Mathf.Abs(h.point.y - point.y) > 0.15f) return false;
            }

        // Uçurum kenarı: vadi tarafına (Blender −Y = Unity +Z) doğru zemin 0.5 m'den fazla düşmemeli; ilk düşüşe kadar mesafe.
        cliffClearance = 99f;
        foreach (float a in new[] { -0.85f, 0f, 0.75f })
            foreach (float s in new[] { 0.3f })
                for (float d = 0f; d <= 4f; d += 0.1f)
                {
                    Vector3 q = point + along * a + Vector3.forward * (s + d);
                    if (!(RayDown(q, groundRoot, out RaycastHit h) && h.point.y > point.y - 0.5f))
                    {
                        cliffClearance = Mathf.Min(cliffClearance, d);
                        break;
                    }
                }
        if (cliffClearance < 1.5f) return false;

        // Çakışma: minibüs, çantalar, kayalar (mekân zemini/yol/korkuluk hariç her collider).
        var env = new[] { "SM_B1_Ground", "SM_B1_Road", "SM_B1_Guardrail" }.Select(n => GameObject.Find(n).transform).ToArray();
        foreach (var c in Physics.OverlapBox(point + Vector3.up * 0.35f, new Vector3(1.0f, 0.3f, 0.5f), Quaternion.identity))
            if (!env.Any(e => c.transform.IsChildOf(e))) return false;
        foreach (var c in minivan.GetComponentsInChildren<Collider>())
            if (Vector3.Distance(c.ClosestPoint(point + Vector3.up * 0.3f), point + Vector3.up * 0.3f) < 1f) return false;
        return true;
    }

    private static bool RayDown(Vector3 p, Transform onlyUnder, out RaycastHit result)
    {
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 8f).OrderBy(h => h.distance))
        {
            if (onlyUnder == null || h.transform.IsChildOf(onlyUnder)) { result = h; return true; }
            if (!h.collider.isTrigger) break; // üstte başka bir şey var
        }
        result = default;
        return false;
    }

    // Sedyeler: çekici kasasının koni tutucusu olmayan yarısında (çekici kökünde Blender X < 0.6), yan yana, boylamasına.
    // Yer, çekici mesh'lerine geçici MeshCollider eklenip ızgara raycast'le doğrulanır (ilk çarpma kasa yüzeyi olmalı).
    private static StretcherController[] PlaceStretchers(GameObject truck, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StretcherPrefabPath);
        var temp = new List<MeshCollider>();
        foreach (var mf in truck.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            temp.Add(mc);
        }
        Physics.SyncTransforms();
        var result = new List<StretcherController>();
        try
        {
            Vector3 L(float bx, float by, float bz) { return truck.transform.TransformPoint(new Vector3(-bx, by, bz)); }
            Vector3 fwd = truck.transform.TransformDirection(new Vector3(0f, -1f, 0f)).normalized;
            Vector3 up = truck.transform.TransformDirection(new Vector3(0f, 0f, 1f)).normalized;
            float[] xs = { -0.85f, -0.2f };
            for (int i = 0; i < 2; i++)
            {
                Vector3 c = L(xs[i], 0.8f, 1.75f);
                float worst = 0f;
                string blocker = "";
                for (float a = -1.05f; a <= 1.051f; a += 0.15f)
                    for (float s = -0.275f; s <= 0.276f; s += 0.1375f)
                    {
                        Vector3 q = c + fwd * a + Vector3.Cross(up, fwd) * s;
                        if (!Physics.Raycast(q + up * 1.0f, -up, out RaycastHit h, 1.5f)) { worst = 99f; blocker = "boş"; continue; }
                        float d = Vector3.Dot(h.point - c, up);
                        if (Mathf.Abs(d) > Mathf.Abs(worst)) { worst = d; blocker = h.collider.name; }
                    }
                if (Mathf.Abs(worst) > 0.03f)
                    throw new Exception("Çekici kasasında sedye yeri dolu: " + blocker + " " + worst.ToString("F3", Inv));
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = StretcherNames[i];
                go.transform.SetParent(parent, true);
                go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(fwd, up));
                result.Add(go.GetComponent<StretcherController>());
                Line("Sedye " + go.name + ": çekici kasasında, Unity " + V(c) + " (çekici kökü Blender " + xs[i].ToString("F2", Inv)
                    + ", 0.80, 1.75), kasa yüzeyi sapması " + (worst * 100f).ToString("F1", Inv) + " cm");
            }
        }
        finally
        {
            foreach (var mc in temp) UnityEngine.Object.DestroyImmediate(mc);
            Physics.SyncTransforms();
        }
        foreach (var s in result)
        {
            var box = s.GetComponent<BoxCollider>();
            foreach (var c in Physics.OverlapBox(s.transform.TransformPoint(box.center), box.size * 0.5f, s.transform.rotation))
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)
                    throw new Exception("Sedye koniyle çakışıyor: " + c.name);
        }
        return result.ToArray();
    }

    private static ThirdPersonPlayer SetupPlayer(GameObject character, Dictionary<string, object> markerData, LanePath lane, Camera cam, CarryRules rules)
    {
        Vector3 spawn = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(markerData["PlayerSpawn"], "position"));
        if (RayDown(spawn, null, out RaycastHit hit)) spawn.y = hit.point.y;
        Vector3 dir = lane.TangentAt(lane.Project(spawn));
        dir.y = 0f;
        dir.Normalize();
        character.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(dir, Vector3.up));

        var animator = character.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(LocomotionControllerPath);
        animator.applyRootMotion = false;

        var cc = character.AddComponent<CharacterController>();
        cc.height = 1.5f;
        cc.radius = 0.25f;
        cc.skinWidth = 0.02f;
        cc.center = new Vector3(0f, 0.75f + cc.skinWidth, 0f);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.3f;

        var player = character.AddComponent<ThirdPersonPlayer>();
        player.animator = animator;
        player.rules = rules;
        player.cameraTransform = cam.transform;
        player.moveSpeed = PlayerMoveSpeed;
        var interaction = character.AddComponent<PlayerInteraction>();
        interaction.player = player;
        var orbit = cam.gameObject.AddComponent<OrbitCamera>();
        orbit.target = character.transform;
        var input = character.AddComponent<PlayerInputReader>();
        input.player = player;
        input.interaction = interaction;
        input.orbitCamera = orbit;
        cam.transform.position = spawn + Vector3.up * 2.6f - dir * 4.2f;
        cam.transform.LookAt(spawn + Vector3.up * 1.3f);
        Line("Oyuncu (" + character.name + "): PlayerSpawn Unity " + V(spawn) + ", hız " + PlayerMoveSpeed.ToString(Inv) + " m/sn, CharacterController 1.5 m, eğim sınırı 45°");
        return player;
    }

    private static StretcherHelperBot SetupBot(Transform player, CarryRules rules, Transform parent)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(BotModelPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = BotName;
        go.transform.SetParent(parent, true);
        Vector3 pos = player.position - player.right * 1.5f; // oyuncunun solunda (dağ tarafı)
        if (RayDown(pos, null, out RaycastHit hit)) pos.y = hit.point.y;
        go.transform.SetPositionAndRotation(pos, player.rotation);
        var animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(LocomotionControllerPath);
        animator.applyRootMotion = false;
        var bot = go.AddComponent<StretcherHelperBot>();
        bot.rules = rules;
        bot.animator = animator;
        bot.assistEnabled = true;
        Line("Test botu: " + BotModelPath + " Unity " + V(pos) + ", varsayılan AÇIK");
        return bot;
    }

    // =====================================================================================
    // Yardımcılar
    // =====================================================================================

    private static Transform LieFrame(Transform slot, Transform vehicleRoot)
    {
        var frame = new GameObject("LieFrame").transform;
        frame.SetParent(slot, false);
        frame.rotation = vehicleRoot.rotation; // baş yönü = aracın önü, yukarı = aracın yukarısı
        return frame;
    }

    // meta: parts[name].motion.axis ve work_pose_deg → Unity kök uzayı ekseni ve çalışma açısı.
    private static Vector3 HingeAxis(Dictionary<string, object> meta, string part, Vector3 fallback, out float magnitude)
    {
        magnitude = 0f;
        try
        {
            if (meta == null || !Bolum1Coordinates.Has(meta, "parts")) return fallback;
            var parts = Bolum1Coordinates.Obj(meta, "parts");
            if (!parts.TryGetValue(part, out object po) || !(po is Dictionary<string, object> pd)) return fallback;
            if (!pd.TryGetValue("motion", out object mo) || !(mo is Dictionary<string, object> motion)) return fallback;
            // Dönüş ekseni sözde vektördür: koordinat dönüşümünde el yönü de ters çevrilir.
            Vector3 axis = -Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(motion, "axis"));
            if (motion.TryGetValue("work_pose_deg", out object work))
                magnitude = Mathf.Abs(Convert.ToSingle(work));
            return axis.normalized;
        }
        catch { return fallback; }
    }

    // Parçanın açılınca aracın arkasına (dışa) doğru gitmesini sağlayan dönüş işareti (+1 / −1).
    private static float OutwardSign(Transform root, Transform part, Vector3 rootAxis, float angle)
    {
        var rs = part.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 1f;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        Vector3 axisW = root.rotation * rootAxis;
        Vector3 rel = b.center - part.position;
        float plus = Vector3.Dot(Quaternion.AngleAxis(angle, axisW) * rel, root.forward);
        float minus = Vector3.Dot(Quaternion.AngleAxis(-angle, axisW) * rel, root.forward);
        return plus <= minus ? 1f : -1f;
    }

    private static float WheelRadius(Transform wheel)
    {
        var rs = wheel.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 0.38f;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.extents.y;
    }

    // Bir alt ağacın mesh sınırları, verilen kökün yerel uzayında.
    private static Bounds LocalBounds(Transform space, Transform subtree)
    {
        bool any = false;
        Bounds result = default;
        foreach (var mf in subtree.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p);
            }
        }
        return result;
    }

    private static IEnumerable<Renderer> Renderers(Transform t) { return t.GetComponentsInChildren<Renderer>(true); }

    private static Transform Empty(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    private static Transform Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    private static Material Mat(string name, Color color, float smoothness = 0.35f, bool emissive = false)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emissive)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
