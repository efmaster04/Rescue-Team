using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Taşıma revizyonu kabul testi (kullanıcı isteği 2026-11-05). Her sınıf için: doğru Animator durumu, hız, koşu kısıtı, avuç → eşya
// yüzeyi (< 5 cm), eşya gövdeye / başa girmiyor, zıplama, FP'de görünürlük; TP ve FP duruş + yürüyüş görüntüleri.
// İnceleme senaryoları: koltukta LowerBody 0, nişanda yana adım, dalışta el-eşya, bırakmada dik durma, kol sıçraması, yeni alışta yumruk, üç sınıf fırlatma.
// Batchmode (ekran görüntüsü için -nographics OLMADAN):
//   -executeMethod CarryClassVerify.RunField   Bölüm 1: koni, çanta, valiz, kangal + yana adım
//   -executeMethod CarryClassVerify.RunGarage  Ofis-garaj: garaj eşyaları + modül paketi
// Başarıda "CARRY VERIFY OK" ve exit 0. Rapor: Captures/Carry/verify_<sahne>.txt, görüntüler: Captures/Carry/<eşya>_<kadraj>.png
// SESSİZLİK: Bolum1AmbulanceAudioMute editör sesini kapatır; Play boyunca AudioListener.volume = 0.
[InitializeOnLoad]
public static class CarryClassVerify
{
    private const string OutDir = "Captures/Carry";
    private const string StageKey = "CarryClassVerify.Stage", ReportKey = "CarryClassVerify.Report", OkKey = "CarryClassVerify.Ok", BatchKey = "CarryClassVerify.Batch", ModeKey = "CarryClassVerify.Mode", SaveBackupKey = "CarryClassVerify.SaveBackup";
    private const float GripLimit = 0.05f;      // avuç → eşya yüzeyi (m)
    private const float BodyLimit = 0.03f;      // eşya → gövde kapsülü (m)
    private const float HeadLimit = 0.03f;      // eşya → baş küresi (m)
    private const float HeadRadius = 0.26f;     // baş + kask küresi (m; merkez Head kemiği + 0.20 yukarı; mesh: ön 0.265, alt kask 0.988)

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StringBuilder report;
    private static bool allOk;
    private static bool batchMode;
    private static bool garage;

    private static ThirdPersonPlayer player;
    private static CarryHandIK ik;
    private static CrewAnimation crew;
    private static OrbitCamera orbit;
    private static Vector3 startPos;
    private static Quaternion startRot;
    private static readonly List<string> table = new List<string>();

    static CarryClassVerify()
    {
        if (SessionState.GetString(StageKey, "") == "play")
            EditorApplication.update += PlayTick;
    }

    public static void RunField() { Begin(true, false); }
    public static void RunGarage() { Begin(true, true); }
    [MenuItem("Rescue Team/Taşıma/Doğrula (Bölüm 1)")] public static void RunFieldMenu() { Begin(false, false); }
    [MenuItem("Rescue Team/Taşıma/Doğrula (Garaj)")] public static void RunGarageMenu() { Begin(false, true); }

    private static string SavePath { get { return Path.Combine(Application.persistentDataPath, "office_garage_v1.json"); } }

    private static void Begin(bool batch, bool inGarage)
    {
        Directory.CreateDirectory(OutDir);
        report = new StringBuilder();
        allOk = true;
        batchMode = batch;
        garage = inGarage;
        Line("CARRY VERIFY " + (inGarage ? "garaj " : "saha ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            string backup = "";
            if (inGarage)
            {
                EditorSceneManager.OpenScene(OfficeGaragePlayerSetup.ScenePath, OpenSceneMode.Single);
                // Oyuncunun kaydı testten etkilenmez (OfficeGarageVerify deseni)
                if (File.Exists(SavePath))
                {
                    backup = SavePath + ".carryverify_bak";
                    File.Copy(SavePath, backup, true);
                    File.Delete(SavePath);
                }
            }
            else EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath, OpenSceneMode.Single);
            SessionState.SetString(SaveBackupKey, backup);
        }
        catch (Exception e)
        {
            Fail("edit aşaması: " + e);
            Finish();
            return;
        }
        SessionState.SetString(ReportKey, report.ToString());
        SessionState.SetBool(OkKey, allOk);
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetBool(ModeKey, inGarage);
        SessionState.SetString(StageKey, "play");
        EditorApplication.update += PlayTick;
        EditorApplication.EnterPlaymode();
    }

    // =====================================================================================
    // Play aşaması altyapısı (Bolum1FirstPersonVerify deseni)
    // =====================================================================================
    private static IEnumerator scenario;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static float realStart = -1f;
    private static int lastFrame = -1;
    private static object waitCondition;

    private static void PlayTick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.volume = 0f; // test boyunca hoparlörden ses çıkmaz
        if (scenario == null)
        {
            if (realStart < 0f) { realStart = Time.realtimeSinceStartup; return; }
            if (Time.frameCount < 5) return;
            report = new StringBuilder(SessionState.GetString(ReportKey, ""));
            allOk = SessionState.GetBool(OkKey, false);
            batchMode = SessionState.GetBool(BatchKey, true);
            garage = SessionState.GetBool(ModeKey, false);
            scenario = Scenario();
            stack.Clear();
            stack.Push(scenario);
        }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (Time.realtimeSinceStartup - realStart > 900f) { Fail("zaman aşımı (15 dk)"); End(); return; }
        try
        {
            if (waitCondition is float until && Time.time < until) return;
            if (waitCondition is Func<bool> cond && !cond()) return;
            waitCondition = null;
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                waitCondition = top.Current;
                break;
            }
            if (stack.Count == 0) End();
        }
        catch (Exception e)
        {
            Fail("play aşaması: " + e);
            End();
        }
    }

    private static void End()
    {
        EditorApplication.update -= PlayTick;
        SessionState.EraseString(StageKey);
        Time.timeScale = 1f;
        scenario = null;
        ThiefDirector.SuppressedForTests = false;
        string backup = SessionState.GetString(SaveBackupKey, "");
        if (garage)
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) File.Move(backup, SavePath);
            }
            catch (Exception e) { Line("kayıt geri yüklenemedi: " + e.Message); }
        }
        SessionState.EraseString(SaveBackupKey);
        Finish();
    }

    private static void Finish()
    {
        if (table.Count > 0)
        {
            Line("TABLO (eşya | sınıf | boyut (elde) kg | yürüme | koşu):");
            foreach (var t in table) Line("  " + t);
        }
        Line(allOk ? "CARRY VERIFY OK" : "CARRY VERIFY FAILED");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, garage ? "verify_garaj.txt" : "verify_saha.txt"), report.ToString());
        Debug.Log(report.ToString());
        Debug.Log(allOk ? "CARRY VERIFY OK" : "CARRY VERIFY FAILED");
        Bolum1AmbulanceAudioMute.Restore();
        if (batchMode) EditorApplication.Exit(allOk ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private static float Seconds(float s) { return Time.time + s; }

    private static IEnumerator Scenario()
    {
        yield return null;
        player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
        ik = player.GetComponent<CarryHandIK>();
        crew = player.GetComponent<CrewAnimation>();
        orbit = Camera.main.GetComponent<OrbitCamera>();
        // Girdi test sürer: gerçek girdi okuyucusu her kare hareketi sıfırlardı
        var reader = player.GetComponent<PlayerInputReader>();
        if (reader != null) reader.enabled = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60; // batchmode sınırsız koşar; kare başı ölçümler 60 fps'de anlamlı
        Check("(0) kurulum: CarryHandIK ThirdPersonPlayer'a eklendi, LowerBody katmanı Crouch'un altında", ik != null && crew != null
            && player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName) >= 1 && player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName) < player.animator.GetLayerIndex(ThirdPersonPlayer.CrouchLayerName),
            "katmanlar " + player.animator.layerCount);
        if (garage) yield return GarageScenario();
        else yield return FieldScenario();
    }

    // =====================================================================================
    // Bölüm 1: koni, çanta / valiz, kangal, yana adım
    // =====================================================================================
    private static IEnumerator FieldScenario()
    {
        ThiefDirector.SuppressedForTests = true;
        var mission = MissionDirector.Instance;
        if (mission != null)
        {
            var a = ScriptableObject.CreateInstance<AmbulanceSchedule>();
            a.firstArrival = 99999f; a.interval = 99999f; a.wait = 15f;
            mission.missLimit = 999;
            mission.Begin(a);
        }
        if (TrafficDirector.Instance != null)
        {
            var s = UnityEngine.Object.Instantiate(Bolum1TrafficBuilder.Settings);
            s.useFixedSeed = true; s.seed = 1;
            s.calmSeconds = new Vector2(99999f, 99999f);
            s.calmSingleVehicleChance = 0f;
            TrafficDirector.Instance.Begin(s);
        }
        yield return null; yield return null;
        startPos = player.transform.position;
        startRot = player.transform.rotation;
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return WarmUp();

        var cone = TrafficCone.All.Count > 0 ? TrafficCone.All[0] : null;
        if (cone == null) Fail("sahnede koni yok");
        else yield return TestItem("koni", CarryClass.Light, () => { player.HeldCone = cone; cone.PickUp(player); }, () => { player.HeldCone = null; cone.PlaceWithPose(startPos + player.transform.right * 3f + Vector3.up * 0.1f, Quaternion.identity); },
            () => cone.target.GetComponentsInChildren<Renderer>());

        foreach (var l in LootItem.All.Where(x => x != null && x.carryable && (x.kind == LootKind.Luggage || x.kind == LootKind.PatientItem)).OrderBy(x => x.name).ToList())
        {
            var item = l;
            if (!item.CanPickUp) { Line("  atlandı (alınamaz): " + item.name); continue; }
            Vector3 at = item.transform.position;
            // Sınıf beklentisi sabit: Valiz ve Çanta orta (valiz kullanıcı kararı), hasta eşyası hafif
            CarryClass wantClass = item.kind == LootKind.Luggage ? CarryClass.Medium : CarryClass.Light;
            yield return TestItem(item.name.Replace("SM_B1_Prop_", ""), wantClass, () => { player.HeldValuable = item; item.PickUp(player); }, () => { player.HeldValuable = null; item.Drop(startPos + player.transform.right * 3f, 0f); },
                () => item.GetComponentsInChildren<Renderer>());
        }

        yield return CoilScenario();
        yield return StrafeScenario();
        var bag = LootItem.All.Where(x => x != null && x.carryable && x.kind == LootKind.Luggage && x.CanPickUp).OrderBy(x => x.name).FirstOrDefault();
        if (bag == null) Fail("sahnede alınabilir çanta yok");
        else
        {
            yield return AimStrafeScenario(bag);
            yield return TackleScenario("çanta", () => { player.HeldValuable = bag; bag.PickUp(player); }, () => { player.HeldValuable = null; bag.Drop(startPos + player.transform.right * 3f, 0f); });
            yield return JustPickedPunch("çanta", () => { player.HeldValuable = bag; bag.PickUp(player); }, () => { player.HeldValuable = null; bag.Drop(startPos + player.transform.right * 3f, 0f); });
        }
        yield return SeatScenario();
    }

    // Nişanda yana adım: gövde kameraya bakar, ayaklar Strafe_Right (bulgu 2)
    private static IEnumerator AimStrafeScenario(LootItem bag)
    {
        var combat = player.GetComponent<PlayerCombat>();
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        yield return Seconds(0.3f);
        player.HeldValuable = bag;
        bag.PickUp(player);
        yield return Seconds(1.3f);
        combat.SetThrowHeld(true);
        yield return Seconds(0.3f);
        bool aiming = combat.Aiming;
        Vector3 hand0 = player.animator.GetBoneTransform(HumanBodyBones.RightHand).position;
        player.SetMoveInput(new Vector2(1f, 0f));
        yield return Seconds(0.5f);
        int layer = player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName);
        float w = crew.LowerBodyWeight;
        string clip = TopClip(layer);
        bool aimState = crew.InState(CrewAnimation.StateThrowAim);
        yield return CaptureMain("nisan_yana_adim_orta");
        player.SetMoveInput(Vector2.zero);
        combat.CancelAim();
        yield return Seconds(0.4f);
        Check("(nişan) yürürken yana adım: LowerBody ağırlığı ~1, Strafe klibi, kollar Throw_Aim", aiming && w > 0.9f && clip == "Strafe_Right" && aimState,
            "nişan " + aiming + ", ağırlık " + w.ToString("F2", Inv) + ", klip " + clip + ", Throw_Aim " + aimState);
        player.HeldValuable = null;
        bag.Drop(startPos + player.transform.right * 3f, 0f);
        yield return null;
    }

    // Dalış: gövde uzanırken iki avuç eşyada (bulgu 3)
    private static IEnumerator TackleScenario(string label, Action pick, Action release)
    {
        var combat = player.GetComponent<PlayerCombat>();
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        yield return Seconds(0.3f);
        pick();
        yield return Seconds(1.3f);
        combat.ResetCooldownsForTest();
        player.SetRunInput(true);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.6f);
        int tackles0 = combat.Tackles;
        bool started = combat.Attack();
        player.SetRunInput(false);
        player.SetMoveInput(Vector2.zero);
        float grip = 0f, t0 = Time.time;
        bool state = false, shot = false;
        while (Time.time - t0 < 1.7f)
        {
            yield return null;
            state |= crew.InState(CrewAnimation.StateTackleDive);
            // Durum kayması (modeBlendSeconds) bittikten sonra
            if (Time.time - t0 > 0.4f) grip = Mathf.Max(grip, ik.GripErrorLeft, ik.GripErrorRight);
            if (!shot && Time.time - t0 > 0.7f) { shot = true; yield return CaptureSide("dalis_" + label, player.transform.position + player.transform.forward * 0.3f + Vector3.up * 0.3f); }
        }
        Check("(dalış " + label + ") eşya avuçlarda (el → eşya < " + Cm(GripLimit) + ")", started && combat.Tackles == tackles0 + 1 && state && grip < GripLimit,
            "başladı " + started + ", durum " + state + ", en büyük mesafe " + Cm(grip));
        yield return Seconds(0.4f);
        release();
        yield return null;
    }

    // Alır almaz yumruk: eşya yumruğu izlemez, gövdeye göre yerinde kalır (bulgu 6)
    private static IEnumerator JustPickedPunch(string label, Action pick, Action release)
    {
        var combat = player.GetComponent<PlayerCombat>();
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return Seconds(0.3f);
        combat.ResetCooldownsForTest();
        pick();
        yield return null;
        yield return null;
        bool started = combat.Attack();
        float drift = 0f, lateral = 0f, t0 = Time.time;
        Vector3 first = Vector3.zero;
        bool have = false;
        while (Time.time - t0 < 0.45f)
        {
            yield return null;
            if (Time.time - t0 < 0.17f) continue; // alışın ilk karesinden eve kayma (modeBlendSeconds)
            Vector3 local = player.transform.InverseTransformPoint(ik.LastCenter);
            if (!have) { first = local; have = true; }
            drift = Mathf.Max(drift, Vector3.Distance(first, local));
            lateral = Mathf.Max(lateral, Mathf.Abs(local.x));
        }
        Check("(yeni alış + yumruk " + label + ") eşya yumruğu izlemez: yan kayma < 6 cm, merkez sapması < 15 cm", started && lateral < 0.06f && drift < 0.15f,
            "yumruk " + started + ", yan " + Cm(lateral) + ", sapma " + Cm(drift));
        yield return Seconds(1.2f);
        release();
        yield return null;
    }

    // Yandan görüntü: oyuncu ve önündeki eşya bir arada görünsün; kamera duvara / ağaca girmeyen taraftan
    private static IEnumerator CaptureSide(string name, Vector3 look)
    {
        yield return null;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        var main = Camera.main;
        var go = new GameObject("CarryVerifyCam");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        cam.enabled = false;
        cam.nearClipPlane = 0.05f;
        float best = -1f;
        Vector3 place = look + Vector3.up * 2f;
        foreach (float yaw in new[] { 90f, -90f, 50f, -50f, 130f, -130f })
        {
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * player.transform.forward;
            Vector3 candidate = look + dir * 2.6f + Vector3.up * 0.9f;
            Vector3 to = candidate - look;
            float clear = Physics.SphereCast(look, 0.15f, to.normalized, out RaycastHit hit, to.magnitude, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(player.transform) ? hit.distance : to.magnitude;
            if (clear > best + 0.01f) { best = clear; place = look + to.normalized * Mathf.Max(0.5f, clear - 0.1f); }
        }
        go.transform.position = place;
        go.transform.LookAt(look);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
    }

    // Kol ve eşya sıçraması: eylem boyunca kök uzayında kare başı hız (m/sn)
    private class JumpMeter
    {
        public float item, left;
        private Vector3 lastItem, lastLeft;
        private bool have;
        public void Sample()
        {
            float dt = Mathf.Max(0.001f, Time.deltaTime);
            Vector3 i = player.transform.InverseTransformPoint(ik.LastCenter);
            Vector3 l = player.transform.InverseTransformPoint(player.animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
            if (have)
            {
                item = Mathf.Max(item, (i - lastItem).magnitude / dt);
                left = Mathf.Max(left, (l - lastLeft).magnitude / dt);
            }
            lastItem = i; lastLeft = l; have = true;
        }
    }

    // Minibüse binince LowerBody donmaz (bulgu 1): FP'de yana yürürken gerçek biniş
    private static IEnumerator SeatScenario()
    {
        var van = ServiceVanDriver.Instance;
        if (van == null) { Fail("sahnede minibüs yok"); yield break; }
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        yield return Seconds(0.3f);
        player.SetMoveInput(new Vector2(1f, 0f));
        yield return Seconds(0.4f);
        float before = crew.LowerBodyWeight;
        bool boarded = van.Board();
        int layer = player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName);
        if (!boarded) crew.enabled = false; // biniş reddedildiyse: aynı yol (DisableForSeat CrewAnimation'ı kapatır)
        yield return Seconds(0.3f);
        float layerW = player.animator.GetLayerWeight(layer);
        float weight = crew.LowerBodyWeight;
        Check("(koltuk) binince LowerBody ağırlığı 0 (Animator açık kalır)", before > 0.5f && layerW == 0f && weight == 0f,
            "binmeden " + before.ToString("F2", Inv) + ", biniş " + boarded + ", katman " + layerW.ToString("F2", Inv) + ", bileşen açık " + crew.enabled);
        if (!boarded) crew.enabled = true;
        player.SetMoveInput(Vector2.zero);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return null;
    }

    // Kangal: Shift'te Carry_Coil_Run hızı, zıplama yok, eller kangalda
    private static IEnumerator CoilScenario()
    {
        var coil = RopeCoil.All.Count > 0 ? RopeCoil.All[0] : null;
        if (coil == null) { Fail("sahnede kangal yok"); yield break; }
        player.RespawnAt(startPos, startRot);
        yield return Seconds(0.3f);
        player.HeldCoil = coil;
        coil.PickUp(player);
        yield return Seconds(1.3f);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.5f);
        float walk = player.HorizontalVelocity.magnitude;
        player.RespawnAt(startPos, startRot);
        player.HeldCoil = coil;
        coil.PickUp(player);
        yield return Seconds(0.5f);
        player.SetRunInput(true);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.5f);
        Vector3 p0 = player.transform.position;
        float t0 = Time.time;
        float maxGrip = 0f;
        while (Time.time - t0 < 0.9f)
        {
            maxGrip = Mathf.Max(maxGrip, coil.GripErrorLeft, coil.GripErrorRight);
            yield return null;
        }
        float run = Flat(player.transform.position - p0).magnitude / (Time.time - t0);
        yield return CaptureMain("kangal_kosu_tp");
        bool state = crew.InState(CrewAnimation.StateCarryCoil);
        player.SetRunInput(false);
        player.SetMoveInput(Vector2.zero);
        float y0 = player.transform.position.y;
        yield return Seconds(0.3f);
        player.RequestJump();
        yield return Seconds(0.3f);
        bool jumped = player.transform.position.y - y0 > 0.1f;
        Check("(kangal) Shift ile koşu " + player.Rules.coilRunSpeed.ToString("F1", Inv) + " m/sn, yürüyüş 2.4, zıplama yok, eller kangalda (≤ 3 cm)",
            state && Mathf.Abs(run - player.Rules.coilRunSpeed) <= 0.12f && Mathf.Abs(walk - player.moveSpeed) <= 0.15f && !jumped && maxGrip <= 0.03f,
            "koşu " + run.ToString("F2", Inv) + ", yürüyüş " + walk.ToString("F2", Inv) + ", zıpladı " + jumped + ", tutuş " + Cm(maxGrip) + ", durum " + state);
        player.RespawnAt(startPos, startRot);
    }

    // FP'de A/D ve S: LowerBody ağırlığı ve klipler; TP'de öne yürürken ağırlık 0
    private static IEnumerator StrafeScenario()
    {
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return Seconds(0.2f);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.8f);
        float tpWeight = crew.LowerBodyWeight;
        player.SetMoveInput(Vector2.zero);
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        yield return Seconds(0.4f);
        int layer = player.animator.GetLayerIndex(CrewAnimation.LowerBodyLayerName);
        player.SetMoveInput(new Vector2(1f, 0f));
        yield return Seconds(0.6f);
        float strafeW = crew.LowerBodyWeight;
        string strafeClip = TopClip(layer);
        float strafeSpeed = player.HorizontalVelocity.magnitude;
        player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.4f);
        float idleW = crew.LowerBodyWeight;
        player.RespawnAt(startPos, startRot);
        yield return Seconds(0.2f);
        player.SetMoveInput(new Vector2(0f, -1f));
        yield return Seconds(0.6f);
        float backW = crew.LowerBodyWeight;
        string backClip = TopClip(layer);
        player.SetMoveInput(Vector2.zero);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        Check("(yana adım) FP'de D → Strafe_Right, S → Walk_Back (ağırlık ~1); durunca ve TP'de öne yürürken 0",
            strafeW > 0.9f && strafeClip == "Strafe_Right" && backW > 0.9f && backClip == "Walk_Back" && idleW < 0.05f && tpWeight < 0.05f && strafeSpeed > 2f,
            "D ağırlık " + strafeW.ToString("F2", Inv) + " " + strafeClip + ", hız " + strafeSpeed.ToString("F2", Inv) + "; S " + backW.ToString("F2", Inv) + " " + backClip + "; dur " + idleW.ToString("F2", Inv) + ", TP " + tpWeight.ToString("F2", Inv));
        player.RespawnAt(startPos, startRot);
    }

    private static string TopClip(int layer)
    {
        var infos = player.animator.GetCurrentAnimatorClipInfo(layer);
        return infos.Length == 0 ? "-" : infos.OrderByDescending(c => c.weight).First().clip.name;
    }

    // =====================================================================================
    // Ofis-garaj: eşyalar ve modül paketi
    // =====================================================================================
    private static IEnumerator GarageScenario()
    {
        var ui = UnityEngine.Object.FindAnyObjectByType<ComputerUI>();
        var runtime = ui.Runtime;
        float t0 = Time.time;
        yield return (Func<bool>)(() => (runtime.Data != null && runtime.manualModules != null && runtime.manualModules.Initialized && player.HasControl) || Time.time - t0 > 6f);
        var bridge = player.GetComponent<CompanyPlayerBridge>();
        // Yürüme için boş yer: binanın arkasındaki çimen, duvara paralel
        Vector3 back = runtime.vehicle.transform.TransformPoint(new Vector3(0f, 0.02f, 6.5f));
        startRot = Quaternion.LookRotation(Flat(-runtime.vehicle.transform.right), Vector3.up);
        if (Physics.Raycast(back + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) back.y = hit.point.y + 0.02f;
        startPos = back;
        player.Teleport(startPos, startRot);
        yield return Seconds(0.2f);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return WarmUp();
        var catalog = runtime.catalog;
        var cases = new (string id, CarryClass want)[]
        {
            ("grease", CarryClass.Light), ("cone", CarryClass.Light),
            ("bin", CarryClass.Medium), ("absorbent", CarryClass.Medium), ("case", CarryClass.Medium), ("fuel", CarryClass.Medium), ("toolbox", CarryClass.Medium), ("rope", CarryClass.Medium),
            ("parts", CarryClass.Heavy), ("tire", CarryClass.Heavy), ("jack", CarryClass.Heavy), ("stretcher", CarryClass.Heavy),
        };
        foreach (var c in cases)
        {
            var def = catalog.Item(c.id);
            if (def == null || def.visualPrefab == null) { Fail("katalogda yok: " + c.id); continue; }
            var go = UnityEngine.Object.Instantiate(def.visualPrefab, startPos + Vector3.up * 20f, Quaternion.identity);
            var item = go.GetComponent<CarryableItem>();
            item.definitionId = c.id;
            item.uid = "carryverify_" + c.id;
            yield return null;
            yield return TestItem(c.id, c.want, () => bridge.Hold(item), () => { bridge.TakeHeld(); UnityEngine.Object.Destroy(go); }, () => go.GetComponentsInChildren<Renderer>());
            yield return null;
        }
        // Modül paketi: ağırlığı katalogdaki modülden; paket yoksa prefab'dan
        var package = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).FirstOrDefault(x => x.GetComponent<ManualModulePackage>() != null);
        var moduleDef = catalog.modules != null && catalog.modules.Length > 0 ? catalog.modules[0] : null;
        Line("  modül: paket sahnede " + (package != null ? package.name : "yok") + ", katalog " + (moduleDef != null ? moduleDef.id + " " + moduleDef.weightKg.ToString("F0", Inv) + " kg" : "yok"));
        if (package != null && moduleDef != null)
        {
            string savedDef = package.definitionId;
            package.definitionId = "module:" + moduleDef.id;
            Vector3 savedPos = package.transform.position; Quaternion savedRot = package.transform.rotation; var savedParent = package.transform.parent;
            yield return TestItem("modul", CarryClass.Heavy, () => bridge.Hold(package), () => { bridge.TakeHeld(); package.Drop(savedPos); package.transform.SetParent(savedParent, true); package.definitionId = savedDef; }, () => package.GetComponentsInChildren<Renderer>());
        }
        yield return GarageExtras(catalog, bridge);
    }

    private static CarryableItem SpawnItem(CompanyCatalog catalog, string id)
    {
        var def = catalog.Item(id);
        var go = UnityEngine.Object.Instantiate(def.visualPrefab, startPos + Vector3.up * 20f, Quaternion.identity);
        var item = go.GetComponent<CarryableItem>();
        item.definitionId = id;
        item.uid = "carryverify_" + id;
        return item;
    }

    private static IEnumerator GarageExtras(CompanyCatalog catalog, CompanyPlayerBridge bridge)
    {
        var combat = player.GetComponent<PlayerCombat>();
        var rules = player.Rules;

        // Bırakma: üç sınıf da dik (bulgu 4)
        foreach (var id in new[] { "grease", "toolbox", "parts", "tire" })
        {
            var item = SpawnItem(catalog, id);
            yield return null;
            player.RespawnAt(startPos, startRot);
            orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
            orbit.Yaw = player.transform.eulerAngles.y;
            yield return Seconds(0.3f);
            bridge.Hold(item);
            yield return Seconds(1.4f);
            bridge.DropHeld();
            yield return null;
            float tiltAtDrop = Vector3.Angle(item.transform.up, Vector3.up);
            bool dropped = bridge.HeldItem == null;
            yield return Seconds(1.2f);
            float tiltRest = Vector3.Angle(item.transform.up, Vector3.up);
            yield return CaptureSide("birak_" + id, player.transform.position + player.transform.forward * 0.6f + Vector3.up * 0.4f);
            // Lastik dik kutusundan devrilebilir (fizik); bırakma anı her eşyada dik, duruşta kutu eşyalar dik
            bool restOk = id == "tire" || tiltRest < 8f;
            Check("(bırakma " + id + ") eşya dik bırakılır", dropped && tiltAtDrop < 1f && restOk, "bırakma anı " + tiltAtDrop.ToString("F1", Inv) + "°, durunca " + tiltRest.ToString("F1", Inv) + "°");
            UnityEngine.Object.Destroy(item.gameObject);
            yield return null;
        }

        // Tackle ve yeni alış, garaj eşyasıyla
        var tb = SpawnItem(catalog, "bin");
        yield return null;
        yield return TackleScenario("kutu", () => bridge.Hold(tb), () => bridge.DropHeld());
        UnityEngine.Object.Destroy(tb.gameObject);
        yield return null;
        foreach (var id in new[] { "bin", "parts" })
        {
            var it = SpawnItem(catalog, id);
            yield return null;
            yield return JustPickedPunch(id, () => bridge.Hold(it), () => bridge.DropHeld());
            UnityEngine.Object.Destroy(it.gameObject);
            yield return null;
        }

        // Fırlatma: üç sınıf (madde 7)
        float baseSpeed = combat.settings.throwSpeed;
        var throwCases = new[] { ("grease", CarryClass.Light), ("toolbox", CarryClass.Medium), ("parts", CarryClass.Heavy) };
        foreach (var c in throwCases)
        {
            var item = SpawnItem(catalog, c.Item1);
            var body = item.GetComponent<Rigidbody>();
            yield return null;
            player.RespawnAt(startPos, startRot);
            orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
            orbit.Yaw = player.transform.eulerAngles.y;
            orbit.Pitch = 10f;
            yield return Seconds(0.3f);
            bridge.Hold(item);
            yield return Seconds(1.4f);
            combat.ResetCooldownsForTest();
            var jump = new JumpMeter();
            int throws0 = combat.PropThrows, hits0 = item.ImpactHits;
            float dur0 = item.durability;
            Vector3 startItem = item.transform.position;
            combat.SetThrowHeld(true);
            bool heavy = c.Item2 == CarryClass.Heavy;
            bool aimed = false, shoveState = false;
            float aimItemJump = 0f, aimLeftJump = 0f;
            if (!heavy)
            {
                float ta = Time.time;
                while (Time.time - ta < 0.45f) { jump.Sample(); aimed |= combat.Aiming; yield return null; }
                float aimJumpItem = jump.item, aimJumpLeft = jump.left;
                yield return CaptureSide("nisan_" + c.Item1, player.transform.position + Vector3.up * 0.9f);
                combat.SetThrowHeld(false);
                aimItemJump = aimJumpItem; aimLeftJump = aimJumpLeft;
            }
            bool heavyShot = false;
            float t0 = Time.time;
            while (combat.PropThrows == throws0 && Time.time - t0 < 2f)
            {
                jump.Sample();
                shoveState |= crew.InState(CrewAnimation.StatePushShove);
                if (heavy && !heavyShot && Time.time - t0 > 0.18f) { heavyShot = true; yield return CaptureSide("itis_orta_an_" + c.Item1, player.transform.position + player.transform.forward * 0.4f + Vector3.up * 0.7f); }
                yield return null;
            }
            bool thrown = combat.PropThrows == throws0 + 1;
            yield return null;
            float speed = body.linearVelocity.magnitude;
            Vector3 v = body.linearVelocity;
            yield return Seconds(0.25f);
            if (heavy) yield return CaptureSide("itis_sonrasi_" + c.Item1, player.transform.position + player.transform.forward * 0.7f + Vector3.up * 0.4f);
            float t1 = Time.time;
            yield return (Func<bool>)(() => !item.Thrown || Time.time - t1 > 4f);
            float travel = Flat(item.transform.position - startItem).magnitude;
            string detail = "hız " + speed.ToString("F2", Inv) + " m/sn, yol " + travel.ToString("F2", Inv) + " m, çarpma " + item.LastImpactSpeed.ToString("F1", Inv) + " m/sn, dayanıklılık " + dur0.ToString("F0", Inv) + "→" + item.durability.ToString("F0", Inv) + ", sıçrama (nişana girişte) eşya " + aimItemJump.ToString("F1", Inv) + " sol el " + aimLeftJump.ToString("F1", Inv) + ", (toplam) eşya " + jump.item.ToString("F1", Inv) + " sol el " + jump.left.ToString("F1", Inv) + " m/sn";
            if (c.Item2 == CarryClass.Light)
                Check("(fırlatma hafif) hız " + baseSpeed.ToString("F1", Inv), thrown && aimed && Mathf.Abs(speed - baseSpeed) < 0.7f && aimItemJump < 8f, detail);
            else if (c.Item2 == CarryClass.Medium)
                Check("(fırlatma orta) hız " + (baseSpeed * rules.mediumThrowFactor).ToString("F1", Inv) + ", menzil hafiften kısa", thrown && aimed && Mathf.Abs(speed - baseSpeed * rules.mediumThrowFactor) < 0.7f && rules.mediumThrowFactor < 1f && aimItemJump < 8f && aimLeftJump < 8f, detail + "; en uzak menzil " + (Mathf.Pow(baseSpeed * rules.mediumThrowFactor, 2f) / 9.81f).ToString("F1", Inv) + " m (hafif " + (baseSpeed * baseSpeed / 9.81f).ToString("F1", Inv) + " m)");
            else
                Check("(fırlatma ağır) nişansız iki elle itiş " + rules.heavyShoveSpeed.ToString("F1", Inv) + " m/sn, kısa düşer, hasar yok, sağ ele sıçramaz",
                    thrown && !aimed && shoveState && !combat.Aiming && speed < rules.heavyShoveSpeed + 1f && travel < 2.2f && travel > 0.2f && item.ImpactHits == hits0 && jump.item < 4f && jump.left < 6f, detail);
            UnityEngine.Object.Destroy(item.gameObject);
            yield return null;
        }
    }

    // İlk kare ışık / gölge ve kamera mesafesi oturmadan alınan görüntü işe yaramaz
    private static IEnumerator WarmUp()
    {
        yield return Seconds(1f);
        yield return CaptureFrame("_isinma", 18f);
        yield return Seconds(0.5f);
    }

    // =====================================================================================
    // Tek eşya sınaması
    // =====================================================================================
    private static IEnumerator TestItem(string label, CarryClass want, Action pick, Action release, Func<Renderer[]> renderers)
    {
        player.RespawnAt(startPos, startRot);
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        yield return Seconds(0.3f);
        pick();
        yield return null; yield return null;
        // PickUp klibi + geçiş
        yield return Seconds(1.3f);

        CarryClass cls = player.HeldClass;
        string wantState = cls == CarryClass.Light ? CrewAnimation.StateCarryCone : cls == CarryClass.Medium ? CrewAnimation.StateCarryTwoHand : CrewAnimation.StateCarryHeavy;
        bool state = crew.InState(wantState);
        var worst = new Worst();
        float t0 = Time.time;
        while (Time.time - t0 < 0.5f) { worst.Sample(); yield return null; }
        yield return CaptureFrame(label + "_tp_durus", 18f);

        // Yürüyüş: hız, tutuş, gövde
        float expectWalk = player.Rules.Speed(cls, false, player.moveSpeed, player.runSpeed);
        float expectRun = player.Rules.Speed(cls, true, player.moveSpeed, player.runSpeed);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.45f);
        Vector3 p0 = player.transform.position;
        t0 = Time.time;
        while (Time.time - t0 < 0.8f) { worst.Sample(); yield return null; }
        float walk = Flat(player.transform.position - p0).magnitude / (Time.time - t0);
        yield return CaptureFrame(label + "_tp_yuruyus", 18f);
        float ikWeight = ik.Weight;
        player.SetMoveInput(Vector2.zero);

        // Koşu
        player.Teleport(startPos, startRot);
        yield return Seconds(0.25f);
        player.SetRunInput(true);
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.45f);
        // Ortanca anlık hız: garajda yol kısa, sonda bir engele değmek ölçümü bozmaz
        var runSamples = new List<float>();
        t0 = Time.time;
        while (Time.time - t0 < 0.5f) { worst.Sample(); runSamples.Add(player.HorizontalVelocity.magnitude); yield return null; }
        runSamples.Sort();
        float run = runSamples.Count > 0 ? runSamples[runSamples.Count / 2] : 0f;
        bool runClipOk = cls == CarryClass.Heavy || player.animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name.EndsWith("_Run") && c.weight > 0.8f);
        player.SetRunInput(false);
        player.SetMoveInput(Vector2.zero);
        player.Teleport(startPos, startRot);
        yield return Seconds(0.35f);

        // Zıplama: ağırda yok, hafif / ortada kollar eşyada kalır
        float y0 = player.transform.position.y;
        int jumps0 = crew.CarriedJumps;
        player.RequestJump();
        float maxY = y0, airGrip = 0f;
        t0 = Time.time;
        bool loopInAir = true;
        while (Time.time - t0 < 0.5f)
        {
            maxY = Mathf.Max(maxY, player.transform.position.y);
            airGrip = Mathf.Max(airGrip, ik.GripErrorLeft, ik.GripErrorRight);
            loopInAir &= crew.InState(wantState);
            yield return null;
        }
        bool jumpOk = cls == CarryClass.Heavy ? maxY - y0 < 0.05f && crew.CarriedJumps == jumps0 : maxY - y0 > 0.4f && crew.CarriedJumps == jumps0 + 1 && loopInAir && airGrip < GripLimit;
        yield return Seconds(0.5f);

        // Yumruk (orta / ağır): eşya gövdeye göre yerinde, sol el üstünde, sağ el klibi oynar
        var combat = player.GetComponent<PlayerCombat>();
        if (combat != null && cls != CarryClass.Light)
        {
            Vector3 before = player.transform.InverseTransformPoint(ik.LastCenter);
            if (combat.Attack())
            {
                float drift = 0f, leftGrip = 0f;
                bool punchState = false;
                t0 = Time.time;
                while (Time.time - t0 < 0.3f)
                {
                    yield return null;
                    drift = Mathf.Max(drift, Vector3.Distance(before, player.transform.InverseTransformPoint(ik.LastCenter)));
                    leftGrip = Mathf.Max(leftGrip, ik.GripErrorLeft);
                    punchState |= crew.InState(PlayerCombat.StatePunchJab);
                }
                Check("(" + label + ") yumruk: eşya yerinde, sol el üstünde", punchState && drift < 0.05f && leftGrip < GripLimit, "durum " + punchState + ", kayma " + Cm(drift) + ", sol el " + Cm(leftGrip));
                yield return Seconds(0.5f);
            }
            else Line("  (" + label + ") yumruk atılamadı: " + combat.LastRejectReason);
        }

        // Eğilme: ağırda yok
        player.SetCrouchInput(true);
        yield return Seconds(0.15f);
        bool crouchOk = cls == CarryClass.Heavy ? !player.IsCrouching : player.IsCrouching;
        player.SetCrouchInput(false);
        yield return Seconds(0.3f);

        // FP: duruş (0° ve 45° aşağı) ve yürüyüş
        orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
        orbit.Yaw = player.transform.eulerAngles.y;
        orbit.Pitch = 0f;
        yield return Seconds(0.3f);
        var rs = renderers();
        float view0 = VisibleFraction(rs);
        bool center0 = CoversScreenCenter(rs);
        yield return CaptureMain(label + "_fp_durus");
        orbit.Pitch = 45f;
        yield return Seconds(0.2f);
        float view45 = VisibleFraction(rs);
        yield return CaptureMain(label + "_fp_asagi");
        var fpWorst = new Worst();
        t0 = Time.time;
        while (Time.time - t0 < 0.2f) { fpWorst.Sample(); yield return null; }
        orbit.Pitch = 25f;
        player.SetMoveInput(Vector2.up);
        yield return Seconds(0.45f);
        t0 = Time.time;
        while (Time.time - t0 < 0.4f) { fpWorst.Sample(); yield return null; }
        yield return CaptureMain(label + "_fp_yuruyus");
        player.SetMoveInput(Vector2.zero);
        orbit.Pitch = 15f;
        orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
        player.Teleport(startPos, startRot);
        yield return Seconds(0.2f);

        Vector3 size = ik.LastSize;
        table.Add(label + " | " + cls + " | " + V(size) + " | " + expectWalk.ToString("F1", Inv) + " | " + (cls == CarryClass.Heavy ? "yok" : expectRun.ToString("F1", Inv)));
        Check("(" + label + ") sınıf " + want + ", durum " + wantState,
            cls == want && state && ikWeight > 0.95f, "sınıf " + cls + ", durum " + state + ", IK " + ikWeight.ToString("F2", Inv) + ", boyut " + V(size) + (ik.SideGrip ? ", yan tutuş" : ", alt tutuş"));
        Check("(" + label + ") hız: yürüme " + expectWalk.ToString("F1", Inv) + ", koşu " + expectRun.ToString("F1", Inv),
            Mathf.Abs(walk - expectWalk) <= 0.12f && Mathf.Abs(run - expectRun) <= 0.12f && runClipOk,
            "yürüme " + walk.ToString("F2", Inv) + ", koşu " + run.ToString("F2", Inv) + ", koşu klibi " + runClipOk);
        Check("(" + label + ") avuç → eşya < " + Cm(GripLimit) + ", gövdeye < " + Cm(BodyLimit) + ", başa < " + Cm(HeadLimit),
            worst.grip < GripLimit && fpWorst.grip < GripLimit && worst.body < BodyLimit && fpWorst.body < BodyLimit && worst.head < HeadLimit && fpWorst.head < HeadLimit,
            "TP tutuş " + Cm(worst.grip) + ", gövde " + Cm(worst.body) + ", baş " + Cm(worst.head) + "; FP tutuş " + Cm(fpWorst.grip) + ", gövde " + Cm(fpWorst.body) + ", baş " + Cm(fpWorst.head) + "; erişime çekilen el " + worst.clamped + " kare");
        Check("(" + label + ") zıplama ve eğilme kuralı", jumpOk && crouchOk, "yükselme " + (maxY - y0).ToString("F2", Inv) + " m, döngüde kaldı " + loopInAir + ", havada tutuş " + Cm(airGrip) + ", eğilme " + crouchOk);
        Check("(" + label + ") FP: 45° aşağı bakınca eşya görünür; düz bakışta ekran merkezini kapatmaz",
            view45 > 0.2f && !center0, "görünür oran 0° %" + (view0 * 100f).ToString("F0", Inv) + ", 45° %" + (view45 * 100f).ToString("F0", Inv) + ", merkez " + center0);

        release();
        yield return null;
    }

    // Tutuş, gövde ve baş ölçümü (son LateUpdate pozundan)
    private class Worst
    {
        public float grip, body, head;
        public int clamped;
        public void Sample()
        {
            grip = Mathf.Max(grip, ik.GripErrorLeft, ik.GripErrorRight);
            body = Mathf.Max(body, ik.BodyOverlap);
            if (ik.ClampedHands > 0) clamped++;
            var h = player.animator.GetBoneTransform(HumanBodyBones.Head);
            if (h == null) return;
            // Baş küresi: Head kemiği + 20 cm yukarı (kask dahil); kutu içine girme derinliği
            Vector3 c = h.position + Vector3.up * 0.20f;
            Vector3 local = Quaternion.Inverse(ik.LastRotation) * (c - ik.LastCenter);
            Vector3 e = ik.LastSize * 0.5f;
            Vector3 q = new Vector3(Mathf.Clamp(local.x, -e.x, e.x), Mathf.Clamp(local.y, -e.y, e.y), Mathf.Clamp(local.z, -e.z, e.z));
            head = Mathf.Max(head, HeadRadius - Vector3.Distance(local, q));
        }
    }

    // Eşya sınır kutusunun içindeki 5×5×5 örnek noktadan ekrana düşen oran
    private static float VisibleFraction(Renderer[] rs)
    {
        var cam = Camera.main;
        int n = 0, seen = 0;
        foreach (var r in rs)
        {
            if (r == null || !r.enabled) continue;
            Bounds b = r.bounds;
            for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++)
            {
                Vector3 v = cam.WorldToViewportPoint(b.min + Vector3.Scale(b.size, new Vector3(x / 4f, y / 4f, z / 4f)));
                n++;
                if (v.z > cam.nearClipPlane && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f) seen++;
            }
        }
        return n > 0 ? (float)seen / n : 0f;
    }

    private static bool CoversScreenCenter(Renderer[] rs)
    {
        var cam = Camera.main;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        foreach (var r in rs)
            if (r != null && r.enabled && r.bounds.IntersectRay(ray)) return true;
        return false;
    }

    private static IEnumerator CaptureFrame(string name, float pitch)
    {
        orbit.Yaw = player.transform.eulerAngles.y + 150f; // önden-yandan: eller ve eşya görünsün
        orbit.Pitch = pitch;
        yield return Seconds(0.05f);
        yield return CaptureMain(name);
        orbit.Yaw = player.transform.eulerAngles.y;
    }

    // Ana kameranın gördüğünü dosyaya yazar; görüntü alınırken poz donar (Bolum1FirstPersonVerify deseni).
    private static IEnumerator CaptureMain(string name)
    {
        yield return null;
        var cam = Camera.main;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        yield return null;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }

    private static void Check(string label, bool ok, string detail)
    {
        allOk &= ok;
        Line((ok ? "[OK]   " : "[FAIL] ") + label + " — " + detail);
    }

    private static void Fail(string message)
    {
        allOk = false;
        Line("[FAIL] " + message);
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string Cm(float m) { return (m * 100f).ToString("F1", Inv) + " cm"; }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
