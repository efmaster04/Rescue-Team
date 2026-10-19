using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// U3 (kullanıcı kararı "Can ve para" + gece kararları, 2026-10-19) Play modu kontrolleri (hastalar patikada yattıktan sonra):
//  (U3-2) eşya: valiz uzakta / sırtı dönük ipucu yok; ≤ 1.5 m yüz dönük "E: Valizi al" + vurgu; elde (Carry_OneHand), koşu serbest;
//         bırak / al; teslim noktasında sırtı dönük koyulmaz, yüz dönük "E: Güvenli yere koy" + halka vurgusu; içindekiler 0.4 sn
//         arayla uçan yazı, toplam doğru, HUD "₺ 1.240"; hasta eşyası al → koy
//  (U3-3) çalınma: ReportStolen → çubuk doğru oranda, kırmızı yazı; güvendeki eşya çalınamaz; %50'de görev başarısız (ekran)
//  (U3-1) can: darbe → HitReact + sendeleme, can düşer; 4 × 25 → düşer, elindeki sedye bırakılır; 15 sn'de çekici yanında %100;
//         arkadaş kaldırırsa %50; 6 sn hasarsız sonra 5/sn dolma; araç düşmesi canı ve geri sayımı etkilemez
//  (U3-4) yüz kuralı: sedye, koni, hastayı sedyeye al, arkadaşı kaldır — sırtı dönükken ipucu yok, yüz dönükken var
public static partial class Bolum1AnimVerify
{
    private static LootRegistry Loot { get { return LootRegistry.Instance; } }
    private static PlayerHealth Health { get { return player.GetComponent<PlayerHealth>(); } }
    private static SafeDropPoint SafeDrop { get { return SafeDropPoint.All.FirstOrDefault(); } }
    private static MissionHud Hud { get { return UnityEngine.Object.FindAnyObjectByType<MissionHud>(); } }

    private static IEnumerator PhaseU3()
    {
        var bot = Bot;
        bool savedAssist = bot.assistEnabled;
        bot.assistEnabled = false;
        ParkBot();
        Transform savedCamera = Player.cameraTransform;
        Player.cameraTransform = null;
        var savedStretchers = StretcherController.All.Select(s => (s, s.transform.position, s.transform.rotation)).ToList();
        if (Loot == null || Health == null || SafeDrop == null || Hud == null)
        {
            Check("(U3-0) sahnede LootRegistry, PlayerHealth, SafeDrop ve HUD var", false, "kayıt " + (Loot != null) + ", can " + (Health != null) + ", teslim " + (SafeDrop != null) + ", HUD " + (Hud != null));
            bot.assistEnabled = savedAssist;
            Player.cameraTransform = savedCamera;
            yield break;
        }

        yield return PhaseU3Loot();
        yield return PhaseU3StolenPartial();
        yield return PhaseU3Health();
        yield return PhaseU3Facing(savedStretchers);
        yield return PhaseU3Fail();

        // Toparla: eşyalar yerine, para/çalınan sıfır, görev yeniden (IsFinished kalkar), sedyeler eski yerine
        Loot.ResetForTest();
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        if (Player.IsDown) Player.knockdown.ResetStanding();
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);
        if (mateGo != null) mateGo.SetActive(false);
        bot.assistEnabled = savedAssist;
        Player.cameraTransform = savedCamera;
        yield return null;
    }

    private static string HlName() { return Highlight.Current != null ? Highlight.Current.name : "yok"; }

    // Hedefin etrafında d m ötede, yürünebilir (≤ 30°) zemin bulunan bir duruş noktası; dir dışa yön.
    private static Vector3 U3StandAround(Vector3 target, float d, out Vector3 dir)
    {
        Vector3 best = target + Vector3.back * d; dir = Vector3.back; float bestScore = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Vector3 o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = target + o * d;
            if (!GroundProbe.Find(new Vector3(p.x, target.y + 1.5f, p.z), 5f, out RaycastHit g)) continue;
            float score = g.normal.y * 10f - Mathf.Abs(g.point.y - target.y);
            if (score > bestScore) { bestScore = score; best = g.point + Vector3.up * 0.02f; dir = o; }
        }
        return best;
    }

    private static Vector3 U3Ground(Vector3 p, float refY)
    {
        if (GroundProbe.Find(new Vector3(p.x, refY + 1.5f, p.z), 6f, out RaycastHit g)) p.y = g.point.y + 0.02f;
        return p;
    }

    // ---- (U3-2) eşyalar ve para ----
    private static IEnumerator PhaseU3Loot()
    {
        var loot = Loot;
        var hud = Hud;
        var safe = SafeDrop;
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }

        // Toplam: bütün eşyaların toplamı; bileşim (valiz 5, hasta 3, ekipman: sedye 2 + halat 1 + koni 4)
        var all = LootItem.All.ToList();
        int sum = all.Sum(l => l.TotalValue);
        int luggage = all.Count(l => l.kind == LootKind.Luggage), pItems = all.Count(l => l.kind == LootKind.PatientItem), equip = all.Count(l => l.kind == LootKind.Equipment);
        var contentsOk = all.Where(l => l.kind == LootKind.Luggage).All(l => l.contents.Count >= 2 && l.contents.Count <= 4 && l.contents.Sum(c => c.value) >= 150 && l.contents.Sum(c => c.value) <= 450 && l.value == 20);
        Note("toplam", loot.TotalValue == sum && sum > 0 && luggage >= 2 && pItems == 3 && equip >= 7 && contentsOk,
            "kayıt " + loot.TotalValue + " = " + sum + " ₺; valiz " + luggage + ", hasta eşyası " + pItems + ", ekipman " + equip + "; valiz içerikleri 2–4 adet, 150–450 ₺, valiz 20 " + contentsOk
            + " [" + string.Join(" | ", all.Where(l => l.kind == LootKind.Luggage).Select(l => l.name + ": " + string.Join("+", l.contents.Select(c => c.name + " " + c.value)))) + "]");

        // Valiz
        var item = all.Where(l => l.kind == LootKind.Luggage).OrderBy(l => l.name).First();
        Vector3 c = item.Center;
        Vector3 standNear = U3StandAround(c, 1.0f, out Vector3 dir);
        Vector3 standFar = U3Ground(c + dir * 2.3f, c.y);
        Player.Teleport(standFar, Quaternion.LookRotation(-dir));
        yield return null; yield return null;
        Note("2.3 m uzakta", Interaction.CurrentHint != PlayerInteraction.HintTakeLuggage, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(standNear, Quaternion.LookRotation(dir));
        yield return null; yield return null;
        Note("sırtı dönük", Interaction.CurrentHint != PlayerInteraction.HintTakeLuggage && Highlight.Current != item.transform, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName());
        Player.Teleport(standNear, Quaternion.LookRotation(-dir));
        yield return null; yield return null;
        Note("yüz dönük (" + M(Flat(c - Player.transform.position).magnitude) + ")", Interaction.CurrentHint == PlayerInteraction.HintTakeLuggage && Highlight.Current == item.transform && Highlight.CopyCount > 0,
            "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName() + " (" + Highlight.CopyCount + " kopya)");
        var col = item.GetComponent<Collider>();
        Interaction.Interact();
        yield return null; yield return null;
        bool pickAnim = PlayerAnim.InState(CrewAnimation.StatePickUp);
        bool held = Player.HeldValuable == item && item.IsHeld && (col == null || !col.enabled);
        // Düz yerde taşıma kontrolü (patika), koşu serbest
        ParkCarriersOffLane();
        yield return Seconds(1.2f);
        bool carryAnim = PlayerAnim.InState(CrewAnimation.StateCarryCone);
        Player.SetRunInput(true);
        bool runAllowed = Mathf.Abs(Player.RequestedSpeed - Player.runSpeed) < 0.01f;
        Player.SetRunInput(false);
        Vector3 local = Player.transform.InverseTransformPoint(item.Center);
        Note("al / taşı", pickAnim && held && carryAnim && runAllowed && local.z > 0.1f && local.y > 0.2f,
            "PickUp " + pickAnim + ", elde " + held + ", CarryCone " + carryAnim + ", koşu serbest " + runAllowed + ", eşya kök uzayında " + V(local));
        // Bırak (hedefsiz), yerde; tekrar al
        Note("bırak ipucu (hedefsiz)", Interaction.CurrentHint == PlayerInteraction.HintDropLoot && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName());
        Interaction.Interact();
        yield return null; yield return null;
        if (col != null) col.enabled = false; // zemin ölçümünde eşyanın kendi collider'ı ışını kesmesin
        float pen = GroundProbe.Find(item.Center + Vector3.up * 0.5f, 3f, out RaycastHit dg) ? dg.point.y - item.WorldBounds.min.y : 99f;
        if (col != null) col.enabled = true;
        bool dropped = Player.HeldValuable == null && item.State == LootItem.LootState.Dropped && Mathf.Abs(pen) < 0.04f && (col == null || col.enabled);
        bool putDownAnim = PlayerAnim.InState(CrewAnimation.StatePutDown);
        Note("yere bırakıldı", dropped && putDownAnim, "durum " + item.State + ", zemine " + Cm(pen) + ", PutDown " + putDownAnim);
        yield return Seconds(1.1f);
        yield return null; yield return null;
        Note("yerden al", Interaction.CurrentHint == PlayerInteraction.HintTakeLuggage && Highlight.Current == item.transform, "ipucu \"" + Interaction.CurrentHint + "\"");
        Interaction.Interact();
        yield return Seconds(1.2f);
        // Teslim noktası
        Vector3 sd = safe.Center;
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName).transform;
        Vector3 away = Flat(sd - truck.position).normalized;
        Vector3 sdStand = U3Ground(sd + away * 1.0f, sd.y);
        Player.Teleport(sdStand, Quaternion.LookRotation(away));
        yield return null; yield return null;
        Note("teslimde sırtı dönük", Interaction.CurrentHint == PlayerInteraction.HintDropLoot && Highlight.Current == null, "ipucu \"" + Interaction.CurrentHint + "\"");
        Player.Teleport(sdStand, Quaternion.LookRotation(-away));
        yield return null; yield return null;
        Note("teslimde yüz dönük", Interaction.CurrentHint == PlayerInteraction.HintSecureLoot && Highlight.Current == safe.HighlightTarget && Highlight.CopyCount > 0,
            "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName());
        int money0 = loot.Money, spawned0 = hud.FloatingSpawned;
        int expect = item.TotalValue, n = 1 + item.contents.Count;
        Interaction.Interact();
        yield return null; yield return null;
        bool secured = item.IsSecured && !item.gameObject.activeSelf && Player.HeldValuable == null && loot.Money == money0 + item.value;
        yield return Seconds(0.55f);
        int floatsMid = hud.FloatingCount;
        Vector3 side = Vector3.Cross(Vector3.up, away);
        Vector3 pp = Player.transform.position;
        yield return CaptureWithHud("u3_valiz_teslim", pp + side * 2.6f + away * 1.4f + Vector3.up * 1.5f, sd + Vector3.up * 0.6f, 50f);
        float t0 = Time.time;
        yield return (Func<bool>)(() => loot.PendingCredits == 0 && loot.Money >= money0 + expect || Time.time - t0 > 5f);
        yield return Seconds(0.3f);
        float seq = Time.time - t0 + 0.55f + 2f * Time.deltaTime;
        bool moneyOk = loot.Money == money0 + expect;
        string hudMoney = hud.MoneyText.text;
        Note("güvenli yere kondu", secured && moneyOk && hud.FloatingSpawned - spawned0 == n && floatsMid >= 2 && hudMoney == LootRegistry.FormatMoney(loot.Money) && LootRegistry.FormatMoney(1240).EndsWith(" 1.240"),
            "güvende " + secured + ", para " + (loot.Money - money0) + "/" + expect + " (" + n + " kalem, uçan yazı " + (hud.FloatingSpawned - spawned0) + ", ortada görünen " + floatsMid + ", süre ~" + S(seq) + "), HUD \"" + hudMoney + "\"");
        Note("güvendeki eşya çalınamaz", !loot.ReportStolen(item) && loot.StolenValue == 0, "çalınan " + loot.StolenValue);
        // Hasta eşyası (patikada yatan hasta)
        var p = Mission.patients.FirstOrDefault(x => x.State == PatientState.LyingGround);
        var pi = all.FirstOrDefault(l => l.kind == LootKind.PatientItem && l.patient == p);
        if (p != null && pi != null)
        {
            Vector3 pc = pi.Center;
            Vector3 pStand = U3StandAround(pc, 1.2f, out Vector3 pdir);
            Player.Teleport(pStand, Quaternion.LookRotation(pdir));
            yield return null; yield return null;
            Note("hasta eşyası sırtı dönük", Interaction.CurrentHint != PlayerInteraction.HintTakePatientItem, "ipucu \"" + Interaction.CurrentHint + "\"");
            Player.Teleport(pStand, Quaternion.LookRotation(-pdir));
            yield return null; yield return null;
            Note("hasta eşyası yüz dönük", Interaction.CurrentHint == PlayerInteraction.HintTakePatientItem && Highlight.Current == pi.transform, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName() + " (hasta " + p.State + ")");
            Interaction.Interact();
            yield return null; yield return null;
            bool piHeld = Player.HeldValuable == pi && pi.transform.parent == null;
            int m1 = loot.Money;
            Player.Teleport(sdStand, Quaternion.LookRotation(-away));
            yield return null; yield return null;
            Interaction.Interact();
            yield return Seconds(0.8f);
            Note("hasta eşyası al → koy", piHeld && pi.IsSecured && loot.Money == m1 + pi.value, "elde " + piHeld + ", güvende " + pi.IsSecured + ", para +" + (loot.Money - m1) + " (" + pi.displayName + " " + pi.value + ")");
        }
        else Note("hasta eşyası", false, "patikada yatan hasta / eşyası yok");
        Check("(U3-2) değerli eşya: valiz uzakta/sırtı dönük ipucu yok, ≤ 1.5 m yüz dönük \"E: Valizi al\" + vurgu; tek elle (Carry_OneHand, koşu serbest); bırak/al; teslim noktasında yüz dönük \"E: Güvenli yere koy\" + halka vurgusu; valiz + içindekiler 0.4 sn arayla, toplam ve HUD doğru; güvendeki çalınamaz; hasta eşyası al → koy",
            ok, string.Join("; ", parts));
    }

    // ---- (U3-3a) kısmi çalınma: çubuk, kırmızı yazı ----
    private static IEnumerator PhaseU3StolenPartial()
    {
        var loot = Loot; var hud = Hud;
        var stretcherItem = LootItem.All.First(l => l.kind == LootKind.Equipment && l.displayName == "Sedye");
        int total = loot.TotalValue, v = stretcherItem.TotalValue, spawned0 = hud.FloatingSpawned;
        bool reported = loot.ReportStolen(stretcherItem);
        yield return null; yield return null;
        float frac = (float)v / total;
        bool barOk = Mathf.Abs(hud.StolenFill.fillAmount - frac) < 0.01f && hud.StolenPercent.text == "%" + Mathf.RoundToInt(frac * 100f);
        bool red = hud.FloatingSpawned == spawned0 + 1;
        Check("(U3-3a) çalınma: ReportStolen(Sedye) → çalınan/toplam oranında çubuk, \"-300 ₺ Sedye\" kırmızı yazı, görev sürüyor",
            reported && stretcherItem.IsStolen && loot.StolenValue == v && Mathf.Abs(loot.StolenFraction - frac) < 1e-4f && barOk && red && !Mission.IsFinished,
            "çalınan " + loot.StolenValue + "/" + total + " = %" + (frac * 100f).ToString("F1", Inv) + ", çubuk " + hud.StolenFill.fillAmount.ToString("F3", Inv) + " \"" + hud.StolenPercent.text + "\", yazı " + red + ", görev bitti " + Mission.IsFinished);
    }

    // ---- (U3-1) can ----
    private static IEnumerator PhaseU3Health()
    {
        var h = Health; var kd = Player.knockdown; var hud = Hud; var loot = Loot;
        var s0 = Stretcher(0);
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        Vector3 truckPos = GameObject.Find(Bolum1SceneBuilder.TowTruckName).transform.position;

        // Sedyeyi tut (bot kapalı: kalkmaz ama elde)
        TeleportPlayerToHandle(s0, false);
        yield return null; yield return null;
        Interaction.Interact();
        yield return Seconds(0.3f);
        bool holding = Player.HeldStretcher == s0;
        // 1 darbe: HitReact, sendeleme, can 75
        bool hit1 = h.TakeDamage(25f, null);
        yield return null; yield return null;
        bool hitAnim = PlayerAnim.InState(CrewAnimation.StateHitReact);
        bool noControl = !Player.HasControl;
        yield return Seconds(0.6f);
        bool controlBack = Player.HasControl;
        Note("darbe", hit1 && hitAnim && noControl && controlBack && Mathf.Abs(h.Current - 75f) < 0.01f && Mathf.Abs(hud.HealthFill.fillAmount - 0.75f) < 0.01f,
            "HitReact " + hitAnim + ", sendeleme (kontrol yok→geldi) " + noControl + "/" + controlBack + ", can " + h.Current.ToString("F1", Inv) + ", HUD çubuk " + hud.HealthFill.fillAmount.ToString("F2", Inv));
        Vector3 pp = Player.transform.position;
        Vector3 pf = Flat(Player.transform.forward).normalized;
        Vector3 ps = Vector3.Cross(Vector3.up, pf);
        yield return CaptureWithHud("u3_hud", pp - pf * 3.2f + ps * 1.8f + Vector3.up * 1.7f, pp + Vector3.up * 0.9f, 50f);
        // 3 darbe daha → düşer, sedye bırakılır, geri sayım
        for (int i = 0; i < 3; i++) { h.TakeDamage(25f, null); yield return Seconds(0.12f); }
        yield return null; yield return null;
        float rem0 = h.RespawnRemaining;
        bool down = holding && Player.IsDown && h.DownByHealth && h.Current <= 0f && Player.HeldStretcher == null && !s0.IsHeldBy(Player) && rem0 > 14f && rem0 <= 15f;
        float downAt = Time.time - (h.RespawnSeconds - rem0);
        yield return Seconds(1.0f);
        string respawnText = hud.RespawnText.text;
        bool textOk = respawnText == MissionHud.RespawnPrefix + Mathf.CeilToInt(h.RespawnRemaining) || respawnText == MissionHud.RespawnPrefix + (Mathf.CeilToInt(h.RespawnRemaining) + 1);
        Vector3 hips = kd.GroundPosition;
        yield return CaptureWithHud("u3_yeniden_dogma", hips + ps * 3.0f - pf * 1.0f + Vector3.up * 1.8f, hips + Vector3.up * 0.3f, 50f);
        Note("4 × 25 → düştü", down && textOk, "yerde " + Player.IsDown + ", can 0'la " + h.DownByHealth + ", sedye bırakıldı " + (Player.HeldStretcher == null) + ", geri sayım " + rem0.ToString("F1", Inv) + ", HUD \"" + respawnText + "\"");
        int r0 = h.Respawns;
        Time.timeScale = 2f;
        yield return Until(() => h.Respawns > r0 || Time.time - downAt > h.RespawnSeconds + 3f, 30f, "yeniden doğma");
        Time.timeScale = 1f;
        float elapsed = Time.time - downAt;
        yield return null; yield return null;
        float toPoint = Flat(Player.transform.position - h.respawnPoint.position).magnitude;
        float toTruck = Flat(Player.transform.position - truckPos).magnitude;
        Note("15 sn'de yeniden doğdu", h.Respawns == r0 + 1 && !Player.IsDown && Player.HasControl && Mathf.Abs(h.Current - h.Max) < 0.01f && toPoint < 0.5f && Mathf.Abs(elapsed - h.RespawnSeconds) < 0.35f && Player.GetComponent<CharacterController>().enabled && h.RespawnRemaining < 0f,
            "süre " + S(elapsed) + ", can " + h.Current.ToString("F0", Inv) + ", noktaya " + M(toPoint) + ", çekiciye " + M(toTruck) + ", ayakta/kontrol " + (!Player.IsDown && Player.HasControl));

        // Arkadaş kaldırır → %50 (arkadaşın yüz kuralı: sırtı dönükken ipucu yok)
        ParkCarriersOffLane();
        yield return Seconds(0.3f);
        h.TakeDamage(100f, null);
        yield return Seconds(1.2f);
        if (mateGo == null)
        {
            mateGo = UnityEngine.Object.Instantiate(Player.gameObject);
            mateGo.name = "TestMate";
            mateGo.GetComponent<PlayerInputReader>().enabled = false;
        }
        mateGo.SetActive(true);
        var mate = mateGo.GetComponent<ThirdPersonPlayer>();
        if (mate.IsDown) mate.knockdown.ResetStanding();
        var mateI = mateGo.GetComponent<PlayerInteraction>();
        hips = kd.GroundPosition;
        Vector3 chest = kd.ChestPosition;
        Vector3 mside = Vector3.Cross(Vector3.up, Flat(chest - hips).sqrMagnitude > 0.01f ? Flat(chest - hips).normalized : Vector3.forward);
        Vector3 mp = U3Ground(chest + mside * 0.7f, hips.y);
        mate.Teleport(mp, Quaternion.LookRotation(-Flat(chest - mp).normalized));
        yield return null; yield return null;
        string mateBack = mateI.CurrentHint;
        mate.Teleport(mp, Quaternion.LookRotation(Flat(chest - mp).normalized));
        yield return null; yield return null;
        string mateFace = mateI.CurrentHint;
        bool mateBarShown = hud.HealthBars.Any(b => b != null && b.health == mateGo.GetComponent<PlayerHealth>() && b.Visible);
        mateI.SetInteractHeld(true);
        yield return Until(() => !Player.IsDown, 5f, "arkadaş kaldırdı");
        mateI.SetInteractHeld(false);
        yield return null; yield return null;
        float afterRevive = h.Current;
        bool reviveOk = Mathf.Abs(afterRevive - h.Max * 0.5f) < 0.01f && !h.DownByHealth && h.RevivesWithHealth == 1 && h.RespawnRemaining < 0f;
        Note("arkadaş kaldırdı → %50", reviveOk && mateBack != PlayerInteraction.HintRevive && mateFace == PlayerInteraction.HintRevive && mateBarShown,
            "can " + afterRevive.ToString("F1", Inv) + ", arkadaş ipucu sırtı dönük \"" + mateBack + "\" / yüz dönük \"" + mateFace + "\", arkadaşın baş üstü can barı " + mateBarShown);
        mateGo.SetActive(false);
        yield return (Func<bool>)(() => Player.HasControl);
        // Dolma: 6 sn beklemede sabit, sonra 5/sn
        float standAt = h.LastDamageTime;
        Time.timeScale = 3f;
        yield return (Func<bool>)(() => Time.time - standAt >= 5.5f);
        float at5 = h.Current;
        yield return (Func<bool>)(() => Time.time - standAt >= 10f);
        Time.timeScale = 1f;
        float at10 = h.Current;
        float expected10 = afterRevive + (10f - (h.rules != null ? h.rules.regenDelay : 6f)) * (h.rules != null ? h.rules.regenPerSecond : 5f);
        Note("dolma", Mathf.Abs(at5 - afterRevive) < 0.5f && Mathf.Abs(at10 - expected10) < 2.5f,
            "5.5 sn'de " + at5.ToString("F1", Inv) + " (beklenen " + afterRevive.ToString("F0", Inv) + "), 10 sn'de " + at10.ToString("F1", Inv) + " (beklenen " + expected10.ToString("F0", Inv) + ")");
        // Araç çarpması kuralı değişmedi: can aynı, geri sayım yok
        float before = h.Current;
        Player.KnockDown(-Player.transform.forward * 2f + Vector3.up * 1f);
        yield return Seconds(0.5f);
        bool vehicleRule = Player.IsDown && !h.DownByHealth && h.RespawnRemaining < 0f && Mathf.Abs(h.Current - before) < 3f && hud.RespawnText.text == "";
        Note("araç düşmesi canı/geri sayımı etkilemez", vehicleRule, "yerde " + Player.IsDown + ", can 0'la " + h.DownByHealth + ", geri sayım " + h.RespawnRemaining.ToString("F1", Inv) + ", can " + h.Current.ToString("F1", Inv));
        kd.ResetStanding();
        yield return null;
        Check("(U3-1) can: darbe → HitReact + sendeleme, can düşer (HUD çubuk); 4 × 25 → düşer, elindeki sedye bırakılır, \"Yeniden doğma: N\"; " + h.RespawnSeconds.ToString(Inv) + " sn'de çekici yanındaki noktada %100; arkadaş kaldırırsa %50 (arkadaşta yüz kuralı, baş üstü bar); 6 sn sonra 5/sn dolma; araç düşmesi canı etkilemez",
            ok, string.Join("; ", parts));
    }

    // ---- (U3-4) yüz kuralı: sedye, koni, hastayı sedyeye al ----
    private static IEnumerator PhaseU3Facing(List<(StretcherController, Vector3, Quaternion)> savedStretchers)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        ParkCarriersOffLane();
        // Sedye sapı
        var s0 = Stretcher(0);
        TeleportPlayerToHandle(s0, false);
        yield return null; yield return null;
        string faceGrab = Interaction.CurrentHint;
        Player.Teleport(Player.transform.position, Player.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
        yield return null; yield return null;
        Note("sedye", faceGrab == PlayerInteraction.HintGrab && Interaction.CurrentHint != PlayerInteraction.HintGrab && Highlight.Current == null, "yüz \"" + faceGrab + "\" / sırt \"" + Interaction.CurrentHint + "\" vurgu " + HlName());
        // Koni
        ParkCarriersOffLane();
        var lane = Traffic.eastBound;
        float sK = lane.Length * 0.3f;
        Vector3 basePt = lane.PointAt(sK);
        Vector3 fwd = Flat(lane.TangentAt(sK)).normalized;
        var c3 = Cone(2);
        Vector3 cPos0 = c3.BasePoint; float cYaw0 = c3.target.eulerAngles.y;
        c3.PlaceAt(basePt + fwd * 0.9f, 0f);
        Player.Teleport(basePt + Vector3.up * 0.02f, Quaternion.LookRotation(fwd));
        yield return Seconds(0.3f);
        string faceCone = Interaction.CurrentHint;
        Player.Teleport(basePt + Vector3.up * 0.02f, Quaternion.LookRotation(-fwd));
        yield return null; yield return null;
        Note("koni", faceCone == PlayerInteraction.HintPickCone && Interaction.CurrentHint != PlayerInteraction.HintPickCone && Highlight.Current == null, "yüz \"" + faceCone + "\" / sırt \"" + Interaction.CurrentHint + "\"");
        c3.PlaceAt(cPos0, cYaw0);
        // Hastayı sedyeye al
        var p = Mission.patients.FirstOrDefault(x => x.State == PatientState.LyingGround);
        if (p != null)
        {
            var s1 = Stretcher(1);
            Vector3 hips = p.Position;
            Vector3 side = Vector3.back;
            Vector3 at = hips + side * 0.8f;
            if (GroundProbe.Find(new Vector3(at.x, hips.y + 1.5f, at.z), 5f, out RaycastHit g)) at.y = g.point.y;
            Vector3 anchorLocal = s1.transform.InverseTransformPoint(s1.patientAnchor.position);
            Quaternion rot = Quaternion.LookRotation(Vector3.right, Vector3.up);
            s1.Teleport(at - rot * new Vector3(anchorLocal.x, 0f, anchorLocal.z), rot);
            Vector3 pp = hips + side * 1.4f;
            if (GroundProbe.Find(new Vector3(pp.x, hips.y + 1.5f, pp.z), 5f, out g)) pp.y = g.point.y + 0.02f;
            Player.Teleport(pp, Quaternion.LookRotation(side));
            yield return null; yield return null;
            string backHint = Interaction.CurrentHint;
            Player.Teleport(pp, Quaternion.LookRotation(-side));
            yield return null; yield return null;
            Note("hastayı sedyeye al", backHint != PlayerInteraction.HintLoadPatient && Interaction.CurrentHint == PlayerInteraction.HintLoadPatient && Highlight.Current == p.transform,
                "sırt \"" + backHint + "\" / yüz \"" + Interaction.CurrentHint + "\" vurgu " + HlName());
            foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);
        }
        else Note("hastayı sedyeye al", false, "yerde hasta yok");
        Check("(U3-4) yüz kuralı (~60° koni, mesafeler aynı): sedye sapı, koni, hastayı sedyeye al — sırtı dönükken ipucu ve vurgu yok, yüz dönükken var (arkadaşı kaldır U3-1'de, bagaj kapısı U3-f'te, eşya/teslim U3-2'de, halat/dolap/kangal U1–U2'de)",
            ok, string.Join("; ", parts));
    }

    // ---- (U3-3b) %50 → görev başarısız ----
    private static IEnumerator PhaseU3Fail()
    {
        var loot = Loot; var hud = Hud;
        ParkCarriersOffLane();
        float limit = loot.values != null ? loot.values.failFraction : 0.5f;
        float lastBelow = loot.StolenFraction;
        bool finishedEarly = false;
        int stolen = 0;
        foreach (var l in LootItem.All.ToList().OrderBy(l => l.kind == LootKind.PatientItem ? 1 : 0).ThenBy(l => l.name))
        {
            if (loot.StolenFraction >= limit) break;
            if (l == null || l.IsSecured || l.IsStolen) continue;
            if (Mission.IsFinished) { finishedEarly = true; break; }
            lastBelow = loot.StolenFraction;
            if (loot.ReportStolen(l)) stolen++;
        }
        yield return null; yield return null;
        string result = hud.ResultText.text;
        Vector3 pp = Player.transform.position;
        yield return CaptureWithHud("u3_basarisiz", pp - Vector3.right * 3.2f + Vector3.forward * 1.5f + Vector3.up * 1.8f, pp + Vector3.up * 0.9f, 50f);
        Check("(U3-3b) çalınan toplamın %" + Mathf.RoundToInt(limit * 100f) + "'sine ulaşınca görev başarısız: \"" + LootRegistry.FailText + "\" + \"" + MissionHud.RestartText + "\" ekranda; eşikten önce görev sürüyordu",
            !finishedEarly && lastBelow < limit && loot.StolenFraction >= limit && Mission.IsFinished && !Mission.Succeeded && Mission.ResultMessage == LootRegistry.FailText && result.StartsWith(LootRegistry.FailText) && result.Contains(MissionHud.RestartText),
            "eşikten önce %" + (lastBelow * 100f).ToString("F1", Inv) + " → %" + (loot.StolenFraction * 100f).ToString("F1", Inv) + " (" + stolen + " eşya çalındı, çalınan " + loot.StolenValue + "/" + loot.TotalValue + "), görev bitti " + Mission.IsFinished + ", HUD \"" + result.Replace("\n", " | ") + "\"");
    }
}
