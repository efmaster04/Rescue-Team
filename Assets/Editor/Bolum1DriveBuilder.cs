using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Sürüş girişi kurulumu (kullanıcı kararı 2026-11-04). Bolum1SceneBuilder.Build sonunda (hırsız kurulumundan sonra) çağrılır:
//  - Park alanı (ParkingZone): minibüsün sahnedeki park pozu merkez; genişlik korkuluk ve şerit sınırına göre kırpılır;
//    yere boyalı sarı çerçeve (zemine raycast'le oturan şerit mesh'i, asset olarak kaydedilir).
//  - ServiceVanDriver bağlantıları: oyuncu, bot, kamera, park alanı.
//  - DriveIntro: Play'de minibüsü EastBound s = StartS'e ışınlar, ekibi oturtur; görev saati ve hırsızlar inişe kadar durur.
// Sahne dosyasında minibüs park pozunda kalır (öbür kurucular ve edit zamanı doğrulamalar bu düzeni görür).
public static class Bolum1DriveBuilder
{
    public const string ZoneName = "ServiceVan_ParkingZone";
    public const string OutlineName = "ServiceVan_ParkingZoneOutline";
    public const string IntroName = "Bolum1_DriveIntro";
    public const string OutlineMeshPath = Bolum1ServiceVan.AssetFolder + "/ParkingZoneOutline.asset";
    public const string PaintMaterialPath = Bolum1ServiceVan.AssetFolder + "/ParkingPaint.mat";
    public const string ReportPath = "Captures/Bolum1/drive_build_report.txt";
    // Alan payları (m): gövdenin yanlarına ve uçlarına; korkuluğa ve şeride en az bu kadar boşluk.
    public const float ZoneSideMargin = .7f, ZoneEndMargin = 1.3f, RailClearance = .3f, LaneClearance = .3f;
    public const float StartS = 12f;
    public const float PaintWidth = .15f, PaintLift = .02f, PaintStep = .4f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report = new StringBuilder();

    public static void SetupScene(Dictionary<string, object> markers, GameObject truck, GameObject character)
    {
        report = new StringBuilder();
        Line("DRIVE BUILD " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        Physics.SyncTransforms();
        var gameplay = GameObject.Find(Bolum1AmbulanceBuilder.GameplayRootName).transform;
        var east = GameObject.Find(Bolum1AmbulanceBuilder.LaneName).GetComponent<LanePath>();
        var west = GameObject.Find(Bolum1TrafficBuilder.WestLaneName).GetComponent<LanePath>();
        var driver = truck.GetComponent<ServiceVanDriver>();
        if (driver == null) throw new Exception("Minibüste ServiceVanDriver yok (Bolum1ServiceVan.SetupDriving çağrılmadı mı?)");
        var player = character.GetComponent<ThirdPersonPlayer>();
        var bot = UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>();
        var orbit = Camera.main.GetComponent<OrbitCamera>();

        // 1) Park alanı: minibüsün park pozu merkez; sağda korkuluk, solda EastBound şeridi
        Vector3 forward = driver.Forward; forward.y = 0f; forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float halfW = (driver.bodyMax.x - driver.bodyMin.x) * .5f + ZoneSideMargin;
        float halfL = (driver.bodyMax.z - driver.bodyMin.z) * .5f + ZoneEndMargin;
        var rail = GameObject.Find("SM_B1_Guardrail");
        float toRail = float.PositiveInfinity;
        if (rail != null)
            foreach (var h in Physics.RaycastAll(truck.transform.position + Vector3.up * .8f, right, 10f))
                if (h.transform.IsChildOf(rail.transform)) toRail = Mathf.Min(toRail, h.distance);
        if (!float.IsPositiveInfinity(toRail)) halfW = Mathf.Min(halfW, toRail - RailClearance);
        east.Project(truck.transform.position, out float lateral, out _);
        halfW = Mathf.Min(halfW, Mathf.Abs(lateral) - east.width * .5f - LaneClearance);
        if (halfW < (driver.bodyMax.x - driver.bodyMin.x) * .5f + .1f) throw new Exception("Park alanı minibüsten dar: yarı genişlik " + halfW.ToString("F2", Inv));

        var zoneGo = new GameObject(ZoneName);
        zoneGo.transform.SetParent(gameplay, false);
        zoneGo.transform.SetPositionAndRotation(truck.transform.position, truck.transform.rotation);
        var zone = zoneGo.AddComponent<ParkingZone>();
        zone.halfExtents = new Vector3(halfW, 1f, halfL);
        zone.lanes = new[] { east, west };
        BuildOutline(zoneGo.transform, halfW, halfL);
        Line("Park alanı " + ZoneName + ": merkez " + V(zoneGo.transform.position) + ", yarı ölçü " + halfW.ToString("F2", Inv) + " × " + halfL.ToString("F2", Inv)
            + " m (korkuluğa " + (float.IsPositiveInfinity(toRail) ? "yok" : toRail.ToString("F2", Inv) + " m") + ", EastBound yanal " + lateral.ToString("F2", Inv) + " m); çerçeve " + OutlineMeshPath);

        // 2) Sürücü bağlantıları
        driver.player = player;
        driver.bot = bot;
        driver.orbitCamera = orbit;
        driver.parkingZone = zone;
        Line("Sürücü: oyuncu " + (player != null ? player.name : "YOK") + ", bot " + (bot != null ? bot.name : "YOK") + ", kamera " + (orbit != null ? orbit.name : "YOK")
            + ", hız " + driver.maxSpeed.ToString(Inv) + " m/sn (geri " + driver.maxReverseSpeed.ToString(Inv) + "), ivme " + driver.acceleration.ToString(Inv) + ", fren " + driver.brakeDecel.ToString(Inv));

        // 3) Sürüş girişi
        var introGo = new GameObject(IntroName);
        introGo.transform.SetParent(gameplay, false);
        var intro = introGo.AddComponent<DriveIntro>();
        intro.van = driver;
        intro.lane = east;
        intro.startS = StartS;
        intro.mission = GameObject.Find(Bolum1AmbulanceBuilder.MissionName).GetComponent<MissionDirector>();
        intro.thieves = UnityEngine.Object.FindAnyObjectByType<ThiefDirector>();
        float zoneS = east.Project(truck.transform.position);
        Line("Sürüş girişi: EastBound s " + StartS.ToString(Inv) + " → park alanı s " + zoneS.ToString("F1", Inv) + " (" + (zoneS - StartS).ToString("F1", Inv) + " m sürüş); görev saati ve hırsızlar inişe kadar durur, trafik akar");
        WriteReport();
    }

    // Sarı çerçeve: dört kenar boyunca PaintStep aralıkla zemine raycast, PaintWidth genişliğinde şerit (alan-yerel vertex'ler).
    private static void BuildOutline(Transform zone, float halfW, float halfL)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Vector3[] corners = { new Vector3(-halfW, 0f, -halfL), new Vector3(halfW, 0f, -halfL), new Vector3(halfW, 0f, halfL), new Vector3(-halfW, 0f, halfL) };
        for (int e = 0; e < 4; e++)
        {
            Vector3 a = corners[e], b = corners[(e + 1) % 4];
            Vector3 dir = (b - a).normalized;
            Vector3 inward = new Vector3(-dir.z, 0f, dir.x); // sola 90°: saat yönünde köşe sırasında iç taraf
            if (Vector3.Dot(inward, -a) < 0f) inward = -inward;
            int steps = Mathf.Max(2, Mathf.CeilToInt((b - a).magnitude / PaintStep));
            int first = vertices.Count;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)i / steps);
                vertices.Add(Grounded(zone, p + inward * (PaintWidth * .5f)));
                vertices.Add(Grounded(zone, p - inward * (PaintWidth * .5f)));
            }
            for (int i = 0; i < steps; i++)
            {
                int n = first + i * 2;
                triangles.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
                triangles.AddRange(new[] { n, n + 1, n + 2, n + 1, n + 3, n + 2 }); // iki yüz (kamera açısından bağımsız)
            }
        }
        var mesh = new Mesh { name = "ParkingZoneOutline" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (AssetDatabase.LoadAssetAtPath<Mesh>(OutlineMeshPath) != null) AssetDatabase.DeleteAsset(OutlineMeshPath);
        AssetDatabase.CreateAsset(mesh, OutlineMeshPath);

        var go = new GameObject(OutlineName, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(zone, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = PaintMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // Alan-yerel nokta → zemine oturtulmuş alan-yerel nokta (zemin yoksa y = 0).
    private static Vector3 Grounded(Transform zone, Vector3 local)
    {
        Vector3 world = zone.TransformPoint(local);
        if (Bolum1ServiceVan.GroundBelow(world + Vector3.up * .5f, out RaycastHit hit)) world = hit.point;
        world += Vector3.up * PaintLift;
        return zone.InverseTransformPoint(world);
    }

    private static Material PaintMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(PaintMaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, PaintMaterialPath);
        }
        material.SetColor("_BaseColor", new Color(1f, .85f, .15f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
