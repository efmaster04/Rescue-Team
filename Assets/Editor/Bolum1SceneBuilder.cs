using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bölüm 1 (uçurum) sahnesini sıfırdan kurar: mekân modülleri, gevşek kayalar ve eşyalar, minibüs, işaretler, test karakteri, ışık.
// Kaynak veriler: markers.json ve minivan_meta.json (Blender koordinatında; dönüşüm Bolum1Coordinates'ta).
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod Bolum1SceneBuilder.BuildBatch -quit
// GUI editörde NewScene + kaydetme bir kez çökme yaptı; bu yüzden sahne tek seferde kurulup kaydedilir, editör kapalıyken çalıştırılması önerilir.
public static class Bolum1SceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Bolum1_Ucurum.unity";
    public const string EnvironmentFolder = "Assets/Models/Environment/Bolum1/";
    public const string MinivanPath = "Assets/Models/Vehicles/Minivan/SM_Minivan.fbx";
    public const string TowTruckPath = "Assets/Models/Vehicles/TowTruck/SM_TowTruck.fbx";
    public const string TowTruckName = "SM_TowTruck";
    public const string CharacterPath = "Assets/Models/Characters/RescueCrew/SK_RescueCrew_Orange.fbx";
    public const string IdleControllerPath = "Assets/Animations/RescueCrew/Controllers/AC_RescueCrew_Idle.controller";

    // Statik modüller (pivot dünya orijininde).
    public static readonly string[] Modules = { "SM_B1_Ground", "SM_B1_Road", "SM_B1_RoadMarkings", "SM_B1_Guardrail", "SM_B1_Vegetation", "SM_B1_Backdrop" };
    // Mesh Collider alanlar: zemin (yamaç ve patika dahil), yol, korkuluk (kullanıcı kararı). Arka plan collider almaz.
    // Yol çizgileri ve bitki örtüsü listede yok; en sade seçenek olarak collider verilmedi (açık soru olarak raporlandı).
    public static readonly string[] ColliderModules = { "SM_B1_Ground", "SM_B1_Road", "SM_B1_Guardrail" };
    public const string NoColliderModule = "SM_B1_Backdrop";

    // Sahnedeki boş işaret objeleri (markers.json → markers).
    public static readonly string[] MarkerNames = { "PlayerSpawn", "RecoveryTruckSlot", "TrafficSpawn_West", "TrafficSpawn_East", "TrafficDespawn_West", "TrafficDespawn_East", "LedgeZone", "FallRespawnPlane" };

    public const string MinivanName = "SM_Minivan";
    public const string CharacterName = "TestCharacter_RescueCrew";
    public const float MinivanMass = 2200f;

    // Minibüs gövdesi iki kutuya bölünür: bel çizgisinin altı (şasi + kapılar) ve üstü (kabin). Değerler metre, minibüs kök uzayında.
    private const float BodySplitHeight = 1.1f;
    private const float BodyHalfWidth = 0.95f;   // minivan_meta measurement.width_m / 2 (aynalar hariç)
    private const float BodyHalfLength = 2.5f;   // measurement.length_m / 2 (çeki halkası hariç)
    private const float RoofHeight = 2.0f;       // measurement.ground_to_roof_m (bagaj rafı hariç)

    [MenuItem("Rescue Team/Bölüm 1/Sahneyi Kur")]
    public static void BuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Bölüm 1", ScenePath + " yeniden kurulacak (varsa üzerine yazılır). Devam?", "Kur", "Vazgeç"))
            return;
        Build();
    }

    public static void BuildBatch()
    {
        try
        {
            Build();
            Debug.Log("BOLUM1 BUILD OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("BOLUM1 BUILD FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static void Build(bool updateBuildSettings = true)
    {
        // Ambulans döngüsü asset ve prefab'ları (yer tutucular dahil) sahneden önce kurulur.
        Bolum1AmbulanceBuilder.BuildAssets();
        // Trafik ayarı ve araç prefab'ları (yer tutucular dahil).
        Bolum1TrafficBuilder.BuildAssets();
        var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        SetupLighting();

        // 1) Statik modüller: orijinde, kendi eksen dönüşümüyle.
        var environment = new GameObject("Bolum1_Environment").transform;
        foreach (string module in Modules)
        {
            var go = InstantiateFbx(EnvironmentFolder + module + ".fbx", environment);
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
            if (Array.IndexOf(ColliderModules, module) < 0)
                continue;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false; // statik, içbükey kalabilir
            }
        }

        // 2) Gevşek kayalar ve eşyalar: markers.json → placements. Şimdilik Rigidbody yok, ilkel collider var.
        var rocks = new GameObject("Bolum1_LooseRocks").transform;
        var props = new GameObject("Bolum1_Props").transform;
        foreach (var kv in Bolum1Coordinates.Obj(markers, "placements"))
        {
            if (kv.Key == "MinivanPlacement" || kv.Key == "TowTruckPlacement")
                continue;
            var parent = kv.Key.Contains("LooseRock") ? rocks : props;
            var go = InstantiateFbx(EnvironmentFolder + kv.Key + ".fbx", parent);
            Vector3 pos = Bolum1Coordinates.Vec3(kv.Value, "position");
            Vector3 euler = Bolum1Coordinates.Vec3(kv.Value, "rotation_euler");
            Bolum1Coordinates.Place(go.transform, pos, Bolum1Coordinates.BlenderEulerXYZ(euler), go.transform.rotation);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<BoxCollider>(); // MeshFilter'lı objede kutu, mesh sınırlarına kendiliğinden oturur
        }

        // 3) Minibüs: MinivanPlacement pozunda, Rigidbody'li.
        var minivanPlacement = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "MinivanPlacement");
        var minivan = InstantiateFbx(MinivanPath, null);
        minivan.name = MinivanName;
        Bolum1Coordinates.Place(minivan.transform,
            Bolum1Coordinates.Vec3(minivanPlacement, "position"),
            Bolum1Coordinates.QuatWxyz(minivanPlacement["rotation_quaternion_wxyz"]),
            minivan.transform.rotation);
        SetupMinivanPhysics(minivan);

        var truckPlacement = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "TowTruckPlacement");
        var truck = InstantiateFbx(TowTruckPath, null);
        truck.name = TowTruckName;
        Bolum1Coordinates.Place(truck.transform,
            Bolum1Coordinates.Vec3(truckPlacement, "position"),
            Bolum1Coordinates.QuatWxyz(truckPlacement["rotation_quaternion_wxyz"]),
            truck.transform.rotation);
        Bolum1TowTruck.SetupPhysics(truck);

        // 4) İşaretler: boş objeler.
        var markerRoot = new GameObject("Bolum1_Markers").transform;
        var markerData = Bolum1Coordinates.Obj(markers, "markers");
        foreach (string name in MarkerNames)
        {
            var m = new GameObject(name).transform;
            m.SetParent(markerRoot, false);
            m.position = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(markerData[name], "position"));
        }

        // 5) Test karakteri (oynanmaz): patika üstünde, minibüsün yanında, Idle + ayak IK'sı.
        PlaceCharacter(markers, minivan.transform);

        // 6) Ambulans döngüsü ve hasta taşıma: şerit, durak, hastalar, sedyeler, oyuncu kontrolcüsü, bot, görev, HUD.
        Bolum1AmbulanceBuilder.SetupScene(markers, minivan, truck, GameObject.Find(CharacterName));

        // 7) Trafik: WestBound şeridi, trafik yönetmeni, koniler, oyuncu ragdoll'u (düşme/kaldırma).
        Bolum1TrafficBuilder.SetupScene(markers, truck, GameObject.Find(CharacterName));

        // 8) Prosedürel animasyonlar (oyuncu, bot), bot ragdoll'u, oyuncu yeniden doğma.
        Bolum1AnimBuilder.SetupScene(markers, GameObject.Find(CharacterName));

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new Exception("Sahne kaydedilemedi: " + ScenePath);
        // Runtime R ile yeniden yükleme, build'de yalnız etkin sahneler için çalışır.
        var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        var entry = buildScenes.Find(s => s.path == ScenePath);
        if (entry == null) buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        else entry.enabled = true;
        if (updateBuildSettings) EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    private static GameObject InstantiateFbx(string path, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new Exception("FBX bulunamadı: " + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (parent != null)
            go.transform.SetParent(parent, true);
        return go;
    }

    private static void SetupLighting()
    {
        // Gün batımı: alçak açılı, sıcak güneş. Vadiden (Blender -Y, Unity +Z) uçurum yüzüne doğru vurur.
        var light = GameObject.Find("Directional Light").GetComponent<Light>();
        light.name = "Sun";
        light.transform.rotation = Quaternion.Euler(12f, 200f, 0f);
        light.color = new Color(1f, 0.72f, 0.48f);
        light.intensity = 1.6f;
        light.shadows = LightShadows.Soft;

        // Gökyüzü: skybox materyali yok, düz gün batımı rengi; ortam ışığı aynı tonlardan (sis/post-process yok).
        var skyColor = new Color(0.96f, 0.69f, 0.52f);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.86f, 0.66f, 0.58f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.50f, 0.50f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.24f, 0.24f);
        RenderSettings.sun = light;

        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = skyColor;
        cam.farClipPlane = 2000f;
        // Varsayılan bakış: vadiden minibüse ve patikaya.
        cam.transform.position = Bolum1Coordinates.ToUnity(new Vector3(4f, -45f, 2f));
        cam.transform.LookAt(Bolum1Coordinates.ToUnity(new Vector3(2f, -12f, -6f)));
    }

    private static void SetupMinivanPhysics(GameObject minivan)
    {
        var rb = minivan.AddComponent<Rigidbody>();
        rb.mass = MinivanMass;

        // Halat bağlanana kadar minibüs yerinde durur (kinematik); halat sistemi ReleaseHold() ile fiziği açar.
        var hold = minivan.AddComponent<VehicleHold>();
        var holdSo = new SerializedObject(hold);
        holdSo.FindProperty("holdOnStart").boolValue = true;
        holdSo.ApplyModifiedPropertiesWithoutUndo();

        // Gövde: kök uzayında iki kutu. Kök uzayı = Blender minibüs uzayı (X işareti ters, simetrik olduğu için fark etmez).
        var body = minivan.transform.Find("SM_Minivan_Body");
        var verts = body.GetComponent<MeshFilter>().sharedMesh.vertices;
        Bounds lower = default, upper = default;
        bool hasLower = false, hasUpper = false;
        foreach (var v in verts)
        {
            // Aynalar, çeki halkası ve bagaj rafı kutuya girmesin diye ölçü sınırlarıyla kırpılır.
            var p = new Vector3(Mathf.Clamp(v.x, -BodyHalfWidth, BodyHalfWidth), Mathf.Clamp(v.y, -BodyHalfLength, BodyHalfLength), Mathf.Min(v.z, RoofHeight));
            if (p.z <= BodySplitHeight)
            {
                if (!hasLower) { lower = new Bounds(p, Vector3.zero); hasLower = true; } else lower.Encapsulate(p);
            }
            else
            {
                if (!hasUpper) { upper = new Bounds(p, Vector3.zero); hasUpper = true; } else upper.Encapsulate(p);
            }
        }
        // Alt kutu bölme yüksekliğine kadar, üst kutu bölme yüksekliğinden başlar (arada boşluk kalmasın).
        lower.SetMinMax(lower.min, new Vector3(lower.max.x, lower.max.y, BodySplitHeight));
        upper.SetMinMax(new Vector3(upper.min.x, upper.min.y, BodySplitHeight), upper.max);
        AddBox(minivan, lower); // alt gövde (şasi, kapılar)
        AddBox(minivan, upper); // kabin

        // Tekerlekler: her tekerlek objesine küre (yarıçap = mesh yarıçapı, 0.375 m).
        foreach (string wheel in new[] { "SM_Minivan_Wheel_FL", "SM_Minivan_Wheel_FR", "SM_Minivan_Wheel_RL", "SM_Minivan_Wheel_RR" })
        {
            var w = minivan.transform.Find(wheel);
            var sc = w.gameObject.AddComponent<SphereCollider>();
            var mb = w.GetComponent<MeshFilter>().sharedMesh.bounds;
            sc.center = mb.center;
            sc.radius = Mathf.Max(mb.extents.y, mb.extents.z);
        }
    }

    private static void AddBox(GameObject owner, Bounds localBounds)
    {
        // Kutular kökün kendisine eklenir (center/size kök uzayında); bileşik collider tek Rigidbody'ye bağlı olur.
        var bc = owner.AddComponent<BoxCollider>();
        bc.center = localBounds.center;
        bc.size = localBounds.size;
    }

    private static void PlaceCharacter(Dictionary<string, object> markers, Transform minivan)
    {
        // Patika orta çizgisinden minibüse yakın bir nokta (markers.json → geometry_probes.path), x ≈ 1.5 m (Blender).
        var path = (List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["path"];
        Vector3 best = Vector3.zero;
        float bestDist = float.MaxValue;
        foreach (var p in path)
        {
            Vector3 b = Bolum1Coordinates.Vec3(p);
            float d = Mathf.Abs(b.x - 1.5f);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        Vector3 pos = Bolum1Coordinates.ToUnity(best);
        // Zemin yüksekliğini raycast ile bul (collider'lar yeni eklendi, fizik sahnesini eşitle).
        Physics.SyncTransforms();
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f) && !hit.transform.IsChildOf(minivan))
            pos.y = hit.point.y;

        var character = InstantiateFbx(CharacterPath, null);
        character.name = CharacterName;
        character.transform.position = pos;
        Vector3 toVan = minivan.position - pos;
        toVan.y = 0f;
        character.transform.rotation = Quaternion.LookRotation(toVan.normalized, Vector3.up);

        var animator = character.GetComponent<Animator>();
        if (animator == null)
            animator = character.AddComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(IdleControllerPath);
        animator.applyRootMotion = false;
        character.AddComponent<FootGroundingIK>();
    }
}
