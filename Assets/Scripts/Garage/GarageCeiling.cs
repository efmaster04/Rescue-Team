using System;
using CompanySim;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GarageCeiling
{
    public static BoxCollider ColliderInstance { get; private set; }
    public static float UndersideY { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ColliderInstance = null;
        UndersideY = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Build(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Build(scene);
    }

    private static void Build(Scene scene)
    {
        if (scene.name != "OfficeGarage") return;

        // Yeniden yüklenen sahnede eski referansı kullanma; aynı sahnede iki kutu üretme.
        if (ColliderInstance != null && ColliderInstance.gameObject.scene == scene)
        {
            UpgradeItems(scene);
            return;
        }
        ColliderInstance = null;
        UndersideY = 0f;

        bool found = false;
        Bounds combined = default;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            {
                if (part.name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) < 0 || IsVehiclePart(part)) continue;
                // Yalnız parçanın kendi rendereri: üst adın altındaki ilgisiz mesh'ler sınırı büyütmesin.
                foreach (Renderer renderer in part.GetComponents<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    if (!found) { combined = renderer.bounds; found = true; }
                    else combined.Encapsulate(renderer.bounds);
                }
            }
        }

        if (!found)
        {
            Debug.LogWarning("GarageCeiling: OfficeGarage sahnesinde tavan parçası bulunamadı.");
            return;
        }

        UndersideY = combined.min.y;
        GameObject ceiling = new GameObject("GarageCeilingCollider");
        ceiling.layer = 0;
        SceneManager.MoveGameObjectToScene(ceiling, scene);
        ceiling.transform.position = new Vector3(combined.center.x, UndersideY + 0.15f, combined.center.z);
        ColliderInstance = ceiling.AddComponent<BoxCollider>();
        ColliderInstance.size = new Vector3(combined.size.x + 1f, 0.3f, combined.size.z + 1f);
        UpgradeItems(scene);
    }

    private static bool IsVehiclePart(Transform part)
    {
        for (Transform node = part; node != null; node = node.parent)
            if (node.name.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0 || node.GetComponent<ServiceVehicle>() != null)
                return true;
        return false;
    }

    private static void UpgradeItems(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body.collisionDetectionMode != CollisionDetectionMode.Discrete) continue;
                bool carryable = body.GetComponent<CarryableItem>() != null || body.GetComponent<LootItem>() != null;
                if (!carryable)
                    carryable = body.name.IndexOf("CarryableItem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                body.name.IndexOf("LootItem", StringComparison.OrdinalIgnoreCase) >= 0;
                if (carryable) body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }
    }
}
