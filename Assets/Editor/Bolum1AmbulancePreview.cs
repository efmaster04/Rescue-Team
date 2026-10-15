using System.IO;
using UnityEditor;
using UnityEngine;

// Ambulans oyunda 3. dakikada doğduğu için düzenleme modunda görünmez. Bu menü, ambulansı duraktaki
// hâliyle (arka kapılar ve basamak açık) Scene view'da gösterir. Önizleme sahneye KAYDEDİLMEZ,
// ses ve hareket bileşenleri kapalıdır; Play'e basıldığında ya da "Önizlemeyi Kaldır" ile silinir.
[InitializeOnLoad]
public static class Bolum1AmbulancePreview
{
    private const string PreviewName = "Ambulans_Onizleme";
    // Bu dosya varsa derlemeden sonra önizleme bir kez kendiliğinden açılır (Claude'un tetikleyicisi).
    private const string AutoShowMarker = "Temp/show_ambulance_preview";

    static Bolum1AmbulancePreview()
    {
        // Play'e girerken önizlemeyi kaldır: gerçek ambulansla karışmasın.
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Remove();
        };
        if (!Application.isBatchMode && File.Exists(AutoShowMarker))
        {
            File.Delete(AutoShowMarker);
            EditorApplication.delayCall += Show;
        }
    }

    [MenuItem("Rescue Team/Bölüm 1/Ambulansı Göster (önizleme)")]
    public static void Show()
    {
        Remove();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulancePrefabPath);
        var stop = Object.FindFirstObjectByType<AmbulanceStop>();
        if (prefab == null || stop == null)
        {
            Debug.LogWarning("Ambulans önizlemesi: prefab ya da AmbulanceStop bulunamadı. Bölüm 1 sahnesi açık mı?");
            return;
        }

        var preview = (GameObject)Object.Instantiate(prefab, stop.transform.position, stop.transform.rotation);
        preview.name = PreviewName;

        // Önizleme yalnız görüntüdür: script'ler ve sesler kapalı, sahneye kaydedilmez.
        var controller = preview.GetComponent<AmbulanceController>();
        foreach (var b in preview.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        foreach (var a in preview.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
        if (controller != null)
        {
            OpenHinge(preview.transform, controller.doorLeft, controller.doorLeftAxis, controller.doorLeftOpenAngle);
            OpenHinge(preview.transform, controller.doorRight, controller.doorRightAxis, controller.doorRightOpenAngle);
            OpenHinge(preview.transform, controller.rearStep, controller.stepAxis, controller.stepOpenAngle);
        }
        foreach (var t in preview.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave;

        Selection.activeGameObject = preview;
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            // Kamera arkada-sağda, hafif yukarıda: açık arka kapılar ve rampa ağzı birlikte görünsün.
            var t = preview.transform;
            var center = t.position + Vector3.up * 1.3f;
            var viewDir = (t.forward - t.right * 0.8f - Vector3.up * 0.5f).normalized; // kameradan ambulansa bakış
            view.LookAt(center, Quaternion.LookRotation(viewDir, Vector3.up), 14f, false, true);
            view.Repaint();
        }
    }

    [MenuItem("Rescue Team/Bölüm 1/Ambulans Önizlemesini Kaldır")]
    public static void Remove()
    {
        var existing = GameObject.Find(PreviewName);
        if (existing != null) Object.DestroyImmediate(existing);
    }

    // AmbulanceController.CacheHinge + SetDoors(1) ile aynı hesap: kök eksenini parçanın yerel eksenine çevirip aç.
    private static void OpenHinge(Transform root, Transform part, Vector3 rootAxis, float angle)
    {
        if (part == null) return;
        var localAxis = Quaternion.Inverse(part.rotation) * (root.rotation * rootAxis.normalized);
        part.localRotation = part.localRotation * Quaternion.AngleAxis(angle, localAxis);
    }
}
