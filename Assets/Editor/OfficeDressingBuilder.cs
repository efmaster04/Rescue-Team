using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CompanySim;
using SelVadisi.EditorBuild;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ofis güzelleştirme: çok odalı ofise yaşanmışlık katan süs eşyalarını kodla üretir ve Resources/OfficeDressing/OfisDekor.prefab
// olarak kaydeder. OfficeInteriorMood bu prefab'ı OfficeGarage açılınca sahneye ekler, sahne dosyası değişmez.
//  - Ana ofis: yarı inik jaluzi (masaya çizgili güneş), iki üst raf (klasör, baret, kupa, telsiz), Sel Vadisi operasyon haritası
//    (haritanın tepeden görüntüsü, iğneler, ip), fotoğraf çerçeveleri, masa lambası, monitörde ışıyan ekran, telsiz istasyonu,
//    köşe bitkisi, klima, duman dedektörü.
//  - Mutfak: sarkıt lambalar, perde, tezgâh önü yolluk, kanepe minderi ve battaniye, meyve kâsesi, kettle.
//  - Depo: üç metal rafın dolusu kutu, kasa, sepet, boya kovası, halat kangalı.
//  - Soyunma: perde, can simidi, halat. Ring odası: ringe dar spotlar, kum torbası paspası, flama dizisi, perdeler.
// Kural: hiçbir parça çarpıştırıcı almaz (CompanyPlayerBridge.FindTarget bilgisayarı ve kapıları gölgelemesin); adlarda
// 'Ceiling' geçmez (GarageCeiling o adları görünmez kutuya çevirir); kapı açılma yaylarına ve OperationsBoard'a girilmez.
// Malzemeler ofis FBX'lerinin kendi URP Lit malzemeleridir (ayrıntı haritalarıyla); yeniler Art/OfficeDetail/Materials'ta.
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod OfficeDressingBuilder.BuildBatch -logFile <dosya>
public static class OfficeDressingBuilder
{
    public const string PrefabPath = "Assets/_Project/Resources/OfficeDressing/OfisDekor.prefab";
    public const string ArtDir = "Assets/_Project/Art/OfficeDetail";
    public const string MeshPath = ArtDir + "/OfisDekor_Meshes.asset";
    public const string MatDir = ArtDir + "/Materials";
    public const string TexDir = ArtDir + "/Textures";
    public const string MapTexture = TexDir + "/SelVadisi_Harita.png";
    public static readonly string[] PhotoTextures = { TexDir + "/SelVadisi_Foto1.png", TexDir + "/SelVadisi_Foto2.png", TexDir + "/SelVadisi_Foto3.png" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static Dictionary<string, Material> office;
    private static Dictionary<Material, MB> groups;
    private static Transform root;
    private static System.Random rng;

    [MenuItem("Rescue Team/Ofis-Garaj/Ofis Dekorunu Kur")]
    public static void Menu() { Build(); }

    public static void BuildBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("OfficeDressingBuilder: " + e); EditorApplication.Exit(1); }
    }

    public static void Build()
    {
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        RenderPicturesIfMissing();
        office = OfficeMaterials();
        groups = new Dictionary<Material, MB>();
        rng = new System.Random(20261106);
        lights = 0;
        var go = new GameObject("18_OfficeDressing");
        root = go.transform;

        DressOffice();
        DressKitchen();
        DressStorage();
        DressLockerRoom();
        DressRingRoom();

        // meshes: one per material, saved in one container asset
        var meshes = new List<Mesh>();
        foreach (var kv in groups)
        {
            if (kv.Value.Count == 0) continue;
            var mesh = kv.Value.ToMesh("Dekor_" + kv.Key.name);
            meshes.Add(mesh);
            var part = new GameObject("Dekor_" + kv.Key.name);
            part.transform.SetParent(root, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = kv.Key;
            mr.shadowCastingMode = kv.Key.name.Contains("Glow") || kv.Key.name.Contains("Screen") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
        if (File.Exists(MeshPath)) AssetDatabase.DeleteAsset(MeshPath);
        if (meshes.Count > 0)
        {
            AssetDatabase.CreateAsset(meshes[0], MeshPath);                 // first mesh is the container's main asset
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
        }
        AssetDatabase.SaveAssets();
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        int verts = meshes.Sum(m => m.vertexCount);
        Debug.Log("[OfficeDressing] prefab " + PrefabPath + ": " + meshes.Count + " malzeme grubu, " + verts + " köşe, " + lights + " ışık");
    }

    // ------------------------------------------------------------------ helpers

    private static int lights;

    private static Dictionary<string, Material> OfficeMaterials()
    {
        var d = new Dictionary<string, Material>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Art/Models/Environment/Office" }))
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Material>())
                if (!d.ContainsKey(m.name)) d[m.name] = m;
        return d;
    }

    private static Material Office(string name)
    {
        if (office.TryGetValue(name, out var m)) return m;
        Debug.LogWarning("[OfficeDressing] ofis malzemesi yok: " + name);
        return Mat("Dekor_" + name, new Color(0.6f, 0.6f, 0.6f), 0.4f);
    }

    // New URP Lit material under Art/OfficeDetail/Materials (created once, updated on rebuild).
    private static Material Mat(string name, Color col, float smooth, Texture tex = null, float emission = 0f, string detail = null, string detailNormal = null, float tiling = 4f)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        m.SetTexture("_BaseMap", tex);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", (tex != null ? Color.white : col) * emission);
            if (tex != null) m.SetTexture("_EmissionMap", tex);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
        if (detail != null)
        {
            m.SetTexture("_DetailAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(OfficeImportSettings.DetailFolder + detail + ".png"));
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(tiling, tiling));
            if (detailNormal != null) { m.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(detailNormal)); m.SetFloat("_DetailNormalMapScale", 0.5f); }
            m.EnableKeyword("_DETAIL_MULX2");
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static MB G(Material m)
    {
        if (!groups.TryGetValue(m, out var b)) { b = new MB(); groups[m] = b; }
        return b;
    }

    private static readonly Color32 W = new Color32(255, 255, 255, 255);

    // axis-aligned box from centre and size
    private static void Box(Material m, Vector3 c, Vector3 s, int skip = 0) => G(m).Box(c, s, W, skip);
    // box rotated about y around its base centre
    private static void BoxY(Material m, Vector3 basePos, Vector3 size, float yaw) => G(m).BoxRot(basePos, size, yaw, W, 0);

    private static void Light(string name, Vector3 pos, Color col, float intensity, float range, LightType type = LightType.Point, Vector3? dir = null, float spot = 60f, bool shadows = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = pos;
        if (dir.HasValue) go.transform.rotation = Quaternion.LookRotation(dir.Value);
        var l = go.AddComponent<Light>();
        l.type = type; l.color = col; l.intensity = intensity; l.range = range;
        if (type == LightType.Spot) { l.spotAngle = spot; l.innerSpotAngle = spot * 0.55f; }
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        lights++;
    }

    // vertical picture/quad facing 'normal' (textured, two triangles with uvs, written straight into a new mesh group)
    private static void Picture(Material m, Vector3 c, Vector3 normal, float w, float h, bool twoSided = true)
    {
        var b = G(m);
        Vector3 n = normal.normalized, right = Vector3.Cross(Vector3.up, n).normalized, up = Vector3.up;
        Vector3 a0 = c - right * w * 0.5f - up * h * 0.5f, a1 = c + right * w * 0.5f - up * h * 0.5f, a2 = c + right * w * 0.5f + up * h * 0.5f, a3 = c - right * w * 0.5f + up * h * 0.5f;
        int i0 = b.Vert(a0, W), i1 = b.Vert(a1, W), i2 = b.Vert(a2, W), i3 = b.Vert(a3, W);
        b.U[i0] = new Vector2(1f, 0f); b.U[i1] = new Vector2(0f, 0f); b.U[i2] = new Vector2(0f, 1f); b.U[i3] = new Vector2(1f, 1f);
        b.Tri(i0, i1, i2); b.Tri(i0, i2, i3);
        if (twoSided)
        {
            int j0 = b.Vert(a0, W), j1 = b.Vert(a1, W), j2 = b.Vert(a2, W), j3 = b.Vert(a3, W);
            b.U[j0] = new Vector2(1f, 0f); b.U[j1] = new Vector2(0f, 0f); b.U[j2] = new Vector2(0f, 1f); b.U[j3] = new Vector2(1f, 1f);
            b.Tri(j0, j2, j1); b.Tri(j0, j3, j2);
        }
    }

    // picture with a wooden frame on a wall (normal points into the room)
    private static void Framed(Material image, Vector3 c, Vector3 normal, float w, float h, float border = 0.035f)
    {
        var frame = Office("M_TimberEdge");
        Vector3 n = normal.normalized, right = Vector3.Cross(Vector3.up, n).normalized;
        float d = 0.025f;
        Vector3 back = c - n * d * 0.5f;
        // four frame bars
        FrameBar(frame, back + Vector3.up * (h * 0.5f + border * 0.5f), right, w + border * 2f, border, d, n);
        FrameBar(frame, back - Vector3.up * (h * 0.5f + border * 0.5f), right, w + border * 2f, border, d, n);
        FrameBar(frame, back + right * (w * 0.5f + border * 0.5f), right, border, h, d, n);
        FrameBar(frame, back - right * (w * 0.5f + border * 0.5f), right, border, h, d, n);
        Box(Office("M_CreamPaper"), back - n * 0.002f, Abs(right * (w + 0.01f) + Vector3.up * (h + 0.01f) + n * 0.004f));
        Picture(image, c + n * 0.003f, n, w, h);
    }

    private static void FrameBar(Material m, Vector3 c, Vector3 right, float w, float h, float d, Vector3 n)
    {
        Box(m, c, Abs(right * w + Vector3.up * h + n * d));
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // curtain panel: vertical folds between x/z extent along 'along' at depth 'depth' (world), hanging from top to bottom
    private static void Curtain(Material m, Vector3 start, Vector3 end, float top, float bottom, Vector3 n, int folds)
    {
        var b = G(m);
        int nu = folds * 4, nv = 4;
        for (int side = 0; side < 2; side++)
        {
            var idx = new int[nu + 1, nv + 1];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float u = i / (float)nu, v = j / (float)nv;
                    Vector3 p = Vector3.Lerp(start, end, u);
                    float wave = Mathf.Sin(u * folds * Mathf.PI * 2f) * 0.022f * (0.8f + 0.4f * v);
                    p += n * (wave + 0.025f - side * 0.002f);
                    p.y = Mathf.Lerp(bottom, top, v);
                    idx[i, j] = b.Vert(p, W);
                }
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    int a = idx[i, j], c1 = idx[i + 1, j], d1 = idx[i + 1, j + 1], e = idx[i, j + 1];
                    if (side == 0) { b.Tri(a, d1, c1); b.Tri(a, e, d1); }   // one face per grid, the two grids face opposite ways
                    else { b.Tri(a, c1, d1); b.Tri(a, d1, e); }
                }
        }
    }

    private static void Rod(Material m, Vector3 a, Vector3 c, float r) => G(m).Rod(a, c, r, 6, W);

    private static void Plant(Vector3 basePos, float scale)
    {
        G(Office("M_Terracotta")).Cyl(basePos, 0.13f * scale, 0.28f * scale, 10, W, 0.16f * scale, false, true);
        G(Office("M_PottingSoil")).Cyl(basePos + Vector3.up * 0.25f * scale, 0.145f * scale, 0.01f, 10, W);
        string[] leaf = { "M_FoliageDeep", "M_FoliageOlive", "M_FoliageSunlit" };
        Vector3 stemBase = basePos + Vector3.up * 0.26f * scale;
        for (int k = 0; k < 11; k++)
        {
            float a = k * 2.4f, h = (0.35f + 0.55f * (k / 11f)) * scale, r = (0.12f + 0.1f * ((k * 7) % 3)) * scale;
            Vector3 tip = stemBase + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
            Rod(Office("M_FoliageDeep"), stemBase, tip, 0.005f * scale);
            // three leaves per stem, the outer one drooping, each a long thin ellipsoid along the stem direction
            for (int q = 0; q < 3; q++)
            {
                float t = 0.55f + 0.22f * q;
                Vector3 at = Vector3.Lerp(stemBase, tip, t) + new Vector3(Mathf.Cos(a + q) * 0.03f, -0.03f * q, Mathf.Sin(a + q) * 0.03f) * scale;
                Vector3 dir = (tip - stemBase).normalized + new Vector3(Mathf.Cos(a), -0.6f - 0.3f * q, Mathf.Sin(a)) * 0.7f;
                var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                var g = G(Office(leaf[(k + q) % 3]));
                g.Push(at, rot);
                g.Ico(new Vector3(0f, 0f, 0.07f * scale), new Vector3(0.045f, 0.012f, 0.11f) * scale, W, 1, 0.08f, rng);
                g.Pop();
            }
        }
    }

    // ------------------------------------------------------------------ rooms (coordinates: office_layout.json, world = layout)

    private static void DressOffice()
    {
        // 1) jaluzi on the south window (glass z=-5.30, x[-1.94,-0.46], y[0.93,2.05]); slats inside the room
        var blind = Mat("Dekor_Jaluzi", new Color(0.9f, 0.88f, 0.83f), 0.45f);
        float bz = -5.13f;
        Box(blind, new Vector3(-1.2f, 2.09f, bz), new Vector3(1.58f, 0.045f, 0.055f));
        for (int i = 0; i < 13; i++)
        {
            float y = 2.03f - i * 0.052f;
            G(blind).Push(new Vector3(-1.2f, y, bz), Quaternion.Euler(28f, 0f, 0f));
            G(blind).Box(Vector3.zero, new Vector3(1.5f, 0.004f, 0.045f), W, 0);
            G(blind).Pop();
        }
        Box(blind, new Vector3(-1.2f, 2.03f - 13 * 0.052f, bz), new Vector3(1.52f, 0.02f, 0.05f));
        Rod(Office("M_SilverSteel"), new Vector3(-0.5f, 2.07f, bz + 0.03f), new Vector3(-0.5f, 1.25f, bz + 0.03f), 0.003f);

        // 2) high shelves on the north wall (inner face z≈1.12), clear of the storage door x[-3.0,-1.8]
        foreach (float cx in new[] { -3.9f, -0.95f })
        {
            Box(Office("M_TimberEdge"), new Vector3(cx, 2.25f, 1.0f), new Vector3(1.3f, 0.03f, 0.24f));
            foreach (float s in new[] { -0.5f, 0.5f }) Box(Office("M_Anthracite"), new Vector3(cx + s, 2.17f, 1.08f), new Vector3(0.03f, 0.14f, 0.08f));
        }
        string[] binder = { "M_ServiceBlue", "M_SafetyOrange", "M_FilingGreen", "M_CobaltBlue", "M_InkGray", "M_SafetyOchre" };
        float bx = -4.45f;
        for (int i = 0; i < 9; i++)
        {
            float w = R(0.055f, 0.075f);
            Box(Office(binder[i % binder.Length]), new Vector3(bx + w * 0.5f, 2.265f + 0.14f, 1.0f), new Vector3(w * 0.92f, 0.28f, 0.21f));
            bx += w;
        }
        // safety helmet and a trophy on the first shelf
        G(Office("M_SafetyOrange")).Ico(new Vector3(-3.5f, 2.33f, 1.0f), new Vector3(0.13f, 0.09f, 0.15f), W, 1, 0f, rng);
        Box(Office("M_SafetyOrange"), new Vector3(-3.5f, 2.275f, 0.92f), new Vector3(0.18f, 0.01f, 0.1f));
        G(Office("M_SafetyOchre")).Cyl(new Vector3(-3.29f, 2.265f, 1.0f), 0.04f, 0.05f, 8, W);
        G(Office("M_SafetyOchre")).Cyl(new Vector3(-3.29f, 2.315f, 1.0f), 0.012f, 0.07f, 6, W);
        G(Office("M_SafetyOchre")).Cyl(new Vector3(-3.29f, 2.385f, 1.0f), 0.03f, 0.07f, 8, W, 0.055f);
        // second shelf: radio, boxes, a trailing plant
        Box(Office("M_CharcoalABS"), new Vector3(-1.4f, 2.32f, 1.0f), new Vector3(0.26f, 0.11f, 0.12f));
        Rod(Office("M_Anthracite"), new Vector3(-1.3f, 2.37f, 1.0f), new Vector3(-1.28f, 2.62f, 1.0f), 0.004f);
        Box(Office("M_LedGreen"), new Vector3(-1.48f, 2.34f, 0.935f), new Vector3(0.02f, 0.012f, 0.004f));
        Box(Office("M_Fibreboard"), new Vector3(-0.98f, 2.37f, 1.0f), new Vector3(0.32f, 0.2f, 0.2f));
        Box(Office("M_Fibreboard"), new Vector3(-0.98f, 2.52f, 1.0f), new Vector3(0.26f, 0.1f, 0.18f));
        G(Office("M_Terracotta")).Cyl(new Vector3(-0.5f, 2.265f, 1.0f), 0.06f, 0.1f, 8, W, 0.07f);
        for (int k = 0; k < 7; k++)
        {
            float a = k * 0.9f;
            Vector3 p0 = new Vector3(-0.5f, 2.36f, 1.0f), p1 = p0 + new Vector3(Mathf.Cos(a) * 0.12f, -0.25f - 0.08f * (k % 3), -0.05f + Mathf.Sin(a) * 0.04f);
            Rod(Office("M_FoliageOlive"), p0, p1, 0.004f);
            G(Office(k % 2 == 0 ? "M_FoliageSunlit" : "M_FoliageDeep")).Ico(p1, new Vector3(0.05f, 0.03f, 0.04f), W, 0, 0.1f, rng);
        }

        // 3) Sel Vadisi operations map on the east wall (inner face x≈-0.08), z[-4.95,-3.7] (garage door z[-3.4,-2.36] stays clear)
        var map = AssetDatabase.LoadAssetAtPath<Texture2D>(MapTexture);
        var mapMat = Mat("Dekor_SelVadisiHarita", Color.white, 0.25f, map);
        Framed(mapMat, new Vector3(-0.1f, 1.45f, -4.32f), Vector3.left, 1.15f, 0.72f);
        var pin = Mat("Dekor_Igne", new Color(0.86f, 0.14f, 0.12f), 0.55f);
        Vector3[] pins = { new Vector3(-0.115f, 1.62f, -4.55f), new Vector3(-0.115f, 1.38f, -4.18f), new Vector3(-0.115f, 1.52f, -3.98f), new Vector3(-0.115f, 1.27f, -4.66f) };
        foreach (var p in pins) G(pin).Ico(p, Vector3.one * 0.016f, W, 0, 0f, rng);
        var red = Mat("Dekor_Ip", new Color(0.75f, 0.1f, 0.1f), 0.3f);
        for (int i = 0; i + 1 < pins.Length; i++) Rod(red, pins[i], pins[i + 1], 0.0018f);

        // 4) framed flood photos on the south wall (inner face z≈-5.12): left of the front door and between door and window
        var photos = PhotoTextures.Select((p, i) => Mat("Dekor_Foto" + (i + 1), Color.white, 0.3f, AssetDatabase.LoadAssetAtPath<Texture2D>(p))).ToArray();
        Framed(photos[0], new Vector3(-4.25f, 1.38f, -5.155f), Vector3.forward, 0.5f, 0.34f);
        Framed(photos[1], new Vector3(-4.25f, 1.92f, -5.155f), Vector3.forward, 0.4f, 0.28f);
        Framed(photos[2], new Vector3(-2.3f, 1.55f, -5.155f), Vector3.forward, 0.3f, 0.4f);

        // 5) desk lamp at the back-left of the desk (desk top y=0.54, x[-1.655,-0.445], z[-0.415,0.215])
        var glow = Mat("Dekor_LambaGlow", new Color(1f, 0.9f, 0.72f), 0.5f, null, 2.4f);
        Vector3 lb = new Vector3(-1.58f, 0.54f, -0.33f);
        G(Office("M_Anthracite")).Cyl(lb, 0.05f, 0.014f, 12, W);
        Vector3 elbow = lb + new Vector3(0.02f, 0.2f, 0.03f), head = elbow + new Vector3(0.1f, 0.05f, 0.1f);
        Rod(Office("M_Anthracite"), lb + Vector3.up * 0.014f, elbow, 0.006f);
        Rod(Office("M_Anthracite"), elbow, head, 0.006f);
        G(Office("M_SafetyOchre")).Push(head, Quaternion.Euler(150f, 45f, 0f));
        G(Office("M_SafetyOchre")).Cyl(Vector3.zero, 0.03f, 0.07f, 12, W, 0.065f, false, false);
        G(Office("M_SafetyOchre")).Pop();
        G(glow).Ico(head + new Vector3(0.012f, -0.04f, 0.012f), Vector3.one * 0.018f, W, 0, 0f, rng);
        Light("MasaLambasi_Isik", head + new Vector3(0.03f, -0.09f, 0.03f), new Color(1f, 0.82f, 0.58f), 0.9f, 1.5f);

        // 6) monitor screen glow (Monitor_01 at (-1.05,0.54,-0.02); panel 0.40×0.21 centred 0.20 up, facing the chair +z)
        var screenTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Textures/service_ui.png");
        var screen = Mat("Dekor_EkranGlow", Color.white, 0.8f, screenTex, 0.9f);
        Picture(screen, new Vector3(-1.05f, 0.74f, 0.0045f), Vector3.forward, 0.385f, 0.198f, false);

        // 7) radio base station on FilingCabinet_01 (top y=1.48, x=-4.42, z=0.82)
        Box(Office("M_CharcoalABS"), new Vector3(-4.42f, 1.54f, 0.82f), new Vector3(0.18f, 0.12f, 0.28f));
        Box(Office("M_ScreenDark"), new Vector3(-4.33f, 1.56f, 0.82f), new Vector3(0.004f, 0.05f, 0.12f));
        Box(Office("M_LedGreen"), new Vector3(-4.328f, 1.585f, 0.9f), new Vector3(0.004f, 0.012f, 0.012f));
        Rod(Office("M_Anthracite"), new Vector3(-4.47f, 1.6f, 0.73f), new Vector3(-4.47f, 1.95f, 0.73f), 0.004f);
        Box(Office("M_CharcoalABS"), new Vector3(-4.36f, 1.515f, 0.62f), new Vector3(0.06f, 0.07f, 0.1f));

        // 8) tall plant in the north-east corner (clear of PC_01 and WallBoard_01)
        Plant(new Vector3(-0.51f, 0f, 0.86f), 0.85f);

        // 10) kurtarma ekibinin masası ve dolabı: yazıcı (FilingCabinet_02 üstü, y=1.48), çalışma masasında çikolata ve şekerler
        GearItem("Aletler/ElektrikliAletlerSeti.fbx", "LASER_PRINTER_MFP", new Vector3(-4.42f, 1.48f, -3.55f), 90f);
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "CHOCOLATE", new Vector3(-0.86f, 0.54f, 0.13f), 10f);
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "BONBON_RED", new Vector3(-1.3f, 0.54f, 0.14f), 30f);
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "BONBON_GOLD", new Vector3(-1.22f, 0.54f, 0.18f), -25f);

        // 9) wall AC unit above the kitchen door (west wall x≈-4.72, door z[-2.6,-1.4]; OperationsBoard is at z<-2.655)
        var acMat = Office("M_FridgeWhite");
        Box(acMat, new Vector3(-4.66f, 2.62f, -2.0f), new Vector3(0.18f, 0.24f, 0.78f));
        Box(Office("M_InkGray"), new Vector3(-4.565f, 2.53f, -2.0f), new Vector3(0.01f, 0.025f, 0.7f));
        // smoke detector (no 'Ceiling' in names)
        G(Office("M_FridgeWhite")).Cyl(new Vector3(-2.4f, 3.205f, -2.2f), 0.06f, 0.035f, 12, W, -1f, true, true);
    }

    private static void DressKitchen()
    {
        // pendant lamps over DiningTable_01 (-7.35, -3.35)
        var glow = Mat("Dekor_LambaGlow", new Color(1f, 0.9f, 0.72f), 0.5f, null, 2.4f);
        var shade = Mat("Dekor_Abajur", new Color(0.16f, 0.32f, 0.34f), 0.35f);
        foreach (float x in new[] { -7.7f, -7.0f })
        {
            Vector3 top = new Vector3(x, 3.24f, -3.35f), lamp = new Vector3(x, 1.78f, -3.35f);
            Rod(Office("M_CharcoalTrim"), top, lamp + Vector3.up * 0.1f, 0.004f);
            G(shade).Cyl(lamp, 0.15f, 0.1f, 14, W, 0.04f, true, false);
            G(glow).Ico(lamp + Vector3.up * 0.02f, Vector3.one * 0.035f, W, 0, 0f, rng);
            Light("Sarkit_Isik", lamp - Vector3.up * 0.05f, new Color(1f, 0.84f, 0.62f), 0.75f, 2.4f);
        }
        // curtains on the west window (z[-3.74,-2.26], inner wall face x≈-9.72)
        var curtain = Mat("Dekor_PerdeHardal", new Color(0.78f, 0.55f, 0.22f), 0.15f, null, 0f, "OfficeDetail_Fabric", OfficeImportSettings.CraftTextures + "Cloth_Normal.png", 6f);
        CurtainPair(curtain, -9.72f, -3.74f, -2.26f, true);
        // runner rug in front of the counter (front face x≈-9.13)
        Box(Mat("Dekor_Yolluk", new Color(0.55f, 0.18f, 0.14f), 0.1f, null, 0f, "OfficeDetail_Fabric", null, 8f), new Vector3(-8.78f, 0.006f, -1.05f), new Vector3(0.58f, 0.012f, 1.6f));
        Box(Office("M_SafetyOchre"), new Vector3(-8.78f, 0.0125f, -1.05f), new Vector3(0.46f, 0.002f, 1.48f), 0);
        Box(Mat("Dekor_Yolluk", new Color(0.55f, 0.18f, 0.14f), 0.1f, null, 0f, "OfficeDetail_Fabric", null, 8f), new Vector3(-8.78f, 0.014f, -1.05f), new Vector3(0.4f, 0.002f, 1.42f), 0);
        // sofa cushions and a throw (Sofa_01 at (-7.35,0,-4.70), 1.56×0.66×0.72, back to the south wall)
        var cushionA = Mat("Dekor_MinderHardal", new Color(0.82f, 0.62f, 0.2f), 0.12f, null, 0f, "OfficeDetail_Fabric", OfficeImportSettings.CraftTextures + "Cloth_Normal.png", 6f);
        var cushionB = Mat("Dekor_MinderPetrol", new Color(0.13f, 0.38f, 0.4f), 0.12f, null, 0f, "OfficeDetail_Fabric", OfficeImportSettings.CraftTextures + "Cloth_Normal.png", 6f);
        foreach (var (x, m) in new[] { (-7.78f, cushionA), (-6.95f, cushionB) })
        {
            G(m).Push(new Vector3(x, 0.50f, -4.73f), Quaternion.Euler(-14f, x < -7.3f ? 8f : -6f, 0f));
            G(m).Ico(Vector3.zero, new Vector3(0.2f, 0.17f, 0.07f), W, 1, 0.02f, rng);
            G(m).Pop();
        }
        Box(cushionB, new Vector3(-6.65f, 0.515f, -4.68f), new Vector3(0.17f, 0.025f, 0.5f));
        Box(cushionB, new Vector3(-6.568f, 0.39f, -4.68f), new Vector3(0.02f, 0.27f, 0.48f));
        // fruit bowl and mugs on the dining table (top y=0.56)
        G(Office("M_CreamCeramic")).Cyl(new Vector3(-7.35f, 0.56f, -3.35f), 0.07f, 0.05f, 12, W, 0.11f, false, true);
        // kâsenin içi (Cyl yalnız dış yüz üretir) ve tabanı
        for (int k = 0; k < 12; k++)
        {
            float a0 = k * Mathf.PI / 6f, a1 = (k + 1) * Mathf.PI / 6f;
            Vector3 cb = new Vector3(-7.35f, 0.564f, -3.35f), ct = new Vector3(-7.35f, 0.61f, -3.35f);
            Vector3 b0 = cb + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.066f, b1 = cb + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.066f;
            Vector3 t0 = ct + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.104f, t1 = ct + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.104f;
            G(Office("M_CreamCeramic")).Quad(b0, t0, t1, b1, W, -new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f)));
        }
        G(Office("M_CreamCeramic")).Cyl(new Vector3(-7.35f, 0.56f, -3.35f), 0.066f, 0.004f, 12, W);
        Color[] fruit = { new Color(0.95f, 0.5f, 0.08f), new Color(0.8f, 0.12f, 0.1f), new Color(0.95f, 0.85f, 0.2f), new Color(0.45f, 0.7f, 0.2f) };
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.1f;
            var fm = Mat("Dekor_Meyve" + (i % 4), fruit[i % 4], 0.45f);
            G(fm).Ico(new Vector3(-7.35f + Mathf.Cos(a) * 0.045f, 0.625f + (i > 3 ? 0.035f : 0f), -3.35f + Mathf.Sin(a) * 0.045f), Vector3.one * 0.032f, W, 1, 0.04f, rng);
        }
        foreach (var p in new[] { new Vector3(-7.72f, 0.56f, -3.2f), new Vector3(-6.98f, 0.56f, -3.48f) })
        {
            G(Office("M_CreamCeramic")).Cyl(p, 0.026f, 0.06f, 10, W);
            G(Office("M_Coffee")).Cyl(p + Vector3.up * 0.0605f, 0.022f, 0.002f, 10, W);
        }
        // kettle on the counter (top y=0.60)
        G(Office("M_SilverSteel")).Cyl(new Vector3(-9.4f, 0.6f, -1.6f), 0.07f, 0.13f, 12, W, 0.055f);
        Rod(Office("M_CharcoalABS"), new Vector3(-9.4f, 0.75f, -1.66f), new Vector3(-9.4f, 0.75f, -1.54f), 0.008f);
        // pizza gecesi: koltuk yanındaki sehpada (SideTable_01, üst y=0.42) tahtada pizza; yemek masasında kola kutusu ve bardağı
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "PIZZA_BOARD", new Vector3(-6.15f, 0.42f, -4.65f), 20f);
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "PIZZA", new Vector3(-6.15f, 0.436f, -4.65f), 20f);
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "COLA_CAN", new Vector3(-6.92f, 0.56f, -3.2f), 0f);
        GearItem("YiyecekIcecek/YiyecekIcecekSeti.fbx", "COLA_GLASS", new Vector3(-7.08f, 0.56f, -3.58f), 0f);
    }

    // kurtarma ekipmanı / yiyecek setlerinden bir öğeyi (kök tabanı merkezinde) dekor köküne yerleştirir
    private static void GearItem(string setRel, string item, Vector3 pos, float yaw)
    {
        var set = RescueGearAssembly.Spawn(setRel, root, Vector3.zero, Quaternion.identity);
        PrefabUtility.UnpackPrefabInstance(set, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var t = RescueGearAssembly.Find(set.transform, item);
        if (t != null)
        {
            t.SetParent(root, true);
            t.rotation = Quaternion.Euler(0f, yaw, 0f) * t.rotation;
            t.position = pos;
            t.name = "Dekor_" + item;
        }
        else Debug.LogWarning("[OfficeDressing] setde yok: " + item);
        UnityEngine.Object.DestroyImmediate(set);
    }

    private static void DressStorage()
    {
        // MetalShelf_01-03 at x -3.85, -2.40, -0.95, z 4.82, 1.2 wide, 0.45 deep; boards at y .12 .55 .98 1.40
        float[] boards = { 0.12f, 0.55f, 0.98f, 1.40f };
        string[] crate = { "M_Fibreboard", "M_Fibreboard", "M_CharcoalABS", "M_SafetyOrange", "M_ToolboxRed", "M_TealMetal" };
        foreach (float sx in new[] { -3.85f, -2.40f, -0.95f })
            foreach (float by in boards)
            {
                float x = sx - 0.55f;
                while (x < sx + 0.5f)
                {
                    float w = R(0.18f, 0.34f);
                    if (x + w > sx + 0.56f) break;
                    double u = rng.NextDouble();
                    Vector3 c = new Vector3(x + w * 0.5f, by, 4.82f + R(-0.04f, 0.04f));
                    if (u < 0.12) { x += w * 0.6f; continue; }   // a gap
                    if (u < 0.3)
                    {
                        // paint cans / buckets
                        G(Office("M_GalvanizedSteel")).Cyl(c, 0.07f, 0.16f, 10, W);
                        G(Office("M_GalvanizedSteel")).Cyl(c + new Vector3(0.15f, 0f, 0f), 0.06f, 0.13f, 10, W);
                        x += 0.3f; continue;
                    }
                    if (u < 0.4 && by < 1.0f)
                    {
                        // rope coil
                        var pts = new List<Vector3>();
                        for (int k = 0; k <= 16; k++) { float a = k / 16f * Mathf.PI * 2f; pts.Add(c + new Vector3(Mathf.Cos(a) * 0.1f, 0.022f, Mathf.Sin(a) * 0.1f)); }
                        G(Office("M_SafetyOchre")).Tube(pts, 0.025f, 6, W);
                        x += 0.26f; continue;
                    }
                    float h = R(0.16f, Mathf.Min(0.36f, by < 1.3f ? 0.38f : 0.3f));
                    Box(Office(crate[rng.Next(crate.Length)]), c + Vector3.up * h * 0.5f, new Vector3(w * 0.94f, h, R(0.26f, 0.38f)));
                    x += w;
                }
            }
    }

    private static void DressLockerRoom()
    {
        var curtain = Mat("Dekor_PerdeKrem", new Color(0.86f, 0.82f, 0.72f), 0.15f, null, 0f, "OfficeDetail_Fabric", OfficeImportSettings.CraftTextures + "Cloth_Normal.png", 6f);
        CurtainPair(curtain, -9.72f, 2.46f, 3.94f, true);
        // rescue gear on the east wall past the lockers (inner face x≈-4.88, z 4.6)
        var ringMat = Office("M_SafetyOrange");
        var pts = new List<Vector3>();
        for (int k = 0; k <= 24; k++) { float a = k / 24f * Mathf.PI * 2f; pts.Add(new Vector3(-4.93f, 1.55f + Mathf.Sin(a) * 0.2f, 4.6f + Mathf.Cos(a) * 0.2f)); }
        G(ringMat).Tube(pts, 0.055f, 8, W);
        for (int q = 0; q < 4; q++)
        {
            float a = q * Mathf.PI * 0.5f + 0.4f;
            var seg = new List<Vector3>();
            for (int k = 0; k <= 3; k++) { float b = a + k * 0.12f; seg.Add(new Vector3(-4.925f, 1.55f + Mathf.Sin(b) * 0.2f, 4.6f + Mathf.Cos(b) * 0.2f)); }
            G(Office("M_ReflectiveTrim")).Tube(seg, 0.058f, 8, W);
        }
        Box(Office("M_Anthracite"), new Vector3(-4.86f, 1.8f, 4.6f), new Vector3(0.04f, 0.04f, 0.12f));
        var coil = new List<Vector3>();
        for (int k = 0; k <= 40; k++) { float a = k / 40f * Mathf.PI * 4f; coil.Add(new Vector3(-4.92f, 0.95f + Mathf.Sin(a) * 0.14f + k * 0.001f, 4.6f + Mathf.Cos(a) * 0.12f)); }
        G(Office("M_SafetyOchre")).Tube(coil, 0.018f, 6, W);
        Box(Office("M_Anthracite"), new Vector3(-4.86f, 1.1f, 4.6f), new Vector3(0.04f, 0.04f, 0.1f));
    }

    private static void DressRingRoom()
    {
        // hero spots over BoxingRing_01 (-4.9, 8.6)
        foreach (float x in new[] { -6.0f, -3.8f })
        {
            G(Office("M_Anthracite")).Cyl(new Vector3(x, 3.10f, 8.6f), 0.09f, 0.14f, 10, W, 0.07f, true, true);
            Light("Ring_Spot", new Vector3(x, 3.06f, 8.6f), new Color(1f, 0.95f, 0.86f), 3.2f, 6f, LightType.Spot, Vector3.down + new Vector3(x < -4.9f ? 0.12f : -0.12f, 0f, 0f), 68f);
        }
        // mat under the punching bag (-1.25, 9.80)
        Box(Office("M_DarkRubber"), new Vector3(-1.25f, 0.006f, 9.8f), new Vector3(1.1f, 0.012f, 1.1f));
        // pennant string along the north wall (inner face z≈11.92), no text
        string[] flagCols = { "Dekor_FlamaKirmizi", "Dekor_FlamaBeyaz", "Dekor_FlamaMavi", "Dekor_FlamaSari" };
        Color[] fc = { new Color(0.85f, 0.16f, 0.14f), new Color(0.94f, 0.93f, 0.9f), new Color(0.13f, 0.32f, 0.72f), new Color(0.95f, 0.75f, 0.15f) };
        int nflags = 20;
        Vector3 a0 = new Vector3(-9.78f, 2.75f, 11.985f), a1 = new Vector3(-0.14f, 2.75f, 11.985f);
        var cord = new List<Vector3>();
        for (int k = 0; k <= 30; k++) { float u = k / 30f; var p = Vector3.Lerp(a0, a1, u); p.y -= Mathf.Sin(u * Mathf.PI) * 0.22f; cord.Add(p); }
        G(Office("M_CharcoalTrim")).Tube(cord, 0.004f, 4, W);
        for (int i = 0; i < nflags; i++)
        {
            float u = (i + 0.5f) / nflags;
            var p = Vector3.Lerp(a0, a1, u); p.y -= Mathf.Sin(u * Mathf.PI) * 0.22f;
            var fm = Mat(flagCols[i % 4], fc[i % 4], 0.2f);
            var b = G(fm);
            float hw = 0.14f;
            Vector3 q0 = p + new Vector3(-hw, 0f, -0.01f), q1 = p + new Vector3(hw, 0f, -0.01f), q2 = p + new Vector3(0f, -0.26f, -0.02f);
            b.Tri(b.Vert(q0, W), b.Vert(q1, W), b.Vert(q2, W));
            b.Tri(b.Vert(q0, W), b.Vert(q2, W), b.Vert(q1, W));
        }
        // curtains: west window z[7.76,9.44], north window x[-5.74,-4.06] (inner face z≈11.92)
        var curtain = Mat("Dekor_PerdePetrol", new Color(0.15f, 0.36f, 0.4f), 0.15f, null, 0f, "OfficeDetail_Fabric", OfficeImportSettings.CraftTextures + "Cloth_Normal.png", 6f);
        CurtainPair(curtain, -9.72f, 7.76f, 9.44f, true);
        CurtainPair(curtain, 11.9f, -5.74f, -4.06f, false);
    }

    // curtain pair beside a window: wallX (or wallZ) is the inner wall face, a..b the opening along the wall
    private static void CurtainPair(Material m, float wall, float a, float b, bool westWall)
    {
        float top = 2.16f, bottom = 0.86f, w = 0.42f;
        var rod = Office("M_Anthracite");
        if (westWall)
        {
            float x = wall + 0.07f;
            Rod(rod, new Vector3(x, top + 0.04f, a - w - 0.05f), new Vector3(x, top + 0.04f, b + w + 0.05f), 0.012f);
            Curtain(m, new Vector3(x - 0.01f, 0f, a - w + 0.02f), new Vector3(x - 0.01f, 0f, a + 0.12f), top, bottom, Vector3.right, 4);
            Curtain(m, new Vector3(x - 0.01f, 0f, b - 0.12f), new Vector3(x - 0.01f, 0f, b + w - 0.02f), top, bottom, Vector3.right, 4);
        }
        else
        {
            float z = wall - 0.07f;
            Rod(rod, new Vector3(a - w - 0.05f, top + 0.04f, z), new Vector3(b + w + 0.05f, top + 0.04f, z), 0.012f);
            Curtain(m, new Vector3(a - w + 0.02f, 0f, z + 0.01f), new Vector3(a + 0.12f, 0f, z + 0.01f), top, bottom, Vector3.back, 4);
            Curtain(m, new Vector3(b - 0.12f, 0f, z + 0.01f), new Vector3(b + w - 0.02f, 0f, z + 0.01f), top, bottom, Vector3.back, 4);
        }
    }

    // ------------------------------------------------------------------ pictures from the flood map

    // Renders a top-down map and three photos of Sel Vadisi into Art/OfficeDetail/Textures (once; delete them to re-render).
    public static void RenderPicturesIfMissing()
    {
        Directory.CreateDirectory(TexDir);
        bool missing = !File.Exists(MapTexture) || PhotoTextures.Any(p => !File.Exists(p));
        if (!missing) return;
        if (!File.Exists(SVBuild.ScenePath)) { Debug.LogWarning("[OfficeDressing] Sel Vadisi sahnesi yok; harita ve fotoğraflar boş kalır"); return; }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var active = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(SVBuild.ScenePath, OpenSceneMode.Single);
        ShaderUtil.allowAsyncCompilation = false;
        var go = new GameObject("PictureCam");
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 3000f; cam.nearClipPlane = 0.3f;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        // map: orthographic, valley centre, north up
        cam.orthographic = true; cam.orthographicSize = 230f;
        float cx = SelVadisi.Valley.CenterX(-60f);
        go.transform.SetPositionAndRotation(new Vector3(cx, 600f, -60f), Quaternion.Euler(90f, 0f, 0f));
        Save(cam, MapTexture, 1024, 640);
        cam.orthographic = false;
        var shots = SVBuild.Shots();
        int[] use = { 0, 1, 6 };
        for (int i = 0; i < 3; i++)
        {
            cam.fieldOfView = 50f;
            go.transform.SetPositionAndRotation(shots[use[i]].position, Quaternion.Euler(shots[use[i]].euler));
            Save(cam, PhotoTextures[i], i == 2 ? 480 : 720, i == 2 ? 640 : 500);
        }
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.Refresh();
        foreach (var p in PhotoTextures.Concat(new[] { MapTexture }))
        {
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }
        if (!string.IsNullOrEmpty(active)) EditorSceneManager.OpenScene(active, OpenSceneMode.Single);
    }

    private static void Save(Camera cam, string path, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
    }
}
