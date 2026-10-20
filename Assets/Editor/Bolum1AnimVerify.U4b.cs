using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// U4b (kullanıcı kararları 2026-10-20, brief u4b_hirsiz_dovus.txt) Play modu kontrolleri (U4-5 savunmadan sonra):
//  (U4b-0) giriş yolu teşhisi: PlayerInputReader bağlamaları (<Mouse>/leftButton, rightButton, <Keyboard>/q, leftShift) etkin;
//          sanal fare/klavye olaylarıyla sol tık → PlayerCombat.Attack (itme), sağ tık basılı → nişan, bırak → atış, Q → düdük,
//          Shift+W+sol tık → tackle. Yüz düzeltmesi: kamera hırsıza bakarken gövde başka yöne dönükken tık → gövde hırsıza döner, itme isabet.
//  (U4b-1) hırsız canı: başının üstünde can barı (dolu); itme 25 → 75, fırlatma isabeti 35 → 40, tackle 50 → 0 → pes: elindekini düşürür,
//          sersemlik bitince ön (yolcu) kapıya yürür, kapı açılır, biner, gizlenir; vurulamaz, hedef seçmez. Hepsi pes edince son hırsız
//          şoför kapısından biner ve kamyonet hemen kalkar. Taşıma pozu: eşya avuçların ortasında, gövdenin önünde, kafanın altında.
//  (U4b-2) kamyonet: hırsızlar arka + yan kapıdan iner (arka açıksa), bagaj 30 sn açık; kamyonete atılan eşya "kamyonette" (turuncu bölüm,
//          "%0 (+%N kamyonette)"); açık bagaja ≤ 1 m + yüz dönük → "E: Geri al (…)" + arka kapak vurgusu → eşya elde / büyük eşya yere;
//          kamyonette ≥ %50 iken geri alınırsa kaçışta görev bitmez; kamyonette ≥ %50 ile kaçış → o anda başarısız (bagaj açıkken bitmez).
public static partial class Bolum1AnimVerify
{
    private const string U4bMouseName = "U4bTestMouse", U4bKeyboardName = "U4bTestKeyboard";

    private static IEnumerator PhaseU4b(ThiefDirector dir)
    {
        TackleClipDiagnostics();
        ParkCarriersOffLane();
        Loot.ResetForTest();
        Health.SetHealthForTest(Health.Max);
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        // Eşyalar kapalı: hırsızlar inince ekipman kapmasın; testler tek tek açar
        var luggage = LootItem.All.Where(l => l.kind == LootKind.Luggage && l.CanPickUp).OrderByDescending(l => l.TotalValue).ThenBy(l => l.name).ToList();
        var hidden = HideLootExcept();
        var item1 = luggage[0]; var item2 = luggage[1];
        ThiefVan van = null;
        dir.Begin(TestThiefSettings(99999f, 99999f, 9999f)); // test kamyoneti giriş + can testleri boyunca kalkmasın
        yield return null;
        yield return SpawnParkedVan(dir, 3, v => van = v);
        var A = van.Thieves[0]; var B = van.Thieves[1]; var C = van.Thieves[2];
        foreach (var t in new[] { A, B, C }) t.ResetForTest();
        B.gameObject.SetActive(false); C.gameObject.SetActive(false);
        yield return PhaseU4bInput(dir, van, A, item1);
        yield return PhaseU4bHealth(dir, van, A, B, C, item1, item2);
        dir.Begin(TestThiefSettings(99999f, 99999f));
        yield return null;
        yield return PhaseU4bVan(dir, hidden);
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

    // Tackle_Dive klibi gerçekten dalış mı: Humanoid kök yüksekliği (RootT.y, normalize) ve gövde eğimi (RootQ) — bilgi satırı (sanat).
    private static void TackleClipDiagnostics()
    {
        var clip = Bolum1AnimBuilder.Clip("Tackle_Dive");
        if (clip == null) { Line("  U4b tanı: Tackle_Dive klibi yok"); return; }
        var ty = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"));
        var qx = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.x"));
        var qy = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.y"));
        var qz = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.z"));
        var qw = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.w"));
        if (ty == null) { Line("  U4b tanı: Tackle_Dive RootT.y eğrisi yok (Humanoid değil?)"); return; }
        float y0 = ty.Evaluate(0f), yMin = float.MaxValue, tMin = 0f, maxPitch = 0f, tPitch = 0f;
        for (float t = 0f; t <= clip.length + 1e-3f; t += 1f / 60f)
        {
            float y = ty.Evaluate(t);
            if (y < yMin) { yMin = y; tMin = t; }
            if (qx != null && qy != null && qz != null && qw != null)
            {
                var q = new Quaternion(qx.Evaluate(t), qy.Evaluate(t), qz.Evaluate(t), qw.Evaluate(t)).normalized;
                float pitch = Vector3.Angle(Vector3.up, q * Vector3.up);
                if (pitch > maxPitch) { maxPitch = pitch; tPitch = t; }
            }
        }
        Line("  U4b tanı: Tackle_Dive " + clip.length.ToString("F2", Inv) + " sn; kök yüksekliği (normalize) başlangıç " + y0.ToString("F2", Inv) + " → en düşük " + yMin.ToString("F2", Inv) + " (" + S(tMin) + ", oran " + (y0 > 1e-4f ? (yMin / y0).ToString("F2", Inv) : "-")
            + "); gövde dikeyden en çok " + maxPitch.ToString("F0", Inv) + "° eğik (" + S(tPitch) + "). Yüzüstü iniş için oran ≲ 0.4 ve eğim ≳ 60° beklenir; kayma kodu 0.2–0.7 sn.");
    }

    // ---- (U4b-0) giriş yolu ----
    private static IEnumerator PhaseU4bInput(ThiefDirector dir, ThiefVan van, Thief A, LootItem item)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var reader = Player.GetComponent<PlayerInputReader>();
        var combat = Combat;
        Vector3 vf = LaneDir(dir, 10f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 arena = LanePoint(dir, 10f, 0.5f);
        Player.Teleport(arena, Quaternion.LookRotation(vf));
        A.TeleportForTest(LanePoint(dir, 20f, 0.5f), Quaternion.LookRotation(-vf));
        A.Stun(60f, Vector3.zero, "test-bekleme"); // uzakta beklesin
        yield return null;
        // Sanal aygıtlar; editörde oyun görünümü odağı yokken fare/klavye olayları oyuna gitmez → test süresince hepsi oyuna
        var settings = InputSystem.settings;
        var savedBehavior = settings.editorInputBehaviorInPlayMode;
        var savedBackground = settings.backgroundBehavior;
        // Batchmode'da oyun odakta değil: olaylar yine oyuna gitsin (odak yok sayılır); test sonunda geri alınır
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        bool realMouse = Mouse.current != null, realKeyboard = Keyboard.current != null;
        var mouse = InputSystem.AddDevice<Mouse>(U4bMouseName);
        var keyboard = InputSystem.AddDevice<Keyboard>(U4bKeyboardName);
        bool savedReader = reader.enabled;
        reader.enabled = true;
        yield return null; yield return null;
        string bindings = reader.BindingSummary();
        bool bindOk = reader.AttackAction != null && reader.AttackAction.enabled && reader.AttackAction.bindings[0].effectivePath == PlayerInputReader.AttackPath && reader.AttackAction.controls.Count > 0
            && reader.ThrowAction.enabled && reader.ThrowAction.bindings[0].effectivePath == PlayerInputReader.ThrowPath
            && reader.WhistleAction.enabled && reader.WhistleAction.bindings[0].effectivePath == PlayerInputReader.WhistlePath
            && reader.RunAction.enabled && reader.RunAction.bindings[0].effectivePath == PlayerInputReader.RunPath;
        Note("bağlamalar", bindOk, bindings + "; gerçek fare " + realMouse + " klavye " + realKeyboard + "; editör davranışı " + savedBehavior);
        try
        {
            // Sol tık → itme (hırsız yok: boş itiş)
            int presses0 = reader.AttackPresses, pushes0 = combat.Pushes, acc0 = reader.AttackAccepted;
            yield return Until(() => Player.HasControl && !combat.Busy, 3f, "oyuncu hazır");
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null; yield return null;
            bool pushAnim = PlayerAnim.InState(CrewAnimation.StatePushShove);
            Note("sol tık → itme", reader.AttackPresses == presses0 + 1 && reader.AttackAccepted == acc0 + 1 && combat.Pushes == pushes0 + 1 && combat.LastAction == "push" && pushAnim,
                "basış " + (reader.AttackPresses - presses0) + ", kabul " + (reader.AttackAccepted - acc0) + ", itme " + (combat.Pushes - pushes0) + " (" + combat.LastAction + ", Push_Shove " + pushAnim + ", ret: " + combat.LastRejectReason + ")");
            yield return Until(() => !combat.Busy && Player.HasControl, 3f, "itme bitti");
            // Yüz düzeltmesi: hırsız oyuncunun SAĞINDA 1.1 m (gövde ileri bakıyor), kamera hırsıza bakıyor → tık → gövde döner, isabet
            A.ResetForTest();
            A.TeleportForTest(U3Ground(arena + vr * 1.1f, arena.y), Quaternion.LookRotation(-vr));
            A.Stun(5f, Vector3.zero, "test-bekleme");
            Player.Teleport(arena, Quaternion.LookRotation(vf));
            var cam = new GameObject("U4b_FaceCam").transform;
            cam.position = arena - vr * 2.5f + Vector3.up * 1.6f;
            cam.rotation = Quaternion.LookRotation(vr, Vector3.up);
            Player.cameraTransform = cam;
            yield return null;
            yield return Until(() => Time.time >= combat.PushReadyAt, 2f, "itme beklemesi");
            float angleBefore = Vector3.Angle(Flat(Player.transform.forward), vr);
            int hits0 = A.Hits; float hp0 = A.Health;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return Seconds(0.4f);
            float angleAfter = Vector3.Angle(Flat(Player.transform.forward), Flat(A.transform.position - Player.transform.position));
            Player.cameraTransform = null;
            UnityEngine.Object.Destroy(cam.gameObject);
            Note("yüz düzeltmesi (gövde yana dönükken tık → hırsıza döner, itme isabet 25)", angleBefore > 80f && angleAfter < 15f && A.Hits == hits0 + 1 && Mathf.Abs(hp0 - A.Health - combat.settings.pushDamage) < 0.01f && A.LastHitReason == "itme",
                "önce " + angleBefore.ToString("F0", Inv) + "° → sonra " + angleAfter.ToString("F0", Inv) + "° (" + combat.LastFacing + "), isabet " + (A.Hits - hits0) + ", can " + hp0.ToString("F0", Inv) + " → " + A.Health.ToString("F0", Inv));
            yield return Until(() => !combat.Busy && Player.HasControl, 3f, "itme bitti");
            A.ResetForTest();
            A.TeleportForTest(LanePoint(dir, 20f, 0.5f), Quaternion.LookRotation(-vf));
            A.Stun(60f, Vector3.zero, "test-bekleme");
            // Sağ tık basılı → nişan; bırak → atış
            item.gameObject.SetActive(true);
            item.Drop(U3Ground(arena + vf * 1.0f, arena.y), 0f);
            Player.Teleport(arena, Quaternion.LookRotation(vf)); // yüz düzeltmesi gövdeyi yana çevirmişti
            yield return null; yield return null;
            Interaction.Interact();
            yield return null;
            bool held = Player.HeldValuable == item;
            if (!held) Note("valiz alınamadı (tanı)", false, "ipucu \"" + Interaction.CurrentHint + "\", alınabilir " + item.CanPickUp + ", uzaklık " + M(Flat(item.Center - Player.transform.position).magnitude));
            int throws0 = combat.Throws, holdFrames0 = reader.ThrowHoldFrames;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right, true));
            yield return null; yield return null; yield return null;
            bool aiming = combat.Aiming && PlayerAnim.InState(CrewAnimation.StateThrowAim);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            float tThrow = Time.time;
            yield return (Func<bool>)(() => item.IsFlying || combat.Throws > throws0 || Time.time - tThrow > 1.5f);
            yield return null;
            Note("sağ tık basılı → nişan, bırak → atış", held && aiming && reader.ThrowHoldFrames > holdFrames0 && combat.Throws == throws0 + 1 && item.IsFlying,
                "elde " + held + ", nişan " + aiming + " (basılı kare " + (reader.ThrowHoldFrames - holdFrames0) + "), atış " + (combat.Throws - throws0) + ", uçuyor " + item.IsFlying);
            yield return Until(() => !item.IsFlying, 8f, "eşya indi");
            yield return Until(() => !combat.Busy && Player.HasControl, 3f, "atış bitti");
            // Q → düdük
            combat.ResetCooldownsForTest(); // U4-5'in 20 sn düdük beklemesi
            int wp0 = reader.WhistlePresses, w0 = combat.Whistles;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
            bool whistleAnim = PlayerAnim.InState(CrewAnimation.StateWhistle);
            Note("Q → düdük", reader.WhistlePresses == wp0 + 1 && combat.Whistles == w0 + 1 && whistleAnim, "basış " + (reader.WhistlePresses - wp0) + ", düdük " + (combat.Whistles - w0) + ", Whistle_Blow " + whistleAnim + (combat.Whistles == w0 ? ", ret: " + combat.LastRejectReason : ""));
            yield return Until(() => !combat.Busy && Player.HasControl, 3f, "düdük bitti");
            // Shift + W (koşu) + sol tık → tackle (geçici kamera şerit yönüne bakar: W = şerit boyunca; hırsız yok)
            Vector3 runDir = LaneDir(dir, 13f);
            Vector3 runFrom = LanePoint(dir, 12f, 0.3f);
            var runCam = new GameObject("U4b_InputRunCam").transform;
            runCam.position = runFrom - runDir * 3f + Vector3.up * 1.6f;
            runCam.rotation = Quaternion.LookRotation(runDir, Vector3.up);
            Player.cameraTransform = runCam;
            Player.Teleport(runFrom, Quaternion.LookRotation(runDir));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.W));
            yield return Seconds(0.6f);
            float runSpeedIn = Player.HorizontalVelocity.magnitude;
            bool running = Player.RunHeld && runSpeedIn > 1.5f;
            combat.ResetCooldownsForTest();
            int tackles0 = combat.Tackles;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
            Player.cameraTransform = null;
            UnityEngine.Object.Destroy(runCam.gameObject);
            bool diveAnim = PlayerAnim.InState(CrewAnimation.StateTackleDive);
            Note("Shift+W koşarken sol tık → tackle", running && combat.Tackles == tackles0 + 1 && combat.LastAction == "tackle" && diveAnim,
                "koşu " + running + " (" + runSpeedIn.ToString("F1", Inv) + " m/sn), tackle " + (combat.Tackles - tackles0) + ", Tackle_Dive " + diveAnim + (combat.Tackles == tackles0 ? ", ret: " + combat.LastRejectReason : ""));
            yield return Until(() => !combat.Busy && Player.HasControl, 4f, "tackle bitti");
        }
        finally
        {
            Player.SetMoveInput(Vector2.zero);
            Player.SetRunInput(false);
            reader.enabled = savedReader;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            settings.editorInputBehaviorInPlayMode = savedBehavior;
            settings.backgroundBehavior = savedBackground;
            Player.cameraTransform = null;
        }
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.Drop(U3Ground(Player.transform.position + Player.transform.forward * 0.7f, Player.transform.position.y), 0f); }
        Check("(U4b-0) giriş yolu teşhisi: PlayerInputReader kodla bağlar (sol tık / sağ tık / Q / Shift), sanal fare-klavye olayları PlayerCombat'a ulaşır (itme, nişan→atış, düdük, koşarken tackle); OYUNDAKİ sorun: gövde yürüyüş yönüne bakıyor, kamera hırsıza dönükken itme konisi boşa gidiyordu → saldırıda gövde kameraya / yakın hırsıza döner",
            ok, string.Join("; ", parts));
    }

    // ---- (U4b-1) can barı, hasar, pes, ön kapı, son hırsız şoför ----
    private static IEnumerator PhaseU4bHealth(ThiefDirector dir, ThiefVan van, Thief A, Thief B, Thief C, LootItem item, LootItem item2)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var combat = Combat;
        var s = combat.settings;
        Vector3 vf = LaneDir(dir, 10f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 arena = LanePoint(dir, 10f, 0.5f);
        BeforeCapture = FaceLabels;
        A.ResetForTest();
        A.Stun(30f, Vector3.zero, "test-bekleme"); // yerinde (eşya hedeflemesin)
        item.gameObject.SetActive(true);
        // Can barı dolu, görünür (etiket eşya yokken gizli)
        yield return null;
        var label = UnityEngine.Object.FindObjectsByType<ThiefLabel>(FindObjectsSortMode.None).FirstOrDefault(l => l.thief == A);
        bool barFull = label != null && label.BarVisible && !label.Visible && Mathf.Abs(label.HealthFill.fillAmount - 1f) < 0.01f && Mathf.Abs(A.Health - s.thiefHealth) < 0.01f;
        Note("can barı dolu (etiket gizli)", barFull, "bar " + (label != null && label.BarVisible) + " dolu " + (label != null ? label.HealthFill.fillAmount.ToString("F2", Inv) : "-") + ", can " + A.Health.ToString("F0", Inv));
        // Taşıma pozu: A valizi taşıyarak kamyonete koşar; eşya avuçların ortasında, önde, kafanın altında
        A.TeleportForTest(LanePoint(dir, 14f, 0.5f), Quaternion.LookRotation(-vf));
        Player.Teleport(LanePoint(dir, 4f, 0.5f), Quaternion.LookRotation(vf)); // oyuncu kadrajdan çıksın
        yield return null;
        A.ResetForTest(); // sersemlik biter, Seek
        A.GiveItemForTest(item); // → Carry
        float tCarry = Time.time;
        yield return (Func<bool>)(() => (ThiefIn(A, Thief.StateMoveCarry) && Time.time - tCarry >= 0.4f) || Time.time - tCarry > 1.6f);
        A.HandsMid(out Vector3 mid, out _);
        Vector3 c = item.Center;
        Vector3 local = A.transform.InverseTransformPoint(c);
        var head = A.animator.GetBoneTransform(HumanBodyBones.Head);
        float headY = head != null ? head.position.y : A.transform.position.y + 1.2f;
        Bounds b = item.WorldBounds;
        bool carryPose = Vector3.Distance(c, mid) <= 0.2f && local.z >= 0.24f && local.y >= 0.5f && local.y <= 1.15f && c.y <= headY && A.State == Thief.ThiefState.Carry && ThiefIn(A, Thief.StateMoveCarry);
        Vector3 tp = A.transform.position, tf = Flat(A.transform.forward).normalized;
        yield return Capture("u4b_kapma", tp + Vector3.Cross(Vector3.up, tf) * 2.4f + tf * 1.2f + Vector3.up * 1.5f, tp + tf * 0.3f + Vector3.up * 0.9f, 45f);
        Note("taşıma pozu (eşya avuçlarda, önde, kafanın altında)", carryPose, "avuçlara " + M(Vector3.Distance(c, mid)) + ", kök uzayında " + V(local) + ", merkez y " + c.y.ToString("F2", Inv) + " / üst " + b.max.y.ToString("F2", Inv) + " (boyut " + V(b.size) + ") / kafa " + headY.ToString("F2", Inv) + ", anim " + ThiefAnim(A));
        // İtme 25: A oyuncunun 1.1 m önünde, sersem (yerinde)
        A.Stun(8f, Vector3.zero, "test-bekleme"); // eşyayı düşürür
        yield return null;
        A.TeleportForTest(U3Ground(arena + vf * 1.1f, arena.y), Quaternion.LookRotation(-vf));
        Player.Teleport(arena, Quaternion.LookRotation(vf));
        yield return null;
        yield return Until(() => Time.time >= combat.PushReadyAt && !combat.Busy && Player.HasControl, 3f, "itme hazır");
        float hp0 = A.Health;
        bool pushed = combat.Attack();
        yield return Seconds(0.4f);
        float fillAfterPush = label != null ? label.HealthFill.fillAmount : -1f;
        Note("itme 25", pushed && Mathf.Abs(hp0 - A.Health - s.pushDamage) < 0.01f && Mathf.Abs(A.Health - 75f) < 0.01f && A.LastHitReason == "itme" && A.IsStunned && Mathf.Abs(fillAfterPush - 0.75f) < 0.02f,
            "can " + hp0.ToString("F0", Inv) + " → " + A.Health.ToString("F0", Inv) + ", bar " + fillAfterPush.ToString("F2", Inv) + ", sersem " + A.IsStunned + " (" + A.LastStunReason + ")");
        Vector3 ap = A.transform.position;
        yield return Capture("u4b_can_bari", ap - vf * 1.0f + vr * 2.4f + Vector3.up * 1.6f, ap + Vector3.up * 1.35f, 40f);
        yield return Until(() => !combat.Busy && Player.HasControl, 3f, "itme bitti");
        // Fırlatma 35: A 5 m önde, sersem; valiz elde, sağ tık → bırak
        A.Stun(6f, Vector3.zero, "test-hedef");
        A.TeleportForTest(LanePoint(dir, 15f, 0.5f), Quaternion.LookRotation(-vf));
        Vector3 ic = item.Center;
        Vector3 standI = U3StandAround(ic, 1.0f, out Vector3 idir);
        Player.Teleport(standI, Quaternion.LookRotation(-idir));
        yield return null; yield return null;
        Interaction.Interact();
        yield return null;
        bool held = Player.HeldValuable == item;
        Player.Teleport(LanePoint(dir, 10f, 0.5f), Quaternion.LookRotation(vf));
        yield return null;
        hp0 = A.Health;
        int hits0 = item.ThrowHits;
        combat.SetThrowHeld(true);
        yield return Seconds(0.3f);
        combat.SetThrowHeld(false);
        float tTh = Time.time;
        yield return (Func<bool>)(() => item.ThrowHits > hits0 || (Time.time - tTh > 0.8f && !item.IsFlying) || Time.time - tTh > 5f);
        yield return null;
        Note("fırlatma isabeti 35", held && item.ThrowHits == hits0 + 1 && Mathf.Abs(hp0 - A.Health - s.throwHitDamage) < 0.01f && Mathf.Abs(A.Health - 40f) < 0.01f && A.LastHitReason == "firlatma",
            "elde " + held + ", isabet +" + (item.ThrowHits - hits0) + ", can " + hp0.ToString("F0", Inv) + " → " + A.Health.ToString("F0", Inv) + " (" + A.LastHitReason + ", durum " + A.State + ", vurulabilir " + A.CanBeHit + ")");
        yield return Until(() => !item.IsFlying, 8f, "eşya indi");
        yield return Until(() => !combat.Busy && Player.HasControl, 3f, "atış bitti");
        // Tackle 50 → 0 → pes: A eşyalı (item2) 3.6 m önde; oyuncu koşar, tackle; eşya düşer, A sersem → pes
        Vector3 runDir = LaneDir(dir, 13f);
        Vector3 runFrom = LanePoint(dir, 12f, 0.3f);
        var runCam = new GameObject("U4b_RunCam").transform;
        runCam.position = runFrom - runDir * 3f + Vector3.up * 1.6f;
        runCam.rotation = Quaternion.LookRotation(runDir, Vector3.up);
        Player.cameraTransform = runCam;
        Player.Teleport(runFrom, Quaternion.LookRotation(runDir));
        A.TeleportForTest(LanePoint(dir, 15.6f, 0.3f), Quaternion.LookRotation(runDir));
        A.Stun(3f, Vector3.zero, "test-bekleme");
        item2.gameObject.SetActive(true);
        yield return null;
        A.GiveItemForTest(item2);
        yield return Until(() => Time.time >= combat.TackleReadyAt, 4f, "tackle hazır");
        Player.SetRunInput(true);
        Player.SetMoveInput(new Vector2(0f, 1f));
        yield return Seconds(0.5f);
        hp0 = A.Health;
        Vector3 before = Player.transform.position;
        bool tackleStarted = combat.Attack();
        Player.SetMoveInput(Vector2.zero);
        Player.SetRunInput(false);
        Player.cameraTransform = null;
        UnityEngine.Object.Destroy(runCam.gameObject);
        float tT = Time.time;
        bool diveAnim = false; float slidAt02 = -1f;
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StateTackleDive)) diveAnim = true; if (slidAt02 < 0f && Time.time - tT >= 0.2f) slidAt02 = Flat(Player.transform.position - before).magnitude; return Time.time - tT >= 0.5f; });
        var hips = PlayerAnim.animator.GetBoneTransform(HumanBodyBones.Hips);
        float hipsMid = hips != null ? hips.position.y - Player.transform.position.y : -1f;
        Vector3 pp = Player.transform.position; Vector3 side = Vector3.Cross(Vector3.up, runDir);
        yield return Capture("u4b_tackle", pp + side * 3.2f - runDir * 0.3f + Vector3.up * 1.3f, pp + runDir * 0.8f + Vector3.up * 0.6f, 50f);
        yield return (Func<bool>)(() => Time.time - tT >= 0.75f);
        float hipsGround = hips != null ? hips.position.y - Player.transform.position.y : -1f;
        float slid = Flat(Player.transform.position - before).magnitude;
        bool getUpSeen = false;
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StateTackleGetUp)) getUpSeen = true; return Time.time - tT >= 1.4f; });
        Note("tackle 50 → 0 (eşya düşer, kayma havada 0.2–0.7 sn)", tackleStarted && diveAnim && getUpSeen && slidAt02 <= 0.5f && slid >= 1.8f && slid <= 3.2f && Mathf.Abs(hp0 - A.Health - Mathf.Min(hp0, s.tackleDamage)) < 0.01f && A.Health <= 0.01f && A.Surrendered && A.LastHitReason == "tackle" && A.Carried == null && item2.State == LootItem.LootState.Dropped && item2.CanPickUp,
            "Tackle_Dive " + diveAnim + " → GetUp " + getUpSeen + ", kayma 0.2 sn'de " + M(Mathf.Max(0f, slidAt02)) + ", toplam " + M(slid) + ", kalça yüksekliği 0.5 sn " + hipsMid.ToString("F2", Inv) + " / 0.75 sn " + hipsGround.ToString("F2", Inv) + " m, can " + hp0.ToString("F0", Inv) + " → " + A.Health.ToString("F0", Inv) + ", pes " + A.Surrendered + ", eşya yerde " + (item2.State == LootItem.LootState.Dropped));
        // Pes akışı: sersemlik bitince Surrender → yolcu kapısı (B, C aktif değil ama sahnede: aktif sayılmaz → A son mu? B/C kapalı → A "son" sayılır)
        // B ve C'yi açıp uzakta sersem bekletiriz ki A yolcu kapısını kullansın.
        B.gameObject.SetActive(true); C.gameObject.SetActive(true);
        foreach (var t in new[] { B, C }) { t.ResetForTest(); t.TeleportForTest(LanePoint(dir, 18f + (t == B ? 0f : 1.5f), -0.8f), Quaternion.LookRotation(-vf)); t.Stun(90f, Vector3.zero, "test-bekleme"); }
        yield return Until(() => !combat.Busy && Player.HasControl, 4f, "tackle bitti");
        Player.Teleport(arena - vr * 2.5f, Quaternion.LookRotation(vf));
        bool hitRefused = false, targetStayedNull = true; float maxDoor = 0f; bool stateSeen = false;
        Time.timeScale = 2f;
        yield return Until(() => { if (A.State == Thief.ThiefState.Surrender) { stateSeen = true; } return stateSeen || A.IsGone; }, 12f, "pes durumu");
        if (A.State == Thief.ThiefState.Surrender) hitRefused = !A.Hit(10f, 1f, Vector3.zero, "test") && A.Health <= 0.01f && !A.CanBeHit;
        // Yolcu tarafı (banket) yürünemezse hırsız öbür ön kapıyı kullanır: beklenen taraf buna göre
        Vector3 doorPos = Vector3.zero, approachPt = Vector3.zero;
        approachPt = van.FrontDoorPoint(false, out doorPos, out bool aDriverSide);
        bool passengerReachable = !aDriverSide;
        float minDoorDist = 99f;
        yield return Until(() =>
        {
            if (A.Target != null) targetStayedNull = false;
            maxDoor = Mathf.Max(maxDoor, van.FrontDoorAmount(aDriverSide));
            approachPt = van.FrontDoorPoint(false, out doorPos, out aDriverSide);
            if (!A.IsGone) minDoorDist = Mathf.Min(minDoorDist, Flat(A.transform.position - doorPos).magnitude);
            return A.IsGone || (Flat(A.transform.position - approachPt).magnitude <= 2.0f && van.FrontDoorAmount(aDriverSide) > 0.3f);
        }, 40f, "ön kapıya yaklaştı");
        Time.timeScale = 1f;
        bool nearDoor = !A.IsGone;
        Vector3 vfwd = Flat(van.transform.forward).normalized, vright = Vector3.Cross(Vector3.up, vfwd);
        // Kamera ön-çaprazdan (kullanılan kapı tarafı), kapıya ve hırsıza bakar
        float doorSide = aDriverSide ? -1f : 1f;
        yield return Capture("u4b_pes", doorPos + vfwd * 3.2f + vright * doorSide * 2.6f + Vector3.up * 1.7f, (doorPos + A.transform.position) * 0.5f + Vector3.up * 0.4f, 50f);
        int boarded0 = van.Boarded;
        Time.timeScale = 2f;
        yield return Until(() => { maxDoor = Mathf.Max(maxDoor, van.FrontDoorAmount(aDriverSide)); if (A.Target != null) targetStayedNull = false; return A.IsGone; }, 20f, "bindi");
        yield return Seconds(1.5f);
        Time.timeScale = 1f;
        bool doorClosed = van.FrontDoorAmount(aDriverSide) <= 0.05f;
        Note("pes → ön kapıya yürür (yolcu tarafı; yürünemezse şoför tarafı), kapı açılır, biner, gizlenir; vurulamaz, hedef seçmez", stateSeen && hitRefused && targetStayedNull && nearDoor && minDoorDist <= 1.8f && maxDoor >= 0.5f && A.IsGone && !A.gameObject.activeInHierarchy && A.UsedDriverDoor == aDriverSide && van.Surrendered == 1 && van.Boarded == boarded0 + 1 && doorClosed && !van.Departing,
            "Surrender görüldü " + stateSeen + ", darbe reddedildi " + hitRefused + ", hedef hep boş " + targetStayedNull + ", kapıya yaklaştı " + nearDoor + " (kapıya en yakın " + M(minDoorDist) + ", yaklaşma noktası kapıya " + M(Flat(approachPt - doorPos).magnitude) + ", yolcu tarafı yürünebilir " + passengerReachable + "), kapı en çok " + maxDoor.ToString("F2", Inv) + " → kapandı " + doorClosed + ", gizli " + !A.gameObject.activeInHierarchy + ", şoför kapısı " + A.UsedDriverDoor + ", pes sayısı " + van.Surrendered + ", kamyonet kalkıyor " + van.Departing);
        // Hepsi pes edince: B (yolcu) sonra C (son → şoför kapısı), kamyonet hemen kalkar
        B.ResetForTest(); B.TeleportForTest(LanePoint(dir, 12f, -0.8f), Quaternion.LookRotation(-vf));
        bool bHit = B.Hit(s.thiefHealth, 0.5f, Vector3.zero, "test");
        Time.timeScale = 3f;
        yield return Until(() => B.IsGone, 40f, "B bindi");
        bool bPassenger = B.UsedDriverDoor == !passengerReachable && !van.Departing;
        C.ResetForTest(); C.TeleportForTest(LanePoint(dir, 12f, -0.8f), Quaternion.LookRotation(-vf));
        bool cHit = C.Hit(s.thiefHealth, 0.5f, Vector3.zero, "test");
        float maxDriverDoor = 0f;
        yield return Until(() => { maxDriverDoor = Mathf.Max(maxDriverDoor, van.FrontDoorAmount(true)); return C.IsGone; }, 40f, "C bindi");
        float parkedAtC = van.ParkedTime;
        yield return Until(() => van.Departing, 5f, "kamyonet kalkış kararı");
        Time.timeScale = 1f;
        Note("hepsi pes: son hırsız şoför kapısından, kamyonet hemen kalkar", bHit && cHit && bPassenger && C.UsedDriverDoor && van.LastBoardWasDriverDoor && maxDriverDoor >= 0.5f && van.Surrendered == 3 && van.Departing && parkedAtC < van.settings.vanWaitSeconds - 2f,
            "B yolcu/öbür kapı " + bPassenger + " (şoför kapısı " + B.UsedDriverDoor + ", yolcu tarafı yürünebilir " + passengerReachable + "), C şoför " + C.UsedDriverDoor + " (kapı " + maxDriverDoor.ToString("F2", Inv) + "), pes " + van.Surrendered + "/3, kalkış " + van.Departing + " park " + S(parkedAtC) + " < bekleme " + van.settings.vanWaitSeconds.ToString(Inv));
        Check("(U4b-1) hırsız canı " + s.thiefHealth.ToString(Inv) + ": başının üstünde can barı; itme " + s.pushDamage.ToString(Inv) + " / fırlatma " + s.throwHitDamage.ToString(Inv) + " / tackle " + s.tackleDamage.ToString(Inv) + "; can 0 → pes: elindekini düşürür, sersemlik bitince ön (yolcu) kapıya yürür, kapı açılır, biner, gizlenir, vurulamaz/hedef seçmez; hepsi pes edince son hırsız şoför kapısından biner, kamyonet hemen kalkar; taşıma pozu avuçlarda; tackle kayması havada",
            ok, string.Join("; ", parts));
        BeforeCapture = null;
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        item.ResetToStart(); item2.ResetToStart();
        item.gameObject.SetActive(false); item2.gameObject.SetActive(false);
    }

    // ---- (U4b-2) kamyonet: iniş kapıları, bagaj 30 sn açık, kamyonette / geri alma / %50 kaçışta ----
    private static IEnumerator PhaseU4bVan(ThiefDirector dir, List<LootItem> hidden)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var hud = Hud;
        Loot.ResetForTest();
        foreach (var l in hidden) if (l != null) l.gameObject.SetActive(false); // ResetForTest açmış olabilir
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        ParkCarriersOffLane();
        int total = Loot.TotalValue;
        float limit = Loot.values != null ? Loot.values.failFraction : 0.5f;
        var luggage = hidden.Where(l => l != null && l.kind == LootKind.Luggage).OrderByDescending(l => l.TotalValue).ThenBy(l => l.name).ToList();
        ThiefVan van = null;
        yield return SpawnParkedVan(dir, 2, v => van = v);
        var T0 = van.Thieves[0]; var T1 = van.Thieves[1];
        // İniş kapıları
        string exits = string.Join(" | ", van.ExitLog);
        float truckGap = Vector3.Distance(van.TrunkCenter, Truck.GetComponentsInChildren<Collider>().Where(c => !(c is MeshCollider mc && !mc.convex)).Select(c => c.ClosestPoint(van.TrunkCenter)).DefaultIfEmpty(Truck.position).OrderBy(p => Vector3.Distance(p, van.TrunkCenter)).First());
        bool exitsOk = van.ExitLog.Count == 2 && (van.RearExitOk ? van.ExitLog[0].StartsWith("arka") && van.ExitLog[1] == "yan" : van.ExitLog.All(e => e == "yan"));
        Note("iniş: arka (açıksa) + yan kapı sırayla", exitsOk, "arka çıkış " + van.RearExitOk + " (" + van.RearExitKind + "), kayıt " + exits + ", arka tampon → çekici " + M(truckGap) + ", iniş noktaları arka " + V(van.RearPoint) + " yan " + V(van.SidePoint));
        BeforeCapture = FaceLabels;
        Vector3 camPos = VanCaptureCamera(van, out _);
        Vector3 vfwd = Flat(van.transform.forward).normalized, vright = Vector3.Cross(Vector3.up, vfwd);
        Vector3 bumper = van.TrunkCenter;
        yield return Capture("u4b_bagaj_acik", camPos, bumper - vfwd * 0.5f + Vector3.up * 0.3f, 60f);
        T0.gameObject.SetActive(false); T1.gameObject.SetActive(false);
        // Elindekini uzağa bırak (bagajın önüne düşen eşya "E: Valizi al" ipucunu öne alırdı)
        int farK = 0;
        void DropHeldFar() { var h = Player.HeldValuable; if (h == null) return; Player.HeldValuable = null; h.Drop(LanePoint(dir, 6f + (farK++) * 0.8f, -1.2f), 0f); }
        // Kamyonette: iki valiz (en değerli iki) atılmış sayılır
        var item1 = luggage[0]; var item2 = luggage[1];
        int floats0 = hud.FloatingSpawned;
        van.AddLoot(item1); van.AddLoot(item2);
        yield return null; yield return null;
        int v1 = item1.TotalValue, v2 = item2.TotalValue;
        float frac12 = (float)(v1 + v2) / total;
        string pct = hud.StolenPercent.text;
        bool inVanOk = item1.IsInVan && item2.IsInVan && !item1.gameObject.activeSelf && !item1.IsStolen && Loot.InVanValue == v1 + v2 && Loot.InVanCount == 2 && Loot.StolenValue == 0 && van.Loot.Count == 2 && van.LootCount == 2
            && pct == MissionHud.StolenPercentText(0f, frac12) && pct.Contains(MissionHud.InVanPercentSuffix) && hud.InVanFill.enabled && Mathf.Abs(hud.InVanFill.rectTransform.anchorMax.x - frac12) < 0.01f && Mathf.Abs(hud.InVanFill.rectTransform.anchorMin.x) < 0.01f
            && hud.FloatingSpawned == floats0 + 2 && !Mission.IsFinished;
        Note("kamyonette (turuncu bölüm, \"" + pct + "\")", inVanOk, "kamyonette " + Loot.InVanValue + " (" + v1 + "+" + v2 + "), çalınan " + Loot.StolenValue + ", bölüm " + hud.InVanFill.rectTransform.anchorMin.x.ToString("F3", Inv) + "–" + hud.InVanFill.rectTransform.anchorMax.x.ToString("F3", Inv) + " / " + frac12.ToString("F3", Inv) + ", uçan yazı +" + (hud.FloatingSpawned - floats0));
        // Bagaj 30 sn açık (süre kontrolü sonda); oyuncu bagaja: aday duruşlar (tam arka, köşeler)
        float rear = van.Follower.rearOverhang, halfW = van.Vehicle.bodySize.x * 0.5f;
        Vector3 bc = van.transform.position - vfwd * rear;
        var stands = new List<Vector3> { bc - vfwd * 0.8f, bc - vfwd * 0.45f + vright * (halfW + 0.35f), bc - vfwd * 0.45f - vright * (halfW + 0.35f), bc - vfwd * 0.9f + vright * (halfW + 0.2f), bc - vfwd * 0.9f - vright * (halfW + 0.2f) };
        string standInfo = "";
        bool hintOk = false;
        foreach (var cand in stands)
        {
            Vector3 p = U3Ground(cand, van.transform.position.y);
            Player.Teleport(p, Quaternion.LookRotation(Flat(van.TrunkCenter - p).normalized));
            yield return null; yield return null;
            standInfo = V(p) + " tampona " + M(van.TrunkDistance(p)) + " ipucu \"" + Interaction.CurrentHint + "\"";
            if (Interaction.CurrentHint.StartsWith(PlayerInteraction.HintRetrievePrefix)) { hintOk = true; break; }
        }
        bool hintMostValuable = Interaction.CurrentHint == PlayerInteraction.HintRetrieve(item1) && Interaction.VanTarget == van && Interaction.LootTarget == item1;
        bool highlightOk = Interaction.HighlightTarget == van.rearDoors && Highlight.Current == van.rearDoors && Highlight.CopyCount >= 2;
        Note("açık bagaja ≤ 1 m + yüz dönük → \"E: Geri al (en değerli)\" + arka kapak vurgusu", hintOk && hintMostValuable && highlightOk, standInfo + ", vurgu " + HlName() + " (" + Highlight.CopyCount + " kopya)");
        // Sırt dönükken ipucu yok
        Vector3 standP = Player.transform.position;
        Player.Teleport(standP, Quaternion.LookRotation(-Flat(van.TrunkCenter - standP).normalized));
        yield return null; yield return null;
        bool backNoHint = !Interaction.CurrentHint.StartsWith(PlayerInteraction.HintRetrievePrefix);
        Player.Teleport(standP, Quaternion.LookRotation(Flat(van.TrunkCenter - standP).normalized));
        yield return null; yield return null;
        // Kamera yol tarafından (banket ve çekici kabini değil): bagaj + oyuncu + HUD
        float roadSide = van.ShoulderLateral >= 0f ? -1f : 1f;
        yield return CaptureWithHud("u4b_geri_al", van.TrunkCenter + vright * roadSide * 3.6f - vfwd * 1.2f + Vector3.up * 1.9f, (standP + van.TrunkCenter) * 0.5f + Vector3.up * 0.3f, 55f);
        // Geri al → elde; çubuk düşer
        floats0 = hud.FloatingSpawned;
        Interaction.Interact();
        yield return null; yield return null;
        string pct2 = hud.StolenPercent.text;
        bool retrieved = Player.HeldValuable == item1 && item1.State == LootItem.LootState.Held && item1.gameObject.activeSelf && Loot.InVanValue == v2 && Loot.InVanCount == 1 && Loot.RetrievedCount == 1 && van.Retrieved == 1 && van.Loot.Count == 1
            && pct2 == MissionHud.StolenPercentText(0f, (float)v2 / total) && hud.FloatingSpawned == floats0 + 1 && Interaction.CurrentHint == PlayerInteraction.HintDropLoot;
        Note("E → eşya elde, çubuk düşer (\"" + pct2 + "\")", retrieved && backNoHint, "elde " + (Player.HeldValuable == item1) + ", kamyonette " + Loot.InVanValue + " (" + v2 + "), geri alınan " + van.Retrieved + ", ipucu \"" + Interaction.CurrentHint + "\", sırt dönükken ipucu yok " + backNoHint);
        // Bırak → tekrar normal eşya (alınabilir, çalınabilir); uzağa konur (bagaj ipucunun önüne geçmesin)
        DropHeldFar();
        yield return null; yield return null;
        bool normalAgain = item1.State == LootItem.LootState.Dropped && item1.CanPickUp && item1.gameObject.activeSelf && T0.IsStealable(item1);
        // Sedye kamyonette → geri al → yere (bagajın arkası)
        // Sedye LootItem'ı gizli listede (HideLootExcept kapattı; GameObject.Find kapalı nesneyi bulmaz)
        var s0Item = hidden.First(l => l != null && l.GetComponent<StretcherController>() != null);
        var s0 = s0Item.GetComponent<StretcherController>();
        van.AddLoot(s0Item);
        yield return null;
        bool stretcherHidden = s0Item.IsInVan && !s0.gameObject.activeSelf;
        // item2 (400) sedyeden (300) değerli: önce item2 gelir → al, bırak; sonra sedye
        Player.Teleport(standP, Quaternion.LookRotation(Flat(van.TrunkCenter - standP).normalized));
        yield return null; yield return null;
        bool item2First = Interaction.CurrentHint == PlayerInteraction.HintRetrieve(item2);
        Interaction.Interact(); yield return null; yield return null; // item2 elde
        bool item2Held = Player.HeldValuable == item2;
        DropHeldFar(); yield return null; yield return null; // bırak (uzağa)
        bool stretcherHint = Interaction.CurrentHint == PlayerInteraction.HintRetrieve(s0Item);
        Interaction.Interact(); yield return null; yield return null; // sedye yere
        float sDist = van.TrunkDistance(s0.transform.position);
        bool stretcherOut = s0.gameObject.activeSelf && StretcherController.All.Contains(s0) && s0Item.State == LootItem.LootState.Dropped && Player.HeldValuable == null && sDist >= 0.6f && sDist <= 4.5f && Loot.InVanValue == 0 && van.Loot.Count == 0;
        Note("büyük eşya (sedye) bagajdan yere", stretcherHidden && item2First && item2Held && stretcherHint && stretcherOut && normalAgain, "gizliydi " + stretcherHidden + ", önce valiz " + item2First + "/" + item2Held + ", sedye ipucu " + stretcherHint + ", sedye yerde " + stretcherOut + " (tampona " + M(sDist) + "), bırakılan valiz yeniden normal " + normalAgain);
        // ≥ %50 kamyonette iken geri alınırsa kaçışta görev bitmez
        var pool = luggage.Skip(2).Where(l => l != null && l.IsStolen == false && l.gameObject.activeSelf == false && l.State == LootItem.LootState.AtStart).ToList();
        pool.AddRange(hidden.Where(l => l != null && l.kind == LootKind.PatientItem && !l.gameObject.activeSelf));
        int added = 0;
        foreach (var l in pool) { if (Loot.InVanFraction >= limit) break; van.AddLoot(l); added++; }
        // Yetmezse elde/yerdeki iki valizi de at
        if (Loot.InVanFraction < limit) { item1.gameObject.SetActive(true); van.AddLoot(item1); added++; }
        if (Loot.InVanFraction < limit) { item2.gameObject.SetActive(true); van.AddLoot(item2); added++; }
        yield return null;
        float fracHigh = Loot.InVanFraction;
        bool highNoFail = fracHigh >= limit && !Mission.IsFinished && van.TrunkOpen;
        // Geri al (al + bırak) ta ki eşik altına insin
        int retrievesB = 0;
        Player.Teleport(standP, Quaternion.LookRotation(Flat(van.TrunkCenter - standP).normalized));
        yield return null; yield return null;
        while (Loot.InVanFraction >= limit && retrievesB < 10 && Interaction.CurrentHint.StartsWith(PlayerInteraction.HintRetrievePrefix))
        {
            Interaction.Interact(); yield return null; yield return null; // al
            DropHeldFar(); yield return null; // uzağa bırak
            retrievesB++;
            Player.Teleport(standP, Quaternion.LookRotation(Flat(van.TrunkCenter - standP).normalized));
            yield return null; yield return null;
        }
        float fracLow = Loot.InVanFraction;
        int inVanRemain = Loot.InVanValue;
        // Bagaj 30 sn: parktan sonra açık kalır, kalkışta kapanır → kalanlar çalınır, görev bitmez
        bool openAt28 = false; float closeAt = -1f;
        Time.timeScale = 5f;
        yield return Until(() => { if (van.ParkedTime >= 28f && van.ParkedTime < 29.5f && van.TrunkOpen) openAt28 = true; if (van.WaitingForGap && closeAt < 0f) closeAt = van.ParkedTime; return van.LootFinalized || van.Phase == ThiefVan.VanPhase.Departing; }, 60f, "bagaj kapandı");
        yield return null;
        Time.timeScale = 1f;
        bool noRetrieveNow = !van.TrunkOpen && !van.CanRetrieve(standP, out _);
        bool lowNoFail = van.LootFinalized && Loot.InVanValue == 0 && Loot.StolenValue == inVanRemain && !Mission.IsFinished && fracLow < limit;
        Note("kamyonette ≥ %50 iken geri alınınca kaçışta başarısız değil; bagaj 30 sn açık, kalkışta kapanır", highNoFail && lowNoFail && openAt28 && closeAt >= 29.5f && closeAt <= 31f && noRetrieveNow,
            "atılan +" + added + " → %" + (fracHigh * 100f).ToString("F1", Inv) + " (görev sürüyor " + highNoFail + "), " + retrievesB + " geri alma → %" + (fracLow * 100f).ToString("F1", Inv) + ", 28. sn açık " + openAt28 + ", kapanış " + S(closeAt) + ", kesinleşen çalınan " + Loot.StolenValue + " = %" + (Loot.StolenFraction * 100f).ToString("F1", Inv) + ", görev bitti " + Mission.IsFinished + ", bagaj erişilmez " + noRetrieveNow);
        // ≥ %50 kamyonette ile kaçış → o anda başarısız
        Loot.ResetForTest();
        foreach (var l in hidden) if (l != null) l.gameObject.SetActive(false);
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        dir.Begin(TestThiefSettings(99999f, 99999f));
        yield return null;
        ThiefVan van2 = null;
        yield return SpawnParkedVan(dir, 1, v => van2 = v);
        van2.Thieves[0].gameObject.SetActive(false);
        int added2 = 0;
        foreach (var l in luggage) { if (Loot.InVanFraction >= limit) break; van2.AddLoot(l); added2++; }
        foreach (var l in hidden.Where(x => x != null && x.kind == LootKind.PatientItem)) { if (Loot.InVanFraction >= limit) break; van2.AddLoot(l); added2++; }
        yield return Seconds(1f);
        bool openNoFail = Loot.InVanFraction >= limit && !Mission.IsFinished && van2.TrunkOpen;
        float fracVan2 = Loot.InVanFraction;
        Time.timeScale = 5f;
        bool finishedWhileOpen = false;
        yield return Until(() => { if (Mission.IsFinished && van2.TrunkOpen) finishedWhileOpen = true; return Mission.IsFinished || van2.Phase == ThiefVan.VanPhase.Departing; }, 60f, "kaçışta başarısız");
        yield return null;
        Time.timeScale = 1f;
        string result = hud.ResultText.text;
        bool failOk = openNoFail && !finishedWhileOpen && Mission.IsFinished && !Mission.Succeeded && Mission.ResultMessage == LootRegistry.FailText && van2.LootFinalized && Loot.StolenFraction >= limit && Loot.InVanValue == 0 && result.StartsWith(LootRegistry.FailText);
        Note("kamyonette ≥ %50 ile kaçış → o anda başarısız (bagaj açıkken değil)", failOk, "atılan " + added2 + " → %" + (fracVan2 * 100f).ToString("F1", Inv) + " (açıkken görev sürüyor " + openNoFail + "), kesinleşince çalınan %" + (Loot.StolenFraction * 100f).ToString("F1", Inv) + ", görev bitti " + Mission.IsFinished + " (açıkken " + finishedWhileOpen + "), HUD \"" + result.Replace("\n", " | ") + "\"");
        Check("(U4b-2) kamyonet: hırsızlar arka (açıksa) + yan kapıdan iner; bagaj " + Bolum1ThiefBuilder.Settings.vanWaitSeconds.ToString(Inv) + " sn açık; atılan eşya \"kamyonette\" (turuncu çubuk bölümü, \"%0 (+%N kamyonette)\"); açık bagaja ≤ " + Bolum1ThiefBuilder.Settings.retrieveRange.ToString(Inv) + " m + yüz dönük \"E: Geri al (…)\" + arka kapak vurgusu → eşya elde / sedye yere, çubuk düşer; %50 kontrolü yalnız kaçışta: geri alınınca bitmez, kamyonette ≥ %50 ile kaçınca biter",
            ok, string.Join("; ", parts));
        BeforeCapture = null;
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
    }
}
