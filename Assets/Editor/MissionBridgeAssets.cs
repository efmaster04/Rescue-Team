using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Görev köprüsü varlıkları (tekrar çalıştırılabilir):
//  - Halat, sedye, koni için ofis prefab'ları (Assets/_Project/Prefabs/Equipment): görseller Bölüm 1 sahnesindeki
//    nesnelerin kopyası (betikler, collider'lar ve fizik çıkarılır), kökte BoxCollider + Rigidbody + CarryableItem
//    (arkadaşın ekipman prefab'larıyla aynı düzen: pivot tabanda).
//  - EquipmentDefinition'lar (Assets/_Project/Data/Equipment) ve Resources/CompanyCatalogAddon (sahnedeki kataloğa
//    çalışma zamanında eklenir; OfficeGarage.unity'ye dokunulmaz).
//  - Mağaza küçük resimleri (Resources/OfficeThumbnails/<id>.png).
//  - recovery işi: "Uçurumda Mahsur Minibüs", saha sahnesi Bölüm 1, zorunlu halat + sedye, önerilen koni.
// Bölüm 1 sahnesi yalnız okunur (kaydedilmez). Batchmode:
// Unity.exe -batchmode -projectPath ... -executeMethod MissionBridgeAssets.BuildBatch -logFile <dosya>
public static class MissionBridgeAssets
{
    public const string FieldScenePath = "Assets/Scenes/Bolum1_Ucurum.unity";
    public const string FieldSceneName = "Bolum1_Ucurum";
    public const string PrefabDir = "Assets/_Project/Prefabs/Equipment";
    public const string GeneratedDir = "Assets/_Project/Prefabs/Equipment/MissionBridgeGenerated";
    public const string DataDir = "Assets/_Project/Data/Equipment";
    public const string ThumbDir = "Assets/_Project/Resources/OfficeThumbnails";
    public const string AddonPath = "Assets/_Project/Resources/" + CatalogAddon.ResourcePath + ".asset";
    public const string RecoveryJobPath = "Assets/_Project/Data/Jobs/recovery.asset";

    // Mağaza değerleri: fiyatlar LootValues'teki ekipman değerleriyle aynı (sedye 300, halat 150, koni 20).
    public sealed class Spec
    {
        public string id, displayName, prefabName;
        public float weightKg;
        public int price;
        public Spec(string id, string displayName, string prefabName, float weightKg, int price)
        { this.id = id; this.displayName = displayName; this.prefabName = prefabName; this.weightKg = weightKg; this.price = price; }
    }

    public static readonly Spec[] Specs =
    {
        new Spec("rope", "Kurtarma halatı", "Equipment_RescueRope", 9f, 150),
        new Spec("stretcher", "Sedye", "Equipment_Stretcher", 14f, 300),
        new Spec("cone", "Trafik konisi", "Equipment_TrafficCone", 2f, 20),
    };

    [MenuItem("Rescue Team/Görev Köprüsü/Ekipman varlıklarını üret")]
    public static void BuildMenu() { Build(); }

    public static void BuildBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("MISSIONBRIDGE ASSETS FAILED: " + e); EditorApplication.Exit(1); }
    }

    public static void Build()
    {

        Directory.CreateDirectory(ThumbDir);
        EditorSceneManager.OpenScene(FieldScenePath, OpenSceneMode.Single);
        var sources = new Dictionary<string, (Transform source, Quaternion rotation)>();
        var coil = Object.FindFirstObjectByType<RopeCoil>();
        if (coil == null) throw new Exception("Bölüm 1'de RopeCoil yok");
        sources["rope"] = (coil.transform, FlattestRotation(coil.transform));
        var stretcher = Object.FindObjectsByType<StretcherController>().OrderBy(s => s.name).FirstOrDefault();
        if (stretcher == null) throw new Exception("Bölüm 1'de StretcherController yok");
        sources["stretcher"] = (stretcher.transform, Quaternion.identity);
        var cone = Object.FindObjectsByType<TrafficCone>().Where(c => c.target != null).OrderBy(c => c.name).FirstOrDefault();
        if (cone == null) throw new Exception("Bölüm 1'de TrafficCone yok");
        // Koni dik dursun: koninin yerel "yukarı" ekseni dünya yukarısına
        var coneUp = cone.target.rotation * cone.localUp.normalized;
        sources["cone"] = (cone.target, Quaternion.FromToRotation(coneUp, Vector3.up) * cone.target.rotation);

        var defs = new List<EquipmentDefinition>();
        foreach (var spec in Specs)
        {
            var (source, rotation) = sources[spec.id];
            var prefab = BuildPrefab(spec, source, rotation, out Vector3 size);
            defs.Add(BuildDefinition(spec, prefab, size));
            Log(spec.id + ": prefab " + AssetDatabase.GetAssetPath(prefab) + ", boyut " + size.ToString("F2"));
        }
        // Bölüm 1 değişiklikleri (kopyalar) kaydedilmeden atılır
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        foreach (var def in defs) RenderThumbnail(def);
        BuildAddon(defs);
        UpdateRecoveryJob();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("MISSIONBRIDGE ASSETS OK");
    }

    static void Log(string s) { Debug.Log("[MissionBridgeAssets] " + s); }

    // Halat kangalı: en yassı duruş (yerde yatar).
    static Quaternion FlattestRotation(Transform t)
    {
        Quaternion best = Quaternion.identity;
        float bestHeight = float.MaxValue;
        foreach (var r in new[] { Quaternion.identity, Quaternion.Euler(90, 0, 0), Quaternion.Euler(0, 0, 90) })
        {
            var probe = Object.Instantiate(t.gameObject);
            probe.transform.SetPositionAndRotation(Vector3.zero, r);
            float h = RendererBounds(probe).size.y;
            Object.DestroyImmediate(probe);
            if (h < bestHeight - 1e-4f) { bestHeight = h; best = r; }
        }
        return best;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * .2f);
        var b = rs[0].bounds;
        foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
        return b;
    }

    // Görseli olmayan her bileşeni söker (birbirine bağlı bileşenler için birkaç tur).
    static void StripToVisual(GameObject root)
    {
        // Önce betikler, sonra eklemler ve fizik, en son geri kalan (RequireComponent bağımlılıkları sırayla çözülür)
        for (int pass = 0; pass < 4; pass++)
            foreach (var c in root.GetComponentsInChildren<Component>(true)
                         .OrderBy(c => c is MonoBehaviour ? 0 : c is Joint ? 1 : c is Rigidbody ? 3 : 2))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer || c is LODGroup) continue;
                Object.DestroyImmediate(c);
            }
    }

    static GameObject BuildPrefab(Spec spec, Transform source, Quaternion rotation, out Vector3 size)
    {
        var visual = Object.Instantiate(source.gameObject);
        visual.name = "Visual";
        visual.SetActive(true);
        StripToVisual(visual);
        visual.transform.SetPositionAndRotation(Vector3.zero, rotation);
        visual.transform.localScale = source.lossyScale;
        foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        PersistAssets(spec, visual);
        var bounds = RendererBounds(visual);
        size = new Vector3(Round2(bounds.size.x), Round2(bounds.size.y), Round2(bounds.size.z));

        var root = new GameObject(spec.prefabName);
        visual.transform.SetParent(root.transform, true);
        // Pivot tabanın ortasında (arkadaşın ekipmanları gibi)
        visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        var box = root.AddComponent<BoxCollider>();
        box.size = bounds.size;
        box.center = new Vector3(0f, bounds.size.y * .5f, 0f);
        var body = root.AddComponent<Rigidbody>();
        body.mass = spec.weightKg;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var item = root.AddComponent<CarryableItem>();
        item.definitionId = spec.id;
        item.uid = "";
        item.durability = 100;
        string path = PrefabDir + "/" + spec.prefabName + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        if (prefab == null) throw new Exception("prefab kaydedilemedi: " + path);
        return prefab;
    }

    static float Round2(float v) { return Mathf.Round(v * 100f) / 100f; }

    // Sahnede üretilmiş (asset olmayan) mesh ve materyaller prefab'da kaybolmasın diye asset'e yazılır.
    static void PersistAssets(Spec spec, GameObject visual)
    {
        int n = 0;
        foreach (var mf in visual.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh))
                mf.sharedMesh = SaveCopy(Object.Instantiate(mf.sharedMesh), spec.id + "_mesh" + (n++) + ".asset");
        foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null && !EditorUtility.IsPersistent(smr.sharedMesh))
                smr.sharedMesh = SaveCopy(Object.Instantiate(smr.sharedMesh), spec.id + "_mesh" + (n++) + ".asset");
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && !EditorUtility.IsPersistent(mats[i]))
                { mats[i] = SaveCopy(new Material(mats[i]), spec.id + "_mat" + (n++) + ".mat"); changed = true; }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static T SaveCopy<T>(T obj, string file) where T : Object
    {
        Directory.CreateDirectory(GeneratedDir);
        string path = GeneratedDir + "/" + file;
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(obj, path);
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    static EquipmentDefinition BuildDefinition(Spec spec, GameObject prefab, Vector3 size)
    {
        string path = DataDir + "/" + spec.id + ".asset";
        var def = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(path);
        if (def == null) { def = ScriptableObject.CreateInstance<EquipmentDefinition>(); AssetDatabase.CreateAsset(def, path); }
        def.id = spec.id;
        def.displayName = spec.displayName;
        def.category = "Field equipment";
        def.weightKg = spec.weightKg;
        def.size = size;
        def.price = spec.price;
        def.portable = true;
        def.visualPrefab = prefab;
        EditorUtility.SetDirty(def);
        return def;
    }

    static void BuildAddon(List<EquipmentDefinition> defs)
    {
        var addon = AssetDatabase.LoadAssetAtPath<CatalogAddon>(AddonPath);
        if (addon == null) { addon = ScriptableObject.CreateInstance<CatalogAddon>(); AssetDatabase.CreateAsset(addon, AddonPath); }
        addon.equipment = defs.ToArray();
        EditorUtility.SetDirty(addon);
    }

    static void UpdateRecoveryJob()
    {
        var job = AssetDatabase.LoadAssetAtPath<JobDefinition>(RecoveryJobPath);
        if (job == null) throw new Exception("recovery işi yok: " + RecoveryJobPath);
        job.title = "Uçurumda Mahsur Minibüs";
        job.client = "İl Afet Koordinasyon Merkezi";
        job.description = "Uçurum kenarında mahsur kalan minibüsteki yolcuları kurtarın, ambulansa yetiştirin ve eşyalarını hırsızlara kaptırmayın.";
        job.category = "Kurtarma";
        job.location = "Uçurum yolu";
        job.specialTerms = "Halat ve sedye olmadan yola çıkılmaz. Koniler şeridi kapatmak için önerilir; yüklenen koni kadar koni sahaya gelir.";
        job.requiredModule = "towing";
        job.requiredEquipment = new[] { "rope", "stretcher" };
        job.recommendedEquipment = new[] { "cone" };
        job.fieldScene = FieldSceneName;
        EditorUtility.SetDirty(job);
    }

    // Mağaza küçük resmi: şeffaf zeminde 512×512 (arkadaşın ModuleEquipmentBuilder.Render düzeniyle aynı ışık ve açı).
    static void RenderThumbnail(EquipmentDefinition def)
    {
        string path = ThumbDir + "/" + def.id + ".png";
        var scene = EditorSceneManager.NewPreviewScene();
        var old = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            var model = Object.Instantiate(def.visualPrefab);
            SceneManager.MoveGameObjectToScene(model, scene);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bounds = RendererBounds(model);
            var camGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var camera = camGo.AddComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0, 0, 0, 0);
            camera.allowHDR = false;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 20;
            camera.transform.position = bounds.center + new Vector3(.95f, .62f, 1.45f).normalized * 3;
            camera.transform.LookAt(bounds.center);
            camera.aspect = 1f;
            float extent = 0;
            foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            {
                var p = camera.transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                extent = Mathf.Max(extent, Mathf.Abs(p.y), Mathf.Abs(p.x));
            }
            camera.orthographicSize = extent * 1.12f;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;
            AddLight(scene, new Vector3(45, -145, 0), 2.1f, new Color(1, .96f, .90f));
            AddLight(scene, new Vector3(25, 40, 0), .45f, new Color(.86f, .92f, 1));
            AddLight(scene, new Vector3(30, 120, 0), .7f, Color.white);
            target = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 4;
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        catch (Exception e) { Debug.LogWarning("[MissionBridgeAssets] küçük resim üretilemedi (" + def.id + "): " + e.Message); return; }
        finally
        {
            RenderTexture.active = old;
            if (target) RenderTexture.ReleaseTemporary(target);
            if (pixels) Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }

    static void AddLight(Scene scene, Vector3 angles, float intensity, Color color)
    {
        var go = new GameObject("PreviewLight");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.rotation = Quaternion.Euler(angles);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
    }
}
