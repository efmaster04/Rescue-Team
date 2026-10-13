using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Bölüm 1 renk ölçümü (yalnızca ölçer, sahneyi kaydetmez): minibüs, mekân ve karakter (4 varyant).
// Batchmode (-nographics OLMADAN): Unity.exe -batchmode -projectPath ... -executeMethod Bolum1ColorProbe.Run -renkTag once -logFile <dosya>
// 1) FBX'lerin gömülü materyallerini okur: shader, renk (sRGB hex), saydamlık. Her materyalin Material.color'ı (sRGB)
//    beklenen hex'le ΔE76 ile karşılaştırılır (MeasureAll; Bolum1Verify (a2) de aynı tabloyu kullanır).
// 2) Sahneyi açar, minibüsü iki ışıkta çeker: sahne ışığı (olduğu gibi) ve nötr ışık (geçici: beyaz güneş kameradan, ortam ışığı kapalı).
//    Nötr ışıkta ışığa dik yüzeylerde URP Lit'in yaygın (diffuse) payı ≈ albedo olur; böylece ekrandaki boya rengi materyal rengiyle karşılaştırılabilir.
// 3) Her pikselin hangi materyale düştüğünü geçici MeshCollider + raycast ile bulur, boya piksellerinin medyan rengini raporlar.
// 4) Karakterin (TestCharacter_RescueCrew, turuncu varyant) yakın planını sahne ışığında çeker.
// Çıktı: Captures/Bolum1/renk_<tag>_sahne.png, renk_<tag>_notr.png, renk_<tag>_karakter.png, renk_<tag>_rapor.txt
public static class Bolum1ColorProbe
{
    private const string OutDir = "Captures/Bolum1";
    private const string PaintName = "M_Minivan_Paint";
    private const string GlassName = "M_Minivan_Glass";
    private static readonly Color32 ExpectedPaint = new Color32(0x9A, 0xC4, 0xD2, 255);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;

    public static void Run()
    {
        int code = 0;
        report = new StringBuilder();
        try
        {
            string tag = Arg("-renkTag", "olcum");
            Directory.CreateDirectory(OutDir);
            Line("BOLUM1 RENK OLCUMU (" + tag + ") " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            Line("renk uzayı: " + PlayerSettings.colorSpace + ", render pipeline: " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in"));
            ReportMaterials();
            var results = MeasureAll(out List<string> problems);
            ReportMeasurements(results, problems);
            ReportAvatars();

            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            var van = GameObject.Find(Bolum1SceneBuilder.MinivanName);
            // Kamera: minibüsün güneşe/vadiye bakan yan yüzüne 3/4 açıdan.
            Vector3 look = van.transform.position + Vector3.up * 1.0f;
            Vector3 camPos = Bolum1Coordinates.ToUnity(new Vector3(-3.5f, -24f, -6.5f));
            Vector3 dir = camPos - look; dir.y = 0f;
            camPos = look + dir.normalized * 8f + Vector3.up * 1.2f;

            var probes = BuildProbeColliders(van);
            Shoot("sahne", tag, camPos, look, probes);
            ShootCharacter(tag);

            // Nötr ışık: değişiklikler yalnızca bellekte, sahne kaydedilmez.
            var sun = GameObject.Find("Sun").GetComponent<Light>();
            sun.color = Color.white;
            sun.intensity = 1f;
            sun.transform.rotation = Quaternion.LookRotation(look - camPos);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            Shoot("notr", tag, camPos, look, probes);

            foreach (var p in probes.Keys) UnityEngine.Object.DestroyImmediate(p.gameObject);
            bool colorsOk = problems.Count == 0 && results.All(r => r.DeltaE < MaxDeltaE);
            if (!colorsOk) code = 1;
            Line(colorsOk ? "BOLUM1 RENK OLCUMU OK" : "BOLUM1 RENK OLCUMU FAILED");
        }
        catch (Exception e)
        {
            Line("[FAIL] " + e);
            code = 1;
        }
        File.WriteAllText(Path.Combine(OutDir, "renk_" + Arg("-renkTag", "olcum") + "_rapor.txt"), report.ToString());
        Debug.Log(report.ToString());
        EditorApplication.Exit(code);
    }

    // ---------- tüm materyallerin beklenen renkleri ----------
    public const float MaxDeltaE = 2f;

    // Mekân: build_bolum1_ucurum.py sample_palette() albedo_srgb (Rock/Dirt/Grass referanstan örneklenip karıştırılır;
    // kesin değerler Export/Bolum1_Ucurum/stats.json palette.albedo_srgb, 2870cdc). sRGB 0-1.
    public static readonly Dictionary<string, Color> EnvironmentExpected = new Dictionary<string, Color>
    {
        { "M_Asphalt",    new Color(0.2000f, 0.2000f, 0.2150f) }, // #333337
        { "M_LineYellow", new Color(0.9100f, 0.6500f, 0.2500f) }, // #E8A640
        { "M_LineWhite",  new Color(0.8600f, 0.8400f, 0.7300f) }, // #DBD6BA
        { "M_Rock",       new Color(0.6434f, 0.5088f, 0.3555f) }, // #A4825B
        { "M_Dirt",       new Color(0.5042f, 0.3287f, 0.2035f) }, // #815434
        { "M_Gravel",     new Color(0.4900f, 0.4350f, 0.3400f) }, // #7D6F57
        { "M_Grass",      new Color(0.3662f, 0.3859f, 0.1159f) }, // #5D621E
        { "M_Metal",      new Color(0.4800f, 0.4800f, 0.4600f) }, // #7A7A75
        { "M_Sign",       new Color(0.7800f, 0.0800f, 0.0700f) }, // #C71412
        { "M_Backdrop",   new Color(0.5500f, 0.5000f, 0.5900f) }, // #8C8096
    };

    // Karakter: build_rescue_crew.py build_materials(); varyantlar export_variants.py (M_Helmet → M_Helmet_<V>, M_Reflective_Orange → M_Reflective_<V>).
    public static readonly Dictionary<string, string> CharacterExpected = new Dictionary<string, string>
    {
        { "M_Skin", "#BD865A" }, { "M_Hair", "#302823" }, { "M_Jacket", "#252E46" },
        { "M_Reflective_Orange", "#F47727" }, { "M_Reflective_Silver", "#C7C9BF" }, { "M_Pants", "#2D3650" },
        { "M_Boots", "#584034" }, { "M_Sole", "#282421" }, { "M_Gloves", "#252627" }, { "M_Belt", "#654636" },
        { "M_Radio", "#30373A" }, { "M_RadioScreen", "#829C64" }, { "M_Helmet", "#D94310" }, { "M_HelmetStrap", "#777A7E" },
        { "M_Lamp", "#FFF0CA" }, { "M_EyeWhite", "#DDD9CE" }, { "M_Pupil", "#111211" }, { "M_Mouth", "#3E211B" },
        { "M_Helmet_Yellow", "#F1C52D" }, { "M_Reflective_Yellow", "#F1C52D" },
        { "M_Helmet_Blue", "#376BE0" }, { "M_Reflective_Blue", "#376BE0" },
        { "M_Helmet_Green", "#53A855" }, { "M_Reflective_Green", "#53A855" },
    };

    public static readonly string[] Variants = { "Orange", "Yellow", "Blue", "Green" };
    public const string CharacterFolder = "Assets/Models/Characters/RescueCrew/";

    public struct ColorResult
    {
        public string Group, Asset, Material;
        public Color Actual, Expected;
        public float DeltaE;
    }

    // Tüm Bölüm 1 materyallerini ölçer. problems: beklenen tabloda olmayan materyal, eksik materyal, yanlış varyant rengi.
    public static List<ColorResult> MeasureAll(out List<string> problems)
    {
        var results = new List<ColorResult>();
        problems = new List<string>();

        // (a) minibüs: minivan_meta.json (build_minivan.py reference_palette çıktısı)
        var vanExpected = Bolum1Coordinates.Obj(Bolum1Coordinates.LoadJson(Bolum1Coordinates.MinivanMetaPath), "materials")
            .ToDictionary(kv => kv.Key, kv => { ColorUtility.TryParseHtmlString((string)kv.Value, out Color c); return c; });
        Measure("minibüs", Bolum1SceneBuilder.MinivanPath, vanExpected, vanExpected.Keys, results, problems);

        // (b) mekân: 24 FBX
        var envSeen = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Bolum1ImportSettings.EnvironmentFolder.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()) envSeen.Add(m.name);
            Measure("mekân", path, EnvironmentExpected, null, results, problems);
        }
        foreach (string name in new[] { "M_Rock", "M_Dirt", "M_Asphalt", "M_Grass", "M_Metal", "M_Sign" })
            if (!envSeen.Contains(name)) problems.Add("mekânda " + name + " yok");

        // (c) karakter: 4 varyant; kask ve turuncu reflektör varyant rengini taşımalı
        var charExpected = CharacterExpected.ToDictionary(kv => kv.Key, kv => { ColorUtility.TryParseHtmlString(kv.Value, out Color c); return c; });
        foreach (string v in Variants)
        {
            string path = CharacterFolder + "SK_RescueCrew_" + v + ".fbx";
            string helmet = v == "Orange" ? "M_Helmet" : "M_Helmet_" + v;
            string reflective = "M_Reflective_" + v;
            var required = charExpected.Keys.Where(k => !k.StartsWith("M_Helmet") && !k.StartsWith("M_Reflective_")).ToList();
            required.AddRange(new[] { helmet, "M_HelmetStrap", reflective, "M_Reflective_Silver" });
            // Bu varyantta olmaması gereken (başka varyantın) kask/reflektör materyalleri tabloda yok sayılır → beklenmeyen olarak raporlanır.
            var allowed = charExpected.Where(kv => required.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            Measure("karakter " + v, path, allowed, required, results, problems);
        }
        return results;
    }

    private static void Measure(string group, string path, Dictionary<string, Color> expected, IEnumerable<string> required,
        List<ColorResult> results, List<string> problems)
    {
        var mats = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToList();
        if (mats.Count == 0) { problems.Add(path + ": materyal yok"); return; }
        foreach (var m in mats)
        {
            if (!expected.TryGetValue(m.name, out Color want)) { problems.Add(group + " " + Path.GetFileName(path) + ": beklenmeyen materyal " + m.name); continue; }
            Color c = m.color; // URP Lit'te _BaseColor ([MainColor])
            results.Add(new ColorResult { Group = group, Asset = Path.GetFileName(path), Material = m.name, Actual = c, Expected = want, DeltaE = DeltaE(c, want) });
        }
        if (required != null)
            foreach (string r in required)
                if (!mats.Any(m => m.name == r)) problems.Add(group + " " + Path.GetFileName(path) + ": eksik materyal " + r);
    }

    private static void ReportMeasurements(List<ColorResult> results, List<string> problems)
    {
        Line("RENK TABLOSU (Material.color sRGB, beklenen, ΔE76; eşik < " + MaxDeltaE.ToString("F0", Inv) + ")");
        // Mekânda aynı materyal birçok FBX'te tekrar eder: materyal başına en kötü ölçüm yazılır.
        foreach (var g in results.GroupBy(r => r.Group + "|" + r.Material).Select(g => g.OrderByDescending(r => r.DeltaE).First()))
            Line("  " + (g.DeltaE < MaxDeltaE ? "[OK]   " : "[FAIL] ") + g.Group + " " + g.Material + ": " + Hex(g.Actual) + " beklenen " + Hex(g.Expected)
                + " ΔE76 " + g.DeltaE.ToString("F2", Inv) + " (" + results.Count(r => r.Group == g.Group && r.Material == g.Material) + " FBX, en kötü " + g.Asset + ")");
        foreach (string p in problems) Line("  [FAIL] " + p);
        var worst = results.OrderByDescending(r => r.DeltaE).FirstOrDefault();
        Line("  toplam " + results.Count + " ölçüm, en kötü ΔE76 " + worst.DeltaE.ToString("F2", Inv) + " (" + worst.Group + " " + worst.Material + ")");
    }

    // Karakter yakın planı (sahne ışığı). Kamera karakterin önünde, göğüs hizasında.
    private static void ShootCharacter(string tag)
    {
        var ch = GameObject.Find(Bolum1SceneBuilder.CharacterName);
        var anim = ch.GetComponent<Animator>();
        Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        Vector3 look = head != null ? head.position + Vector3.down * 0.3f : ch.transform.position + Vector3.up * 1.0f;
        Vector3 fwd = ch.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        Vector3 pos = look + fwd.normalized * 2.4f + Vector3.up * 0.2f;

        const int W = 1920, H = 1080;
        var go = new GameObject("Bolum1ColorProbe_CharCam");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Camera.main != null ? Camera.main.backgroundColor : Color.gray;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000f;
        cam.fieldOfView = 48f;
        go.transform.position = pos;
        go.transform.LookAt(look);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        string file = Path.Combine(OutDir, "renk_" + tag + "_karakter.png");
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Line("GÖRÜNTÜ karakter: " + file + " (kamera " + F3v(pos) + " → " + F3v(look) + ")");
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(go);
    }

    // Reimport sonrası karakter avatarı: importer'dan geri okunur (importSettingsMissing'e güvenilmez).
    private static void ReportAvatars()
    {
        Line("KARAKTER AVATARI");
        foreach (string v in Variants)
        {
            string path = CharacterFolder + "SK_RescueCrew_" + v + ".fbx";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            int mapped = imp != null ? imp.humanDescription.human.Length : -1;
            Line("  " + v + ": userData " + (imp != null ? imp.userData : "?") + ", tip " + (imp != null ? imp.animationType.ToString() : "?")
                + ", blendShapes " + (imp != null && imp.importBlendShapes) + ", avatar " + (avatar == null ? "YOK" : (avatar.isValid ? "geçerli" : "GEÇERSİZ") + (avatar.isHuman ? " humanoid" : " generic"))
                + ", kemik eşlemesi " + mapped + "/" + HumanTrait.RequiredBoneCount + " zorunlu (toplam insan kemiği " + HumanTrait.BoneCount + ")");
        }
    }

    private static string F3v(Vector3 v) { return v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv); }

    private static void ReportMaterials()
    {
        foreach (var m in AssetDatabase.LoadAllAssetsAtPath(Bolum1SceneBuilder.MinivanPath).OfType<Material>().OrderBy(m => m.name))
        {
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            string surface = m.HasProperty("_Surface") ? (m.GetFloat("_Surface") > 0.5f ? "Transparent" : "Opaque") : "?";
            string line = "  " + m.name + ": shader " + m.shader.name + ", renk #" + ColorUtility.ToHtmlStringRGB(c) + " a=" + c.a.ToString("F2", Inv)
                + " (lineer " + F3(c.linear) + "), yüzey " + surface + ", kuyruk " + m.renderQueue;
            if (m.name == PaintName) line += ", beklenen #9AC4D2 ile ΔE76 " + DeltaE(c, ExpectedPaint).ToString("F1", Inv);
            Line(line);
        }
    }

    // Minibüsün her MeshRenderer'ına geçici collider (ayrı katmanda değil; raycast sonucu yalnızca bu collider'lara bakar).
    private static Dictionary<MeshCollider, MeshRenderer> BuildProbeColliders(GameObject van)
    {
        var map = new Dictionary<MeshCollider, MeshRenderer>();
        foreach (var mr in van.GetComponentsInChildren<MeshRenderer>())
        {
            var go = new GameObject("Bolum1ColorProbe_Collider");
            go.transform.SetPositionAndRotation(mr.transform.position, mr.transform.rotation);
            go.transform.localScale = mr.transform.lossyScale;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
            map[mc] = mr;
        }
        Physics.SyncTransforms();
        return map;
    }

    private static void Shoot(string light, string tag, Vector3 pos, Vector3 look, Dictionary<MeshCollider, MeshRenderer> probes)
    {
        const int W = 1920, H = 1080;
        var go = new GameObject("Bolum1ColorProbe_Cam");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Camera.main != null ? Camera.main.backgroundColor : Color.gray;
        cam.farClipPlane = 2000f;
        cam.fieldOfView = 40f;
        go.transform.position = pos;
        go.transform.LookAt(look);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        string file = Path.Combine(OutDir, "renk_" + tag + "_" + light + ".png");
        File.WriteAllBytes(file, tex.EncodeToPNG());

        // Piksel → materyal eşlemesi (4 pikselde bir örnek).
        var sun = RenderSettings.sun != null ? RenderSettings.sun : GameObject.Find("Sun").GetComponent<Light>();
        Vector3 toLight = -sun.transform.forward;
        var paintAll = new List<Color>();
        var paintFacing = new List<Color>();
        var glass = new List<Color>();
        for (int y = 2; y < H; y += 4)
            for (int x = 2; x < W; x += 4)
            {
                var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0f));
                RaycastHit best = default; bool found = false;
                foreach (var h in Physics.RaycastAll(ray, 200f))
                    if (h.collider is MeshCollider mc && probes.ContainsKey(mc) && (!found || h.distance < best.distance)) { best = h; found = true; }
                if (!found) continue;
                string mat = MaterialAt(probes[(MeshCollider)best.collider], best.triangleIndex);
                Color px = tex.GetPixel(x, y);
                if (mat == PaintName)
                {
                    paintAll.Add(px);
                    if (Vector3.Dot(best.normal, toLight) > 0.9f) paintFacing.Add(px);
                }
                else if (mat == GlassName) glass.Add(px);
            }
        Line("GÖRÜNTÜ " + light + ": " + file);
        Line("  boya pikselleri (tümü, " + paintAll.Count + "): medyan " + Hex(Median(paintAll)) + ", ΔE76 " + DeltaE(Median(paintAll), ExpectedPaint).ToString("F1", Inv));
        if (paintFacing.Count > 0)
            Line("  boya pikselleri (ışığa dik, N·L>0.9, " + paintFacing.Count + "): medyan " + Hex(Median(paintFacing)) + ", ΔE76 " + DeltaE(Median(paintFacing), ExpectedPaint).ToString("F1", Inv));
        if (glass.Count > 0) Line("  cam pikselleri (" + glass.Count + "): medyan " + Hex(Median(glass)));

        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(go);
    }

    private static string MaterialAt(MeshRenderer mr, int triangle)
    {
        var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
        int index = triangle * 3;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var d = mesh.GetSubMesh(s);
            if (index >= d.indexStart && index < d.indexStart + d.indexCount)
                return s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null ? mr.sharedMaterials[s].name : "?";
        }
        return "?";
    }

    private static Color Median(List<Color> list)
    {
        if (list.Count == 0) return Color.black;
        float M(Func<Color, float> f) { var v = list.Select(f).OrderBy(a => a).ToList(); return v[v.Count / 2]; }
        return new Color(M(c => c.r), M(c => c.g), M(c => c.b));
    }

    // CIE76 ΔE, sRGB (D65) → Lab.
    public static float DeltaE(Color a, Color b)
    {
        Vector3 la = Lab(a), lb = Lab(b);
        return (la - lb).magnitude;
    }

    private static Vector3 Lab(Color c)
    {
        Color l = c.linear;
        float X = (0.4124f * l.r + 0.3576f * l.g + 0.1805f * l.b) / 0.95047f;
        float Y = 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        float Z = (0.0193f * l.r + 0.1192f * l.g + 0.9505f * l.b) / 1.08883f;
        float F(float t) { return t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f; }
        return new Vector3(116f * F(Y) - 16f, 500f * (F(X) - F(Y)), 200f * (F(Y) - F(Z)));
    }

    private static string Arg(string name, string fallback)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
    }

    private static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }
    private static string F3(Color c) { return c.r.ToString("F3", Inv) + ", " + c.g.ToString("F3", Inv) + ", " + c.b.ToString("F3", Inv); }
    private static void Line(string s) { report.AppendLine(s); }
}
