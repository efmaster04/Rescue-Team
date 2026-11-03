using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bölüm 1: boş sedye tek kişi, hastalı sedye iki kişi kabul testi.
// (a) boş sedyeyi yalnız oyuncu kaldırır, bot bağlanmaz; (b) oyuncu yürüyünce sedye birlikte gider, serbest uç yere sürtmez,
// tutulan uç elde; (c) yere bırakılıp hasta konunca iki kişi gerekir, bot yardım eder; hasta inince bot bırakır;
// (e) minibüs yük zeminindeki boş sedyeyi dışarıdaki oyuncu tek başına alır ve geri yürüyerek çıkarır; (d) exception yok.
[InitializeOnLoad]
public static class Bolum1StretcherSoloVerify
{
    private const string StageKey = "Bolum1StretcherSoloVerify.Stage";
    private const string BatchKey = "Bolum1StretcherSoloVerify.Batch";
    private static StringBuilder report;
    private static bool ok;
    private static bool batch;
    private static IEnumerator scenario;
    // İç içe enumerator'lar (Walk, Seconds) Unity coroutine'i değil, burada elle sürülür (Bolum1AnimVerify deseni).
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
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
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.realtimeSinceStartup - start > 120f) { Fail("Zaman aşımı"); End(); return; }
        try
        {
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                break; // null: bir kare bekle
            }
            if (stack.Count == 0) End();
        }
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
        var mission = MissionDirector.Instance;
        mission.Begin(NoAmbulanceSchedule()); // ambulans gelmesin (asset değişmez)
        var player = GameObject.Find(Bolum1SceneBuilder.CharacterName).GetComponent<ThirdPersonPlayer>();
        var interaction = player.GetComponent<PlayerInteraction>();
        var bot = interaction.helper;
        var stretcher = GameObject.Find("Stretcher_01").GetComponent<StretcherController>();
        player.GetComponent<PlayerInputReader>().enabled = false;
        if (bot != null) bot.assistEnabled = true; // bot açık: boş sedyeye yine de bağlanmamalı
        Vector3 vanPosition = stretcher.transform.position;
        Quaternion vanRotation = stretcher.transform.rotation;
        Check("başlangıçta boş, gereken taşıyıcı 1", stretcher.Passenger == null && stretcher.RequiredCarriers == 1,
            "gereken " + stretcher.RequiredCarriers);

        // (a) açık yolda: boş sedyeyi yalnız oyuncu kaldırır, bot bağlanmaz
        var lane = mission.lane;
        float laneS = lane.Length * 0.5f;
        Vector3 forward = Flat(lane.TangentAt(laneS));
        Vector3 pos = lane.PointAt(laneS) - lane.RightAt(laneS) * 1.5f;
        stretcher.Teleport(pos, Quaternion.LookRotation(forward));
        TeleportToHandle(player, stretcher, false);
        yield return null; yield return null;
        string grabHint = interaction.CurrentHint;
        interaction.Interact();
        yield return null;
        Check("(a) boş sedye E ile tek kişi kalkar, bot bağlanmaz", stretcher.IsRaised && stretcher.CarrierCount == 1
            && player.HeldStretcher == stretcher && (bot == null || bot.HeldStretcher != stretcher),
            "ipucu " + grabHint + ", mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount);

        // (b) oyuncu yürür: sedye birlikte gider, serbest uç yere sürtmez, tutulan uç elde (IK)
        Vector3 before = stretcher.transform.position;
        Vector3 playerBefore = player.transform.position;
        yield return Walk(player, forward, 2.7f);
        float moved = Vector3.Distance(before, stretcher.transform.position);
        float playerMoved = Vector3.Distance(playerBefore, player.transform.position);
        float freeClear = FreeEndClearance(stretcher, true);
        float heldErr = Vector3.Distance(stretcher.handleRear.position, player.GetHandPoint(stretcher.transform.position));
        var ik = player.GetComponent<StretcherHandIK>();
        var anim = player.GetComponent<CrewAnimation>();
        Check("(b) oyuncu yürüyünce sedye merkezi >2 m ilerler; serbest uç zeminin üstünde; tutulan uç elde",
            moved > 2f && stretcher.IsRaised && freeClear > -0.01f && heldErr < 0.05f && (ik == null || ik.Weight > 0.9f),
            moved.ToString("F2") + " m (oyuncu " + playerMoved.ToString("F2") + " m, sedye hızı " + stretcher.Velocity.magnitude.ToString("F2") + " m/sn), ön sap zemin+sap yüksekliğinden " + (freeClear * 100f).ToString("F0") + " cm yukarıda, arka sap–el "
            + (heldErr * 100f).ToString("F1") + " cm, IK ağırlığı " + (ik != null ? ik.Weight.ToString("F2") : "-") + ", animasyon Carry=" + (anim != null ? anim.Carry.ToString() : "-"));
        interaction.Interact();
        yield return Seconds(Mathf.Max(0.5f, stretcher.liftTime + 0.2f));
        Check("(b2) bırakınca yerde ve tutuşsuz", !stretcher.IsRaised && stretcher.CarrierCount == 0,
            "mod " + stretcher.CurrentMode);

        // (c) hasta konur: iki kişi gerekir, bot yardım eder
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
        Check("(c) yere bırakılan sedyeye hasta konur, gereken taşıyıcı 2", canPlace && stretcher.Passenger == patient && stretcher.RequiredCarriers == stretcher.rules.requiredCarriers,
            "yerleştirme " + canPlace + ", gereken " + stretcher.RequiredCarriers);
        TeleportToHandle(player, stretcher, false);
        yield return null; yield return null;
        interaction.Interact();
        yield return null; yield return null;
        if (bot != null)
            Check("(c2) hastalı sedye: bot yardım eder, iki tutuşla kalkar", stretcher.IsRaised && stretcher.CarrierCount >= 2 && bot.HeldStretcher == stretcher,
                "mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount);
        else
            Check("(c2) bot yok: hastalı sedye tek kişiyle kalkmaz", !stretcher.IsRaised && stretcher.CarrierCount == 1,
                "mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount);
        // hasta sedyeden inince (ambulansa yükleme gibi) bot bırakır, sedye oyuncuda kalkık kalır
        if (bot != null && stretcher.IsRaised)
        {
            var taken = stretcher.TakePatient();
            yield return null; yield return null;
            Check("(c3) hasta inince bot bırakır, boş sedye oyuncuda kalkık kalır", bot.HeldStretcher == null && stretcher.IsRaised
                && stretcher.CarrierCount == 1 && player.HeldStretcher == stretcher && !stretcher.DropPending,
                "bot " + (bot.HeldStretcher != null ? "tutuyor" : "bıraktı") + ", mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount);
            if (taken != null) taken.Fall(Vector3.zero); // hasta yere (sonraki aşama için sedye boş)
        }
        if (player.HeldStretcher != null) { interaction.Interact(); yield return Seconds(0.6f); }

        // (e) minibüs: yük zeminindeki boş sedyeyi dışarıdaki oyuncu tek başına alır, serbest uç yük zeminine gömülmez,
        // arkaya yürüyünce sedye dışarı çıkar (motivasyon: iki taşıyıcı biri içeride biri dışarıda sıkışıyordu)
        stretcher.Teleport(vanPosition, vanRotation);
        yield return null;
        TeleportToHandle(player, stretcher, false);
        yield return null; yield return null;
        string vanHint = interaction.CurrentHint;
        interaction.Interact();
        yield return Seconds(Mathf.Max(0.4f, stretcher.liftTime + 0.1f));
        float vanClear = FreeEndClearance(stretcher, true);
        bool vanRaised = stretcher.IsRaised && stretcher.CarrierCount == 1 && (bot == null || bot.HeldStretcher != stretcher);
        Check("(e) minibüsteki boş sedye dışarıdan tek kişi kalkar; serbest uç yük zeminine gömülmez", vanRaised && vanClear > -0.01f,
            "ipucu " + vanHint + ", mod " + stretcher.CurrentMode + ", tutuş " + stretcher.CarrierCount
            + ", ön sap yük zemini+sap yüksekliğinden " + (vanClear * 100f).ToString("F0") + " cm yukarıda");
        Vector3 vanBefore = stretcher.transform.position;
        Vector3 outDir = -Flat(stretcher.handleFront.position - stretcher.handleRear.position);
        yield return Walk(player, outDir, 3.0f);
        float vanMoved = Vector3.Dot(stretcher.transform.position - vanBefore, outDir);
        Check("(e2) oyuncu arkaya yürüyünce sedye minibüsten >2 m çıkar", vanMoved > 2f && stretcher.IsRaised, vanMoved.ToString("F2") + " m");
        interaction.Interact();
        yield return Seconds(0.6f);
        Check("(d) konsolda exception yok", exceptions == 0, "sayı " + exceptions);
    }

    private static AmbulanceSchedule NoAmbulanceSchedule()
    {
        var a = ScriptableObject.CreateInstance<AmbulanceSchedule>();
        a.firstArrival = 99999f; a.interval = 99999f; a.wait = 15f;
        return a;
    }

    // Oyuncuyu dünya yönünde (kameraya göre girdiyle) seconds boyunca yürütür.
    private static IEnumerator Walk(ThirdPersonPlayer player, Vector3 direction, float seconds)
    {
        Vector3 cameraForward = Vector3.forward, cameraRight = Vector3.right;
        if (player.cameraTransform != null)
        {
            cameraForward = Flat(player.cameraTransform.forward);
            cameraRight = Vector3.Cross(Vector3.up, cameraForward);
        }
        player.SetMoveInput(new Vector2(Vector3.Dot(direction, cameraRight), Vector3.Dot(direction, cameraForward)));
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
        player.SetMoveInput(Vector2.zero);
        yield return null;
    }

    private static IEnumerator Seconds(float s)
    {
        float until = Time.time + s;
        while (Time.time < until) yield return null;
    }

    // Serbest sapın altındaki zemine göre fazlası: sap y − (zemin y + sapın kökten yüksekliği). Negatifse sedye zemine gömülü.
    private static float FreeEndClearance(StretcherController s, bool front)
    {
        Transform handle = front ? s.handleFront : s.handleRear;
        float handleHeight = s.transform.InverseTransformPoint(handle.position).y;
        if (!GroundProbe.Find(handle.position + Vector3.up * 0.3f, 3f, out RaycastHit hit)) return 9f;
        return handle.position.y - (hit.point.y + handleHeight);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    private static void TeleportToHandle(ThirdPersonPlayer player, StretcherController stretcher, bool front)
    {
        Transform handle = front ? stretcher.handleFront : stretcher.handleRear;
        Vector3 outward = Flat(handle.position - stretcher.transform.position);
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
