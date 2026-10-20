using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// U4 (kullanıcı kararı "Hırsızlar" + "Ek kararlar", 2026-10-19) Play modu kontrolleri (hastalar patikada, U3'ten sonra):
//  (U4-0) sahne kurulumu ve ayarlar (180 / 300 sn, 1→2→3→4→5→5, kamyonet 90 sn)
//  (U4-1) dalga 1 GERÇEK zamanlamayla (hızlandırılmış): uyarı 175, kamyonet 180, durağa çekilir, park (şerit açık; arkadan araç geçer), 1 hırsız iner
//  (U4-2) kapma → taşıma (etiket) → kamyonete atma → ReportStolen, eşya kaybolur
//  (U4-3) kamyonet bekleme dolunca kalkar, geride kalan hırsız kaçar, kamyonet kaybolur
//  (U4-4) dalga sayıları (kısa aralıklı 2. dalgada 2 hırsız)
//  (U4-5) savunma: yumruk 25 + bekleme, elden kapma, itme, tackle, fırlatma (+ boşluğa düşen eşya geri döner), düdük + bekleme, sedye taşırken saldırı yok
//  (U4-6) kurallar: hastalı sedye ve bağlı halat çalınmaz; hasta eşyası kapılır; boş sedye sürüklenir ve çalınınca kaybolur
//  (U4-7) tavan valizleri bagaj kapısıyla düşer, LootItem olur, toplam güncellenir, alınabilir
//  (U4-8) %50 çalınınca görev başarısız (5 gerçek hırsız, hızlandırılmış)
public static partial class Bolum1AnimVerify
{
    private static ThiefDirector Thieves { get { return ThiefDirector.Instance; } }
    private static PlayerCombat Combat { get { return player.GetComponent<PlayerCombat>(); } }
    private static Transform Truck { get { return GameObject.Find(Bolum1SceneBuilder.TowTruckName).transform; } }

    private static ThiefSettings TestThiefSettings(float first, float interval, float vanWait = 90f, int maxThieves = 5)
    {
        var s = UnityEngine.Object.Instantiate(Bolum1ThiefBuilder.Settings);
        s.firstWaveSeconds = first;
        s.waveIntervalSeconds = interval;
        s.vanWaitSeconds = vanWait;
        s.maxThieves = maxThieves;
        s.useFixedSeed = true;
        return s;
    }

    private static void FaceLabels(Camera cam)
    {
        foreach (var l in UnityEngine.Object.FindObjectsByType<ThiefLabel>(FindObjectsSortMode.None)) l.FaceCamera(cam);
    }

    // Test kurulumu: hırsızın yalnız istenen eşyaları görmesi için öbür eşyalar geçici kapatılır (LootItem.All yalnız etkinleri sayar).
    private static List<LootItem> HideLootExcept(params LootItem[] keep)
    {
        var hidden = new List<LootItem>();
        foreach (var l in LootItem.All.ToArray())
        {
            if (l == null || Array.IndexOf(keep, l) >= 0) continue;
            l.gameObject.SetActive(false);
            hidden.Add(l);
        }
        return hidden;
    }

    private static void RestoreLoot(List<LootItem> hidden)
    {
        foreach (var l in hidden) if (l != null) l.gameObject.SetActive(true);
        hidden.Clear();
    }

    // Şerit koordinatı: duraktan ds m ileride (kamyonetin gidiş yönü), şerit orta çizgisinden lateral m sağda, zeminde
    private static Vector3 LanePoint(ThiefDirector dir, float ds, float lateral)
    {
        var lane = dir.lane;
        float s = Mathf.Clamp(dir.StopS + ds, 0f, lane.Length);
        Vector3 p = lane.PointAt(s) + lane.RightAt(s) * lateral;
        return U3Ground(p, p.y);
    }

    private static Vector3 LaneDir(ThiefDirector dir, float ds)
    {
        return Flat(dir.lane.TangentAt(Mathf.Clamp(dir.StopS + ds, 0f, dir.lane.Length))).normalized;
    }

    // Çıkış tarafından (banket ya da yol) kamyonetin arka ucuna bakan kamera konumu
    private static Vector3 VanCaptureCamera(ThiefVan van, out Vector3 look)
    {
        Vector3 vf = Flat(van.transform.forward).normalized, vr = Vector3.Cross(Vector3.up, vf);
        Vector3 exit = van.RearPoint;
        Vector3 bumper = van.transform.position - vf * van.Follower.rearOverhang;
        float side = Mathf.Sign(Vector3.Dot(exit - van.transform.position, vr));
        if (Mathf.Abs(side) < 0.5f) side = 1f;
        look = (bumper + exit) * 0.5f + Vector3.up * 0.9f;
        return bumper + vr * side * 5.5f + vf * 1.0f + Vector3.up * 2.1f;
    }

    private static string ThiefAnim(Thief t)
    {
        if (t == null || t.animator == null || !t.animator.isActiveAndEnabled) return "-";
        var info = t.animator.GetCurrentAnimatorStateInfo(0);
        foreach (var n in new[] { Thief.StateMove, Thief.StateMoveCarry, Thief.StateGrabAnim, Thief.StatePunchAnim, Thief.StateStunnedAnim, Thief.StateThrowAnim, Thief.StateGetUpAnim })
            if (info.IsName(n)) return n;
        return "?";
    }

    private static bool ThiefIn(Thief t, string state)
    {
        if (t == null || t.animator == null || !t.animator.isActiveAndEnabled) return false;
        if (t.animator.GetCurrentAnimatorStateInfo(0).IsName(state)) return true;
        return t.animator.IsInTransition(0) && t.animator.GetNextAnimatorStateInfo(0).IsName(state);
    }

    private static IEnumerator PhaseU4()
    {
        var bot = Bot;
        bool savedAssist = bot.assistEnabled;
        bot.assistEnabled = false;
        ParkBot();
        Transform savedCamera = Player.cameraTransform;
        Player.cameraTransform = null;
        var savedStretchers = StretcherController.All.Select(s => (s, s.transform.position, s.transform.rotation)).ToList();
        var dir = Thieves;
        var combat = Combat;
        var roof = UnityEngine.Object.FindAnyObjectByType<MinivanRoofLuggage>();
        Loot.ResetForTest();
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        Health.SetHealthForTest(Health.Max);
        var asset = Bolum1ThiefBuilder.Settings;
        bool u4 = dir != null && dir.settings != null && dir.vanStop != null && dir.vanPrefab != null && dir.thiefPrefabs.Length == 3 && dir.thiefPrefabs.All(p => p != null)
            && dir.navMeshData != null && dir.NavMeshLoaded && combat != null && combat.settings != null && roof != null && roof.suitcases.Length == 2 && asset != null;
        string counts = asset != null ? string.Join(",", Enumerable.Range(0, 6).Select(k => asset.ThievesForWave(k))) : "-";
        float truckDist = dir != null && dir.vanStop != null ? Flat(dir.vanStop.position - Truck.position).magnitude : -1f;
        Check("(U4-0) sahne: ThiefDirector + ThiefVanStop (bankette, çekiciye ≥ 4 m; brief ~15–25 m istedi, şerit dışı düz yer yalnız çekici slotunda) + kamyonet ve 3 hırsız prefab'ı + hırsız NavMesh'i (eğim ≤ 35°) yüklü; oyuncuda PlayerCombat; minibüste MinivanRoofLuggage (2 valiz); ayar ilk dalga 180 sn, aralık 300 sn, 1→2→3→4→5→5, kamyonet 90 sn",
            u4 && asset.firstWaveSeconds == 180f && asset.waveIntervalSeconds == 300f && counts == "1,2,3,4,5,5" && asset.vanWaitSeconds == 90f && truckDist >= 4f && truckDist <= 26f,
            "yönetmen " + (dir != null) + ", durak " + (dir != null && dir.vanStop != null ? V(dir.vanStop.position) + " çekiciye " + M(truckDist) : "-") + ", NavMesh " + (dir != null && dir.navMeshData != null ? dir.navMeshData.name + " yüklü " + dir.NavMeshLoaded : "yok") + ", savunma " + (combat != null)
            + ", tavan valizi " + (roof != null ? roof.suitcases.Length : 0) + "; ayar " + (asset != null ? asset.firstWaveSeconds + "/" + asset.waveIntervalSeconds + " sn, " + counts + ", bekleme " + asset.vanWaitSeconds : "-"));
        if (u4)
        {
            yield return PhaseU4Wave(dir);
            yield return PhaseU4Defense(dir);
            yield return PhaseU4Rules(dir);
            yield return PhaseU4Roof(roof);
            yield return PhaseU4Fail(dir);
        }
        // Toparla
        if (dir != null) dir.Begin(TestThiefSettings(99999f, 99999f));
        foreach (var t in Thief.All.ToArray()) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
        if (Player.IsDown) Player.knockdown.ResetStanding();
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        if (Player.HeldStretcher != null) Interaction.Interact();
        Loot.ResetForTest();
        // Tavan valizleri minibüsün arkasına (patikaya) indi; sonraki sedye fazları orada yürür (yön kilidi valizi dik zemin sayar) → kapat
        if (roof != null)
        {
            Line("  U4 temizlik: tavan valizi " + roof.Items.Count + " adet sahneden kaldırıldı (" + string.Join(", ", roof.Items.Where(i => i != null).Select(i => i.name + " " + V(i.transform.position))) + ")");
            foreach (var it in roof.Items) if (it != null) UnityEngine.Object.Destroy(it.gameObject);
            roof.Items.Clear();
            yield return null;
            Loot.ComputeTotal();
        }
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        Health.SetHealthForTest(Health.Max);
        foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);
        // U4-6 hastayı 1.5 m kaydırmıştı: sonraki sedye fazları (b1s yürüyüş hattı) hastaları LayOnPath yerlerinde bekler
        Bolum1TestPatients.LayOnPath(Mission.patients);
        if (mateGo != null) mateGo.SetActive(false);
        Time.timeScale = 1f;
        BeforeCapture = null;
        bot.assistEnabled = savedAssist;
        Player.cameraTransform = savedCamera;
        yield return null;
    }

    // ---- (U4-1..4) dalga, kapma, kalkış, sayılar ----
    private static IEnumerator PhaseU4Wave(ThiefDirector dir)
    {
        var lane = dir.lane;
        var truck = Truck;
        ParkCarriersOffLane();
        const float testWait = 60f;
        dir.Begin(TestThiefSettings(180f, 300f, testWait)); // gerçek zamanlama; kamyonet 60 sn bekler (test), ayar 90
        // Hedef yok (bütün eşyalar geçici kapalı; yoksa hırsız çekicinin yanındaki ekipmanı 1 sn'de kapıyor ya da yola fırlayıp araca çarpılıyor).
        // Test valizi (U4-2) araç geçtikten sonra yola konur.
        var item = LootItem.All.Where(l => l.kind == LootKind.Luggage && l.CanPickUp).OrderBy(l => l.name).First();
        var hidden = HideLootExcept();
        float warnAt = -1f;
        Time.timeScale = 10f;
        yield return Until(() => { if (warnAt < 0f && dir.WarningActive) warnAt = dir.Elapsed; return dir.Vans.Count > 0 || dir.Elapsed > 215f; }, 230f, "ilk kamyonet");
        float spawnAt = dir.Elapsed;
        var van = dir.ActiveVan;
        var vanEvents = new List<string>();
        if (van != null)
        {
            van.Parked += v => vanEvents.Add("park " + S(dir.Elapsed));
            van.Departed += v => vanEvents.Add("kalkış " + S(dir.Elapsed));
            van.Despawned += v => vanEvents.Add("yok oldu " + S(dir.Elapsed) + " faz " + v.Phase + " s " + (v.Follower != null ? v.Follower.Distance.ToString("F1", Inv) : "-"));
        }
        string hudWarn = Hud.ThiefWarningText.text;
        string expectWarn = string.Format(dir.Active.waveWarningFormat, 1);
        bool warnOk = warnAt >= 174.5f && warnAt <= 176f && hudWarn == expectWarn && GameNotice.LastMessage == expectWarn;
        bool spawnOk = van != null && spawnAt >= 179.9f && spawnAt <= 181.5f && dir.WaveLog.Count == 1 && dir.WaveLog[0].thieves == 1;
        Time.timeScale = 4f;
        yield return Until(() => van == null || van.IsParked, 80f, "kamyonet park");
        Time.timeScale = 1f;
        bool parked = van != null && van.IsParked;
        float lat = 0f, s = 0f, truckD = -1f;
        bool outsideLane = false, unregistered = false, nearStop = false, doorsOpen = false;
        if (parked)
        {
            s = lane.Project(van.transform.position, out lat, out _);
            outsideLane = Mathf.Abs(lat) - van.Vehicle.bodySize.x * 0.5f >= lane.width * 0.5f - 0.05f;
            unregistered = !LaneOccupancy.Followers.Contains(van.Follower);
            nearStop = Mathf.Abs(s - dir.StopS) < 0.6f;
            truckD = Flat(van.transform.position - truck.position).magnitude;
            yield return Seconds(0.8f);
            doorsOpen = van.DoorAmount > 0.99f;
        }
        // Şerit açık: arkadan gelen araç yavaşlamadan geçer
        TrafficVehicle car = null;
        float minSpeedNear = 99f;
        bool passed = false;
        if (parked)
        {
            bool east = lane == Traffic.eastBound;
            yield return Until(() => car != null || (car = Traffic.ForceSpawn(east, TrafficVehicleType.Sedan)) != null, 10f, "geçen araç");
            Time.timeScale = 4f;
            yield return (Func<bool>)(() =>
            {
                if (car != null && Mathf.Abs(car.Follower.Distance - dir.StopS) < 12f) minSpeedNear = Mathf.Min(minSpeedNear, car.Follower.Speed);
                return car == null || car.Follower.Distance > dir.StopS + 12f;
            });
            Time.timeScale = 1f;
            passed = car != null && car.Follower.Distance > dir.StopS + 12f && minSpeedNear > 4f;
        }
        // Hırsız iner
        Thief thief = null;
        bool oneThief = false, sneak = false;
        if (parked)
        {
            yield return Until(() => van == null || van.Spawned >= 1, 10f, "hırsız indi");
            thief = van != null && van.Thieves.Count > 0 ? van.Thieves[0] : null;
            yield return Seconds(0.7f);
            if (van == null)
            {
                Check("(U4-1) kamyonet hırsız indirdikten hemen sonra yok oldu", false, "olaylar: " + string.Join(" | ", vanEvents) + "; hırsız " + (thief != null ? thief.State.ToString() : "yok"));
                BeforeCapture = null;
                dir.Begin(TestThiefSettings(99999f, 99999f));
                yield break;
            }
            oneThief = van.PlannedThieves == 1 && van.Spawned == 1 && Thief.All.Count == 1 && thief != null && !thief.IsGone;
            sneak = ThiefIn(thief, Thief.StateMove);
            BeforeCapture = FaceLabels;
            // Çıkış tarafından, kamyonetin arka ucu hizasından: kapılar, iniş noktası ve hırsız
            Vector3 camPos = VanCaptureCamera(van, out Vector3 camLook);
            yield return Capture("u4_kamyonet", camPos, camLook, 55f);
        }
        Check("(U4-1) dalga 1 (gerçek zamanlama, hızlandırılmış): uyarı " + (dir.Active.firstWaveSeconds - dir.Active.warnLeadSeconds).ToString(Inv) + ". sn'de HUD + bildirim; kamyonet 180. sn'de şeride çıkar, ThiefVanStop'a çekilir (bankette, ambulans durağından uzak), park edince şerit kaydından çıkar ve gövdesi şeridin dışında kalır; arkadan gelen araç yavaşlamadan geçer; kapılar açılır, 1 hırsız iner (Thief_Sneak_Walk)",
            warnOk && spawnOk && parked && outsideLane && unregistered && nearStop && doorsOpen && passed && oneThief && sneak && truckD >= 4f && truckD <= 27f,
            "uyarı " + S(warnAt) + " \"" + hudWarn + "\"; kamyonet " + S(spawnAt) + "; park " + parked + " (s " + s.ToString("F1", Inv) + " / durak " + dir.StopS.ToString("F1", Inv) + ", yanal " + lat.ToString("F2", Inv) + " m, şerit dışı " + outsideLane
            + ", kayıt dışı " + unregistered + ", çekiciye " + M(truckD) + ", kapılar " + doorsOpen + ", iniş " + (van != null && van.SideExit ? "yan kapı (arka kapalı)" : "arka") + "); geçen araç " + passed + " (en düşük hız " + minSpeedNear.ToString("F1", Inv) + " m/sn); hırsız " + oneThief + " anim " + ThiefAnim(thief)
            + (thief != null ? " (durum " + thief.State + ", etkin " + thief.gameObject.activeInHierarchy + ", animator " + (thief.animator != null ? thief.animator.enabled + "/" + thief.animator.isActiveAndEnabled : "yok") + ", hedef " + (thief.Target != null ? thief.Target.name : "-") + ", yol " + thief.PathStatus + ", konum " + V(thief.transform.position) + ", iniş " + (van != null ? V(van.RearPoint) : "-") + ")" : ""));
        if (!parked || thief == null) { BeforeCapture = null; RestoreLoot(hidden); yield break; }

        // (U4-2) kapma: valiz kamyonetin 7 m önünde, şeritte (iniş noktasından ~8 m)
        item.gameObject.SetActive(true);
        item.Drop(LanePoint(dir, 7f, 0.5f), 0f);
        thief.ResetForTest();
        thief.ForceTarget(item);
        float tg0 = Time.time;
        yield return (Func<bool>)(() => thief == null || thief.Carried == item || thief.IsGone || Time.time - tg0 > 25f);
        if (thief == null || thief.Carried != item)
        {
            Check("(U4-2) kapma: hırsız valizi kapamadı", false, thief == null ? "hırsız yok (kamyonet " + (van != null ? van.Phase.ToString() : "yok") + ")" : "durum " + thief.State + ", hedef " + (thief.Target != null ? thief.Target.name : "-") + ", konum " + V(thief.transform.position) + " eşya " + V(item.Center)
                + ", yol " + thief.PathStatus + " kalan " + M(thief.RemainingDistance) + ", takılma " + S(thief.StuckTime) + ", vazgeçme " + thief.StuckGiveUps + " (son: " + thief.LastBlock + ")"
                + (van != null ? ", iniş noktası " + V(van.RearPoint) + " yan " + van.SideExit + " kamyonet " + van.Phase : ", kamyonet yok"));
            BeforeCapture = null;
            RestoreLoot(hidden);
            dir.Begin(TestThiefSettings(99999f, 99999f));
            yield break;
        }
        bool grabbed = thief.Carried == item && item.IsHeldByThief && !item.CanPickUp && thief.Grabs == 1;
        string labelText = item.LabelText;
        yield return Seconds(0.35f);
        var label = UnityEngine.Object.FindObjectsByType<ThiefLabel>(FindObjectsSortMode.None).FirstOrDefault(l => l.thief == thief);
        bool labelOk = label != null && label.Visible && label.Text.text == labelText && labelText.Contains(LootRegistry.MoneySymbol);
        bool carryAnim = ThiefIn(thief, Thief.StateMoveCarry) || ThiefIn(thief, Thief.StateGrabAnim);
        Vector3 local = thief.transform.InverseTransformPoint(item.Center);
        Vector3 tp = thief.transform.position, tf = Flat(thief.transform.forward).normalized;
        yield return Capture("u4_kapma", tp + Vector3.Cross(Vector3.up, tf) * 2.6f - tf * 1.4f + Vector3.up * 1.7f, tp + Vector3.up * 1.0f, 50f);
        int stolen0 = Loot.StolenValue, floats0 = Hud.FloatingSpawned;
        float tt0 = Time.time;
        yield return (Func<bool>)(() => thief == null || item.IsStolen || thief.IsGone || Time.time - tt0 > 25f);
        yield return null;
        string throwDiag = thief != null ? "durum " + thief.State + ", konum " + V(thief.transform.position) + (van != null ? ", iniş noktası " + V(van.RearPoint) + " (yan " + van.SideExit + "), gövdeye " + M(van.DistanceToBody(thief.transform.position)) : ", kamyonet yok") + ", yol " + thief.PathStatus + " kalan " + M(thief.RemainingDistance) + ", takılma " + S(thief.StuckTime) + " (son: " + thief.LastBlock + ")" : "hırsız yok";
        bool stolenOk = item.IsStolen && !item.gameObject.activeSelf && Loot.StolenValue == stolen0 + item.TotalValue && van != null && van.LootCount == 1 && Hud.FloatingSpawned == floats0 + 1 && thief != null && thief.Throws == 1;
        Check("(U4-2) kapma: hırsız 4.5 m'deki valizi hedefler, Thief_Grab ile kapar (elde; oyuncu alamaz), başının üstünde \"" + labelText + "\", Thief_Run_Carry ile kamyonete koşar, Thief_ThrowInVan → LootRegistry.ReportStolen (−₺ yazısı), eşya kaybolur",
            grabbed && labelOk && carryAnim && local.z > 0.1f && local.y > 0.6f && stolenOk,
            "kaptı " + grabbed + ", etiket " + labelOk + " (\"" + (label != null ? label.Text.text : "-") + "\"), taşıma anim " + carryAnim + " (" + ThiefAnim(thief) + "), eşya kök uzayında " + V(local) + "; çalındı " + item.IsStolen + " gizli " + !item.gameObject.activeSelf
            + ", çalınan +" + (Loot.StolenValue - stolen0) + "/" + item.TotalValue + ", kamyonette " + (van != null ? van.LootCount : -1) + ", uçan yazı +" + (Hud.FloatingSpawned - floats0) + (item.IsStolen ? "" : "; TANI: " + throwDiag));
        if (!item.IsStolen || van == null) { BeforeCapture = null; RestoreLoot(hidden); dir.Begin(TestThiefSettings(99999f, 99999f)); yield break; }

        // (U4-3) kalkış: bekleme (test 60 sn) dolunca; hırsız (öbür eşyalar açık: çalmaya devam eder, binmez) kaçar; kamyonet kaybolur
        RestoreLoot(hidden);
        thief.ResetForTest();
        Time.timeScale = 4f;
        yield return Until(() => van == null || van.Phase == ThiefVan.VanPhase.Departing, 100f, "kamyonet kalktı");
        float departAt = van != null ? van.ParkedTime : -1f;
        bool reRegistered = van != null && LaneOccupancy.Followers.Contains(van.Follower);
        float tfl = Time.time;
        yield return (Func<bool>)(() => thief == null || thief.IsGone || thief.State == Thief.ThiefState.Flee || Time.time - tfl > 20f);
        bool thiefFlee = thief == null || thief.State == Thief.ThiefState.Flee || thief.IsGone;
        string fleeInfo = thief != null ? thief.State.ToString() : "yok";
        float tf0 = Time.time;
        yield return (Func<bool>)(() => thief == null || Time.time - tf0 > 60f);
        bool fled = thief == null;
        yield return Until(() => van == null, 90f, "kamyonet kayboldu");
        Time.timeScale = 1f;
        Check("(U4-3) kamyonet bekleme süresi dolunca (test " + testWait.ToString(Inv) + " sn; ayar " + Bolum1ThiefBuilder.Settings.vanWaitSeconds.ToString(Inv) + " sn) kapıları kapatıp şeride kaydolur ve kalkar; geride kalan hırsız panikle kaçıp kaybolur; kamyonet şerit sonunda kaybolur",
            departAt >= testWait - 0.5f && departAt <= testWait + 4f && reRegistered && thiefFlee && fled && van == null,
            "kalkış park süresi " + S(departAt) + ", şeride kaydoldu " + reRegistered + ", hırsız kaçış " + thiefFlee + " (" + fleeInfo + ") / kayboldu " + fled + (thief != null ? " (durum " + thief.State + ", konum " + V(thief.transform.position) + ", yol " + thief.PathStatus + ")" : "") + ", kamyonet kayboldu " + (van == null));

        // (U4-4) dalga sayıları: kısa aralık, 2. dalga 2 hırsız (eşyalar yine açık)
        RestoreLoot(hidden);
        ParkCarriersOffLane();
        dir.Begin(TestThiefSettings(3f, 15f, 4f));
        Time.timeScale = 8f;
        yield return Until(() => dir.WaveLog.Count >= 2 || dir.Elapsed > 60f, 70f, "iki dalga");
        var van2 = dir.ActiveVan;
        yield return Until(() => van2 == null || van2.Spawned >= 2 || (van2.IsParked && van2.ParkedTime > 6f), 80f, "2. dalga indi");
        Time.timeScale = 1f;
        float gap = dir.WaveLog.Count >= 2 ? dir.WaveLog[1].spawnTime - dir.WaveLog[0].spawnTime : -1f;
        bool countsOk = dir.WaveLog.Count >= 2 && dir.WaveLog[0].thieves == 1 && dir.WaveLog[1].thieves == 2 && van2 != null && van2.Spawned == 2 && gap >= 14.5f && gap <= 25f;
        Check("(U4-4) dalga sayıları 1 → 2 → 3 → 4 → 5 → 5 (ayar); kısa aralıklı testte 2. dalga 15 sn sonra geldi ve 2 hırsız indirdi",
            countsOk && string.Join(",", Enumerable.Range(0, 6).Select(k => dir.Active.ThievesForWave(k))) == "1,2,3,4,5,5",
            "dalga kaydı " + string.Join(" | ", dir.WaveLog.Select(w => "#" + (w.index + 1) + " plan " + S(w.plannedTime) + " gerçek " + S(w.spawnTime) + " hırsız " + w.thieves)) + "; 2. kamyonet indirdi " + (van2 != null ? van2.Spawned : -1) + ", aralık " + S(gap));
        dir.Begin(TestThiefSettings(99999f, 99999f));
        BeforeCapture = null;
        yield return null;
    }

    // Park etmiş kamyonet ve inmiş hırsızlar (test kurulumu).
    private static IEnumerator SpawnParkedVan(ThiefDirector dir, int thieves, Action<ThiefVan> onVan)
    {
        var van = dir.SpawnVan(thieves, 0);
        float t0 = Time.time;
        while (van == null && Time.time - t0 < 10f) { yield return null; van = dir.SpawnVan(thieves, 0); }
        if (van == null) throw new Exception("test kamyoneti çıkamadı");
        Time.timeScale = 6f;
        yield return Until(() => van == null || van.IsParked, 90f, "test kamyoneti park");
        yield return Until(() => van == null || van.Spawned >= thieves, 20f, "hırsızlar indi");
        Time.timeScale = 1f;
        onVan(van);
    }

    // ---- (U4-5) savunma ----
    private static IEnumerator PhaseU4Defense(ThiefDirector dir)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var combat = Combat;
        var h = Health;
        ParkCarriersOffLane();
        Loot.ResetForTest();
        h.SetHealthForTest(h.Max);
        // Tek eşya (arena valizi); hırsızlar inince hemen ekipman kapmasın
        var item = LootItem.All.Where(l => l.kind == LootKind.Luggage && l.CanPickUp).OrderBy(l => l.name).First();
        var hidden = HideLootExcept(item);
        ThiefVan van = null;
        yield return SpawnParkedVan(dir, 3, v => van = v);
        Vector3 rear = van.RearPoint;
        var A = van.Thieves[0]; var B = van.Thieves[1]; var C = van.Thieves[2];
        foreach (var t in new[] { A, B, C }) t.ResetForTest();
        B.gameObject.SetActive(false); C.gameObject.SetActive(false);
        // Arena: kamyonetin 10 m önünde, şeritte (trafik yok). Oyuncu valizi elinde, sırtı hırsıza dönük. Yönler şeritten (yol virajlı).
        Vector3 vf = LaneDir(dir, 10f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 arena = LanePoint(dir, 10f, 0.5f);
        item.Drop(LanePoint(dir, 10f, -0.8f), 0f);
        Player.Teleport(arena, Quaternion.LookRotation(-vr));
        yield return null; yield return null;
        Interaction.Interact();
        yield return null;
        bool holding = Player.HeldValuable == item;
        Player.Teleport(arena, Quaternion.LookRotation(vf)); // sırt kamyonete (hırsız arkadan gelir)
        A.TeleportForTest(LanePoint(dir, 6f, 0.5f), Quaternion.LookRotation(vf));
        A.ForceTarget(item);
        float hp0 = h.Current;
        float punch1At = -1f;
        bool punchAnimSeen = false;
        float tp0 = Time.time;
        yield return (Func<bool>)(() => { if (A.State == Thief.ThiefState.Punch && ThiefIn(A, Thief.StatePunchAnim)) punchAnimSeen = true; if (h.Current < hp0 && punch1At < 0f) punch1At = Time.time; return punch1At > 0f || Time.time - tp0 > 15f; });
        if (punch1At < 0f)
        {
            Note("yumruk gelmedi (tanı)", false, "hırsız " + A.State + " etkin " + A.gameObject.activeInHierarchy + ", hedef " + (A.Target != null ? A.Target.name : "-") + ", oyuncuya " + M(Flat(A.transform.position - Player.transform.position).magnitude)
                + ", yol " + A.PathStatus + " kalan " + M(A.RemainingDistance) + ", elde " + (Player.HeldValuable == item) + ", son " + A.LastBlock + ", vuruş " + A.Punches + ", konum " + V(A.transform.position) + " oyuncu " + V(Player.transform.position));
            // Devam edebilmek için hırsızı oyuncunun önüne getir
            A.TeleportForTest(U3Ground(Player.transform.position + Player.transform.forward * 1.0f, Player.transform.position.y), Quaternion.LookRotation(-Player.transform.forward));
        }
        bool hitReact = PlayerAnim.InState(CrewAnimation.StateHitReact);
        Vector3 ap = A.transform.position, af = Flat(A.transform.forward).normalized;
        yield return Capture("u4_yumruk", ap + Vector3.Cross(Vector3.up, af) * 2.8f + af * 0.6f + Vector3.up * 1.6f, (ap + Player.transform.position) * 0.5f + Vector3.up * 1.0f, 50f);
        Note("yumruk", A.PunchHits == 1 && Mathf.Abs(hp0 - h.Current - combat.settings.punchDamage) < 0.01f && punchAnimSeen && hitReact && holding,
            "hasar " + (hp0 - h.Current).ToString("F0", Inv) + ", Thief_Punch " + punchAnimSeen + ", HitReact " + hitReact + ", elde valiz " + holding);
        // İtme: oyuncu hırsıza döner (≤ 1.4 m), sol tık
        yield return (Func<bool>)(() => Player.HasControl);
        Vector3 toA = Flat(A.transform.position - Player.transform.position);
        Player.Teleport(Player.transform.position, Quaternion.LookRotation(toA.normalized));
        yield return null;
        float dBefore = toA.magnitude;
        Vector3 aBefore = A.transform.position;
        bool pushStarted = combat.Attack();
        bool pushAnim = false;
        float tPush = Time.time;
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StatePushShove)) pushAnim = true; return Time.time - tPush >= 0.35f; });
        bool stunnedPush = A.IsStunned && A.LastStunReason == "itme";
        float stunLeft = A.StunRemaining;
        bool secondRejected = !combat.Attack(); // 0.8 sn bekleme
        yield return Capture("u4_itme", Player.transform.position - toA.normalized * 2.5f + Vector3.Cross(Vector3.up, toA.normalized) * 2.2f + Vector3.up * 1.6f, (Player.transform.position + A.transform.position) * 0.5f + Vector3.up * 0.9f, 50f);
        yield return Seconds(0.4f);
        float pushed = Flat(A.transform.position - aBefore).magnitude;
        bool stunAnim = ThiefIn(A, Thief.StateStunnedAnim);
        yield return Until(() => !A.IsStunned, 4f, "sersemlik bitti");
        float stunDur = Time.time - tPush - 0.27f;
        Note("itme", pushStarted && pushAnim && combat.Pushes == 1 && combat.PushHits == 1 && stunnedPush && stunLeft > 1.3f && pushed >= 1.0f && pushed <= 2.0f && stunAnim && secondRejected && Mathf.Abs(stunDur - combat.settings.pushStunSeconds) < 0.5f,
            "Push_Shove " + pushAnim + ", isabet " + combat.PushHits + ", sersem(itme) " + stunnedPush + " " + S(stunDur) + " (Thief_Stunned " + stunAnim + "), savrulma " + M(pushed) + " (önce " + M(dBefore) + "), 2. tık reddedildi " + secondRejected);
        // Elden kapma: oyuncu hırsıza dönük, hırsız önünde ≤ 1.2 m
        A.ForceTarget(item);
        yield return Until(() => Player.HeldValuable == null || A.Carried == item, 12f, "elden kapma");
        yield return null;
        bool handGrab = A.HandGrabs == 1 && A.Carried == item && Player.HeldValuable == null && item.IsHeldByThief;
        Note("elden kapma", handGrab, "HandGrabs " + A.HandGrabs + ", hırsızda " + (A.Carried == item) + ", oyuncunun eli boş " + (Player.HeldValuable == null));
        // Tackle: hırsız (eşyayla) oyuncunun 3.6 m önünde; oyuncu şerit boyunca koşar (kamera yok → girdi ileri = oyuncunun baktığı yön değil,
        // dünya +Z; bu yüzden geçici bir kamera şerit yönüne bakar)
        Vector3 runDir = LaneDir(dir, 13f);
        Vector3 runFrom = LanePoint(dir, 12f, 0.3f);
        var runCam = new GameObject("U4_RunCam").transform;
        runCam.position = runFrom - runDir * 3f + Vector3.up * 1.6f;
        runCam.rotation = Quaternion.LookRotation(runDir, Vector3.up);
        Player.cameraTransform = runCam;
        Player.Teleport(runFrom, Quaternion.LookRotation(runDir));
        A.TeleportForTest(LanePoint(dir, 15.6f, 0.3f), Quaternion.LookRotation(runDir));
        A.Stun(1.2f, Vector3.zero, "test-bekleme"); // yerinde dursun (eşyayı düşürür → tekrar verilir)
        yield return null;
        A.GiveItemForTest(item); // sersemken elinde (Stun bitince kamyonete koşar; tackle önce gelir)
        Player.SetRunInput(true);
        Player.SetMoveInput(new Vector2(0f, 1f));
        yield return Seconds(0.5f);
        float runSpeed = Player.HorizontalVelocity.magnitude;
        Vector3 before = Player.transform.position;
        bool tackleStarted = combat.Attack();
        Player.SetMoveInput(Vector2.zero);
        Player.SetRunInput(false);
        Player.cameraTransform = null;
        UnityEngine.Object.Destroy(runCam.gameObject);
        bool diveAnim = false, getUpAnim = false;
        float tT = Time.time;
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StateTackleDive)) diveAnim = true; return Time.time - tT >= 0.38f; });
        bool noControl = !Player.HasControl;
        Vector3 pp = Player.transform.position; Vector3 side = Vector3.Cross(Vector3.up, runDir);
        yield return Capture("u4_tackle", pp + side * 3.0f - runDir * 0.5f + Vector3.up * 1.5f, pp + runDir * 1.2f + Vector3.up * 0.6f, 50f);
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StateTackleGetUp)) getUpAnim = true; return Time.time - tT >= 1.3f; });
        float slid = Flat(Player.transform.position - before).magnitude;
        bool tackleStun = A.LastStunReason == "tackle" && A.Carried == null && item.State == LootItem.LootState.Dropped;
        float tackleStunLeft = A.StunRemaining;
        yield return (Func<bool>)(() => Player.HasControl || Time.time - tT > 3f);
        float controlAfter = Time.time - tT;
        Note("tackle", tackleStarted && runSpeed > 1.5f && diveAnim && noControl && getUpAnim && combat.TackleHits == 1 && tackleStun && tackleStunLeft > 2.3f && slid >= 1.8f && slid <= 3.2f && controlAfter >= 1.6f && controlAfter <= 2.3f,
            "koşu " + runSpeed.ToString("F1", Inv) + " m/sn, Tackle_Dive " + diveAnim + " → Tackle_GetUp " + getUpAnim + ", kayma " + M(slid) + ", kontrol " + S(controlAfter) + " sonra, isabet " + combat.TackleHits + " (sersem " + tackleStun + ", kalan " + S(tackleStunLeft) + "), eşya düştü " + (item.State == LootItem.LootState.Dropped));
        // Fırlatma: oyuncu eşyayı alır, hırsız 5 m önde; sağ tık basılı (Throw_Aim), bırak (Throw_Release)
        yield return Until(() => !A.IsStunned, 5f, "tackle sersemliği bitti");
        Vector3 ic = item.Center;
        Vector3 standI = U3StandAround(ic, 1.0f, out Vector3 idir);
        Player.Teleport(standI, Quaternion.LookRotation(-idir));
        yield return null; yield return null;
        Interaction.Interact();
        yield return null;
        bool held2 = Player.HeldValuable == item;
        Vector3 throwDir = LaneDir(dir, 14f);
        Player.Teleport(LanePoint(dir, 13f, 0.3f), Quaternion.LookRotation(throwDir));
        yield return null;
        A.TeleportForTest(LanePoint(dir, 18f, 0.3f), Quaternion.LookRotation(-throwDir));
        A.Stun(4f, Vector3.zero, "test-hedef"); // yerinde dursun
        yield return null;
        combat.SetThrowHeld(true);
        yield return Seconds(0.4f);
        bool aiming = combat.Aiming && PlayerAnim.InState(CrewAnimation.StateThrowAim);
        Vector3 handLocal = Player.transform.InverseTransformPoint(item.Center);
        combat.SetThrowHeld(false);
        bool releaseAnim = false;
        float tR = Time.time;
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StateThrowRelease)) releaseAnim = true; return item.IsFlying || Time.time - tR > 0.6f; });
        yield return null;
        bool flying = item.IsFlying && Player.HeldValuable == null && combat.Throws == 1;
        Vector3 fp = Player.transform.position;
        Vector3 throwSide = Vector3.Cross(Vector3.up, throwDir);
        yield return Capture("u4_firlatma", fp - throwDir * 1.5f + throwSide * 3.2f + Vector3.up * 1.8f, fp + throwDir * 2.5f + Vector3.up * 1.2f, 55f);
        yield return Until(() => item.ThrowHits > 0 || !item.IsFlying, 4f, "fırlatma isabeti");
        bool throwHit = item.ThrowHits == 1 && A.LastStunReason == "firlatma";
        yield return Until(() => !item.IsFlying, 8f, "eşya yere indi");
        bool landed = item.State == LootItem.LootState.Dropped && item.GetComponent<Rigidbody>() == null && item.CanPickUp;
        Note("fırlatma", held2 && aiming && handLocal.y > 1.0f && releaseAnim && flying && throwHit && landed,
            "Throw_Aim " + aiming + " (eşya kök uzayında " + V(handLocal) + "), Throw_Release " + releaseAnim + ", uçtu " + flying + ", isabet " + item.ThrowHits + " (sersem " + A.LastStunReason + "), yere indi/alınabilir " + landed);
        // Boşluğa düşen eşya geri döner: uçurum kenarından vadiye fırlat (kamera: aşağı doğru). Hırsız A bu sırada sersem beklesin
        // (öbür eşyalar kapalı kalır; yalnız ikinci valiz açılır), yoksa arena valizini kamyonete taşıyıp testi bozuyor.
        A.ResetForTest();
        A.TeleportForTest(arena, Quaternion.identity);
        A.Stun(25f, Vector3.zero, "test-bekleme");
        var item2 = hidden.Where(l => l != null && l.kind == LootKind.Luggage && l != item).OrderBy(l => l.name).First();
        item2.gameObject.SetActive(true);
        yield return null;
        Vector3 start2 = item2.transform.position; Transform parent2 = item2.transform.parent;
        var edgeRope = Rope;
        Vector3 edge = edgeRope.edgePoint.position;
        Vector3 standE = U3Ground(edge - Vector3.forward * 2.0f, edge.y);
        item2.Drop(U3Ground(standE + Vector3.right * 1.0f, standE.y), 0f);
        Player.Teleport(standE, Quaternion.LookRotation(Vector3.right));
        yield return null; yield return null;
        Interaction.Interact();
        yield return null;
        bool held3 = Player.HeldValuable == item2;
        Player.Teleport(standE, Quaternion.LookRotation(Vector3.forward));
        var aimCam = new GameObject("U4_AimCam").transform;
        aimCam.position = Player.transform.position + Vector3.up * 1.6f;
        aimCam.rotation = Quaternion.LookRotation((Vector3.forward * 2f + Vector3.down * 1f).normalized);
        Player.cameraTransform = aimCam;
        yield return null;
        combat.SetThrowHeld(true);
        yield return Seconds(0.3f);
        combat.SetThrowHeld(false);
        yield return Until(() => item2.IsFlying, 1f, "boşluk atışı");
        Player.cameraTransform = null;
        UnityEngine.Object.Destroy(aimCam.gameObject);
        Time.timeScale = 3f;
        yield return Until(() => !item2.IsFlying, 12f, "boşluğa düştü");
        Time.timeScale = 1f;
        bool returned = item2.FallReturns == 1 && item2.State == LootItem.LootState.AtStart && Vector3.Distance(item2.transform.position, start2) < 0.05f && item2.transform.parent == parent2 && !item2.IsStolen;
        Note("boşluğa düşen eşya geri döner", held3 && returned, "FallReturns " + item2.FallReturns + ", durum " + item2.State + ", başlangıca " + M(Vector3.Distance(item2.transform.position, start2)) + ", çalınan sayılmadı " + !item2.IsStolen);
        // Düdük: 3 hırsız 6 m içinde (A eşyalı), Q → korku, kamyonete koşar, eşya bırakılmaz; bekleme 20 sn, HUD
        B.gameObject.SetActive(true); C.gameObject.SetActive(true);
        var hidden2 = HideLootExcept(item);
        Player.Teleport(arena, Quaternion.LookRotation(vf)); // yüz ileri (kamyonet arkada); hırsızlar önde
        foreach (var t in new[] { A, B, C }) t.ResetForTest();
        A.TeleportForTest(U3Ground(arena + vf * 3f + vr * 1.5f, arena.y), Quaternion.LookRotation(-vf));
        B.TeleportForTest(U3Ground(arena + vf * 4f - vr * 1.5f, arena.y), Quaternion.LookRotation(-vf));
        C.TeleportForTest(U3Ground(arena + vf * 5.5f, arena.y), Quaternion.LookRotation(-vf));
        foreach (var t in new[] { A, B, C }) t.Stun(2.5f, Vector3.zero, "test-bekleme");
        yield return Seconds(2.7f);
        if (item.IsStolen) Loot.RestoreForTest(item);
        A.GiveItemForTest(item); // A eşyalı (kamyonete koşmaya başlar; düdük hemen gelir)
        yield return null;
        RestoreLoot(hidden2);
        var rearDist0 = new Dictionary<Thief, float>();
        foreach (var t in new[] { A, B, C }) rearDist0[t] = Flat(rear - t.transform.position).magnitude;
        bool whistleStarted = combat.Whistle();
        bool whistleAnim = false;
        float tW = Time.time;
        yield return (Func<bool>)(() => { if (PlayerAnim.InState(CrewAnimation.StateWhistle)) whistleAnim = true; return Time.time - tW >= 0.55f; });
        bool allFeared = A.IsFeared && B.IsFeared && C.IsFeared;
        bool secondWhistle = combat.Whistle();
        string hudWhistle = Hud.WhistleText.text;
        yield return CaptureWithHud("u4_duduk", Player.transform.position - vf * 3.0f + vr * 2.6f + Vector3.up * 1.9f, Player.transform.position + vf * 2.5f + Vector3.up * 0.9f, 55f);
        yield return Seconds(1.2f);
        bool allToward = new[] { A, B, C }.All(t => Flat(rear - t.transform.position).magnitude < rearDist0[t] - 1.0f);
        bool keptItem = A.Carried == item;
        float fearLeft = A.FearRemaining;
        Note("düdük", whistleStarted && whistleAnim && combat.WhistleHits == 3 && allFeared && allToward && keptItem && !secondWhistle && !combat.WhistleReady && hudWhistle.StartsWith(MissionHud.WhistlePrefix) && hudWhistle.Contains(" sn") && Mathf.Abs(combat.WhistleRemaining - (combat.settings.whistleCooldown - (Time.time - tW))) < 0.3f,
            "Whistle_Blow " + whistleAnim + ", korkan " + combat.WhistleHits + "/3 (" + allFeared + "), kamyonete koştu " + allToward + ", eşya bırakılmadı " + keptItem + " (korku kalan " + S(fearLeft) + "), 2. düdük reddedildi " + !secondWhistle + ", HUD \"" + hudWhistle + "\"");
        // Sedye taşırken saldırı ve düdük yok (eşyalar — sedye dahil — yeniden açık)
        foreach (var t in new[] { A, B, C }) t.gameObject.SetActive(false);
        RestoreLoot(hidden);
        yield return null;
        var s0 = Stretcher(0);
        TeleportPlayerToHandle(s0, false);
        yield return null; yield return null;
        Interaction.Interact();
        yield return Seconds(0.3f);
        bool holdingStretcher = Player.HeldStretcher == s0;
        bool noAttack = !combat.Attack() && !combat.CanAct;
        combat.SetThrowHeld(true);
        bool noAim = !combat.Aiming;
        combat.SetThrowHeld(false);
        Interaction.Interact(); // bırak
        yield return Seconds(0.3f);
        Note("sedye taşırken saldırı yok", holdingStretcher && noAttack && noAim, "sedye elde " + holdingStretcher + ", sol tık reddedildi " + noAttack + ", nişan yok " + noAim);
        Check("(U4-5) savunma: yumruk " + combat.settings.punchDamage.ToString(Inv) + " hasar (Thief_Punch, HitReact); sol tık Push_Shove → " + combat.settings.pushRange.ToString(Inv) + " m/" + combat.settings.pushAngle.ToString(Inv) + "° içindeki hırsız " + combat.settings.pushKnockback.ToString(Inv) + " m savrulur, " + combat.settings.pushStunSeconds.ToString(Inv) + " sn sersem, bekleme " + combat.settings.pushCooldown.ToString(Inv) + " sn; elden kapma; koşarken sol tık Tackle_Dive → " + combat.settings.tackleDistance.ToString(Inv) + " m kayma, hırsız " + combat.settings.tackleStunSeconds.ToString(Inv) + " sn sersem + eşyayı düşürür, Tackle_GetUp; sağ tık Throw_Aim/Release → " + combat.settings.throwSpeed.ToString(Inv) + " m/sn, isabet " + combat.settings.throwHitStunSeconds.ToString(Inv) + " sn, boşluğa düşen geri döner; Q Whistle_Blow → " + combat.settings.whistleRadius.ToString(Inv) + " m'de " + combat.settings.fearSeconds.ToString(Inv) + " sn korku (eşya bırakılmaz), bekleme " + combat.settings.whistleCooldown.ToString(Inv) + " sn + HUD; sedye taşırken yok",
            ok, string.Join("; ", parts));
        RestoreLoot(hidden);
        h.SetHealthForTest(h.Max);
        dir.Begin(TestThiefSettings(99999f, 99999f));
        yield return null;
    }

    // ---- (U4-6) kurallar ----
    private static IEnumerator PhaseU4Rules(ThiefDirector dir)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        ParkCarriersOffLane();
        Loot.ResetForTest();
        var hiddenAll = HideLootExcept(); // hırsız inince hedefsiz kalsın (biner), testler tek tek açar
        ThiefVan van = null;
        yield return SpawnParkedVan(dir, 1, v => van = v);
        var thief = van.Thieves[0];
        thief.gameObject.SetActive(true);
        thief.ResetForTest();
        RestoreLoot(hiddenAll);
        yield return null;
        // Hastalı sedye çalınmaz; boşalınca çalınabilir
        var s0 = Stretcher(0);
        var s0Item = s0.GetComponent<LootItem>();
        var tourist = PatientNamed("Tourist");
        yield return PutPatientOnStretcher(tourist, s0);
        yield return Seconds(0.5f);
        bool withPatient = !thief.IsStealable(s0Item);
        s0.TakePatient();
        var touristSpot = s0.patientAnchor.position;
        tourist.LieOnGround(U3Ground(touristSpot + Vector3.forward * 1.5f, touristSpot.y), Vector3.right, Vector3.up);
        yield return null;
        bool empty = thief.IsStealable(s0Item);
        Note("hastalı sedye çalınmaz / boş sedye çalınır", withPatient && empty, "hastalı " + !withPatient + ", boş " + empty);
        // Bağlı / atılmış halat çalınmaz
        var rope = Rope;
        var coilItem = rope.coil != null ? rope.coil.GetComponent<LootItem>() : null;
        bool ropeNo = coilItem != null && !thief.IsStealable(coilItem) && rope.State != RopeAnchor.RopeState.Stowed;
        Note("bağlı/atılmış halat çalınmaz", ropeNo, "halat durumu " + rope.State + ", kangal " + (rope.coil != null ? rope.coil.State.ToString() : "-") + ", çalınabilir " + (coilItem != null && thief.IsStealable(coilItem)));
        // Hasta eşyası (yerde yatan hasta): çalınabilir ve kapılır
        var p = Mission.patients.FirstOrDefault(x => x.State == PatientState.LyingGround);
        var pi = LootItem.All.FirstOrDefault(l => l.kind == LootKind.PatientItem && l.patient == p);
        bool piStealable = pi != null && thief.IsStealable(pi);
        bool piGrabbed = false;
        if (pi != null)
        {
            var hiddenPi = HideLootExcept(pi);
            thief.ResetForTest();
            thief.TeleportForTest(U3StandAround(pi.Center, 1.0f, out Vector3 pdir), Quaternion.LookRotation(-pdir));
            thief.ForceTarget(pi);
            yield return Until(() => thief.Carried == pi, 8f, "hasta eşyası kapıldı");
            piGrabbed = thief.Carried == pi && pi.transform.parent == null && pi.IsHeldByThief;
            thief.Stun(0.5f, Vector3.zero, "test"); // bırak
            yield return Seconds(0.7f);
            RestoreLoot(hiddenPi);
        }
        Note("hasta eşyası", piStealable && piGrabbed && pi != null && pi.State == LootItem.LootState.Dropped && pi.CanPickUp, "çalınabilir " + piStealable + ", kapıldı " + piGrabbed + ", bırakınca yerde alınabilir " + (pi != null && pi.CanPickUp));
        // Boş sedye: kapar, arkasında sürükler, kamyonete atınca sedye kaybolur
        var s1 = Stretcher(1);
        var s1Item = s1.GetComponent<LootItem>();
        Vector3 vf = LaneDir(dir, 9f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 sPos = LanePoint(dir, 9f, -1.0f);
        s1.Teleport(sPos, Quaternion.LookRotation(vr, Vector3.up));
        var hiddenS = HideLootExcept(s1Item);
        thief.ResetForTest();
        thief.TeleportForTest(LanePoint(dir, 11f, -1.0f), Quaternion.LookRotation(-vf));
        thief.ForceTarget(s1Item);
        yield return Until(() => thief.Carried == s1Item, 10f, "sedye kapıldı");
        bool dragging = thief.Carried == s1Item;
        yield return Seconds(0.8f);
        Vector3 rel = thief.transform.InverseTransformPoint(s1.transform.position);
        bool behind = dragging && rel.z < -0.5f && rel.z > -2.5f && Mathf.Abs(rel.x) < 0.8f && (ThiefIn(thief, Thief.StateMoveCarry) || thief.State == Thief.ThiefState.Carry);
        Time.timeScale = 3f;
        yield return Until(() => s1Item.IsStolen || thief.IsGone, 40f, "sedye kamyonete atıldı");
        Time.timeScale = 1f;
        yield return null;
        bool gone = s1Item.IsStolen && !s1.gameObject.activeSelf && !StretcherController.All.Contains(s1) && Loot.StolenValue >= s1Item.TotalValue;
        Note("boş sedye sürüklenir ve çalınınca kaybolur", dragging && behind && gone, "kaptı " + dragging + ", arkasında " + V(rel) + " (" + ThiefAnim(thief) + "), çalındı/gizli " + gone);
        Loot.RestoreForTest(s1Item);
        RestoreLoot(hiddenS);
        // Koni ve ulaşılmazlık
        var cone = Cone(2);
        var coneItem = cone != null ? cone.GetComponent<LootItem>() : null;
        bool coneStealable = coneItem != null && thief.IsStealable(coneItem);
        bool ledgeNo = !dir.Reachable(new Vector3(0f, dir.pathLevelY - 5f, 0f)) && dir.Reachable(new Vector3(0f, dir.pathLevelY, 0f)) && dir.Reachable(new Vector3(0f, dir.roadLevelY, 0f));
        Note("koni çalınabilir, kaya çıkıntısı ulaşılmaz", coneStealable && ledgeNo, "koni " + coneStealable + ", seviye kuralı " + ledgeNo);
        Check("(U4-6) kurallar: hastalı sedye ve bağlı/atılmış halat çalınmaz; yatan hastanın eşyası kapılır; boş sedye tek hırsızla arkasında sürüklenir, kamyonete atılınca sedye sahneden kalkar; koni çalınabilir; kaya çıkıntısı ulaşılmaz",
            ok, string.Join("; ", parts));
        dir.Begin(TestThiefSettings(99999f, 99999f));
        yield return null;
    }

    // ---- (U4-7) tavan valizleri ----
    private static IEnumerator PhaseU4Roof(MinivanRoofLuggage roof)
    {
        ParkCarriersOffLane();
        Loot.ResetForTest();
        var tg = UnityEngine.Object.FindAnyObjectByType<MinivanTailgate>();
        int items0 = LootItem.All.Count;
        int total0 = Loot.TotalValue;
        bool wasOpen = tg.IsOpen;
        if (!tg.IsOpen) { tg.Open(); yield return Until(() => tg.IsOpen, 3f, "bagaj kapısı"); }
        roof.enabled = true;
        yield return Until(() => roof.Dropped, 2f, "tavan valizleri düştü");
        yield return Seconds(0.5f);
        Vector3 rear = tg.rearPoint.position, back = tg.RearDirection, side = Vector3.Cross(Vector3.up, back);
        yield return Capture("u4_tavan_valiz", rear + back * 4.5f + side * 4.5f + Vector3.up * 2.4f, rear + back * 1.5f + Vector3.up * 1.2f, 50f);
        yield return Until(() => roof.Landed == 2, 5f, "indi");
        yield return null; yield return null;
        var a = roof.Items[0]; var b = roof.Items[1];
        float PenOf(LootItem it) { var bnd = it.WorldBounds; return GroundProbe.Find(bnd.center + Vector3.up * 1f, 4f, out RaycastHit g) ? bnd.min.y - g.point.y : 99f; }
        var cols = new List<Collider>(a.GetComponentsInChildren<Collider>()); cols.AddRange(b.GetComponentsInChildren<Collider>());
        foreach (var c in cols) c.enabled = false;
        float penA = PenOf(a), penB = PenOf(b);
        foreach (var c in cols) c.enabled = true;
        bool grounded = penA > -0.03f && penA < 0.1f && penB > -0.03f && penB < 0.1f;
        bool loot = LootItem.All.Count == items0 + 2 && a.CanPickUp && b.CanPickUp && a.kind == LootKind.Luggage && a.value == Loot.values.suitcaseValue && a.contents.Count >= 2 && a.contents.Count <= 4 && b.contents.Count >= 2
            && Loot.TotalValue == total0 + a.TotalValue + b.TotalValue;
        bool behind = Vector3.Dot(Flat(a.Center - rear), back) > 1.0f && Vector3.Dot(Flat(b.Center - rear), back) > 1.0f && Flat(a.Center - rear).magnitude < 7f && Flat(b.Center - rear).magnitude < 7f && Flat(a.Center - b.Center).magnitude > 0.5f;
        bool detached = a.transform.parent == null && b.transform.parent == null;
        // Alınabilir
        Vector3 stand = U3StandAround(a.Center, 1.0f, out Vector3 dirA);
        Player.Teleport(stand, Quaternion.LookRotation(-dirA));
        yield return null; yield return null;
        string hint = Interaction.CurrentHint;
        Interaction.Interact();
        yield return null;
        bool picked = Player.HeldValuable == a && hint == PlayerInteraction.HintTakeLuggage;
        yield return Seconds(0.3f);
        Interaction.Interact(); // bırak
        yield return Seconds(0.3f);
        Check("(U4-7) tavan valizleri: bagaj kapısı açılınca 2 valiz tavandan minibüsün arkasına yuvarlanır, yere oturur, minibüsten ayrılır, U3 valiz kuralıyla LootItem olur (" + Loot.values.suitcaseValue + " ₺ + 2–4 içerik), toplam değer ve %50 eşiği güncellenir, E ile alınır",
            roof.Dropped && roof.Landed == 2 && grounded && loot && behind && detached && picked,
            "kapı açıktı " + wasOpen + "; indi " + roof.Landed + "; zemine " + (penA * 100f).ToString("F1", Inv) + " / " + (penB * 100f).ToString("F1", Inv) + " cm; eşya " + LootItem.All.Count + " (önce " + items0 + "), toplam " + total0 + " → " + Loot.TotalValue
            + " (+" + (a.TotalValue + b.TotalValue) + ": " + a.name + " " + string.Join("+", a.contents.Select(c => c.name)) + " / " + b.name + " " + string.Join("+", b.contents.Select(c => c.name)) + "); arkada " + behind + ", ayrıldı " + detached + "; ipucu \"" + hint + "\" alındı " + picked);
    }

    // ---- (U4-8) %50 → görev başarısız (gerçek hırsızlarla) ----
    private static IEnumerator PhaseU4Fail(ThiefDirector dir)
    {
        ParkCarriersOffLane();
        Loot.ResetForTest();
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        float limit = Loot.values != null ? Loot.values.failFraction : 0.5f;
        int total = Loot.TotalValue;
        ThiefVan van = null;
        yield return SpawnParkedVan(dir, 5, v => van = v);
        // Eşyalar kamyonetin 6–10 m önüne, yola (şerit koordinatıyla; yay)
        var items = LootItem.All.Where(l => l.carryable && !l.IsSecured && !l.IsStolen && !l.IsHeld && !l.IsHeldByThief).OrderBy(l => l.name).ToList();
        for (int i = 0; i < items.Count; i++)
            items[i].Drop(LanePoint(dir, 6f + (i % 3) * 1.6f, ((i % 4) - 1.5f) * 1.2f), i * 37f);
        var s0 = Stretcher(0); var s1 = Stretcher(1);
        Vector3 sr = Vector3.Cross(Vector3.up, LaneDir(dir, 11f));
        s0.Teleport(LanePoint(dir, 11f, 0.3f), Quaternion.LookRotation(sr));
        s1.Teleport(LanePoint(dir, 11f, -2.5f), Quaternion.LookRotation(sr));
        float t0 = Time.time;
        Time.timeScale = 5f;
        yield return Until(() => Mission.IsFinished || van == null || Time.time - t0 > 220f, 240f, "%50 çalındı");
        Time.timeScale = 1f;
        yield return null;
        int stealers = Thief.All.Count(t => t != null && t.Throws > 0);
        string result = Hud.ResultText.text;
        Check("(U4-8) 5 hırsız (hızlandırılmış) eşyaları kamyonete taşır; çalınan toplamın %" + Mathf.RoundToInt(limit * 100f) + "'sine ulaşınca görev başarısız: \"" + LootRegistry.FailText + "\"",
            Mission.IsFinished && !Mission.Succeeded && Mission.ResultMessage == LootRegistry.FailText && Loot.StolenFraction >= limit && van != null && van.LootCount >= 3 && result.StartsWith(LootRegistry.FailText),
            "süre " + S(Time.time - t0) + ", çalınan " + Loot.StolenValue + "/" + total + " = %" + (Loot.StolenFraction * 100f).ToString("F1", Inv) + ", kamyonette " + (van != null ? van.LootCount : -1) + " eşya, çalan hırsız " + stealers + "/" + Thief.All.Count + ", görev bitti " + Mission.IsFinished + ", HUD \"" + result.Replace("\n", " | ") + "\"");
        dir.Begin(TestThiefSettings(99999f, 99999f));
        yield return null;
    }
}
