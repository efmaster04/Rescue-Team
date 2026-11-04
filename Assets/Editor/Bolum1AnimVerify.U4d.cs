using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// 2026-11-05 (kullanıcı isteği: "sol tık yumruk, sağ tık itme, Ctrl eğilme"; elde fırlatılabilir eşya varken sağ tık nişan/fırlatma)
// Play modu kontrolleri (U4c'den sonra):
//  (U4d-1) yumruk: önünde 0.9 m'deki eşyalı hırsız → Punch_Jab, punchDamage hasar, punchStunSeconds sersem, eşya düşer, küçük geri
//          tepme (itmenin 1.5 m'sinden az); eylem bitince hemen ikinci tık "yumruk beklemede", bekleme dolunca kabul.
//  (U4d-2) eğilme (sanal klavye sol Ctrl): kapsül 1.0 m, kalça alçak (Crouch katmanı), üçüncü şahıs pivotu yumuşakça ~0.4 m alçalır;
//          eğik yürüyüş 1.2 m/sn (Shift'le de), zıplama yok; alçak kirişin (alt kenarı zeminden 1.15 m) altından ayakta geçilemez, eğik
//          geçilir; altında Ctrl bırakılınca kalkamaz; eğikken sol tık yumruk (koşu tuşuyla da tackle değil), yumrukta bacaklar eğik
//          kalır; kirişten çıkınca kendiliğinden kalkar; birinci şahısta göz alçalır.
//  (U4d-3) eğikken elde valizle sağ tık basılı → Throw_Aim, bırak → fırlatma (eğik kalır).
// Sedye taşırken eğilme yok: U4-5 "sedye taşırken saldırı ve eğilme yok".
public static partial class Bolum1AnimVerify
{
    private const string U4dMouseName = "U4dTestMouse", U4dKeyboardName = "U4dTestKeyboard";

    private static IEnumerator PhaseU4d(ThiefDirector dir)
    {
        ParkCarriersOffLane();
        Loot.ResetForTest();
        Health.SetHealthForTest(Health.Max);
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        var luggage = LootItem.All.Where(l => l.kind == LootKind.Luggage && l.CanPickUp).OrderByDescending(l => l.TotalValue).ThenBy(l => l.name).ToList();
        var hidden = HideLootExcept();
        dir.Begin(TestThiefSettings(99999f, 99999f, 9999f)); // kamyonet kalkmasın
        yield return null;
        ThiefVan van = null;
        yield return SpawnParkedVan(dir, 1, v => van = v);
        var A = van.Thieves[0];
        A.ResetForTest();
        yield return PhaseU4dPunch(dir, A, luggage[0]);
        yield return PhaseU4dCrouch(dir, A, luggage[0]);
        // Toparla
        Player.SetCrouchInput(false);
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        Player.cameraTransform = null;
        RestoreLoot(hidden);
        Loot.ResetForTest();
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        Health.SetHealthForTest(Health.Max);
        dir.Begin(TestThiefSettings(99999f, 99999f));
        BeforeCapture = null;
        Time.timeScale = 1f;
        yield return null;
    }

    // ---- (U4d-1) yumruk ----
    private static IEnumerator PhaseU4dPunch(ThiefDirector dir, Thief A, LootItem item)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var combat = Combat;
        Vector3 vf = LaneDir(dir, 10f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 arena = LanePoint(dir, 10f, 0.5f);
        item.gameObject.SetActive(true);
        Player.Teleport(arena, Quaternion.LookRotation(vf));
        A.ResetForTest();
        A.TeleportForTest(U3Ground(arena + vf * 0.9f, arena.y), Quaternion.LookRotation(-vf));
        A.Stun(0.6f, Vector3.zero, "test-bekleme"); // yerinde dursun; eşyayı sersemken alır
        yield return null;
        A.GiveItemForTest(item);
        yield return null;
        bool carrying = A.Carried == item;
        yield return Until(() => Time.time >= combat.PunchReadyAt && !combat.Busy && Player.HasControl, 3f, "yumruk hazır");
        float hp0 = A.Health;
        int hits0 = combat.PunchHits, punches0 = combat.Punches, pushes0 = combat.Pushes;
        Vector3 a0 = A.transform.position;
        bool started = combat.Attack();
        bool punchAnim = false;
        float t0 = Time.time, hitAt = -1f, stunLeft = -1f;
        yield return (Func<bool>)(() =>
        {
            if (PlayerAnim.InState(PlayerCombat.StatePunchJab)) punchAnim = true;
            if (hitAt < 0f && combat.PunchHits > hits0) { hitAt = Time.time; stunLeft = A.StunRemaining; }
            return Time.time - t0 >= 0.3f;
        });
        Vector3 pp = Player.transform.position;
        yield return Capture("u4d_yumruk", pp + vr * 2.4f + vf * 0.5f + Vector3.up * 1.5f, pp + vf * 0.6f + Vector3.up * 1.0f, 50f);
        bool dropped = A.Carried == null && item.State == LootItem.LootState.Dropped;
        // Eylem bitince (Busy kapanır) bekleme hâlâ sürüyor → ikinci tık reddedilir
        yield return Until(() => !combat.Busy, 1f, "yumruk eylemi bitti");
        bool secondRejected = !combat.Attack();
        string rejectReason = combat.LastRejectReason;
        float busyAt = Time.time - t0;
        yield return Seconds(0.25f);
        float knock = Flat(A.transform.position - a0).magnitude;
        yield return Until(() => !A.IsStunned, 3f, "yumruk sersemliği bitti");
        float stunDur = Time.time - hitAt;
        Note("yumruk isabeti", started && punchAnim && combat.Punches == punches0 + 1 && combat.PunchHits == hits0 + 1 && combat.Pushes == pushes0 && carrying && dropped
            && Mathf.Abs(hp0 - A.Health - combat.punchDamage) < 0.01f && A.LastHitReason == "yumruk" && hitAt - t0 > 0.12f && hitAt - t0 < 0.32f,
            "Punch_Jab " + punchAnim + ", isabet " + (combat.PunchHits - hits0) + " @" + S(hitAt - t0) + ", can " + hp0.ToString("F0", Inv) + " → " + A.Health.ToString("F0", Inv) + " (" + A.LastHitReason + "), eşya taşıyordu " + carrying + " → düştü " + dropped);
        Note("sersemletme ~" + combat.punchStunSeconds.ToString("F1", Inv) + " sn + küçük geri tepme", stunLeft > combat.punchStunSeconds - 0.1f && Mathf.Abs(stunDur - combat.punchStunSeconds) < 0.25f && knock >= 0.2f && knock <= 0.9f,
            "isabette kalan " + S(stunLeft) + ", süre " + S(stunDur) + ", geri tepme " + M(knock) + " (itme " + combat.settings.pushKnockback.ToString(Inv) + " m)");
        yield return Until(() => Time.time >= combat.PunchReadyAt && !combat.Busy, 2f, "yumruk beklemesi");
        A.Stun(30f, Vector3.zero, "test-bekleme");
        A.TeleportForTest(LanePoint(dir, 20f, 0.5f), Quaternion.LookRotation(-vf)); // uzakta beklesin (eşya kapmasın)
        int punches1 = combat.Punches;
        bool thirdOk = combat.Attack();
        Note("kısa bekleme " + combat.punchCooldown.ToString(Inv) + " sn", secondRejected && rejectReason == "yumruk beklemede" && thirdOk && combat.Punches == punches1 + 1,
            "eylem " + S(busyAt) + "'de bitti, hemen ikinci tık reddedildi " + secondRejected + " (" + rejectReason + "), bekleme sonrası kabul " + thirdOk);
        yield return Until(() => !combat.Busy && Player.HasControl, 2f, "yumruk bitti");
        item.Drop(U3Ground(arena - vr * 1.5f, arena.y), 0f);
        Check("(U4d-1) sol tık yumruk (Punch_Jab, hit_frame 7): " + combat.punchRange.ToString(Inv) + " m / " + combat.punchAngle.ToString(Inv) + "° içindeki hırsız " + combat.punchDamage.ToString(Inv) + " hasar, "
            + combat.punchStunSeconds.ToString(Inv) + " sn sersem, elindekini düşürür, " + combat.punchKnockback.ToString(Inv) + " m geri teper; bekleme " + combat.punchCooldown.ToString(Inv) + " sn", ok, string.Join("; ", parts));
    }

    // ---- (U4d-2, U4d-3) eğilme ----
    private static IEnumerator PhaseU4dCrouch(ThiefDirector dir, Thief A, LootItem item)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var combat = Combat;
        var reader = Player.GetComponent<PlayerInputReader>();
        var cc = Player.GetComponent<CharacterController>();
        var orbit = UnityEngine.Object.FindAnyObjectByType<OrbitCamera>();
        var hips = PlayerAnim.animator.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 vf = LaneDir(dir, 10f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 arena = LanePoint(dir, 10f, 0.5f);
        float Along() { return Vector3.Dot(Flat(Player.transform.position - arena), vf); }
        float HipsH() { return hips != null ? hips.position.y - Player.transform.position.y : -1f; }
        A.Stun(60f, Vector3.zero, "test-bekleme");
        // Alçak kiriş: arena + 2.5 m ileride, 3 m geniş, 1.2 m derin, alt kenarı zeminden 1.15 m (eğik kapsül 1.02 m, ayakta 1.52 m)
        const float beamAt = 2.5f, beamDepth = 1.2f, beamBottom = 1.15f, beamThick = 0.3f;
        Vector3 beamGround = U3Ground(arena + vf * beamAt, arena.y);
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "U4d_LowBeam";
        beam.transform.SetPositionAndRotation(new Vector3(beamGround.x, beamGround.y - 0.02f + beamBottom + beamThick * 0.5f, beamGround.z), Quaternion.LookRotation(vf));
        beam.transform.localScale = new Vector3(3f, beamThick, beamDepth);
        Physics.SyncTransforms();
        var moveCam = new GameObject("U4d_MoveCam").transform; // W = şerit boyunca (yol virajlı)
        moveCam.position = arena - vf * 3f + Vector3.up * 1.6f;
        moveCam.rotation = Quaternion.LookRotation(vf + Vector3.down * 0.25f, Vector3.up);
        Player.cameraTransform = moveCam;
        // Kamera açısı sonradan geri yüklenir: sonraki fazlar (b1s sedye yürüyüşü) kameranın baktığı yöne yürür
        float savedYaw = orbit != null ? orbit.Yaw : 0f, savedPitch = orbit != null ? orbit.Pitch : 0f;
        if (orbit != null) { orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true); orbit.Yaw = Quaternion.LookRotation(vf).eulerAngles.y; orbit.Pitch = 18f; }
        // Sanal aygıtlar (U4b deseni): batchmode'da olaylar oyuna gitsin
        var settings = InputSystem.settings;
        var savedBehavior = settings.editorInputBehaviorInPlayMode;
        var savedBackground = settings.backgroundBehavior;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var mouse = InputSystem.AddDevice<Mouse>(U4dMouseName);
        var keyboard = InputSystem.AddDevice<Keyboard>(U4dKeyboardName);
        bool savedReader = reader.enabled;
        reader.enabled = true;
        int savedFps = Application.targetFrameRate;
        Application.targetFrameRate = 60; // kare başı ölçümler (kamera adımı, engelli kare) oyun hızında
        try
        {
            Player.Teleport(arena, Quaternion.LookRotation(vf));
            yield return null; yield return null;
            yield return Seconds(0.3f);
            float standCc = cc.height, standHips = HipsH(), standPivot = orbit != null ? orbit.CurrentPivotHeight : -1f;
            // Ayakta kirişe yürü: geçilemez
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return Seconds(2.0f);
            float standReach = Along();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Note("ayakta kirişin altından geçilmez", standReach < beamAt - beamDepth * 0.5f && !Player.IsCrouching && Mathf.Abs(standCc - Player.StandingHeight) < 0.01f,
                "kirişin ön kenarı " + M(beamAt - beamDepth * 0.5f) + ", varılan " + M(standReach) + ", kapsül " + M(standCc));
            // Ctrl basılı: eğil (kapsül, kalça, kamera pivotu yumuşak)
            Player.Teleport(arena, Quaternion.LookRotation(vf));
            yield return null; yield return null;
            int ctrl0 = reader.CrouchHoldFrames;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            float tc = Time.time, lastPivot = standPivot, maxStep = 0f;
            yield return (Func<bool>)(() =>
            {
                if (orbit != null) { float p = orbit.CurrentPivotHeight; maxStep = Mathf.Max(maxStep, Mathf.Abs(p - lastPivot)); lastPivot = p; }
                return Time.time - tc >= 0.45f;
            });
            float crouchHips = HipsH(), crouchPivot = orbit != null ? orbit.CurrentPivotHeight : -1f;
            float camDrop = orbit != null ? standPivot - crouchPivot : -1f;
            Vector3 cp = Player.transform.position;
            yield return Capture("u4d_egilme", cp + vr * 2.6f - vf * 0.8f + Vector3.up * 1.4f, cp + vf * 1.2f + Vector3.up * 0.8f, 55f);
            Note("Ctrl → eğik (kapsül, kalça, Crouch katmanı)", Player.IsCrouching && reader.CrouchHoldFrames > ctrl0 && Mathf.Abs(cc.height - Player.crouchHeight) < 0.01f && Player.CrouchAmount > 0.99f
                && crouchHips > 0f && crouchHips < standHips * 0.85f,
                "basılı kare " + (reader.CrouchHoldFrames - ctrl0) + ", kapsül " + M(standCc) + " → " + M(cc.height) + ", kalça " + M(standHips) + " → " + M(crouchHips) + " (%" + (standHips > 0f ? (100f * crouchHips / standHips).ToString("F0", Inv) : "-") + "), ağırlık " + Player.CrouchAmount.ToString("F2", Inv));
            Note("kamera pivotu yumuşakça alçalır", orbit != null && camDrop > orbit.crouchPivotDrop - 0.02f && maxStep < 0.12f,
                orbit != null ? "pivot " + M(standPivot) + " → " + M(crouchPivot) + " (−" + M(camDrop) + "), kare başı en büyük adım " + M(maxStep) : "OrbitCamera yok");
            // Eğik yürüyüş Shift basılıyken de 1.2 m/sn; yolda zıplama denemesi; kirişin ortasına kadar
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.LeftShift, Key.W));
            yield return Seconds(0.5f);
            float sumV = 0f; int nV = 0; float y0 = Player.transform.position.y, maxRise = 0f;
            float tw = Time.time;
            bool jumped = false;
            yield return (Func<bool>)(() =>
            {
                sumV += Player.HorizontalVelocity.magnitude; nV++;
                if (!jumped && Time.time - tw >= 0.1f) { jumped = true; InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.LeftShift, Key.W, Key.Space)); }
                else if (jumped) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.LeftShift, Key.W));
                maxRise = Mathf.Max(maxRise, Player.transform.position.y - y0);
                return Time.time - tw >= 0.5f;
            });
            float crouchSpeed = nV > 0 ? sumV / nV : 0f;
            yield return Until(() => Along() >= beamAt, 5f, "kirişin altına eğik yürüyüş");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            float underAlong = Along();
            Note("eğik yürüyüş " + Player.crouchSpeed.ToString(Inv) + " m/sn (Shift'le de koşu yok), zıplama yok, kirişin altına girer", Mathf.Abs(crouchSpeed - Player.crouchSpeed) < 0.12f && maxRise < 0.06f && underAlong >= beamAt && Player.IsCrouching,
                "hız " + crouchSpeed.ToString("F2", Inv) + " m/sn (Shift basılı), zıplamada yükselme " + M(maxRise) + ", kirişin altında " + M(underAlong));
            // Kirişin altında Ctrl bırak: kalkılamaz
            int blocked0 = Player.StandBlockedFrames;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Seconds(0.6f);
            Note("kirişin altında Ctrl bırakılınca kalkamaz", Player.IsCrouching && !Player.CrouchHeld && Player.StandBlockedFrames > blocked0 + 10 && Player.LastStandBlocker == beam.name && Mathf.Abs(cc.height - Player.crouchHeight) < 0.01f && !Player.CanStandUp(),
                "eğik " + Player.IsCrouching + ", engelli kare +" + (Player.StandBlockedFrames - blocked0) + " (" + Player.LastStandBlocker + "), kapsül " + M(cc.height));
            // Eğikken sol tık (Shift basılı) → yumruk, tackle değil; yumrukta bacaklar eğik
            combat.ResetCooldownsForTest();
            int tackles0 = combat.Tackles, punches0 = combat.Punches;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return Seconds(0.15f);
            bool punchAnim = PlayerAnim.InState(PlayerCombat.StatePunchJab);
            float punchHips = HipsH();
            Note("eğikken sol tık → yumruk (tackle yok), bacaklar eğik kalır", combat.Punches == punches0 + 1 && combat.Tackles == tackles0 && combat.LastAction == "punch" && punchAnim && Player.IsCrouching && punchHips < standHips * 0.85f,
                "yumruk +" + (combat.Punches - punches0) + ", tackle +" + (combat.Tackles - tackles0) + ", Punch_Jab " + punchAnim + ", kalça " + M(punchHips) + (combat.Punches == punches0 ? ", ret: " + combat.LastRejectReason : ""));
            yield return Until(() => !combat.Busy && Player.HasControl, 2f, "yumruk bitti");
            // Kirişten çık (Ctrl bırakık): açığa çıkınca kendiliğinden kalkar
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return Until(() => !Player.IsCrouching, 5f, "kirişten çıkınca kalkış");
            float standAlong = Along();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Seconds(0.4f);
            float farEdge = beamAt + beamDepth * 0.5f;
            Note("kirişten çıkınca kalkar", standAlong > farEdge + cc.radius * 0.5f && Mathf.Abs(cc.height - Player.StandingHeight) < 0.01f && Player.CrouchAmount < 0.01f && (orbit == null || Mathf.Abs(orbit.CurrentPivotHeight - standPivot) < 0.01f),
                "kalkış " + M(standAlong) + "'de (kirişin arka kenarı " + M(farEdge) + "), kapsül " + M(cc.height) + ", pivot " + (orbit != null ? M(orbit.CurrentPivotHeight) : "-"));
            // Birinci şahıs: göz (baş kemiği) eğilince alçalır
            if (orbit != null)
            {
                orbit.SetMode(OrbitCamera.ViewMode.FirstPerson, true);
                yield return Seconds(0.3f);
                float eyeStand = orbit.transform.position.y - Player.transform.position.y;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                yield return Seconds(0.45f);
                float eyeCrouch = orbit.transform.position.y - Player.transform.position.y;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return Until(() => !Player.IsCrouching, 2f, "FP kalkış");
                yield return Seconds(0.3f);
                orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true);
                // Karakter küçük (ayakta göz ~1.1 m, kalça 0.45 m): göz kalça oranında (~%20) alçalmalı
                Note("birinci şahısta göz alçalır (≥ %15)", eyeStand - eyeCrouch > eyeStand * 0.15f, "göz " + M(eyeStand) + " → " + M(eyeCrouch) + " (−" + M(eyeStand - eyeCrouch) + ")");
            }
            // (U4d-3) eğikken elde valizle sağ tık basılı → nişan, bırak → fırlat
            item.Drop(U3Ground(Player.transform.position + vf * 1.0f, Player.transform.position.y), 0f);
            yield return null; yield return null;
            Interaction.Interact();
            yield return null;
            bool held = Player.HeldValuable == item;
            // Nişan kamerası oyuncunun arkasında, şerit boyunca hafif aşağı (hedef ~15 m ileride)
            moveCam.position = Player.transform.position - vf * 2f + Vector3.up * 1.6f;
            moveCam.rotation = Quaternion.LookRotation(vf + Vector3.down * 0.1f, Vector3.up);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return Seconds(0.4f);
            bool crouchedWithItem = Player.IsCrouching && held;
            int throws0 = combat.Throws, pushes0 = combat.Pushes;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right, true));
            yield return Seconds(0.35f);
            bool aiming = combat.Aiming && PlayerAnim.InState(CrewAnimation.StateThrowAim) && Player.IsCrouching;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            float tThrow = Time.time;
            yield return (Func<bool>)(() => item.IsFlying || combat.Throws > throws0 || Time.time - tThrow > 1.5f);
            yield return null;
            bool flying = item.IsFlying && Player.HeldValuable == null && combat.Throws == throws0 + 1 && combat.Pushes == pushes0;
            Note("eğikken elde valizle sağ tık → nişan, bırak → fırlatma (itme yok)", crouchedWithItem && aiming && flying && Player.IsCrouching,
                "valizle eğik " + crouchedWithItem + ", Throw_Aim " + aiming + ", fırlatıldı " + flying + " (itme +" + (combat.Pushes - pushes0) + "), eğik " + Player.IsCrouching);
            yield return Until(() => !item.IsFlying, 8f, "eşya indi");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Until(() => !combat.Busy && !Player.IsCrouching, 3f, "fırlatma bitti, kalkış");
        }
        finally
        {
            Player.SetMoveInput(Vector2.zero);
            Player.SetRunInput(false);
            Player.SetCrouchInput(false);
            reader.enabled = savedReader;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            settings.editorInputBehaviorInPlayMode = savedBehavior;
            settings.backgroundBehavior = savedBackground;
            Player.cameraTransform = null;
            if (orbit != null) { orbit.SetMode(OrbitCamera.ViewMode.ThirdPerson, true); orbit.Yaw = savedYaw; orbit.Pitch = savedPitch; }
            Application.targetFrameRate = savedFps;
            UnityEngine.Object.Destroy(moveCam.gameObject);
            UnityEngine.Object.Destroy(beam);
        }
        yield return null;
        Check("(U4d-2/3) sol Ctrl eğilme: kapsül " + Player.StandingHeight.ToString("F1", Inv) + " → " + Player.crouchHeight.ToString("F1", Inv) + " m, Crouch_Idle/Walk katmanı, " + Player.crouchSpeed.ToString(Inv)
            + " m/sn, koşu / zıplama / tackle yok, alçak kirişin altından geçer ve altında kalkamaz, kamera pivotu −" + (orbit != null ? orbit.crouchPivotDrop.ToString(Inv) : "-") + " m (yumuşak), FP göz alçalır; eğikken yumruk ve fırlatma serbest",
            ok, string.Join("; ", parts));
    }
}
