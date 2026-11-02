using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Garajın restore edilmiş görselini okur; CompanySim davranışlarını Bölüm 1'e taşımaz.
public static class Bolum1ServiceVan
{
    public const string GarageScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
    public const string ModulePath = "Assets/_Project/Prefabs/Modules/ServiceModule_towing.prefab";
    public const string AssetFolder = "Assets/Prefabs/Bolum1/ServiceVan";
    public const string VisualName = "ServiceVan_Visual";
    public const string ModuleName = "ServiceVan_TowingModule";
    public const string EquipmentName = "Bolum1_ServiceVanEquipment";
    public const string DoorName = "ServiceVan_RopeLockerDoor";
    public const float CargoFloor = .66f;
    public const float ConeMass = 3f;
    public static readonly string[] ConeNames = {
        "ServiceVan_Cone_01", "ServiceVan_Cone_02", "ServiceVan_Cone_03", "ServiceVan_Cone_04"
    };

    public static Transform Part(GameObject root, string name)
    {
        var part = root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name);
        if (part == null) throw new Exception("Servis minibüsü parçası eksik: " + name);
        return part;
    }

    public static Vector3 Forward(GameObject van) => -van.transform.forward;
    // Açılan kapılar, modül ve ekipman arka tampon ölçüsüne katılmaz.
    public static Vector3 RearEnd(GameObject van) => Part(van, "RearBumperReference").position;

    public static GameObject Build(Dictionary<string, object> markers)
    {
        EnsureFolder();
        var destination = SceneManager.GetActiveScene();
        var van = new GameObject(Bolum1SceneBuilder.ServiceVanName);
        var preview = EditorSceneManager.OpenPreviewScene(GarageScenePath);
        try
        {
            var source = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name == "04_Vehicle_ServiceVan_Original");
            var visual = CopyVisual(source, van.transform);
            visual.name = VisualName;
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            SceneManager.SetActiveScene(destination);
        }
        var art = Part(van, VisualName);
        Bounds body = LocalBounds(art, van.transform);
        var rear = new GameObject("RearBumperReference").transform;
        rear.SetParent(van.transform, false);
        rear.localPosition = new Vector3(0f, body.min.y, body.max.z);
        Debug.Log("Servis minibüsü kapalı görsel bounds (genişlik/yükseklik/uzunluk): " + body.size.ToString("F3")
            + "; eski araç gövdesi (aynalar hariç): (2.500, 3.200, 8.000) m.");

        // ManualModuleSystem.Spawn → CarryableItem.DockAt: sokette sıfır konum/dönüş, ölçek 1.
        // towing.asset.visualPrefab eski büyük kasadır; güncel OfficeGarage.packageVisuals bunu kullanır.
        var modulePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath);
        if (modulePrefab == null) throw new Exception("Garajın takılı vinç paketi eksik: " + ModulePath);
        var mount = Part(van, "CassetteSocket");
        var module = CopyVisual(modulePrefab.transform, mount);
        module.name = ModuleName;
        module.localPosition = Vector3.zero;
        module.localRotation = Quaternion.identity;
        module.localScale = Vector3.one;
        AddBox(module.gameObject, new Vector3(0f, .22f, 0f), new Vector3(.66f, .44f, .46f));
        BuildRoofSignal(van.transform);

        // Kapılar açık kalır; parça ve taşıyıcının kapıya göre garaj transform'u değişmez.
        foreach (string side in new[] { "Left", "Right" })
        {
            var door = Part(van, "RearDoor_" + side);
            float sign = side == "Left" ? 1f : -1f;
            AddBox(door.gameObject, new Vector3(-sign * .5f, 1.56f, 0f), new Vector3(1f, 1.94f, .15f));
            door.localRotation = Quaternion.Euler(0f, sign * 108f, 0f);
        }
        SetupPhysics(van);
        var markerData = Bolum1Coordinates.Obj(markers, "markers");
        var placement = Bolum1Coordinates.Obj(markerData, "TowTruckPlacement");
        var rotation = Bolum1Coordinates.ToUnity(Bolum1Coordinates.QuatWxyz(placement["rotation_quaternion_wxyz"]));
        van.transform.SetPositionAndRotation(Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(placement, "position")), rotation);
        van.transform.localScale = Vector3.one;
        AlignToGround(van, body.min.y);
        BuildEquipment(van);
        return van;
    }

    // Yalnız Transform + MeshFilter + MeshRenderer: kaynak prefab/sahne betikleri hiç instantiate edilmez.
    private static Transform CopyVisual(Transform source, Transform parent)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        var filter = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null)
        {
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var target = copy.gameObject.AddComponent<MeshRenderer>();
            target.sharedMaterials = renderer.sharedMaterials;
            target.enabled = renderer.enabled;
            target.shadowCastingMode = renderer.shadowCastingMode;
            target.receiveShadows = renderer.receiveShadows;
        }
        foreach (Transform child in source) CopyVisual(child, copy);
        copy.gameObject.SetActive(source.gameObject.activeSelf);
        return copy;
    }

    // Mesh.bounds köşeleri kullanılır: okunabilir FBX/import değişikliği gerektirmez.
    public static Bounds LocalBounds(Transform subtree, Transform space)
    {
        Bounds bounds = default;
        bool found = false;
        foreach (var mf in subtree.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = mf.GetComponent<Renderer>();
            if (!mf.sharedMesh || !renderer || !renderer.enabled || !mf.gameObject.activeSelf) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var p = space.InverseTransformPoint(mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
        }
        if (!found) throw new Exception("Servis aracı görsel bounds boş: " + subtree.name);
        return bounds;
    }

    public static bool GroundBelow(Vector3 point, out RaycastHit ground)
    {
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 5f, Vector3.down, 15f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (!hit.collider.GetComponentInParent<Transform>().GetComponentsInParent<Transform>()
                .Any(t => t.name == "SM_B1_Ground" || t.name == "SM_B1_Road")) continue;
            ground = hit;
            return true;
        }
        ground = default;
        return false;
    }

    private static void AlignToGround(GameObject van, float bottom)
    {
        Physics.SyncTransforms();
        if (!GroundBelow(van.transform.position, out var hit)) throw new Exception("Servis minibüsü marker'ında zemin yok");
        van.transform.position += Vector3.up * (hit.point.y - van.transform.position.y - bottom);
        Physics.SyncTransforms();
    }

    public static void SetupPhysics(GameObject van)
    {
        // GarageSystemsBuilder ölçüleri; arka açıklık ve yük alanı erişilebilir kalır.
        AddBox(van, new Vector3(-.97f, 1.60f, .95f), new Vector3(.10f, 2.2f, 3.5f));
        AddBox(van, new Vector3(.97f, 1.60f, .95f), new Vector3(.10f, 2.2f, 3.5f));
        AddBox(van, new Vector3(0f, .58f, .95f), new Vector3(1.94f, .16f, 3.5f));
        AddBox(van, new Vector3(0f, 2.64f, .95f), new Vector3(1.94f, .12f, 3.5f));
        AddBox(van, new Vector3(0f, 1.60f, -.73f), new Vector3(1.94f, 2.2f, .12f));
        AddBox(van, new Vector3(0f, 1.2f, -1.80f), new Vector3(1.90f, 2.1f, 2.35f));
        AddBox(Part(van, "ExteriorModuleCarrier").gameObject, new Vector3(0f, 1.96f, .28f), new Vector3(.88f, .70f, .56f));
    }

    public static Vector3 StretcherLocalPosition(int index) => new Vector3(index == 0 ? -.44f : .44f, CargoFloor + .025f, 1.69f);

    private static void BuildEquipment(GameObject van)
    {
        var equipment = new GameObject(EquipmentName).transform;
        equipment.SetPositionAndRotation(van.transform.position, van.transform.rotation);
        // Bağımsız fizik objeleri; araca ait mesh değiller. Arka raftan elle alınırlar.
        var orange = Material("ConeOrange", new Color(.82f, .30f, .07f));
        var white = Material("ConeWhite", new Color(.9f, .88f, .8f));
        var mesh = ConeMesh();
        Cube(van.transform, "EquipmentShelf", new Vector3(0f, 1.20f, 2.42f), new Vector3(1.80f, .06f, .46f), Material("Shelf", new Color(.16f, .18f, .19f)));
        for (int i = 0; i < ConeNames.Length; i++)
        {
            var cone = new GameObject(ConeNames[i], typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(Rigidbody));
            cone.transform.SetParent(equipment, false);
            cone.transform.localPosition = new Vector3(-.675f + i * .45f, 1.235f, 2.42f);
            cone.GetComponent<MeshFilter>().sharedMesh = mesh;
            cone.GetComponent<MeshRenderer>().sharedMaterials = new[] { orange, white };
            var collider = cone.GetComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = true;
            var body = cone.GetComponent<Rigidbody>(); body.mass = ConeMass; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        var metal = Material("Locker", new Color(.29f, .33f, .34f));
        // Yük açıklığının sol yanında küçük dolap; tabandan erişilen, arkaya bakan kapak.
        var center = new Vector3(1.12f, 1.12f, 2.68f);
        Cube(van.transform, "RopeLockerBack", center + new Vector3(0f, 0f, -.16f), new Vector3(.60f, .65f, .04f), metal);
        foreach (float x in new[] { -.30f, .30f })
            Cube(van.transform, "RopeLockerSide", center + new Vector3(x, 0f, -.04f), new Vector3(.04f, .65f, .28f), metal);
        foreach (float y in new[] { -.325f, .325f })
            Cube(van.transform, "RopeLockerShelf", center + new Vector3(0f, y, -.04f), new Vector3(.64f, .04f, .28f), metal);
        var door = new GameObject(DoorName).transform;
        door.SetParent(van.transform, false);
        door.localPosition = center + new Vector3(-.30f, 0f, .12f);
        Cube(door, "RopeLockerLid", new Vector3(.30f, 0f, 0f), new Vector3(.60f, .65f, .04f), metal);
    }

    private static void BuildRoofSignal(Transform van)
    {
        // ServiceVehicle.CreateAccessory("towing") ile aynı göreli ölçüler.
        var socket = new GameObject("ServiceVan_RoofSignal").transform;
        socket.SetParent(van, false); socket.localPosition = new Vector3(0f, 2.79f, .12f);
        var dark = Material("SignalHousing", new Color(.07f, .09f, .11f));
        var amber = Material("SignalAmber", new Color(1f, .57f, .08f));
        foreach (float x in new[] { -.42f, .42f }) Cube(socket, "RoofMountFoot", new Vector3(x, -.0675f, 0f), new Vector3(.10f, .075f, .20f), dark);
        Cube(socket, "LowProfileHousing", Vector3.zero, new Vector3(1.05f, .07f, .24f), dark);
        foreach (float x in new[] { -.29f, .29f }) Cube(socket, "SignalLens", new Vector3(x, .06f, 0f), new Vector3(.43f, .09f, .20f), amber);
    }

    private static void AddBox(GameObject owner, Vector3 center, Vector3 size)
    {
        var box = owner.AddComponent<BoxCollider>(); box.center = center; box.size = size;
    }

    private static void Cube(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = center; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Bolum1")) AssetDatabase.CreateFolder("Assets/Prefabs", "Bolum1");
        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/Prefabs/Bolum1", "ServiceVan");
    }

    private static Material Material(string name, Color color)
    {
        string path = AssetFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .35f);
        EditorUtility.SetDirty(material); return material;
    }

    private static Mesh ConeMesh()
    {
        string path = AssetFolder + "/TrafficCone.asset";
        var stored = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (stored != null) return stored;
        var vertices = new List<Vector3>(); var orange = new List<int>(); var white = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> triangles)
        {
            int n = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
            triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        // 40 cm kare taban + 70 cm yüksek gövde; kapalı ve dışa dönük yüzler.
        Vector3[] bottom = { new Vector3(-.2f, 0f, -.2f), new Vector3(.2f, 0f, -.2f), new Vector3(.2f, 0f, .2f), new Vector3(-.2f, 0f, .2f) };
        Quad(bottom[0], bottom[1], bottom[2], bottom[3], orange);
        Quad(bottom[3] + Vector3.up * .04f, bottom[2] + Vector3.up * .04f, bottom[1] + Vector3.up * .04f, bottom[0] + Vector3.up * .04f, orange);
        for (int i = 0; i < 4; i++) Quad(bottom[(i + 1) % 4], bottom[i], bottom[i] + Vector3.up * .04f, bottom[(i + 1) % 4] + Vector3.up * .04f, orange);
        float[] heights = { .04f, .30f, .43f, .70f };
        for (int ring = 0; ring < heights.Length - 1; ring++)
            for (int i = 0; i < 12; i++)
            {
                Vector3 Point(int side, float y) { float a = side * Mathf.PI / 6f, r = Mathf.Lerp(.17f, .025f, (y - .04f) / .66f); return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }
                Quad(Point(i + 1, heights[ring]), Point(i, heights[ring]), Point(i, heights[ring + 1]), Point(i + 1, heights[ring + 1]), ring == 1 ? white : orange);
            }
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6f, b = (i + 1) * Mathf.PI / 6f;
            Quad(new Vector3(0f, .7f, 0f), new Vector3(Mathf.Cos(b) * .025f, .7f, Mathf.Sin(b) * .025f), new Vector3(Mathf.Cos(a) * .025f, .7f, Mathf.Sin(a) * .025f), new Vector3(0f, .7f, 0f), orange);
        }
        var mesh = new Mesh { name = "ServiceVan_TrafficCone", subMeshCount = 2 };
        mesh.SetVertices(vertices); mesh.SetTriangles(orange, 0); mesh.SetTriangles(white, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
}
