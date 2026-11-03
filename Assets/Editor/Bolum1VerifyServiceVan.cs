using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Bolum1Verify'ın servis minibüsü bölümü: yerleşim, hiyerarşi/collider, zemine oturma, yola taşmama, ekipman ve koni fiziği.
public static partial class Bolum1Verify
{
    private static void CheckServiceVan(Dictionary<string, object> markers)
    {
        var van = GameObject.Find(Bolum1SceneBuilder.ServiceVanName);
        var placement = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "TowTruckPlacement");
        var position = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(placement, "position"));
        var rotation = Bolum1Coordinates.ToUnity(Bolum1Coordinates.QuatWxyz(placement["rotation_quaternion_wxyz"])) * Quaternion.Euler(0f, 180f, 0f);
        // Yükseklik ve eğim banketten gelir (AlignToGround); XZ ve yaw marker'dan.
        float xz = Vector2.Distance(XZ(position), XZ(van.transform.position));
        float yaw = Mathf.Abs(Mathf.DeltaAngle(rotation.eulerAngles.y, van.transform.eulerAngles.y));
        Check("minibüs yerleşimi (TowTruckPlacement, eski çekicinin yönü, ölçek 1)", xz < 0.01f && yaw < 1f
            && Vector3.Distance(van.transform.localScale, Vector3.one) < 0.001f,
            "Unity konum " + V(van.transform.position) + "; Euler " + V(van.transform.eulerAngles) + "; XZ sapma " + Cm(xz) + ", yaw sapma " + yaw.ToString("F2", Inv) + "°");

        // Hiyerarşi: görsel kopya, takılı modül, tavan lambası, açık arka kapılar; CompanySim betiği ve Rigidbody yok.
        bool visual = van.transform.Find(Bolum1ServiceVan.VisualName) != null;
        var mount = van.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == Bolum1ServiceVan.MountName);
        bool module = mount != null && mount.Find(Bolum1ServiceVan.ModuleName) != null && mount.Find(Bolum1ServiceVan.ModuleName).GetComponentsInChildren<MeshRenderer>().Length >= 2
            && mount.Find(Bolum1ServiceVan.ModuleName).localPosition.magnitude < 0.001f;
        bool signal = van.transform.Find(Bolum1ServiceVan.RoofSignalName) != null;
        bool doorsOpen = true;
        foreach (string side in new[] { "Left", "Right" })
        {
            var door = van.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RearDoor_" + side);
            doorsOpen &= door != null && Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(0f, door.localEulerAngles.y)) - Bolum1ServiceVan.DoorOpenAngle) < 0.5f && door.GetComponent<BoxCollider>() != null;
        }
        // Sürüş girişi (2026-11-04): minibüs artık sürülen araç; kökte yalnız sürüş bileşenleri (ServiceVanDriver, VehicleAudio) ve tek,
        // kinematik Rigidbody olur. CompanySim betiği ve başka Rigidbody yine yok.
        var scripts = van.GetComponentsInChildren<MonoBehaviour>(true);
        bool noScripts = scripts.All(s => s is ServiceVanDriver || s is VehicleAudio) && scripts.OfType<ServiceVanDriver>().Count() == 1
            && van.GetComponentsInChildren<Rigidbody>(true).Length == 1;
        var rootBody = van.GetComponent<Rigidbody>();
        bool physicsOk = rootBody != null && rootBody.isKinematic && !rootBody.useGravity && van.GetComponents<BoxCollider>().Length >= 6
            && van.GetComponent<UnityEngine.AI.NavMeshObstacle>() != null && van.GetComponent<UnityEngine.AI.NavMeshObstacle>().carving;
        bool noPlaceholder = !UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name.StartsWith("PH_"));
        Check("minibüs hiyerarşi / collider (görsel kopya, CassetteSocket'te modül, tavan lambası, 108° açık arka kapılar, yalnız sürüş betiği + kinematik Rigidbody + NavMeshObstacle)",
            visual && module && signal && doorsOpen && noScripts && physicsOk && noPlaceholder,
            "görsel " + visual + ", modül " + module + ", lamba " + signal + ", kapılar " + doorsOpen + ", yalnız sürüş betikleri " + noScripts + ", kök kutu " + van.GetComponents<BoxCollider>().Length + " (fizik " + physicsOk + "), PH yok=" + noPlaceholder);

        // Gövde tabanı dört tekerlek noktasında bankette (±8 cm, M_Gravel).
        var art = van.transform.Find(Bolum1ServiceVan.VisualName);
        Bounds body = Bolum1ServiceVan.LocalBounds(art, van.transform);
        bool wheelsOk = true;
        foreach (var local in Bolum1ServiceVan.WheelPointsLocal(body))
        {
            Vector3 world = van.transform.TransformPoint(local);
            bool groundFound = RayDownToModule(world + Vector3.up * 0.5f, "SM_B1_Ground", out RaycastHit hit);
            float gap = groundFound ? world.y - hit.point.y : 999f;
            string material = groundFound ? HitMaterial(hit) : "YOK";
            wheelsOk &= groundFound && material == "M_Gravel" && Mathf.Abs(gap) <= 0.08f;
            Line("  minibüs tekerlek noktası " + V(local) + " taban-banket " + Cm(gap) + "; yüzey " + material);
        }
        Check("minibüs 4 teker tabanı bankette ±8 cm", wheelsOk, "gövde tabanı → Ground/M_Gravel raycast; kapalı gövde " + V(body.size));

        // Dikey izdüşümde görsel parçaların dışbükey zarfı (açık kapılar ve modül dahil): yola ve korkuluğa taşmamalı.
        var footprint = ConvexHull(van.GetComponentsInChildren<MeshFilter>()
            .SelectMany(m => m.sharedMesh.vertices.Select(v => XZ(m.transform.TransformPoint(v)))).ToList());
        int roadOverlap = FootprintOverlaps(footprint, GameObject.Find("SM_B1_Road"));
        int railOverlap = FootprintOverlaps(footprint, GameObject.Find("SM_B1_Guardrail"));
        Check("minibüs yola / korkuluğa taşmıyor", roadOverlap == 0 && railOverlap == 0,
            "XZ görsel zarf / üçgen kesişimi: yol=" + roadOverlap + ", korkuluk=" + railOverlap);

        // Ekipman yük bölümünde: 4 serbest koni (3 kg, dışbükey MeshCollider), 2 sedye, halat kangalı dolapta (TowTruckLocker kapağı).
        var cargo = new Bounds(new Vector3(0f, (Bolum1ServiceVan.CargoFloor + 2.49f) * 0.5f, 1.0f), new Vector3(1.8f, 2.49f - Bolum1ServiceVan.CargoFloor, 3.3f));
        bool InCargo(Vector3 world) => cargo.Contains(van.transform.InverseTransformPoint(world));
        bool conesOk = true;
        foreach (string name in Bolum1ServiceVan.ConeNames)
        {
            var cone = GameObject.Find(name);
            var rb = cone != null ? cone.GetComponent<Rigidbody>() : null;
            var mc = cone != null ? cone.GetComponent<MeshCollider>() : null;
            conesOk &= rb != null && Mathf.Approximately(rb.mass, 3f) && rb.useGravity && !rb.isKinematic
                && rb.constraints == RigidbodyConstraints.None && mc != null && mc.convex && mc.enabled && !mc.isTrigger && InCargo(cone.transform.position);
        }
        var stretchers = Bolum1AmbulanceBuilder.StretcherNames.Select(n => GameObject.Find(n)).ToList();
        bool stretchersOk = stretchers.All(s => s != null && InCargo(s.transform.position) && s.GetComponent<StretcherController>() != null);
        var coil = UnityEngine.Object.FindAnyObjectByType<RopeCoil>();
        var locker = UnityEngine.Object.FindAnyObjectByType<TowTruckLocker>();
        var lockerDoor = van.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == Bolum1ServiceVan.DoorName);
        bool coilOk = coil != null && locker != null && lockerDoor != null && locker.door == lockerDoor && locker.coil == coil && coil.locker == locker
            && InCargo(coil.GripMid) && Vector3.Distance(coil.GripMid, van.transform.TransformPoint(Bolum1ServiceVan.LockerCenter)) < 0.08f;
        Check("ekipman yük bölümünde: 4 koni × 3 kg (raflarda), 2 sedye (zeminde), halat kangalı dolapta", conesOk && stretchersOk && coilOk,
            "koniler " + conesOk + ", sedyeler " + stretchersOk + " (" + string.Join(", ", stretchers.Select(s => s != null ? V(van.transform.InverseTransformPoint(s.transform.position)) : "YOK")) + "), kangal "
            + coilOk + (coil != null ? " kök-yerel " + V(van.transform.InverseTransformPoint(coil.GripMid)) : ""));
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
            bodies = Bolum1ServiceVan.ConeNames.Select(n => GameObject.Find(n).GetComponent<Rigidbody>()).ToArray();
            starts = bodies.Select(b => b.position).ToArray();
        }

        public void Sample()
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                distances[i] = Mathf.Max(distances[i], Vector3.Distance(starts[i], bodies[i].position));
                // Koni mesh'inin yukarısı yerel +Y; elle simülasyonda interpolation transform'u geciktirebilir.
                tilts[i] = Mathf.Max(tilts[i], Vector3.Angle(bodies[i].rotation * Vector3.up, Vector3.up));
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
            Check("minibüs koni fiziği (Play, 3.00 sn / 150 × 0.02 sn)", Application.isPlaying && bodies.Length == 4
                && distances.All(d => d <= 0.05f) && tilts.All(t => t < 10f) && penetration < 0.0001f,
                "maksimum hareket=" + (distances.Max() * 100f).ToString("F3", Inv) + " cm; eğim=" + tilts.Max().ToString("F3", Inv)
                + "°; koniler arası giriş=" + (penetration * 1000f).ToString("F3", Inv) + " mm");
        }
    }

    private static void CaptureServiceVan()
    {
        var van = GameObject.Find(Bolum1SceneBuilder.ServiceVanName);
        Capture("minibus_sahne", Bolum1Coordinates.ToUnity(new Vector3(-11f, -40f, 13f)),
            Bolum1Coordinates.ToUnity(new Vector3(9f, -14f, -3.5f)), 48f);
        var look = van.transform.position + Vector3.up * 1.5f;
        var pos = look + new Vector3(-9f, 5.5f, 10f);
        // Arka açıklık: ekipman (sedyeler, koniler, dolap) ve kapıdaki modül görünsün.
        Vector3 rear = Bolum1ServiceVan.RearEnd(van);
        Vector3 back = -Bolum1ServiceVan.Forward(van);
        Vector3 side = Vector3.Cross(Vector3.up, back);
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
            Capture("minibus_notr", pos, look, 42f);
            Vector3 rearLook = rear + Vector3.up * 1.4f - back * 0.8f;
            Vector3 rearPos = rear + back * 3.2f + side * 1.6f + Vector3.up * 1.9f;
            sun.transform.rotation = Quaternion.LookRotation(rearLook - rearPos);
            Capture("minibus_arka", rearPos, rearLook, 55f);
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
