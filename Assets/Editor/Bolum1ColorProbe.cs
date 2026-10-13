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

// Minibüs renk ölçümü (yalnızca ölçer, sahneyi kaydetmez).
// Batchmode (-nographics OLMADAN): Unity.exe -batchmode -projectPath ... -executeMethod Bolum1ColorProbe.Run -renkTag once -logFile <dosya>
// 1) FBX'in gömülü materyallerini okur: shader, renk (sRGB hex), saydamlık.
// 2) Sahneyi açar, minibüsü iki ışıkta çeker: sahne ışığı (olduğu gibi) ve nötr ışık (geçici: beyaz güneş kameradan, ortam ışığı kapalı).
//    Nötr ışıkta ışığa dik yüzeylerde URP Lit'in yaygın (diffuse) payı ≈ albedo olur; böylece ekrandaki boya rengi materyal rengiyle karşılaştırılabilir.
// 3) Her pikselin hangi materyale düştüğünü geçici MeshCollider + raycast ile bulur, boya piksellerinin medyan rengini raporlar.
// Çıktı: Captures/Bolum1/renk_<tag>_sahne.png, renk_<tag>_notr.png, renk_<tag>_rapor.txt
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

            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            var van = GameObject.Find(Bolum1SceneBuilder.MinivanName);
            // Kamera: minibüsün güneşe/vadiye bakan yan yüzüne 3/4 açıdan.
            Vector3 look = van.transform.position + Vector3.up * 1.0f;
            Vector3 camPos = Bolum1Coordinates.ToUnity(new Vector3(-3.5f, -24f, -6.5f));
            Vector3 dir = camPos - look; dir.y = 0f;
            camPos = look + dir.normalized * 8f + Vector3.up * 1.2f;

            var probes = BuildProbeColliders(van);
            Shoot("sahne", tag, camPos, look, probes);

            // Nötr ışık: değişiklikler yalnızca bellekte, sahne kaydedilmez.
            var sun = GameObject.Find("Sun").GetComponent<Light>();
            sun.color = Color.white;
            sun.intensity = 1f;
            sun.transform.rotation = Quaternion.LookRotation(look - camPos);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            Shoot("notr", tag, camPos, look, probes);

            foreach (var p in probes.Keys) UnityEngine.Object.DestroyImmediate(p.gameObject);
            Line("BOLUM1 RENK OLCUMU OK");
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
