using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CompanySim;
using CompanySimEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Ofis-garaj sahnesine bizim oyuncu kurulumunu getirir (kullanıcı kararı: her yerde bizim karakter).
// Bölüm 1 sahnesindeki oyuncu (TestCharacter_RescueCrew: CharacterController, ThirdPersonPlayer, PlayerInteraction, PlayerInputReader,
// PlayerCombat, PlayerKnockdown + ragdoll, PlayerHealth, PlayerFallRespawn, CrewAnimation, FootGroundingIK, FirstPersonBody…) ve
// ana kamerası (OrbitCamera) olduğu gibi kopyalanır; Bölüm 1'e özgü sahne referansları (yardımcı bot, doğma noktası) ofis
// karşılıklarına bağlanır. Arkadaşın FPS oyuncusu (08_Player_Worker, PlayerInteractor + FirstPersonCamera) sahneden silinir; eski
// kameraya işaret eden referanslar yeni kameraya çevrilir. Karaktere CompanyPlayerBridge eklenir ve ComputerUI.player ona bağlanır.
// Ölçek: karakterin ölçeğine dokunulmaz (chibi oran korunur).
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod OfficeGaragePlayerSetup.ApplyBatch -logFile <dosya>
public static class OfficeGaragePlayerSetup
{
    public const string ScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
    public const string PlayerName = "08_Player_RescueCrew";
    public const string CameraName = "Main Camera";
    public const string SpawnName = "PlayerSpawn";
    public const float KillBelowY = -8f;      // ofis zemini y≈0; lift çukuru yok, altı boşluk sayılır
    public const float FarClip = 45f;         // arkadaşın optimizasyonu (GarageSystemsBuilder)
    public const float OfficeMinPitch = -60f; // yukarı bakış sınırı (yüksekteki modül cıvataları için; Bölüm 1'de −20)
    private const string OutDir = "Captures/OfficeGarage";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;

    [MenuItem("Rescue Team/Ofis-Garaj/Oyuncuyu Kur (bizim karakter)")]
    public static void ApplyMenu()
    {
        if (!EditorUtility.DisplayDialog("Ofis-Garaj", ScenePath + " içindeki oyuncu bizim karakterle değiştirilecek ve sahne kaydedilecek. Devam?", "Kur", "Vazgeç")) return;
        Apply();
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("OfficeGaragePlayerSetup: " + e); EditorApplication.Exit(1); }
    }

    public static void Apply()
    {
        report = new StringBuilder();
        Line("OFFICEGARAGE PLAYER SETUP " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 1) Arkadaşın oyuncusu (ya da önceki kurulumumuz): doğma yeri ve eski kamera
        Vector3 spawnPos; Quaternion spawnRot;
        Camera oldCam = null;
        var oldRoots = new List<GameObject>();
        PlayerInteractor friendPlayer = null;
        foreach (var p in UnityEngine.Object.FindObjectsByType<PlayerInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!(p is CompanyPlayerBridge)) friendPlayer = p;
        var ui = UnityEngine.Object.FindAnyObjectByType<ComputerUI>(FindObjectsInactive.Include);
        if (ui == null) throw new Exception("Sahnede ComputerUI yok");
        if (friendPlayer != null)
        {
            spawnPos = friendPlayer.transform.position;
            spawnRot = Quaternion.Euler(0f, friendPlayer.transform.eulerAngles.y, 0f);
            oldCam = friendPlayer.viewCamera;
            oldRoots.Add(friendPlayer.gameObject);
            Line("Arkadaşın oyuncusu: " + friendPlayer.gameObject.name + " " + V(spawnPos) + ", kamera " + (oldCam != null ? oldCam.name : "yok") + " (silinecek)");
        }
        else
        {
            var marker = GameObject.Find(SpawnName);
            var existing = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>(FindObjectsInactive.Include);
            if (marker == null && existing == null) throw new Exception("Ne arkadaşın oyuncusu ne PlayerSpawn ne de bizim oyuncu var");
            Transform src = marker != null ? marker.transform : existing.transform;
            spawnPos = src.position;
            spawnRot = Quaternion.Euler(0f, src.eulerAngles.y, 0f);
            Line("Yeniden kurulum: doğma yeri " + V(spawnPos));
        }
        // Önceki kurulumumuz varsa temizle (yeniden çalıştırılabilir)
        foreach (var tp in UnityEngine.Object.FindObjectsByType<ThirdPersonPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) oldRoots.Add(tp.gameObject);
        foreach (var oc in UnityEngine.Object.FindObjectsByType<OrbitCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (oldCam == null) oldCam = oc.GetComponent<Camera>();
            oldRoots.Add(oc.gameObject);
        }
        var oldMarker = GameObject.Find(SpawnName);
        if (oldMarker != null) oldRoots.Add(oldMarker);

        // 2) Bölüm 1'den oyuncu ve kamera kopyası
        var b1 = EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Additive);
        GameObject srcCharacter = null; OrbitCamera srcOrbit = null;
        foreach (var root in b1.GetRootGameObjects())
        {
            if (root.name == Bolum1SceneBuilder.CharacterName) srcCharacter = root;
            var o = root.GetComponentInChildren<OrbitCamera>(true);
            if (o != null && srcOrbit == null) srcOrbit = o;
        }
        if (srcCharacter == null || srcOrbit == null) throw new Exception("Bölüm 1 sahnesinde karakter / OrbitCamera bulunamadı");
        SceneManager.SetActiveScene(scene);
        var character = UnityEngine.Object.Instantiate(srcCharacter);
        character.name = PlayerName;
        SceneManager.MoveGameObjectToScene(character, scene);
        var camGo = UnityEngine.Object.Instantiate(srcOrbit.gameObject);
        camGo.name = CameraName;
        camGo.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var copiedComponents = new List<Component>();
        copiedComponents.AddRange(character.GetComponentsInChildren<Component>(true));
        copiedComponents.AddRange(camGo.GetComponentsInChildren<Component>(true));
        Line("Bölüm 1 kopyası: " + character.name + " (" + Names(character) + "), kamera (" + Names(camGo) + ")");

        // 3) Bölüm 1 sahnesindeki nesnelere (yardımcı bot, doğma noktası, görev yönetmeni…) giden referanslar temizlenir
        int cleared = 0;
        foreach (var c in copiedComponents)
        {
            if (c == null) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool changed = false;
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var v = it.objectReferenceValue;
                var sceneOf = v is GameObject go ? go.scene : v is Component comp ? comp.gameObject.scene : default;
                if (sceneOf.IsValid() && sceneOf == b1)
                {
                    it.objectReferenceValue = null; cleared++; changed = true;
                    Line("  Bölüm 1 referansı temizlendi: " + c.GetType().Name + "." + it.propertyPath + " (" + v.name + ")");
                }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        Line("Temizlenen referans: " + cleared);
        EditorSceneManager.CloseScene(b1, true);

        // 4) Eski oyuncu silinir, eski kameraya işaret eden sahne referansları yeni kameraya çevrilir
        var cam = camGo.GetComponent<Camera>();
        if (oldCam != null)
        {
            int repointed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (oldRoots.Contains(root) || root == character || root == camGo) continue;
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                        var v = it.objectReferenceValue;
                        if (v == oldCam) { it.objectReferenceValue = cam; changed = true; }
                        else if (v == oldCam.gameObject) { it.objectReferenceValue = camGo; changed = true; }
                        else if (v == oldCam.transform) { it.objectReferenceValue = camGo.transform; changed = true; }
                        else continue;
                        repointed++;
                        Line("  kamera referansı çevrildi: " + c.gameObject.name + "/" + c.GetType().Name + "." + it.propertyPath);
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            Line("Eski kamera referansı: " + repointed);
        }
        foreach (var go in oldRoots) if (go != null) UnityEngine.Object.DestroyImmediate(go);

        // 5) Konum: arkadaşın doğma yeri, zemine oturtulur (karakterin ölçeği değişmez)
        Vector3 at = spawnPos;
        foreach (var h in Physics.RaycastAll(spawnPos + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.collider.transform.IsChildOf(character.transform) && Mathf.Abs(h.point.y - spawnPos.y) < 0.5f) { at.y = h.point.y + 0.02f; break; }
        character.transform.SetPositionAndRotation(at, spawnRot); // ölçek kopyadan aynen gelir
        var marker2 = new GameObject(SpawnName);
        marker2.transform.SetPositionAndRotation(at, spawnRot);
        SceneManager.MoveGameObjectToScene(marker2, scene);
        Line("Oyuncu: " + V(at) + " yaw " + spawnRot.eulerAngles.y.ToString("F0", Inv) + "°, ölçek " + V(character.transform.lossyScale));

        // 6) Bileşen bağları
        var player = character.GetComponent<ThirdPersonPlayer>();
        var input = character.GetComponent<PlayerInputReader>();
        var interaction = character.GetComponent<PlayerInteraction>();
        var combat = character.GetComponent<PlayerCombat>();
        var orbit = camGo.GetComponent<OrbitCamera>();
        if (player == null || input == null || interaction == null || orbit == null) throw new Exception("Kopyada ThirdPersonPlayer / PlayerInputReader / PlayerInteraction / OrbitCamera eksik");
        player.cameraTransform = cam.transform;
        orbit.target = character.transform;
        input.player = player; input.interaction = interaction; input.orbitCamera = orbit; input.combat = combat;
        interaction.helper = null; // ofiste yardımcı bot yok
        var fall = character.GetComponent<PlayerFallRespawn>();
        if (fall != null) { fall.player = player; fall.spawnPoint = marker2.transform; fall.killBelowY = KillBelowY; }
        var health = character.GetComponent<PlayerHealth>();
        if (health != null) { health.player = player; health.respawnPoint = marker2.transform; }
        if (combat != null) { combat.player = player; combat.killBelowY = KillBelowY; if (combat.settings == null) Line("  UYARI: PlayerCombat.settings boş (fırlatma çalışmaz)"); }
        if (character.GetComponent<FirstPersonBody>() == null) character.AddComponent<FirstPersonBody>();

        // 7) Kamera: ana kamera etiketi, dinleyici, arkadaşın URP ayarı (post-process, FXAA) ve yakın kırpma uzaklığı
        cam.tag = "MainCamera";
        cam.farClipPlane = FarClip;
        if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
        foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.gameObject != camGo) { Line("  fazla AudioListener silindi: " + l.gameObject.name); UnityEngine.Object.DestroyImmediate(l); }
        OfficePresentationBuilder.ConfigureCamera(cam);
        // Ölçek: modül cıvataları / bağlantısı 1.9–2.2 m'de (arkadaşın 1.72 m insanı için). Chibi karakterin pivotu 1.3 m; üçüncü
        // şahısta yukarı bakış sınırı (−20°) bunlara yetmiyor. Yalnız bu sahnede yukarı −60°'ye kadar bakılır (Bölüm 1 değeri değişmez).
        orbit.minPitch = OfficeMinPitch;
        Line("Kamera: fov " + cam.fieldOfView.ToString("F0", Inv) + "°, yakın " + cam.nearClipPlane.ToString("F3", Inv) + ", uzak " + cam.farClipPlane.ToString("F0", Inv) + ", OrbitCamera uzaklık " + orbit.distance.ToString("F2", Inv) + " m, eğim sınırı " + orbit.minPitch.ToString("F0", Inv) + "…" + orbit.maxPitch.ToString("F0", Inv) + "°");

        // 8) Köprü: arkadaşın etkileşim dünyası bizim karakterle çalışır
        var bridge = character.GetComponent<CompanyPlayerBridge>();
        if (bridge == null) bridge = character.AddComponent<CompanyPlayerBridge>();
        bridge.viewCamera = cam; bridge.ui = ui; bridge.player = player; bridge.inputReader = input; bridge.interaction = interaction; bridge.combat = combat; bridge.orbitCamera = orbit;
        bridge.interactKey = Key.E; bridge.interactionDistance = 2.4f; bridge.reach = 2.4f; bridge.lookSensitivity = orbit.sensitivity;
        var uiSo = new SerializedObject(ui);
        uiSo.FindProperty("player").objectReferenceValue = bridge;
        uiSo.ApplyModifiedPropertiesWithoutUndo();
        Line("Köprü: CompanyPlayerBridge → ComputerUI.player");

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Sahne kaydedilemedi: " + ScenePath);
        AssetDatabase.SaveAssets();
        Line("Sahne kaydedildi: " + ScenePath);
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "player_setup_report.txt"), report.ToString());
        Debug.Log(report.ToString());
    }

    private static string Names(GameObject go)
    {
        var names = new List<string>();
        foreach (var c in go.GetComponents<Component>()) if (c != null) names.Add(c.GetType().Name);
        return string.Join(", ", names);
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
