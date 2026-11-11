using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Doğrulama: ağ bağlantısı, edit modunda fizik + trafik simülasyonu, görüntüler.
// Batchmode: -executeMethod OfisHaritasi.EditorTools.OHVerify.RunBatch  (ortam: OH_SIM=sn, OH_SHOTS=ad1,ad2)
namespace OfisHaritasi.EditorTools
{
    public static class OHVerify
    {
        static readonly StringBuilder R = new StringBuilder();

        public static void RunBatch()
        {
            int code = 0;
            try { code = Run() ? 0 : 2; }
            catch (System.Exception ex) { Debug.LogError("OHVerify: " + ex); R.AppendLine("HATA " + ex); code = 1; }
            File.WriteAllText(Path.Combine(OHAssetPrep.ReportDir(), "Dogrulama_Raporu.txt"), R.ToString());
            EditorApplication.Exit(code);
        }

        [MenuItem("Rescue Team/Ofis Haritası/Doğrula (simülasyon + görüntü)")]
        public static void RunMenu() { Run(); File.WriteAllText(Path.Combine(OHAssetPrep.ReportDir(), "Dogrulama_Raporu.txt"), R.ToString()); }

        public static bool Run()
        {
            R.Clear();
            bool ok = true;
            var scene = EditorSceneManager.OpenScene(OHMapBuilder.ScenePath);
            var root = GameObject.Find(OHMapBuilder.RootName);
            if (!root) { R.AppendLine("Harita kökü yok"); return false; }
            var net = root.GetComponentInChildren<OHTrafficNetwork>();
            var man = root.GetComponentInChildren<OHTrafficManager>();
            net.Init();
            ok &= CheckNetwork(net);

            float simSeconds = 240f;
            float.TryParse(System.Environment.GetEnvironmentVariable("OH_SIM") ?? "", out simSeconds);
            if (simSeconds <= 0) simSeconds = 240f;
            ok &= Simulate(man, net, simSeconds);

            var shots = System.Environment.GetEnvironmentVariable("OH_SHOTS");
            if (shots != "yok") Shots(root, shots);
            return ok;
        }

        static bool CheckNetwork(OHTrafficNetwork net)
        {
            bool ok = true;
            int conns = net.lanes.Count(l => l.IsConnector);
            R.AppendLine($"Ag: {net.lanes.Count} serit ({conns} baglanti), {net.junctions.Count} kavsak, {net.sources.Length} kaynak, {net.sinks.Length} cikis");
            foreach (var l in net.lanes)
            {
                if (l.length < 0.5f) { R.AppendLine($"  KISA serit {l.id}"); ok = false; }
                if (l.next.Length == 0 && !l.sink) { R.AppendLine($"  CIKMAZ serit {l.id} (kenar {l.edge})"); ok = false; }
            }
            // her şerit bir kaynaktan erişilebilir mi, her şeritten bir çıkışa gidilebilir mi
            var reach = new HashSet<int>();
            var q = new Queue<int>(net.sources);
            foreach (var s in net.sources) reach.Add(s);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int n in net.lanes[c].next) if (reach.Add(n)) q.Enqueue(n); }
            int unreach = net.lanes.Count - reach.Count;
            R.AppendLine($"  kaynaklardan erisilemeyen serit: {unreach}");
            if (unreach > 0) ok = false;
            // ters yön: çıkışa ulaşabilen
            var prev = new List<int>[net.lanes.Count];
            for (int i = 0; i < prev.Length; i++) prev[i] = new List<int>();
            foreach (var l in net.lanes) foreach (int n in l.next) prev[n].Add(l.id);
            var toSink = new HashSet<int>(net.sinks);
            q = new Queue<int>(net.sinks);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int p in prev[c]) if (toSink.Add(p)) q.Enqueue(p); }
            R.AppendLine($"  cikisa ulasamayan serit: {net.lanes.Count - toSink.Count}");
            foreach (var j in net.junctions)
            {
                int y = j.connectors.Count(c => net.lanes[c].yields);
                R.AppendLine($"  {j.name}: {j.connectors.Length} baglanti, {y} yol veren, isik={j.signalized}");
            }
            return ok;
        }

        static bool Simulate(OHTrafficManager man, OHTrafficNetwork net, float seconds)
        {
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            const float dt = 0.02f;
            var focusPts = new[] {
                new Vector3(4, 1.7f, -16), new Vector3(-150, 1.7f, -22), new Vector3(130, 1.7f, -22), new Vector3(-45, 1.7f, -100),
                new Vector3(288, 1.7f, -16), new Vector3(-150, 1.7f, -180), new Vector3(340, 1.7f, -170), new Vector3(4, 1.7f, -16) };
            man.hasFocusOverride = true;
            man.focusOverride = focusPts[0];
            man.SpawnAll();
            int steps = Mathf.RoundToInt(seconds / dt);
            int overlaps = 0, maxQueue = 0;
            var overlapPairs = new HashSet<string>();
            float physicsShare = 0;
            var lastPos = new Dictionary<OHVehicle, Vector3>();
            float maxStill = 0; OHVehicle stillest = null;
            var still = new Dictionary<OHVehicle, float>();
            var t0 = System.DateTime.Now;
            for (int i = 0; i < steps; i++)
            {
                float t = i * dt;
                man.focusOverride = focusPts[Mathf.Min(focusPts.Length - 1, (int)(t / (seconds / focusPts.Length)))];
                man.Step(dt);
                Physics.Simulate(dt);
                if (i % 25 == 0)
                {
                    physicsShare += man.CountMode(OHDriveMode.Physics);
                    var vs = man.vehicles;
                    for (int a = 0; a < vs.Count; a++)
                    {
                        var va = vs[a];
                        if (va.mode == OHDriveMode.Wreck) continue;
                        var ra = new OHRect(va.transform.TransformPoint(0, 0, va.axleMidZ + (va.frontExtent - va.rearExtent) * 0.5f), va.transform.rotation, va.width - 0.2f, va.Length - 0.25f);
                        for (int b = a + 1; b < vs.Count; b++)
                        {
                            var vb = vs[b];
                            if (vb.mode == OHDriveMode.Wreck) continue;
                            if ((va.transform.position - vb.transform.position).sqrMagnitude > 100f) continue;
                            var rb2 = new OHRect(vb.transform.TransformPoint(0, 0, vb.axleMidZ + (vb.frontExtent - vb.rearExtent) * 0.5f), vb.transform.rotation, vb.width - 0.2f, vb.Length - 0.25f);
                            if (ra.Overlaps(rb2, -0.05f)) { overlaps++; overlapPairs.Add($"{va.name}~{vb.name}@{va.transform.position:F0} {va.waitReason}/{vb.waitReason} {va.mode}/{vb.mode}"); }
                        }
                        float sp = Mathf.Abs(va.speed);
                        float st; still.TryGetValue(va, out st);
                        st = sp < 0.3f ? st + 0.5f : 0;
                        still[va] = st;
                        if (st > maxStill) { maxStill = st; stillest = va; }
                    }
                    int waiting = vs.Count(v => v.waitReason == "kavsak");
                    maxQueue = Mathf.Max(maxQueue, waiting);
                }
            }
            float secs = (float)(System.DateTime.Now - t0).TotalSeconds;
            Physics.simulationMode = prevMode;
            var odo = man.vehicles.Select(v => v.odometer).OrderBy(x => x).ToList();
            float avg = odo.Count > 0 ? odo.Average() : 0;
            R.AppendLine($"Simulasyon: {seconds:F0} sn ({secs:F0} sn hesap), arac {man.vehicles.Count}, ort. yol {avg:F0} m, en az {odo.FirstOrDefault():F0} m, en cok {odo.LastOrDefault():F0} m");
            R.AppendLine($"  tunel gecisi {man.teleportCount}, kaza/sikisma {man.failCount}, korna {man.honkCount}, kavsakta en cok bekleyen {maxQueue}");
            R.AppendLine($"  ortalama fizik kipindeki arac: {physicsShare / (steps / 25f):F1}");
            R.AppendLine($"  ust uste binme olayi: {overlaps} (cift {overlapPairs.Count})");
            foreach (var p in overlapPairs.Take(12)) R.AppendLine("    " + p);
            R.AppendLine($"  en uzun durma: {maxStill:F0} sn ({(stillest ? stillest.name + " " + stillest.waitReason + " @" + stillest.transform.position.ToString("F0") : "-")})");
            foreach (var l in man.log.Take(25)) R.AppendLine("  log: " + l);
            int misaligned = 0;
            foreach (var v in man.vehicles)
            {
                if (v.mode == OHDriveMode.Wreck || v.route.Count == 0) continue;
                Vector3 t; v.RoutePoint(v.s, out t);
                if (Vector3.Dot(v.transform.forward, t) < 0.5f) { misaligned++; R.AppendLine($"    TERS/YAN: {v.name} {v.mode} @{v.transform.position:F0} dot {Vector3.Dot(v.transform.forward, t):F2}"); }
            }
            R.AppendLine($"  sonda serit yonune ters/yan duran: {misaligned}");
            int slow = odo.Count(x => x < 60f);
            R.AppendLine($"  60 m'den az ilerleyen: {slow}");
            foreach (var v in man.vehicles.Where(v => v.odometer < 60f).Take(10))
                R.AppendLine($"    {v.name} {v.mode} {v.waitReason} hiz {v.speed:F1} @{v.transform.position:F0} serit {v.route[0]} s {v.s:F1}");
            return man.failCount <= 3 && overlapPairs.Count <= 3 && maxStill < 120f && misaligned == 0;
        }

        // ---------------------------------------------------------------- görüntüler

        class Shot { public string name; public Vector3 pos, look; public float fov = 55f; }

        static readonly Shot[] AllShots = {
            new Shot { name = "01_kus_bakisi", pos = new Vector3(40, 210, -470), look = new Vector3(10, 0, -70), fov = 62 },
            new Shot { name = "02_ofis_onu_sokak", pos = new Vector3(-12, 1.7f, -9.0f), look = new Vector3(40, 1.2f, -11f), fov = 62 },
            new Shot { name = "03_ofis_karsisi", pos = new Vector3(18, 1.7f, -15.5f), look = new Vector3(2, 2.5f, 0f), fov = 62 },
            new Shot { name = "04_bati_kavsak", pos = new Vector3(-118, 26, -48), look = new Vector3(-150, 0, -10), fov = 55 },
            new Shot { name = "05_dogu_kavsak_benzinlik", pos = new Vector3(110, 22, -50), look = new Vector3(150, 0, 0), fov = 58 },
            new Shot { name = "06_mahalle_sokagi", pos = new Vector3(-60, 1.7f, -97), look = new Vector3(-150, 2, -97), fov = 60 },
            new Shot { name = "07_park", pos = new Vector3(-20, 14, -110), look = new Vector3(40, 0, -138), fov = 60 },
            new Shot { name = "08_dere_koprusu", pos = new Vector3(300, 6, -30), look = new Vector3(285, 0, -9), fov = 60 },
            new Shot { name = "09_bati_tuneli", pos = new Vector3(-455, 4, -26), look = new Vector3(-495, 3, -32.5f), fov = 55 },
            new Shot { name = "10_dogu_tarlalar", pos = new Vector3(300, 40, -260), look = new Vector3(400, 0, -100), fov = 60 },
            new Shot { name = "11_sanayi_yolu", pos = new Vector3(-142, 1.7f, 8), look = new Vector3(-150, 2, 75), fov = 60 },
            new Shot { name = "12_tepe_manzara", pos = new Vector3(-60, 45, 190), look = new Vector3(-20, 0, -60), fov = 62 },
        };

        static void Shots(GameObject root, string filter)
        {
            var ws = root.GetComponent<OHWorldSettings>();
            if (ws) ws.Apply();
            string dir = OHAssetPrep.ShotDir();
            var go = new GameObject("OH_ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.2f; cam.farClipPlane = 2500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var set = string.IsNullOrEmpty(filter) ? null : new HashSet<string>(filter.Split(','));
            foreach (var s in AllShots)
            {
                if (set != null && !set.Any(f => s.name.Contains(f))) continue;
                cam.transform.position = s.pos;
                cam.transform.LookAt(s.look);
                cam.fieldOfView = s.fov;
                cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                RenderTexture.active = null;
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            R.AppendLine("Goruntuler: " + dir);
        }
    }
}
