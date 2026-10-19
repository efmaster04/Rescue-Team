using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// U3 (kullanıcı kararı "Can ve para" + gece kararları, 2026-10-19): can, değerli eşyalar, para ve çalınan çubuğu sahne kurulumu.
// Bolum1SceneBuilder.Build sonunda (9. adım) çağrılır; sahne her kurulumda baştan kurulur.
//  - Ayarlar: Assets/Settings/Bolum1/LootValues.asset (değerler), HealthRules.asset (can). Varsa dokunulmaz.
//  - Oyuncuya PlayerHealth; çekicinin yanında yeniden doğma noktası (yolun üstünde değil).
//  - Bolum1_Loot: LootRegistry, SafeDrop (çekici kasasının arka ucunun gerisinde, yoldan uzak yanda, zemin halkası).
//  - Eşyalar: yerdeki valiz/çanta prop'ları (Bolum1_Props; sabit tohumlu 2–4 içerik), her hastada bir eşya (koyu kutu, hastanın
//    kökünde karnında), ekipman kaydı (sedyeler, halat kangalı, koniler; sağlık çantası sahnede yok).
//  - Minibüs tavan bagajındaki valizler (SM_Minivan_RackSuitcase_01/02) minibüs parçasıdır ve 2 m'de tavandadır: sayılmaz (rapor).
public static class Bolum1LootBuilder
{
    public const string LootValuesPath = Bolum1AmbulanceBuilder.SettingsFolder + "/LootValues.asset";
    public const string HealthRulesPath = Bolum1AmbulanceBuilder.SettingsFolder + "/HealthRules.asset";
    public const string SafeDropMaterialPath = Bolum1AmbulanceBuilder.MaterialFolder + "/M_SafeDrop.mat";
    public const string PatientItemMaterialPath = Bolum1AmbulanceBuilder.MaterialFolder + "/M_PatientItem.mat";
    public const string ReportPath = "Captures/Bolum1/loot_build_report.txt";
    public const string LootRootName = "Bolum1_Loot";
    public const string RegistryName = "LootRegistry";
    public const string SafeDropName = "SafeDrop";
    public const string RespawnPointName = "HealthRespawnPoint";
    public const string PatientItemPrefix = "Valuable_";
    public const string PropsRootName = "Bolum1_Props";
    // Çekici kök uzayı = Blender çekici uzayı, X ters (Bolum1TowTruck.AddBox): kasa arka ucu y 3.92, ön −Y.
    public const float DeckRearLocalY = 3.92f;
    public const float SafeDropBehind = 0.9f, SafeDropSide = 1.7f;
    public const float RespawnBehind = 2.5f, RespawnSide = 3.0f;
    public const float RingDiameter = 1.3f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringBuilder report = new StringBuilder();
    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }

    public static LootValues Values { get { return AssetDatabase.LoadAssetAtPath<LootValues>(LootValuesPath); } }
    public static HealthRules Rules { get { return AssetDatabase.LoadAssetAtPath<HealthRules>(HealthRulesPath); } }

    public static void BuildAssets()
    {
        if (!AssetDatabase.IsValidFolder(Bolum1AmbulanceBuilder.SettingsFolder)) AssetDatabase.CreateFolder("Assets/Settings", "Bolum1");
        if (Values == null) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LootValues>(), LootValuesPath);
        if (Rules == null) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<HealthRules>(), HealthRulesPath);
        AssetDatabase.SaveAssets();
    }

    public static void SetupScene(GameObject truck, GameObject character)
    {
        report.Length = 0;
        Line("LOOT BUILD " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        Physics.SyncTransforms();
        var values = Values;
        var rules = Rules;
        if (values == null || rules == null) throw new Exception("LootValues / HealthRules yok (BuildAssets çağrılmadı mı?)");
        var gameplay = GameObject.Find(Bolum1AmbulanceBuilder.GameplayRootName).transform;
        var mission = GameObject.Find(Bolum1AmbulanceBuilder.MissionName).GetComponent<MissionDirector>();
        var root = new GameObject(LootRootName).transform;
        root.SetParent(gameplay, false);

        // 1) Kayıt
        var regGo = new GameObject(RegistryName);
        regGo.transform.SetParent(root, false);
        var registry = regGo.AddComponent<LootRegistry>();
        registry.values = values;
        registry.mission = mission;

        // 2) Çekici eksenleri; yoldan uzak yan
        Vector3 forward = Vector3.ProjectOnPlane(truck.transform.TransformDirection(new Vector3(0f, -1f, 0f)), Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 rearEnd = truck.transform.TransformPoint(new Vector3(0f, DeckRearLocalY, 0f));
        float sideSign = OffRoadSide(rearEnd, forward, right, out string sideInfo);

        // 3) Güvenli yer: kasa arka ucunun gerisinde, yoldan uzak yanda (sedyeler tam arkada, ±0.5 m'de)
        Vector3 sd = rearEnd - forward * SafeDropBehind + right * sideSign * SafeDropSide;
        sd = Ground(sd, truck.transform, out bool sdGround);
        if (!sdGround) throw new Exception("Teslim noktası için zemin bulunamadı: " + V(sd));
        var sdGo = new GameObject(SafeDropName);
        sdGo.transform.SetParent(root, false);
        sdGo.transform.SetPositionAndRotation(sd, Quaternion.LookRotation(-forward, Vector3.up));
        var safe = sdGo.AddComponent<SafeDropPoint>();
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Ring";
        ring.transform.SetParent(sdGo.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        ring.transform.localScale = new Vector3(RingDiameter, 0.006f, RingDiameter);
        UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(SafeDropMaterialPath, new Color(0.45f, 0.95f, 0.55f, 1f), new Color(0.1f, 0.5f, 0.2f));
        ring.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        safe.marker = ring.transform;
        Line("Teslim noktası (SafeDrop): " + V(sd) + ", kasa arka ucu " + V(rearEnd) + ", yan " + (sideSign > 0 ? "+sağ" : "−sağ") + " (" + sideInfo + "), halka Ø " + RingDiameter.ToString(Inv) + " m, menzil " + safe.interactRange.ToString(Inv) + " m");

        // 4) Yeniden doğma noktası: çekicinin yanı (yoldan uzak), kasanın gerisinde
        Vector3 rp = truck.transform.position - forward * RespawnBehind + right * sideSign * RespawnSide;
        rp = Ground(rp, truck.transform, out bool rpGround);
        if (!rpGround) { rp = Ground(sd + right * sideSign * 1.5f, truck.transform, out rpGround); }
        if (!rpGround) throw new Exception("Yeniden doğma noktası için zemin bulunamadı");
        var rpT = new GameObject(RespawnPointName).transform;
        rpT.SetParent(root, false);
        rpT.SetPositionAndRotation(rp + Vector3.up * 0.02f, Quaternion.LookRotation(forward, Vector3.up));
        bool rpOnRoad = OnRoad(rp);
        Line("Yeniden doğma noktası: " + V(rp) + ", yolda " + rpOnRoad + ", çekici köküne " + Vector3.Distance(rp, truck.transform.position).ToString("F2", Inv) + " m");

        // 5) Oyuncu canı
        var player = character.GetComponent<ThirdPersonPlayer>();
        var health = character.GetComponent<PlayerHealth>();
        if (health == null) health = character.AddComponent<PlayerHealth>();
        health.player = player;
        health.rules = rules;
        health.respawnPoint = rpT;
        health.crewAnimation = character.GetComponent<CrewAnimation>();
        Line("Oyuncu canı: " + rules.maxHealth.ToString(Inv) + ", kaldırılınca %" + Mathf.RoundToInt(rules.reviveFraction * 100f) + ", yeniden doğma " + rules.respawnSeconds.ToString(Inv)
            + " sn, dolma " + rules.regenDelay.ToString(Inv) + " sn sonra " + rules.regenPerSecond.ToString(Inv) + "/sn, sendeleme " + rules.staggerSeconds.ToString(Inv) + " sn");

        // 6) Valizler / çantalar (yerdekiler; tavan bagajı sayılmaz)
        var props = GameObject.Find(PropsRootName);
        int luggage = 0, luggageTotal = 0;
        if (props != null)
        {
            int idx = 0;
            foreach (Transform t in props.transform)
            {
                bool suitcase = t.name.Contains("Suitcase");
                bool bag = t.name.Contains("Bag");
                if (!suitcase && !bag) continue;
                var item = t.gameObject.AddComponent<LootItem>();
                item.displayName = suitcase ? "Valiz" : "Çanta";
                item.value = values.suitcaseValue;
                item.kind = LootKind.Luggage;
                item.carryable = true;
                item.contents = Roll(values, idx);
                luggage++;
                luggageTotal += item.TotalValue;
                Line("Valiz " + t.name + ": " + V(t.position) + ", " + item.displayName + " " + item.value + " + " + string.Join(", ", item.contents.Select(c => c.name + " " + c.value)) + " = " + item.TotalValue + " ₺");
                idx++;
            }
        }
        var van = GameObject.Find(Bolum1SceneBuilder.MinivanName);
        var rack = van != null ? van.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("RackSuitcase")).ToList() : new List<Transform>();
        Line("Tavan bagajı valizleri (" + rack.Count + ": " + string.Join(", ", rack.Select(r => r.name)) + ") minibüs parçası, " + (rack.Count > 0 ? (rack.Min(r => r.position.y) - van.transform.position.y).ToString("F2", Inv) : "-") + " m yukarıda: sayılmadı (yalnız yerdekiler)");

        // 7) Hasta eşyaları
        int patientTotal = 0;
        var itemMat = MakeMaterial(PatientItemMaterialPath, new Color(0.12f, 0.1f, 0.1f, 1f), Color.black);
        foreach (var p in mission.patients)
        {
            if (p == null) continue;
            var entry = values.PatientItem(p.name);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = PatientItemPrefix + p.name.Replace("Patient_", "");
            UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(p.transform, false);
            Vector3 hipsInRoot = p.hips != null ? p.transform.InverseTransformPoint(p.hips.position) : new Vector3(0f, 0.75f, 0f);
            box.transform.localPosition = hipsInRoot + new Vector3(0.1f, 0.0f, 0.14f);
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = new Vector3(0.16f, 0.05f, 0.11f);
            box.GetComponent<MeshRenderer>().sharedMaterial = itemMat;
            var item = box.AddComponent<LootItem>();
            item.displayName = entry.name;
            item.value = entry.value;
            item.kind = LootKind.PatientItem;
            item.carryable = true;
            item.patient = p;
            patientTotal += entry.value;
            Line("Hasta eşyası " + p.name + ": " + entry.name + " " + entry.value + " ₺, kök-yerel " + V(box.transform.localPosition));
        }

        // 8) Ekipman (para kazandırmaz; çalınırsa −)
        int equipTotal = 0;
        foreach (var n in Bolum1AmbulanceBuilder.StretcherNames)
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            equipTotal += Equipment(go, "Sedye", values.stretcherValue);
        }
        var coil = UnityEngine.Object.FindAnyObjectByType<RopeCoil>();
        if (coil != null) equipTotal += Equipment(coil.gameObject, "Halat", values.ropeValue);
        int cones = 0;
        foreach (var n in Bolum1TowTruck.ConeNames)
        {
            var go = GameObject.Find(Bolum1TrafficBuilder.ConeControllerName(n));
            if (go == null) continue;
            equipTotal += Equipment(go, "Koni", values.coneValue);
            cones++;
        }
        Line("Ekipman: sedye " + Bolum1AmbulanceBuilder.StretcherNames.Length + "×" + values.stretcherValue + ", halat " + (coil != null ? 1 : 0) + "×" + values.ropeValue + ", koni " + cones + "×" + values.coneValue
            + "; sağlık çantası sahnede yok (" + values.medkitValue + " ₺ kaydedilmedi)");
        int total = luggageTotal + patientTotal + equipTotal;
        Line("Toplam değer " + total + " ₺ (valiz " + luggage + " adet " + luggageTotal + ", hasta eşyası " + patientTotal + ", ekipman " + equipTotal + "); başarısızlık eşiği %" + Mathf.RoundToInt(values.failFraction * 100f) + " = " + Mathf.CeilToInt(total * values.failFraction) + " ₺");
        WriteReport();
    }

    private static int Equipment(GameObject go, string name, int value)
    {
        var item = go.GetComponent<LootItem>();
        if (item == null) item = go.AddComponent<LootItem>();
        item.displayName = name;
        item.value = value;
        item.kind = LootKind.Equipment;
        item.carryable = false;
        item.contents = new List<LootValues.Entry>();
        return value;
    }

    // Sabit tohumla 2–4 tekrarsız içerik; toplam [min, max] aralığında kalana kadar yeniden çekilir.
    public static List<LootValues.Entry> Roll(LootValues v, int index)
    {
        var rng = new System.Random(v.seed + index * 7919);
        List<LootValues.Entry> last = null;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            int count = rng.Next(v.minItems, v.maxItems + 1);
            var pool = Enumerable.Range(0, v.catalog.Length).OrderBy(_ => rng.Next()).Take(count).ToList();
            last = pool.Select(i => new LootValues.Entry(v.catalog[i].name, v.catalog[i].value)).ToList();
            int total = last.Sum(e => e.value);
            if (total >= v.minContentTotal && total <= v.maxContentTotal) return last;
        }
        return last ?? new List<LootValues.Entry>();
    }

    private static Vector3 Ground(Vector3 p, Transform exclude, out bool found)
    {
        found = false;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 10f).OrderBy(h => h.distance))
        {
            if (exclude != null && h.transform.IsChildOf(exclude)) continue;
            if (h.collider.isTrigger || GroundProbe.IsMovingGameObject(h.collider)) continue;
            found = true;
            return h.point;
        }
        return p;
    }

    private static bool OnRoad(Vector3 p)
    {
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 6f).OrderBy(h => h.distance))
        {
            if (h.collider.isTrigger || GroundProbe.IsMovingGameObject(h.collider)) continue;
            return h.transform.name.StartsWith("SM_B1_Road") || (h.transform.parent != null && h.transform.parent.name.StartsWith("SM_B1_Road"));
        }
        return false;
    }

    // Çekicinin hangi yanı yoldan uzak: kasa arkasının 3 m yanındaki noktalardan yolda olmayan, zemini bulunan taraf (+1 sağ / −1 sol).
    private static float OffRoadSide(Vector3 rearEnd, Vector3 forward, Vector3 right, out string info)
    {
        var scores = new List<string>();
        float best = 1f; int bestScore = int.MinValue;
        foreach (float sign in new[] { 1f, -1f })
        {
            Vector3 p = rearEnd - forward * RespawnBehind + right * sign * RespawnSide;
            Vector3 g = Ground(p, null, out bool found);
            int score = (found ? 2 : 0) + (found && !OnRoad(g) ? 4 : 0) + (found && Mathf.Abs(g.y - rearEnd.y) < 1.5f ? 1 : 0);
            scores.Add((sign > 0 ? "sağ" : "sol") + ": zemin " + found + ", yolda " + (found && OnRoad(g)) + ", y " + g.y.ToString("F2", Inv));
            if (score > bestScore) { bestScore = score; best = sign; }
        }
        info = string.Join("; ", scores);
        return best;
    }

    private static Material MakeMaterial(string path, Color baseColor, Color emission)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", 0.3f);
        if (emission.maxColorComponent > 0.01f)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }
}
