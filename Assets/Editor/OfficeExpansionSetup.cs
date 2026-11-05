using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CompanySim;
using CompanySimEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Çok odalı yeni ofisi (build_office.py → Office_Shell.fbx + eşya FBX'leri + office_layout.json) OfficeGarage sahnesine kurar.
// Kullanıcı kararı (2026-11-05): ofis tek odadan beş odaya çıkar (ana ofis, mutfak, depo, soyunma, ring odası); garaj ve
// oynanış aynen kalır. Eski ofisin FBX parçaları sahnede gizlenir (FBX değişmez), eski Office_* çarpışma kutuları kalkar,
// yeni duvar ve eşya kutuları layout'tan gelir. Depo↔garaj geçişi x=0 z[1.6,3.6]: Garage_Left_Rear kutusu yerini
// layout'taki Wall_East_Storage_S / Lintel / N kutularına bırakır. Bilgisayar etkileşimi (Office_Computer_Main) yeni masadaki
// monitöre taşınır. Her oda tavan armatürüyle uyumlu sıcak spot ışık alır; yansıma probe'ları oda başına güncellenir.
// Tekrar çalıştırılabilir: önceki kurulum kökü silinir, önceki gizlemeler office_hidden_parts.txt listesinden geri açılır.
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod OfficeExpansionSetup.ApplyBatch -logFile C:/.../x.log
public static class OfficeExpansionSetup
{
    public const string RootName = "11_OfficeExpansion";
    public const string ModelFolder = OfficeImportSettings.Folder;
    public const string LayoutPath = ModelFolder + "office_layout.json";
    public const string HiddenListPath = ModelFolder + "office_hidden_parts.txt";
    public const string ShellFbx = ModelFolder + "Office_Shell.fbx";
    public const string ComputerName = "Office_Computer_Main";
    public const string LightingRootName = "09_PresentationLighting";
    private const string OutDir = "Captures/OfficeGarage";

    // Yeni binanın ayak izi (dış duvar kalınlığı dahil) ve iç hacmi
    public static readonly Vector3 FootprintMin = new Vector3(-9.95f, -0.02f, -5.45f);
    public static readonly Vector3 FootprintMax = new Vector3(0.05f, 3.4f, 12.3f);
    public const float PartitionKeepX = -0.05f; // bundan doğuda kalan merkezler bölme duvarına (x=0) ait sayılır (−0.12'de eski duvar saati ve askılar kalıyordu)
    // Depo/ring ↔ garaj arasındaki eski x=0 duvarı (köşe ayakları z≈1.2 ve z≈5.2 korunur)
    public const float EastWallZMin = 1.45f, EastWallZMax = 5.1f, EastWallHalfX = 0.45f, EastWallMaxY = 4.15f;
    // Korunan parçalar: bölme duvarı, kapılar ve dış giriş öğeleri
    private static readonly string[] KeepPrefixes = { "A03_", "A38_", "A11_", "A12_", "A13_", "A14_", "A34_", "EK_", "A08_", "A09_", "A10_", "A20_", "A21_" };
    private static readonly string[] OldCollisionBoxes = { "Office_West", "Office_Rear", "Office_Front_Left", "Office_Front_Right", "Office_Desk", "Office_Chair", "Office_FilingCabinet", "Office_DocumentShelf", "Garage_Left_Rear" };

    // Oda ışıkları: armatür konumları build_office.py ROOMS['lights'] ile aynı; (oda, x, z, gölge)
    private static readonly (string room, float x, float z, bool shadows)[] Fixtures =
    {
        ("Office", -2.4f, -3.4f, false), ("Office", -2.4f, -0.6f, true),
        ("Kitchen", -7.3f, -3.4f, true), ("Kitchen", -7.3f, -0.6f, false),
        ("Storage", -2.4f, 3.2f, true),
        ("LockerRoom", -7.3f, 3.2f, true),
        ("RingRoom", -7.0f, 8.6f, true), ("RingRoom", -2.8f, 8.6f, false),
    };
    public const float FixtureLightY = 3.05f;            // difüzör 3.175–3.195, ışık hemen altında
    // Blender FBX (axis_forward=-Z, up=Y) Unity'de X ve Z işareti ters okunur: prefab kopyaları 180° yaw ile düzeltilir
    public static readonly Quaternion FbxYaw = Quaternion.Euler(0f, 180f, 0f);
    public const float ExteriorOverlapTolerance = 0.15f; // dış süs parçası iç yüzü (duvar kalınlığı 0.15–0.24) aşmıyorsa bırakılır; aşanı tavandan içeri giriyordu
    public static readonly Color WarmLight = new Color(1f, 0.88f, 0.72f);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;

    // ---- office_layout.json (JsonUtility) ----
    [Serializable] public class Layout { public Room[] rooms; public DoorOpening[] door_openings; public BoxDef[] wall_colliders; public Item[] items; }
    [Serializable] public class Room { public string id; public string name_tr; public float[] bounds_min; public float[] bounds_max; public float[] center; public float[] size; }
    [Serializable] public class DoorOpening { public string id; public string type; public float[] center; public float[] size; public float[] bounds_min; public float[] bounds_max; }
    [Serializable] public class BoxDef { public string name; public float[] center; public float[] size; }
    [Serializable] public class Steps { public int count; public float rise_m; public float tread_depth_m; public float width_m; public float top_y_m; public float[] climb_direction_local; }
    [Serializable] public class Item { public string id; public string asset; public string fbx; public string room; public float[] position; public float rotation_y; public BoxDef box_collider; public BoxDef[] box_colliders; public Steps steps; }

    public static Layout LoadLayout()
    {
        if (!File.Exists(LayoutPath)) throw new Exception("Yerleşim dosyası yok: " + LayoutPath);
        return JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
    }
    public static Vector3 V3(float[] a) { return new Vector3(a[0], a[1], a[2]); }
    public static bool HasBox(BoxDef b) { return b != null && b.size != null && b.size.Length == 3 && b.center != null && b.center.Length == 3 && b.size[0] > 0f; }

    [MenuItem("Rescue Team/Ofis-Garaj/Yeni Ofisi Kur (çok odalı)")]
    public static void ApplyMenu()
    {
        if (!EditorUtility.DisplayDialog("Ofis genişletme", OfficeGaragePlayerSetup.ScenePath + " içine çok odalı ofis kurulacak, eski ofis parçaları gizlenecek ve sahne kaydedilecek. Devam?", "Kur", "Vazgeç")) return;
        Apply();
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("OfficeExpansionSetup: " + e); EditorApplication.Exit(1); }
    }

    public static void Apply()
    {
        report = new StringBuilder();
        Line("OFFICE EXPANSION SETUP " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        EnsureImported();
        var layout = LoadLayout();
        Line("Yerleşim: " + layout.rooms.Length + " oda, " + layout.items.Length + " eşya, " + layout.wall_colliders.Length + " duvar kutusu, " + layout.door_openings.Length + " kapı boşluğu");
        var scene = EditorSceneManager.OpenScene(OfficeGaragePlayerSetup.ScenePath, OpenSceneMode.Single);
        var env = GameObject.Find("01_Environment_OfficeGarage");
        if (env == null) throw new Exception("Sahnede 01_Environment_OfficeGarage yok");

        // 1) Önceki kurulum: kök silinir, gizlenen parçalar geri açılır (kurallar yeniden değerlendirilir)
        var oldRoot = GameObject.Find(RootName);
        if (oldRoot != null) { UnityEngine.Object.DestroyImmediate(oldRoot); Line("Önceki kurulum kökü silindi"); }
        RestoreHidden(env);
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.name == "Office_WarmLight" && !l.gameObject.activeSelf) l.gameObject.SetActive(true);

        // 2) Eski ofis parçaları ve yeni ayak izine giren dış süs parçaları gizlenir
        var hidden = new List<Transform>();
        var hiddenExterior = new List<string>();
        var hiddenEastWall = new List<string>();
        int hiddenOffice = 0;
        foreach (var r in env.GetComponentsInChildren<Renderer>(false))
        {
            if (!ShouldHide(r, out string reason)) continue;
            r.gameObject.SetActive(false);
            hidden.Add(r.transform);
            if (reason == "exterior") hiddenExterior.Add(r.name + " " + B(r.bounds));
            else if (reason == "eastwall") hiddenEastWall.Add(r.name + " " + B(r.bounds));
            else hiddenOffice++;
        }
        // Ayak izinde kalan çarpışma kutuları (FBX'ten gelen COL_* vb.)
        foreach (var c in env.GetComponentsInChildren<Collider>(false))
        {
            var b = c.bounds;
            if (c.GetComponentInParent<OfficeDoor>() != null) continue;
            if (b.center.x < PartitionKeepX && Inside(b.center)) { c.gameObject.SetActive(false); hidden.Add(c.transform); Line("  FBX collider gizlendi: " + c.name + " " + B(b)); }
        }
        SaveHidden(env, hidden);
        Line("Gizlenen: eski ofis " + hiddenOffice + ", x=0 doğu duvarı " + hiddenEastWall.Count + ", dış süs " + hiddenExterior.Count + " (liste: " + HiddenListPath + ")");
        foreach (var s in hiddenEastWall) Line("  x=0 duvarı: " + s);
        foreach (var s in hiddenExterior) Line("  dış: " + s);

        // 3) Eski Office_* çarpışma kutuları ve Garage_Left_Rear (geçiş için bölünmüş hâli layout'tan gelir)
        var worldCollision = GameObject.Find("00_WorldCollision");
        if (worldCollision == null) throw new Exception("00_WorldCollision yok");
        foreach (var n in OldCollisionBoxes)
        {
            var t = worldCollision.transform.Find(n);
            if (t != null) { UnityEngine.Object.DestroyImmediate(t.gameObject); Line("  eski kutu silindi: " + n); }
        }

        // 4) Yeni kök: kabuk, duvar/zemin kutuları, eşyalar, ışıklar
        var root = new GameObject(RootName);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var shellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShellFbx);
        if (shellPrefab == null) throw new Exception("Kabuk FBX yok: " + ShellFbx);
        // Blender FBX (−Z ileri, Y yukarı) Unity'de X ve Z işareti ters gelir (incir tuzağı); prefab kopyası 180° yaw'lı çocuk olur
        var shell = new GameObject("Office_Shell");
        shell.transform.SetParent(root.transform, false);
        var shellInstance = (GameObject)PrefabUtility.InstantiatePrefab(shellPrefab);
        shellInstance.transform.SetParent(shell.transform, false);
        ApplyFbxYaw(shellInstance.transform);
        MarkStatic(shell);
        var shellBounds = WorldBounds(shell);
        Line("Kabuk: " + shell.GetComponentsInChildren<Renderer>().Length + " parça, sınır " + B(shellBounds));
        foreach (var room in layout.rooms)
        {
            foreach (var key in new[] { "Floor", "Walls", "Ceiling", "Fixtures" })
                if (FindDeep(shell.transform, "Room_" + room.id + "_" + key) == null) Line("  UYARI: kabukta Room_" + room.id + "_" + key + " yok");
        }

        var walls = new GameObject("Walls").transform; walls.SetParent(root.transform, false);
        foreach (var w in layout.wall_colliders) Box(walls, w.name, V3(w.center), V3(w.size), false);
        Line("Duvar kutusu: " + layout.wall_colliders.Length);
        var floors = new GameObject("Floors").transform; floors.SetParent(root.transform, false);
        foreach (var room in layout.rooms)
        {
            var c = V3(room.center); var s = V3(room.size);
            Box(floors, "Floor_" + room.id, new Vector3(c.x, -0.06f, c.z), new Vector3(s.x + 0.3f, 0.12f, s.z + 0.3f), false); // üst yüzey y=0
        }

        var itemsRoot = new GameObject("Items").transform; itemsRoot.SetParent(root.transform, false);
        var roomParents = new Dictionary<string, Transform>();
        int itemCount = 0, colliderCount = 0;
        foreach (var item in layout.items)
        {
            if (!roomParents.TryGetValue(item.room, out var parent))
            {
                parent = new GameObject(item.room).transform; parent.SetParent(itemsRoot, false); roomParents[item.room] = parent;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + item.fbx);
            if (prefab == null) throw new Exception("Eşya FBX yok: " + ModelFolder + item.fbx);
            // Eşya kökü layout çerçevesinde (collider'lar burada), prefab kopyası 180° yaw'lı çocuk (FBX eksen tuzağı)
            var go = new GameObject(item.id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(V3(item.position), Quaternion.Euler(0f, item.rotation_y, 0f));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(go.transform, false);
            ApplyFbxYaw(instance.transform);
            if (item.box_colliders != null && item.box_colliders.Length > 0)
            {
                foreach (var bd in item.box_colliders) if (HasBox(bd)) { var bc = go.AddComponent<BoxCollider>(); bc.center = V3(bd.center); bc.size = V3(bd.size); colliderCount++; }
            }
            else if (item.steps != null && item.steps.count > 0 && HasBox(item.box_collider))
            {
                // Basamaklar: tek sınır kutusu çıkılamaz; üst üste basamak kutuları da yandan yaklaşan kapsülü duvarla sıkıştırıyor
                // (basamak dibi ile güney duvarı arası 12 cm, 2. basamağın yan yüzü 0.233 > step 0.22). Görsel basamakların altına
                // eğik rampa kutusu (≈21°, yerel +z yönünde yükselir) konur; CharacterController her yönden çıkar.
                var bb = item.box_collider; float depth = bb.size[2]; float top = item.steps.top_y_m;
                float z0 = bb.center[2] - depth * 0.5f;
                float length = Mathf.Sqrt(depth * depth + top * top); float angle = Mathf.Atan2(top, depth) * Mathf.Rad2Deg;
                var ramp = new GameObject("StepsRamp"); ramp.transform.SetParent(go.transform, false);
                // Rampa 1,5 cm aşağıda ve iki ucu 2,5 cm uzun: üst uç yüzü (eğime dik; slopeLimit'e göre "duvar") platform
                // kutusunun altında kalır. Dışarıda kalınca kapsül platformdan dümdüz güneye inerken ona takılıyordu.
                ramp.transform.SetLocalPositionAndRotation(new Vector3(bb.center[0], -0.015f, z0), Quaternion.Euler(-angle, 0f, 0f));
                var rc = ramp.AddComponent<BoxCollider>(); const float thick = 0.3f;
                rc.center = new Vector3(0f, -thick * 0.5f, length * 0.5f); rc.size = new Vector3(item.steps.width_m, thick, length + 0.05f);
                colliderCount++;
            }
            else if (HasBox(item.box_collider))
            {
                var bc = go.AddComponent<BoxCollider>(); bc.center = V3(item.box_collider.center); bc.size = V3(item.box_collider.size); colliderCount++;
            }
            MarkStatic(go);
            itemCount++;
        }
        Line("Eşya: " + itemCount + ", eşya kutusu: " + colliderCount);

        // 5) Işık: eski tek ofis ışığı kapanır, her odaya armatürle hizalı sıcak spot
        var oldWarm = GameObject.Find("Office_WarmLight");
        if (oldWarm != null) { oldWarm.SetActive(false); Line("Office_WarmLight kapatıldı (yerini oda ışıkları aldı)"); }
        var lights = new GameObject("Lights").transform; lights.SetParent(root.transform, false);
        int shadowed = 0;
        var perRoom = Fixtures.GroupBy(f => f.room).ToDictionary(g => g.Key, g => g.Count());
        foreach (var f in Fixtures)
        {
            // İlk turda odalar Blender render'ına göre loş ve grimsi kaldı; sıcak spotlar güçlendirildi
            float intensity = f.room == "RingRoom" ? 5.5f : perRoom[f.room] > 1 ? 4.8f : 6.0f;
            Spot(lights, "Light_" + f.room + "_" + f.x.ToString("0.0", Inv) + "_" + f.z.ToString("0.0", Inv), new Vector3(f.x, FixtureLightY, f.z), intensity, f.room == "RingRoom" ? 8f : 7.5f, f.shadows);
            if (f.shadows) shadowed++;
        }
        // Tavan yalnız ortam ışığıyla gri kalıyordu (render'da sıcak): oda başına yukarı bakan gölgesiz zayıf "sıçrama" spotu
        foreach (var room in layout.rooms)
        {
            var c = V3(room.center);
            var bounce = Spot(lights, "Bounce_" + room.id, new Vector3(c.x, 1.2f, c.z), 1.4f, 4.5f, false);
            bounce.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            bounce.spotAngle = 150f; bounce.innerSpotAngle = 110f;
        }
        Line("Ek ışık: " + Fixtures.Length + " armatür spotu (" + shadowed + " gölgeli) + " + layout.rooms.Length + " gölgesiz tavan sıçrama spotu");

        // 6) Yansıma probe'ları: OfficeReflection ana ofise oturur, diğer odalara birer probe (09_PresentationLighting altında, bake)
        var lightingRoot = GameObject.Find(LightingRootName);
        if (lightingRoot == null) { lightingRoot = new GameObject(LightingRootName); Line("  UYARI: 09_PresentationLighting yoktu, oluşturuldu"); }
        foreach (var room in layout.rooms)
        {
            string probeName = room.id == "Office" ? "OfficeReflection" : room.id + "Reflection";
            var pt = lightingRoot.transform.Find(probeName);
            var probe = pt != null ? pt.GetComponent<ReflectionProbe>() : null;
            if (probe == null)
            {
                var pgo = new GameObject(probeName); pgo.transform.SetParent(lightingRoot.transform, false);
                probe = pgo.AddComponent<ReflectionProbe>(); probe.resolution = 128; probe.boxProjection = true; probe.intensity = .65f;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox; probe.nearClipPlane = .1f; probe.farClipPlane = 35f;
            }
            probe.transform.position = V3(room.center);
            probe.size = V3(room.size);
            probe.mode = ReflectionProbeMode.Baked;
        }
        Line("Yansıma probe'u: " + layout.rooms.Length + " oda (+ GarageReflection yeniden bake)");

        // 7) Bilgisayar etkileşimi yeni masadaki monitöre
        var monitor = layout.items.FirstOrDefault(i => i.id == "Monitor_01");
        var computer = GameObject.Find(ComputerName);
        if (computer == null) throw new Exception(ComputerName + " yok");
        if (monitor == null) throw new Exception("Yerleşimde Monitor_01 yok");
        var monitorTf = FindDeep(itemsRoot, "Monitor_01");
        Vector3 monitorCenter = monitorTf.TransformPoint(HasBox(monitor.box_collider) ? V3(monitor.box_collider.center) : new Vector3(0f, .17f, 0f));
        computer.transform.SetPositionAndRotation(monitorCenter, Quaternion.Euler(0f, monitor.rotation_y, 0f));
        var pc = computer.GetComponent<BoxCollider>();
        if (pc == null) pc = computer.AddComponent<BoxCollider>();
        pc.isTrigger = true; pc.center = Vector3.zero;
        pc.size = HasBox(monitor.box_collider) ? V3(monitor.box_collider.size) + new Vector3(.08f, .1f, .2f) : new Vector3(.5f, .4f, .35f);
        var oc = computer.GetComponent<OfficeComputer>();
        if (oc == null || oc.ui == null) Line("  UYARI: Office_Computer_Main'de OfficeComputer/ui eksik");
        Line("Bilgisayar: " + V(computer.transform.position) + ", kutu " + V(pc.size));

        // 7b) x=0 duvarının garaj yüzü: ofis sıvası yerine garajın kendi sıva/bant kaplaması (GarageRefinementBuilder "Finish" deseni:
        //     ince küp levhalar, ofis yüzü değişmez). Kasa dikmeleri z[1.535,1.6] / [3.6,3.665], başlık y[2.5,2.565] açık bırakılır.
        //     Eski A02 duvarı gibi dünya ölçekli PlanarUV: küpün 0-1 UV'si 3 m'lik levhaya dokuyu gerer.
        var garageFace = new GameObject("GarageFace").transform; garageFace.SetParent(root.transform, false);
        var gPlaster = AssetDatabase.LoadAssetAtPath<Material>(GaragePlasterPath);
        var gBand = AssetDatabase.LoadAssetAtPath<Material>(GarageBandPath);
        if (gPlaster == null || gBand == null) Line("  UYARI: garaj sıva/bant materyali yok (" + GaragePlasterPath + ")");
        else
        {
            const float faceX = 0.130f, thick = 0.006f, band = 0.96f, top = 4.0f, head = 2.565f;
            foreach (var (side, z0, z1) in new[] { ("S", 1.40f, 1.535f), ("N", 3.665f, 5.20f) })
            {
                Finish(garageFace, "GarageFace_Band_" + side, new Vector3(faceX, band * .5f, (z0 + z1) * .5f), new Vector3(thick, band, z1 - z0), gBand);
                Finish(garageFace, "GarageFace_Plaster_" + side, new Vector3(faceX, (band + top) * .5f, (z0 + z1) * .5f), new Vector3(thick, top - band, z1 - z0), gPlaster);
            }
            Finish(garageFace, "GarageFace_Lintel", new Vector3(faceX, (head + top) * .5f, (1.535f + 3.665f) * .5f), new Vector3(thick, top - head, 3.665f - 1.535f), gPlaster);
            Line("Garaj yüzü kaplaması: " + garageFace.childCount + " levha (Garage_Plaster / Garage_ProtectionBand)");
        }

        // 7c) Upgrade_OperationsBoard (pasif yükseltme): ProjectBuilder.CloneParts kökün y'sini (1.6) parçaların dünya y'sine ekliyor,
        //     pano y[3.35,3.84] ile ofis tavanının üstünde, yüzü de duvara dönük kalıyordu. Yüzü kaynaktaki gibi +x'e baktığından
        //     batı duvarına (x=-4.72 yüzü) oturur; FilingCabinet_02 ve üstündeki kâğıtların (y≤1.61) üstünde, kapı kasasının güneyinde.
        var board = FindRoot("Upgrade_OperationsBoard");
        if (board != null)
        {
            var bb = InactiveWorldBounds(board.transform);
            Vector3 target = new Vector3(-4.72f + bb.size.x * .5f + 0.003f, OperationsBoardCenterY, -3.9f);
            board.transform.position += target - bb.center;
            var after = InactiveWorldBounds(board.transform);
            Line("Upgrade_OperationsBoard: " + B(bb) + " → " + B(after));
        }
        else Line("  UYARI: Upgrade_OperationsBoard yok");

        // 8) Oyuncu doğuşu: ana ofiste, zemine oturmuş ve engelsiz
        Physics.SyncTransforms();
        var spawn = GameObject.Find(OfficeGaragePlayerSetup.SpawnName);
        var player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>(FindObjectsInactive.Include);
        if (spawn != null && player != null)
        {
            var office = layout.rooms.First(r => r.id == "Office");
            Vector3 at = spawn.transform.position;
            if (!InsideXZ(at, V3(office.bounds_min), V3(office.bounds_max)) || !FreeCapsule(at, player)) { at = new Vector3(-2.8f, 0f, -1.35f); Line("  doğma yeri ana ofise taşındı"); }
            if (!FreeCapsule(at, player)) { at = new Vector3(-2.2f, 0f, -2.6f); Line("  doğma yeri halı ortasına alındı"); }
            foreach (var h in Physics.RaycastAll(at + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (!h.collider.transform.IsChildOf(player.transform)) { at.y = h.point.y + 0.02f; break; }
            spawn.transform.position = at;
            player.transform.position = at;
            Line("Oyuncu doğuşu: " + V(at) + " engelsiz " + FreeCapsule(at, player));
        }
        else Line("  UYARI: PlayerSpawn / ThirdPersonPlayer yok (OfficeGaragePlayerSetup önce çalışmalı)");

        // 9) Yansımalar bake edilir, sahne kaydedilir
        OfficePresentationBuilder.BakeReflections();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Sahne kaydedilemedi: " + OfficeGaragePlayerSetup.ScenePath);
        AssetDatabase.SaveAssets();
        Line("Sahne kaydedildi: " + OfficeGaragePlayerSetup.ScenePath);
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "office_expansion_setup_report.txt"), report.ToString());
        Debug.Log(report.ToString());
    }

    // FBX import ayarlarının (OfficeImportSettings) uygulandığından emin olur; değilse klasörü yeniden içe aktarır.
    private static void EnsureImported()
    {
        var importer = AssetImporter.GetAtPath(ShellFbx) as ModelImporter;
        if (importer == null) throw new Exception("Kabuk FBX içe aktarılmamış: " + ShellFbx);
        if (OfficeImportSettings.IsApplied(importer)) return;
        AssetDatabase.ImportAsset(ModelFolder.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        importer = AssetImporter.GetAtPath(ShellFbx) as ModelImporter;
        if (!OfficeImportSettings.IsApplied(importer)) throw new Exception("OfficeImportSettings uygulanamadı: " + ShellFbx);
    }

    // ---- gizleme kuralları (OfficeExpansionVerify de kullanır) ----
    public static bool Inside(Vector3 c)
    {
        return c.x >= FootprintMin.x && c.x <= FootprintMax.x && c.z >= FootprintMin.z && c.z <= FootprintMax.z;
    }
    private static bool InsideXZ(Vector3 p, Vector3 min, Vector3 max) { return p.x >= min.x && p.x <= max.x && p.z >= min.z && p.z <= max.z; }

    public static bool ShouldHide(Renderer r, out string reason)
    {
        reason = null;
        if (r.GetComponentInParent<OfficeDoor>() != null) return false;
        string n = r.name;
        foreach (var k in KeepPrefixes) if (n.StartsWith(k)) return false;
        var b = r.bounds; var c = b.center;
        if (n.StartsWith("E"))
        {
            // Dış süs/bitki: merkezi ayak izinde olan ya da iç hacme toleranstan fazla giren gizlenir (zemin altındakiler kalır)
            if (b.max.y <= FootprintMin.y || b.min.y >= FootprintMax.y) return false;
            float ox = Mathf.Min(b.max.x, FootprintMax.x - ExteriorOverlapTolerance) - Mathf.Max(b.min.x, FootprintMin.x + ExteriorOverlapTolerance);
            float oz = Mathf.Min(b.max.z, FootprintMax.z - ExteriorOverlapTolerance) - Mathf.Max(b.min.z, FootprintMin.z + ExteriorOverlapTolerance);
            bool centerInside = Inside(c) && c.y > FootprintMin.y && c.y < FootprintMax.y;
            if (centerInside || (ox > 0f && oz > 0f)) { reason = "exterior"; return true; }
            return false;
        }
        if (c.x < PartitionKeepX && Inside(c)) { reason = "office"; return true; }
        // x=0 doğu duvarı (A02/A42 duvar, A18 kepenk, A19 kılavuz, A05 koping); garaj rafı (G01) ve diğerleri dokunulmaz
        if (n.StartsWith("A") && Mathf.Abs(c.x) < EastWallHalfX && c.z >= EastWallZMin && c.z <= EastWallZMax && c.y < EastWallMaxY) { reason = "eastwall"; return true; }
        return false;
    }

    private static void SaveHidden(GameObject env, List<Transform> hidden)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OfficeExpansionSetup tarafından gizlenen 01_Environment_OfficeGarage parçaları (kardeş sırası yolu | ad). Elle düzenleme.");
        foreach (var t in hidden) sb.AppendLine(IndexPath(env.transform, t) + "|" + t.name);
        File.WriteAllText(HiddenListPath, sb.ToString());
    }

    private static void RestoreHidden(GameObject env)
    {
        if (!File.Exists(HiddenListPath)) return;
        int restored = 0;
        foreach (var line in File.ReadAllLines(HiddenListPath))
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('|');
            var t = ByIndexPath(env.transform, parts[0]);
            if (t == null || (parts.Length > 1 && t.name != parts[1])) t = FindDeep(env.transform, parts.Length > 1 ? parts[1] : "", true);
            if (t != null && !t.gameObject.activeSelf) { t.gameObject.SetActive(true); restored++; }
        }
        Line("Önceki gizlemeler geri açıldı: " + restored);
    }

    private static string IndexPath(Transform root, Transform t)
    {
        var idx = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) idx.Add(x.GetSiblingIndex().ToString(Inv));
        idx.Reverse();
        return string.Join("/", idx);
    }
    private static Transform ByIndexPath(Transform root, string path)
    {
        var t = root;
        foreach (var s in path.Split('/'))
        {
            if (!int.TryParse(s, NumberStyles.Integer, Inv, out int i) || i < 0 || i >= t.childCount) return null;
            t = t.GetChild(i);
        }
        return t;
    }

    // ---- yardımcılar ----
    private static Transform FindDeep(Transform root, string name, bool exact = true)
    {
        foreach (Transform child in root)
        {
            if (exact ? child.name == name : child.name.StartsWith(name)) return child;
            var f = FindDeep(child, name, exact); if (f != null) return f;
        }
        return null;
    }

    public const string GaragePlasterPath = "Assets/_Project/Art/Garage/Materials/Garage_Plaster.mat";
    public const string GarageBandPath = "Assets/_Project/Art/Garage/Materials/Garage_ProtectionBand.mat";
    public const float OperationsBoardCenterY = 2.0f; // pano 0,49 m yüksek: y[1.755,2.245]

    // GarageRefinementBuilder.Finish ile aynı: collider'sız ince küp levha. Adlar benzersiz olmalı, PlanarUV mesh'i ada göre saklar.
    private static GameObject Finish(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.position = position; go.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        GarageRefinementBuilder.PlanarUV(renderer);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
        return go;
    }

    // Pasif (inactive) kök nesneyi de bulur (GameObject.Find pasifleri görmez)
    public static GameObject FindRoot(string name)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == name) return go;
        return null;
    }

    // Pasif nesnenin dünya sınırı: renderer.bounds pasifte güvenilmez, mesh sınırı dönüştürülür
    public static Bounds InactiveWorldBounds(Transform root)
    {
        bool any = false; var result = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
            foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            {
                var p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p);
            }
        }
        return result;
    }

    // Prefab kökünün içe aktarmadan gelen dönüşü (tek nesneli FBX'te kökte −90° X olabilir) korunur, üstüne 180° yaw eklenir.
    // Kökü doğrudan FbxYaw ile ezmek eşyaları 90° yatırıyordu (ilk turda halı dik, duvar panoları yatay çıktı).
    private static void ApplyFbxYaw(Transform instance)
    {
        instance.localRotation = FbxYaw * instance.localRotation;
        instance.localPosition = FbxYaw * instance.localPosition;
    }

    private static void Box(Transform parent, string name, Vector3 center, Vector3 size, bool trigger)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = center;
        var bc = go.AddComponent<BoxCollider>(); bc.size = size; bc.isTrigger = trigger;
    }

    private static Light Spot(Transform parent, string name, Vector3 position, float intensity, float range, bool shadows)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.intensity = intensity; l.range = range; l.color = WarmLight;
        l.spotAngle = 140f; l.innerSpotAngle = 100f;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.shadowStrength = .6f; l.shadowBias = .025f; l.shadowNormalBias = .25f; l.shadowNearPlane = .08f;
        return l;
    }

    private static void MarkStatic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic);
    }

    private static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
    }

    private static bool FreeCapsule(Vector3 at, ThirdPersonPlayer player)
    {
        var cc = player.GetComponent<CharacterController>();
        float radius = cc != null ? cc.radius : .3f, height = cc != null ? cc.height : 1.5f;
        Vector3 p0 = at + Vector3.up * (radius + .05f), p1 = at + Vector3.up * (height - radius);
        foreach (var c in Physics.OverlapCapsule(p0, p1, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(player.transform)) return false;
        return true;
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
    private static string B(Bounds b) { return "x[" + b.min.x.ToString("F2", Inv) + "," + b.max.x.ToString("F2", Inv) + "] y[" + b.min.y.ToString("F2", Inv) + "," + b.max.y.ToString("F2", Inv) + "] z[" + b.min.z.ToString("F2", Inv) + "," + b.max.z.ToString("F2", Inv) + "]"; }
}
