using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Ölçüm aracı (yalnızca Play modunda): ayak IK'sının etkisini sayıyla gösterir.
// Yöntem: Her karede SkinnedMeshRenderer'ı bake edip, baskın kemiği Foot/ToeBase olan köşelerin
// zemin düzlemine olan en düşük işaretli mesafesini (negatif = zemine gömülü) sol ve sağ ayak için kaydeder.
// Sonuç CSV olarak Captures/footik_<tag>.csv dosyasına yazılır.
public static class FootGroundingMeasure
{
    private class Target
    {
        public GameObject go;
        public Animator animator;
        public SkinnedMeshRenderer[] smrs;
        public List<int>[] leftIdx, rightIdx; // her SMR için sol/sağ ayak köşe indeksleri
        public FootGroundingIK ik;
    }

    private static List<Target> targets = new List<Target>();
    private static StringBuilder csv;
    private static float endTime;
    private static string fileTag;
    private static Vector3 planePoint = Vector3.zero, planeNormal = Vector3.up;
    private static int lastFrame = -1;
    private static bool followRamp;
    private static float rampAngle;
    private static Vector3 rampOrigin;
    private static readonly List<Vector3> tmp = new List<Vector3>();
    private static Mesh baked;

    // names: ölçülecek karakter objeleri. ikEnabled: FootGroundingIK açık mı.
    // ramp: true ise karakterin kökü rampa yüzeyini takip eder (karakter denetleyicisi taklidi).
    public static string Start(string[] names, bool ikEnabled, float seconds, string tag, float rampAngleDeg, Vector3 rampOriginPos)
    {
        if (!Application.isPlaying) return "Play modu degil";
        targets.Clear(); csv = new StringBuilder("name,time,phase,left,right,leftAnkle,rightAnkle\n");
        baked = new Mesh();
        fileTag = tag; endTime = Time.time + seconds;
        rampAngle = rampAngleDeg; followRamp = rampAngleDeg != 0f; rampOrigin = rampOriginPos;
        if (followRamp)
        {
            // Rampa düzlemi: x ekseni etrafında yukarı eğim, +z yönünde yükselir.
            planeNormal = Quaternion.Euler(-rampAngleDeg, 0, 0) * Vector3.up;
            planePoint = rampOriginPos;
        }
        else { planeNormal = Vector3.up; planePoint = Vector3.zero; }

        foreach (string n in names)
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            var t = new Target { go = go, animator = go.GetComponent<Animator>(), smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>() };
            t.ik = go.GetComponent<FootGroundingIK>();
            if (t.ik != null) t.ik.enabled = ikEnabled;
            Transform lf = t.animator.GetBoneTransform(HumanBodyBones.LeftFoot), rf = t.animator.GetBoneTransform(HumanBodyBones.RightFoot);
            t.leftIdx = new List<int>[t.smrs.Length]; t.rightIdx = new List<int>[t.smrs.Length];
            for (int s = 0; s < t.smrs.Length; s++)
            {
                var sm = t.smrs[s]; var bones = sm.bones; var bw = sm.sharedMesh.boneWeights;
                t.leftIdx[s] = new List<int>(); t.rightIdx[s] = new List<int>();
                // Taban köşeleri: adı "Sole" içeren materyalin alt mesh'i (varsa); yoksa ayak kemiğine bağlı tüm köşeler.
                var isSole = new bool[bw.Length]; bool anySole = false;
                for (int sub = 0; sub < sm.sharedMesh.subMeshCount; sub++)
                {
                    if (sub >= sm.sharedMaterials.Length || sm.sharedMaterials[sub] == null || !sm.sharedMaterials[sub].name.Contains("Sole")) continue;
                    foreach (int vi in sm.sharedMesh.GetTriangles(sub)) { isSole[vi] = true; anySole = true; }
                }
                for (int i = 0; i < bw.Length; i++)
                {
                    if (anySole && !isSole[i]) continue;
                    Transform b = bones[bw[i].boneIndex0];
                    if (b == lf || b.IsChildOf(lf)) t.leftIdx[s].Add(i);
                    else if (b == rf || b.IsChildOf(rf)) t.rightIdx[s].Add(i);
                }
            }
            targets.Add(t);
        }
        lastFrame = -1;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "basladi: " + targets.Count + " karakter, ik=" + ikEnabled;
    }

    // Editör odakta değilken Play döngüsü neredeyse durduğu için ölçümü senkron yürütürüz:
    // Animator'ı kapatıp her kare Animator.Update(dt) ile elle ilerletiriz (OnAnimatorIK de bu çağrıda çalışır).
    public static string RunStepped(float seconds, float dt)
    {
        if (!Application.isPlaying || targets.Count == 0) return "Start cagrilmadi";
        EditorApplication.update -= Tick;
        foreach (var t in targets) t.animator.enabled = false;
        float simTime = 0f;
        int steps = Mathf.RoundToInt(seconds / dt);
        for (int i = 0; i < steps; i++)
        {
            simTime += dt;
            foreach (var t in targets) { t.animator.Update(dt); if (t.ik != null) t.ik.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver); } // LateUpdate Animator.Update ile çalışmaz, elle tetikle
            foreach (var t in targets) Sample(t, simTime);
        }
        foreach (var t in targets) t.animator.enabled = true;
        string tag = fileTag;
        Finish();
        return "bitti: " + steps + " kare, dosya footik_" + tag + ".csv";
    }

    // Bağ pozundaki (edit modu, animasyon yok) karakterin taban boyutunu ölçer: Foot kemiğine göre ileri/geri/yan uç ve bilek yüksekliği.
    // Yeni model (ör. rig v6) gelince FootGroundingIK'daki Taban boyutu alanlarını bu değerlerle güncelle.
    // Taban köşeleri: adı 'Sole' içeren materyalin alt mesh'i.
    public static string SoleExtents(string objectName)
    {
        var go = GameObject.Find(objectName);
        var a = go.GetComponent<Animator>();
        Vector3 ankle = a.GetBoneTransform(HumanBodyBones.LeftFoot).position;
        Vector3 rootPos = go.transform.position;
        float fMin = 1e9f, fMax = -1e9f, lMin = 1e9f, lMax = -1e9f;
        var m = new Mesh(); var v = new List<Vector3>();
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            s.BakeMesh(m); m.GetVertices(v);
            for (int sub = 0; sub < s.sharedMesh.subMeshCount; sub++)
            {
                if (sub >= s.sharedMaterials.Length || s.sharedMaterials[sub] == null || !s.sharedMaterials[sub].name.Contains("Sole")) continue;
                foreach (int i in s.sharedMesh.GetTriangles(sub))
                {
                    Vector3 p = s.transform.TransformPoint(v[i]);
                    if (p.x > rootPos.x) continue; // tek ayak (sol)
                    fMin = Mathf.Min(fMin, p.z - ankle.z); fMax = Mathf.Max(fMax, p.z - ankle.z);
                    lMin = Mathf.Min(lMin, p.x - ankle.x); lMax = Mathf.Max(lMax, p.x - ankle.x);
                }
            }
        }
        Object.DestroyImmediate(m);
        return "ankleHeight=" + (ankle.y - rootPos.y).ToString("F3") + " soleHeelBack=" + (-fMin).ToString("F3") + " soleToeForward=" + fMax.ToString("F3") + " soleHalfWidth=" + ((lMax - lMin) * 0.5f).ToString("F3") + " soleCenterOffset(disa)=" + (-(lMax + lMin) * 0.5f).ToString("F3");
    }

    private static void Tick()
    {
        if (!Application.isPlaying) { Finish(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        foreach (var t in targets) Sample(t, Time.time);
        if (Time.time >= endTime) Finish();
    }

    private static void Sample(Target t, float time)
    {
        {
            if (followRamp)
            {
                // Kökü rampa yüzeyine oturt (gerçek oyunda CharacterController yapardı).
                Vector3 p = t.go.transform.position;
                float dz = p.z - rampOrigin.z;
                if (dz > 3f) { p.z = rampOrigin.z; dz = 0f; } // rampa başına dön
                p.y = rampOrigin.y + Mathf.Max(dz, 0f) * Mathf.Tan(rampAngle * Mathf.Deg2Rad);
                t.go.transform.position = p;
            }
            else
            {
                Vector3 p = t.go.transform.position;
                if (p.z > 3.5f) { p.z = -3f; t.go.transform.position = p; }
            }
            float l = float.MaxValue, r = float.MaxValue;
            for (int s = 0; s < t.smrs.Length; s++)
            {
                var sm = t.smrs[s];
                sm.BakeMesh(baked); baked.GetVertices(tmp);
                foreach (int i in t.leftIdx[s]) l = Mathf.Min(l, SignedDist(sm.transform.TransformPoint(tmp[i])));
                foreach (int i in t.rightIdx[s]) r = Mathf.Min(r, SignedDist(sm.transform.TransformPoint(tmp[i])));
            }
            float phase = Mathf.Repeat(t.animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
            csv.Append(t.go.name).Append(',').Append(time.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(phase.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
               .Append(l.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(r.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
               .Append(SignedDist(t.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
               .Append(SignedDist(t.animator.GetBoneTransform(HumanBodyBones.RightFoot).position).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    // Zemin yüzeyi: rampa modunda düz zemin ile rampanın yüksek olanı; yani nokta ikisinin de üstündeyse havadadır.
    private static float SignedDist(Vector3 p)
    {
        float d = Vector3.Dot(p - planePoint, planeNormal);
        return followRamp ? Mathf.Min(d, p.y) : d;
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        if (csv == null) return;
        Directory.CreateDirectory("Captures");
        File.WriteAllText("Captures/footik_" + fileTag + ".csv", csv.ToString());
        csv = null;
        if (baked != null) Object.DestroyImmediate(baked);
        Debug.Log("FootGroundingMeasure bitti: Captures/footik_" + fileTag + ".csv");
    }

    public static bool IsRunning { get { return csv != null; } }
}
