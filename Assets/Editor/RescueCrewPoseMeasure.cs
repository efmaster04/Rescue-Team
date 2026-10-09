using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Ölçüm aracı: klibi kare kare örnekleyip önkol/el köşelerinin kafa+gövde mesh'inin içine girme derinliğini ölçer.
// Yöntem: Karakteri örnekle, SkinnedMeshRenderer'ı bake et; baskın kemiği LowerArm/Hand alt ağacında olan köşeler "kol",
// kol dışı (upper arm hariç) köşeler "gövde" sayılır. Kol köşesi gövde yüzeyinin içindeyse (ışın paritesi) derinlik = yüzeye en kısa mesafe.
public static class RescueCrewPoseMeasure
{
    private const string CharPath = "Assets/Models/Characters/RescueCrew/SK_RescueCrew_{0}.fbx";
    private const string AnimPath = "Assets/Animations/RescueCrew/Mixamo_{0}.fbx";

    public static string MeasureAll(string color)
    {
        var sb = new StringBuilder();
        foreach (string clipName in new[] { "Idle", "Walking", "Running", "Waving" })
            sb.AppendLine(Measure(color, clipName, false, null));
        return sb.ToString();
    }

    public static string Measure(string color, string clipName, bool shoot, string shotPrefix)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(CharPath, color));
        AnimationClip clip = null;
        // "Running_ArmFix" = türetilmiş .anim klibi
        string clipAssetPath = clipName == "Running_ArmFix" ? "Assets/Animations/RescueCrew/Derived/Mixamo_Running_ArmFix.anim" : string.Format(AnimPath, clipName);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(clipAssetPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clip = c;
        if (prefab == null || clip == null) return clipName + ": yukleme hatasi";

        var inst = (GameObject)Object.Instantiate(prefab);
        inst.transform.position = new Vector3(1000, 0, 1000);
        var anim = inst.GetComponent<Animator>();
        var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>();

        // Kol alt ağacı belirle
        var armRoots = new List<Transform> { anim.GetBoneTransform(HumanBodyBones.LeftLowerArm), anim.GetBoneTransform(HumanBodyBones.RightLowerArm) };
        var upperRoots = new List<Transform> { anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), anim.GetBoneTransform(HumanBodyBones.RightUpperArm) };
        // Tüm SkinnedMeshRenderer'lar (Body, Face, Helmet) tek listede birleştirilir
        var kindList = new List<int>(); // 1 = önkol/el, 2 = gövde/kafa, 0 = yok say
        var triList = new List<int>(); var regionList = new List<int>(); // 0 = kafa/kask, 1 = göğüs/üst gövde, 2 = kalça/bacak
        int total = 0;
        foreach (var sm in smrs)
        {
            var m = sm.sharedMesh; var bones = sm.bones; var bw = m.boneWeights;
            for (int i = 0; i < m.vertexCount; i++)
            {
                Transform b = bones[bw[i].boneIndex0];
                string bname = b.name;
                regionList.Add(bname.Contains("Head") || bname.Contains("Neck") ? 0 : (bname.Contains("Spine1") || bname.Contains("Spine2") || bname.Contains("Shoulder") ? 1 : 2));
                if (IsUnder(b, armRoots)) kindList.Add(1);
                else if (IsUnder(b, upperRoots)) kindList.Add(0);
                else kindList.Add(2);
            }
            foreach (int ti in m.triangles) triList.Add(ti + total);
            total += m.vertexCount;
        }
        int n = total;
        var kind = kindList.ToArray(); regionOf = regionList.ToArray(); LastRegionMax = new float[3];
        int[] tris = triList.ToArray();
        var bodyTris = new List<int>();
        for (int t = 0; t < tris.Length; t += 3)
            if (kind[tris[t]] == 2 && kind[tris[t + 1]] == 2 && kind[tris[t + 2]] == 2) bodyTris.Add(t);

        var bodyVerts = new List<int>();
        for (int i = 0; i < n; i++) if (kind[i] == 2) bodyVerts.Add(i);

        int frames = Mathf.Clamp(Mathf.RoundToInt(clip.length * clip.frameRate), 2, 30); // en fazla 30 örnek
        float worst = 0; int worstFrame = 0; int worstCount = 0; double sumMax = 0;
        var baked = new Mesh();
        var verts = new List<Vector3>(n);
        var tmp = new List<Vector3>(); var perFrame = new StringBuilder();
        for (int f = 0; f < frames; f++)
        {
            float t = clip.length * f / frames;
            clip.SampleAnimation(inst, t);
            verts.Clear();
            foreach (var sm in smrs)
            {
                sm.BakeMesh(baked);
                baked.GetVertices(tmp);
                foreach (var v0 in tmp) verts.Add(sm.transform.TransformPoint(v0));
            }
            float max = 0; int cnt = 0;
            for (int i = 0; i < n; i++)
            {
                if (kind[i] != 1) continue;
                Vector3 p = verts[i];
                // Hızlı ön eleme: en yakın gövde köşesi 6 cm'den uzaksa içeride olamaz
                bool near = false;
                foreach (int bv in bodyVerts)
                    if ((verts[bv] - p).sqrMagnitude < 0.0036f) { near = true; break; }
                if (!near) continue;
                if (!Inside(p, verts, tris, bodyTris)) continue;
                cnt++;
                float d = DistToSurface(p, verts, tris, bodyTris);
                if (d > max) max = d;
                if (d > LastRegionMax[lastRegion]) LastRegionMax[lastRegion] = d;
            }
            sumMax += max; perFrame.Append(max.ToString("F3")).Append(' ');
            if (max > worst) { worst = max; worstFrame = f; worstCount = cnt; }
        }


        string shotInfo = "";
        if (shoot)
        {
            Directory.CreateDirectory("Captures");
            clip.SampleAnimation(inst, clip.length * worstFrame / frames);
            shotInfo = " shot=" + Shoot(inst, shotPrefix);
        }
        Object.DestroyImmediate(baked);
        Object.DestroyImmediate(inst);
        string res = string.Format("{0}/{1}: maxDepth={2:F4}m (kare {3}/{4}, icerideki koseler={5}) ortMax={6:F4}m{7} kareler=[{8}] bolge(kafa/gogus/kalca)=[{9:F4} {10:F4} {11:F4}]",
            color, clipName, worst, worstFrame, frames, worstCount, sumMax / frames, shotInfo, perFrame, LastRegionMax[0], LastRegionMax[1], LastRegionMax[2]);
        Directory.CreateDirectory("Captures");
        File.AppendAllText("Captures/measure_log.txt", System.DateTime.Now.ToString("HH:mm:ss") + " " + res + "\n");
        return res;
    }

    private static bool IsUnder(Transform b, List<Transform> roots)
    {
        foreach (var r in roots)
            if (r != null && (b == r || b.IsChildOf(r))) return true;
        return false;
    }

    // Üç eksende ışın paritesi, çoğunluk oyu
    private static bool Inside(Vector3 p, List<Vector3> v, int[] tris, List<int> bodyTris)
    {
        // Y ve X ışınları; üçgenin sınır kutusuyla hızlı eleme, ikisi de tekse içeride
        int hy = 0, hx = 0;
        foreach (int t in bodyTris)
        {
            Vector3 a = v[tris[t]], b = v[tris[t + 1]], c = v[tris[t + 2]];
            if (!(p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) ||
                  p.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || p.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z)) ||
                  Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < p.y))
                if (RayTri(p, Vector3.up, a, b, c)) hy++;
            if (!(p.y < Mathf.Min(a.y, Mathf.Min(b.y, c.y)) || p.y > Mathf.Max(a.y, Mathf.Max(b.y, c.y)) ||
                  p.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || p.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z)) ||
                  Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < p.x))
                if (RayTri(p, Vector3.right, a, b, c)) hx++;
        }
        return (hy & 1) == 1 && (hx & 1) == 1;
    }

    private static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, h = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, h);
        if (Mathf.Abs(det) < 1e-9f) return false;
        float inv = 1f / det; Vector3 s = o - a;
        float u = inv * Vector3.Dot(s, h); if (u < 0 || u > 1) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float vv = inv * Vector3.Dot(d, q); if (vv < 0 || u + vv > 1) return false;
        return inv * Vector3.Dot(e2, q) > 1e-6f;
    }

    public static float[] LastRegionMax = new float[3]; // kafa, göğüs, kalça: tüm karelerdeki en derin gömülme
    private static int[] regionOf; private static int lastRegion;
    private static float DistToSurface(Vector3 p, List<Vector3> v, int[] tris, List<int> bodyTris)
    {
        float best = float.MaxValue;
        foreach (int t in bodyTris)
        {
            Vector3 c = Closest(p, v[tris[t]], v[tris[t + 1]], v[tris[t + 2]]);
            float d = (p - c).sqrMagnitude;
            if (d < best) { best = d; lastRegion = regionOf[tris[t]]; }
        }
        return Mathf.Sqrt(best);
    }

    // Ericson, Real-Time Collision Detection: ClosestPtPointTriangle
    private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    // Karakteri önden ve yandan çekip PNG yazar
    private static string Shoot(GameObject inst, string prefix)
    {
        var camGo = new GameObject("TmpMeasureCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
        var lightGo = new GameObject("TmpMeasureLight");
        var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(40, 30, 0);
        // Kök hareketi örneklemede konumu sıfırlıyor; kamerayı Hips kemiğine göre kur, sahnedeki diğer renderer'ları geçici kapat
        var hips = inst.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
        Vector3 c = new Vector3(hips.position.x, 0.75f, hips.position.z);
        var rt = new RenderTexture(900, 900, 24);
        cam.targetTexture = rt; cam.fieldOfView = 30;
        // Sahnedeki diğer objeler görünmesin: karakteri boş bir layer'a alıp yalnız onu çek
        foreach (var tr in inst.GetComponentsInChildren<Transform>()) tr.gameObject.layer = 31;
        cam.cullingMask = 1 << 31; l.cullingMask = 1 << 31;
        var sb = new StringBuilder();
        string[] names = { "front", "side" };
        Vector3[] offs = { new Vector3(0, 0, 3.2f), new Vector3(3.2f, 0, 0) };
        for (int i = 0; i < 2; i++)
        {
            camGo.transform.position = c + offs[i];
            camGo.transform.LookAt(c);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply();
            string path = "Captures/" + prefix + "_" + names[i] + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            sb.Append(path + ";");
        }
        RenderTexture.active = null; cam.targetTexture = null;
        rt.Release(); Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
        return sb.ToString();
    }
}
