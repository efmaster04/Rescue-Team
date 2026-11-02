using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bölüm 1 kurtarma aracı: takım arkadaşının servis minibüsü + arka kapı taşıyıcısına takılı çekici modülü.
// Görsel, OfficeGarage sahnesindeki restore edilmiş araçtan (04_Vehicle_ServiceVan_Original) yalnız Transform +
// MeshFilter + MeshRenderer olarak kopyalanır; CompanySim çalışma zamanı bileşenleri (ServiceVehicle, VehicleDoor,
// CarryableItem…) Bölüm 1'e taşınmaz. Çekici modülü garajın takılı paketidir (ServiceModule_towing.prefab,
// ManualModuleSystem.mount = CassetteSocket, sokette sıfır konum/dönüş); towing.asset'in visualPrefab'ı (Module_Towing)
// güncel garajda artık kullanılmıyor. Tavandaki uyarı lambası ServiceVehicle.CreateAccessory("towing") ölçüleridir.
// Araç kök uzayı = garaj araç uzayı: +Y yukarı, +Z arka (kabin −Z'de), +X sol. Yük zemini y ≈ 0.66, iç duvarlar x ±0.90,
// iç tavan 2.49, bölme z −0.68, arka açıklık z ≈ 2.6; arka kapı menteşeleri (±1.0, 0, 2.73), açık açı ±108°.
public static class Bolum1ServiceVan
{
    public const string GarageScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
    public const string GarageVehicleName = "04_Vehicle_ServiceVan_Original";
    public const string ModulePath = "Assets/_Project/Prefabs/Modules/ServiceModule_towing.prefab";
    public const string MountName = "CassetteSocket";
    public const string CarrierName = "ExteriorModuleCarrier";
    public const string AssetFolder = "Assets/Prefabs/Bolum1/ServiceVan";
    public const string VisualName = "ServiceVan_Visual";
    public const string ModuleName = "ServiceVan_TowingModule";
    public const string RoofSignalName = "ServiceVan_RoofSignal";
    public const string RearReferenceName = "RearBumperReference";
    public const string EquipmentName = "Bolum1_ServiceVanEquipment";
    public const string DoorName = "ServiceVan_RopeLockerDoor";
    public const string LockerBodyName = "ServiceVan_RopeLockerBody";
    public const float CargoFloor = .66f;
    public const float DoorOpenAngle = 108f;
    public const float ConeMass = 3f;
    public const float ConeBase = .32f, ConeHeight = .55f;
    public static readonly string[] ConeNames = {
        "ServiceVan_Cone_01", "ServiceVan_Cone_02", "ServiceVan_Cone_03", "ServiceVan_Cone_04"
    };
    // Sedyeler yük zemininde yan yana, arka sapları açıklığa yakın (kutu 0.63 × 2.1 m; arka uç z ≈ 2.6).
    public static Vector3 StretcherLocalPosition(int index) => new Vector3(index == 0 ? .40f : -.40f, CargoFloor + .025f, 1.55f);
    // Koniler sağ iç duvarda iki raf (alt 1.10, üst 1.80), her rafta 2 koni; en arkadakiler açıklıktan alınır.
    public const float ConeSideX = -.68f;
    public static readonly float[] ConeShelfY = { 1.10f, 1.80f };
    public static readonly float[] ConeShelfZ = { 1.95f, 2.35f };
    public static Vector3 ConeLocalPosition(int index) => new Vector3(ConeSideX, ConeShelfY[index / 2], ConeShelfZ[index % 2]);
    // Halat dolabı sol iç duvarda, arka açıklığın hemen içinde; kapak arkaya (+Z) bakar, menteşe dış kenarda (x +0.87).
    public static readonly Vector3 LockerCenter = new Vector3(.62f, 1.35f, 2.30f);
    public static readonly Vector3 LockerSize = new Vector3(.50f, .50f, .30f);
    public const float LockerWall = .03f;

    public static Transform Part(GameObject root, string name)
    {
        var part = root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name);
        if (part == null) throw new Exception("Servis minibüsü parçası eksik: " + name);
        return part;
    }

    public static Vector3 Forward(GameObject van) => -van.transform.forward;
    // Arka tampon ucu (kapalı gövde ölçüsü): açık kapılar, modül ve ekipman sayılmaz.
    public static Vector3 RearEnd(GameObject van) => Part(van, RearReferenceName).position;
    public static float BodyBottomLocalY(GameObject van) => Part(van, RearReferenceName).localPosition.y;

    public static GameObject Build(Dictionary<string, object> markers)
    {
        EnsureFolder();
        var van = new GameObject(Bolum1SceneBuilder.ServiceVanName);
        CopyGarageVisual(van.transform);

        var art = Part(van, VisualName);
        Bounds body = LocalBounds(art, van.transform);
        var rear = new GameObject(RearReferenceName).transform;
        rear.SetParent(van.transform, false);
        rear.localPosition = new Vector3(0f, body.min.y, body.max.z);
        Debug.Log("Servis minibüsü kapalı görsel bounds (genişlik × yükseklik × uzunluk): " + body.size.ToString("F3")
            + ", min " + body.min.ToString("F3") + ", max " + body.max.ToString("F3") + "; eski çekici gövdesi (aynalar hariç): 2.50 × 3.20 × 8.00 m.");

        MountTowingModule(van);
        BuildRoofSignal(van.transform);
        OpenRearDoors(van);
        SetupPhysics(van);

        // Eski çekicinin marker'ı ve yönü: çekicinin ileri yönü kök-yerel −Y (Blender), minibüsünki kök-yerel −Z;
        // marker dönüşü doğrudan uygulanınca minibüs ters bakar, 180° yaw eklenir.
        var placement = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "TowTruckPlacement");
        var rotation = Bolum1Coordinates.ToUnity(Bolum1Coordinates.QuatWxyz(placement["rotation_quaternion_wxyz"])) * Quaternion.Euler(0f, 180f, 0f);
        van.transform.SetPositionAndRotation(Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(placement, "position")), rotation);
        van.transform.localScale = Vector3.one;
        AlignToGround(van, body);
        BuildEquipment(van);
        Debug.Log("Servis minibüsü: konum " + van.transform.position.ToString("F2") + ", ileri (dünya) " + Forward(van).ToString("F2")
            + ", Euler " + van.transform.eulerAngles.ToString("F1") + ", arka uç " + RearEnd(van).ToString("F2"));
        return van;
    }

    // Garaj sahnesini önizleme sahnesi olarak açar, aracın görselini kopyalar, kapatır.
    private static void CopyGarageVisual(Transform parent)
    {
        var destination = SceneManager.GetActiveScene();
        var preview = EditorSceneManager.OpenPreviewScene(GarageScenePath);
        try
        {
            var source = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .SingleOrDefault(t => t.name == GarageVehicleName);
            if (source == null) throw new Exception("Garaj sahnesinde araç yok: " + GarageVehicleName);
            var visual = CopyVisual(source, parent, HiddenLodRenderers(source));
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
    }

    // Garajın takılı vinç paketi: ManualModuleSystem.Spawn → CarryableItem.DockAt(mount): sokette sıfır konum/dönüş, ölçek 1.
    private static void MountTowingModule(GameObject van)
    {
        var modulePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath);
        if (modulePrefab == null) throw new Exception("Garajın takılı vinç paketi eksik: " + ModulePath);
        var mount = Part(van, MountName);
        var module = CopyVisual(modulePrefab.transform, mount, HiddenLodRenderers(modulePrefab.transform));
        module.name = ModuleName;
        module.localPosition = Vector3.zero;
        module.localRotation = Quaternion.identity;
        module.localScale = Vector3.one;
        var source = modulePrefab.GetComponent<BoxCollider>();
        if (source != null) AddBox(module.gameObject, source.center, source.size);
        else AddBox(module.gameObject, new Vector3(0f, .22f, 0f), new Vector3(.66f, .44f, .46f));
    }

    // Kapılar açık kalır (yük bölümü erişilebilir); modül taşıyıcısı sol kapının dış yüzünde kapıyla birlikte döner.
    private static void OpenRearDoors(GameObject van)
    {
        foreach (string side in new[] { "Left", "Right" })
        {
            var door = Part(van, "RearDoor_" + side);
            float sign = side == "Left" ? 1f : -1f;
            AddBox(door.gameObject, new Vector3(-sign * .5f, 1.56f, 0f), new Vector3(1f, 1.94f, .15f));
            door.localRotation = Quaternion.Euler(0f, sign * DoorOpenAngle, 0f);
        }
    }

    // LODGroup'larda LOD0 dışındaki renderer'lar kopyalanmaz (aksi hâlde üç seviye üst üste görünür).
    private static HashSet<Renderer> HiddenLodRenderers(Transform root)
    {
        var hidden = new HashSet<Renderer>();
        foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = group.GetLODs();
            for (int i = 1; i < lods.Length; i++)
                foreach (var r in lods[i].renderers) if (r != null) hidden.Add(r);
        }
        return hidden;
    }

    // Yalnız Transform + MeshFilter + MeshRenderer: kaynak prefab/sahne betikleri hiç instantiate edilmez.
    // UI (RectTransform) dalları ve gizli LOD seviyeleri atlanır. Mesh ve materyaller asset referansıdır (sahneye gömülü değil).
    private static Transform CopyVisual(Transform source, Transform parent, HashSet<Renderer> hidden)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        var filter = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && !hidden.Contains(renderer))
        {
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var target = copy.gameObject.AddComponent<MeshRenderer>();
            target.sharedMaterials = renderer.sharedMaterials;
            target.enabled = renderer.enabled;
            target.shadowCastingMode = renderer.shadowCastingMode;
            target.receiveShadows = renderer.receiveShadows;
        }
        foreach (Transform child in source)
        {
            if (child is RectTransform) continue;
            var childRenderer = child.GetComponent<Renderer>();
            if (childRenderer != null && hidden.Contains(childRenderer) && child.childCount == 0) continue;
            CopyVisual(child, copy, hidden);
        }
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
            if (!mf.sharedMesh || !renderer || !renderer.enabled || !mf.gameObject.activeInHierarchy) continue;
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
            if (!hit.collider.GetComponentsInParent<Transform>(true).Any(t => t.name == "SM_B1_Ground" || t.name == "SM_B1_Road")) continue;
            ground = hit;
            return true;
        }
        ground = default;
        return false;
    }

    // Tekerlek izdüşümleri (kök-yerel): ön/arka aks ve sol/sağ taraf, gövde kutusunun biraz içinde; y = gövde tabanı.
    public static Vector3[] WheelPointsLocal(Bounds body)
    {
        float x = body.extents.x * .72f, zf = body.min.z + .95f, zr = body.max.z - .95f;
        return new[] { new Vector3(x, body.min.y, zf), new Vector3(-x, body.min.y, zf), new Vector3(x, body.min.y, zr), new Vector3(-x, body.min.y, zr) };
    }

    // Dört tekerlek noktasındaki zemin düzlemine oturtur: yaw marker'dan, eğim (pitch/roll) banketten; gövde tabanı zeminde.
    private static void AlignToGround(GameObject van, Bounds body)
    {
        Physics.SyncTransforms();
        var local = WheelPointsLocal(body);
        var ground = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            if (!GroundBelow(van.transform.TransformPoint(local[i]), out var hit)) throw new Exception("Servis minibüsü tekerlek noktasında zemin yok: " + i);
            ground[i] = hit.point;
        }
        Vector3 frontMid = (ground[0] + ground[1]) * .5f, rearMid = (ground[2] + ground[3]) * .5f;
        Vector3 leftMid = (ground[0] + ground[2]) * .5f, rightMid = (ground[1] + ground[3]) * .5f;
        Vector3 rearDir = (rearMid - frontMid).normalized;
        Vector3 up = Vector3.Cross(rearDir, leftMid - rightMid).normalized; // +Z arka, +X sol → yukarı = Z × X
        if (up.y < 0f) up = -up;
        van.transform.rotation = Quaternion.LookRotation(rearDir, up);
        Physics.SyncTransforms();
        float lift = 0f;
        for (int i = 0; i < 4; i++) lift += ground[i].y - van.transform.TransformPoint(local[i]).y;
        van.transform.position += Vector3.up * (lift / 4f);
        Physics.SyncTransforms();
        Debug.Log("Servis minibüsü zemine oturdu: tekerlek noktaları " + string.Join(", ", ground.Select(g => g.ToString("F2"))) + ", yukarı " + up.ToString("F3"));
    }

    public static void SetupPhysics(GameObject van)
    {
        // Yük bölümü: iki yan duvar, zemin, tavan, bölme (iç astar ölçüleri); kabin tek kutu. Park hâlindeki kökte Rigidbody yok.
        AddBox(van, new Vector3(-.97f, 1.60f, .95f), new Vector3(.10f, 2.2f, 3.5f));
        AddBox(van, new Vector3(.97f, 1.60f, .95f), new Vector3(.10f, 2.2f, 3.5f));
        AddBox(van, new Vector3(0f, CargoFloor - .08f, .95f), new Vector3(1.94f, .16f, 3.5f));
        AddBox(van, new Vector3(0f, 2.58f, .95f), new Vector3(1.94f, .16f, 3.5f));
        AddBox(van, new Vector3(0f, 1.60f, -.73f), new Vector3(1.94f, 2.2f, .12f));
        AddBox(van, new Vector3(0f, 1.2f, -1.80f), new Vector3(1.90f, 2.1f, 2.35f));
        // Modül taşıyıcısı (kapının dış yüzü): montaj plakası, raylar ve modül bölgesi.
        AddBox(Part(van, CarrierName).gameObject, new Vector3(0f, 1.96f, .28f), new Vector3(.88f, .70f, .56f));
    }

    // Ekipman: sedyeler (Bolum1AmbulanceBuilder.PlaceStretchers), koniler (burada, bağımsız fizik objeleri), halat dolabı
    // (kutu + menteşeli kapak; TowTruckLocker ve RopeCoil'i Bolum1RescueBuilder.SetupRopeCoil ekler).
    private static void BuildEquipment(GameObject van)
    {
        var equipment = new GameObject(EquipmentName).transform;
        equipment.SetPositionAndRotation(van.transform.position, van.transform.rotation);
        var shelfMat = Material("Shelf", new Color(.16f, .18f, .19f));
        var orange = Material("ConeOrange", new Color(.82f, .30f, .07f));
        var white = Material("ConeWhite", new Color(.9f, .88f, .8f));
        var mesh = ConeMesh();
        // Koni rafları: sağ iç duvarda, iki seviye.
        for (int level = 0; level < ConeShelfY.Length; level++)
            Cube(van.transform, "ConeShelf_" + (level == 0 ? "Lower" : "Upper"), new Vector3(ConeSideX, ConeShelfY[level] - .03f, (ConeShelfZ[0] + ConeShelfZ[1]) * .5f),
                new Vector3(.36f, .06f, ConeShelfZ[1] - ConeShelfZ[0] + ConeBase + .08f), shelfMat);
        for (int i = 0; i < ConeNames.Length; i++)
        {
            var cone = new GameObject(ConeNames[i], typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(Rigidbody));
            cone.transform.SetParent(equipment, false);
            cone.transform.localPosition = ConeLocalPosition(i) + Vector3.up * .005f;
            // Dünya-dik: araç bankette birkaç derece eğik olsa da koninin "yukarı" ekseni (TrafficCone.localUp) tam +Y okunur;
            // fizik koniyi rafa oturtur.
            cone.transform.rotation = Quaternion.identity;
            cone.GetComponent<MeshFilter>().sharedMesh = mesh;
            cone.GetComponent<MeshRenderer>().sharedMaterials = new[] { orange, white };
            var collider = cone.GetComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = true; collider.contactOffset = .001f;
            var body = cone.GetComponent<Rigidbody>(); body.mass = ConeMass; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        // Halat dolabı: sol iç duvarda kutu (arka yüzü açık), kapak menteşesi dış kenarda, +Y ekseninde +90° ile arkaya açılır.
        var metal = Material("Locker", new Color(.29f, .33f, .34f));
        var lockerBody = new GameObject(LockerBodyName).transform;
        lockerBody.SetParent(van.transform, false);
        lockerBody.localPosition = LockerCenter;
        Vector3 s = LockerSize; float w = LockerWall;
        Cube(lockerBody, "Back", new Vector3(0f, 0f, -s.z * .5f + w * .5f), new Vector3(s.x, s.y, w), metal);
        foreach (float x in new[] { -1f, 1f }) Cube(lockerBody, "Side", new Vector3(x * (s.x * .5f - w * .5f), 0f, 0f), new Vector3(w, s.y, s.z), metal);
        foreach (float y in new[] { -1f, 1f }) Cube(lockerBody, "Shelf", new Vector3(0f, y * (s.y * .5f - w * .5f), 0f), new Vector3(s.x - 2f * w, w, s.z), metal);
        var door = new GameObject(DoorName).transform;
        door.SetParent(van.transform, false);
        door.localPosition = LockerCenter + new Vector3(s.x * .5f, 0f, s.z * .5f + w * .5f); // menteşe: dış kenar, kapak düzlemi
        Cube(door, "Lid", new Vector3(-s.x * .5f, 0f, 0f), new Vector3(s.x, s.y, w), metal);
    }

    private static void BuildRoofSignal(Transform van)
    {
        // ServiceVehicle.SetModuleImmediate: soket (0, 2.79, 0.12); CreateAccessory("towing") ile aynı göreli ölçüler.
        var socket = new GameObject(RoofSignalName).transform;
        socket.SetParent(van, false); socket.localPosition = new Vector3(0f, 2.79f, .12f);
        var dark = Material("SignalHousing", new Color(.07f, .09f, .11f));
        var amber = Material("SignalAmber", new Color(1f, .57f, .08f));
        foreach (float x in new[] { -.42f, .42f }) Cube(socket, "RoofMountFoot", new Vector3(x, -.0675f, 0f), new Vector3(.10f, .075f, .20f), dark, false);
        Cube(socket, "LowProfileHousing", Vector3.zero, new Vector3(1.05f, .07f, .24f), dark, false);
        foreach (float x in new[] { -.29f, .29f }) Cube(socket, "SignalLens", new Vector3(x, .06f, 0f), new Vector3(.43f, .09f, .20f), amber, false);
    }

    private static void AddBox(GameObject owner, Vector3 center, Vector3 size)
    {
        var box = owner.AddComponent<BoxCollider>(); box.center = center; box.size = size;
    }

    private static void Cube(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = center; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
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

    // Trafik konisi: kare taban + 12 kenarlı gövde, beyaz şerit ayrı alt mesh; pivot tabanda, yukarı +Y.
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
        float half = ConeBase * .5f, plate = .035f, top = ConeHeight, r0 = half * .82f, r1 = .022f;
        Vector3[] bottom = { new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(half, 0f, half), new Vector3(-half, 0f, half) };
        Quad(bottom[0], bottom[1], bottom[2], bottom[3], orange);
        Quad(bottom[3] + Vector3.up * plate, bottom[2] + Vector3.up * plate, bottom[1] + Vector3.up * plate, bottom[0] + Vector3.up * plate, orange);
        for (int i = 0; i < 4; i++) Quad(bottom[(i + 1) % 4], bottom[i], bottom[i] + Vector3.up * plate, bottom[(i + 1) % 4] + Vector3.up * plate, orange);
        float[] heights = { plate, top * .42f, top * .60f, top };
        Vector3 Point(int side, float y) { float a = side * Mathf.PI / 6f, r = Mathf.Lerp(r0, r1, (y - plate) / (top - plate)); return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }
        for (int ring = 0; ring < heights.Length - 1; ring++)
            for (int i = 0; i < 12; i++)
                Quad(Point(i + 1, heights[ring]), Point(i, heights[ring]), Point(i, heights[ring + 1]), Point(i + 1, heights[ring + 1]), ring == 1 ? white : orange);
        for (int i = 0; i < 12; i++)
            Quad(new Vector3(0f, top, 0f), Point(i + 1, top), Point(i, top), new Vector3(0f, top, 0f), orange);
        var mesh = new Mesh { name = "ServiceVan_TrafficCone", subMeshCount = 2 };
        mesh.SetVertices(vertices); mesh.SetTriangles(orange, 0); mesh.SetTriangles(white, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
}
