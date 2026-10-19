using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// D2 kabul kontrolleri (2026-10-17 kararları 1–5, 8): başlangıç yerleşimi, minibüsten çekme, halat, sırtta taşıma, ölçümler.
// Taze sahnede, öbür fazlardan önce çalışır; sonunda hastalar eski testler için patikaya yatırılır (Bolum1TestPatients).
public static partial class Bolum1AnimVerify
{
    private static MinivanTailgate Tailgate { get { return MinivanTailgate.All.FirstOrDefault(); } }
    private static RopeAnchor Rope { get { return RopeAnchor.All.FirstOrDefault(); } }

    private static IEnumerator PhaseRescue()
    {
        var bot = Bot;
        var tg = Tailgate;
        var rope = Rope;
        var tourist = PatientNamed("Tourist");
        var civilian = PatientNamed("Civilian");
        var elder = PatientNamed("Elder");
        var van = GameObject.Find(Bolum1SceneBuilder.MinivanName).transform;
        Vector3 vanPos0 = van.position;
        Quaternion vanRot0 = van.rotation;
        var s1 = Stretcher(0);
        var savedStretchers = StretcherController.All.Select(s => (s, s.transform.position, s.transform.rotation)).ToList();
        bot.assistEnabled = false;
        // Hareket yönleri dünya eksenine göre (kamera yok): (1, 0) = +X, (-1, 0) = -X; kaya çıkıntısı X boyunca uzanır.
        Transform savedCamera = Player.cameraTransform;
        Player.cameraTransform = null;

        // ---- (a) başlangıç yerleşimi ----
        bool seated = new[] { tourist, civilian }.All(p => p.State == PatientState.InVehicle && p.transform.IsChildOf(van) && p.animator.GetCurrentAnimatorStateInfo(0).IsName(Patient.StateSit));
        float seatErr = new[] { (tourist, "Seat_05"), (civilian, "Seat_06") }.Max(x =>
        {
            var m = van.GetComponentsInChildren<Transform>().First(t => t.name == x.Item2);
            return Vector3.Distance(x.Item1.transform.position + van.TransformDirection(0f, 0f, 1f) * 0.262f, m.position);
        });
        bool ledge = elder.State == PatientState.LyingGround && elder.onLedge && elder.Position.y < rope.topStand.position.y - 3f
            && (elder.animator.GetCurrentAnimatorStateInfo(0).IsName("Groan") || elder.animator.GetCurrentAnimatorStateInfo(0).IsName("HelpWave"));
        bool counted = Mission.patients.Length == 3 && Mission.patients.All(p => p.MissedAmbulances == 0);
        bool sling = civilian.animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Patient_SitDazed_ArmSling");
        Check("(D2-a) başlangıç: Turist Seat_05, Sivil Seat_06'da oturuyor (InVehicle, SitDazed; Sivil ArmSling); Yaşlı kaya çıkıntısında yatıyor; 3 hasta sayaçta",
            seated && seatErr < 0.01f && sling && ledge && counted,
            "oturuyor " + seated + " (koltuk yüzeyi hatası " + Cm(seatErr) + ", Sivil klibi ArmSling " + sling + "); Yaşlı " + elder.State + " kayada " + elder.onLedge
            + " kalça y " + elder.Position.y.ToString("F2", Inv) + " (patika " + rope.topStand.position.y.ToString("F2", Inv) + "); sayaç " + counted);

        // ---- (b) kapı kapalıyken çekme yok; E kapıyı açar; açıkken E hastayı patikaya çeker; sedyeye alınır ----
        Vector3 spot = tg.PullerSpot(tourist, out Quaternion rot);
        Player.Teleport(spot, rot);
        yield return null; yield return null;
        string hintClosed = Interaction.CurrentHint;
        Transform u1HlClosed = Highlight.Current; // U1: bagaj kapısı vurgusu
        bool directRefused = !tg.Extract(tourist, Player) && tourist.State == PatientState.InVehicle;
        // Minibüs hastası sırta alınamaz (e)
        bool vanNoPiggy = !tourist.CanPiggyback && hintClosed != PlayerInteraction.HintPiggyback;
        Interaction.Interact();
        yield return Seconds(1.0f);
        bool opened = tg.IsOpen && Mathf.Abs(tg.Angle - tg.openAngle) < 0.5f;
        string hintOpen = Interaction.CurrentHint;
        // Kapı açık, koltuklarda 2 hasta (görüntü)
        Vector3 back = tg.RearDirection;
        Vector3 seatsMid = (tourist.Position + civilian.Position) * 0.5f;
        Player.Teleport(tg.rearPoint.position + back * 4f + Vector3.Cross(Vector3.up, back) * 2.5f, rot);
        yield return Capture("d2_minibus_hastalar", tg.rearPoint.position + back * 3.2f + Vector3.up * 2.1f + Vector3.Cross(Vector3.up, back) * 0.6f, seatsMid + Vector3.up * 0.2f, 55f);
        Player.Teleport(spot, rot);
        yield return null; yield return null;
        string hintExtract = Interaction.CurrentHint;
        var target = tg.ExtractTarget(Player.transform.position);
        Transform u1HlExtract = Highlight.Current; // U1: minibüsteki hasta vurgusu
        Interaction.Interact();
        float t0 = Time.time;
        yield return null; yield return null;
        bool pullAnim = PlayerAnim.InState(CrewAnimation.StateExtract);
        yield return Seconds(0.45f + 0.6f);
        bool dragAnim = target != null && target.animator.GetCurrentAnimatorStateInfo(0).IsName(Patient.StateDraggedOut);
        Vector3 side = Vector3.Cross(Vector3.up, back);
        yield return Capture("d2_cikarma", tg.rearPoint.position + back * 1.6f + side * 3.6f + Vector3.up * 1.7f, tg.rearPoint.position + back * 0.9f + Vector3.up * 0.5f, 55f);
        yield return Until(() => target != null && target.State == PatientState.LyingGround, 5f, "çekilen hasta yere yattı");
        float dragTime = Time.time - t0;
        bool onPath = GroundProbe.Find(target.Position + Vector3.up, 2f, out RaycastHit g) && g.transform.name.Contains("Ground") && Mathf.Abs(target.Position.y - target.hipsToBack - g.point.y) < 0.05f;
        float behind = Vector3.Dot(Flat(target.Position - tg.rearPoint.position), back);
        // Yatan gövde (kalçadan başa ~0.8 m, ayaklara ~0.7 m) eşyalara / minibüse değmiyor mu
        var env = new[] { "SM_B1_Ground", "SM_B1_Road", "SM_B1_Guardrail" }.Select(n => GameObject.Find(n).transform).ToArray();
        string[] touching = Physics.OverlapBox(target.Position + back * 0.05f, new Vector3(0.3f, 0.12f, 0.75f), Quaternion.LookRotation(back), ~0, QueryTriggerInteraction.Ignore)
            .Where(c => !env.Any(e => c.transform.IsChildOf(e)) && !c.transform.IsChildOf(target.transform) && !(c is CharacterController)).Select(c => c.name).Distinct().ToArray();
        yield return Seconds(1.0f);
        float pullerErr = GroundError(Player.transform.position); // çeken, yatan hastanın üstüne çıkmadı mı
        // Kapı açık kalır (E ile kapanmaz); ikinci hasta
        bool stillOpen = tg.IsOpen;
        var other = target == tourist ? civilian : tourist;
        Vector3 spot2 = tg.PullerSpot(other, out Quaternion rot2);
        Player.Teleport(spot2, rot2);
        yield return null; yield return null;
        string hint2 = Interaction.CurrentHint;
        Interaction.Interact();
        yield return Until(() => other.State == PatientState.LyingGround, 5f, "ikinci hasta yere yattı");
        yield return Seconds(1.6f);
        float apart = Vector3.Distance(Flat(tourist.Position), Flat(civilian.Position));
        // Patikadaki hasta da sırta alınamaz (e)
        Player.Teleport(target.Position + side * 0.9f + Vector3.down * target.hipsToBack, rot);
        yield return null; yield return null;
        bool pathNoPiggy = !target.CanPiggyback && Interaction.CurrentHint != PlayerInteraction.HintPiggyback;
        string pathHint = Interaction.CurrentHint;
        // Sedyeye alma (mevcut kural)
        yield return PutPatientOnStretcher(target, s1);
        bool stretcherOk = target.State == PatientState.OnStretcher;
        float vanMoved = Vector3.Distance(van.position, vanPos0);
        float vanTurned = Quaternion.Angle(van.rotation, vanRot0);
        Check("(D2-b) kapı kapalıyken çekme reddediliyor; E kapıyı " + tg.openAngle.ToString("F0", Inv) + "°'ye açıyor; açıkken E hastayı patikaya çekiyor (LyingGround), sedyeye alınıyor; minibüs sabit",
            hintClosed == PlayerInteraction.HintOpenTailgate && directRefused && opened && hintOpen == PlayerInteraction.HintExtract && hintExtract == PlayerInteraction.HintExtract
            && pullAnim && dragAnim && onPath && behind > 0.3f && touching.Length == 0 && Mathf.Abs(pullerErr) < 0.1f && stillOpen && hint2 == PlayerInteraction.HintExtract && apart > 0.6f && stretcherOk && vanMoved < 0.001f && vanTurned < 0.01f,
            "kapalıyken ipucu \"" + hintClosed + "\", doğrudan çekme reddedildi " + directRefused + "; açıldı " + opened + " (" + tg.Angle.ToString("F1", Inv) + "°), ipucu \"" + hintOpen
            + "\"; Extract_Pull " + pullAnim + ", Patient_DraggedOut " + dragAnim + ", yere yatma " + S(dragTime) + ", zeminde " + onPath + ", arka tampondan " + M(behind) + ", gövdeye değen " + (touching.Length == 0 ? "yok" : string.Join(",", touching)) + ", çeken zeminden " + Cm(pullerErr)
            + " geride; kapı açık kaldı " + stillOpen + "; 2. hasta ipucu \"" + hint2 + "\", iki hasta arası " + M(apart) + "; sedyeye " + stretcherOk
            + "; minibüs oynaması " + Cm(vanMoved) + " / " + vanTurned.ToString("F3", Inv) + "°");
        Check("(U1-2t) beyaz vurgu: bagaj kapısı ipucunda kapı, çekme ipucunda minibüsteki hasta vurgulu",
            u1HlClosed == tg.tailgate && target != null && u1HlExtract == target.transform,
            "kapı ipucunda vurgu " + (u1HlClosed != null ? u1HlClosed.name : "yok") + ", çekme ipucunda " + (u1HlExtract != null ? u1HlExtract.name : "yok") + " (hedef " + (target != null ? target.name : "-") + ")");

        // ---- (c) halat: bağlan, in, kayada çöz, yeniden bağlan, boşken çık ----
        Player.Teleport(rope.topStand.position, rope.topStand.rotation);
        yield return null; yield return null;
        string hintAttach = Interaction.CurrentHint;
        Interaction.Interact();
        bool attached = Player.Rope == rope;
        yield return Seconds(0.3f);
        Player.SetMoveInput(new Vector2(0f, -1f));
        yield return Seconds(0.6f);
        bool descendAnim = PlayerAnim.InState(CrewAnimation.StateRopeDescend);
        var handsDescend = new HandMeter(Player.animator, Player.transform);
        float y0 = Player.transform.position.y; t0 = Time.time;
        while (Time.time - t0 < 1.5f) { handsDescend.Sample(); yield return null; }
        float vDescend = (Player.transform.position.y - y0) / (Time.time - t0);
        yield return Capture("d2_halat_inis", Player.transform.position + Vector3.forward * 4.2f + Vector3.right * 2.2f + Vector3.up * 1.6f, Player.transform.position + Vector3.up * 0.8f, 50f);
        Time.timeScale = 3f;
        yield return Until(() => rope.AtBottom, 30f, "halat dibi");
        Time.timeScale = 1f;
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.3f);
        string hintBottom = Interaction.CurrentHint;
        Interaction.Interact();
        bool detached = Player.Rope == null;
        yield return Seconds(0.4f);
        Player.SetMoveInput(new Vector2(1f, 0f)); // kayada yürü
        Vector3 w0 = Player.transform.position;
        yield return Seconds(0.8f);
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.3f);
        float walked = Flat(Player.transform.position - w0).magnitude;
        bool onRock = Mathf.Abs(Player.transform.position.y - rope.climbBottom.position.y) < 0.3f && !Player.IsDown;
        // Yeniden bağlan (dipte)
        // U1: ≤ 1.0 m ve yüz halat köküne dönük
        Vector3 awayFromRope = Flat(Player.transform.position - rope.climbBottom.position).normalized;
        Player.Teleport(rope.climbBottom.position + awayFromRope * 0.6f + Vector3.up * 0.02f, Quaternion.LookRotation(-awayFromRope));
        yield return null; yield return null;
        string hintReattach = Interaction.CurrentHint;
        Interaction.Interact();
        bool reattached = Player.Rope == rope && rope.AtBottom;
        Player.SetMoveInput(new Vector2(0f, 1f));
        yield return Seconds(0.6f);
        bool climbAnim = PlayerAnim.InState(CrewAnimation.StateRopeClimb);
        var handsClimb = new HandMeter(Player.animator, Player.transform);
        y0 = Player.transform.position.y; t0 = Time.time;
        while (Time.time - t0 < 1.5f) { handsClimb.Sample(); yield return null; }
        float vClimb = (Player.transform.position.y - y0) / (Time.time - t0);
        Time.timeScale = 3f;
        yield return Until(() => Player.Rope == null, 30f, "boşken tepeye çıkış");
        Time.timeScale = 1f;
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(0.5f);
        bool topOut = Player.Rope == null && !Player.IsDown && Mathf.Abs(Player.transform.position.y - rope.topStand.position.y) < 0.3f;
        Check("(D2-c) halat: kazıkta E bağlanır, S iner (Rope_Descend), dipte E çözülür ve kayada yürür, E yeniden bağlanır, W boşken çıkar (Rope_Climb), tepede patikaya çıkar",
            hintAttach == PlayerInteraction.HintAttachRope && attached && descendAnim && Mathf.Abs(vDescend + rope.descendSpeed) < 0.03f && hintBottom == PlayerInteraction.HintDetachRope && detached
            && walked > 0.5f && onRock && hintReattach == PlayerInteraction.HintAttachRope && reattached && climbAnim && Mathf.Abs(vClimb - rope.climbSpeed) < 0.03f && topOut,
            "ipucu \"" + hintAttach + "\" → bağlı " + attached + "; iniş " + vDescend.ToString("F3", Inv) + " m/sn (Rope_Descend " + descendAnim + "); dipte \"" + hintBottom + "\" → çözüldü " + detached
            + ", kayada yürüdü " + M(walked) + " (kayada " + onRock + "); \"" + hintReattach + "\" → yeniden bağlı " + reattached + "; tırmanış " + vClimb.ToString("F3", Inv)
            + " m/sn (Rope_Climb " + climbAnim + "); tepede patikada " + topOut + " (y " + Player.transform.position.y.ToString("F2", Inv) + ")");
        Line("  ÖLÇÜM (8) iniş/tırmanış ayırt edilebilirliği: Rope_Descend " + handsDescend.Report(vDescend) + " | Rope_Climb " + handsClimb.Report(vClimb));

        // ---- (d) Yaşlı: sırta al, sırtta yürü, kendi tırmanamaz, bot çeker, yukarıda bırak, sedye ----
        Player.Teleport(rope.climbBottom.position + Vector3.up * 0.02f, rope.ClimbRotation);
        Vector3 toElder = Flat(elder.Position - rope.climbBottom.position);
        Vector3 nearElder = rope.climbBottom.position + toElder.normalized * Mathf.Max(0f, toElder.magnitude - 0.9f);
        if (GroundProbe.Find(nearElder + Vector3.up, 2f, out g)) nearElder.y = g.point.y + 0.02f;
        Player.Teleport(nearElder, Quaternion.LookRotation(toElder.normalized));
        yield return null; yield return null;
        string hintPiggy = Interaction.CurrentHint;
        Interaction.Interact();
        yield return null; yield return null;
        bool pickAnim = PlayerAnim.InState(CrewAnimation.StatePiggybackPickUp);
        bool notYet = elder.State == PatientState.LyingGround;
        yield return Seconds(0.65f);
        bool onBack = Player.Piggyback == elder && elder.State == PatientState.OnBack && elder.transform.parent == Player.piggybackSocket;
        yield return Seconds(1.2f);
        bool riderAnim = elder.animator.GetCurrentAnimatorStateInfo(0).IsName(Patient.StateRider);
        bool carryState = PlayerAnim.InState(CrewAnimation.StateCarryPiggyback);
        // Sırtta yürüyüş: koşamaz (Shift basılı), 1.2 m/sn; ayak kayması; iç içe geçme
        var pen = new PenetrationMeter(Player.animator, elder);
        // Kayanın ortasında (duvardan ve kenardan uzak) +X boyunca yürü
        Vector3 walkStart = new Vector3(elder.transform.position.x, 0f, rope.climbBottom.position.z + 0.9f);
        walkStart.x = rope.climbBottom.position.x - 1.0f;
        walkStart.y = rope.climbBottom.position.y + 0.02f;
        Player.Teleport(walkStart, Quaternion.LookRotation(Vector3.right));
        yield return Seconds(0.2f);
        Player.SetRunInput(true);
        Player.SetMoveInput(new Vector2(1f, 0f));
        yield return Seconds(0.4f);
        var meter = new FootSlideMeter(Player.animator, Player.transform);
        Vector3 p0 = Player.transform.position; t0 = Time.time;
        int frame = 0;
        while (Time.time - t0 < 2.4f) { meter.Sample(); if (frame++ % 4 == 0) pen.Sample("yürüyüş"); yield return null; }
        float vWalk = Flat(Player.transform.position - p0).magnitude / (Time.time - t0);
        Player.SetMoveInput(Vector2.zero);
        Player.SetRunInput(false);
        yield return Seconds(0.3f);
        // U1: kayada bırakılamayan yerde "Burada bırakılamaz" uyarısı da ipucu yerinde görünür (eylem değil)
        bool noOtherActions = Interaction.CurrentHint == "" || Interaction.CurrentHint == PlayerInteraction.HintAttachRope || Interaction.CurrentHint == PlayerInteraction.NoticeCannotPutDown;
        bool cantPutDownOnLedge = Interaction.CurrentHint != PlayerInteraction.HintPutDownBack;
        // Halata sırtında hastayla bağlan; bot kapalı: W ile kendi tırmanamaz, aşağıda kalır (U1: yüz halat köküne dönük)
        Player.Teleport(rope.climbBottom.position + Vector3.right * 0.5f + Vector3.up * 0.02f, Quaternion.LookRotation(Vector3.left));
        yield return null; yield return null;
        string hintAttachLoaded = Interaction.CurrentHint;
        Interaction.Interact();
        bool loadedAttached = Player.Rope == rope && Player.Piggyback == elder;
        float tBefore = rope.T;
        Player.SetMoveInput(new Vector2(0f, 1f));
        yield return Seconds(2.5f);
        bool stuck = Mathf.Abs(rope.T - tBefore) < 1e-4f && !bot.Pulling;
        Player.SetMoveInput(Vector2.zero);
        // Bot açık: kazığa ışınlanıp çeker
        bot.assistEnabled = true;
        yield return Seconds(0.8f);
        bool botAtStake = Flat(bot.transform.position - rope.pullerStand.position).magnitude < 0.05f && bot.Pulling;
        bool botPullAnim = BotAnim.InState(CrewAnimation.StateRopePull);
        bool pClimbAnim = PlayerAnim.InState(CrewAnimation.StateRopePiggybackClimb);
        y0 = Player.transform.position.y; t0 = Time.time;
        frame = 0;
        while (Time.time - t0 < 1.5f) { if (frame++ % 4 == 0) pen.Sample("halat"); yield return null; }
        float vPull = (Player.transform.position.y - y0) / (Time.time - t0);
        yield return Capture("d2_sirtta_tasima", Player.transform.position + Vector3.forward * 3.6f + Vector3.left * 2.4f + Vector3.up * 1.4f, Player.transform.position + Vector3.up * 0.9f, 50f);
        Time.timeScale = 3f;
        yield return Until(() => Player.Rope == null, 40f, "çekilerek tepeye çıkış");
        Time.timeScale = 1f;
        yield return Seconds(0.5f);
        bool upWithElder = Player.Piggyback == elder && !Player.IsDown && Mathf.Abs(Player.transform.position.y - rope.topStand.position.y) < 0.3f;
        bool botStopped = !bot.Pulling;
        string hintPut = Interaction.CurrentHint;
        Interaction.Interact();
        yield return null; yield return null;
        bool putAnim = PlayerAnim.InState(CrewAnimation.StatePiggybackPutDown);
        yield return Seconds(1.4f);
        bool putDown = elder.State == PatientState.LyingGround && !elder.onLedge && Player.Piggyback == null
            && GroundProbe.Find(elder.Position + Vector3.up, 2f, out g) && Mathf.Abs(elder.Position.y - elder.hipsToBack - g.point.y) < 0.06f;
        float standErr = GroundError(Player.transform.position); // oyuncu bırakılan hastanın üstüne çıkmadı mı
        // Patikaya bırakılan Yaşlı bir daha sırta alınamaz (e)
        Player.Teleport(Player.transform.position, Player.transform.rotation);
        yield return null; yield return null;
        bool elderPathNoPiggy = !elder.CanPiggyback && Interaction.CurrentHint != PlayerInteraction.HintPiggyback;
        Line("  tanı (D2-d tepe): ipucu \"" + hintPut + "\", Yaşlı " + elder.State + ", oyuncu " + V(Player.transform.position) + " ileri " + V(Player.transform.forward)
            + ", halatta " + (Player.Rope != null) + ", sırtta " + (Player.Piggyback != null ? Player.Piggyback.name : "-"));
        if (Player.Rope != null) rope.Detach(Player, rope.topStand.position, rope.topStand.rotation);
        if (Player.Piggyback != null) { var pb = Player.ReleasePiggyback(); pb.PutDownFromBack(Player.transform.position - Player.transform.forward * 0.5f, -Player.transform.forward, Vector3.up); }
        bool elderStretcher = false;
        if (elder.CanBePickedUp)
        {
            yield return PutPatientOnStretcher(elder, s1.Passenger == null ? s1 : Stretcher(1));
            elderStretcher = elder.State == PatientState.OnStretcher;
        }
        Check("(D2-d) Yaşlı: kayada E sırta alır (Piggyback_PickUp, kare 17'de sırtta, Piggyback_Rider); sırtta 1.2 m/sn, koşamaz; kendi tırmanamaz; bot kazıkta çeker (Rope_Pull / Piggyback_Rope_Climb); tepede E bırakır; sedyeye alınır",
            hintPiggy == PlayerInteraction.HintPiggyback && pickAnim && notYet && onBack && riderAnim && carryState && Mathf.Abs(vWalk - 1.2f) < 0.05f && noOtherActions && cantPutDownOnLedge
            && hintAttachLoaded == PlayerInteraction.HintAttachRope && loadedAttached && stuck && botAtStake && botPullAnim && pClimbAnim && Mathf.Abs(vPull - rope.pullSpeed) < 0.03f
            && upWithElder && botStopped && hintPut == PlayerInteraction.HintPutDownBack && putAnim && putDown && Mathf.Abs(standErr) < 0.1f && elderStretcher,
            "ipucu \"" + hintPiggy + "\", PickUp " + pickAnim + ", kare 17'den önce yerde " + notYet + ", sonra sırtta " + onBack + " (Rider " + riderAnim + ", CarryPiggyback " + carryState + ")"
            + "; Shift basılıyken hız " + vWalk.ToString("F3", Inv) + " m/sn; kayada başka eylem yok " + noOtherActions + ", kayada bırakma yok " + cantPutDownOnLedge
            + "; yükle bağlandı " + loadedAttached + " (\"" + hintAttachLoaded + "\"), bot kapalıyken W ile 2.5 sn'de yer değiştirmedi " + stuck
            + "; bot kazıkta " + botAtStake + " (Rope_Pull " + botPullAnim + "), oyuncu Piggyback_Rope_Climb " + pClimbAnim + ", çekilme " + vPull.ToString("F3", Inv) + " m/sn"
            + "; tepede sırtında " + upWithElder + ", bot durdu " + botStopped + "; \"" + hintPut + "\" → PutDown " + putAnim + ", patikada yatıyor " + putDown + " (oyuncu zeminden " + Cm(standErr) + ")" + "; sedyeye " + elderStretcher);
        Check("(D2-e) sırta alma yalnız kayadaki hasta: minibüsteki, patikaya çekilen ve patikaya bırakılan hasta sırta alınamıyor",
            vanNoPiggy && pathNoPiggy && elderPathNoPiggy,
            "minibüste " + vanNoPiggy + " (ipucu \"" + hintClosed + "\"), patikada " + pathNoPiggy + " (ipucu \"" + pathHint + "\"), bırakılan Yaşlı " + elderPathNoPiggy);
        CheckFootSlide("(D2-8) ÖLÇÜM Piggyback_Walk ayak kayması (1.2 m/sn)", meter, Mathf.Abs(vWalk - 1.2f) < 0.05f, "hız " + vWalk.ToString("F3", Inv) + " m/sn");
        Line("  ÖLÇÜM (8) sırttaki Yaşlı–taşıyan iç içe geçme (taşıyanın kemik kapsülü yaklaşımı, alçılı bacak dahil): " + pen.Report());
        Line("  ÖLÇÜM halat hızları: iniş " + vDescend.ToString("F3", Inv) + " (klip -0.6), boş tırmanış " + vClimb.ToString("F3", Inv) + " (klip 0.3), çekilme " + vPull.ToString("F3", Inv)
            + " (Piggyback_Rope_Climb 0.25; Rope_Pull halatı 0.255 → oynatma " + Bolum1AnimBuilder.RopePullPlayback.ToString("F3", Inv) + "), çekilen toplam " + M(rope.PulledDistance));

        // Toparla: bot kapalı, sedye eski yerine (öbür fazlar sahnedeki ilk yerleşimi bekler)
        bot.assistEnabled = false;
        Player.cameraTransform = savedCamera;
        foreach (var s in StretcherController.All) if (s.Passenger != null) s.TakePatient();
        foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);
        yield return null;
    }

    private static string Cm(float m) { return (m * 100f).ToString("F1", Inv) + " cm"; }

    // D2 madde 7: Carry_Stretcher_Walk.fbx.meta temiz import'ta kendi Avatar'ıyla (kol limitleri 0) yazılıyordu.
    // Ölçüm: importer kaynak Avatar'dan kopya mı, kol limitleri sıfır değil mi, kas eğrileri klibin yazıldığı pozu veriyor mu
    // (README: eller önde sedye saplarında, x ±0.30 m). Tüm klip meta'larında Avatar kurulumu aynı mı.
    private static void CheckCarryClipMeta()
    {
        const string name = "Carry_Stretcher_Walk";
        var imp = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(ProceduralAnimImportSettings.ClipPath(name));
        var hd = imp.humanDescription;
        var arm = hd.human.FirstOrDefault(h => h.humanName == "LeftUpperArm");
        bool limits = arm.limit.useDefaultValues || arm.limit.min != Vector3.zero || arm.limit.max != Vector3.zero;
        var clip = ProceduralAnimImportSettings.LoadClip(name);
        var muscles = UnityEditor.AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Animator) && (b.propertyName.Contains("Arm") || b.propertyName.Contains("Forearm"))).ToList();
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralAnimImportSettings.SourceAvatarPath);
        var inst = UnityEngine.Object.Instantiate(prefab);
        inst.transform.SetPositionAndRotation(new Vector3(2000f, 0f, 2000f), Quaternion.identity);
        var a = inst.GetComponent<Animator>();
        float worstX = 0f, minFwd = float.MaxValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i <= 8; i++)
        {
            clip.SampleAnimation(inst, clip.length * i / 8f);
            foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                Vector3 h = inst.transform.InverseTransformPoint(a.GetBoneTransform(hb).position);
                worstX = Mathf.Max(worstX, Mathf.Abs(Mathf.Abs(h.x) - 0.30f));
                minFwd = Mathf.Min(minFwd, h.z); minY = Mathf.Min(minY, h.y); maxY = Mathf.Max(maxY, h.y);
            }
        }
        UnityEngine.Object.DestroyImmediate(inst);
        var setups = ProceduralAnimImportSettings.ClipNames.Select(n => (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(ProceduralAnimImportSettings.ClipPath(n)))
            .Where(i => i != null && i.avatarSetup != UnityEditor.ModelImporterAvatarSetup.CopyFromOther).Select(i => System.IO.Path.GetFileNameWithoutExtension(i.assetPath)).ToList();
        Check("(D2-7) Carry_Stretcher_Walk meta: Avatar SK_RescueCrew_Blue kopyası, kol limitleri dolu, kas eğrileri eller önde (x ±0.30 m); bütün klipler CopyFromOther",
            imp.avatarSetup == UnityEditor.ModelImporterAvatarSetup.CopyFromOther && imp.sourceAvatar != null && limits && muscles.Count >= 8 && worstX < 0.08f && minFwd > 0.1f && setups.Count == 0,
            "avatarSetup " + imp.avatarSetup + " (" + (imp.sourceAvatar != null ? imp.sourceAvatar.name : "-") + "), LeftUpperArm limit min " + arm.limit.min + " max " + arm.limit.max
            + (arm.limit.useDefaultValues ? " (varsayılan)" : "") + "; kol kas eğrisi " + muscles.Count + "; eller: |x|−0.30 en çok " + Cm(worstX) + ", önde en az " + Cm(minFwd)
            + ", yükseklik " + minY.ToString("F2", Inv) + "–" + maxY.ToString("F2", Inv) + " m; kendi Avatar'lı klip: " + (setups.Count == 0 ? "yok" : string.Join(",", setups)));
    }

    // Halattayken iki elin hareketi: köke göre aralık ve dünyada halatta "sabit" kalma oranı (|dikey hız| < 0.1 m/sn).
    // Tırmanışta tutan el halatta sabit kalır, gövde yukarı çıkar; inişte eller gövdeyle birlikte aşağı kayar.
    private sealed class HandMeter
    {
        private readonly Transform root, l, r;
        private readonly List<Vector4> h = new List<Vector4>(); // x,y: köke göre; z,w: dünya y
        private readonly List<float> t = new List<float>();
        public HandMeter(Animator a, Transform root)
        {
            this.root = root;
            l = a.GetBoneTransform(HumanBodyBones.LeftHand);
            r = a.GetBoneTransform(HumanBodyBones.RightHand);
        }
        public void Sample() { h.Add(new Vector4(l.position.y - root.position.y, r.position.y - root.position.y, l.position.y, r.position.y)); t.Add(Time.time); }
        public string Report(float rootSpeed)
        {
            if (h.Count < 3) return "örnek yok";
            float lr = h.Max(v => v.x) - h.Min(v => v.x), rr = h.Max(v => v.y) - h.Min(v => v.y);
            int fixedN = 0, n = 0;
            float down = 0f, up = 0f;
            for (int i = 1; i < h.Count; i++)
            {
                float dt = t[i] - t[i - 1]; if (dt <= 0f) continue;
                foreach (float v in new[] { (h[i].z - h[i - 1].z) / dt, (h[i].w - h[i - 1].w) / dt })
                {
                    n++;
                    if (Mathf.Abs(v) < 0.1f) fixedN++;
                    if (v < 0f) down = Mathf.Min(down, v); else up = Mathf.Max(up, v);
                }
            }
            return "kök " + rootSpeed.ToString("+0.00;-0.00", Inv) + " m/sn; eller köke göre sol " + (lr * 100f).ToString("F1", Inv) + " / sağ " + (rr * 100f).ToString("F1", Inv)
                + " cm oynuyor; ellerin dünyada halatta sabit kaldığı oran %" + (100f * fixedN / Mathf.Max(1, n)).ToString("F0", Inv)
                + ", el dünya dikey hızı " + down.ToString("F2", Inv) + "…+" + up.ToString("F2", Inv) + " m/sn";
        }
    }

    // Sırttaki hastanın mesh köşelerinin, taşıyanın kemik kapsüllerine (yarıçap: o kemiğe bağlı köşelerin %75'lik uzaklığı) girme derinliği.
    private sealed class PenetrationMeter
    {
        private readonly List<(Transform a, Transform b, float r, string name)> caps = new List<(Transform, Transform, float, string)>();
        private readonly SkinnedMeshRenderer[] rider;
        private readonly Patient patient;
        private float worst; private string worstInfo = "-"; private int samples;
        private readonly Dictionary<string, float> worstByPart = new Dictionary<string, float>();

        public PenetrationMeter(Animator carrier, Patient patient)
        {
            this.patient = patient;
            rider = patient.GetComponentsInChildren<SkinnedMeshRenderer>();
            Transform B(HumanBodyBones x) { return carrier.GetBoneTransform(x); }
            var pairs = new List<(HumanBodyBones, HumanBodyBones)>
            {
                (HumanBodyBones.Hips, HumanBodyBones.Spine), (HumanBodyBones.Spine, HumanBodyBones.Chest), (HumanBodyBones.Chest, HumanBodyBones.Neck),
                (HumanBodyBones.Neck, HumanBodyBones.Head),
                (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
                (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
            };
            // Kapsül yarıçapı: taşıyan mesh'inin bu kemiğe ağırlıklı köşelerinin segmente uzaklığı
            var smr = carrier.GetComponentsInChildren<SkinnedMeshRenderer>();
            var dist = pairs.Select(_ => new List<float>()).ToList();
            var baked = new Mesh();
            foreach (var s in smr)
            {
                s.BakeMesh(baked);
                var m = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                var verts = baked.vertices;
                var weights = s.sharedMesh.boneWeights;
                for (int i = 0; i < verts.Length && i < weights.Length; i++)
                {
                    var bone = s.bones[weights[i].boneIndex0];
                    int k = pairs.FindIndex(p => B(p.Item1) == bone);
                    if (k < 0) continue;
                    var a = B(pairs[k].Item1); var b = B(pairs[k].Item2);
                    if (a == null || b == null) continue;
                    dist[k].Add(SegDist(m.MultiplyPoint3x4(verts[i]), a.position, b.position));
                }
            }
            UnityEngine.Object.Destroy(baked);
            for (int k = 0; k < pairs.Count; k++)
            {
                var a = B(pairs[k].Item1); var b = B(pairs[k].Item2);
                if (a == null || b == null || dist[k].Count < 8) continue;
                dist[k].Sort();
                caps.Add((a, b, dist[k][(int)(dist[k].Count * 0.75f)], pairs[k].Item1.ToString()));
            }
        }

        public void Sample(string phase)
        {
            samples++;
            var baked = new Mesh();
            var a = patient.animator;
            foreach (var s in rider)
            {
                s.BakeMesh(baked);
                var m = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                var verts = baked.vertices;
                var weights = s.sharedMesh.boneWeights;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 v = m.MultiplyPoint3x4(verts[i]);
                    foreach (var c in caps)
                    {
                        float d = c.r - SegDist(v, c.a.position, c.b.position);
                        if (d <= 0f) continue;
                        string part = i < weights.Length ? s.bones[weights[i].boneIndex0].name.Replace("mixamorig:", "") : "?";
                        string key = part + "→" + c.name;
                        if (!worstByPart.TryGetValue(key, out float w) || d > w) worstByPart[key] = d;
                        if (d > worst) { worst = d; worstInfo = "binen " + part + " → taşıyan " + c.name + " (" + phase + ")"; }
                    }
                }
            }
            UnityEngine.Object.Destroy(baked);
        }

        public float Worst { get { return worst; } }

        public string Report()
        {
            var top = worstByPart.OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key + " " + (kv.Value * 100f).ToString("F1") + " cm");
            string leg = worstByPart.Where(kv => kv.Key.Contains("Leg") || kv.Key.Contains("Foot")).Select(kv => kv.Value).DefaultIfEmpty(0f).Max().ToString("F3");
            return samples + " örnek, en derin " + (worst * 100f).ToString("F1") + " cm (" + worstInfo + "); bacak/ayak (alçı dahil) en derin " + leg + " m; ilk 4: " + string.Join(", ", top);
        }

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
