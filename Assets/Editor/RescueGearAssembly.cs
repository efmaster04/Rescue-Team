using UnityEditor;
using UnityEngine;

// Kurtarma ekipmanı modellerini kaynak klasörlerdeki tasarıma göre birleştirir (02_KAYNAK_MODELLER README'leri):
//   Kurtarma vanı + drone istasyonu (yük bölmesinde, kök van kökünde) + drone (iniş pistindeki park noktasında)
//   + çeki demiri (kök van kökünde) + bot römorku (çeki topuna bağlı) + kurtarma botu (römorktaki yerleşim noktasında).
// FBX'ler Blender'dan -Z ileri / Y yukarı dışa aktarılır (Art/Blender/Scripts/export_rescue_gear.py). Unity eksen dönüşümünü
// modelin KÖK dönüşüne koyar (R0 = X çevresinde -90°); alt parçalar X'i aynalanmış Blender çerçevesinde kalır:
//   alt parça yerel = (-x, y, z) Blender; dünya = R0 · yerel = (-x, z, -y). Aracın önü Unity +X.
// Bu yüzden: bir modeli dönüşümsüz bir ebeveyne koyarken kök dönüşü korunur (Spawn); başka bir modelin noktasına takarken
// birim dönüş verilir (SpawnAt). Alt parçaların Blender açıları yerel çerçevede: Y çevresinde θ → AngleAxis(-θ, up),
// Z çevresinde θ → AngleAxis(-θ, forward), X çevresinde θ → AngleAxis(θ, right).
public static class RescueGearAssembly
{
    public const string Dir = "Assets/_Project/Art/Models/RescueGear/";

    static GameObject Load(string rel)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + rel);
        if (prefab == null) throw new System.Exception("Model yok: " + Dir + rel);
        return prefab;
    }

    /// Model under a plain (unconverted) parent: localRot is the Unity facing; the FBX root's axis rotation is kept.
    public static GameObject Spawn(string rel, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var prefab = Load(rel);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot * prefab.transform.localRotation;
        return go;
    }

    /// Model attached to a point (or the root) of another model of the same export: identity in that frame.
    public static GameObject SpawnAt(string rel, Transform point)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load(rel), point);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    public static Transform Find(Transform root, string prefix)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(prefix)) return t;
        return null;
    }

    /// Boat parked on its trailer (motor tilted up). Trailer front (+X) is the coupler. Returns the trailer root.
    public static GameObject TrailerWithBoat(Transform parent, Vector3 localPos, Quaternion localRot, bool hitched)
    {
        var trailer = Spawn("BotRomorku/BotRomorku.fbx", parent, localPos, localRot);
        trailer.name = "Bot_Romorku";
        var boat = SpawnAt("KurtarmaBotu/KurtarmaBotu.fbx", Find(trailer.transform, "RM_Bot_Yerlesim_Noktasi"));
        boat.name = "Kurtarma_Botu";
        // KB11 motor kaldırma braketi: Blender Y 0° (suda) → -70° (tam kalkık); römorkta kalkık durur
        var tilt = Find(boat.transform, "KB11");
        if (tilt != null) tilt.localRotation = Quaternion.AngleAxis(70f, Vector3.up);
        // RM05 destek tekerleği: Blender Y 0° park (yere basar) → 90° yolda (katlı)
        var jockey = Find(trailer.transform, "RM05");
        if (jockey != null && hitched) jockey.localRotation = Quaternion.AngleAxis(-90f, Vector3.up);
        return trailer;
    }

    /// The rescue convoy: van with the drone station (pad slid out, drone on it), tow bar, hitched boat trailer.
    /// Root = van origin (ground, mid-length, centreline); van front is local +X. Total length about 12.1 m.
    public static GameObject Convoy(Transform parent, Vector3 localPos, Quaternion localRot, bool padOut, float rearDoorsDeg)
    {
        var root = new GameObject("Kurtarma_Konvoyu");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPos;
        root.transform.localRotation = localRot;

        var van = Spawn("KurtarmaVani/KurtarmaVani.fbx", root.transform, Vector3.zero, Quaternion.identity);
        van.name = "Kurtarma_Vani (drone araci)";
        var station = SpawnAt("Drone/DroneIstasyonu.fbx", van.transform);
        station.name = "Drone_Istasyonu";
        var drone = SpawnAt("Drone/Drone.fbx", Find(station.transform, "DS_Drone_Park_Noktasi"));
        drone.name = "Drone";
        if (padOut)
        {
            // DS12 orta ray 0,70 m, DS13 iniş pisti 0,75 m arkaya kayar (Blender +X = yerel -X)
            var mid = Find(station.transform, "DS12"); if (mid != null) mid.localPosition += new Vector3(-0.70f, 0f, 0f);
            var pad = Find(station.transform, "DS13"); if (pad != null) pad.localPosition += new Vector3(-0.75f, 0f, 0f);
        }
        // Arka yük kapıları: menteşe Blender Z; sağ kapı +θ, sol kapı -θ ile dışa açılır → yerel AngleAxis(-θ, forward)
        foreach (var t in van.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("V21")) continue;
            float s = t.name.Contains("Sağ") ? 1f : -1f;
            t.localRotation = Quaternion.AngleAxis(-s * rearDoorsDeg, Vector3.forward);
        }
        SpawnAt("BotRomorku/CekiDemiri.fbx", van.transform).name = "Ceki_Demiri";
        // çeki topu van yerelinde (-3,00, 0,48, 0); römorkun kaplini römork yerelinde (+4,02, 0,48, 0) → römork kökü (-7,02, 0, 0)
        TrailerWithBoat(root.transform, new Vector3(-7.02f, 0f, 0f), Quaternion.identity, true);
        return root;
    }
}
