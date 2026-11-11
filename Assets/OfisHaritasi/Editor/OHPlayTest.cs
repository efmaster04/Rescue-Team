using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Play modunda gerçek sahneyle kısa deneme: trafik Start/FixedUpdate'ten doğuyor mu, oyuncu zeminde mi,
// hata günlüğü temiz mi, kameranın uzak düzlemi açıldı mı. Kayıt dosyasına dokunmaz.
// Batchmode (-quit OLMADAN): -executeMethod OfisHaritasi.EditorTools.OHPlayTest.RunBatch
namespace OfisHaritasi.EditorTools
{
    [InitializeOnLoad]
    public static class OHPlayTest
    {
        const string Key = "OHPlayTest.Active";
        const string ReportKey = "OHPlayTest.Report";
        static double started, lastSample;
        static int stage;
        static readonly List<string> errors = new List<string>();
        static readonly StringBuilder R = new StringBuilder();
        static Vector3 playerStart;

        static OHPlayTest()
        {
            EditorApplication.playModeStateChanged += OnState;
        }

        [MenuItem("Rescue Team/Ofis Haritası/Play Modunda Dene")]
        public static void RunMenu()
        {
            // Begin sahneyi yeniden açar: açık sahnedeki kaydedilmemiş iş sorulmadan kaybolmasın
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Begin();
        }
        public static void RunBatch() { Begin(); }

        static void Begin()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetString(ReportKey, "");
            EditorSceneManager.OpenScene(OHMapBuilder.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true;
                errors.Clear(); R.Clear(); stage = 0;
                Application.logMessageReceived += OnLog;
                started = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                Application.logMessageReceived -= OnLog;
                SessionState.SetBool(Key, false);
                string text = SessionState.GetString(ReportKey, "");
                File.WriteAllText(Path.Combine(OHAssetPrep.ReportDir(), "PlayModu_Raporu.txt"), text);
                Debug.Log("OHPlayTest bitti\n" + text);
                if (Application.isBatchMode) EditorApplication.Exit(text.Contains("SONUC: TAMAM") ? 0 : 3);
            }
        }

        static void OnLog(string msg, string stack, LogType t)
        {
            if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert)
                if (errors.Count < 30) errors.Add(t + ": " + msg.Split('\n')[0] + " | " + (stack ?? "").Split('\n').FirstOrDefault());
        }

        static void Tick()
        {
            double t = EditorApplication.timeSinceStartup - started;
            var man = Object.FindAnyObjectByType<OHTrafficManager>();
            var cc = Object.FindAnyObjectByType<CharacterController>();
            if (stage == 0 && t > 3)
            {
                stage = 1;
                R.AppendLine($"Play modu: {Time.time:F1} sn oyun zamani");
                R.AppendLine($"  trafik araci: {(man ? man.vehicles.Count : -1)}, park etmis: {Object.FindObjectsByType<OHVehicle>(FindObjectsSortMode.None).Count(v => v.mode == OHDriveMode.Parked)}");
                var cam = Camera.main;
                R.AppendLine($"  ana kamera: {(cam ? cam.name + " uzak " + cam.farClipPlane : "yok")}, sis {RenderSettings.fog}");
                if (cc)
                {
                    playerStart = cc.transform.position;
                    R.AppendLine($"  oyuncu baslangic: {playerStart:F2} zeminde {cc.isGrounded}");
                    // oyuncuyu dışarıya, ofis önündeki kaldırıma al
                    cc.enabled = false;
                    cc.transform.position = new Vector3(-6f, 0.4f, -6.2f);
                    cc.enabled = true;
                }
            }
            if (stage == 1 && t > 8)
            {
                stage = 2;
                if (cc) R.AppendLine($"  kaldirimda oyuncu: {cc.transform.position:F2} zeminde {cc.isGrounded}");
                if (cc) { cc.enabled = false; cc.transform.position = new Vector3(-150f, 0.6f, -30f); cc.enabled = true; }
            }
            if (stage == 2 && t > 13)
            {
                stage = 3;
                if (cc) R.AppendLine($"  bati kavsak yaninda oyuncu: {cc.transform.position:F2} zeminde {cc.isGrounded}");
                if (cc) { cc.enabled = false; cc.transform.position = new Vector3(-2.8f, 0.3f, -1.3f); cc.enabled = true; }
            }
            if (stage == 3 && t > 75)
            {
                stage = 4;
                if (cc) R.AppendLine($"  ofise donen oyuncu: {cc.transform.position:F2} zeminde {cc.isGrounded}");
                if (man)
                {
                    var odo = man.vehicles.Select(v => v.odometer).ToList();
                    R.AppendLine($"  {Time.time:F0} sn sonra: arac {man.vehicles.Count}, ort. yol {(odo.Count > 0 ? odo.Average() : 0):F0} m, en az {(odo.Count > 0 ? odo.Min() : 0):F0} m, fizik kipinde {man.CountMode(OHDriveMode.Physics)}, kaza {man.failCount}, tunel {man.teleportCount}");
                    foreach (var l in man.log.Take(12)) R.AppendLine("    log: " + l);
                }
                R.AppendLine($"  hata/istisna: {errors.Count}");
                foreach (var e in errors.Take(15)) R.AppendLine("    " + e);
                bool ok = man && man.vehicles.Count > 30 && errors.Count == 0 && cc && cc.isGrounded && man.failCount <= 2;
                R.AppendLine(ok ? "SONUC: TAMAM" : "SONUC: SORUN VAR");
                SessionState.SetString(ReportKey, R.ToString());
                EditorApplication.ExitPlaymode();
            }
        }
    }
}
