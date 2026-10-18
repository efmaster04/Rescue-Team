using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Bölüm 1 trafik: ayar asset'i, 5 araç prefab'ı ve sahne kurulumu (WestBound şeridi, trafik yönetmeni, koniler,
// oyuncu ragdoll'u). Bolum1SceneBuilder.Build içinden çağrılır (BuildAssets ambulans asset'lerinden sonra,
// SetupScene ambulans sahne kurulumundan sonra).
//
// SÖZLEŞME: Assets/Models/Vehicles/Traffic/SM_<Tip>.fbx + <tip>_meta.json (sanat reposu Export/Traffic, kopyası).
// Kök SM_<Tip> (pivot tekerleklerin yere değdiği dikdörtgenin merkezi, Z=0, burun +ileri). Çocuklar: SM_<Tip>_Body,
// SM_<Tip>_Glass, SM_<Tip>_Wheel_<konum> (binek FL/FR/RL/RR; kamyon F_L/F_R/R1_L/R1_R/R2_L/R2_R).
// FBX VARSA o, YOKSA aynı ad ve ölçülerde kutu yer tutucu kullanılır. FBX gelince builder'ı yeniden çalıştırmak yeterli.
public static class Bolum1TrafficBuilder
{
    public const string ModelFolder = "Assets/Models/Vehicles/Traffic/";
    public static string MetaPath(TrafficVehicleType t) { return ModelFolder + t.ToString().ToLowerInvariant() + "_meta.json"; }
    public const string PrefabFolder = Bolum1AmbulanceBuilder.PrefabFolder + "/Traffic";
    public const string MaterialFolder = Bolum1AmbulanceBuilder.MaterialFolder;
    public const string SettingsPath = Bolum1AmbulanceBuilder.SettingsFolder + "/TrafficSettings.asset";
    public const string ReportPath = "Captures/Bolum1/traffic_build_report.txt";

    public const string WestLaneName = "TrafficLane_WestBound";
    public const string DirectorName = "Bolum1_Traffic";
    public static string ConeControllerName(string cone) { return "TrafficCone_" + cone; }

    public static readonly TrafficVehicleType[] Types = { TrafficVehicleType.Sedan, TrafficVehicleType.Hatchback, TrafficVehicleType.Pickup, TrafficVehicleType.PanelVan, TrafficVehicleType.BoxTruck };

    public static string ModelPath(TrafficVehicleType t) { return ModelFolder + "SM_" + t + ".fbx"; }
    public static string PrefabPath(TrafficVehicleType t) { return PrefabFolder + "/Vehicle_" + t + ".prefab"; }
    public static TrafficSettings Settings { get { return AssetDatabase.LoadAssetAtPath<TrafficSettings>(SettingsPath); } }

    // Yer tutucu ölçüleri (U × G × Y, m), tekerlek yarıçapı, aks konumları (kök Z) ve iz genişliği.
    private struct Spec
    {
        public float length, width, height, wheelRadius, track;
        public float[] axles;
        public string[] wheelNames;
        public float engineHz, hornLow, hornHigh;
    }

    private static readonly string[] CarWheels = { "FL", "FR", "RL", "RR" };
    private static readonly string[] TruckWheels = { "F_L", "F_R", "R1_L", "R1_R", "R2_L", "R2_R" };

    private static Spec SpecFor(TrafficVehicleType t)
    {
        switch (t)
        {
            case TrafficVehicleType.Sedan: return new Spec { length = 4.5f, width = 1.8f, height = 1.45f, wheelRadius = 0.32f, track = 1.55f, axles = new[] { 1.35f, -1.35f }, wheelNames = CarWheels, engineHz = 55f, hornLow = 420f, hornHigh = 530f };
            case TrafficVehicleType.Hatchback: return new Spec { length = 4.0f, width = 1.75f, height = 1.5f, wheelRadius = 0.31f, track = 1.5f, axles = new[] { 1.25f, -1.25f }, wheelNames = CarWheels, engineHz = 60f, hornLow = 470f, hornHigh = 590f };
            case TrafficVehicleType.Pickup: return new Spec { length = 5.3f, width = 1.9f, height = 1.85f, wheelRadius = 0.38f, track = 1.6f, axles = new[] { 1.6f, -1.6f }, wheelNames = CarWheels, engineHz = 48f, hornLow = 380f, hornHigh = 480f };
            case TrafficVehicleType.PanelVan: return new Spec { length = 5.4f, width = 2.0f, height = 2.4f, wheelRadius = 0.36f, track = 1.7f, axles = new[] { 1.7f, -1.7f }, wheelNames = CarWheels, engineHz = 44f, hornLow = 350f, hornHigh = 440f };
            default: return new Spec { length = 9.5f, width = 2.5f, height = 3.6f, wheelRadius = 0.5f, track = 2.0f, axles = new[] { 3.3f, -2.0f, -3.3f }, wheelNames = TruckWheels, engineHz = 32f, hornLow = 220f, hornHigh = 277f };
        }
    }

    // Yer tutucu binek renkleri: 4 ayırt edilebilir düz renk (gerçek modelde traffic_meta.json'dan).
    public static readonly Color[] PlaceholderCarColors =
    {
        new Color(0.75f, 0.1f, 0.08f),   // kırmızı
        new Color(0.12f, 0.3f, 0.75f),   // mavi
        new Color(0.78f, 0.79f, 0.8f),   // gümüş
        new Color(0.1f, 0.42f, 0.18f),   // yeşil
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report = new StringBuilder();

    // =====================================================================================
    // Asset'ler
    // =====================================================================================

    public static void BuildAssets()
    {
        report = new StringBuilder();
        Line("TRAFFIC BUILD " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);
        if (Settings == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TrafficSettings>(), SettingsPath); // varsa (kullanıcı ayarı) dokunma
        AssetDatabase.SaveAssets();
        foreach (var t in Types) BuildVehiclePrefab(t, File.Exists(MetaPath(t)) ? Bolum1Coordinates.LoadJson(MetaPath(t)) : null);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static readonly Color SmokeColor = new Color(0.055f, 0.075f, 0.095f, 0.82f);
    public static Material SmokeGlass()
    {
        string path = MaterialFolder + "/M_Traffic_SmokeGlass.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", SmokeColor);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Smoothness", 0.65f);
        material.SetFloat("_Metallic", 0.05f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void BuildVehiclePrefab(TrafficVehicleType type, Dictionary<string, object> meta)
    {
        var spec = SpecFor(type);
        string modelName = "SM_" + type;
        var root = new GameObject("Vehicle_" + type);
        try
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(type));
            GameObject model;
            if (fbx != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = modelName;
                model.transform.SetParent(root.transform, false); // FBX kök dönüşü korunur; ön = +Z
            }
            else
            {
                model = BuildPlaceholder(type, spec);
                model.transform.SetParent(root.transform, false);
            }

            Transform Find(string n)
            {
                var t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                if (t == null) throw new Exception("Trafik aracı parçası eksik: " + n);
                return t;
            }

            var bodyPart = Find(modelName + "_Body");
            foreach (var renderer in Find(modelName + "_Glass").GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = SmokeGlass();
            var wheels = spec.wheelNames.Select(w => Find(modelName + "_Wheel_" + w)).ToArray();

            // Gövde kutusu (kök uzayında): gövde + cam sınırları; alt kenar alçak konileri de itsin diye 0.15 m.
            Bounds b = LocalBounds(root.transform, bodyPart);
            b.Encapsulate(LocalBounds(root.transform, Find(modelName + "_Glass")));
            float bottom = 0.15f;
            var center = new Vector3(b.center.x, (bottom + b.max.y) * 0.5f, b.center.z);
            var size = new Vector3(b.size.x, b.max.y - bottom, b.size.z);
            // Aynalar gövde ölçüsüne girmez (meta: measurement.width_m / dimensions_m.width): kutu genişliği ölçüye kırpılır.
            float metaWidth = MetaWidth(meta);
            if (fbx != null && metaWidth > 0.5f && size.x > metaWidth) { size.x = metaWidth; center.x = 0f; }

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var box = root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;

            var entry = TrafficSettingsDefaults.Entry(type);
            var follower = root.AddComponent<LaneFollower>();
            follower.cruiseSpeed = entry.cruiseSpeed;
            follower.acceleration = 3f;
            follower.braking = 6f;
            follower.wheels = wheels;
            follower.wheelRadius = WheelRadius(wheels[0], spec.wheelRadius);
            follower.frontOverhang = center.z + size.z * 0.5f;
            follower.rearOverhang = -(center.z - size.z * 0.5f);
            // Aks noktaları şeritte: yarım dingil mesafesi (en öndeki ve en arkadaki aks)
            var axleZ = wheels.Select(w => root.transform.InverseTransformPoint(w.position).z).ToArray();
            follower.axleHalfSpan = (axleZ.Max() - axleZ.Min()) * 0.5f;
            // 2 m'den geniş araç şeridin sağına yakın gider (3.5 m aralıklı şeritlerde iki kamyon virajda birbirine değmesin)
            follower.lateralOffset = Mathf.Max(0f, (size.x - 2.0f) * 0.5f);

            var audio = root.AddComponent<VehicleAudio>();
            audio.engine = root.AddComponent<AudioSource>();
            audio.horn = root.AddComponent<AudioSource>();
            foreach (var s in new[] { audio.engine, audio.horn })
            {
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.dopplerLevel = 1f;
            }
            audio.engineBaseHz = spec.engineHz;
            audio.hornLowHz = spec.hornLow;
            audio.hornHighHz = spec.hornHigh;
            audio.referenceSpeed = entry.cruiseSpeed;

            var v = root.AddComponent<TrafficVehicle>();
            v.type = type;
            v.settings = Settings;
            v.sound = audio;
            v.bodyCenter = center;
            v.bodySize = size;
            string colorSource = "-";
            if (TrafficSettings.IsPassengerCar(type))
            {
                v.paintRenderers = bodyPart.GetComponentsInChildren<Renderer>(true).Where(r => fbx != null || r.name != "Trim").ToArray();
                v.paintMaterialIndex = 0;
                Color[] colors = null;
                if (fbx != null && meta != null)
                {
                    colors = MetaPaints(meta, out string paintMaterial);
                    // Boya malzemesini taşıyan renderer'lar ve malzeme yuvası (meta paint_material adıyla)
                    var painted = model.GetComponentsInChildren<Renderer>(true)
                        .Where(r => r.sharedMaterials.Any(m => m != null && m.name == paintMaterial)).ToArray();
                    if (painted.Length == 0) throw new Exception(type + ": boya malzemesi " + paintMaterial + " modelde yok");
                    v.paintRenderers = painted;
                    v.paintMaterialIndex = Array.FindIndex(painted[0].sharedMaterials, m => m != null && m.name == paintMaterial);
                    if (painted.Any(r => Array.FindIndex(r.sharedMaterials, m => m != null && m.name == paintMaterial) != v.paintMaterialIndex))
                        throw new Exception(type + ": boya malzemesi renderer'larda farklı yuvada");
                    colorSource = MetaPath(type) + " paint_variants (" + paintMaterial + ", yuva " + v.paintMaterialIndex + ", " + painted.Length + " renderer)";
                }
                if (colors == null) colorSource = "yer tutucu renkleri";
                v.paintColors = colors ?? PlaceholderCarColors;
            }

            // Pikap balyaları (şef kararı 5): kasada bağlı, koni çarpışmasında kopar
            if (type == TrafficVehicleType.Pickup && fbx != null)
            {
                var cargo = root.AddComponent<HayCargo>();
                cargo.bales = new[] { Find(modelName + "_Hay_01"), Find(modelName + "_Hay_02") };
                Line("  Pikap balyaları: " + string.Join(", ", cargo.bales.Select(x => x.name + " " + V(root.transform.InverseTransformPoint(x.position)))) + ", " + cargo.baleMass + " kg, ömür " + cargo.lifetime + " sn");
            }
            Line("Araç " + type + ": " + (fbx != null ? ModelPath(type) : "yer tutucu kutular") + ", gövde kutusu merkez " + V(center) + " boyut " + V(size)
                + ", ön/arka çıkıntı " + follower.frontOverhang.ToString("F2", Inv) + "/" + follower.rearOverhang.ToString("F2", Inv)
                + " m, tekerlek " + wheels.Length + " × r " + follower.wheelRadius.ToString("F2", Inv) + ", motor " + spec.engineHz + " Hz, korna "
                + spec.hornLow + "/" + spec.hornHigh + " Hz" + (TrafficSettings.IsPassengerCar(type) ? ", renk varyantı " + v.paintColors.Length + " (" + colorSource + ")" : ""));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(type));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // <tip>_meta.json: paint_variants [{name, srgb_hex}] + paint_material. Renkler sRGB; MaterialPropertyBlock.SetColor
    // lineer projede bir kez lineere çevirir (FBX'teki lineer renkle aynı sonuç; ek telafi yok).
    private static Color[] MetaPaints(Dictionary<string, object> meta, out string paintMaterial)
    {
        paintMaterial = meta.TryGetValue("paint_material", out object pm) ? pm as string : null;
        if (paintMaterial == null || !meta.TryGetValue("paint_variants", out object pv) || !(pv is List<object> list)) return null;
        var colors = new List<Color>();
        foreach (var e in list)
            if (e is Dictionary<string, object> d && d.TryGetValue("srgb_hex", out object hex) && ColorUtility.TryParseHtmlString((string)hex, out Color c)) colors.Add(c);
        return colors.Count > 0 ? colors.ToArray() : null;
    }

    public static string[] MetaPaintNames(TrafficVehicleType t)
    {
        if (!File.Exists(MetaPath(t))) return new string[0];
        var meta = Bolum1Coordinates.LoadJson(MetaPath(t));
        if (!meta.TryGetValue("paint_variants", out object pv) || !(pv is List<object> list)) return new string[0];
        return list.OfType<Dictionary<string, object>>().Select(d => d.TryGetValue("name", out object n) ? (string)n : "?").ToArray();
    }

    private static float MetaWidth(Dictionary<string, object> meta)
    {
        if (meta == null) return 0f;
        foreach (var k in new[] { "measurement", "dimensions_m" })
            if (meta.TryGetValue(k, out object o) && o is Dictionary<string, object> d)
                foreach (var w in new[] { "width_m", "width" })
                    if (d.TryGetValue(w, out object v)) return Convert.ToSingle(v, Inv);
        return 0f;
    }

    // Yer tutucu: kök SM_<Tip> (pivot = tekerlek dikdörtgeninin merkezi, yerde), ön +Z. Gövde, cam ve tekerlekler ayrı parçalar.
    private static GameObject BuildPlaceholder(TrafficVehicleType type, Spec sp)
    {
        string n = "SM_" + type;
        var tire = Mat("M_Placeholder_Tire", new Color(0.12f, 0.12f, 0.13f));
        var glass = Mat("M_Placeholder_TrafficGlass", new Color(0.16f, 0.22f, 0.28f), 0.9f);
        var trim = Mat("M_Placeholder_TrafficTrim", new Color(0.2f, 0.2f, 0.22f));
        var paint = Mat("M_Placeholder_Traffic_" + type, PlaceholderBodyColor(type));
        var cargo = Mat("M_Placeholder_TrafficCargo", new Color(0.85f, 0.85f, 0.82f));

        var model = new GameObject(n);
        var body = Empty(n + "_Body", model.transform, Vector3.zero);
        var glassT = Empty(n + "_Glass", model.transform, Vector3.zero);
        float L = sp.length, W = sp.width, H = sp.height;
        float floor = sp.wheelRadius * 0.7f;
        switch (type)
        {
            case TrafficVehicleType.Sedan:
            case TrafficVehicleType.Hatchback:
            {
                float beltline = floor + (H - floor) * 0.5f;
                Box("Lower", body, new Vector3(0f, (floor + beltline) * 0.5f, 0f), new Vector3(W, beltline - floor, L), paint);
                float cabinLen = type == TrafficVehicleType.Sedan ? L * 0.48f : L * 0.6f;
                float cabinZ = type == TrafficVehicleType.Sedan ? -L * 0.04f : -L * 0.12f;
                Box("Cabin", body, new Vector3(0f, (beltline + H) * 0.5f, cabinZ), new Vector3(W * 0.88f, H - beltline, cabinLen), paint);
                Box("Windshield", glassT, new Vector3(0f, (beltline + H) * 0.5f, cabinZ + cabinLen * 0.5f + 0.01f), new Vector3(W * 0.8f, (H - beltline) * 0.8f, 0.02f), glass);
                Box("Trim", body, new Vector3(0f, floor + 0.12f, L * 0.5f + 0.02f), new Vector3(W * 0.95f, 0.15f, 0.04f), trim);
                break;
            }
            case TrafficVehicleType.Pickup:
            {
                float beltline = 1.15f;
                Box("Lower", body, new Vector3(0f, (floor + beltline) * 0.5f, 0f), new Vector3(W, beltline - floor, L), paint);
                float cabLen = L * 0.42f, cabZ = L * 0.5f - 1.2f - cabLen * 0.5f;
                Box("Cabin", body, new Vector3(0f, (beltline + H) * 0.5f, cabZ), new Vector3(W * 0.92f, H - beltline, cabLen), paint);
                Box("Windshield", glassT, new Vector3(0f, (beltline + H) * 0.5f, cabZ + cabLen * 0.5f + 0.01f), new Vector3(W * 0.8f, (H - beltline) * 0.8f, 0.02f), glass);
                break;
            }
            case TrafficVehicleType.PanelVan:
            {
                Box("Shell", body, new Vector3(0f, (floor + H) * 0.5f, 0f), new Vector3(W, H - floor, L), paint);
                Box("Windshield", glassT, new Vector3(0f, H * 0.68f, L * 0.5f + 0.01f), new Vector3(W * 0.82f, H * 0.3f, 0.02f), glass);
                break;
            }
            default: // BoxTruck: kabin + kasa
            {
                float cabLen = 2.3f;
                float cabH = 3.0f;
                float cabZ = L * 0.5f - cabLen * 0.5f;
                Box("Cab", body, new Vector3(0f, (floor + cabH) * 0.5f, cabZ), new Vector3(W, cabH - floor, cabLen), paint);
                float boxLen = L - cabLen - 0.15f;
                Box("CargoBox", body, new Vector3(0f, (floor + 0.3f + H) * 0.5f, -L * 0.5f + boxLen * 0.5f), new Vector3(W, H - floor - 0.3f, boxLen), cargo);
                Box("Windshield", glassT, new Vector3(0f, cabH * 0.72f, L * 0.5f + 0.01f), new Vector3(W * 0.85f, cabH * 0.32f, 0.02f), glass);
                break;
            }
        }
        foreach (var w in sp.wheelNames)
        {
            bool left = w.EndsWith("L");
            float z = AxleZ(w, sp.axles);
            var wt = Empty(n + "_Wheel_" + w, model.transform, new Vector3((left ? -1f : 1f) * sp.track * 0.5f, sp.wheelRadius, z));
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.name = "Geo";
            cyl.transform.SetParent(wt, false);
            cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // silindir ekseni X (aks)
            cyl.transform.localScale = new Vector3(sp.wheelRadius * 2f, 0.12f, sp.wheelRadius * 2f);
            cyl.GetComponent<MeshRenderer>().sharedMaterial = tire;
            Box("Cap", wt, new Vector3(left ? -0.125f : 0.125f, sp.wheelRadius * 0.45f, 0f), new Vector3(0.01f, sp.wheelRadius * 0.35f, sp.wheelRadius * 0.35f), cargo);
        }
        return model;
    }

    private static float AxleZ(string wheel, float[] axles)
    {
        if (wheel.StartsWith("F")) return axles[0];
        if (wheel.StartsWith("R1")) return axles[1];
        if (wheel.StartsWith("R2")) return axles[2];
        return axles[axles.Length - 1]; // RL / RR
    }

    private static Color PlaceholderBodyColor(TrafficVehicleType t)
    {
        switch (t)
        {
            case TrafficVehicleType.Pickup: return new Color(0.45f, 0.12f, 0.1f);
            case TrafficVehicleType.PanelVan: return new Color(0.92f, 0.92f, 0.9f);
            case TrafficVehicleType.BoxTruck: return new Color(0.9f, 0.62f, 0.1f);
            default: return Color.white; // binek: renk varyantı MaterialPropertyBlock ile
        }
    }

    // =====================================================================================
    // Sahne kurulumu (Bolum1SceneBuilder.Build sonunda, ambulans kurulumundan sonra)
    // =====================================================================================

    public static void SetupScene(Dictionary<string, object> markers, GameObject truck, GameObject character)
    {
        Physics.SyncTransforms();
        var markerData = Bolum1Coordinates.Obj(markers, "markers");
        var root = GameObject.Find(Bolum1AmbulanceBuilder.GameplayRootName).transform;
        var settings = Settings;

        // 1) WestBound şeridi (EastBound'u ambulans kurulumu yaptı)
        var westGo = new GameObject(WestLaneName);
        westGo.transform.SetParent(root, false);
        var west = westGo.AddComponent<LanePath>();
        west.points = ((List<object>)Bolum1Coordinates.Obj(markerData, WestLaneName)["polyline"])
            .Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToArray();
        west.width = 4f;
        west.Rebuild();
        var east = GameObject.Find(Bolum1AmbulanceBuilder.LaneName).GetComponent<LanePath>();
        Line("Şerit " + WestLaneName + ": " + west.points.Length + " nokta, " + west.Length.ToString("F1", Inv) + " m; EastBound " + east.Length.ToString("F1", Inv) + " m");

        // 2) Trafik yönetmeni
        var dirGo = new GameObject(DirectorName);
        dirGo.transform.SetParent(root, false);
        var director = dirGo.AddComponent<TrafficDirector>();
        director.settings = settings;
        director.eastBound = east;
        director.westBound = west;
        director.prefabs = Types.Select(t => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(t)).GetComponent<TrafficVehicle>()).ToArray();
        director.mission = GameObject.Find(Bolum1AmbulanceBuilder.MissionName).GetComponent<MissionDirector>();
        Line("Trafik yönetmeni: " + DirectorName + ", ayarlar " + SettingsPath + " (sakin " + settings.calmSeconds + ", dalga " + settings.waveSize + ", aralık " + settings.waveGapSeconds + ")");

        // 3) Koniler (çekici kasasında): her biri için ayrı kontrol objesi (TrafficCone_<ad>); çekici hiyerarşisine betik eklenmez.
        //    Yukarı ekseni ve taban ölçüsü sahnedeki dik duruşlarından.
        var road = GameObject.Find("SM_B1_Road").transform;
        float killY = GameObject.Find("FallRespawnPlane").transform.position.y;
        foreach (string name in Bolum1TowTruck.ConeNames)
        {
            var cone = Bolum1TowTruck.Part(truck, name);
            var ctrl = new GameObject(ConeControllerName(name));
            ctrl.transform.SetParent(root, false);
            ctrl.transform.position = cone.position;
            var c = ctrl.AddComponent<TrafficCone>();
            c.target = cone;
            Vector3 up = Vector3.up;
            c.localUp = (Quaternion.Inverse(cone.rotation) * up).normalized;
            MeasureCone(cone, up, out float baseOffset, out float radius, out float height);
            c.baseOffset = baseOffset;
            c.radius = radius;
            c.height = height;
            c.road = road;
            c.killBelowY = killY;
            var src = ctrl.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 1f;
            c.hitSource = src;
            Line("Koni " + name + ": yukarı ekseni (yerel) " + V(c.localUp) + ", pivot tabandan " + baseOffset.ToString("F3", Inv) + " m, taban yarıçapı "
                + radius.ToString("F3", Inv) + " m, boy " + height.ToString("F3", Inv) + " m, kaybolma y < " + killY.ToString("F1", Inv));
        }

        // 4) Oyuncu ragdoll'u ve düşme/kaldırma
        var player = character.GetComponent<ThirdPersonPlayer>();
        var animator = character.GetComponent<Animator>();
        var kd = character.AddComponent<PlayerKnockdown>();
        kd.player = player;
        kd.animator = animator;
        kd.hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Bolum1AmbulanceBuilder.BuildRagdoll(animator, 70f, out kd.ragdollBodies, out kd.ragdollColliders);
        var ik = character.GetComponent<FootGroundingIK>();
        kd.disableWhileDown = ik != null ? new Behaviour[] { ik } : new Behaviour[0];
        player.knockdown = kd;
        Line("Oyuncu ragdoll'u: " + kd.ragdollBodies.Length + " gövde, " + kd.ragdollColliders.Length + " collider (Patient ile aynı kurulum, 70 kg); kaldırma "
            + kd.reviveSeconds.ToString(Inv) + " sn, menzil " + kd.reviveRange.ToString(Inv) + " m");

        WriteReport();
    }

    // Koninin mesh sınırlarından: pivotun tabandan yüksekliği, taban yarıçapı, boy (dünya yukarısı boyunca, dik duruşta).
    private static void MeasureCone(Transform cone, Vector3 up, out float baseOffset, out float radius, out float height)
    {
        var mf = cone.GetComponent<MeshFilter>();
        var mb = mf.sharedMesh.bounds;
        float minUp = float.MaxValue, maxUp = float.MinValue, maxSide = 0f;
        for (int i = 0; i < 8; i++)
        {
            var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 w = cone.TransformPoint(corner) - cone.position;
            float u = Vector3.Dot(w, up);
            minUp = Mathf.Min(minUp, u);
            maxUp = Mathf.Max(maxUp, u);
            Vector3 side = w - up * u;
            maxSide = Mathf.Max(maxSide, side.magnitude);
        }
        baseOffset = -minUp;
        height = maxUp - minUp;
        radius = maxSide / Mathf.Sqrt(2f); // kare sınırın köşesi → kenar yarısı
    }

    // =====================================================================================
    // Yardımcılar
    // =====================================================================================

    private static float WheelRadius(Transform wheel, float fallback)
    {
        var rs = wheel.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return fallback;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.extents.y > 0.05f ? b.extents.y : fallback;
    }

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

    private static Material Mat(string name, Color color, float smoothness = 0.35f)
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

    // Prefab'a yazılan varsayılan hızlar: ayar asset'inden (yoksa ScriptableObject varsayılanı).
    private static class TrafficSettingsDefaults
    {
        public static TrafficSettings.TypeEntry Entry(TrafficVehicleType t)
        {
            var s = Settings;
            var e = s != null ? s.Entry(t) : null;
            return e ?? new TrafficSettings.TypeEntry { type = t, cruiseSpeed = 12f };
        }
    }
}
