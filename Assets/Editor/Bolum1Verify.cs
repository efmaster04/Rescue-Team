using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bölüm 1 kabul testi. Batchmode (render için -nographics OLMADAN):
// Unity.exe -batchmode -projectPath ... -executeMethod Bolum1Verify.Run -logFile <dosya>
// Kontroller: import ayarları, sahne objeleri, koordinat dönüşümü (raycast), yol genişliği, patika derinliği,
// minibüs arka tekerlekleri, karakter boyu, derleme hatası. Minibüs fizik testi yalnızca ölçülür (kabul ölçütü değil).
// Çıktı: Captures/Bolum1/verify_report.txt ve ekran görüntüleri. Sahne kaydedilmez.
//
// İki aşama: (1) edit modunda statik kontroller, (2) Play modunda karakter (Animator + ayak IK'sı yalnızca Play'de çalışır),
// ekran görüntüleri ve fizik testi. Play'e girerken domain yeniden yüklendiği için ara durum SessionState'te taşınır.
// Fizik testi sırasında Rigidbody'nin kendiliğinden düşmemesi için Physics.simulationMode geçici olarak Script yapılır, sonda geri alınır.
[InitializeOnLoad]
public static partial class Bolum1Verify
{
    private const string OutDir = "Captures/Bolum1";
    private const string StageKey = "Bolum1Verify.Stage";
    private const string ReportKey = "Bolum1Verify.Report";
    private const string OkKey = "Bolum1Verify.Ok";
    private const string BatchKey = "Bolum1Verify.Batch";
    private const string SimModeKey = "Bolum1Verify.SimMode";
    private const float SettleSeconds = 2f; // Idle + IK'nın oturması için Play'de beklenen süre

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;

    static Bolum1Verify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayStageTick;
    }

    [MenuItem("Rescue Team/Bölüm 1/Doğrula")]
    public static void RunMenu()
    {
        Begin(false);
    }

    public static void Run()
    {
        Begin(true);
    }

    private static void Begin(bool batch)
    {
        playStart = -1f;
        try
        {
            Directory.CreateDirectory(OutDir);
            report = new StringBuilder();
            allOk = true;
            Line("BOLUM1 VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));

            CheckImports();                                    // (a)
            CheckMaterialColors();                           // (a2) tüm renkler
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            CheckSceneObjects();                               // (b)
            Physics.SyncTransforms();
            var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
            CheckCoordinates(markers);                         // dönüşüm doğrulaması
            CheckRoadAndPath(markers);                         // (c)
            CheckRearWheels(GameObject.Find(Bolum1SceneBuilder.MinivanName)); // (d)
            CheckTowTruck(markers);
            var character = GameObject.Find(Bolum1SceneBuilder.CharacterName);
            bindPoseGap = SoleGap(character);
            SessionState.SetFloat("Bolum1Verify.BindHeight", MeasureHeight(character, out _));
        }
        catch (Exception e)
        {
            Fail("edit aşaması: " + e);
            Finish(batch);
            return;
        }

        SessionState.SetString(ReportKey, report.ToString());
        SessionState.SetBool(OkKey, allOk);
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetFloat("Bolum1Verify.BindGap", bindPoseGap);
        SessionState.SetInt(SimModeKey, (int)Physics.simulationMode);
        SessionState.SetString(StageKey, "play");
        Physics.simulationMode = SimulationMode.Script;
        EditorApplication.update += PlayStageTick;
        EditorApplication.EnterPlaymode();
    }

    private static float bindPoseGap;
    private static float playStart = -1f;

    private static void PlayStageTick()
    {
        if (!EditorApplication.isPlaying) return;
        if (playStart < 0f) { playStart = Time.realtimeSinceStartup; return; }
        if (Time.realtimeSinceStartup - playStart < SettleSeconds || Time.frameCount < 30) return;

        EditorApplication.update -= PlayStageTick;
        SessionState.EraseString(StageKey);
        report = new StringBuilder(SessionState.GetString(ReportKey, ""));
        allOk = SessionState.GetBool(OkKey, false);
        bool batch = SessionState.GetBool(BatchKey, true);
        bindPoseGap = SessionState.GetFloat("Bolum1Verify.BindGap", float.NaN);
        try
        {
            var minivan = GameObject.Find(Bolum1SceneBuilder.MinivanName);
            var character = GameObject.Find(Bolum1SceneBuilder.CharacterName);
            CheckCharacter(character);                         // (e)
            var player = character.GetComponent<ThirdPersonPlayer>();
            var bodies = UnityEngine.Object.FindObjectsByType<NpcBody>(FindObjectsSortMode.None);
            Check("oynanış düzeltmeleri Play'de kurulu", player != null && Mathf.Approximately(player.runSpeed, 3.2f)
                && Mathf.Approximately(player.hardLandingHeight, 3f) && bodies.Length >= 4
                && bodies.All(b => b.Body != null && b.Body.enabled == SolidExpected(b)),
                "koşu " + player.runSpeed + ", düşüş eşiği " + player.hardLandingHeight + ", NPC bedenleri " + bodies.Length + " (katı: "
                + string.Join(", ", bodies.Select(b => b.name + " " + b.Body.enabled)) + "; D2: koltuktaki hastalar katı değil)");

            Capture("overview", Bolum1Coordinates.ToUnity(new Vector3(-25f, -75f, 25f)), Bolum1Coordinates.ToUnity(new Vector3(0f, 5f, -6f)), 50f);
            Vector3 vanLook = minivan.transform.position + Vector3.up * 0.3f;
            Vector3 closeCam = Bolum1Coordinates.ToUnity(new Vector3(-3.5f, -24f, -6.5f));
            Capture("closeup_minivan_character", closeCam, (vanLook + character.transform.position) * 0.5f, 45f);
            CaptureTowTruck();

            PhysicsTest(minivan, closeCam, vanLook);           // yalnızca rapor
        }
        catch (Exception e)
        {
            Fail("play aşaması: " + e);
        }
        Physics.simulationMode = (SimulationMode)SessionState.GetInt(SimModeKey, (int)SimulationMode.FixedUpdate);
        Finish(batch);
    }

    private static void Finish(bool batch)
    {
        CheckCompileErrors();                              // (f)
        Line(allOk ? "BOLUM1 VERIFY OK" : "BOLUM1 VERIFY FAILED");
        File.WriteAllText(Path.Combine(OutDir, "verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "BOLUM1 VERIFY OK" : "BOLUM1 VERIFY FAILED");
        if (batch)
        {
            EditorApplication.Exit(allOk ? 0 : 1);
        }
        else
        {
            EditorApplication.isPlaying = false;
            EditorUtility.DisplayDialog("Bölüm 1", allOk ? "BOLUM1 VERIFY OK" : "BOLUM1 VERIFY FAILED (bkz. " + OutDir + "/verify_report.txt)", "Tamam");
        }
    }

    private static void Fail(string message)
    {
        allOk = false;
        Line("[FAIL] " + message);
    }

    // ---------- (a) import ayarları ----------
    private static void CheckImports()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models/Environment/Bolum1", "Assets/Models/Vehicles/Minivan", "Assets/Models/Vehicles/TowTruck" });
        int ok = 0, total = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Bolum1ImportSettings.IsBolum1Model(path)) continue;
            total++;
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp != null && Bolum1ImportSettings.IsApplied(imp) && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) ok++;
            else Line("  ayar hatalı: " + path);
        }
        Check("(a) FBX import + ayarlar (importer'dan geri okundu)", ok == 26 && total == 26, ok + "/" + total + " (beklenen 26: 24 mekân + minibüs + çekici)");
    }

    // ---------- (a2) materyal renkleri: minibüs, mekân, karakter ----------
    // Her materyalin Material.color'ı (sRGB) beklenen hex'e ΔE76 < 2 ile eşit olmalı (tablo: Bolum1ColorProbe.MeasureAll);
    // minibüs camı Transparent ve alfa ≈ 0.35.
    private static void CheckMaterialColors()
    {
        var results = Bolum1ColorProbe.MeasureAll(out List<string> problems);
        foreach (string p in problems) Line("  renk sorunu: " + p);
        var parts = new List<string>();
        foreach (var g in results.GroupBy(r => r.Group.StartsWith("karakter") ? "karakter" : r.Group))
        {
            var w = g.OrderByDescending(r => r.DeltaE).First();
            parts.Add(g.Key + " " + g.Select(r => r.Group + r.Material).Distinct().Count() + " materyal, en büyük ΔE76 " + w.DeltaE.ToString("F2", Inv) + " (" + w.Material + (g.Key == "karakter" ? " " + w.Group.Substring(9) : "") + ")");
        }
        foreach (var r in results.Where(r => r.DeltaE >= Bolum1ColorProbe.MaxDeltaE))
            Line("  renk sapması: " + r.Group + " " + r.Asset + " " + r.Material + " #" + ColorUtility.ToHtmlStringRGB(r.Actual) + " beklenen #" + ColorUtility.ToHtmlStringRGB(r.Expected) + " ΔE76 " + r.DeltaE.ToString("F2", Inv));
        bool colorsOk = problems.Count == 0 && results.Count > 0 && results.All(r => r.DeltaE < Bolum1ColorProbe.MaxDeltaE);

        var mats = AssetDatabase.LoadAllAssetsAtPath(Bolum1SceneBuilder.MinivanPath).OfType<Material>().ToDictionary(m => m.name);
        mats.TryGetValue(Bolum1ImportSettings.PaintMaterial, out var paint);
        mats.TryGetValue(Bolum1ImportSettings.GlassMaterial, out var glass);
        bool glassOk = glass != null && glass.GetFloat("_Surface") > 0.5f && glass.renderQueue >= 3000
            && Mathf.Abs(glass.GetColor("_BaseColor").a - Bolum1ImportSettings.GlassAlpha) < 0.01f;
        Check("(a2) materyal renkleri (minibüs + mekân + karakter 4 varyant, ΔE76 < 2) ve şeffaf cam", colorsOk && glassOk,
            string.Join("; ", parts) + "; sorun " + problems.Count + "; boya "
            + (paint != null ? "#" + ColorUtility.ToHtmlStringRGB(paint.GetColor("_BaseColor")) : "YOK")
            + "; cam " + (glass != null ? (glass.GetFloat("_Surface") > 0.5f ? "Transparent" : "Opaque") + " a=" + glass.GetColor("_BaseColor").a.ToString("F2", Inv) + " kuyruk " + glass.renderQueue : "YOK"));
    }

    // ---------- (b) sahne ----------
    private static void CheckSceneObjects()
    {
        var expected = new List<string>(Bolum1SceneBuilder.Modules) { Bolum1SceneBuilder.MinivanName, Bolum1SceneBuilder.TowTruckName, Bolum1SceneBuilder.CharacterName, "Sun", "Main Camera" };
        expected.AddRange(Bolum1SceneBuilder.MarkerNames);
        var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
        foreach (var kv in Bolum1Coordinates.Obj(markers, "placements"))
            if (kv.Key != "MinivanPlacement" && kv.Key != "TowTruckPlacement") expected.Add(kv.Key);
        var missing = expected.Where(n => GameObject.Find(n) == null).ToList();

        // Collider kuralları
        var backdrop = GameObject.Find(Bolum1SceneBuilder.NoColliderModule);
        bool backdropClean = backdrop.GetComponentsInChildren<Collider>().Length == 0;
        bool modulesMesh = Bolum1SceneBuilder.Modules.All(m =>
        {
            var cols = GameObject.Find(m).GetComponentsInChildren<Collider>();
            bool wants = Array.IndexOf(Bolum1SceneBuilder.ColliderModules, m) >= 0;
            return wants ? cols.Length > 0 && cols.All(c => c is MeshCollider mc && !mc.convex) : cols.Length == 0;
        });
        var van = GameObject.Find(Bolum1SceneBuilder.MinivanName);
        var rb = van.GetComponent<Rigidbody>();
        bool vanOk = rb != null && Mathf.Approximately(rb.mass, Bolum1SceneBuilder.MinivanMass)
            && van.GetComponentsInChildren<MeshCollider>().All(c => c.convex);
        // Minibüste VehicleHold var ve holdOnStart açık (private SerializeField, SerializedObject ile okunur).
        var hold = van.GetComponent<VehicleHold>();
        bool holdOk = hold != null && new SerializedObject(hold).FindProperty("holdOnStart").boolValue;
        Check("(g) minibüs VehicleHold, holdOnStart açık", holdOk, hold != null ? "VehicleHold var" : "VehicleHold YOK");
        Check("(b) sahne açıldı, beklenen objeler", missing.Count == 0 && backdropClean && modulesMesh && vanOk,
            expected.Count + " obje, eksik: " + (missing.Count == 0 ? "yok" : string.Join(",", missing))
            + "; backdrop collider yok=" + backdropClean + "; modül collider kuralı (Ground/Road/Guardrail convex olmayan MeshCollider, diğerleri yok)=" + modulesMesh
            + "; minibüs Rigidbody " + (rb != null ? rb.mass.ToString("F0", Inv) + " kg" : "YOK")
            + ", box=" + van.GetComponentsInChildren<BoxCollider>().Length + " sphere=" + van.GetComponentsInChildren<SphereCollider>().Length);
    }

    // ---------- koordinat dönüşümü ----------
    private static void CheckCoordinates(Dictionary<string, object> markers)
    {
        var ground = GameObject.Find("SM_B1_Ground").transform;
        Line("  modül kökü (Ground): konum " + V(ground.position) + ", dönüş " + V(ground.eulerAngles) + ", ölçek " + V(ground.lossyScale));
        var probes = Bolum1Coordinates.Obj(markers, "geometry_probes");
        float worstPath = 0f, worstRoad = 0f;
        int n = 0;
        foreach (var p in (List<object>)probes["path"])
        {
            Vector3 b = Bolum1Coordinates.Vec3(p);
            if (!RayDownToModule(Bolum1Coordinates.ToUnity(b), "SM_B1_Ground", out RaycastHit hit)) { worstPath = 99f; continue; }
            worstPath = Mathf.Max(worstPath, Mathf.Abs(hit.point.y - b.z)); n++;
        }
        foreach (var cs in (List<object>)probes["road_cross_sections"])
        {
            Vector3 b = Bolum1Coordinates.Vec3(cs, "center");
            if (!RayDownToModule(Bolum1Coordinates.ToUnity(b), "SM_B1_Road", out RaycastHit hit)) { worstRoad = 99f; continue; }
            worstRoad = Mathf.Max(worstRoad, Mathf.Abs(hit.point.y - b.z));
        }
        Check("dönüşüm Blender(x,y,z)→Unity(-x,z,-y) raycast", worstPath < 0.05f && worstRoad < 0.05f,
            "patika " + n + " nokta en büyük sapma " + Cm(worstPath) + ", yol kesitleri en büyük sapma " + Cm(worstRoad));
    }

    // ---------- (c) yol genişliği ve patika derinliği ----------
    private static void CheckRoadAndPath(Dictionary<string, object> markers)
    {
        var probes = Bolum1Coordinates.Obj(markers, "geometry_probes");
        var sections = (List<object>)probes["road_cross_sections"];
        var widths = new List<float>();
        foreach (var cs in sections)
        {
            Vector3 c = Bolum1Coordinates.Vec3(cs, "center"), nrm = Bolum1Coordinates.Vec3(cs, "normal");
            float min = float.MaxValue, max = float.MinValue;
            for (float s = -7f; s <= 7f; s += 0.01f)
            {
                Vector3 u = Bolum1Coordinates.ToUnity(c + nrm * s);
                if (RayDownToModule(u, "SM_B1_Road", out _)) { min = Mathf.Min(min, s); max = Mathf.Max(max, s); }
            }
            if (max > min) widths.Add(max - min + 0.01f);
        }
        float wMin = widths.Min(), wMax = widths.Max(), wAvg = widths.Average();
        Check("(c1) yol genişliği 8 ± 0.2 m", widths.Count == sections.Count && wMin >= 7.8f && wMax <= 8.2f,
            widths.Count + "/" + sections.Count + " kesit, ort " + M(wAvg) + " (min " + M(wMin) + ", max " + M(wMax) + ")");

        // Patika derinliği: her patika örneğinin üstündeki yol (yamaç profili 'slope' top → yol kenarı) yerine,
        // minibüse en yakın yol kesitinin yüzeyi ile minibüs hizasındaki patika yüzeyi karşılaştırılır.
        var path = ((List<object>)probes["path"]).Select(Bolum1Coordinates.Vec3).ToList();
        var depths = new List<string>();
        bool depthOk = true;
        foreach (float x in new[] { -5f, 0f, 4f, 8f })
        {
            Vector3 pp = path.OrderBy(p => Mathf.Abs(p.x - x)).First();
            Vector3 rc = sections.Select(s => Bolum1Coordinates.Vec3(s, "center")).Where(c => c.y < 20f).OrderBy(c => Mathf.Abs(c.x - pp.x)).First();
            if (!RayDownToModule(Bolum1Coordinates.ToUnity(pp), "SM_B1_Ground", out RaycastHit ph) || !RayDownToModule(Bolum1Coordinates.ToUnity(rc), "SM_B1_Road", out RaycastHit rh))
            { depthOk = false; depths.Add("x=" + x + " ışın boş"); continue; }
            float d = rh.point.y - ph.point.y;
            depthOk &= Mathf.Abs(d - 9f) <= 0.6f;
            depths.Add("x=" + pp.x.ToString("F1", Inv) + ": " + M(d));
        }
        Check("(c2) patika yoldan 9 ± 0.6 m aşağıda", depthOk, string.Join(", ", depths));
    }

    // ---------- (d) minibüs arka tekerlekleri ----------
    private static void CheckRearWheels(GameObject van)
    {
        bool ok = true;
        var parts = new List<string>();
        foreach (string w in new[] { "SM_Minivan_Wheel_RL", "SM_Minivan_Wheel_RR" })
        {
            var wheel = van.transform.Find(w);
            float bottom = WorldMinY(wheel.GetComponent<MeshFilter>());
            Vector3 c = wheel.position;
            if (!RayDownIgnoring(new Vector3(c.x, c.y + 2f, c.z), van.transform, out RaycastHit hit)) { ok = false; parts.Add(w + " zemin yok"); continue; }
            float gap = bottom - hit.point.y;
            ok &= Mathf.Abs(gap) <= 0.05f;
            parts.Add(w.Replace("SM_Minivan_", "") + " taban-zemin " + Cm(gap) + " (" + hit.collider.name + ")");
        }
        Check("(d) arka tekerlek tabanları patikada ± 5 cm", ok, string.Join(", ", parts));
    }

    // ---------- (e) karakter boyu ----------
    // Bağ pozunda (edit aşaması) ölçülür ve kontrol edilir: modelin ölçeği. Play aşamasında Idle + IK pozu ayrıca raporlanır.

    private static void CheckCharacter(GameObject character)
    {
        float idleHeight = MeasureHeight(character, out float idleBottom);
        Vector3 p = character.transform.position;
        RayDownIgnoring(new Vector3(p.x, p.y + 1f, p.z), character.transform, out RaycastHit g);
        float idleGap = idleBottom - g.point.y;
        var ik = character.GetComponent<FootGroundingIK>();
        float bindHeight = SessionState.GetFloat("Bolum1Verify.BindHeight", float.NaN);
        Check("(e) karakter boyu 1.50 ± 0.05 m (bağ pozu)", Mathf.Abs(bindHeight - 1.5f) <= 0.05f,
            M(bindHeight) + "; Play'de Idle + IK (" + (ik != null && ik.enabled ? "açık" : "KAPALI") + ", " + SettleSeconds.ToString("F0", Inv) + " sn sonra): boy " + M(idleHeight)
            + ", taban-zemin " + Cm(idleGap) + " (bağ pozunda " + Cm(bindPoseGap) + ")");
    }

    // Pozlanmış mesh'i dünya uzayında geçici bir MeshCollider'a çevirip 1 cm ızgarayla yukarıdan/aşağıdan ışın atar (renderer bounds değil).
    private static float MeasureHeight(GameObject character, out float bottom)
    {
        var temp = BakeToCollider(character, out Bounds b);
        float top = float.MinValue;
        bottom = float.MaxValue;
        for (float x = b.min.x; x <= b.max.x; x += 0.01f)
            for (float z = b.min.z; z <= b.max.z; z += 0.01f)
            {
                if (temp.Raycast(new Ray(new Vector3(x, b.max.y + 1f, z), Vector3.down), out RaycastHit h, 5f)) top = Mathf.Max(top, h.point.y);
                if (temp.Raycast(new Ray(new Vector3(x, b.min.y - 1f, z), Vector3.up), out h, 5f)) bottom = Mathf.Min(bottom, h.point.y);
            }
        UnityEngine.Object.DestroyImmediate(temp.gameObject);
        return top - bottom;
    }

    private static float SoleGap(GameObject character)
    {
        var temp = BakeToCollider(character, out Bounds b);
        float bottom = float.MaxValue;
        for (float x = b.min.x; x <= b.max.x; x += 0.01f)
            for (float z = b.min.z; z <= b.max.z; z += 0.01f)
                if (temp.Raycast(new Ray(new Vector3(x, b.min.y - 1f, z), Vector3.up), out RaycastHit h, 5f)) bottom = Mathf.Min(bottom, h.point.y);
        UnityEngine.Object.DestroyImmediate(temp.gameObject);
        Vector3 p = character.transform.position;
        RayDownIgnoring(new Vector3(p.x, p.y + 1f, p.z), character.transform, out RaycastHit g);
        return bottom - g.point.y;
    }

    private static MeshCollider BakeToCollider(GameObject character, out Bounds bounds)
    {
        var combine = new List<CombineInstance>();
        foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var m = new Mesh();
            smr.BakeMesh(m); // ölçeksiz; ölçek localToWorldMatrix'ten gelir
            // Her alt mesh ayrı eklenmeli; CombineInstance yalnızca subMeshIndex'teki parçayı alır.
            for (int sub = 0; sub < m.subMeshCount; sub++)
                combine.Add(new CombineInstance { mesh = m, subMeshIndex = sub, transform = smr.transform.localToWorldMatrix });
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(combine.ToArray(), true, true);
        var go = new GameObject("Bolum1Verify_TempCharacterCollider");
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        Physics.SyncTransforms();
        bounds = mesh.bounds;
        return mc;
    }

    // ---------- fizik testi (ölç ve raporla, ayar yok) ----------
    private static void PhysicsTest(GameObject van, Vector3 camPos, Vector3 camLook)
    {
        var rb = van.GetComponent<Rigidbody>();
        var oldMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        Physics.SyncTransforms();
        Vector3 start = van.transform.position;
        Line("FİZİK TESTİ (minibüs, 2200 kg, adım 0.02 sn): 5 sn sabit (VehicleHold), sonra ReleaseHold() ve 2 sn serbest");
        var hold = van.GetComponent<VehicleHold>();
        Quaternion startRot = van.transform.rotation;
        float heldDrift = 0f, heldTilt = 0f;
        // Başlangıçta minibüs collider'larının mekâna gömülme derinliği (sıfırdan büyükse fizik ilk karede iter).
        foreach (var c in van.GetComponentsInChildren<Collider>())
        {
            float deepest = 0f; string other = "-";
            foreach (var o in Physics.OverlapBox(c.bounds.center, c.bounds.extents))
            {
                if (o.transform.IsChildOf(van.transform)) continue;
                if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation, out _, out float dist) && dist > deepest)
                { deepest = dist; other = o.name; }
            }
            Line("  başlangıç gömülmesi " + c.GetType().Name + " @" + c.name + ": " + Cm(deepest) + " (" + other + ")");
        }
        Line("  t(s)  kök(x,y,z)                 yer değiştirme  eğim(°)  yuvarlanma(°)  en düşük y   hız(m/s)");
        Capture("physics_t0", camPos, camLook, 45f);
        var coneProbe = new ConePhysicsProbe();
        for (int step = 0; step <= 350; step++)
        {
            if (step == 250)
            {
                // 5 sn doldu: sabit kalma ölçümü, ardından minibüs bırakılır.
                heldDrift = Vector3.Distance(start, van.transform.position);
                heldTilt = Quaternion.Angle(startRot, van.transform.rotation);
                Capture("physics_t5", camPos, camLook, 45f);
                hold.ReleaseHold();
                Line("  >>> ReleaseHold() çağrıldı (IsHeld=" + hold.IsHeld + ")");
            }
            if (step <= 150) coneProbe.Sample();
            if (step == 150) coneProbe.Report();
            if (step % 25 == 0)
            {
                // Minibüsün önü kök uzayında -Y (Blender), yukarısı +Z.
                Vector3 fwd = van.transform.TransformDirection(new Vector3(0f, -1f, 0f));
                Vector3 up = van.transform.TransformDirection(new Vector3(0f, 0f, 1f));
                float pitch = Mathf.Asin(Mathf.Clamp(-fwd.y, -1f, 1f)) * Mathf.Rad2Deg; // + = burun aşağı
                Vector3 right = Vector3.Cross(up, fwd);
                float roll = Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;
                float lowest = van.GetComponentsInChildren<MeshFilter>().Min(WorldMinY);
                Line(string.Format(Inv, "  {0,4:F1}  {1,-26} {2,8:F3} m     {3,6:F1}   {4,8:F1}      {5,8:F3}   {6,6:F2}",
                    step * 0.02f, V(van.transform.position), Vector3.Distance(start, van.transform.position), pitch, roll, lowest, rb.linearVelocity.magnitude));
            }
            if (step < 350) Physics.Simulate(0.02f);
        }
        float freeMove = Vector3.Distance(van.transform.position, start);
        Capture("physics_t7", camPos, camLook, 45f);
        Check("(h) 5 sn sonra minibüs sabit (konum < 1 cm, eğim < 0.5°)", heldDrift < 0.01f && heldTilt < 0.5f,
            "konum farkı " + Cm(heldDrift) + ", dönme farkı " + heldTilt.ToString("F3", Inv) + "°");
        Check("(i) ReleaseHold sonrası 2 sn içinde minibüs hareket ediyor (> 0.5 m)", freeMove > 0.5f,
            "yer değiştirme " + freeMove.ToString("F2", Inv) + " m (bilgi: serbest kalınca öne devrilip düşmesi bilinen durum)");
        Physics.simulationMode = oldMode;
    }

    // ---------- (f) derleme hatası ----------
    private static void CheckCompileErrors()
    {
        string log = Application.consoleLogPath;
        bool clean = true;
        string note = "log: " + log;
        try
        {
            using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                clean = !sr.ReadToEnd().Contains("error CS");
        }
        catch (Exception e) { clean = false; note += " okunamadı: " + e.Message; }
        // Etikette aranan dizeyi yazmıyoruz; yoksa rapor log'a basılınca sonraki taramalar kendini bulur.
        Check("(f) log'da C# derleme hatası yok", clean, note);
    }

    // ---------- yardımcılar ----------
    private static bool RayDownToModule(Vector3 at, string module, out RaycastHit result)
    {
        var target = GameObject.Find(module).transform;
        foreach (var h in Physics.RaycastAll(new Vector3(at.x, at.y + 30f, at.z), Vector3.down, 60f).OrderBy(h => h.distance))
            if (h.transform.IsChildOf(target)) { result = h; return true; }
        result = default;
        return false;
    }

    private static bool RayDownIgnoring(Vector3 from, Transform ignore, out RaycastHit result)
    {
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 20f).OrderBy(h => h.distance))
            if (!h.transform.IsChildOf(ignore)) { result = h; return true; }
        result = default;
        return false;
    }

    private static float WorldMinY(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null) return float.MaxValue;
        float min = float.MaxValue;
        var m = mf.transform.localToWorldMatrix;
        foreach (var v in mf.sharedMesh.vertices) min = Mathf.Min(min, m.MultiplyPoint3x4(v).y);
        return min;
    }

    private static void Capture(string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("Bolum1Verify_CaptureCam");
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = main != null ? main.backgroundColor : Color.gray;
        cam.farClipPlane = 2000f;
        cam.fieldOfView = fov;
        go.transform.position = pos;
        go.transform.LookAt(look);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(go);
        Line("  görüntü: " + OutDir + "/" + name + ".png");
    }

    private static void Check(string label, bool ok, string detail)
    {
        allOk &= ok;
        Line((ok ? "[OK]   " : "[FAIL] ") + label + " — " + detail);
    }

    // NpcBody yalnız yerde yatan hastada ve ayaktaki botta katıdır (D2: minibüs koltuğundaki hasta katı değil).
    private static bool SolidExpected(NpcBody b)
    {
        var p = b.GetComponent<Patient>();
        if (p != null) return p.State == PatientState.LyingGround;
        var bot = b.GetComponent<StretcherHelperBot>();
        return bot != null && !bot.IsDown;
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
    private static string M(float m) { return m.ToString("F3", Inv) + " m"; }
    private static string Cm(float m) { return (m * 100f).ToString("F1", Inv) + " cm"; }
}
