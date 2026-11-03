using System;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GarageBoardingSetup
{
    const string ScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";

    public static void ApplyBatch()
    {
        Bolum1AmbulanceAudioMute.Apply();
        AudioListener.volume = 0f;
        EditorUtility.audioMasterMute = true;
        try
        {
            Apply();
            Debug.Log("GARAGE BOARDING SETUP OK");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Rescue Team/Ofis-Garaj/Minibüse binme noktasını kur")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var runtime = UnityEngine.Object.FindAnyObjectByType<CompanyRuntime>();
        if (!runtime || !runtime.vehicle) throw new InvalidOperationException("Garaj aracı bulunamadı.");
        var vehicle = runtime.vehicle;
        var door = vehicle.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FrontDoor_Left");
        if (!door) throw new InvalidOperationException("FrontDoor_Left bulunamadı.");
        // Mevcut kapı açma hedefini koru; dış yüzeydeki binme alanı ışını önce yakalar.
        var target = door.Find("GarageBoardingPoint");
        if (!target)
        {
            target = new GameObject("GarageBoardingPoint").transform;
            target.SetParent(door, false);
        }
        var leaf = door.GetComponent<BoxCollider>();
        if (!leaf) throw new InvalidOperationException("Şoför kapısının collider'ı yok.");
        Vector3 center = leaf.bounds.center;
        Vector3 local = vehicle.transform.InverseTransformPoint(center);
        float side = local.x < 0 ? -1f : 1f;
        // Kapı yüzeyinin 12 cm dışında; chibi karakterin etkileşim yüksekliğinde.
        local.x += side * (.12f + leaf.bounds.extents.x);
        local.y = 1.25f;
        target.position = vehicle.transform.TransformPoint(local);
        target.rotation = vehicle.transform.rotation;
        target.localScale = Vector3.one;
        var boarding = target.GetComponent<GarageBoardingDoor>();
        if (!boarding) boarding = target.gameObject.AddComponent<GarageBoardingDoor>();
        boarding.runtime = runtime;
        var collider = target.GetComponent<BoxCollider>();
        if (!collider) collider = target.gameObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.center = Vector3.zero;
        collider.size = new Vector3(.24f, .8f, .8f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
