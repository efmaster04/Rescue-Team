using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CompanySim;
using SelVadisi;
using SelVadisi.EditorBuild;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Sel Vadisi saha görevi kabul testi (Rescue Team düzeni: Run → 'SELVADISI VERIFY OK', rapor Captures/SelVadisi).
// (a) veri: iş, ek katalog, Build Settings, diğer işler. (b) sahne: kamera, oyuncu, görev, dinleyici.
// (c) Play: aileler ve kıyıdan ulaşılabilirlik, bildirme, masada rapor, sahte köprü oturumunda FieldMissionTracker sonucu,
//     derin suda yeniden doğma.
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod SelVadisiVerify.Run -logFile <dosya>
public static class SelVadisiVerify
{
    private const string OutDir = "Captures/SelVadisi";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk, batch;
    private static int stage;
    private static double t0;
    private static SelVadisiMission mission;
    private static ThirdPersonPlayer player;
    private static PlayerFallRespawn fall;
    private static FieldMissionTracker tracker;
    private static MissionBridge.Session session;
    private static Vector3 spawnPos;
    private static List<Vector3> dry;
    private static readonly List<string> trace = new List<string>();
    private static readonly List<string> startTrace = new List<string>();

    [MenuItem("Rescue Team/Sel Vadisi/Doğrula")]
    public static void Menu() { Start(false); }

    public static void Run() { Start(true); }

    private static void Start(bool isBatch)
    {
        report = new StringBuilder(); allOk = true; batch = isBatch; stage = 0;
        Line("SELVADISI VERIFY " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            // (a) veri
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>(SelVadisiMissionSetup.JobPath);
            Check("(a) selvadisi işi → " + SelVadisiMissionSetup.SceneName, job != null && job.fieldScene == SelVadisiMissionSetup.SceneName && job.HasFieldScene,
                job != null ? job.title + " → " + job.fieldScene : "yok");
            var addon = AssetDatabase.LoadAssetAtPath<CatalogAddon>(SelVadisiMissionSetup.AddonPath);
            Check("(a) iş ek katalogda (OfficeGarage.unity değişmeden)", addon != null && addon.jobs != null && addon.jobs.Contains(job),
                addon != null && addon.jobs != null ? string.Join(", ", addon.jobs.Where(j => j != null).Select(j => j.id)) : "-");
            var scenes = EditorBuildSettings.scenes;
            Check("(a) Build Settings: OfficeGarage ilk, Sel Vadisi listede",
                scenes.Length > 0 && scenes[0].path == SelVadisiMissionSetup.OfficeScenePath && scenes.Any(s => s.path == SVBuild.ScenePath && s.enabled),
                string.Join(", ", scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
            var recovery = AssetDatabase.LoadAssetAtPath<JobDefinition>(MissionBridgeAssets.RecoveryJobPath);
            Check("(a) recovery işi hâlâ Bölüm 1'e bağlı", recovery != null && recovery.fieldScene == MissionBridgeAssets.FieldSceneName, recovery != null ? recovery.fieldScene : "yok");
            if (job != null)
            {
                // sözleşme hedef sürede sağlanır, geç kalınca sağlanmaz (aşınma FieldPayout'un kendi formülüyle)
                var onTime = FieldPayout.Compute(new FieldPayout.Inputs { succeeded = true, jobPayment = job.payment, distanceKm = job.distanceKm, fieldSeconds = SelVadisiMission.QuickResponseMinutes * 60f - 5f });
                var late = FieldPayout.Compute(new FieldPayout.Inputs { succeeded = true, jobPayment = job.payment, distanceKm = job.distanceKm, fieldSeconds = (SelVadisiMission.QuickResponseMinutes + 2f) * 60f });
                Check("(a) Hızlı müdahale sözleşmesi " + SelVadisiMission.QuickResponseMinutes.ToString("0", Inv) + " dk içinde sağlanır, geç kalınca sağlanmaz",
                    onTime.vehicleWear <= job.contractMaxVehicleWear && onTime.equipmentWear <= job.contractMaxEquipmentWear && late.vehicleWear > job.contractMaxVehicleWear,
                    "sınır %" + job.contractMaxEquipmentWear.ToString("0.#", Inv) + " / %" + job.contractMaxVehicleWear.ToString("0.#", Inv) +
                    ", zamanında araç %" + onTime.vehicleWear.ToString("0.#", Inv) + ", geç %" + late.vehicleWear.ToString("0.#", Inv));
            }
            string sceneGuid = AssetDatabase.AssetPathToGUID(SVBuild.ScenePath);
            Check("(a) harita sahnesinin GUID'i sabit (Build Settings her makinede aynı)", sceneGuid == SVBuild.HostSceneGuid, sceneGuid);

            // (b) sahne
            EditorSceneManager.OpenScene(SVBuild.ScenePath, OpenSceneMode.Single);
            Check("(b) haritanın serbest kamerası yok", UnityEngine.Object.FindObjectsByType<FlyCamera>(FindObjectsInactive.Include).Length == 0, "");
            var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            var main = cams.FirstOrDefault(c => c.CompareTag("MainCamera"));
            Check("(b) tek ana kamera ve OrbitCamera", cams.Length == 1 && main != null && main.GetComponent<OrbitCamera>() != null, cams.Length + " kamera");
            Check("(b) tek AudioListener", UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length == 1, "");
            var p = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>(FindObjectsInactive.Include);
            var spawn = GameObject.Find(SelVadisiMissionSetup.SpawnName);
            bool grounded = false;
            if (spawn != null)
                foreach (var h in Physics.RaycastAll(spawn.transform.position + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                    if (p == null || !h.collider.transform.IsChildOf(p.transform))
                        if (Mathf.Abs(h.point.y - spawn.transform.position.y) < 0.35f) grounded = true;
            Check("(b) oyuncu PlayerSpawn'da ve altında zemin var", p != null && spawn != null && Vector3.Distance(p.transform.position, spawn.transform.position) < 0.1f && grounded,
                spawn != null ? V(spawn.transform.position) : "doğma yok");
            var m = UnityEngine.Object.FindAnyObjectByType<SelVadisiMission>(FindObjectsInactive.Include);
            Check("(b) SelVadisiMission: masa, oyuncu, yeniden doğma, işaret malzemesi", m != null && m.reportDesk != null && m.player != null && m.fallRespawn != null && m.beaconMaterial != null,
                m != null ? "gereken " + m.requiredReports : "yok");
        }
        catch (Exception e) { Fail("hazırlık: " + e); Finish(); return; }

        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            double t = EditorApplication.timeSinceStartup - t0;
            switch (stage)
            {
                case 0:
                    t0 = EditorApplication.timeSinceStartup;
                    mission = SelVadisiMission.Instance;
                    player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
                    fall = player != null ? player.GetComponent<PlayerFallRespawn>() : null;
                    spawnPos = player != null ? player.transform.position : Vector3.zero;
                    // ofisten gelinmiş gibi bir köprü oturumu: takipçi görevi FieldMission.Current üzerinden okumalı
                    session = new MissionBridge.Session { jobId = "selvadisi", jobTitle = "Sel Vadisi: Mahsur Kalanlar", fieldScene = SelVadisiMissionSetup.SceneName, officeScene = "-", jobPayment = 1900, distanceKm = 64f };
                    tracker = new GameObject("Verify_FieldTracker").AddComponent<FieldMissionTracker>();
                    tracker.Setup(session);
                    stage = 1;
                    break;
                case 1:
                    if (player != null && startTrace.Count < 6 && Time.frameCount % 10 == 0)
                    {
                        var cc = player.GetComponent<CharacterController>();
                        startTrace.Add(t.ToString("F2", Inv) + " " + V(player.transform.position) + " yerde " + (cc != null && cc.isGrounded));
                    }
                    if (t < 2.5) break;
                    Check("(c) görev ve oyuncu hazır", mission != null && player != null && fall != null, "");
                    bool stayed = player != null && fall != null && mission != null && Vector3.Distance(player.transform.position, spawnPos) < 0.8f && fall.Respawns == 0 && mission.WaterRespawns == 0;
                    Check("(c) oyuncu doğduğu yerde duruyor (düşmedi, suya girmedi)", stayed,
                        player != null && fall != null && mission != null ? V(player.transform.position) + " doğma " + V(spawnPos) + ", düşme " + fall.Respawns + ", su " + mission.WaterRespawns + ", yere yığıldı " + player.IsDown : "");
                    if (!stayed) foreach (var tr in startTrace) Line("    başlangıç " + tr);
                    Check("(c) çatı/balkon aileleri bulundu", mission.GroupCount >= 4 && mission.Required >= 4 && mission.Required <= mission.GroupCount, mission.GroupCount + " aile, gereken " + mission.Required);
                    Reachability();
                    stage = 2;
                    break;
                case 2:
                    // en yakın kuru noktadan her aileyi sırayla bildir
                    int done = 0;
                    var order = Enumerable.Range(0, mission.GroupCount).OrderBy(i => NearestDry(mission.GroupCenter(i)).dist).ToList();
                    foreach (int i in order)
                    {
                        if (mission.Reported >= mission.Required) break;
                        var (pt, d) = NearestDry(mission.GroupCenter(i));
                        Vector3 eye = pt + Vector3.up * 1.4f;
                        if (mission.ReportAlong(eye, mission.GroupCenter(i) - eye)) done++;
                    }
                    Check("(c) gereken aileler kıyıdan bildirildi", mission.Reported >= mission.Required, mission.Reported + " / " + mission.Required + " aile, " + mission.PeopleReported + " kişi");
                    Check("(c) bildirilen ailelere işaret kondu", UnityEngine.Object.FindObjectsByType<SelVadisiBeacon>().Length == mission.Reported, "");
                    // masaya git ve rapor ver
                    Vector3 desk = mission.reportDesk.position + new Vector3(-1.2f, 0.05f, -0.6f);
                    player.Teleport(desk, Quaternion.identity);
                    stage = 3; t0 = EditorApplication.timeSinceStartup;
                    break;
                case 3:
                    if (t < 0.5) break;
                    bool finished = mission.TryFinishAtDesk();
                    Check("(c) koordinasyon masasında rapor verildi, görev başarılı", finished && mission.IsFinished && mission.Succeeded, "süre " + mission.Elapsed.ToString("F1", Inv) + " sn");
                    stage = 4; t0 = EditorApplication.timeSinceStartup;
                    break;
                case 4:
                    if (t < 0.6) break;
                    Check("(c) FieldMissionTracker sonucu hesapladı (köprü oturumunda)", tracker.ResultComputed && session.result != null && session.result.succeeded,
                        session.result != null ? "bonus " + session.result.bonus + ", gider " + session.result.expenses + ", araç aşınması " + session.result.vehicleWear.ToString("F1", Inv) : "sonuç yok");
                    // derin su: oyuncu kurtarma alanında yeniden doğmalı
                    Vector3 deep = DeepWater();
                    player.Teleport(deep, Quaternion.identity);
                    stage = 5; t0 = EditorApplication.timeSinceStartup;
                    break;
                case 5:
                    if (trace.Count < 8 && Time.frameCount % 5 == 0) trace.Add(t.ToString("F2", Inv) + " " + V(player.transform.position) + " su " + mission.WaterRespawns + " düşme " + fall.Respawns + " yere yığıldı " + player.IsDown);
                    if (t < 1.0) break;
                    bool respawned = mission.WaterRespawns >= 1 && Vector3.Distance(player.transform.position, spawnPos) < 3f;
                    Check("(c) derin suya giren oyuncu kurtarma alanında doğdu", respawned,
                        "yeniden doğma " + mission.WaterRespawns + ", konum " + V(player.transform.position));
                    if (!respawned) foreach (var tr in trace) Line("    iz " + tr);
                    stage = 6;
                    EditorApplication.ExitPlaymode();
                    Finish();
                    break;
            }
        }
        catch (Exception e)
        {
            Fail("play: " + e);
            stage = 99;
            EditorApplication.ExitPlaymode();
            Finish();
        }
    }

    // Kuru zemin örnekleri (sel suyunun en az 0.3 m üstü) ve her ailenin en yakın kuru noktaya uzaklığı.
    private static void Reachability()
    {
        dry = new List<Vector3>();
        for (float x = -520f; x <= 520f; x += 3f)
            for (float z = -760f; z <= 760f; z += 3f)
                if (Valley.Depth(x, z) < -0.3f) dry.Add(new Vector3(x, Valley.TerrainHeight(x, z), z));
        var dists = new List<float>();
        for (int i = 0; i < mission.GroupCount; i++) dists.Add(NearestDry(mission.GroupCenter(i)).dist);
        dists.Sort();
        int in75 = dists.Count(d => d <= 75f), in100 = dists.Count(d => d <= 100f), in120 = dists.Count(d => d <= 120f);
        Line("  kıyıdan uzaklık (m): en yakın " + (dists.Count > 0 ? dists[0].ToString("F0", Inv) : "-") + ", ortanca " + (dists.Count > 0 ? dists[dists.Count / 2].ToString("F0", Inv) : "-") +
             ", en uzak " + (dists.Count > 0 ? dists[dists.Count - 1].ToString("F0", Inv) : "-") + " | ≤75: " + in75 + ", ≤100: " + in100 + ", ≤120: " + in120 + " / " + dists.Count +
             " | menzil " + mission.reportRange.ToString("F0", Inv));
        Check("(c) gereken sayıda aile bildirme menzilinde kıyıdan görülebilir", dists.Count(d => d <= mission.reportRange - 2f) >= mission.Required,
            dists.Count(d => d <= mission.reportRange - 2f) + " aile menzilde, gereken " + mission.Required);
    }

    private static (Vector3 pt, float dist) NearestDry(Vector3 c)
    {
        Vector3 best = Vector3.zero; float bd = float.PositiveInfinity;
        foreach (var p in dry)
        {
            float dx = p.x - c.x, dz = p.z - c.z, d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = p; }
        }
        return (best, Mathf.Sqrt(bd));
    }

    private static Vector3 DeepWater()
    {
        for (float z = -300f; z <= 300f; z += 5f)
            for (float x = -200f; x <= 200f; x += 5f)
                if (Valley.Depth(x, z) > 2.5f) return new Vector3(x, Valley.TerrainHeight(x, z) + 0.1f, z);
        return spawnPos + new Vector3(30f, -5f, 0f);
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Line(allOk ? "SELVADISI VERIFY OK" : "SELVADISI VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "verify_report.txt"), report.ToString());
        Debug.Log(report.ToString());
        if (batch) EditorApplication.delayCall += () => EditorApplication.Exit(allOk ? 0 : 1);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (!ok) allOk = false;
        Line((ok ? "OK   " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
    }

    private static void Fail(string s) { allOk = false; Line("FAIL " + s); }
    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F1", Inv) + ", " + v.y.ToString("F1", Inv) + ", " + v.z.ToString("F1", Inv) + ")"; }
}
