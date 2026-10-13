using System;
using System.Linq;
using UnityEngine;

// Yalnız editörde kurulum: hareketli parçalara oyun mekaniği eklemez.
public static class Bolum1TowTruck
{
    public const float ConeMass = 3f;
    public static readonly string[] WheelNames = {
        "SM_TowTruck_Wheel_F_L", "SM_TowTruck_Wheel_F_R",
        "SM_TowTruck_Wheel_R1_L", "SM_TowTruck_Wheel_R1_R",
        "SM_TowTruck_Wheel_R2_L", "SM_TowTruck_Wheel_R2_R"
    };
    public static readonly string[] ConeNames = {
        "SM_TowTruck_Cone_01", "SM_TowTruck_Cone_02", "SM_TowTruck_Cone_03", "SM_TowTruck_Cone_04"
    };

    public static Transform Part(GameObject truck, string name)
    {
        var part = truck.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name);
        if (part == null) throw new Exception("Çekici parçası eksik: " + name);
        return part;
    }

    public static void SetupPhysics(GameObject truck)
    {
        // Blender ölçülerinden gövde kutuları; kök uzayında X ters, Y/Z korunur.
        // Şasi, kaput, kabin, servis kasası ve üst tabla. Park hâlindeki kökte Rigidbody yok.
        AddBox(truck, new Vector3(-0.75f, -3.85f, 0.40f), new Vector3(0.75f, 3.93f, 1.12f));
        AddBox(truck, new Vector3(-1.20f, -4f, 0.72f), new Vector3(1.20f, -3.15f, 1.40f));
        AddBox(truck, new Vector3(-1.17f, -3.15f, 1.12f), new Vector3(1.17f, -1.05f, 2.45f));
        AddBox(truck, new Vector3(-1.25f, -0.95f, 1.12f), new Vector3(1.25f, 3.92f, 1.66f));
        AddBox(truck, new Vector3(-1.25f, -0.95f, 1.66f), new Vector3(1.25f, 3.92f, 1.75f));

        // Görsel tutucunun iki yan rayı ve beş bölücüsü de statik gövdeye aittir.
        foreach (float x in new[] { 0.63f, 1.07f })
            AddBox(truck, new Vector3(x - 0.015f, 0.14f, 1.75f), new Vector3(x + 0.015f, 2.10f, 1.85f));
        foreach (float y in new[] { 0.16f, 0.64f, 1.12f, 1.60f, 2.08f })
            AddBox(truck, new Vector3(0.645f, y - 0.015f, 1.75f), new Vector3(1.055f, y + 0.015f, 1.85f));

        foreach (string name in WheelNames)
        {
            var wheel = Part(truck, name);
            var bounds = wheel.GetComponent<MeshFilter>().sharedMesh.bounds;
            var sphere = wheel.gameObject.AddComponent<SphereCollider>();
            sphere.center = bounds.center;
            sphere.radius = Mathf.Max(bounds.extents.y, bounds.extents.z);
        }

        var hook = Part(truck, "SM_TowTruck_Crane_Hook");
        var hookBounds = hook.GetComponent<MeshFilter>().sharedMesh.bounds;
        var hookCollider = hook.gameObject.AddComponent<SphereCollider>();
        hookCollider.center = hookBounds.center;
        hookCollider.radius = hookBounds.extents.magnitude;

        foreach (string name in ConeNames)
        {
            var cone = Part(truck, name);
            var collider = cone.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = cone.GetComponent<MeshFilter>().sharedMesh;
            collider.convex = true;
            collider.contactOffset = 0.001f;
            var body = cone.gameObject.AddComponent<Rigidbody>();
            body.mass = ConeMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }

    private static void AddBox(GameObject truck, Vector3 blenderMin, Vector3 blenderMax)
    {
        var box = truck.AddComponent<BoxCollider>();
        var center = (blenderMin + blenderMax) * 0.5f;
        box.center = new Vector3(-center.x, center.y, center.z);
        box.size = blenderMax - blenderMin;
        box.contactOffset = 0.001f;
    }
}
