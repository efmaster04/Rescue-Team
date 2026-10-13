using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class Bolum1Verify
{
    private static void CheckTowTruck(Dictionary<string, object> markers)
    {
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1SceneBuilder.TowTruckPath);
        var placement = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "TowTruckPlacement");
        var position = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(placement, "position"));
        var rotation = Bolum1Coordinates.ToUnity(Bolum1Coordinates.QuatWxyz(placement["rotation_quaternion_wxyz"])) * prefab.transform.rotation;
        Check("çekici yerleşimi (TowTruckPlacement)", Vector3.Distance(position, truck.transform.position) < 0.001f
            && Quaternion.Angle(rotation, truck.transform.rotation) < 0.01f && Vector3.Distance(truck.transform.localScale, prefab.transform.localScale) < 0.001f,
            "Unity konum " + V(truck.transform.position) + "; Euler " + V(truck.transform.eulerAngles));

        var meta = Bolum1Coordinates.LoadJson(Bolum1Coordinates.TowTruckMetaPath);
        bool hierarchyOk = true;
        foreach (var entry in Bolum1Coordinates.Obj(meta, "parts"))
        {
            var part = Bolum1TowTruck.Part(truck, entry.Key);
            string parent = (string)((Dictionary<string, object>)entry.Value)["parent"];
            hierarchyOk &= part.parent != null && part.parent.name == parent;
            if (!Bolum1TowTruck.WheelNames.Contains(entry.Key) && !Bolum1TowTruck.ConeNames.Contains(entry.Key)
                && entry.Key != "SM_TowTruck_Crane_Hook")
                hierarchyOk &= part.GetComponents<Collider>().Length == 0 && part.GetComponents<Rigidbody>().Length == 0;
        }
        hierarchyOk &= truck.GetComponentsInChildren<MonoBehaviour>(true).Length == 0;
        bool physicsOk = truck.GetComponent<Rigidbody>() == null && truck.GetComponents<BoxCollider>().Length >= 3
            && truck.GetComponentsInChildren<Rigidbody>().Length == 4
            && truck.GetComponentsInChildren<MeshCollider>().All(c => Bolum1TowTruck.ConeNames.Contains(c.name) && c.convex);
        foreach (string name in Bolum1TowTruck.ConeNames)
        {
            var cone = Bolum1TowTruck.Part(truck, name);
            var rb = cone.GetComponent<Rigidbody>();
            var mc = cone.GetComponent<MeshCollider>();
            physicsOk &= rb != null && Mathf.Approximately(rb.mass, 3f) && rb.useGravity && !rb.isKinematic
                && rb.constraints == RigidbodyConstraints.None && mc != null && mc.convex && mc.enabled && !mc.isTrigger;
        }
        var hook = Bolum1TowTruck.Part(truck, "SM_TowTruck_Crane_Hook");
        var hookCollider = hook.GetComponent<SphereCollider>();
        physicsOk &= hook.GetComponent<Rigidbody>() == null && hookCollider != null && hookCollider.radius < 0.25f;
        bool noPlaceholder = !UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name.StartsWith("PH_"));
        Check("çekici hiyerarşi / collider / Rigidbody", hierarchyOk && physicsOk && noPlaceholder,
            "statik kök; kutu " + truck.GetComponents<BoxCollider>().Length + "; 6 teker + kanca küre; 4 serbest koni × 3 kg; PH yok=" + noPlaceholder);

        bool wheelsOk = true;
        foreach (string name in Bolum1TowTruck.WheelNames)
        {
            var wheel = Bolum1TowTruck.Part(truck, name);
            var col = wheel.GetComponent<SphereCollider>();
            bool groundFound = RayDownToModule(wheel.position, "SM_B1_Ground", out RaycastHit hit);
            float gap = groundFound ? WorldMinY(wheel.GetComponent<MeshFilter>()) - hit.point.y : 999f;
            string material = groundFound ? HitMaterial(hit) : "YOK";
            wheelsOk &= groundFound && material == "M_Gravel" && Mathf.Abs(gap) <= 0.05f && col != null && col.enabled;
            Line("  çekici " + name + " taban-banket " + Cm(gap) + "; yüzey " + material);
        }
        Check("çekici 6 teker tabanı bankette ±5 cm", wheelsOk, "mesh tabanı → Ground/M_Gravel raycast");

        // Dikey izdüşümde tüm görsel parçaların dışbükey zarfı: aynalar ve vinç de taşmamalı.
        var footprint = ConvexHull(truck.GetComponentsInChildren<MeshFilter>()
            .SelectMany(m => m.sharedMesh.vertices.Select(v => XZ(m.transform.TransformPoint(v)))).ToList());
        int roadOverlap = FootprintOverlaps(footprint, GameObject.Find("SM_B1_Road"));
        int railOverlap = FootprintOverlaps(footprint, GameObject.Find("SM_B1_Guardrail"));
        Check("çekici yola / korkuluğa taşmıyor", roadOverlap == 0 && railOverlap == 0,
            "XZ görsel zarf / üçgen kesişimi: yol=" + roadOverlap + ", korkuluk=" + railOverlap);

        var glass = AssetDatabase.LoadAllAssetsAtPath(Bolum1SceneBuilder.TowTruckPath).OfType<Material>()
            .SingleOrDefault(m => m.name == Bolum1ImportSettings.TowTruckGlassMaterial);
        float alpha = Convert.ToSingle(meta["glass_alpha"]);
        Check("çekici camı", glass != null && glass.GetFloat("_Surface") > 0.5f && glass.renderQueue >= 3000
            && Mathf.Abs(glass.GetColor("_BaseColor").a - alpha) < 0.001f,
            "Transparent; beklenen alfa=" + alpha.ToString("F2", Inv));
    }

    private static string HitMaterial(RaycastHit hit)
    {
        var mesh = ((MeshCollider)hit.collider).sharedMesh;
        var renderer = hit.collider.GetComponent<Renderer>();
        int first = 0;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int count = (int)mesh.GetIndexCount(sub) / 3;
            if (hit.triangleIndex < first + count) return renderer.sharedMaterials[sub].name;
            first += count;
        }
        return "YOK";
    }

    private static Vector2 XZ(Vector3 p) { return new Vector2(p.x, p.z); }
    private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        points = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        var hull = new List<Vector2>();
        foreach (var p in points)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lower = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            var p = points[i];
            while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static int FootprintOverlaps(List<Vector2> footprint, GameObject module)
    {
        int count = 0;
        foreach (var mf in module.GetComponentsInChildren<MeshFilter>())
        {
            var vertices = mf.sharedMesh.vertices.Select(v => XZ(mf.transform.TransformPoint(v))).ToArray();
            var triangles = mf.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var tri = new[] { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] };
                if (Mathf.Abs(Cross(tri[1] - tri[0], tri[2] - tri[0])) < 0.000001f) continue;
                if (!Separated(footprint, tri) && !Separated(tri, footprint)) count++;
            }
        }
        return count;
    }

    private static bool Separated(IList<Vector2> a, IList<Vector2> b)
    {
        for (int i = 0; i < a.Count; i++)
        {
            var edge = a[(i + 1) % a.Count] - a[i];
            if (edge.sqrMagnitude < 1e-12f) continue;
            var axis = new Vector2(-edge.y, edge.x).normalized;
            float amin = a.Min(p => Vector2.Dot(p, axis)), amax = a.Max(p => Vector2.Dot(p, axis));
            float bmin = b.Min(p => Vector2.Dot(p, axis)), bmax = b.Max(p => Vector2.Dot(p, axis));
            if (amax <= bmin + 0.001f || bmax <= amin + 0.001f) return true;
        }
        return false;
    }

    private sealed class ConePhysicsProbe
    {
        private readonly Rigidbody[] bodies;
        private readonly Vector3[] starts;
        private readonly float[] distances = new float[4], tilts = new float[4];
        private float penetration;

        public ConePhysicsProbe()
        {
            var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
            bodies = Bolum1TowTruck.ConeNames.Select(n => Bolum1TowTruck.Part(truck, n).GetComponent<Rigidbody>()).ToArray();
            starts = bodies.Select(b => b.position).ToArray();
        }

        public void Sample()
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                distances[i] = Mathf.Max(distances[i], Vector3.Distance(starts[i], bodies[i].position));
                // FBX konisinin yukarısı yerel +Z; elle simülasyonda interpolation transform'u geciktirebilir.
                tilts[i] = Mathf.Max(tilts[i], Vector3.Angle(bodies[i].rotation * Vector3.forward, Vector3.up));
                for (int j = i + 1; j < bodies.Length; j++)
                    if (Physics.ComputePenetration(bodies[i].GetComponent<Collider>(), bodies[i].position, bodies[i].rotation,
                        bodies[j].GetComponent<Collider>(), bodies[j].position, bodies[j].rotation, out _, out float depth))
                        penetration = Mathf.Max(penetration, depth);
            }
        }

        public void Report()
        {
            for (int i = 0; i < bodies.Length; i++)
                Line("  " + bodies[i].name + " maksimum hareket=" + (distances[i] * 100f).ToString("F3", Inv)
                    + " cm; maksimum eğim=" + tilts[i].ToString("F3", Inv) + "°");
            Check("çekici koni fiziği (Play, 3.00 sn / 150 × 0.02 sn)", Application.isPlaying && bodies.Length == 4
                && distances.All(d => d <= 0.05f) && tilts.All(t => t < 10f) && penetration < 0.0001f,
                "maksimum hareket=" + (distances.Max() * 100f).ToString("F3", Inv) + " cm; eğim=" + tilts.Max().ToString("F3", Inv)
                + "°; koniler arası giriş=" + (penetration * 1000f).ToString("F3", Inv) + " mm");
        }
    }

    private static void CaptureTowTruck()
    {
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
        Capture("cekici_sahne", Bolum1Coordinates.ToUnity(new Vector3(-11f, -40f, 13f)),
            Bolum1Coordinates.ToUnity(new Vector3(9f, -14f, -3.5f)), 48f);
        var look = truck.transform.position + Vector3.up * 1.5f;
        var pos = look + new Vector3(-9f, 5.5f, 10f);
        var sun = GameObject.Find("Sun").GetComponent<Light>();
        var oldColor = sun.color;
        float oldIntensity = sun.intensity;
        var oldRotation = sun.transform.rotation;
        var oldMode = RenderSettings.ambientMode;
        var oldAmbient = RenderSettings.ambientLight;
        var oldSky = RenderSettings.ambientSkyColor;
        var oldEquator = RenderSettings.ambientEquatorColor;
        var oldGround = RenderSettings.ambientGroundColor;
        try
        {
            sun.color = Color.white;
            sun.intensity = 1f;
            sun.transform.rotation = Quaternion.LookRotation(look - pos);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            Capture("cekici_notr", pos, look, 42f);
        }
        finally
        {
            sun.color = oldColor;
            sun.intensity = oldIntensity;
            sun.transform.rotation = oldRotation;
            RenderSettings.ambientMode = oldMode;
            RenderSettings.ambientLight = oldAmbient;
            RenderSettings.ambientSkyColor = oldSky;
            RenderSettings.ambientEquatorColor = oldEquator;
            RenderSettings.ambientGroundColor = oldGround;
        }
    }
}
