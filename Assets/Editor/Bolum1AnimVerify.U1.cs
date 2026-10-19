using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// U1 (üçüncü test turu, kullanıcı kararları 2026-10-19) Play modu kontrolleri:
//  (U1-1) halata bağlanma yalnız ≤ 1.0 m VE yüz dönükken: 1.2 m'de bağlanmaz, 0.9 m yüz dönük bağlanır, 0.9 m sırtı dönük bağlanmaz
//  (U1-2) beyaz vurgu: ipucunun hedefi (sedye, koni, hasta, halat; bagaj kapısı ve arkadaş öbür fazlarda) vurgulanır,
//         hedefsiz eylemde (bırak, koy, yere bırak) ve uyarıda vurgu yok, hedef kaybolunca aynı kare kalkar
//  (U1-3) sırttan bırakma: patika/yol/banket üzerinde ≥ 30 rastgele yürünebilir noktada başarı ≥ %95, hasta zemine girmiyor (≤ 3 cm);
//         kaya çıkıntısında ve uçurum kenarında bırakılmıyor, "Burada bırakılamaz" ipucu yerinde (uyarı renginde)
public static partial class Bolum1AnimVerify
{
    private static InteractionHighlight Highlight { get { return player.GetComponent<InteractionHighlight>(); } }
    private const int U1PutDownSamples = 30;

    private static IEnumerator PhaseU1()
    {
        var rope = Rope;
        var bot = Bot;
        bool savedAssist = bot.assistEnabled;
        bot.assistEnabled = false;
        Transform savedCamera = Player.cameraTransform;
        Player.cameraTransform = null;
        var savedStretchers = StretcherController.All.Select(s => (s, s.transform.position, s.transform.rotation)).ToList();
        if (Player.Rope != null) rope.Detach(Player, rope.topStand.position, rope.topStand.rotation);
        if (Player.Piggyback != null) { var pb = Player.ReleasePiggyback(); pb.PutDownFromBack(Player.transform.position - Player.transform.forward * 0.5f, -Player.transform.forward, Vector3.up); }

        yield return PhaseU1Rope(rope);
        yield return PhaseU1Highlight(savedStretchers);
        yield return PhaseU1PutDown(rope);

        // Toparla
        foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);
        bot.assistEnabled = savedAssist;
        Player.cameraTransform = savedCamera;
        yield return null;
    }

    // Patika üzerinde, topStand'den along yönünde d metre ötede zemin noktası
    private static Vector3 U1PathPoint(RopeAnchor rope, Vector3 along, float d)
    {
        Vector3 p = rope.topStand.position + along * d;
        if (GroundProbe.Find(p + Vector3.up * 1f, 3f, out RaycastHit g)) p.y = g.point.y + 0.02f;
        return p;
    }

    // ---- (U1-1) halat yakınlığı ve yönü ----
    private static IEnumerator PhaseU1Rope(RopeAnchor rope)
    {
        // Patika boyunca yön: topStand'in yanı (±X); 1.2 m ötede patika zemini olan taraf
        Vector3 along = Flat(rope.topStand.right).normalized;
        Vector3 probe = U1PathPoint(rope, along, 1.2f);
        if (Mathf.Abs(probe.y - rope.topStand.position.y) > 0.3f) along = -along;

        // 1.2 m, yüz dönük → bağlanmaz
        Player.Teleport(U1PathPoint(rope, along, 1.2f), Quaternion.LookRotation(-along));
        yield return null; yield return null;
        string hintFar = Interaction.CurrentHint;
        bool attachFar = rope.Attach(Player);
        if (attachFar) rope.Detach(Player, U1PathPoint(rope, along, 1.2f), Quaternion.LookRotation(-along));
        float distFar = Flat(Player.transform.position - rope.topStand.position).magnitude;

        // 0.9 m, sırtı dönük → bağlanmaz, vurgu yok
        Player.Teleport(U1PathPoint(rope, along, 0.9f), Quaternion.LookRotation(along));
        yield return null; yield return null;
        string hintBack = Interaction.CurrentHint;
        Transform hlBack = Highlight.Current;
        bool attachBack = rope.Attach(Player);
        if (attachBack) rope.Detach(Player, U1PathPoint(rope, along, 0.9f), Quaternion.LookRotation(along));

        // 0.9 m, yüz dönük → ipucu + halat vurgusu (çizgi kopyası beyaz), E bağlar
        Player.Teleport(U1PathPoint(rope, along, 0.9f), Quaternion.LookRotation(-along));
        yield return null; yield return null;
        string hintNear = Interaction.CurrentHint;
        Transform hlNear = Highlight.Current;
        int copies = Highlight.CopyCount;
        var lineCopy = Highlight.Copies.OfType<LineRenderer>().FirstOrDefault();
        bool lineWhite = lineCopy != null && lineCopy.sharedMaterial != null && lineCopy.sharedMaterial.shader != null
            && lineCopy.sharedMaterial.shader.name == InteractionHighlight.LineShaderName && lineCopy.widthMultiplier > rope.line.widthMultiplier + 0.004f;
        float distNear = Flat(Player.transform.position - rope.topStand.position).magnitude;
        float angleNear = Vector3.Angle(Flat(Player.transform.forward), Flat(rope.topStand.position - Player.transform.position));
        Vector3 pp = Player.transform.position;
        Vector3 side = Vector3.Cross(Vector3.up, along);
        Vector3 ropeMid = (rope.ropeAttach.position + rope.topStand.position) * 0.5f;
        yield return Capture("u1_halat_vurgu", pp + along * 2.4f + side * 1.6f + Vector3.up * 1.9f, ropeMid + Vector3.up * 0.5f, 50f);
        Interaction.Interact();
        bool attachedNear = Player.Rope == rope;
        yield return Seconds(0.2f);
        if (Player.Rope != null) rope.Detach(Player, rope.topStand.position, rope.topStand.rotation);
        yield return null; yield return null;

        Check("(U1-1) halata bağlanma yalnız ≤ " + rope.attachRange.ToString("F1", Inv) + " m ve yüz dönükken (≤ " + rope.facingAngle.ToString("F0", Inv) + "°): 1.2 m'de yok, 0.9 m sırtı dönük yok, 0.9 m yüz dönük ipucu + beyaz halat vurgusu + E bağlar",
            hintFar != PlayerInteraction.HintAttachRope && !attachFar && hintBack != PlayerInteraction.HintAttachRope && !attachBack && hlBack == null
            && hintNear == PlayerInteraction.HintAttachRope && hlNear == rope.transform && copies > 0 && lineWhite && attachedNear,
            "1.2 m (" + M(distFar) + "): ipucu \"" + hintFar + "\", Attach " + attachFar + "; 0.9 m sırtı dönük: ipucu \"" + hintBack + "\", Attach " + attachBack + ", vurgu " + (hlBack != null ? hlBack.name : "yok")
            + "; 0.9 m yüz dönük (" + M(distNear) + ", açı " + angleNear.ToString("F0", Inv) + "°): ipucu \"" + hintNear + "\", vurgu " + (hlNear != null ? hlNear.name : "yok") + " (" + copies + " kopya, halat çizgisi beyaz/geniş " + lineWhite + "), E bağladı " + attachedNear);
    }

    // ---- (U1-2) vurgu: eylem türleri ----
    private static IEnumerator PhaseU1Highlight(List<(StretcherController, Vector3, Quaternion)> savedStretchers)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        string HlName() { return Highlight.Current != null ? Highlight.Current.name : "yok"; }

        // Sedye (tut) → sedye vurgulu; tutarken (bırak) vurgu yok
        var s0 = Stretcher(0);
        TeleportPlayerToHandle(s0, false);
        yield return null; yield return null;
        bool grabHl = Interaction.CurrentHint == PlayerInteraction.HintGrab && Highlight.Current == s0.transform && Highlight.CopyCount > 0;
        Note("sedye", grabHl, "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName() + " (" + Highlight.CopyCount + " kopya)");
        Vector3 heading = Flat(s0.handleFront.position - s0.handleRear.position).normalized;
        Vector3 sside = Vector3.Cross(Vector3.up, heading);
        Vector3 sc = s0.transform.position;
        yield return Capture("u1_sedye_vurgu", sc + sside * 3.4f - heading * 1.2f + Vector3.up * 2.0f, sc + Vector3.up * 0.4f, 50f);
        Interaction.Interact();
        yield return null; yield return null;
        bool holding = Player.HeldStretcher == s0;
        Note("tutarken bırak (hedefsiz)", holding && Interaction.CurrentHint == PlayerInteraction.HintRelease && Highlight.Current == null && Highlight.CopyCount == 0,
            "tutuyor " + holding + ", ipucu \"" + Interaction.CurrentHint + "\", vurgu " + HlName());
        if (holding) { Interaction.Interact(); yield return Seconds(0.3f); }
        foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);

        // Koni (al) → koni vurgulu; tutarken (koy) vurgu yok; koni eski yerine
        var cone0 = Cone(0);
        if (cone0 != null)
        {
            Vector3 dir = Vector3.right;
            Vector3 cp = cone0.BasePoint + dir * 0.9f;
            if (GroundProbe.Find(cp + Vector3.up * 1.5f, 4f, out RaycastHit g)) cp.y = g.point.y + 0.02f;
            Player.Teleport(cp, Quaternion.LookRotation(-dir));
            yield return null; yield return null;
            // İpucunun hedefi en yakın boş koni (koniler yan yana; 0 numaralı olmayabilir)
            var cone = TrafficCone.All.Where(c => c != null && !c.IsHeld).OrderBy(c => Flat(c.BasePoint - Player.transform.position).sqrMagnitude).First();
            Vector3 basePt = cone.BasePoint;
            float yaw = cone.transform.eulerAngles.y;
            Note("koni", Interaction.CurrentHint == PlayerInteraction.HintPickCone && Highlight.Current == cone.target && Highlight.CopyCount > 0,
                "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName() + " (en yakın " + cone.name + ")");
            Interaction.Interact();
            yield return null; yield return null;
            bool held = Player.HeldCone == cone;
            Note("koni tutarken koy (hedefsiz)", held && Interaction.CurrentHint == PlayerInteraction.HintPlaceCone && Highlight.Current == null,
                "tutuyor " + held + ", ipucu \"" + Interaction.CurrentHint + "\", vurgu " + HlName());
            // Tutulan koni (hangisiyse) eski yerine
            var heldCone = Player.HeldCone;
            if (heldCone != null) { Player.HeldCone = null; heldCone.PlaceAt(heldCone == cone ? basePt : heldCone.BasePoint, heldCone == cone ? yaw : heldCone.transform.eulerAngles.y); }
            yield return null;
        }
        else Note("koni", false, "koni yok");

        // Hasta → sedyeye al: hasta vurgulu (sedye yanında)
        var p = Mission.patients.FirstOrDefault(x => x.CanBePickedUp && x.State == PatientState.LyingGround);
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
            Player.Teleport(pp, Quaternion.LookRotation(-side));
            yield return null; yield return null;
            Note("hastayı sedyeye al", Interaction.CurrentHint == PlayerInteraction.HintLoadPatient && Highlight.Current == p.transform,
                "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName());
            foreach (var st in savedStretchers) st.Item1.Teleport(st.Item2, st.Item3);

            // Sırta al (kayadaki hasta gibi): hasta vurgulu, skinned kopya var; hedef kaybolunca vurgu aynı kare kalkar
            p.onLedge = true;
            Vector3 toP = Flat(p.Position - Player.transform.position).normalized;
            Vector3 np = p.Position - toP * 0.9f;
            if (GroundProbe.Find(np + Vector3.up * 1.5f, 5f, out g)) np.y = g.point.y + 0.02f;
            Player.Teleport(np, Quaternion.LookRotation(toP));
            yield return null; yield return null;
            bool skinned = Highlight.Copies.OfType<SkinnedMeshRenderer>().Any(r => r.sharedMaterial != null && r.sharedMaterial.shader.name == InteractionHighlight.OutlineShaderName && r.bones != null && r.bones.Length > 0);
            Note("sırta al (hasta)", Interaction.CurrentHint == PlayerInteraction.HintPiggyback && Highlight.Current == p.transform && skinned,
                "ipucu \"" + Interaction.CurrentHint + "\" vurgu " + HlName() + ", skinned kopya " + skinned);
            Vector3 pside = Vector3.Cross(Vector3.up, toP);
            yield return Capture("u1_hasta_vurgu", p.Position + pside * 2.4f - toP * 1.2f + Vector3.up * 1.7f, p.Position + Vector3.up * 0.3f, 50f);
            p.onLedge = false; // hedef kayboldu
            yield return null; yield return null;
            // U3: hasta artık hedef değil; önündeki başka bir hedef (ör. hastanın değerli eşyası) vurgulanabilir
            Note("hedef kaybolunca", Highlight.Current != p.transform && Interaction.CurrentHint != PlayerInteraction.HintPiggyback,
                "ipucu \"" + Interaction.CurrentHint + "\", vurgu " + HlName() + " (" + Highlight.CopyCount + " kopya)");
        }
        else Note("hasta", false, "yerde hasta yok");

        Check("(U1-2) beyaz vurgu = ipucunun hedefi (sedye, koni, hasta); hedefsiz eylemde (bırak, koy) yok; hedef kaybolunca aynı kare kalkar", ok, string.Join("; ", parts));
    }

    // ---- (U1-3) sırttan bırakma ----
    private static IEnumerator PhaseU1PutDown(RopeAnchor rope)
    {
        var p = Mission.patients.FirstOrDefault(x => x.State == PatientState.LyingGround) ?? Mission.patients[0];
        p.onLedge = false;

        // Yürünebilir noktalar: patika (markers), yol kenarı (şeritler), banket (sedye yerleri ±1 m)
        var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
        var probes = Bolum1Coordinates.Obj(markers, "geometry_probes");
        var pool = new List<KeyValuePair<string, Vector3>>();
        foreach (var o in (List<object>)probes["path"]) pool.Add(new KeyValuePair<string, Vector3>("patika", Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(o))));
        foreach (var lane in new[] { Traffic.eastBound, Traffic.westBound })
            for (int i = 1; i <= 9; i++)
            {
                float sl = lane.Length * i / 10f;
                pool.Add(new KeyValuePair<string, Vector3>("yol", lane.PointAt(sl) + lane.RightAt(sl) * (lane.width * 0.5f - 0.3f)));
                pool.Add(new KeyValuePair<string, Vector3>("yol", lane.PointAt(sl)));
            }
        foreach (var b in e2Banket) { pool.Add(new KeyValuePair<string, Vector3>("banket", b)); pool.Add(new KeyValuePair<string, Vector3>("banket", b + Vector3.right)); pool.Add(new KeyValuePair<string, Vector3>("banket", b + Vector3.left)); }
        var walkable = new List<KeyValuePair<string, Vector3>>();
        foreach (var kv in pool)
        {
            if (!GroundProbe.Find(kv.Value + Vector3.up * 1.5f, 5f, out RaycastHit g) || Vector3.Angle(g.normal, Vector3.up) > 35f) continue;
            if (RopeAnchor.All.Any(r => r != null && r.IsBelowEdge(g.point))) continue;
            walkable.Add(new KeyValuePair<string, Vector3>(kv.Key, g.point));
        }
        var rng = new System.Random(Seed + 1);
        var picks = walkable.OrderBy(_ => rng.Next()).Take(U1PutDownSamples).ToList();
        while (picks.Count < U1PutDownSamples && walkable.Count > 0) picks.Add(walkable[rng.Next(walkable.Count)]);

        int success = 0, hlNullCount = 0;
        float worstPen = -99f, maxSlope = 0f;
        var failures = new List<string>();
        var byGroup = new Dictionary<string, int[]>();
        Time.timeScale = 2f;
        foreach (var kv in picks)
        {
            if (!byGroup.ContainsKey(kv.Key)) byGroup[kv.Key] = new int[2];
            byGroup[kv.Key][1]++;
            if (Player.Piggyback != p) Player.SetPiggyback(p);
            float yaw = (float)(rng.NextDouble() * 360.0);
            Player.Teleport(kv.Value + Vector3.up * 0.03f, Quaternion.Euler(0f, yaw, 0f));
            yield return (Func<bool>)(() => Player.HasControl);
            yield return null; yield return null;
            string hint = Interaction.CurrentHint;
            if (Highlight.Current == null) hlNullCount++;
            GroundProbe.Find(kv.Value + Vector3.up * 1f, 3f, out RaycastHit gs);
            float slope = Vector3.Angle(gs.normal, Vector3.up);
            maxSlope = Mathf.Max(maxSlope, slope);
            if (hint != PlayerInteraction.HintPutDownBack)
            {
                failures.Add(kv.Key + " " + V(kv.Value) + " " + slope.ToString("F0", Inv) + "° ipucu \"" + hint + "\"");
                continue;
            }
            Interaction.Interact();
            float end = Time.time + 3f;
            yield return (Func<bool>)(() => (p.State == PatientState.LyingGround && Player.Piggyback == null) || Time.time >= end);
            if (p.State != PatientState.LyingGround || Player.Piggyback != null)
            {
                failures.Add(kv.Key + " " + V(kv.Value) + " bırakma bitmedi (" + p.State + ")");
                continue;
            }
            yield return null;
            float pen = U1Penetration(p, out string penBone);
            worstPen = Mathf.Max(worstPen, pen);
            if (pen > 0.03f) { failures.Add(kv.Key + " " + V(kv.Value) + " zemine girdi " + Cm(pen) + " (" + penBone + ")"); continue; }
            success++;
            byGroup[kv.Key][0]++;
        }
        Time.timeScale = 1f;
        float ratio = picks.Count > 0 ? (float)success / picks.Count : 0f;
        string groups = string.Join(", ", byGroup.Select(g => g.Key + " " + g.Value[0] + "/" + g.Value[1]));
        Check("(U1-3a) sırttan bırakma: patika/yol/banket üzerinde " + picks.Count + " rastgele yürünebilir noktada (rastgele yön) başarı ≥ %95, hasta zemine girmiyor (≤ 3 cm), bırakma ipucunda vurgu yok",
            picks.Count >= U1PutDownSamples && ratio >= 0.95f && worstPen <= 0.03f && hlNullCount == picks.Count,
            "başarı " + success + "/" + picks.Count + " (%" + (ratio * 100f).ToString("F0", Inv) + "; " + groups + "), en dik zemin " + maxSlope.ToString("F0", Inv) + "°, en kötü gömülme " + Cm(Mathf.Max(0f, worstPen))
            + ", vurgusuz " + hlNullCount + "/" + picks.Count + (failures.Count > 0 ? "; başarısız: " + string.Join(" | ", failures) : ""));

        // Kaya çıkıntısı: bırakılamaz, uyarı ipucu yerinde (HUD soluk renk), vurgu yok
        if (Player.Piggyback != p) Player.SetPiggyback(p);
        Vector3 ledge = rope.climbBottom.position + new Vector3(-1.0f, 0.02f, 0.9f);
        if (GroundProbe.Find(ledge + Vector3.up * 1f, 3f, out RaycastHit gl)) ledge.y = gl.point.y + 0.02f;
        Player.Teleport(ledge, Quaternion.LookRotation(Vector3.right));
        yield return (Func<bool>)(() => Player.HasControl);
        yield return null; yield return null;
        string hintLedge = Interaction.CurrentHint;
        bool warnLedge = Interaction.HintIsWarning;
        bool hlLedge = Highlight.Current == null;
        var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        yield return null;
        bool hudWarn = hud != null && hud.HintText.text == PlayerInteraction.NoticeCannotPutDown && hud.HintText.color == MissionHud.WarningHintColor;
        Vector3 lp = Player.transform.position;
        yield return CaptureWithHud("u1_birakilamaz", lp - Vector3.right * 3.0f + Vector3.forward * 1.2f + Vector3.up * 1.7f, lp + Vector3.up * 0.9f, 50f);
        Interaction.Interact();
        yield return Seconds(0.3f);
        bool stillOnBack = Player.Piggyback == p;

        // Uçurum kenarı (patika, yüz uçuruma): ya bırakılmaz (uyarı) ya da hasta kenardan ≥ 0.95 m içeride (yan/ön yönlerden biri) yatar
        Vector3 edge = rope.edgePoint.position + Vector3.back * 0.35f;
        if (GroundProbe.Find(edge + Vector3.up * 1f, 3f, out RaycastHit ge)) edge.y = ge.point.y + 0.02f;
        Player.Teleport(edge, Quaternion.LookRotation(Vector3.forward));
        yield return (Func<bool>)(() => Player.HasControl);
        yield return null; yield return null;
        string hintEdge = Interaction.CurrentHint;
        bool edgeOk;
        string edgeInfo;
        if (hintEdge == PlayerInteraction.HintPutDownBack)
        {
            Interaction.Interact();
            float end = Time.time + 3f;
            yield return (Func<bool>)(() => (p.State == PatientState.LyingGround && Player.Piggyback == null) || Time.time >= end);
            yield return null;
            float minClear = U1Bones(p).Min(b => rope.edgePoint.position.z - b.position.z);
            float pen = U1Penetration(p, out _);
            edgeOk = p.State == PatientState.LyingGround && minClear >= 0.95f && pen <= 0.03f;
            edgeInfo = "bırakıldı, gövdenin kenara en yakın noktası " + M(minClear) + " içeride, gömülme " + Cm(Mathf.Max(0f, pen)) + ", baş yönü " + V(Flat(p.transform.up).normalized);
        }
        else
        {
            edgeOk = hintEdge == PlayerInteraction.NoticeCannotPutDown && Interaction.HintIsWarning;
            edgeInfo = "bırakılmadı, ipucu \"" + hintEdge + "\"";
        }
        Check("(U1-3b) kaya çıkıntısında bırakılmaz: \"" + PlayerInteraction.NoticeCannotPutDown + "\" ipucu yerinde uyarı renginde, vurgu yok, E bir şey yapmaz; uçurum kenarında hasta kenara 1 m'den yakın yatırılmaz",
            hintLedge == PlayerInteraction.NoticeCannotPutDown && warnLedge && hlLedge && hudWarn && stillOnBack && edgeOk,
            "kayada ipucu \"" + hintLedge + "\" (uyarı " + warnLedge + ", HUD " + hudWarn + ", vurgu yok " + hlLedge + "), E sonrası sırtta " + stillOnBack + "; kenarda " + edgeInfo);

        if (Player.Piggyback != null) { var pb = Player.ReleasePiggyback(); pb.PutDownFromBack(Player.transform.position + Vector3.back * 1.0f, Vector3.back, Vector3.up); }
        p.onLedge = false;
    }

    private static IEnumerable<Transform> U1Bones(Patient p)
    {
        var list = new List<Transform>();
        if (p.animator != null && p.animator.isHuman)
            foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Chest })
            {
                var t = p.animator.GetBoneTransform(b);
                if (t != null) list.Add(t);
            }
        if (list.Count == 0 && p.hips != null) list.Add(p.hips);
        if (list.Count == 0) list.Add(p.transform);
        return list;
    }

    // Kemik merkezlerinin zemine gömülmesi (m, + = zeminin altında); en kötüsü
    private static float U1Penetration(Patient p, out string bone)
    {
        float worst = -99f;
        bone = "-";
        foreach (var t in U1Bones(p))
        {
            if (!GroundProbe.Find(t.position + Vector3.up * 1f, 3f, out RaycastHit g)) continue;
            float pen = g.point.y - t.position.y;
            if (pen > worst) { worst = pen; bone = t.name; }
        }
        return worst;
    }

    // Capture gibi; HUD kanvası geçici olarak kameraya bağlanır ki ipucu yazısı görüntüde çıksın.
    private static IEnumerator CaptureWithHud(string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("AnimVerify_CaptureCam");
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = main != null ? main.backgroundColor : Color.gray;
        cam.farClipPlane = 2000f;
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = fov;
        go.transform.position = pos;
        go.transform.LookAt(look);
        BeforeCapture?.Invoke(cam);
        var hud = UnityEngine.Object.FindAnyObjectByType<MissionHud>();
        Canvas canvas = hud != null ? hud.ScreenCanvas : null;
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
        }
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        yield return null;
        BeforeCapture?.Invoke(cam);
        Canvas.ForceUpdateCanvases();
        cam.Render();
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
        }
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
        Line("  görüntü: " + OutDir + "/" + name + ".png");
    }
}
