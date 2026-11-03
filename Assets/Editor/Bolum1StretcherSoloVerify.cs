using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bölüm 1: boş sedye tek kişi, hastalı sedye iki kişi kabul testi.
[InitializeOnLoad]
public static class Bolum1StretcherSoloVerify
{
    private const string StageKey = "Bolum1StretcherSoloVerify.Stage";
    private const string BatchKey = "Bolum1StretcherSoloVerify.Batch";
    private static StringBuilder report;
    private static bool ok;
    private static bool batch;
    private static IEnumerator scenario;
    private static int lastFrame = -1;
    private static float start;
    private static int exceptions;

    static Bolum1StretcherSoloVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play") EditorApplication.update += PlayTick;
    }

    [MenuItem("Rescue Team/Bölüm 1/Boş sedye tek kişi: Doğrula")]
    public static void RunMenu() { Begin(false); }
    public static void Run() { Begin(true); }

    private static void Begin(bool batchMode)
    {
        report = new StringBuilder();
        ok = true;
        batch = batchMode;
        EditorUtility.audioMasterMute = true;
        AudioListener.volume = 0f;
        try { EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single); }
        catch (Exception e) { Fail("Sahne açılamadı: " + e); Finish(); return; }
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetString(StageKey, "play");
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    private static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.volume = 0f;
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (scenario == null)
        {
            if (Time.frameCount < 5) return;
            report = new StringBuilder();
            ok = true;
            batch = SessionState.GetBool(BatchKey, true);
            exceptions = 0;
            Application.logMessageReceived += OnLog;
            start = Time.realtimeSinceStartup;
            scenario = Scenario();
        }
        if (Time.realtimeSinceStartup - start > 120f) { Fail("Zaman aşımı"); End(); return; }
        try { if (!scenario.MoveNext()) End(); }
        catch (Exception e) { Fail("Play aşaması: " + e); End(); }
    }

    private static void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Exception) exceptions++;
    }

    private static IEnumerator Scenario()
    {
        TrafficDirector.SuppressedForTests = true;
        if (TrafficDirector.Instance != null) TrafficDirector.Instance.ClearVehicles();
        ThiefDirector.SuppressedForTests = true;
        if (ThiefDirector.Instance != null) ThiefDirector.Instance.ClearAll();
        var player = GameObject.Find(Bolum1SceneBuilder.CharacterName).GetComponent<ThirdPersonPlayer>();
        var interaction = player.GetComponent<PlayerInteraction>();
        var bot = interaction.helper;
        var stretcher = GameObject.Find("Stretcher_01").GetComponent<StretcherController>();
        player.GetComponent<PlayerInputReader>().enabled = false;
        Check("başlangıçta boş", stretcher.Passenger == null && stretcher.RequiredCarriers == 1,
            "gereken " + stretcher.RequiredCarriers);
        var lane = MissionDirector.Instance.lane;
        float laneS = lane.Length * 0.5f;
        Vector3 forward = lane.TangentAt(laneS); forward.y = 0f; forward.Normalize();
        Vector3 pos = lane.PointAt(laneS) - lane.RightAt(laneS) * 1.5f;
        stretcher.Teleport(pos, Quaternion.LookRotation(forward));
        TeleportToHandle(player, stretcher, false);
        yield return null; yield return null;
        string grabHint = interaction.CurrentHint;
        interaction.Interact();
        yield return null;
        Check("boş sedye E ile tek kişi kalkar, bot bağlanmaz", stretcher.IsRaised && stretcher.CarrierCount == 1
            && player.HeldStretcher == stretcher && (bot == null || bot.HeldStretcher != stretcher),
            "ipucu " + grabHint + ", mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount);
        Vector3 before = stretcher.transform.position;
        Vector3 cameraForward = player.cameraTransform.forward; cameraForward.y = 0f; cameraForward.Normalize();
        Vector3 cameraRight = player.cameraTransform.right; cameraRight.y = 0f; cameraRight.Normalize();
        player.SetMoveInput(new Vector2(Vector3.Dot(forward, cameraRight), Vector3.Dot(forward, cameraForward)));
        float until = Time.time + 2.7f;
        while (Time.time < until) yield return null;
        player.SetMoveInput(Vector2.zero);
        yield return null;
        float moved = Vector3.Distance(before, stretcher.transform.position);
        Check("oyuncu yürüyünce sedye merkezi >2 m ilerler", moved > 2f && stretcher.IsRaised,
            moved.ToString("F2") + " m");
        interaction.Interact();
        until = Time.time + Mathf.Max(0.5f, stretcher.liftTime + 0.2f);
        while (Time.time < until) yield return null;
        Check("bırakınca yerde ve tutuşsuz", !stretcher.IsRaised && stretcher.CarrierCount == 0,
            "mod " + stretcher.CurrentMode);
        var mission = MissionDirector.Instance;
        Bolum1TestPatients.LayOnPath(mission.patients);
        yield return null;
        Patient patient = mission.patients[0];
        Vector3 hips = patient.Position;
        Vector3 at = hips + Vector3.back * 0.8f;
        if (GroundProbe.Find(at + Vector3.up * 1.5f, 5f, out RaycastHit ground)) at.y = ground.point.y;
        Vector3 localAnchor = stretcher.transform.InverseTransformPoint(stretcher.patientAnchor.position);
        Quaternion rot = Quaternion.LookRotation(Vector3.right);
        stretcher.Teleport(at - rot * new Vector3(localAnchor.x, 0f, localAnchor.z), rot);
        Vector3 playerPos = hips + Vector3.back * 1.4f;
        if (GroundProbe.Find(playerPos + Vector3.up * 1.5f, 5f, out ground)) playerPos.y = ground.point.y + 0.02f;
        player.Teleport(playerPos, Quaternion.LookRotation(Vector3.forward));
        yield return null; yield return null;
        bool canPlace = stretcher.CanAcceptPatient(patient, player.transform.position, interaction.stretcherToPatientRange, interaction.patientRange);
        if (canPlace) stretcher.PlacePatient(patient);
        Check("yere bırakılan sedyeye hasta konur", canPlace && stretcher.Passenger == patient && stretcher.RequiredCarriers == stretcher.rules.requiredCarriers,
            "yerleştirme " + canPlace + ", gereken " + stretcher.RequiredCarriers);
        TeleportToHandle(player, stretcher, false);
        yield return null; yield return null;
        bot.assistEnabled = true;
        interaction.Interact();
        yield return null; yield return null;
        Check("hastalı sedye iki tutuşla kalkar", stretcher.IsRaised && stretcher.CarrierCount >= 2 && bot.HeldStretcher == stretcher,
            "mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount);
        Check("konsolda exception yok", exceptions == 0, "sayı " + exceptions);
    }

    private static void TeleportToHandle(ThirdPersonPlayer player, StretcherController stretcher, bool front)
    {
        Transform handle = front ? stretcher.handleFront : stretcher.handleRear;
        Vector3 outward = handle.position - stretcher.transform.position;
        outward.y = 0f; outward.Normalize();
        Vector3 pos = handle.position + outward * 0.55f;
        if (GroundProbe.Find(pos + Vector3.up * 1.5f, 5f, out RaycastHit ground)) pos.y = ground.point.y + 0.02f;
        player.Teleport(pos, Quaternion.LookRotation(-outward));
    }

    private static void Check(string label, bool pass, string detail)
    {
        ok &= pass;
        report.AppendLine((pass ? "[OK] " : "[FAIL] ") + label + " — " + detail);
    }
    private static void Fail(string message) { ok = false; report.AppendLine("[FAIL] " + message); }
    private static void End()
    {
        Application.logMessageReceived -= OnLog;
        EditorApplication.update -= PlayTick;
        SessionState.EraseString(StageKey);
        scenario = null;
        Finish();
    }
    private static void Finish()
    {
        report.AppendLine(ok ? "SOLO VERIFY OK" : "SOLO VERIFY FAILED");
        Debug.Log(report.ToString());
        EditorUtility.audioMasterMute = false;
        AudioListener.volume = 1f;
        if (batch) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}

