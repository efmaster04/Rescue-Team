using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bölüm 1 sahnesini GUI editörde açar, Scene view kamerasını genel görünüme getirir ve minibüsü seçer.
// GUI editörde: Unity.exe -projectPath ... -executeMethod Bolum1Showcase.Open (veya Rescue Team menüsü).
// Batchmode'da Scene view olmadığı için yalnızca sahne açılır, hata verilmez.
public static class Bolum1Showcase
{
    [MenuItem("Rescue Team/Bölüm 1/Sahneyi Aç ve Göster")]
    public static void Open()
    {
        // Sahne açılırken kaydedilmemiş değişiklik varsa kullanıcıya sorulur; batchmode'da soru sorulmaz.
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);

        var minivan = GameObject.Find(Bolum1SceneBuilder.MinivanName);
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
        if (minivan != null)
            Selection.activeGameObject = minivan; // Hierarchy'de minibüs seçili olsun

        var view = SceneView.lastActiveSceneView;
        if (view == null && !Application.isBatchMode && SceneView.sceneViews.Count > 0)
            view = (SceneView)SceneView.sceneViews[0];
        if (view == null || minivan == null || truck == null)
            return; // Scene view yok (batchmode): yalnız sahne açıldı

        // Yol + minibüs + çekici birlikte görünsün: ikisini kapsayan kutu, kamera biraz yukarıdan ve vadi yönünden bakar.
        var bounds = new Bounds(minivan.transform.position, Vector3.zero);
        bounds.Encapsulate(truck.transform.position);
        bounds.Expand(8f); // etraftaki yol da kadraja girsin
        view.in2DMode = false;
        view.Frame(bounds, true);
        // Frame yönü korur; genel görünüm için sabit bir bakış açısı ver (hafif yukarıdan, yola çapraz).
        view.LookAt(bounds.center, Quaternion.Euler(28f, 35f, 0f), bounds.extents.magnitude * 1.8f, false, true);
        view.Repaint();
    }
}
