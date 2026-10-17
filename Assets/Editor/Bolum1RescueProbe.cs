using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Geliştirme ölçümü (D2): minibüs koltukları, bagaj kapısı, kaya çıkıntısı ve uçurum profili, ambulans iç mesh'i.
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod Bolum1RescueProbe.Run -logFile <dosya> (kendi çıkar).
public static class Bolum1RescueProbe
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Run()
    {
        var sb = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var van = GameObject.Find(Bolum1SceneBuilder.MinivanName).transform;
            sb.AppendLine("van pos " + V(van.position) + " rot " + V(van.eulerAngles) + " fwd(-Y) " + V(van.TransformDirection(0, -1, 0)) + " up(Z) " + V(van.TransformDirection(0, 0, 1)));
            foreach (Transform c in van) sb.AppendLine("  child " + c.name + " local " + V(c.localPosition) + " world " + V(c.position));
            foreach (var mf in van.GetComponentsInChildren<MeshFilter>()) sb.AppendLine("  mesh " + mf.name + " bounds " + V(mf.sharedMesh.bounds.center) + " / " + V(mf.sharedMesh.bounds.size));
            foreach (var n in new[] { "Seat_05", "Seat_06" })
            {
                var t = van.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                sb.AppendLine("  " + n + " transform " + (t != null ? V(t.position) : "yok"));
            }
            var ground = GameObject.Find("SM_B1_Ground").transform;
            // Kaya çıkıntısı ve üstündeki patika: Blender x -4..10 boyunca y profili
            for (float bx = -4f; bx <= 10f; bx += 1f)
            {
                var row = new StringBuilder("x " + bx.ToString("F0", Inv) + ":");
                for (float by = -24f; by <= -13f; by += 0.5f)
                {
                    Vector3 u = Bolum1Coordinates.ToUnity(new Vector3(bx, by, 0f));
                    var hits = Physics.RaycastAll(new Vector3(u.x, 20f, u.z), Vector3.down, 60f).OrderBy(h => h.distance).ToArray();
                    var h0 = hits.FirstOrDefault();
                    string tag = h0.collider == null ? "--" : (h0.transform.IsChildOf(ground) ? "" : "*") + (h0.point.y).ToString("F1", Inv);
                    row.Append(" " + tag);
                }
                sb.AppendLine(row.ToString());
            }
            var amb = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulanceModelPath);
            foreach (var mf in amb.GetComponentsInChildren<MeshFilter>(true))
                sb.AppendLine("amb mesh " + mf.name + " tri " + (mf.sharedMesh.triangles.Length / 3) + " subm " + mf.sharedMesh.subMeshCount + " mats " + string.Join(",", mf.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m != null ? m.name : "-")));
            var anchor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Props/RopeAnchor/SM_RopeAnchor.fbx");
            if (anchor != null) foreach (var t in anchor.GetComponentsInChildren<Transform>(true)) sb.AppendLine("anchor " + t.name + " local " + V(t.localPosition) + " rot " + V(t.localEulerAngles));
            File.WriteAllText("Captures/Bolum1/rescue_probe.txt", sb.ToString());
            Debug.Log(sb.ToString());
            Debug.Log("RESCUE PROBE OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("RESCUE PROBE FAILED " + e);
            EditorApplication.Exit(1);
        }
    }

    private static string V(Vector3 v) { return "(" + v.x.ToString("F3", Inv) + ", " + v.y.ToString("F3", Inv) + ", " + v.z.ToString("F3", Inv) + ")"; }
}
