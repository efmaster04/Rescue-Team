using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CompanySim;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Garajı üç katına büyütür: garajın doğusuna "Kurtarma Hangarı" (iç alan 22,4 × 9,0 m ≈ 202 m²; garaj 96,7 m² → toplam ≈ 3,1 kat).
// Menü: Rescue Team > Ofis-Garaj > Garajı Büyüt: Kurtarma Hangarı (yeniden çalıştırılabilir) / Kurtarma Hangarını Kaldır.
//  - Garajın doğu duvarında (A02 #5 dilimi, z 0,8–3,8) z 2,45–3,75 arası 2,5 m yüksek geçiş açılır; dilim gizlenip aynı malzemeyle
//    geçişin iki yanı ve lentosu yeniden kurulur. Garage_East çarpıştırıcısı bölünür, hava hortumu makarası 1,3 m kuzeye alınır;
//    geçişin önündeki zemin sandığı kuzeydoğu köşeye, varilin üstündeki yedek lastik varilin ortasına kayar (Kaldır geri koyar).
//  - Hangar: 5,4 m saçak, beşik çatı (mahya 6,4 m), çelik makaslar ve kolonlar, ışıklıklar, güneyde kapalı sarmal kapı, doğuda açık
//    araç kapısı, pencereler, epoksi zemin ve zemin çizgileri, 10 LED armatür, yansıma sondası.
//  - İçerik: kurtarma konvoyu (drone aracı + römork + bot, RescueGearAssembly), kurtarma ekipmanı duvarı, erzak paletleri ve mola
//    masası (yiyecek-içecek seti), drone iniş matı, alet tezgâhı (elektrikli aletler), köpek Fındık (KopekDavranisi) ve köşesi.
//  - Hangar alanına düşen çevre parçaları (ağaçlar, çalılar, kayalar, konteyner, tahtalar) gizlenir; liste hangar_hidden_parts.txt.
// Üretilen meshler okunamaz kaydedilir (GarageRuntimeOptimization statik birleştirmesine girmez). Adlarda "Ceiling" geçmez.
public static class GarageHangarBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
    public const string RootName = "12_KurtarmaHangari";
    public const string ArtDir = "Assets/_Project/Art/Hangar";
    public const string MatDir = ArtDir + "/Materials";
    public const string MeshPath = ArtDir + "/Hangar_Meshes.asset";
    public const string HiddenList = ArtDir + "/hangar_hidden_parts.txt";
    public const string ProbePath = "Assets/_Project/Art/Lighting/HangarReflection.exr";
    public const string DogControllerPath = "Assets/_Project/Art/Models/RescueGear/Kopek/Kopek.controller";
    public const string EnvRoot = "01_Environment_OfficeGarage";

    // hangar (iç yüzler) ve yapı ölçüleri
    public const float X0 = 9.39f, X1 = 31.8f, Z0 = -5.2f, Z1 = 3.8f, T = 0.25f, H = 5.4f, Ridge = 6.4f, ZR = -0.7f;
    // garaj doğu duvarındaki geçiş
    public const float PZ0 = 2.45f, PZ1 = 3.75f, PH = 2.5f;
    // kapılar ve pencereler
    public const float D1X0 = 12.6f, D1X1 = 16.4f, D1H = 4.2f;                 // güney sarmal kapı (kapalı)
    public const float D2Z0 = -2.95f, D2Z1 = 1.55f, D2H = 4.6f;                // doğu araç kapısı (açık)
    public static readonly float[] TrussX = { 12.1f, 16.6f, 21.1f, 25.6f, 30.1f };
    public static readonly Vector2[] WindowsX = { new Vector2(17.65f, 20.05f), new Vector2(22.15f, 24.55f), new Vector2(26.65f, 29.05f) };
    // köpeğin serbest gezinme alanı (x, z) — kurulum burayı boş bırakır
    public static readonly Vector2 DogMin = new Vector2(13.4f, -1.4f), DogMax = new Vector2(17.2f, 2.9f);
    public static readonly Vector3 ConvoyOrigin = new Vector3(27.25f, 0f, -0.3f);

    static Transform root, geo, col, props;
    static Dictionary<Material, HM> groups;
    static StringBuilder report;
    static int lightCount;

    [MenuItem("Rescue Team/Ofis-Garaj/Garajı Büyüt: Kurtarma Hangarı")]
    public static void Menu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        EditorUtility.DisplayDialog("Kurtarma Hangarı", report.ToString(), "Tamam");
    }

    [MenuItem("Rescue Team/Ofis-Garaj/Kurtarma Hangarını Kaldır")]
    public static void RemoveMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        Remove();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void BuildBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("GarageHangarBuilder: " + e); EditorApplication.Exit(1); }
    }

    public static void Build()
    {
        report = new StringBuilder();
        lightCount = 0;
        Directory.CreateDirectory(MatDir);
        var scene = EditorSceneManager.OpenScene(ScenePath);
        Remove();

        // malzemeler: dış cephe garajın kendi sıva/bant/taban malzemeleri (geçiş dilimi gizlenmeden önce okunur)
        var wallPart = EnvRenderers().Where(r => InWallSlice(r.bounds)).ToList();
        Material plaster = PickMat(wallPart, "plastered masonry"), band = PickMat(wallPart, "blue lower painted band"),
                 baseMat = PickMat(wallPart, "lower concrete base"), coping = PickMat(EnvRenderers().Where(r => r.name.StartsWith("A05_masonry coping") && r.bounds.min.x > 9f).ToList(), "coping");
        if (plaster == null || band == null) throw new Exception("Garaj duvar malzemeleri bulunamadı (A02 #5 dilimi)");

        HideParts();

        root = new GameObject(RootName).transform;
        geo = Child(root, "Yapi");
        col = Child(root, "Carpisma");
        props = Child(root, "Donanim");
        groups = new Dictionary<Material, HM>();

        var M = new Mats(plaster, band, baseMat ?? plaster, coping ?? plaster);
        BuildPassage(M);
        BuildShell(M);
        BuildRoof(M);
        BuildStructure(M);
        BuildDoorsAndWindows(M);
        BuildFloorMarks(M);
        BuildLights(M);
        BuildEquipmentWall(M);
        BuildSuppliesAndBreak(M);
        BuildWorkshop(M);
        BuildDogCorner(M);
        BuildConvoy();
        BuildSigns(M);
        int meshCount = SaveMeshes();
        BuildProbe();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.Insert(0, "KURTARMA HANGARI kuruldu: iç alan " + ((X1 - X0) * (Z1 - Z0)).ToString("F0") + " m², " + meshCount + " malzeme grubu, " + lightCount + " ışık\n");
        Directory.CreateDirectory("Captures/Hangar");
        File.WriteAllText("Captures/Hangar/hangar_build_report.txt", report.ToString());
        Debug.Log(report.ToString());
    }

    // ------------------------------------------------------------------ geri alma / gizleme

    static IEnumerable<Renderer> EnvRenderers()
    {
        var env = GameObject.Find(EnvRoot);
        return env != null ? env.GetComponentsInChildren<Renderer>(true) : Enumerable.Empty<Renderer>();
    }

    // garajın doğu duvarındaki A02 #5 dilimi (x 9,1–9,4, z 0,8–3,8) ve kaplaması
    static bool InWallSlice(Bounds b) => b.min.x > 9.05f && b.max.x < 9.45f && Mathf.Abs(b.min.z - 0.8f) < 0.05f && Mathf.Abs(b.max.z - 3.8f) < 0.05f;

    // hangar kabuğuyla (duvar dış yüzleri dahil) kesişen, zeminden yüksek çevre parçası; dışarıdan duvara giren çalılar da sayılır
    static bool InFootprint(Bounds b)
        => b.max.x > X0 + 0.01f && b.min.x < X1 + T + 0.6f && b.max.z > Z0 - T - 0.4f && b.min.z < Z1 + T + 0.02f
           && b.max.y > 0.02f && b.size.x < 30f && b.size.z < 30f;

    static void HideParts()
    {
        var hidden = new List<string>();
        foreach (var r in EnvRenderers().ToList())
        {
            if (!r.gameObject.activeSelf) continue;
            var b = r.bounds;
            bool slice = InWallSlice(b) || (r.name.StartsWith("A05_masonry coping") && Mathf.Abs(b.min.z - 0.8f) < 0.05f && b.min.x > 9f);
            if (!slice && !InFootprint(b)) continue;
            if (!slice && b.min.x < X0 - 0.05f) continue; // garajın kendi parçaları (ayaklar, sayaç kutusu) kalır
            r.gameObject.SetActive(false);
            hidden.Add(PathOf(r.transform));
        }
        File.WriteAllLines(HiddenList, hidden);
        AssetDatabase.ImportAsset(HiddenList);
        report.AppendLine("Gizlenen çevre parçası: " + hidden.Count + " (" + HiddenList + ")");

        // iç cephe: duvar başlığı rayı geçişi keser → gizlenip iki parça olarak yeniden kurulur; hortum makarası kuzeye
        var rail = GameObject.Find("11_WorkshopInterior/RightWallCapRail");
        if (rail != null) rail.SetActive(false);
        var reel = GameObject.Find("11_WorkshopInterior/WallAirHoseReel");
        if (reel != null)
        {
            var rb = BoundsOf(reel);
            if (rb.center.z > PZ0 - 0.6f && rb.center.z < PZ1 + 0.3f) { reel.transform.position += new Vector3(0f, 0f, 4.45f - rb.center.z); report.AppendLine("Hava hortumu makarası z " + rb.center.z.ToString("F2") + " → 4,45"); }
        }
        // Garage_East çarpıştırıcısı geçişin güneyinde biter; kuzey parçası ve lento hangar kökünde
        var east = GameObject.Find("00_WorldCollision/Garage_East");
        if (east != null) { var bc = east.GetComponent<BoxCollider>(); east.transform.position = new Vector3(9.25f, 2f, (-5.2f + PZ0) * 0.5f); east.transform.localScale = Vector3.one; if (bc != null) { bc.center = Vector3.zero; bc.size = new Vector3(0.28f, 4f, PZ0 + 5.2f); } }
        MoveCornerProps(true);
    }

    // Geçişin garaj tarafı: zemin sandığı (17_GarageStorage/FloorStorageCase + Garage_DepotTrunk) geçişin kuzey yarısının tam
    // önündeydi; tezgâhın ucuyla arasında yalnız 0,7 m kalıyordu. Sandık aynı yönüyle kuzeydoğu köşeye (raf 03 ile doğu duvarı
    // arası, hortum makarasının altı) alınır; mor varilin üstündeki yedek lastik varilin ortasına kayar (geçiş hattından çıkar).
    static readonly Vector3 CaseHome = new Vector3(8.15f, 0.33f, 3.425f), CaseAway = new Vector3(8.40f, 0.33f, 4.42f);
    static readonly Vector3 TireHome = new Vector3(7.55f, 0.42f, 3.18f), TireAway = new Vector3(7.45f, 0.42f, 3.46f);

    static void MoveCornerProps(bool away)
    {
        Vector3 from = away ? CaseHome : CaseAway, to = away ? CaseAway : CaseHome;
        var box = GameObject.Find("17_GarageStorage/FloorStorageCase");
        if (box != null)
        {
            var c = BoundsOf(box).center;
            if (Mathf.Abs(c.x - from.x) < 0.08f && Mathf.Abs(c.z - from.z) < 0.08f)
            {
                box.transform.position += new Vector3(to.x - c.x, 0f, to.z - c.z);
                if (away) report.AppendLine("Zemin sandığı (" + c.x.ToString("F2") + ", " + c.z.ToString("F2") + ") → (" + to.x.ToString("F2") + ", " + to.z.ToString("F2") + ")");
            }
        }
        var trunk = GameObject.Find("00_WorldCollision/Garage_DepotTrunk");
        if (trunk != null && Vector3.Distance(trunk.transform.position, from) < 0.08f) trunk.transform.position = to;
        var tire = GameObject.Find("06_PhysicalEquipment/Equipment_tire_01");
        Vector3 tf = away ? TireHome : TireAway, tt = away ? TireAway : TireHome;
        if (tire != null)
        {
            var p = tire.transform.position;
            if (Mathf.Abs(p.x - tf.x) < 0.05f && Mathf.Abs(p.z - tf.z) < 0.05f) tire.transform.position = new Vector3(tt.x, p.y, tt.z);
        }
    }

    public static void Remove()
    {
        var old = GameObject.Find(RootName);
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        if (File.Exists(HiddenList))
        {
            foreach (var p in File.ReadAllLines(HiddenList))
            {
                var t = FindPath(p);
                if (t != null) t.gameObject.SetActive(true);
            }
        }
        var rail = FindPath("11_WorkshopInterior/RightWallCapRail");
        if (rail != null) rail.gameObject.SetActive(true);
        var reel = GameObject.Find("11_WorkshopInterior/WallAirHoseReel");
        if (reel != null)
        {
            var rb = BoundsOf(reel);
            if (Mathf.Abs(rb.center.z - 4.45f) < 0.05f) reel.transform.position += new Vector3(0f, 0f, 3.155f - rb.center.z);
        }
        var east = GameObject.Find("00_WorldCollision/Garage_East");
        if (east != null) { var bc = east.GetComponent<BoxCollider>(); east.transform.position = new Vector3(9.25f, 2f, 0f); if (bc != null) { bc.center = Vector3.zero; bc.size = new Vector3(0.28f, 4f, 10.4f); } }
        if (report == null) report = new StringBuilder();
        MoveCornerProps(false);
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var c = t; c != null; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts);
    }

    static Transform FindPath(string path)
    {
        var parts = path.Split('/');
        GameObject top = null;
        foreach (var g in EditorSceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == parts[0]) { top = g; break; }
        if (top == null) return null;
        Transform t = top.transform;
        for (int i = 1; i < parts.Length && t != null; i++)
        {
            Transform next = null;
            foreach (Transform ch in t) if (ch.name == parts[i]) { next = ch; break; }
            t = next;
        }
        return t;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    static Material PickMat(List<Renderer> rs, string key)
    {
        foreach (var r in rs) if (r.name.Contains(key) && r.sharedMaterial != null) return r.sharedMaterial;
        return null;
    }

    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    // ------------------------------------------------------------------ malzemeler

    sealed class Mats
    {
        public Material plaster, band, baseConcrete, coping, floor, panel, panelSeam, blueLining, steel, steelLight, roof, roofDeck, skylight,
            glass, slats, yellow, white, black, wood, pallet, cardboard, water, waterCap, orange, red, green, rubber, pegboard, led, bedFabric, bedCushion,
            board, cream, blueSign, exitGreen, grate, tyre;
        public Mats(Material plaster, Material band, Material baseConcrete, Material coping)
        {
            this.plaster = plaster; this.band = band; this.baseConcrete = baseConcrete; this.coping = coping;
            string G = OfficeImportSettings.GarageTextures, C = OfficeImportSettings.CraftTextures, D = OfficeImportSettings.DetailFolder;
            floor = Mat("Hangar_EpoksiZemin", new Color(0.56f, 0.58f, 0.58f), 0f, 0.42f, tex: G + "Epoxy_Albedo.png", normal: G + "Epoxy_Normal.png", tiling: 0.35f, detail: D + "OfficeDetail_Concrete.png", detailTiling: 0.6f);
            panel = Mat("Hangar_IcPanel", new Color(0.83f, 0.84f, 0.82f), 0.05f, 0.28f, normal: G + "Paint_Normal.png", tiling: 0.5f, detail: D + "OfficeDetail_Paint.png", detailTiling: 0.7f);
            panelSeam = Mat("Hangar_PanelFuga", new Color(0.62f, 0.64f, 0.65f), 0.2f, 0.3f);
            blueLining = Mat("Hangar_MaviAltPanel", new Color(0.19f, 0.33f, 0.45f), 0.05f, 0.3f, normal: G + "Paint_Normal.png", tiling: 0.6f);
            steel = Mat("Hangar_Celik", new Color(0.2f, 0.22f, 0.25f), 0.55f, 0.42f, normal: G + "PowderCoat_Normal.png", tiling: 1f);
            steelLight = Mat("Hangar_FircaCelik", new Color(0.62f, 0.64f, 0.66f), 0.75f, 0.5f, tex: G + "BrushedSteel_Albedo.png", normal: G + "BrushedSteel_Normal.png", tiling: 1f);
            roof = Mat("Hangar_CatiSac", new Color(0.27f, 0.33f, 0.38f), 0.45f, 0.38f);
            roofDeck = Mat("Hangar_CatiAltSac", new Color(0.72f, 0.74f, 0.76f), 0.35f, 0.32f, normal: G + "BrushedSteel_Normal.png", tiling: 0.8f);
            skylight = Mat("Hangar_Isiklik", new Color(0.86f, 0.92f, 1f), 0f, 0.6f, emission: new Color(0.78f, 0.86f, 1f) * 1.35f);
            glass = Mat("Hangar_Cam", new Color(0.62f, 0.74f, 0.8f, 0.28f), 0.1f, 0.9f, alpha: 0.28f);
            slats = Mat("Hangar_KapiLameli", new Color(0.66f, 0.7f, 0.74f), 0.6f, 0.45f, tex: G + "BrushedSteel_Albedo.png", normal: G + "BrushedSteel_Normal.png", tiling: 0.8f);
            yellow = Mat("Hangar_SariCizgi", new Color(0.96f, 0.76f, 0.12f), 0f, 0.45f);
            white = Mat("Hangar_BeyazCizgi", new Color(0.93f, 0.93f, 0.9f), 0f, 0.42f);
            black = Mat("Hangar_Siyah", new Color(0.08f, 0.08f, 0.09f), 0.1f, 0.35f);
            wood = Mat("Hangar_Ahsap", new Color(0.66f, 0.48f, 0.3f), 0f, 0.3f, tex: null, detail: D + "OfficeDetail_Wood.png", detailTiling: 2f);
            pallet = Mat("Hangar_Palet", new Color(0.72f, 0.58f, 0.4f), 0f, 0.18f, detail: D + "OfficeDetail_Wood.png", detailTiling: 2.5f);
            cardboard = Mat("Hangar_Karton", new Color(0.72f, 0.56f, 0.36f), 0f, 0.15f, tex: C + "Cardboard_Albedo.png", normal: C + "Cardboard_Normal.png", tiling: 2f);
            water = Mat("Hangar_SuPaketi", new Color(0.55f, 0.75f, 0.9f), 0f, 0.7f);
            waterCap = Mat("Hangar_SuKapagi", new Color(0.15f, 0.42f, 0.82f), 0f, 0.5f);
            orange = Mat("Hangar_Turuncu", new Color(0.95f, 0.45f, 0.1f), 0f, 0.4f);
            red = Mat("Hangar_Kirmizi", new Color(0.78f, 0.1f, 0.08f), 0.1f, 0.55f);
            green = Mat("Hangar_Yesil", new Color(0.2f, 0.5f, 0.3f), 0f, 0.35f);
            rubber = Mat("Hangar_Kaucuk", new Color(0.13f, 0.13f, 0.14f), 0f, 0.2f, normal: G + "RibbedRubber_Normal.png", tiling: 2f);
            pegboard = Mat("Hangar_DelikliPano", new Color(0.82f, 0.8f, 0.74f), 0f, 0.3f, tex: G + "Pegboard_Albedo.png", normal: G + "Pegboard_Normal.png", tiling: 2f);
            led = Mat("Hangar_LED", new Color(1f, 0.98f, 0.94f), 0f, 0.5f, emission: new Color(1f, 0.96f, 0.9f) * 2.6f);
            bedFabric = Mat("Hangar_YatakKumas", new Color(0.24f, 0.4f, 0.5f), 0f, 0.12f, tex: C + "Cloth_Albedo.png", normal: C + "Cloth_Normal.png", tiling: 3f);
            bedCushion = Mat("Hangar_YatakMinder", new Color(0.9f, 0.82f, 0.66f), 0f, 0.1f, tex: C + "Cloth_Albedo.png", normal: C + "Cloth_Normal.png", tiling: 3f);
            board = Mat("Hangar_EkipmanPanosu", new Color(0.22f, 0.3f, 0.36f), 0.3f, 0.35f, normal: G + "PowderCoat_Normal.png", tiling: 1.5f);
            cream = Mat("Hangar_Krem", new Color(0.93f, 0.9f, 0.82f), 0f, 0.35f);
            blueSign = Mat("Hangar_MaviTabela", new Color(0.12f, 0.3f, 0.52f), 0.2f, 0.45f);
            exitGreen = Mat("Hangar_CikisYesili", new Color(0.1f, 0.62f, 0.3f), 0f, 0.4f, emission: new Color(0.1f, 0.75f, 0.35f) * 1.4f);
            grate = Mat("Hangar_Izgara", new Color(0.3f, 0.31f, 0.33f), 0.7f, 0.4f, tex: G + "BrushedSteel_Albedo.png", tiling: 4f);
            tyre = Mat("Hangar_Lastik", new Color(0.12f, 0.12f, 0.12f), 0f, 0.25f, normal: C + "Tyre_Normal.png", tiling: 2f);
        }
    }

    static Material Mat(string name, Color color, float metallic, float smooth, string tex = null, string normal = null, float tiling = 1f,
                        string detail = null, float detailTiling = 1f, Color? emission = null, float alpha = 1f)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        var t = tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(tex) : null;
        m.SetTexture("_BaseMap", t);
        m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        var n = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
        m.SetTexture("_BumpMap", n);
        if (n != null) { m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.6f); } else m.DisableKeyword("_NORMALMAP");
        var d = detail != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(detail) : null;
        m.SetTexture("_DetailAlbedoMap", d);
        if (d != null) { m.SetTextureScale("_DetailAlbedoMap", new Vector2(detailTiling, detailTiling)); m.SetFloat("_DetailAlbedoMapScale", 1f); m.EnableKeyword("_DETAIL_MULX2"); }
        else m.DisableKeyword("_DETAIL_MULX2");
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
        if (alpha < 1f)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetShaderPassEnabled("ShadowCaster", false); m.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            m.SetFloat("_Surface", 0f); m.SetFloat("_ZWrite", 1f); m.SetOverrideTag("RenderType", "Opaque"); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetShaderPassEnabled("ShadowCaster", true); m.renderQueue = -1;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ mesh toplayıcı (dünya düzlemi UV'li, düz gölgeli)

    sealed class HM
    {
        public readonly List<Vector3> V = new List<Vector3>(); public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> U = new List<Vector2>(); public readonly List<int> T = new List<int>();
        Matrix4x4 m = Matrix4x4.identity; readonly Stack<Matrix4x4> st = new Stack<Matrix4x4>();
        public void Push(Vector3 p, Quaternion r) { st.Push(m); m = m * Matrix4x4.TRS(p, r, Vector3.one); }
        public void Pop() { m = st.Pop(); }

        static Vector2 UV(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2(p.x, p.z);
            if (ax >= az) return new Vector2(p.z, p.y);
            return new Vector2(p.x, p.y);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b); c = m.MultiplyPoint3x4(c); d = m.MultiplyPoint3x4(d);
            n = m.MultiplyVector(n).normalized;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = d; d = t; }
            int i = V.Count;
            foreach (var p in new[] { a, b, c, d }) { V.Add(p); N.Add(n); U.Add(UV(p, n)); }
            T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b); c = m.MultiplyPoint3x4(c); n = m.MultiplyVector(n).normalized;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = c; c = t; }
            int i = V.Count;
            foreach (var p in new[] { a, b, c }) { V.Add(p); N.Add(n); U.Add(UV(p, n)); }
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public const int PX = 1, NX = 2, PY = 4, NY = 8, PZ = 16, NZ = 32;

        public void Box(Vector3 c, Vector3 s, int skip = 0)
        {
            Vector3 h = s * 0.5f;
            float x0 = c.x - h.x, x1 = c.x + h.x, y0 = c.y - h.y, y1 = c.y + h.y, z0 = c.z - h.z, z1 = c.z + h.z;
            if ((skip & PX) == 0) Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right);
            if ((skip & NX) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left);
            if ((skip & PY) == 0) Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up);
            if ((skip & NY) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down);
            if ((skip & PZ) == 0) Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward);
            if ((skip & NZ) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back);
        }

        public void MinMax(float x0, float y0, float z0, float x1, float y1, float z1, int skip = 0)
            => Box(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), skip);

        // a→b doğrultusunda w × h kesitli kiriş
        public void Beam(Vector3 a, Vector3 b, float w, float h)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 1e-4f) return;
            Vector3 up = Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            Push((a + b) * 0.5f, Quaternion.LookRotation(d / len, up));
            Box(Vector3.zero, new Vector3(w, h, len));
            Pop();
        }

        // dik silindir (taban merkezi c)
        public void Cyl(Vector3 c, float r, float h, int seg, bool top = true, bool bottom = false, float rz = -1f)
        {
            if (rz < 0f) rz = r;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * rz), p1 = c + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * rz);
                Vector3 n = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                Quad(p0, p1, p1 + Vector3.up * h, p0 + Vector3.up * h, n);
                if (top) Tri(c + Vector3.up * h, p0 + Vector3.up * h, p1 + Vector3.up * h, Vector3.up);
                if (bottom) Tri(c, p1, p0, Vector3.down);
            }
        }

        // halka (yatay torus yerine sekizgen kesitli halka kutuları)
        public void Ring(Vector3 c, float r, float thick, float h, int seg, float rz = -1f)
        {
            if (rz < 0f) rz = r;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 o0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * rz), o1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * rz);
                Vector3 i0 = new Vector3(Mathf.Cos(a0) * (r - thick), 0f, Mathf.Sin(a0) * (rz - thick)), i1 = new Vector3(Mathf.Cos(a1) * (r - thick), 0f, Mathf.Sin(a1) * (rz - thick));
                Vector3 n = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f)), up = Vector3.up * h;
                Quad(c + o0, c + o1, c + o1 + up, c + o0 + up, n);
                Quad(c + i0, c + i1, c + i1 + up, c + i0 + up, -n);
                Quad(c + o0 + up, c + o1 + up, c + i1 + up, c + i0 + up, Vector3.up);
            }
        }

        // yarım küre (kask, kubbe)
        public void Dome(Vector3 c, float r, float ry, int seg, int rings)
        {
            for (int j = 0; j < rings; j++)
            {
                float t0 = j * Mathf.PI * 0.5f / rings, t1 = (j + 1) * Mathf.PI * 0.5f / rings;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    Vector3 P(float a, float t) => c + new Vector3(Mathf.Cos(a) * Mathf.Cos(t) * r, Mathf.Sin(t) * ry, Mathf.Sin(a) * Mathf.Cos(t) * r);
                    Vector3 p00 = P(a0, t0), p10 = P(a1, t0), p11 = P(a1, t1), p01 = P(a0, t1);
                    Vector3 n = ((p00 + p10 + p11 + p01) * 0.25f - c).normalized;
                    if (j == rings - 1) Tri(p00, p10, p11, n); else Quad(p00, p10, p11, p01, n);
                }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, U); mesh.SetTriangles(T, 0, true);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }

    static HM G(Material m)
    {
        if (!groups.TryGetValue(m, out var h)) { h = new HM(); groups[m] = h; }
        return h;
    }

    static void Box(Material m, float x0, float y0, float z0, float x1, float y1, float z1, int skip = 0) => G(m).MinMax(x0, y0, z0, x1, y1, z1, skip);

    static BoxCollider Collider(string name, float x0, float y0, float z0, float x1, float y1, float z1, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent ?? col, false);
        go.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        var bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        return bc;
    }

    static int SaveMeshes()
    {
        var meshes = new List<Mesh>();
        foreach (var kv in groups)
        {
            if (kv.Value.V.Count == 0) continue;
            var mesh = kv.Value.ToMesh("Hangar_" + kv.Key.name);
            meshes.Add(mesh);
            var part = new GameObject("Hangar_" + kv.Key.name);
            part.transform.SetParent(geo, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = kv.Key;
            bool emissive = kv.Key.IsKeywordEnabled("_EMISSION");
            bool glass = kv.Key.name.Contains("Cam");
            mr.shadowCastingMode = glass || emissive || kv.Key.name.Contains("Cizgi") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            mr.receiveShadows = !emissive;
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
        }
        if (File.Exists(MeshPath)) AssetDatabase.DeleteAsset(MeshPath);
        if (meshes.Count > 0)
        {
            AssetDatabase.CreateAsset(meshes[0], MeshPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
            // çalışma anında okunamaz: GarageRuntimeOptimization bunları statik birleştirmeye katmaz
            foreach (var m in meshes) { var so = new SerializedObject(m); so.FindProperty("m_IsReadable").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.SaveAssets();
        }
        int verts = meshes.Sum(m => m.vertexCount), tris = meshes.Sum(m => (int)m.GetIndexCount(0) / 3);
        report.AppendLine("Hangar meshleri: " + meshes.Count + " grup, " + verts + " köşe, " + tris + " üçgen (" + MeshPath + ")");
        return meshes.Count;
    }

    // ------------------------------------------------------------------ garaj duvarındaki geçiş

    static void BuildPassage(Mats M)
    {
        // duvar gövdesi (sıva) x 9,13–9,37; alt beton taban 0–0,22; iki yüzde mavi bant 0–0,96; üstte harpuşta 3,97–4,07
        float wx0 = 9.13f, wx1 = 9.37f;
        Box(M.plaster, wx0, 0f, 0.8f, wx1, 4f, PZ0);
        Box(M.plaster, wx0, PH, PZ0, wx1, 4f, PZ1, HM.NY ^ HM.NY);
        Box(M.plaster, wx0, 0f, PZ1, wx1, 4f, 3.8f);
        foreach (var (a, b) in new[] { (0.8f, PZ0), (PZ1, 3.8f) })
        {
            Box(M.baseConcrete, 9.12f, 0f, a, 9.39f, 0.22f, b);
            Box(M.band, 9.12f, 0f, a, 9.13f, 0.96f, b);
            Box(M.band, 9.37f, 0f, a, 9.38f, 0.96f, b);
        }
        Box(M.coping, 9.13f, 3.97f, 0.8f, 9.38f, 4.07f, 3.8f);
        // kapı kasası (çelik, iki yüz) ve eşik
        foreach (float x in new[] { 9.08f, 9.36f })
        {
            Box(M.steel, x, 0f, PZ0 - 0.06f, x + 0.06f, PH + 0.06f, PZ0);
            Box(M.steel, x, 0f, PZ1, x + 0.06f, PH + 0.06f, PZ1 + 0.06f);
            Box(M.steel, x, PH, PZ0 - 0.06f, x + 0.06f, PH + 0.06f, PZ1 + 0.06f);
        }
        Box(M.grate, 9.11f, -0.02f, PZ0, 9.42f, 0.006f, PZ1);
        // duvar başlığı rayı iki parça (garaj iç yüzü)
        var rail = FindPath("11_WorkshopInterior/RightWallCapRail");
        var railMat = rail != null ? rail.GetComponent<Renderer>()?.sharedMaterial : null;
        if (railMat != null)
        {
            Box(railMat, 9.08f, 1.09f, -5.06f, 9.11f, 1.12f, PZ0 - 0.06f);
            Box(railMat, 9.08f, 1.09f, PZ1 + 0.06f, 9.11f, 1.12f, 5.06f);
        }
        Collider("Gecis_KuzeyDuvar", 9.11f, 0f, PZ1, 9.39f, 4f, 5.2f);
        Collider("Gecis_Lento", 9.11f, PH, PZ0, 9.39f, 4f, PZ1);
        report.AppendLine("Geçiş: garaj doğu duvarı z " + PZ0 + "–" + PZ1 + ", yükseklik " + PH + " m");
    }

    // ------------------------------------------------------------------ kabuk: zemin, duvarlar, iç kaplama

    static void BuildShell(Mats M)
    {
        float xe = X1 + T, zs = Z0 - T, zn = Z1 + T;
        // zemin plakası (üst y 0; dışta çimden 0,28 m yükselen kenar)
        Box(M.floor, X0, -0.32f, zs, xe, 0f, zn, HM.NY);
        Box(M.baseConcrete, X0, -0.33f, zs - 0.06f, xe + 0.06f, 0.02f, zs, HM.NY);       // güney ve doğu dış kenar bordürü
        Box(M.baseConcrete, xe, -0.33f, zs, xe + 0.06f, 0.02f, zn + 0.06f, HM.NY);
        Box(M.baseConcrete, X0, -0.33f, zn, xe, 0.02f, zn + 0.06f, HM.NY);

        // kuzey duvarı (pencereli: üst sıra), güney duvarı (kapı + pencereler), doğu alın duvarı (araç kapısı)
        WallX(M, Z1, zn, X0, xe, new[] { (WindowsX[0].x, WindowsX[0].y, 3.4f, 4.6f), (WindowsX[1].x, WindowsX[1].y, 3.4f, 4.6f), (WindowsX[2].x, WindowsX[2].y, 3.4f, 4.6f) }, insideNegZ: true);
        WallX(M, zs, Z0, X0, xe, new[] { (D1X0, D1X1, 0f, D1H), (WindowsX[0].x, WindowsX[0].y, 2.6f, 4.3f), (WindowsX[1].x, WindowsX[1].y, 2.6f, 4.3f), (WindowsX[2].x, WindowsX[2].y, 2.6f, 4.3f) }, insideNegZ: false);
        WallZ(M, X1, xe, zs, zn, (D2Z0, D2Z1, 0f, D2H));
        // batı üst duvarı (garaj duvarının üstünde, 4,07 m'den saçağa) + iki alın üçgeni
        Box(M.plaster, X0, 4.07f, zs, X0 + 0.22f, H, zn);
        Box(M.panel, X0 + 0.22f, 4.07f, Z0, X0 + 0.24f, H, Z1);
        Gable(M.plaster, X0, X0 + 0.22f, zs, zn);
        Gable(M.plaster, X1, xe, zs, zn);
        Gable(M.panel, X0 + 0.22f, X0 + 0.24f, Z0, Z1);
        Gable(M.panel, X1 - 0.02f, X1, Z0, Z1);

        // çarpıştırıcılar: duvarlar (güney kapı kapalı olduğundan tek kutu), doğu kapının iki yanı ve lentosu, batı üst duvarı
        Collider("Duvar_Kuzey", X0, 0f, Z1, xe, H, zn);
        Collider("Duvar_Guney", X0, 0f, zs, xe, H, Z0);
        Collider("Duvar_Dogu_G", X1, 0f, zs, xe, H, D2Z0);
        Collider("Duvar_Dogu_K", X1, 0f, D2Z1, xe, H, zn);
        Collider("Duvar_Dogu_Lento", X1, D2H, D2Z0, xe, H, D2Z1);
        Collider("Duvar_BatiUst", X0, 4.0f, zs, X0 + 0.22f, Ridge, zn);
    }

    // x boyunca uzanan duvar (z0..z1 kalınlık), açıklıklar (xa, xb, ya, yb); iç yüz kaplaması ve dış mavi bant
    static void WallX(Mats M, float z0, float z1, float xa, float xb, (float x0, float x1, float y0, float y1)[] holes, bool insideNegZ)
    {
        var xs = new List<float> { xa, xb };
        foreach (var h in holes) { xs.Add(h.x0); xs.Add(h.x1); }
        xs = xs.Distinct().OrderBy(v => v).ToList();
        float zi = insideNegZ ? z0 : z1;                 // iç yüz
        float zo = insideNegZ ? z1 : z0;                 // dış yüz
        float sIn = insideNegZ ? -1f : 1f;               // iç yüzden içeri yön
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            float a = xs[i], b = xs[i + 1];
            var spans = new List<(float, float)> { (0f, H) };
            foreach (var h in holes)
                if (h.x0 <= a + 1e-3f && h.x1 >= b - 1e-3f)
                {
                    var next = new List<(float, float)>();
                    foreach (var s in spans) { if (h.y0 > s.Item1) next.Add((s.Item1, Mathf.Min(h.y0, s.Item2))); if (h.y1 < s.Item2) next.Add((Mathf.Max(h.y1, s.Item1), s.Item2)); }
                    spans = next;
                }
            foreach (var (y0, y1) in spans)
            {
                if (y1 - y0 < 1e-3f) continue;
                Box(M.plaster, a, y0, Mathf.Min(z0, z1), b, y1, Mathf.Max(z0, z1));
                // iç kaplama: 0–1,2 mavi alt panel, üstü açık panel
                float l0 = zi, l1 = zi + sIn * 0.02f;
                if (y0 < 1.2f) Box(M.blueLining, a, y0, Mathf.Min(l0, l1), b, Mathf.Min(1.2f, y1), Mathf.Max(l0, l1));
                if (y1 > 1.2f) Box(M.panel, a, Mathf.Max(1.2f, y0), Mathf.Min(l0, l1), b, y1, Mathf.Max(l0, l1));
                // dış mavi bant (garajla aynı) 0–0,96 ve taban 0–0,22
                if (y0 < 0.96f)
                {
                    float o0 = zo, o1 = zo - sIn * 0.012f;
                    Box(M.band, a, y0, Mathf.Min(o0, o1), b, Mathf.Min(0.96f, y1), Mathf.Max(o0, o1));
                }
            }
            // iç panel fugaları her 1,25 m
            for (float x = Mathf.Ceil(a / 1.25f) * 1.25f; x < b - 0.05f; x += 1.25f)
                foreach (var (y0, y1) in spans) if (y1 > 1.25f) Box(M.panelSeam, x - 0.02f, Mathf.Max(1.2f, y0), Mathf.Min(zi, zi + sIn * 0.032f), x + 0.02f, y1, Mathf.Max(zi, zi + sIn * 0.032f));
        }
        // mavi panel üst çıtası ve saçak hizası profili (iç)
        Box(M.steelLight, xa, 1.18f, Mathf.Min(zi, zi + sIn * 0.045f), xb, 1.24f, Mathf.Max(zi, zi + sIn * 0.045f));
    }

    // z boyunca uzanan doğu alın duvarı (x0..x1 kalınlık), tek açıklık
    static void WallZ(Mats M, float x0, float x1, float za, float zb, (float z0, float z1, float y0, float y1) hole)
    {
        foreach (var (a, b) in new[] { (za, hole.z0), (hole.z1, zb) })
        {
            Box(M.plaster, x0, 0f, a, x1, H, b);
            Box(M.blueLining, x0 - 0.02f, 0f, Mathf.Max(a, Z0), x0, 1.2f, Mathf.Min(b, Z1));
            Box(M.panel, x0 - 0.02f, 1.2f, Mathf.Max(a, Z0), x0, H, Mathf.Min(b, Z1));
            Box(M.band, x1, 0f, a, x1 + 0.012f, 0.96f, b);
        }
        Box(M.plaster, x0, hole.y1, hole.z0, x1, H, hole.z1);
        Box(M.panel, x0 - 0.02f, hole.y1, hole.z0, x0, H, hole.z1);
        Box(M.steelLight, x0 - 0.045f, 1.18f, Mathf.Max(za, Z0), x0, 1.24f, hole.z0);
        Box(M.steelLight, x0 - 0.045f, 1.18f, hole.z1, x0, 1.24f, Mathf.Min(zb, Z1));
    }

    // alın üçgeni: saçaktan (H) mahyaya (Ridge), x0..x1 kalınlık
    static void Gable(Material m, float x0, float x1, float za, float zb)
    {
        var g = G(m);
        Vector3 a0 = new Vector3(x0, H, za), b0 = new Vector3(x0, H, zb), r0 = new Vector3(x0, Ridge, ZR);
        Vector3 a1 = new Vector3(x1, H, za), b1 = new Vector3(x1, H, zb), r1 = new Vector3(x1, Ridge, ZR);
        g.Tri(a0, r0, b0, Vector3.left); g.Tri(a1, b1, r1, Vector3.right);
        g.Quad(a0, a1, r1, r0, (Vector3.back + Vector3.up).normalized); g.Quad(b0, r0, r1, b1, (Vector3.forward + Vector3.up).normalized);
    }

    // ------------------------------------------------------------------ çatı

    static void BuildRoof(Mats M)
    {
        float over = 0.35f, xa = X0 - 0.05f, xb = X1 + T + 0.3f;
        float zs = Z0 - T - over, zn = Z1 + T + over;
        float slope = (Ridge - H) / (ZR - (Z0 - T));
        float ys = H - slope * over, yn = H - slope * over;
        // iki eğim: üst sac (dış) ve alt kaplama (iç), 0,14 m arayla
        Slope(M.roof, M.roofDeck, xa, xb, zs, ys, ZR, Ridge);
        Slope(M.roof, M.roofDeck, xa, xb, zn, yn, ZR, Ridge);
        // mahya başlığı, oluklar ve iniş boruları
        Box(M.steel, xa, Ridge + 0.1f, ZR - 0.18f, xb, Ridge + 0.2f, ZR + 0.18f);
        foreach (float z in new[] { zs, zn })
        {
            float s = z < ZR ? -1f : 1f;
            Box(M.steelLight, xa, ys - 0.05f, z + s * 0.0f - 0.07f, xb, ys + 0.06f, z + 0.07f);
            foreach (float x in new[] { xa + 0.15f, xb - 0.15f }) G(M.steelLight).Cyl(new Vector3(x, -0.25f, z - s * 0.1f), 0.05f, ys + 0.2f, 8);
        }
        // ışıklıklar: her eğimde dört panel (içeride ışıyan, dışta koyu cam)
        foreach (float cx in new[] { 14.35f, 18.85f, 23.35f, 27.85f })
            foreach (int side in new[] { -1, 1 })
            {
                float t0 = 0.32f, t1 = 0.68f;
                float zA = Mathf.Lerp(ZR, side < 0 ? Z0 - T : Z1 + T, t0), zB = Mathf.Lerp(ZR, side < 0 ? Z0 - T : Z1 + T, t1);
                float yA = Mathf.Lerp(Ridge, H, t0), yB = Mathf.Lerp(Ridge, H, t1);
                var n = new Vector3(0f, 1f, side * slope).normalized;
                G(M.skylight).Quad(new Vector3(cx - 1.1f, yA - 0.012f, zA), new Vector3(cx + 1.1f, yA - 0.012f, zA), new Vector3(cx + 1.1f, yB - 0.012f, zB), new Vector3(cx - 1.1f, yB - 0.012f, zB), -n);
                G(M.glass).Quad(new Vector3(cx - 1.1f, yA + 0.165f, zA), new Vector3(cx + 1.1f, yA + 0.165f, zA), new Vector3(cx + 1.1f, yB + 0.165f, zB), new Vector3(cx - 1.1f, yB + 0.165f, zB), n);
            }
        // çatı çarpıştırıcıları (fırlatılan eşya dışarı çıkmasın)
        foreach (int side in new[] { -1, 1 })
        {
            float zEave = side < 0 ? Z0 - T : Z1 + T;
            var go = new GameObject(side < 0 ? "Cati_Guney" : "Cati_Kuzey");
            go.transform.SetParent(col, false);
            Vector3 a = new Vector3((xa + xb) * 0.5f, H, zEave), b = new Vector3((xa + xb) * 0.5f, Ridge, ZR);
            go.transform.position = (a + b) * 0.5f + Vector3.up * 0.07f;
            go.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(xb - xa, 0.14f, (b - a).magnitude);
        }
    }

    static void Slope(Material top, Material under, float xa, float xb, float zEave, float yEave, float zRidge, float yRidge)
    {
        float s = zEave < zRidge ? -1f : 1f;
        Vector3 e0 = new Vector3(xa, yEave, zEave), e1 = new Vector3(xb, yEave, zEave), r0 = new Vector3(xa, yRidge, zRidge), r1 = new Vector3(xb, yRidge, zRidge);
        var n = Vector3.Cross(r0 - e0, e1 - e0).normalized; if (n.y < 0f) n = -n;
        Vector3 up = n * 0.14f;
        var gt = G(top); var gu = G(under);
        gt.Quad(e0 + up, e1 + up, r1 + up, r0 + up, n);               // üst sac
        gu.Quad(e0, e1, r1, r0, -n);                                 // alt kaplama
        gt.Quad(e0, e1, e1 + up, e0 + up, new Vector3(0f, 0f, s));    // saçak alnı
        gt.Quad(e0, e0 + up, r0 + up, r0, Vector3.left);
        gt.Quad(e1, r1, r1 + up, e1 + up, Vector3.right);
        // kenet çizgileri (her 0,6 m)
        for (float x = xa + 0.3f; x < xb; x += 0.6f)
        {
            Vector3 a = new Vector3(x, yEave, zEave) + up, b = new Vector3(x, yRidge, zRidge) + up;
            gt.Beam(a + n * 0.02f, b + n * 0.02f, 0.025f, 0.04f);
        }
    }

    // ------------------------------------------------------------------ çelik taşıyıcılar

    static void BuildStructure(Mats M)
    {
        foreach (float x in TrussX)
        {
            // kolonlar (iç yüzde) ve taban plakaları
            foreach (var (za, zb) in new[] { (Z0, Z0 + 0.3f), (Z1 - 0.3f, Z1) })
            {
                Box(M.steel, x - 0.11f, 0f, za, x + 0.11f, H, zb);
                Box(M.steel, x - 0.17f, 0f, za - 0.0f, x + 0.17f, 0.02f, zb);
                Box(M.yellow, x - 0.115f, 0.02f, za, x + 0.115f, 0.9f, zb, HM.NY);              // sarı-siyah koruma
                for (int k = 0; k < 4; k++) Box(M.black, x - 0.116f, 0.08f + k * 0.22f, za - 0.001f, x + 0.116f, 0.16f + k * 0.22f, zb + 0.001f, HM.NY);
                Collider("Kolon_" + x.ToString("F1") + (za < 0 ? "_G" : "_K"), x - 0.11f, 0f, za, x + 0.11f, H, zb);
            }
            // makas: alt başlık, üst başlıklar, orta dikme, dikmeler ve çaprazlar
            float yb = H - 0.22f;
            Vector3 sA = new Vector3(x, H - 0.05f, Z0 + 0.1f), nA = new Vector3(x, H - 0.05f, Z1 - 0.1f), top = new Vector3(x, Ridge - 0.22f, ZR);
            G(M.steel).Beam(new Vector3(x, yb, Z0 + 0.1f), new Vector3(x, yb, Z1 - 0.1f), 0.12f, 0.16f);
            G(M.steel).Beam(sA, top, 0.12f, 0.18f);
            G(M.steel).Beam(top, nA, 0.12f, 0.18f);
            G(M.steel).Beam(new Vector3(x, yb, ZR), top, 0.09f, 0.09f);
            foreach (int s in new[] { -1, 1 })
            {
                float zEdge = s < 0 ? Z0 + 0.1f : Z1 - 0.1f;
                for (int k = 1; k <= 2; k++)
                {
                    float t = k / 3f;
                    float z = Mathf.Lerp(ZR, zEdge, t), yTop = Mathf.Lerp(Ridge - 0.22f, H - 0.05f, t);
                    G(M.steel).Beam(new Vector3(x, yb, z), new Vector3(x, yTop, z), 0.07f, 0.07f);
                    float zPrev = Mathf.Lerp(ZR, zEdge, (k - 1) / 3f), yPrev = Mathf.Lerp(Ridge - 0.22f, H - 0.05f, (k - 1) / 3f);
                    G(M.steel).Beam(new Vector3(x, yb, z), new Vector3(x, yPrev, zPrev), 0.06f, 0.06f);
                }
            }
        }
        // aşıklar (makaslar boyunca, çatının altında)
        foreach (float t in new[] { 0.18f, 0.5f, 0.82f })
            foreach (int s in new[] { -1, 1 })
            {
                float zEdge = s < 0 ? Z0 : Z1;
                float z = Mathf.Lerp(ZR, zEdge, t), y = Mathf.Lerp(Ridge, H, t) - 0.12f;
                G(M.steel).Beam(new Vector3(X0 + 0.25f, y, z), new Vector3(X1 - 0.02f, y, z), 0.08f, 0.14f);
            }
        // kablo kanalı (kuzey duvar boyunca, 3,6 m)
        Box(M.steelLight, X0 + 0.3f, 3.55f, Z1 - 0.16f, X1 - 0.3f, 3.62f, Z1 - 0.04f);
    }

    // ------------------------------------------------------------------ kapılar ve pencereler

    static void BuildDoorsAndWindows(Mats M)
    {
        // güney sarmal kapı (kapalı): lameller, yan raylar, üstte tambur kutusu, kumanda kutusu
        float zc = Z0 - T * 0.5f;
        for (float y = 0.02f; y < D1H - 0.06f; y += 0.125f)
            Box(M.slats, D1X0, y, zc - 0.03f, D1X1, Mathf.Min(y + 0.118f, D1H), zc + 0.03f);
        Box(M.rubber, D1X0, 0f, zc - 0.035f, D1X1, 0.03f, zc + 0.035f);
        foreach (float x in new[] { D1X0 - 0.08f, D1X1 })
            Box(M.steel, x, 0f, Z0 - 0.02f, x + 0.08f, D1H + 0.1f, Z0 + 0.1f);
        Box(M.steel, D1X0 - 0.2f, D1H + 0.05f, Z0, D1X1 + 0.2f, D1H + 0.6f, Z0 + 0.5f);
        // kumanda kutusu: kapı ile 12,1'deki kolon arasındaki boşlukta
        Box(M.yellow, D1X0 - 0.34f, 1.15f, Z0, D1X0 - 0.14f, 1.45f, Z0 + 0.09f);
        G(M.red).Cyl(new Vector3(D1X0 - 0.24f, 1.38f, Z0 + 0.09f), 0.025f, 0.015f, 8);
        G(M.green).Cyl(new Vector3(D1X0 - 0.24f, 1.25f, Z0 + 0.09f), 0.025f, 0.015f, 8);
        // dış yüz: kapı lamelleri dışta da görünsün diye ikinci kat yok (kutu iki yüzlü); dış bordür
        // doğu araç kapısı (açık, sarılmış): tambur kutusu, yan raylar, alt profil görünür; dışarıda beton rampa
        Box(M.steel, X1 - 0.5f, D2H + 0.05f, D2Z0 - 0.2f, X1, D2H + 0.6f, D2Z1 + 0.2f);
        Box(M.slats, X1 - 0.42f, D2H - 0.08f, D2Z0, X1 - 0.08f, D2H + 0.06f, D2Z1);
        foreach (float z in new[] { D2Z0 - 0.08f, D2Z1 })
            Box(M.steel, X1 - 0.1f, 0f, z, X1 + 0.02f, D2H + 0.1f, z + 0.08f);
        Box(M.baseConcrete, X1 + T, -0.3f, D2Z0 - 0.3f, X1 + T + 2.6f, -0.02f, D2Z1 + 0.3f, HM.NY);
        Box(M.yellow, X1 + T + 0.05f, -0.019f, D2Z0 - 0.3f, X1 + T + 0.15f, -0.012f, D2Z1 + 0.3f, HM.NY);
        // pencereler: cam, çerçeve (iki yüz), orta kayıt, denizlik
        foreach (var w in WindowsX)
        {
            foreach (var (y0, y1, z0, z1) in new[] { (2.6f, 4.3f, Z0 - T, Z0), (3.4f, 4.6f, Z1, Z1 + T) })
            {
                float zm = (z0 + z1) * 0.5f;
                G(M.glass).Quad(new Vector3(w.x, y0, zm), new Vector3(w.y, y0, zm), new Vector3(w.y, y1, zm), new Vector3(w.x, y1, zm), z0 < 0 ? Vector3.back : Vector3.forward);
                G(M.glass).Quad(new Vector3(w.x, y0, zm), new Vector3(w.x, y1, zm), new Vector3(w.y, y1, zm), new Vector3(w.y, y0, zm), z0 < 0 ? Vector3.forward : Vector3.back);
                foreach (float zf in new[] { z0 - 0.02f, z1 - 0.04f })
                {
                    Box(M.steel, w.x, y0, zf, w.y, y0 + 0.06f, zf + 0.06f);
                    Box(M.steel, w.x, y1 - 0.06f, zf, w.y, y1, zf + 0.06f);
                    Box(M.steel, w.x, y0, zf, w.x + 0.06f, y1, zf + 0.06f);
                    Box(M.steel, w.y - 0.06f, y0, zf, w.y, y1, zf + 0.06f);
                    Box(M.steel, (w.x + w.y) * 0.5f - 0.03f, y0, zf, (w.x + w.y) * 0.5f + 0.03f, y1, zf + 0.06f);
                }
                Box(M.steelLight, w.x - 0.05f, y0 - 0.04f, Mathf.Min(z0, z1) - 0.06f, w.y + 0.05f, y0, Mathf.Max(z0, z1) + 0.06f);
            }
        }
    }

    // ------------------------------------------------------------------ zemin çizgileri

    static void BuildFloorMarks(Mats M)
    {
        const float y0 = 0.001f, y1 = 0.004f;
        // konvoy park alanı (sarı çerçeve) ve araç durma çizgisi
        float bx0 = 17.7f, bx1 = 31.0f, bz0 = -2.25f, bz1 = 1.65f;
        Box(M.yellow, bx0, y0, bz0, bx1, y1, bz0 + 0.1f, HM.NY); Box(M.yellow, bx0, y0, bz1 - 0.1f, bx1, y1, bz1, HM.NY);
        Box(M.yellow, bx0, y0, bz0, bx0 + 0.1f, y1, bz1, HM.NY); Box(M.yellow, bx1 - 0.1f, y0, bz0, bx1, y1, bz1, HM.NY);
        Box(M.white, ConvoyOrigin.x + 3.35f, y0, bz0 + 0.3f, ConvoyOrigin.x + 3.5f, y1, bz1 - 0.3f, HM.NY);
        // yürüme yolu kesik çizgileri
        for (float x = X0 + 0.4f; x < X1 - 0.5f; x += 1.5f) Box(M.white, x, y0, 2.05f, Mathf.Min(x + 1.0f, X1 - 0.4f), y1, 2.15f, HM.NY);
        for (float x = 17.4f; x < X1 - 0.5f; x += 1.5f) Box(M.white, x, y0, -3.45f, Mathf.Min(x + 1.0f, X1 - 0.4f), y1, -3.35f, HM.NY);
        // doğu kapısı önünde sarı-siyah tehlike şeridi
        for (float z = D2Z0; z < D2Z1 - 0.01f; z += 0.5f)
        {
            Box(M.yellow, X1 - 1.6f, y0, z, X1 - 0.02f, y1, Mathf.Min(z + 0.25f, D2Z1), HM.NY);
            Box(M.black, X1 - 1.6f, y0, Mathf.Min(z + 0.25f, D2Z1), X1 - 0.02f, y1, Mathf.Min(z + 0.5f, D2Z1), HM.NY);
        }
        // güney kapı önünde boş tutulacak alan (çapraz taralı yerine sarı çerçeve)
        Box(M.yellow, D1X0, y0, Z0 + 0.02f, D1X0 + 0.08f, y1, Z0 + 1.6f, HM.NY); Box(M.yellow, D1X1 - 0.08f, y0, Z0 + 0.02f, D1X1, y1, Z0 + 1.6f, HM.NY);
        Box(M.yellow, D1X0, y0, Z0 + 1.52f, D1X1, y1, Z0 + 1.6f, HM.NY);
        // drenaj kanalı ızgarası (konvoy alanı boyunca)
        Box(M.grate, 18.2f, -0.004f, -2.75f, 30.8f, 0.003f, -2.55f, HM.NY);
    }

    // ------------------------------------------------------------------ ışıklar

    static void BuildLights(Mats M)
    {
        var lights = Child(root, "Isiklar");
        int i = 0;
        foreach (float x in TrussX)
            foreach (float z in new[] { -3.0f, 1.6f })
            {
                Vector3 p = new Vector3(x, H - 0.75f, z);
                // armatür: iki askı, gövde, ışıyan alt yüz
                G(M.steelLight).Beam(p + new Vector3(-0.18f, 0.1f, 0f), new Vector3(x - 0.18f, H - 0.3f, z), 0.012f, 0.012f);
                G(M.steelLight).Beam(p + new Vector3(0.18f, 0.1f, 0f), new Vector3(x + 0.18f, H - 0.3f, z), 0.012f, 0.012f);
                G(M.steel).Cyl(p + Vector3.up * 0.02f, 0.3f, 0.1f, 14, true, false);
                G(M.led).Cyl(p, 0.27f, 0.022f, 14, false, true);
                bool shadow = (x == 21.1f && z > 0f) || (x == 25.6f && z < 0f);
                AddLight(lights, "LED_" + (++i), p - Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.down), LightType.Spot, new Color(1f, 0.96f, 0.9f), 4.6f, 10f, 120f, shadow);
            }
        // ekipman duvarına yıkama ışığı, kapıların üstünde dış aplikler
        AddLight(lights, "EkipmanDuvari_Isik", new Vector3(13.2f, 4.6f, 1.2f), Quaternion.LookRotation(new Vector3(0f, -2.4f, 2.6f)), LightType.Spot, new Color(1f, 0.88f, 0.72f), 3.2f, 9f, 75f, false);
        // dış aplikler kapıların iki yanında (tabelaların önünü kapatmaz)
        var outside = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { D1X0 - 0.45f, D1X1 + 0.45f }) outside.Add((new Vector3(x, 3.75f, Z0 - T - 0.12f), Vector3.back));
        foreach (float zz in new[] { D2Z0 - 0.45f, D2Z1 + 0.45f }) outside.Add((new Vector3(X1 + T + 0.12f, 4.0f, zz), Vector3.right));
        foreach (var (pos, outward) in outside)
        {
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 h = side * 0.11f + outward * 0.1f;
            Box(M.steel, pos.x - Mathf.Abs(h.x), pos.y - 0.14f, pos.z - Mathf.Abs(h.z), pos.x + Mathf.Abs(h.x), pos.y + 0.14f, pos.z + Mathf.Abs(h.z));
            Box(M.led, pos.x - Mathf.Abs(h.x) * 0.8f, pos.y - 0.152f, pos.z - Mathf.Abs(h.z) * 0.8f, pos.x + Mathf.Abs(h.x) * 0.8f, pos.y - 0.14f, pos.z + Mathf.Abs(h.z) * 0.8f, HM.PY);
            AddLight(lights, "Dis_Aplik", pos - Vector3.up * 0.2f + outward * 0.05f, Quaternion.LookRotation((Vector3.down + outward * 0.35f).normalized), LightType.Spot, new Color(1f, 0.82f, 0.6f), 3.2f, 8f, 110f, false);
        }
    }

    static void AddLight(Transform parent, string name, Vector3 pos, Quaternion rot, LightType type, Color c, float intensity, float range, float angle, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var l = go.AddComponent<Light>();
        l.type = type; l.color = c; l.intensity = intensity; l.range = range;
        if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f; }
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        lightCount++;
    }

    static void BuildProbe()
    {
        var go = new GameObject("HangarReflection");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3((X0 + X1) * 0.5f, 2.8f, (Z0 + Z1) * 0.5f);
        var p = go.AddComponent<ReflectionProbe>();
        p.size = new Vector3(X1 - X0, 5.6f, Z1 - Z0);
        p.boxProjection = true;
        p.mode = ReflectionProbeMode.Custom;
        p.importance = 2;
        p.resolution = 256;
        p.intensity = 1f;
        Directory.CreateDirectory(Path.GetDirectoryName(ProbePath));
        bool ok = Lightmapping.BakeReflectionProbe(p, ProbePath);
        if (ok) { AssetDatabase.ImportAsset(ProbePath); p.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(ProbePath); }
        report.AppendLine("Yansıma sondası: " + (ok ? ProbePath : "pişirilemedi"));
    }

    // ------------------------------------------------------------------ bölgeler ve donanım

    static Transform Zone(string name) => Child(props, name);

    static void BuildEquipmentWall(Mats M)
    {
        var z = Zone("KurtarmaEkipmaniDuvari");
        float zw = Z1 - 0.02f;
        // iki pano (12,1'deki kolonun iki yanında)
        foreach (var (a, b) in new[] { (9.75f, 11.85f), (12.35f, 16.25f) })
        {
            Box(M.board, a, 0.95f, zw - 0.04f, b, 3.05f, zw);
            Box(M.steelLight, a - 0.03f, 0.92f, zw - 0.05f, b + 0.03f, 0.95f, zw);
            Box(M.steelLight, a - 0.03f, 3.05f, zw - 0.05f, b + 0.03f, 3.08f, zw);
        }
        // askı kancaları
        void Hook(float x, float y) { G(M.steelLight).Beam(new Vector3(x, y, zw - 0.04f), new Vector3(x, y + 0.03f, zw - 0.16f), 0.018f, 0.018f); }
        // can simitleri (duvara dik), altında ağaç kayışları
        foreach (float x in new[] { 10.3f, 11.3f })
        {
            Hook(x, 2.6f);
            RescueGearAssembly.Spawn("SelKurtarma/CanSimidi.fbx", z, new Vector3(x, 2.25f, zw - 0.1f), Quaternion.Euler(-90f, 0f, 0f)).name = "Can_Simidi";
            Hook(x, 1.62f);
            RescueGearAssembly.Spawn("SelKurtarma/AgacKayisi.fbx", z, new Vector3(x + 0.18f, 1.45f, zw - 0.08f), Quaternion.Euler(-90f, 0f, 0f)).name = "Agac_Kayisi";
        }
        // makaralı kurtarma askısı (kancadan sarkar)
        Hook(12.85f, 2.9f);
        RescueGearAssembly.Spawn("SelKurtarma/KurtarmaAskisi.fbx", z, new Vector3(12.85f, 2.9f, zw - 0.2f), Quaternion.Euler(0f, 90f, 0f)).name = "Kurtarma_Askisi";
        // raf: megafonlar, telsiz ve can yeleği paketleri, ip makarası
        Box(M.steelLight, 13.5f, 1.28f, zw - 0.36f, 16.15f, 1.31f, zw - 0.04f);
        foreach (float x in new[] { 13.7f, 16.0f }) Box(M.steel, x - 0.02f, 1.12f, zw - 0.3f, x + 0.02f, 1.28f, zw - 0.04f);
        RescueGearAssembly.Spawn("SelKurtarma/Megafon.fbx", z, new Vector3(13.85f, 1.31f, zw - 0.2f), Quaternion.Euler(0f, 90f, 0f)).name = "Megafon";
        RescueGearAssembly.Spawn("SelKurtarma/Megafon.fbx", z, new Vector3(14.3f, 1.31f, zw - 0.2f), Quaternion.Euler(0f, 110f, 0f)).name = "Megafon";
        RescueGearAssembly.Spawn("Drone/DroneYuku_Telsiz.fbx", z, new Vector3(14.75f, 1.31f, zw - 0.18f), Quaternion.Euler(0f, 90f, 0f)).name = "Telsiz_Paketi";
        RescueGearAssembly.Spawn("Drone/DroneYuku_Telsiz.fbx", z, new Vector3(15.0f, 1.31f, zw - 0.18f), Quaternion.Euler(0f, 90f, 0f)).name = "Telsiz_Paketi";
        RescueGearAssembly.Spawn("Drone/DroneYuku_CanYelegi.fbx", z, new Vector3(15.4f, 1.31f, zw - 0.2f), Quaternion.Euler(0f, 90f, 0f)).name = "CanYelegi_Paketi";
        RescueGearAssembly.Spawn("Drone/DroneYuku_CanYelegi.fbx", z, new Vector3(15.85f, 1.31f, zw - 0.2f), Quaternion.Euler(0f, 80f, 0f)).name = "CanYelegi_Paketi";
        // ilk yardım dolabı (beyaz, kırmızı haç)
        Box(M.cream, 15.3f, 1.65f, zw - 0.14f, 15.95f, 2.45f, zw - 0.02f);
        Box(M.red, 15.6f, 1.88f, zw - 0.145f, 15.65f, 2.22f, zw - 0.14f, HM.PZ);
        Box(M.red, 15.455f, 2.025f, zw - 0.145f, 15.795f, 2.075f, zw - 0.14f, HM.PZ);
        // tezgâh: ahşap üst, çelik ayaklar; üstünde kasklar ve halat kangalı
        Box(M.wood, 12.45f, 0.42f, zw - 0.46f, 14.45f, 0.47f, zw - 0.04f);
        foreach (float x in new[] { 12.5f, 14.4f }) foreach (float zz in new[] { zw - 0.42f, zw - 0.08f }) Box(M.steel, x - 0.025f, 0f, zz - 0.025f, x + 0.025f, 0.42f, zz + 0.025f);
        Collider("Tezgah_Ekipman", 12.45f, 0f, zw - 0.46f, 14.45f, 0.47f, zw - 0.04f, z);
        for (int k = 0; k < 4; k++)
        {
            var c = new Vector3(12.75f + k * 0.42f, 0.47f, zw - 0.25f);
            G(k % 2 == 0 ? M.orange : M.cream).Dome(c, 0.14f, 0.12f, 12, 4);
            G(k % 2 == 0 ? M.orange : M.cream).Ring(c, 0.16f, 0.03f, 0.012f, 12);
        }
        G(M.yellow).Ring(new Vector3(14.25f, 0.47f, zw - 0.3f), 0.17f, 0.06f, 0.05f, 14);
        // yangın söndürücüler (geçiş yanında, kapılar yanında)
        foreach (var p in new[] { new Vector3(9.55f, 0f, 1.9f), new Vector3(X1 - 0.15f, 0f, 2.25f), new Vector3(11.7f, 0f, Z0 + 0.15f) })
        {
            G(M.red).Cyl(p + Vector3.up * 0.22f, 0.08f, 0.5f, 10, true, true);
            G(M.black).Cyl(p + Vector3.up * 0.72f, 0.03f, 0.08f, 8);
            Box(M.red, p.x - 0.12f, 0.9f, p.z - 0.12f, p.x + 0.12f, 1.12f, p.z + 0.12f);
        }
    }

    static void BuildSuppliesAndBreak(Mats M)
    {
        var z = Zone("ErzakVeMola");
        // paletler: iki su, bir erzak
        void Pallet(float cx, float cz, bool water)
        {
            for (int i = 0; i < 3; i++) Box(M.pallet, cx - 0.6f, 0f, cz - 0.4f + i * 0.35f, cx + 0.6f, 0.1f, cz - 0.3f + i * 0.35f);
            Box(M.pallet, cx - 0.6f, 0.1f, cz - 0.4f, cx + 0.6f, 0.14f, cz + 0.4f);
            Collider("Palet", cx - 0.6f, 0f, cz - 0.4f, cx + 0.6f, water ? 1.0f : 0.9f, cz + 0.4f, z);
            for (int l = 0; l < 3; l++)
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 2; b++)
                    {
                        float x0 = cx - 0.57f + a * 0.38f, z0 = cz - 0.38f + b * 0.38f, yy = 0.14f + l * (water ? 0.28f : 0.25f);
                        if (water)
                        {
                            Box(M.water, x0, yy, z0, x0 + 0.37f, yy + 0.27f, z0 + 0.37f);
                            if (l == 2) for (int u = 0; u < 2; u++) for (int v = 0; v < 3; v++) G(M.waterCap).Cyl(new Vector3(x0 + 0.1f + u * 0.17f, yy + 0.27f, z0 + 0.07f + v * 0.115f), 0.025f, 0.02f, 6);
                        }
                        else
                        {
                            Box(M.cardboard, x0, yy, z0, x0 + 0.36f, yy + 0.24f, z0 + 0.36f);
                            Box(M.cream, x0 + 0.05f, yy + 0.08f, z0 - 0.002f, x0 + 0.31f, yy + 0.16f, z0, HM.PZ);
                        }
                    }
        }
        Pallet(10.25f, -4.55f, true);
        Pallet(11.6f, -4.55f, true);
        Pallet(10.25f, -3.45f, false);
        // mola masası ve sandalyeler; masada yiyecek-içecek seti
        float tx = 11.3f, tz = -1.95f;
        Box(M.wood, tx - 0.8f, 0.71f, tz - 0.4f, tx + 0.8f, 0.75f, tz + 0.4f);
        foreach (float sx in new[] { -0.72f, 0.72f }) foreach (float sz in new[] { -0.32f, 0.32f }) Box(M.steel, tx + sx - 0.025f, 0f, tz + sz - 0.025f, tx + sx + 0.025f, 0.71f, tz + sz + 0.025f);
        Collider("MolaMasasi", tx - 0.8f, 0f, tz - 0.4f, tx + 0.8f, 0.75f, tz + 0.4f, z);
        foreach (var (cx, cz, yaw) in new[] { (tx - 0.45f, tz - 0.75f, 0f), (tx + 0.45f, tz - 0.75f, 0f), (tx, tz + 0.78f, 180f) })
        {
            var g = G(M.steel); g.Push(new Vector3(cx, 0f, cz), Quaternion.Euler(0f, yaw, 0f));
            g.MinMax(-0.21f, 0.43f, -0.2f, 0.21f, 0.46f, 0.22f);
            foreach (float lx in new[] { -0.18f, 0.18f }) foreach (float lz in new[] { -0.17f, 0.19f }) g.MinMax(lx - 0.015f, 0f, lz - 0.015f, lx + 0.015f, 0.43f, lz + 0.015f);
            g.MinMax(-0.21f, 0.46f, -0.22f, 0.21f, 0.86f, -0.19f);
            g.Pop();
            var gc = G(M.orange); gc.Push(new Vector3(cx, 0f, cz), Quaternion.Euler(0f, yaw, 0f)); gc.MinMax(-0.19f, 0.46f, -0.18f, 0.19f, 0.49f, 0.2f); gc.MinMax(-0.19f, 0.58f, -0.235f, 0.19f, 0.84f, -0.215f); gc.Pop();
        }
        var food = RescueGearAssembly.Spawn("YiyecekIcecek/YiyecekIcecekSeti.fbx", z, new Vector3(tx + 0.05f, 0.75f, tz), Quaternion.Euler(0f, 0f, 0f));
        food.name = "Yiyecek_Icecek_Seti";
        // su sebili (batı duvarı önünde)
        var wc = new Vector3(9.75f, 0f, -0.7f);
        Box(M.cream, wc.x - 0.16f, 0f, wc.z - 0.16f, wc.x + 0.16f, 1.0f, wc.z + 0.16f);
        G(M.waterCap).Cyl(wc + Vector3.up * 1.0f, 0.13f, 0.38f, 12, true, false);
        Box(M.black, wc.x + 0.16f, 0.72f, wc.z - 0.06f, wc.x + 0.17f, 0.82f, wc.z + 0.06f);
        Collider("SuSebili", wc.x - 0.16f, 0f, wc.z - 0.16f, wc.x + 0.16f, 1.4f, wc.z + 0.16f, z);
        // nöbet panosu (batı duvarında, garaj duvarının dış yüzü)
        Box(M.white, X0, 1.15f, -2.9f, X0 + 0.03f, 2.15f, -1.3f);
        Box(M.steel, X0, 1.12f, -2.93f, X0 + 0.035f, 1.15f, -1.27f);
        // beş satır çizgisi alttan; başlık en üstte, satırların üstünde boşlukla (önceki yerleşimde PZT satırı başlığa biniyordu)
        for (int r = 0; r < 5; r++) Box(M.blueSign, X0 + 0.031f, 1.24f + r * 0.145f, -2.75f, X0 + 0.033f, 1.252f + r * 0.145f, -1.45f, HM.NX);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var ink = new Color(0.05f, 0.1f, 0.28f);
        Text(z, font, "NÖBET ÇİZELGESİ", new Vector3(X0 + 0.036f, 2.05f, -2.1f), Quaternion.Euler(0f, -90f, 0f), 0.0078f, new Color(0.72f, 0.08f, 0.06f), FontStyle.Bold);
        string[] rows = { "CUM   Ekip 2 · Fındık", "PER   Ekip 1", "ÇAR   Ekip 3", "SAL   Ekip 2", "PZT   Ekip 1" };
        for (int r = 0; r < rows.Length; r++) Text(z, font, rows[r], new Vector3(X0 + 0.036f, 1.305f + r * 0.145f, -2.1f), Quaternion.Euler(0f, -90f, 0f), 0.006f, ink, FontStyle.Bold);
    }

    static void BuildWorkshop(Mats M)
    {
        var z = Zone("DroneVeAletTezgahi");
        // tezgâh (güney duvarı, 16,6 ve 21,1 kolonları arasında)
        float bx0 = 17.2f, bx1 = 20.8f, bz0 = Z0 + 0.05f, bz1 = Z0 + 0.8f, top = 0.9f;
        Box(M.wood, bx0, top - 0.05f, bz0, bx1, top, bz1);
        Box(M.steel, bx0, 0.22f, bz0 + 0.05f, bx1, 0.25f, bz1 - 0.05f);
        foreach (float x in new[] { bx0 + 0.05f, (bx0 + bx1) * 0.5f, bx1 - 0.05f }) foreach (float zz in new[] { bz0 + 0.05f, bz1 - 0.05f }) Box(M.steel, x - 0.03f, 0f, zz - 0.03f, x + 0.03f, top - 0.05f, zz + 0.03f);
        Collider("AletTezgahi", bx0, 0f, bz0, bx1, top, bz1, z);
        // delikli pano ve üzerinde birkaç alet
        Box(M.pegboard, bx0, 1.15f, Z0 + 0.02f, bx1, 2.2f, Z0 + 0.04f);
        for (int k = 0; k < 7; k++) Box(k % 2 == 0 ? M.red : M.steel, bx0 + 0.3f + k * 0.45f, 1.45f + (k % 3) * 0.12f, Z0 + 0.04f, bx0 + 0.34f + k * 0.45f, 1.85f + (k % 3) * 0.12f, Z0 + 0.07f);
        // elektrikli aletler setinden parçalar (yazıcı ofise gider)
        var set = RescueGearAssembly.Spawn("Aletler/ElektrikliAletlerSeti.fbx", z, Vector3.zero, Quaternion.identity);
        PrefabUtility.UnpackPrefabInstance(set, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        // setin önü Unity +Z (Blender -Y): yaw 0 aleti tezgâhın önündeki oyuncuya döndürür
        Place(set, "BENCH_DRILL_PRESS", z, new Vector3(17.6f, top, Z0 + 0.4f), 0f);
        Place(set, "CORDLESS_DRILL", z, new Vector3(18.3f, top, Z0 + 0.5f), 20f);
        Place(set, "DRILL_CHARGER", z, new Vector3(18.7f, top, Z0 + 0.4f), 0f);
        Place(set, "DRILL_BIT_CASE", z, new Vector3(19.15f, top, Z0 + 0.45f), -15f);
        Place(set, "ANGLE_GRINDER", z, new Vector3(19.55f, top, Z0 + 0.5f), 30f);
        UnityEngine.Object.DestroyImmediate(set);
        // tezgâhta bakımdaki ikinci drone
        RescueGearAssembly.Spawn("Drone/Drone.fbx", z, new Vector3(20.3f, top, Z0 + 0.5f), Quaternion.Euler(0f, 45f, 0f)).name = "Drone_Bakimda";
        // hareketli takım dolabı (kırmızı)
        float cx0 = 21.45f, cx1 = 22.25f;
        Box(M.red, cx0, 0.12f, Z0 + 0.08f, cx1, 1.0f, Z0 + 0.62f);
        for (int k = 0; k < 5; k++) Box(M.steelLight, cx0 + 0.05f, 0.25f + k * 0.15f, Z0 + 0.62f, cx1 - 0.05f, 0.27f + k * 0.15f, Z0 + 0.635f);
        Box(M.rubber, cx0 - 0.02f, 1.0f, Z0 + 0.06f, cx1 + 0.02f, 1.03f, Z0 + 0.64f);
        foreach (float x in new[] { cx0 + 0.06f, cx1 - 0.06f }) foreach (float zz in new[] { Z0 + 0.14f, Z0 + 0.56f }) G(M.tyre).Cyl(new Vector3(x, 0f, zz), 0.05f, 0.11f, 8, true, false);
        Collider("TakimDolabi", cx0, 0f, Z0 + 0.08f, cx1, 1.03f, Z0 + 0.64f, z);
        // drone iniş matı (zemin, "H") ve yanında hazır drone ile yükleri
        var mc = new Vector3(19.0f, 0f, -3.55f);
        Box(M.rubber, mc.x - 0.75f, 0f, mc.z - 0.75f, mc.x + 0.75f, 0.012f, mc.z + 0.75f, HM.NY);
        G(M.yellow).Ring(new Vector3(mc.x, 0.012f, mc.z), 0.62f, 0.05f, 0.002f, 24);
        Box(M.yellow, mc.x - 0.25f, 0.012f, mc.z - 0.3f, mc.x - 0.17f, 0.014f, mc.z + 0.3f, HM.NY);
        Box(M.yellow, mc.x + 0.17f, 0.012f, mc.z - 0.3f, mc.x + 0.25f, 0.014f, mc.z + 0.3f, HM.NY);
        Box(M.yellow, mc.x - 0.17f, 0.012f, mc.z - 0.04f, mc.x + 0.17f, 0.014f, mc.z + 0.04f, HM.NY);
        RescueGearAssembly.Spawn("Drone/Drone.fbx", z, mc + Vector3.up * 0.012f, Quaternion.Euler(0f, 20f, 0f)).name = "Drone_Hazir";
        RescueGearAssembly.Spawn("Drone/DroneYuku_IpMakarasi.fbx", z, new Vector3(20.05f, 0f, -3.0f), Quaternion.Euler(0f, -30f, 0f)).name = "Ip_Makarasi";
        RescueGearAssembly.Spawn("Drone/DroneYuku_CanYelegi.fbx", z, new Vector3(20.1f, 0f, -3.55f), Quaternion.Euler(0f, 15f, 0f)).name = "CanYelegi_Paketi";
    }

    // bir set FBX'inden (açılmış) bir öğeyi kökü taban merkezinde olacak şekilde yerleştirir
    static void Place(GameObject set, string item, Transform parent, Vector3 pos, float yaw)
    {
        var t = RescueGearAssembly.Find(set.transform, item);
        if (t == null) { report.AppendLine("UYARI: setde yok: " + item); return; }
        t.SetParent(parent, true);
        t.rotation = Quaternion.Euler(0f, yaw, 0f) * t.rotation;
        t.position = pos;
        t.name = item;
    }

    // köpeğin yatakta yattığı yön: yüzü batıya, geçişten gelen oyuncuya dönük, yatağın uzun ekseninde
    public const float BedYaw = 255f;

    static void BuildDogCorner(Mats M)
    {
        var z = Zone("KopekKosesi");
        // yatak (oval minder, kenarlı), mama ve su kapları, top, isim levhası
        // yatak yürüme yolu çizgisinin (z 2,05–2,15) kuzeyinde, kask tezgâhının (z ≥ 3,32) önünde
        var bedPos = new Vector3(14.25f, 0f, 2.72f);
        // oval yatak, uzun ekseni x; köpek bu eksende yatar (BedYaw), kısa eksende burnu kenardan taşıyordu
        G(M.bedFabric).Cyl(bedPos, 0.56f, 0.06f, 22, true, false, 0.43f);
        G(M.bedCushion).Cyl(bedPos + Vector3.up * 0.06f, 0.45f, 0.04f, 22, true, false, 0.32f);
        G(M.bedFabric).Ring(bedPos + Vector3.up * 0.06f, 0.58f, 0.12f, 0.12f, 22, 0.45f);
        var bowlPos = new Vector3(15.25f, 0f, 3.2f);
        foreach (var (dx, fill) in new[] { (-0.14f, M.cardboard), (0.14f, M.waterCap) })
        {
            G(M.steelLight).Cyl(bowlPos + new Vector3(dx, 0f, 0f), 0.11f, 0.06f, 14, false, false);
            G(fill).Cyl(bowlPos + new Vector3(dx, 0.045f, 0f), 0.095f, 0.005f, 14, true, false);
        }
        G(M.orange).Dome(new Vector3(16.2f, 0f, 1.4f), 0.065f, 0.065f, 10, 3);
        G(M.orange).Dome(new Vector3(16.2f, 0.065f, 1.4f), 0.065f, -0.065f, 10, 3);
        // isim levhası panonun önünde (ön yüzü panonunkiyle aynı düzlemdeydi: z-çakışması)
        Box(M.wood, 14.9f, 0.95f, Z1 - 0.078f, 15.25f, 1.08f, Z1 - 0.06f);

        // köpek: kök (davranış + tetik), model (Animator), yatak ve kap işaretleri
        var dog = new GameObject("Findik (kopek)");
        dog.transform.SetParent(z, false);
        dog.transform.position = bedPos;
        var model = RescueGearAssembly.Spawn("Kopek/Kopek.fbx", dog.transform, Vector3.zero, Quaternion.identity);
        model.name = "Kopek_Model";
        var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        anim.runtimeAnimatorController = DogController();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        var hit = new GameObject("Kopek_Hedef");
        hit.transform.SetParent(dog.transform, false);
        var bc = hit.AddComponent<BoxCollider>();
        bc.isTrigger = true; bc.center = new Vector3(0f, 0.32f, 0.05f); bc.size = new Vector3(0.4f, 0.62f, 0.85f);
        var rb = dog.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
        var bedMark = new GameObject("Kopek_Yatagi"); bedMark.transform.SetParent(z, false); bedMark.transform.SetPositionAndRotation(bedPos + Vector3.up * 0.06f, Quaternion.Euler(0f, BedYaw, 0f));
        dog.transform.rotation = Quaternion.Euler(0f, BedYaw, 0f);
        var bowlMark = new GameObject("Kopek_Kaplari"); bowlMark.transform.SetParent(z, false); bowlMark.transform.position = bowlPos;
        var beh = dog.AddComponent<KopekDavranisi>();
        beh.animator = anim; beh.bed = bedMark.transform; beh.bowl = bowlMark.transform;
        beh.areaMin = DogMin; beh.areaMax = DogMax;
        report.AppendLine("Köpek: Fındık, yatak " + bedPos + ", alan " + DogMin + "–" + DogMax);
    }

    static RuntimeAnimatorController DogController()
    {
        if (File.Exists(DogControllerPath)) AssetDatabase.DeleteAsset(DogControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(DogControllerPath);
        var sm = ctrl.layers[0].stateMachine;
        var clips = AssetDatabase.LoadAllAssetsAtPath(RescueGearAssembly.Dir + "Kopek/Kopek.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
        AnimatorState sleep = null;
        foreach (var c in clips.OrderBy(c => c.name))
        {
            var st = sm.AddState(c.name);
            st.motion = c;
            if (c.name == "Lie_Sleep") sleep = st;
        }
        if (sleep != null) sm.defaultState = sleep;
        AssetDatabase.SaveAssets();
        report.AppendLine("Köpek Animator: " + clips.Length + " durum");
        return ctrl;
    }

    static void BuildConvoy()
    {
        var z = Zone("KurtarmaKonvoyu");
        RescueGearAssembly.Convoy(z, ConvoyOrigin, Quaternion.identity, true, 100f);
        Collider("Konvoy_Van", ConvoyOrigin.x - 2.95f, 0f, ConvoyOrigin.z - 1.05f, ConvoyOrigin.x + 3.05f, 2.75f, ConvoyOrigin.z + 1.05f, z);
        Collider("Konvoy_RomorkBot", ConvoyOrigin.x - 9.05f, 0f, ConvoyOrigin.z - 1.05f, ConvoyOrigin.x - 3.0f, 1.55f, ConvoyOrigin.z + 1.05f, z);
        report.AppendLine("Konvoy: van kökü " + ConvoyOrigin + " (önü doğu kapısına)");
    }

    static void BuildSigns(Mats M)
    {
        var z = Zone("Tabelalar");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // dış cephe: güney kapısının üstünde mavi tabela
        float sx0 = D1X0 - 0.2f, sx1 = D1X1 + 0.2f, sz = Z0 - T - 0.03f;
        Box(M.blueSign, sx0, 4.38f, sz, sx1, 5.08f, sz + 0.03f);
        Text(z, font, "KURTARMA HANGARI", new Vector3((sx0 + sx1) * 0.5f, 4.73f, sz - 0.005f), Quaternion.identity, 0.032f, Color.white);
        // doğu kapısının üstünde dışta
        Box(M.blueSign, X1 + T, 5.3f, -1.95f, X1 + T + 0.03f, 5.75f, 0.55f);
        Text(z, font, "KURTARMA EKİBİ", new Vector3(X1 + T + 0.035f, 5.525f, -0.7f), Quaternion.Euler(0f, -90f, 0f), 0.02f, Color.white);
        // içeride: ekipman duvarının başlığı, köpeğin adı, çıkış işaretleri
        Text(z, font, "KURTARMA EKİPMANI", new Vector3(14.3f, 3.32f, Z1 - 0.05f), Quaternion.identity, 0.024f, new Color(0.95f, 0.95f, 0.92f));
        Text(z, font, "FINDIK", new Vector3(15.075f, 1.015f, Z1 - 0.083f), Quaternion.identity, 0.007f, new Color(0.2f, 0.12f, 0.06f), FontStyle.Bold);
        Box(M.exitGreen, X0 + 0.02f, 2.62f, 2.7f, X0 + 0.05f, 2.9f, 3.5f);
        Text(z, font, "GARAJ · OFİS", new Vector3(X0 + 0.055f, 2.76f, 3.1f), Quaternion.Euler(0f, -90f, 0f), 0.008f, Color.white);
        Box(M.exitGreen, X1 - 0.05f, 4.68f, -1.05f, X1 - 0.02f, 4.95f, -0.35f);
        Text(z, font, "ÇIKIŞ", new Vector3(X1 - 0.055f, 4.815f, -0.7f), Quaternion.Euler(0f, 90f, 0f), 0.011f, Color.white);
    }

    // TextMesh için derinlik sınamalı malzeme (varsayılan yazı tipi malzemesi duvarların arkasından görünür)
    static Material TextMaterial(Font font)
    {
        string path = MatDir + "/Hangar_Yazi.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("RescueTeam/HangarText")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("RescueTeam/HangarText");
        m.mainTexture = font.material.mainTexture;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Text(Transform parent, Font font, string s, Vector3 pos, Quaternion rot, float size, Color c, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject("Yazi_" + s);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var tm = go.AddComponent<TextMesh>();
        tm.text = s; tm.font = font; tm.fontSize = 120; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c; tm.fontStyle = style;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = TextMaterial(font);
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }
}
