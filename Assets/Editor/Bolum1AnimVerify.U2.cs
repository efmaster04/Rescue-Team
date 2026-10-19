using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// U2 (üçüncü test turu kararı 3, 2026-10-19) Play modu kontrolleri — halat zinciri GERÇEK akışla, taze sahnede, PhaseRescue'dan önce:
//  (U2-1) başlangıç: kazık boş (SM_RopeAnchor_Bare), halat çizilmiyor, bağlanılamıyor; kangal kapalı L1 dolabında
//  (U2-2) dolap: uzakta / sırtı dönük ipucu yok; ≤ 1 m yüz dönük "E: Dolabı aç" + kapak vurgusu; 0.4 sn'de açılır;
//         açıkken "E: Halatı al" + kangal vurgusu; Cabinet_TakeCoil, grab karesinden sonra kangal ellerde; "E: Dolabı kapat"
//  (U2-3) taşıma: Carry_Coil_Walk, Shift'le 2.4 m/sn (koşu yok), zıplama yok, kangal ellerde ≤ 3 cm; bırak (yerde, vurgu yok) / yerden al (PickUp)
//  (U2-4) kazık: uzak / sırtı dönük bağlama ipucu yok; ≤ 1 m yüz dönük "E: Halatı kazığa bağla" + kazık vurgusu; Rope_TieStake;
//         kangal yerde aralığında kazık yanında dik; sonunda bağlı (Tied), kangal ellerde, çizgi kazıktan kangala
//  (U2-5) atış: yanlış yönde ipucu yok; kenara dönük "E: Halatı at" + kazık vurgusu; Rope_Throw; kangal uçar (çizgi uzar);
//         0.8–1.2 sn'de kayaya iner, halat hazır (çizgi kazıktan dibe); bağlanma + iniş çalışır
public static partial class Bolum1AnimVerify
{
    private static TowTruckLocker Locker { get { return TowTruckLocker.All.FirstOrDefault(); } }

    private static IEnumerator PhaseU2()
    {
        var rope = Rope;
        var coil = rope != null ? rope.coil : null;
        var locker = Locker;
        var bot = Bot;
        bool savedAssist = bot.assistEnabled;
        bot.assistEnabled = false;
        Transform savedCamera = Player.cameraTransform;
        Player.cameraTransform = null;
        if (coil == null || locker == null)
        {
            Check("(U2-0) sahnede halat kangalı ve çekici dolabı var", false, "kangal " + (coil != null) + ", dolap " + (locker != null));
            if (rope != null) rope.ForceReady();
            bot.assistEnabled = savedAssist;
            Player.cameraTransform = savedCamera;
            yield break;
        }

        yield return PhaseU2Start(rope, coil, locker);
        yield return PhaseU2Locker(rope, coil, locker);
        yield return PhaseU2Carry(rope, coil, locker);
        yield return PhaseU2Tie(rope, coil);
        yield return PhaseU2Throw(rope, coil);

        if (!rope.IsReady) { rope.ForceReady(); Line("  (U2) gerçek akış tamamlanamadı; sonraki fazlar için hızlı kurulum"); }
        if (Player.HeldCoil != null) Player.HeldCoil = null;
        bot.assistEnabled = savedAssist;
        Player.cameraTransform = savedCamera;
        yield return null;
    }

    private static Vector3 U2Ground(Vector3 p, float refY)
    {
        if (GroundProbe.Find(new Vector3(p.x, refY + 1.5f, p.z), 5f, out RaycastHit g)) p.y = g.point.y + 0.02f;
        return p;
    }

    private static string U2Hl() { return Highlight.Current != null ? Highlight.Current.name : "yok"; }

    // ---- (U2-1) başlangıç ----
    private static IEnumerator PhaseU2Start(RopeAnchor rope, RopeCoil coil, TowTruckLocker locker)
    {
        Player.Teleport(rope.topStand.position, rope.topStand.rotation);
        yield return null; yield return null;
        string hint = Interaction.CurrentHint;
        bool attach = rope.Attach(Player);
        if (attach) rope.Detach(Player, rope.topStand.position, rope.topStand.rotation);
        bool lineOff = rope.line != null && !rope.line.enabled;
        var stakeMesh = rope.GetComponentInChildren<MeshFilter>();
        int tris = stakeMesh != null && stakeMesh.sharedMesh != null ? stakeMesh.sharedMesh.triangles.Length / 3 : -1;
        bool bare = stakeMesh != null && stakeMesh.sharedMesh != null && tris < 200 && !stakeMesh.GetComponentsInChildren<MeshFilter>().Any(m => m.sharedMesh != null && m.sharedMesh.name.Contains("Coil"));
        // Kangal kapak sınırlarının içinde (yatayda), kapak kapalı
        Bounds db = locker.DoorBounds;
        Vector3 cc = coil.GripMid;
        bool inLocker = coil.InLocker && Mathf.Abs(locker.Angle) < 0.5f && !locker.IsOpen
            && Vector3.Dot(cc - db.center, locker.outward) < 0f && Vector3.Distance(Flat(cc), Flat(db.center)) < 0.6f && cc.y > db.min.y && cc.y < db.max.y;
        Check("(U2-1) başlangıç: kazık boş (Bare), halat çizilmiyor ve bağlanılamıyor; kangal kapalı L1 dolabında",
            rope.State == RopeAnchor.RopeState.Stowed && hint != PlayerInteraction.HintAttachRope && !attach && lineOff && bare && inLocker,
            "durum " + rope.State + ", topStand ipucu \"" + hint + "\", Attach " + attach + ", çizgi kapalı " + lineOff + ", kazık mesh " + (stakeMesh != null ? stakeMesh.sharedMesh.name : "-") + " " + tris + " üçgen"
            + "; kangal " + coil.State + " " + V(cc) + ", kapak merkezi " + V(db.center) + " açı " + locker.Angle.ToString("F1", Inv) + "°, dolapta " + inLocker);
    }

    // ---- (U2-2) dolap ----
    private static IEnumerator PhaseU2Locker(RopeAnchor rope, RopeCoil coil, TowTruckLocker locker)
    {
        Vector3 n = locker.outward;
        Vector3 side = Vector3.Cross(Vector3.up, n);
        Vector3 dc = locker.DoorCenter;
        float refY = Player.transform.position.y;
        Vector3 near = U2Ground(dc + n * 0.75f, dc.y);
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }

        // 2.0 m uzakta yüz dönük → yok; 0.75 m sırtı dönük → yok
        Player.Teleport(U2Ground(dc + n * 2.0f, dc.y), Quaternion.LookRotation(-n));
        yield return null; yield return null;
        Note("2.0 m uzakta", Interaction.CurrentHint != PlayerInteraction.HintOpenLocker && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(near, Quaternion.LookRotation(n));
        yield return null; yield return null;
        // Sırtı dönükken dolap ipucu yok (deckteki koni menzilde olabilir: "E: Koniyi al" mevcut kural, kapak vurgulanmaz)
        Note("sırtı dönük", Interaction.CurrentHint != PlayerInteraction.HintOpenLocker && Highlight.Current != locker.door, "ipucu \"" + Interaction.CurrentHint + "\"");
        // Yüz dönük → "E: Dolabı aç" + kapak vurgusu; E → ~0.4 sn'de açılır
        Player.Teleport(near, Quaternion.LookRotation(-n));
        yield return null; yield return null;
        float dist = Flat(new Bounds(dc, locker.DoorBounds.size).ClosestPoint(Player.transform.position) - Player.transform.position).magnitude;
        Note("yüz dönük (" + M(dist) + ")", Interaction.CurrentHint == PlayerInteraction.HintOpenLocker && Highlight.Current == locker.door && Highlight.CopyCount > 0,
            "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl());
        Interaction.Interact();
        float t0 = Time.time;
        yield return (Func<bool>)(() => locker.IsOpen || Time.time - t0 > 2f);
        float openTime = Time.time - t0;
        Note("açıldı", locker.IsOpen && Mathf.Abs(Mathf.Abs(locker.Angle) - Mathf.Abs(locker.openAngle)) < 0.5f && openTime < 0.7f,
            "açı " + locker.Angle.ToString("F0", Inv) + "° (" + S(openTime) + ")");
        // Kapak dışa açıldı mı: kapak merkezi kapalı merkeze göre dış yönde
        Bounds openB = locker.DoorBounds;
        float outwardShift = Vector3.Dot(openB.center - dc, n);
        Note("kapak dışa", outwardShift > 0.15f, "merkez dışa " + M(outwardShift));
        // Açıkken kangal hedefi
        yield return null; yield return null;
        Note("açıkken kangal", Interaction.CurrentHint == PlayerInteraction.HintTakeCoil && Highlight.Current == coil.transform && Highlight.CopyCount > 0,
            "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl() + " (" + Highlight.CopyCount + " kopya)");
        // Kamera kapağın menteşesiz (arka) yanından: açık kapak ve oyuncu görüşü kapatmasın
        yield return Capture("u2_dolap", dc + n * 1.9f - side * 2.2f + Vector3.up * 1.1f, dc + Vector3.up * 0.05f, 50f);
        // E → Cabinet_TakeCoil; grab karesinden önce kangal dolapta, sonra ellerde
        Interaction.Interact();
        yield return null; yield return null;
        bool takeAnim = PlayerAnim.InState(CrewAnimation.StateTakeCoil);
        bool stillInLocker = coil.InLocker && Player.HeldCoil == null;
        yield return Seconds(Interaction.takeCoilGrabTime + 0.15f);
        bool held = Player.HeldCoil == coil && coil.IsHeld;
        yield return Seconds(Interaction.takeCoilSeconds);
        bool carryLoop = PlayerAnim.InState(CrewAnimation.StateCarryCoil);
        Note("Cabinet_TakeCoil", takeAnim && stillInLocker && held && carryLoop,
            "klip " + takeAnim + ", grab'den önce dolapta " + stillInLocker + ", sonra elde " + held + ", CarryCoil döngüsü " + carryLoop);
        // Taşırken bırakma ipucu hedefsiz; bırak → yerde, PutDown; sonra "E: Dolabı kapat" (kangal dışarıda)
        Player.Teleport(U2Ground(dc + n * 1.6f, dc.y), Quaternion.LookRotation(side));
        yield return null; yield return null;
        Note("taşırken bırak (hedefsiz)", Interaction.CurrentHint == PlayerInteraction.HintDropCoil && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl());
        Interaction.Interact();
        yield return null; yield return null;
        bool putDownAnim = PlayerAnim.InState(CrewAnimation.StatePutDown);
        float pen = GroundProbe.Find(coil.GripMid + Vector3.up * 0.5f, 2f, out RaycastHit cg) ? cg.point.y - (coil.GripMid.y - coil.halfDepth) : 99f;
        Note("yere bırakıldı", coil.OnGround && Player.HeldCoil == null && Mathf.Abs(pen) < 0.03f && putDownAnim, "kangal " + coil.State + ", zemine " + Cm(pen) + ", PutDown " + putDownAnim);
        yield return Seconds(1.0f);
        Player.Teleport(near, Quaternion.LookRotation(-n));
        yield return null; yield return null;
        Note("kapat ipucu", Interaction.CurrentHint == PlayerInteraction.HintCloseLocker && Highlight.Current == locker.door, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl());
        Interaction.Interact();
        yield return Seconds(0.6f);
        Note("kapandı", !locker.IsOpen && Mathf.Abs(locker.Angle) < 0.5f, "açı " + locker.Angle.ToString("F1", Inv) + "°");
        // Yerden al: ≤ 1 m yüz dönük "E: Halatı al" + kangal vurgusu; E → PickUp, elde
        Vector3 cpos = coil.GripMid;
        Vector3 toCoil = Flat(cpos - Player.transform.position);
        Vector3 standAt = U2Ground(cpos - (toCoil.sqrMagnitude > 1e-4f ? toCoil.normalized : side) * 0.8f, cpos.y);
        Player.Teleport(standAt, Quaternion.LookRotation(Flat(cpos - standAt).normalized));
        yield return null; yield return null;
        Note("yerden al", Interaction.CurrentHint == PlayerInteraction.HintTakeCoil && Highlight.Current == coil.transform, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl());
        Interaction.Interact();
        yield return null; yield return null;
        bool pickAnim = PlayerAnim.InState(CrewAnimation.StatePickUp);
        Note("PickUp", pickAnim && Player.HeldCoil == coil, "klip " + pickAnim + ", elde " + (Player.HeldCoil == coil));
        yield return Seconds(1.2f);
        Check("(U2-2) dolap: uzakta / sırtı dönük ipucu yok; ≤ 1 m yüz dönük \"E: Dolabı aç\" + kapak vurgusu; " + locker.openSeconds.ToString(Inv) + " sn'de dışa açılır; açıkken \"E: Halatı al\" + kangal vurgusu; Cabinet_TakeCoil ile kangal ellere; bırak / kapat / yerden al",
            ok, string.Join("; ", parts));
    }

    // ---- (U2-3) taşıma ----
    private static IEnumerator PhaseU2Carry(RopeAnchor rope, RopeCoil coil, TowTruckLocker locker)
    {
        // Hız ve kayma ölçümü düz test zemininde (düzeltme 1 ile aynı desen; patikada eğim/engel hızı 2.17 m/sn'ye düşürüyordu)
        Vector3 origin = new Vector3(320f, 0f, 320f);
        var floor = TestBox("U2Floor", origin + Vector3.down * 0.5f, new Vector3(60f, 1f, 60f), Quaternion.identity);
        Player.Teleport(origin, Quaternion.identity);
        yield return Seconds(0.3f);
        bool heldBefore = Player.HeldCoil == coil;
        Player.SetRunInput(true);
        Player.SetMoveInput(Vector2.up);
        yield return Seconds(0.4f);
        var meter = new FootSlideMeter(Player.animator, Player.transform);
        Vector3 p0 = Player.transform.position; float t0 = Time.time;
        float worstGrip = 0f; int walkFrames = 0, walkState = 0;
        while (Time.time - t0 < 1.6f)
        {
            meter.Sample();
            worstGrip = Mathf.Max(worstGrip, coil.GripErrorLeft, coil.GripErrorRight);
            walkFrames++;
            if (PlayerAnim.InState(CrewAnimation.StateCarryCoil)) walkState++;
            yield return null;
        }
        float vWalk = Flat(Player.transform.position - p0).magnitude / (Time.time - t0);
        Player.SetMoveInput(Vector2.zero);
        Player.SetRunInput(false);
        yield return Seconds(0.4f);
        // Zıplama yok
        float yBefore = Player.transform.position.y; float maxRise = 0f;
        Player.RequestJump();
        float tj = Time.time;
        while (Time.time - tj < 0.6f) { maxRise = Mathf.Max(maxRise, Player.transform.position.y - yBefore); yield return null; }
        // Bekleme: Carry_Coil_Idle (CarryCoil durumunda, Speed ~0), kangal hâlâ ellerde
        yield return Seconds(0.5f);
        bool idleState = PlayerAnim.InState(CrewAnimation.StateCarryCoil) && Player.animator.GetFloat("Speed") < 0.2f;
        float idleGrip = Mathf.Max(coil.GripErrorLeft, coil.GripErrorRight);
        // Kangal gövdeyle örtüşmüyor: merkez kökün önünde ve göğüs hizasında
        Vector3 local = Player.transform.InverseTransformPoint(coil.GripMid);
        UnityEngine.Object.Destroy(floor);
        // Görüntü: patikada (halat kazığının batısı) +X boyunca yürürken
        Vector3 start = U2Ground(rope.topStand.position + Vector3.left * 3.0f, rope.topStand.position.y);
        Player.Teleport(start, Quaternion.LookRotation(Vector3.right));
        Player.SetMoveInput(new Vector2(1f, 0f));
        yield return Seconds(0.8f);
        yield return Capture("u2_tasima", Player.transform.position + Vector3.forward * 2.6f + Vector3.right * 1.2f + Vector3.up * 1.3f, Player.transform.position + Vector3.up * 0.8f, 45f);
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.3f);
        Check("(U2-3) kangal taşıma: Carry_Coil_Walk, Shift basılıyken 2.4 m/sn (koşu yok), zıplama yok, kangal iki avuçta (tutuş hatası ≤ 3 cm), beklerken CarryCoil",
            heldBefore && Mathf.Abs(vWalk - Player.moveSpeed) < 0.08f && walkState > walkFrames * 0.9f && maxRise < 0.05f && worstGrip <= 0.03f && idleGrip <= 0.03f && idleState && local.z > 0.15f && local.y > 0.5f,
            "hız " + vWalk.ToString("F3", Inv) + " m/sn (istenen " + Player.moveSpeed.ToString(Inv) + ", koşu " + Player.runSpeed.ToString(Inv) + "), CarryCoil kare oranı " + walkState + "/" + walkFrames
            + ", zıplama yükselmesi " + Cm(maxRise) + ", tutuş hatası yürürken " + Cm(worstGrip) + " / beklerken " + Cm(idleGrip) + ", bekleme durumu " + idleState
            + ", kangal kök uzayında " + V(local));
        CheckFootSlide("(U2-3b) ÖLÇÜM Carry_Coil_Walk ayak kayması (2.4 m/sn)", meter, Mathf.Abs(vWalk - Player.moveSpeed) < 0.08f, "hız " + vWalk.ToString("F3", Inv) + " m/sn");
    }

    // ---- (U2-4) kazığa bağlama ----
    private static IEnumerator PhaseU2Tie(RopeAnchor rope, RopeCoil coil)
    {
        Vector3 stake = rope.transform.position;
        Vector3 back = Vector3.back; // dağ tarafı (patika), kazık uçurum kenarında
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        if (Player.HeldCoil != coil) { Player.HeldCoil = coil; coil.PickUp(Player); yield return null; }

        Player.Teleport(U2Ground(stake + back * 2.0f, stake.y), Quaternion.LookRotation(-back));
        yield return null; yield return null;
        Note("2.0 m uzakta", Interaction.CurrentHint == PlayerInteraction.HintDropCoil && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(U2Ground(stake + back * 0.9f, stake.y), Quaternion.LookRotation(back));
        yield return null; yield return null;
        Note("sırtı dönük", Interaction.CurrentHint == PlayerInteraction.HintDropCoil && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(U2Ground(stake + back * 0.9f, stake.y), Quaternion.LookRotation(-back));
        yield return null; yield return null;
        Note("yüz dönük", Interaction.CurrentHint == PlayerInteraction.HintTieStake && Highlight.Current == rope.transform && Highlight.CopyCount > 0,
            "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl());
        Interaction.Interact();
        yield return null; yield return null;
        bool tieAnim = PlayerAnim.InState(CrewAnimation.StateTieStake);
        float ahead = Vector3.Dot(stake - Player.transform.position, Player.transform.forward);
        bool heldEarly = coil.IsHeld && Vector3.Distance(coil.GripMid, (coil.Palm(Player.animator, true) + coil.Palm(Player.animator, false)) * 0.5f) < 0.05f;
        // Yerde aralığı (kare 17–45): kangal kazık yanında dik, alt kenarı zemine yakın, eller kangaldan ayrı
        yield return Seconds((Interaction.tieGroundStart + Interaction.tieGroundEnd) * 0.5f - 2f * Time.deltaTime);
        Vector3 gc = coil.GripMid;
        float bottomGap = GroundProbe.Find(gc + Vector3.up * 0.5f, 2f, out RaycastHit gg) ? (gc.y - coil.radius) - gg.point.y : 99f;
        float toStake = Flat(gc - stake).magnitude;
        bool upright = Vector3.Angle(coil.Normal, Vector3.up) > 70f;
        bool tiedMid = rope.State == RopeAnchor.RopeState.Tied && rope.line.enabled && rope.line.positionCount >= 2;
        Vector3 side = Vector3.right;
        yield return Capture("u2_baglama", stake + side * 2.3f + back * 1.2f + Vector3.up * 1.4f, stake + Vector3.up * 0.35f, 45f);
        Note("yerde aralığı", Mathf.Abs(bottomGap) < 0.06f && toStake < 0.9f && upright && tiedMid,
            "kangal alt kenarı zemine " + Cm(bottomGap) + ", kazığa " + M(toStake) + ", dik " + upright + ", bağlı+çizgi " + tiedMid);
        yield return Seconds(Interaction.tieSeconds - (Interaction.tieGroundStart + Interaction.tieGroundEnd) * 0.5f + 0.3f);
        bool heldAfter = Player.HeldCoil == coil && coil.IsHeld;
        var pts = new Vector3[Mathf.Max(2, rope.line.positionCount)];
        rope.line.GetPositions(pts);
        bool lineToCoil = rope.line.enabled && rope.line.positionCount == 2 && Vector3.Distance(pts[0], rope.ropeAttach.position) < 0.01f && Vector3.Distance(pts[1], coil.GripMid) < 0.05f;
        bool carryLoop = PlayerAnim.InState(CrewAnimation.StateCarryCoil);
        Note("Rope_TieStake", tieAnim && Mathf.Abs(ahead - Interaction.tieStakeAhead) < 0.05f && heldEarly && heldAfter && rope.State == RopeAnchor.RopeState.Tied && lineToCoil && carryLoop,
            "klip " + tieAnim + ", kazık önde " + M(ahead) + ", başta elde " + heldEarly + ", sonda elde " + heldAfter + ", durum " + rope.State + ", çizgi kazık→kangal " + lineToCoil + " (" + rope.line.positionCount + " nokta), CarryCoil " + carryLoop);
        // Bağlıyken bağlanılamaz, bırakılamaz
        Player.Teleport(rope.topStand.position, rope.topStand.rotation);
        yield return null; yield return null;
        bool attach = rope.Attach(Player);
        if (attach) rope.Detach(Player, rope.topStand.position, rope.topStand.rotation);
        Note("bağlıyken halata bağlanılamaz / kangal bırakılamaz", !attach && Interaction.CurrentHint != PlayerInteraction.HintAttachRope && Interaction.CurrentHint != PlayerInteraction.HintDropCoil,
            "Attach " + attach + ", ipucu \"" + Interaction.CurrentHint + "\"");
        Check("(U2-4) kazık: uzak / sırtı dönük bağlama ipucu yok; ≤ 1 m yüz dönük \"E: Halatı kazığa bağla\" + kazık vurgusu; Rope_TieStake (kare 17–45 kangal kazık yanında yerde dik); sonunda bağlı, kangal ellerde, çizgi kazıktan kangala",
            ok, string.Join("; ", parts));
    }

    // ---- (U2-5) atış ----
    private static IEnumerator PhaseU2Throw(RopeAnchor rope, RopeCoil coil)
    {
        Vector3 stake = rope.transform.position;
        Vector3 edge = rope.edgePoint.position;
        Vector3 valley = Flat(edge - stake).sqrMagnitude > 1e-4f ? Flat(edge - stake).normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, valley);
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        if (Player.HeldCoil != coil) { Player.HeldCoil = coil; coil.PickUp(Player); yield return null; }

        // Kazığın gerisinde (0.51 m), sırtı uçuruma → ipucu yok; 2.6 m uzakta yüz uçuruma → yok; yüz uçuruma → "E: Halatı at" + kazık vurgusu
        Vector3 at = U2Ground(stake - valley * 0.51f, stake.y);
        Player.Teleport(at, Quaternion.LookRotation(-valley));
        yield return null; yield return null;
        Note("sırtı uçuruma", Interaction.CurrentHint == "" && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(U2Ground(edge - valley * 2.6f, stake.y), Quaternion.LookRotation(valley));
        yield return null; yield return null;
        Note("2.6 m uzakta", Interaction.CurrentHint == "" && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(at, Quaternion.LookRotation(valley));
        yield return null; yield return null;
        float edgeDist = Flat(edge - Player.transform.position).magnitude;
        Note("yüz uçuruma (" + M(edgeDist) + ")", Interaction.CurrentHint == PlayerInteraction.HintThrowRope && Highlight.Current == rope.transform && Highlight.CopyCount > 0,
            "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + U2Hl());
        Interaction.Interact();
        yield return null; yield return null;
        bool throwAnim = PlayerAnim.InState(CrewAnimation.StateRopeThrow);
        bool heldBeforeRelease = Player.HeldCoil == coil;
        yield return Seconds(Interaction.throwReleaseTime + 0.25f);
        bool flying = rope.State == RopeAnchor.RopeState.Thrown && coil.State == RopeCoil.CoilState.Flying && Player.HeldCoil == null;
        var pts = new Vector3[Mathf.Max(2, rope.line.positionCount)];
        rope.line.GetPositions(pts);
        bool lineToFlying = rope.line.enabled && rope.line.positionCount >= 2 && Vector3.Distance(pts[0], rope.ropeAttach.position) < 0.01f && Vector3.Distance(pts[rope.line.positionCount - 1], coil.GripMid) < 0.05f;
        float ropeLen = 0f; for (int i = 1; i < rope.line.positionCount; i++) ropeLen += Vector3.Distance(pts[i - 1], pts[i]);
        Vector3 coilAir = coil.GripMid;
        bool inAir = GroundProbe.Find(coilAir + Vector3.up * 0.2f, 10f, out RaycastHit ag) ? coilAir.y - ag.point.y > 0.3f : true;
        yield return Capture("u2_atis", edge + side * 3.2f - valley * 1.6f + Vector3.up * 1.6f, (stake + coilAir) * 0.5f + Vector3.up * 0.2f, 55f);
        Note("Rope_Throw / uçuş", throwAnim && heldBeforeRelease && flying && lineToFlying && inAir,
            "klip " + throwAnim + ", bırakıştan önce elde " + heldBeforeRelease + ", uçuyor " + flying + " (kangal " + V(coilAir) + ", zeminden yüksek " + inAir + "), çizgi kazık→kangal " + lineToFlying + " " + rope.line.positionCount + " nokta, uzunluk " + M(ropeLen));
        float tRel = rope.ThrowStartTime;
        float tEnd = Time.time + 3f;
        yield return (Func<bool>)(() => rope.IsReady || Time.time >= tEnd);
        float flight = Time.time - tRel;
        Vector3 bottom = rope.climbBottom.position;
        Vector3 cpos = coil.GripMid;
        float toBottom = Flat(cpos - bottom).magnitude;
        float pen = GroundProbe.Find(cpos + Vector3.up * 0.5f, 2f, out RaycastHit bg) ? bg.point.y - (cpos.y - coil.halfDepth) : 99f;
        bool flat = Vector3.Angle(coil.Normal, Vector3.up) < 20f || Vector3.Angle(coil.Normal, Vector3.down) < 20f;
        yield return null;
        pts = new Vector3[Mathf.Max(3, rope.line.positionCount)];
        rope.line.GetPositions(pts);
        Vector3 last = pts[rope.line.positionCount - 1];
        bool lineReady = rope.line.enabled && rope.line.positionCount >= 3 && Vector3.Distance(pts[0], rope.ropeAttach.position) < 0.01f && Vector3.Distance(pts[1], edge) < 0.01f && Vector3.Distance(last, bottom) < 0.2f;
        yield return Capture("u2_hazir", edge + side * 3.6f + valley * 1.2f + Vector3.up * 0.8f, Vector3.Lerp(edge, bottom, 0.5f), 55f);
        Note("iniş", rope.IsReady && coil.State == RopeCoil.CoilState.Bottom && flight >= 0.7f && flight <= 1.2f && toBottom <= 0.8f && Mathf.Abs(pen) < 0.05f && flat && lineReady,
            "uçuş " + S(flight) + ", kangal dipte " + coil.State + " halat köküne " + M(toBottom) + ", zemine " + Cm(pen) + ", yatay " + flat + "; çizgi kazık→kenar→dip " + lineReady + " (" + rope.line.positionCount + " nokta, son " + V(last) + ")");
        // Hazır halat: bağlanma + iniş çalışır; kangal artık alınamaz
        yield return Seconds(Interaction.throwSeconds);
        Player.Teleport(rope.topStand.position, rope.topStand.rotation);
        yield return null; yield return null;
        string hintAttach = Interaction.CurrentHint;
        Interaction.Interact();
        bool attached = Player.Rope == rope;
        Player.SetMoveInput(new Vector2(0f, -1f));
        yield return Seconds(0.8f);
        bool descending = Player.Rope == rope && PlayerAnim.InState(CrewAnimation.StateRopeDescend) && rope.T < 0.97f;
        Player.SetMoveInput(Vector2.zero);
        if (Player.Rope != null) rope.Detach(Player, rope.topStand.position, rope.topStand.rotation);
        yield return null; yield return null;
        Note("hazır: bağlan + in", hintAttach == PlayerInteraction.HintAttachRope && attached && descending && !coil.CanPickUp,
            "ipucu \"" + hintAttach + "\", bağlı " + attached + ", iniyor " + descending + " (T " + rope.T.ToString("F2", Inv) + "), kangal alınamaz " + !coil.CanPickUp);
        Check("(U2-5) atış: sırtı / uzakta ipucu yok; kenara dönük \"E: Halatı at\" + kazık vurgusu; Rope_Throw bırakışında kangal uçar, çizgi kazıktan kangala uzar; " + rope.throwSeconds.ToString(Inv) + " sn'de kayaya iner, halat hazır (kazık→kenar→dip); bağlanma ve iniş çalışır",
            ok, string.Join("; ", parts));
    }
}
