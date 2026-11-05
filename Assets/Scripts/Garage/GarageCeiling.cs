using System;
using System.Collections.Generic;
using CompanySim;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ofis-garajda yukarı fırlatılan eşya tavandan çıkmasın (kullanıcı isteği): tavan parçalarının üstüne görünmez statik kutular kurar.
// Parçalar alt yüz yüksekliğine göre gruplanır, her gruba bir kutu. Tek kutuda birleştirmek olmaz: farklı yükseklikteki tavanlar
// (garaj ~4 m, yeni ofis odaları 3,24 m) bir araya gelince kutu en alçağa iner ve yüksek olanın altını da kapatır.
public static class GarageCeiling
{
    // Aynı gruba girecek alt yüzler arasındaki en büyük fark (m).
    private const float HeightGroupTolerance = .25f;
    // En yüksek grup duvarların üstünü örter. Alçak grup az taşar: yandaki yüksek tavanlı alana görünmez çıkıntı yapmasın.
    private const float HighestGroupMargin = .5f, LowerGroupMargin = .15f;
    private const float Thickness = .3f;

    private static readonly List<BoxCollider> colliders = new List<BoxCollider>();

    // Bütün tavan kutuları, alt yüzü alçaktan yükseğe.
    public static IReadOnlyList<BoxCollider> Colliders { get { return colliders; } }
    // En yüksek tavanın kutusu (tek gruplu sahnede tek kutu).
    public static BoxCollider ColliderInstance { get; private set; }
    public static float UndersideY { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        colliders.Clear();
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

        // Yeniden yüklenen sahnede eski referansı kullanma; aynı sahnede iki kez kutu üretme.
        if (ColliderInstance != null && ColliderInstance.gameObject.scene == scene)
        {
            UpgradeItems(scene);
            return;
        }
        colliders.Clear();
        ColliderInstance = null;
        UndersideY = 0f;

        List<Bounds> parts = CollectCeilingBounds(scene);
        if (parts.Count == 0)
        {
            Debug.LogWarning("GarageCeiling: OfficeGarage sahnesinde tavan parçası bulunamadı.");
            return;
        }

        parts.Sort((a, b) => a.min.y.CompareTo(b.min.y));
        var groups = new List<Bounds>();
        foreach (Bounds part in parts)
        {
            int last = groups.Count - 1;
            if (last >= 0 && part.min.y - groups[last].min.y <= HeightGroupTolerance)
            {
                Bounds merged = groups[last];
                merged.Encapsulate(part);
                groups[last] = merged;
            }
            else groups.Add(part);
        }

        for (int i = 0; i < groups.Count; i++)
        {
            bool highest = i == groups.Count - 1;
            colliders.Add(CreateBox(scene, groups[i], highest ? HighestGroupMargin : LowerGroupMargin));
        }
        ColliderInstance = colliders[colliders.Count - 1];
        UndersideY = groups[groups.Count - 1].min.y;
        UpgradeItems(scene);
    }

    private static List<Bounds> CollectCeilingBounds(Scene scene)
    {
        var parts = new List<Bounds>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            {
                if (part.name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) < 0 || IsVehiclePart(part)) continue;
                // Yalnız parçanın kendi rendereri: üst adın altındaki ilgisiz mesh'ler sınırı büyütmesin.
                // Gizlenmiş parça (eski ofis tavanı gibi) gerçek tavan sayılmaz.
                foreach (Renderer renderer in part.GetComponents<Renderer>())
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy) parts.Add(renderer.bounds);
            }
        }
        return parts;
    }

    private static BoxCollider CreateBox(Scene scene, Bounds group, float margin)
    {
        float underside = group.min.y;
        var ceiling = new GameObject("GarageCeilingCollider");
        ceiling.layer = 0;
        SceneManager.MoveGameObjectToScene(ceiling, scene);
        ceiling.transform.position = new Vector3(group.center.x, underside + Thickness * .5f, group.center.z);
        var box = ceiling.AddComponent<BoxCollider>();
        box.size = new Vector3(group.size.x + margin * 2f, Thickness, group.size.z + margin * 2f);
        return box;
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
