using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// E2 (ikinci test turu düzeltmeleri) Play modu kontrolleri:
//  (E2-1) ön/arka taşıma klibi ve el IK'sı (el–sap ≤ 3 cm; oyuncu + bot, oyuncu arkada ve önde)
//  (E2-2) 35° eğim sınırı, kırık korkuluktan iniş → kayma + ragdoll, rampadan yürüyerek iniş/çıkış ragdoll değil,
//         eğim değişikliğinin yürünebilir yerlere etkisi (ölçüm)
//  (E2-3) rampada hastalı sedyeyle iniş/çıkış akıcı (0.5 sn pencerede hız ≥ %70), sedye zemine girmiyor
//  (E2-4) minibüsteki hastaların göstergesi tavanın üstünde yan yana; dışarıda başın üstünde
//  (E2-5) trafik camı koyu füme (renk/opaklık ölçümü), ambulans ve minibüs camı değişmemiş
public static partial class Bolum1AnimVerify
{
    private const float E2GripTolerance = 0.03f;
    private static readonly List<Vector3> e2Banket = new List<Vector3>();

    private static StretcherHandIK HandIK(Component c) { return c.GetComponent<StretcherHandIK>(); }

    // El–sap uzaklığı (iki el, en kötüsü): avuç noktası (el kemiği + 5 cm) ↔ sedyenin sap borusundaki tutma noktası.
    private static float GripError(ICarrier carrier, StretcherHandIK ik)
    {
        var s = carrier.HeldStretcher;
        if (s == null || ik == null) return float.MaxValue;
        return Mathf.Max(Vector3.Distance(ik.Palm(true), s.GripPoint(carrier, true)), Vector3.Distance(ik.Palm(false), s.GripPoint(carrier, false)));
    }

    private static List<Vector3> RampPoints()
    {
        var probes = Bolum1Coordinates.Obj(Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath), "geometry_probes");
        var list = new List<Vector3>();
        foreach (var o in (List<object>)probes["ramp"])
        {
            Vector3 p = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(o));
            if (GroundProbe.Find(p + Vector3.up * 2f, 5f, out RaycastHit g)) p.y = g.point.y;
            list.Add(p);
        }
        return list;
    }

    private static float Along(List<Vector3> path, Vector3 p)
    {
        Vector3 d = Flat(path[path.Count - 1] - path[0]).normalized;
        return Vector3.Dot(Flat(p - path[0]), d);
    }

    // Yol boyunca hedef: oyuncunun izdüşümünden 1.5 m ilerideki örnek.
    private static Vector3 Steer(List<Vector3> path, Vector3 at, bool forward)
    {
        float a = Along(path, at);
        var ordered = forward ? path : Enumerable.Reverse(path).ToList();
        foreach (var q in ordered)
        {
            float b = Along(path, q);
            if (forward ? b > a + 1.5f : b < a - 1.5f) return q;
        }
        return ordered[ordered.Count - 1];
    }

    private static void Drive(Vector3 worldDir)
    {
        worldDir.y = 0f;
        worldDir = worldDir.sqrMagnitude > 1e-6f ? worldDir.normalized : Vector3.zero;
        Player.SetMoveInput(new Vector2(worldDir.x, worldDir.z));
    }

    // 0.5 sn pencerelerde yatay hız (en düşük)
    private sealed class WindowSpeed
    {
        private readonly List<KeyValuePair<float, Vector3>> samples = new List<KeyValuePair<float, Vector3>>();
        public float Min = float.MaxValue;
        public void Add(Transform t)
        {
            samples.Add(new KeyValuePair<float, Vector3>(Time.time, t.position));
            int j = samples.Count - 1;
            for (int i = j; i >= 0; i--)
            {
                float dt = samples[j].Key - samples[i].Key;
                if (dt < 0.5f) continue;
                Min = Mathf.Min(Min, Flat(samples[j].Value - samples[i].Value).magnitude / dt);
                break;
            }
        }
    }

    // Sedye kutusunun alt yüzü (5×3 nokta) ile altındaki zemin arasındaki en kötü gömülme (+: zemine girmiş).
    private static float StretcherPenetration(StretcherController s)
    {
        var box = s.GetComponent<BoxCollider>();
        float worst = float.MinValue;
        for (int i = 0; i <= 4; i++)
            for (int k = 0; k <= 2; k++)
            {
                Vector3 local = box.center + new Vector3((k / 2f - 0.5f) * box.size.x, -0.5f * box.size.y, (i / 4f - 0.5f) * box.size.z);
                Vector3 w = s.transform.TransformPoint(local);
                if (GroundProbe.Find(w + Vector3.up * 3f, 6f, out RaycastHit g)) worst = Mathf.Max(worst, g.point.y - w.y);
            }
        return worst;
    }

    private static float SlopeAngle(Vector3 p, out bool found)
    {
        found = GroundProbe.Find(p + Vector3.up * 1.5f, 4f, out RaycastHit g);
        return found ? Vector3.Angle(g.normal, Vector3.up) : 0f;
    }

    // Ana kamerayı (OrbitCamera kapalı) çekim pozuna alır; göstergeler Camera.main'e döndüğü için.
    private static IEnumerator CaptureMain(string name, Vector3 pos, Vector3 look, float fov)
    {
        var main = Camera.main;
        var orbit = main != null ? main.GetComponent<OrbitCamera>() : null;
        bool orbitOn = orbit != null && orbit.enabled;
        Vector3 oldPos = main != null ? main.transform.position : Vector3.zero;
        Quaternion oldRot = main != null ? main.transform.rotation : Quaternion.identity;
        if (orbit != null) orbit.enabled = false;
        if (main != null) main.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        yield return null;
        yield return Capture(name, pos, look, fov);
        if (main != null) main.transform.SetPositionAndRotation(oldPos, oldRot);
        if (orbit != null) orbit.enabled = orbitOn;
    }

    // =====================================================================================
    // (E2-4) gösterge — taze sahnede, hastalar minibüsteyken
    // =====================================================================================
    private static IEnumerator PhaseE2IndicatorInVehicle()
    {
        // Batchmode'da şeffaf URP Lit varyantı eşzamansız derlenirken hiç çizilmiyordu (ölçüldü: cam görüntüde yoktu); eşzamanlı derle.
        ShaderUtil.allowAsyncCompilation = false;
        e2Banket.Clear();
        for (int i = 0; i < 2; i++) e2Banket.Add(Stretcher(i).transform.position);
        yield return Seconds(0.3f);
        var van = GameObject.Find(Bolum1SceneBuilder.MinivanName);
        var seated = Mission.patients.Where(p => p.State == PatientState.InVehicle).ToList();
        var inds = UnityEngine.Object.FindObjectsByType<MissIndicator>(FindObjectsSortMode.None);
        Bounds vb = new Bounds(van.transform.position, Vector3.zero);
        foreach (var r in van.GetComponentsInChildren<Renderer>())
            if (r.GetComponentInParent<Patient>() == null && !(r is LineRenderer)) vb.Encapsulate(r.bounds);
        var mine = seated.Select(p => inds.FirstOrDefault(m => m.patient == p)).ToList();
        bool ok = seated.Count == 2 && mine.All(m => m != null && m.Visible);
        var parts = new List<string>();
        if (ok)
        {
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = mine[i].transform.position;
                float above = at.y - vb.max.y;
                float fromCenter = Flat(at - vb.center).magnitude;
                float headGap = at.y - seated[i].Position.y;
                bool good = above >= 0.2f && fromCenter <= 2.5f;
                ok &= good;
                parts.Add(seated[i].name + " tavan +" + M(above) + ", minibüs ortasına " + M(fromCenter) + ", kalçaya göre +" + M(headGap) + (good ? "" : " [HATA]"));
            }
            var cam = Camera.main;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            float apart = Mathf.Abs(Vector3.Dot(mine[0].transform.position - mine[1].transform.position, right));
            float dy = Mathf.Abs(mine[0].transform.position.y - mine[1].transform.position.y);
            ok &= apart >= 0.6f && dy < 0.05f;
            parts.Add("yan yana aralık " + M(apart) + ", yükseklik farkı " + M(dy));
        }
        Check("(E2-4a) minibüsteki 2 hastanın göstergesi tavanın üstünde yan yana", ok,
            "InVehicle " + seated.Count + "; " + string.Join("; ", parts) + "; tavan y " + vb.max.y.ToString("F2", Inv));
        Vector3 vc = vb.center;
        var tail = MinivanTailgate.All.FirstOrDefault();
        Vector3 back = tail != null ? tail.RearDirection : Vector3.back;
        yield return CaptureMain("e2_gosterge_minibus", vc + back * 7f + Vector3.Cross(Vector3.up, back) * 3f + Vector3.up * 3.5f, vc + Vector3.up * 0.6f, 50f);
    }

    // Dışarı çıkarılan hastanın göstergesi kendi başının üstünde (PhaseRescue sonrası).
    private static void CheckE2IndicatorOutside()
    {
        var inds = UnityEngine.Object.FindObjectsByType<MissIndicator>(FindObjectsSortMode.None);
        var parts = new List<string>();
        bool ok = true;
        int n = 0;
        foreach (var p in Mission.patients)
        {
            if (p.State == PatientState.InVehicle || p.IsAboard) continue;
            var m = inds.FirstOrDefault(x => x.patient == p);
            if (m == null || !m.Visible) continue;
            float err = Vector3.Distance(m.transform.position, p.Position + Vector3.up * m.height);
            ok &= err < 0.01f;
            n++;
            parts.Add(p.name + " " + p.State + " " + (err * 100f).ToString("F1", Inv) + " cm");
        }
        Check("(E2-4b) minibüs dışındaki hastanın göstergesi kendi başının üstünde", ok && n >= 2, string.Join(", ", parts));
    }

    // =====================================================================================
    // (E2-1) ön/arka klip ve el IK'sı (patikada; oyuncu arkada, sonra önde)
    // =====================================================================================
    private static IEnumerator PhaseE2Carry()
    {
        var bot = Bot;
        bot.assistEnabled = true;
        var s = Stretcher(0);
        var p = PatientNamed("Tourist");
        // Patikada, hastanın yatış yönünde (yol boyunca) taze başlangıç: önceki fazın bıraktığı yön yamaca dönük olabilir.
        if (Player.HeldStretcher != null) { Interaction.Interact(); yield return Seconds(1f); }
        Bolum1TestPatients.LayOnPath(Mission.patients);
        yield return Seconds(0.5f);
        yield return PutPatientOnStretcher(p, s);
        yield return Seconds(0.5f);
        Vector3 startPos = s.transform.position;
        Quaternion startRot = s.transform.rotation;
        Transform oldCam = Player.cameraTransform;
        Player.cameraTransform = null;
        var pik = HandIK(Player);
        var bik = HandIK(bot);
        foreach (bool playerFront in new[] { false, true })
        {
            s.Teleport(startPos, playerFront ? startRot * Quaternion.Euler(0f, 180f, 0f) : startRot);
            TeleportPlayerToHandle(s, playerFront);
            yield return null; yield return null;
            Interaction.Interact();
            yield return Until(() => s.IsRaised, 2f, "sedye kalktı");
            yield return Seconds(0.8f);
            Vector3 heading = Flat(s.handleFront.position - s.handleRear.position).normalized;
            Drive(heading);
            float t0 = Time.time;
            int frames = 0, clipOk = 0, unreachable = 0;
            float worstPlayer = 0f, worstBot = 0f, worstShoulder = 0f;
            string worstInfo = "";
            bool captured = playerFront;
            while (Time.time - t0 < 2.4f)
            {
                yield return null;
                Drive(heading);
                if (pik.Weight < 1f || bik.Weight < 1f) continue;
                frames++;
                bool pState = PlayerAnim.InState(playerFront ? CrewAnimation.StateCarryStretcherFront : CrewAnimation.StateCarryStretcher);
                bool bState = BotAnim.InState(playerFront ? CrewAnimation.StateCarryStretcher : CrewAnimation.StateCarryStretcherFront);
                if (pState && bState) clipOk++;
                float ep = GripError(Player, pik), eb = GripError(bot, bik);
                if (Mathf.Max(ep, eb) > Mathf.Max(worstPlayer, worstBot))
                    worstInfo = " (en kötü kare t=" + (Time.time - t0).ToString("F2", Inv) + " sn, sedye " + s.CurrentMode + ", tutan oyuncu/bot " + (Player.HeldStretcher == s) + "/" + (bot.HeldStretcher == s)
                        + ", sap–işaret oyuncu " + M(Vector3.Distance(s.HandleFor(Player).position, StretcherHandIK.MarkerPoint(Player))) + " bot " + M(Vector3.Distance(s.HandleFor(bot).position, StretcherHandIK.MarkerPoint(bot))) + ", bot düştü " + bot.IsDown + ")";
                worstPlayer = Mathf.Max(worstPlayer, ep);
                worstBot = Mathf.Max(worstBot, eb);
                unreachable += pik.UnreachableHands + bik.UnreachableHands;
                worstShoulder = Mathf.Max(worstShoulder, Mathf.Max(pik.MaximumShoulderDistance, bik.MaximumShoulderDistance));
                if (!captured && Time.time - t0 > 1.6f)
                {
                    captured = true;
                    Vector3 c = s.transform.position;
                    Vector3 fwd = Flat(Player.transform.forward).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, fwd);
                    yield return Capture("e2_sedye_on_arka", c + side * 3.4f + fwd * 0.6f + Vector3.up * 1.3f, c + Vector3.up * 0.55f, 50f);
                }
            }
            Player.SetMoveInput(Vector2.zero);
            yield return Seconds(0.3f);
            string role = playerFront ? "oyuncu önde (Front), bot arkada" : "oyuncu arkada, bot önde (Front)";
            Check("(E2-1) " + role + ": klip ataması ve el–sap ≤ 3 cm (her kare)",
                frames > 30 && clipOk == frames && worstPlayer <= E2GripTolerance && worstBot <= E2GripTolerance && unreachable == 0,
                "kare " + frames + ", doğru klip " + clipOk + "; en kötü el–sap oyuncu " + (worstPlayer * 100f).ToString("F2", Inv) + " cm, bot "
                + (worstBot * 100f).ToString("F2", Inv) + " cm; ulaşılamayan el " + unreachable + "; omuz→hedef en çok " + M(worstShoulder) + worstInfo);
            // Bırak: IK birkaç karede yumuşakça kapanır
            Interaction.Interact();
            var weights = new List<float>();
            float r0 = Time.time;
            while (Time.time - r0 < 0.4f) { yield return null; weights.Add(pik.Weight); }
            bool smooth = weights.Count > 3 && weights[0] > 0f && weights[0] < 1f && weights.Last() == 0f;
            for (int i = 1; i < weights.Count; i++) smooth &= weights[i] <= weights[i - 1];
            Check("(E2-1r) bırakınca el IK'sı yumuşakça kapanır (" + (playerFront ? "önde" : "arkada") + ")", smooth,
                "ağırlık " + string.Join(" ", weights.Take(8).Select(w => w.ToString("F2", Inv))) + (weights.Count > 8 ? " …" : ""));
            yield return Seconds(1.0f);
        }
        Player.cameraTransform = oldCam;
    }

    // =====================================================================================
    // (E2-3) rampada hastalı sedye (oyuncu arkada, bot önde): iniş, dönüş, çıkış
    // =====================================================================================
    private static IEnumerator PhaseE2RampStretcher()
    {
        var bot = Bot;
        bot.assistEnabled = true;
        var s = Stretcher(0);
        var ramp = RampPoints();
        Transform oldCam = Player.cameraTransform;
        Player.cameraTransform = null;
        Vector3 down = Flat(ramp[ramp.Count - 1] - ramp[0]).normalized;
        Vector3 at = ramp[3];
        s.Teleport(at, Quaternion.LookRotation(down, Vector3.up));
        if (s.Passenger == null) throw new Exception("rampa testi: sedyede hasta yok");
        TeleportPlayerToHandle(s, false);
        yield return null; yield return null;
        Interaction.Interact();
        yield return Until(() => s.IsRaised, 2f, "rampada sedye kalktı");
        yield return Seconds(0.8f);
        int slides0 = Player.SlideStarts;
        var pik = HandIK(Player);
        var bik = HandIK(bot);
        var results = new List<string>();
        bool allGood = true;
        float worstPen = float.MinValue, worstGrip = 0f;
        foreach (bool goingDown in new[] { true, false })
        {
            var wsP = new WindowSpeed();
            var wsB = new WindowSpeed();
            float t0 = Time.time;
            bool captured = !goingDown;
            float endAlong = goingDown ? Along(ramp, ramp[ramp.Count - 3]) : Along(ramp, ramp[3]);
            // Dönüş (çıkış başı) ilk 1.2 sn ölçülmez: oyuncu 180° döner, sedye onunla döner.
            float settle = goingDown ? 0f : 1.2f;
            while (Time.time - t0 < 30f)
            {
                Vector3 target = Steer(ramp, Player.transform.position, goingDown);
                Drive(target - Player.transform.position);
                yield return null;
                if (Player.IsDown || bot.IsDown || Player.HeldStretcher != s) break;
                float a = Along(ramp, Player.transform.position);
                if (goingDown ? a >= endAlong : a <= endAlong) break;
                if (Time.time - t0 < settle) continue;
                wsP.Add(Player.transform);
                wsB.Add(bot.transform);
                worstPen = Mathf.Max(worstPen, StretcherPenetration(s));
                if (pik.Weight >= 1f && bik.Weight >= 1f) worstGrip = Mathf.Max(worstGrip, Mathf.Max(GripError(Player, pik), GripError(bot, bik)));
                if (!captured && Time.time - t0 > 6f)
                {
                    captured = true;
                    Vector3 c = s.transform.position;
                    Vector3 side = Vector3.Cross(Vector3.up, down);
                    yield return Capture("e2_rampa_sedye", c - side * 4.5f + down * 1.5f + Vector3.up * 2.2f, c + Vector3.up * 0.3f, 50f);
                }
            }
            Player.SetMoveInput(Vector2.zero);
            float target70 = Player.carryStretcherSpeed * 0.7f;
            bool good = !Player.IsDown && !bot.IsDown && Player.HeldStretcher == s && s.IsRaised && s.Passenger != null
                && wsP.Min >= target70 && wsB.Min >= target70 && Time.time - t0 < 30f;
            allGood &= good;
            results.Add((goingDown ? "iniş" : "çıkış") + " " + (Time.time - t0).ToString("F1", Inv) + " sn, 0.5 sn pencerede en düşük hız oyuncu "
                + wsP.Min.ToString("F2", Inv) + " / bot " + wsB.Min.ToString("F2", Inv) + " m/sn (eşik " + target70.ToString("F2", Inv) + ")" + (good ? "" : " [HATA]"));
            yield return Seconds(0.3f);
        }
        bool noSlide = Player.SlideStarts == slides0;
        bool noPen = worstPen <= 0.0f;
        Check("(E2-3) rampada hastalı sedye iniş + çıkış akıcı, ragdoll/kayma yok, sedye zemine girmiyor", allGood && noSlide && noPen,
            string.Join("; ", results) + "; kayma " + (Player.SlideStarts - slides0) + "; sedye alt yüzü–zemin en kötü " + (worstPen * 100f).ToString("F1", Inv)
            + " cm (+ = gömülme); rampada el–sap en kötü " + (worstGrip * 100f).ToString("F2", Inv) + " cm");
        Interaction.Interact();
        yield return Seconds(0.8f);
        Bolum1TestPatients.LayOnPath(Mission.patients);
        Player.cameraTransform = oldCam;
        ParkCarriersOffLane();
        yield return Seconds(0.5f);
    }

    // =====================================================================================
    // (E2-2) 35° eğim sınırı ve kaygan yamaç
    // =====================================================================================
    private static IEnumerator PhaseE2Slope()
    {
        var bot = Bot;
        bot.assistEnabled = true;
        Transform oldCam = Player.cameraTransform;
        Player.cameraTransform = null;
        var cc = Player.GetComponent<CharacterController>();
        Check("(E2-2a) oyuncu yürünebilir eğim sınırı 35°", Mathf.Abs(cc.slopeLimit - 35f) < 0.01f, "slopeLimit " + cc.slopeLimit.ToString("F1", Inv) + "°");

        // Test yüzeyleri: 30° çıkılır, 40° çıkılamaz ve üstünde durulamaz (kayar).
        Vector3 origin = new Vector3(400f, 0f, 300f);
        var floor = TestBox("E2Floor", origin + Vector3.down * .5f, new Vector3(60f, 1f, 60f), Quaternion.identity);
        var parts = new List<string>();
        bool ok = true;
        foreach (float angle in new[] { 30f, 40f })
        {
            float rad = angle * Mathf.Deg2Rad;
            // Yüzey z = 3'te zemine kenarsız birleşir (kutunun alt ucu zeminin içinde): kenar teması ölçümü bozmasın.
            Vector3 dir = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad)), nrm = new Vector3(0f, Mathf.Cos(rad), -Mathf.Sin(rad));
            var slope = TestBox("E2Slope" + angle, origin + new Vector3(0f, 0f, 3f) + dir * 4f - nrm * 0.1f, new Vector3(8f, .2f, 12f), Quaternion.Euler(-angle, 0f, 0f));
            Player.RespawnAt(origin + Vector3.forward * 1.5f, Quaternion.identity);
            yield return Seconds(.3f);
            float y0 = Player.transform.position.y;
            Player.SetMoveInput(Vector2.up);
            yield return Seconds(3f);
            Player.SetMoveInput(Vector2.zero);
            yield return Seconds(.5f);
            float gain = Player.transform.position.y - y0;
            // Yüzeyin ortasına koy, girdisiz bekle
            Vector3 mid = origin + new Vector3(0f, 0f, 3f) + dir * 2.5f;
            GroundProbe.Find(mid + Vector3.up * 5f, 10f, out RaycastHit mh);
            int s0 = Player.SlideStarts;
            Player.Teleport(mh.point + Vector3.up * .03f, Quaternion.identity);
            yield return Seconds(1.5f);
            float drop = mh.point.y - Player.transform.position.y;
            bool slid = Player.SlideStarts > s0 && drop > 0.5f;
            bool good = angle < 35f ? gain > 1.0f && !slid : gain < 0.5f && slid;
            ok &= good && !Player.IsDown;
            parts.Add(angle.ToString("F0", Inv) + "°: yürüyerek yükselme " + M(gain) + ", üstünde bırakılınca kayma " + slid + " (" + M(drop) + " aşağı)" + (good ? "" : " [HATA]"));
            UnityEngine.Object.Destroy(slope);
            yield return null;
        }
        Check("(E2-2b) 30° yüzey çıkılır; 40° yüzey çıkılamaz, üstünde kayılır", ok, string.Join("; ", parts));
        UnityEngine.Object.Destroy(floor);

        // Kırık korkuluktan (Blender X ≈ 1) patikaya yürüyerek inme denemesi → kayma + ragdoll
        Vector3 start = Bolum1Coordinates.ToUnity(new Vector3(1f, -4.1f, 0f));
        GroundProbe.Find(start + Vector3.up * 5f, 10f, out RaycastHit sh);
        start = sh.point + Vector3.up * .02f;
        Vector3 downhill = Bolum1Coordinates.ToUnity(new Vector3(1f, -12.7f, -9f)) - start;
        Player.RespawnAt(start, Quaternion.LookRotation(Flat(downhill).normalized));
        yield return Seconds(.4f);
        int slides0 = Player.SlideStarts;
        float topY = Player.transform.position.y;
        Drive(downhill);
        float t0 = Time.time;
        bool captured = false;
        float slideFrom = float.NaN;
        while (Time.time - t0 < 10f && !Player.IsDown)
        {
            yield return null;
            if (Player.IsSliding && float.IsNaN(slideFrom)) slideFrom = Player.transform.position.y;
            if (!captured && Player.IsSliding && topY - Player.transform.position.y > 2.5f)
            {
                captured = true;
                Vector3 c = Player.transform.position;
                Vector3 side = Vector3.Cross(Vector3.up, Flat(downhill).normalized);
                yield return Capture("e2_yamac_kayma", c + side * 6f - Flat(downhill).normalized * 2f + Vector3.up * 2.5f, c + Vector3.up * 0.4f, 55f);
            }
        }
        Player.SetMoveInput(Vector2.zero);
        bool rag = Player.IsDown;
        float fell = topY - Player.transform.position.y;
        yield return Seconds(0.5f);
        Vector3 restAt = Player.knockdown != null ? Player.knockdown.GroundPosition : Player.transform.position;
        bool onPath = Mathf.Abs(restAt.y - (-9f)) < 1.0f;
        Check("(E2-2c) kırık korkuluktan aşağı yürüme: kayar, patikada ragdoll (görünmez duvar yok)", Player.SlideStarts > slides0 && rag && Player.LastLandingDrop > 3f && onPath,
            "kayma başladı " + (Player.SlideStarts - slides0) + " kez (ilk kayma y " + slideFrom.ToString("F2", Inv) + "), düşüş " + M(fell) + ", son basıştan " + M(Player.LastLandingDrop)
            + ", ragdoll " + rag + ", durduğu y " + restAt.y.ToString("F2", Inv) + (captured ? "" : " (görüntü yok)"));
        Player.RespawnAt(parkPosition, Quaternion.identity);
        yield return Seconds(0.5f);

        // Rampadan yürüyerek iniş ve çıkış: ragdoll ve kayma yok, takılma yok
        var ramp = RampPoints();
        Player.RespawnAt(ramp[0] + Vector3.up * .02f, Quaternion.LookRotation(Flat(ramp[ramp.Count - 1] - ramp[0]).normalized));
        yield return Seconds(.4f);
        int rs0 = Player.SlideStarts;
        var rparts = new List<string>();
        bool rok = true;
        foreach (bool goingDown in new[] { true, false })
        {
            var ws = new WindowSpeed();
            float r0 = Time.time;
            float endAlong = goingDown ? Along(ramp, ramp[ramp.Count - 1]) - 0.3f : Along(ramp, ramp[0]) + 0.3f;
            float y0 = Player.transform.position.y;
            while (Time.time - r0 < 25f && !Player.IsDown)
            {
                Drive(Steer(ramp, Player.transform.position, goingDown) - Player.transform.position);
                yield return null;
                float a = Along(ramp, Player.transform.position);
                if (goingDown ? a >= endAlong : a <= endAlong) break;
                if (Time.time - r0 > 0.6f) ws.Add(Player.transform);
            }
            Player.SetMoveInput(Vector2.zero);
            float dy = Player.transform.position.y - y0;
            bool good = !Player.IsDown && ws.Min >= Player.moveSpeed * 0.7f && Mathf.Abs(dy) > 6f;
            rok &= good;
            rparts.Add((goingDown ? "iniş " : "çıkış ") + M(dy) + " / " + (Time.time - r0).ToString("F1", Inv) + " sn, en düşük pencere hızı " + ws.Min.ToString("F2", Inv) + " m/sn" + (good ? "" : " [HATA]"));
            yield return Seconds(0.3f);
        }
        Check("(E2-2d) rampadan yürüyerek iniş ve çıkış: ragdoll ve kayma yok", rok && Player.SlideStarts == rs0,
            string.Join("; ", rparts) + "; kayma " + (Player.SlideStarts - rs0));

        // Eğim değişikliğinin yürünebilir yerlere etkisi: zemin eğimi (35–45° arası yeni yasak) ve 0.6 sn durunca kayma
        yield return SurveyWalkable();
        Player.cameraTransform = oldCam;
        ParkCarriersOffLane();
    }

    private static IEnumerator SurveyWalkable()
    {
        var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
        var probes = Bolum1Coordinates.Obj(markers, "geometry_probes");
        var groups = new List<KeyValuePair<string, List<Vector3>>>();
        groups.Add(new KeyValuePair<string, List<Vector3>>("patika", ((List<object>)probes["path"]).Select(o => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(o))).ToList()));
        groups.Add(new KeyValuePair<string, List<Vector3>>("rampa", RampPoints()));
        var ledge = Bolum1Coordinates.Obj(probes, "ledge");
        Vector3 lmin = Bolum1Coordinates.Vec3(ledge, "min"), lmax = Bolum1Coordinates.Vec3(ledge, "max");
        var lp = new List<Vector3>();
        for (int i = 0; i <= 4; i++)
            for (int k = 0; k <= 2; k++)
                lp.Add(Bolum1Coordinates.ToUnity(new Vector3(Mathf.Lerp(lmin.x + .5f, lmax.x - .5f, i / 4f), Mathf.Lerp(lmin.y + .5f, lmax.y - .5f, k / 2f), lmax.z)));
        groups.Add(new KeyValuePair<string, List<Vector3>>("kaya çıkıntısı", lp));
        var road = new List<Vector3>();
        var shoulder = new List<Vector3>();
        foreach (var lane in new[] { Traffic.eastBound, Traffic.westBound })
            for (int i = 1; i <= 9; i++)
            {
                float sl = lane.Length * i / 10f;
                road.Add(lane.PointAt(sl) + lane.RightAt(sl) * (lane.width * 0.5f - 0.3f));
                shoulder.Add(lane.PointAt(sl) + lane.RightAt(sl) * (lane.width * 0.5f + 0.5f));
            }
        groups.Add(new KeyValuePair<string, List<Vector3>>("yol kenarı", road));
        groups.Add(new KeyValuePair<string, List<Vector3>>("yol dışı şerit (+0.5 m)", shoulder));
        var banket = new List<Vector3>();
        foreach (var b in e2Banket) { banket.Add(b); banket.Add(b + Vector3.right * 1f); banket.Add(b + Vector3.left * 1f); }
        groups.Add(new KeyValuePair<string, List<Vector3>>("banket (sedye yeri)", banket));
        var vanBack = new List<Vector3>();
        var tail = MinivanTailgate.All.FirstOrDefault();
        if (tail != null)
        {
            Vector3 r = tail.rearPoint.position, d = tail.RearDirection, side = Vector3.Cross(Vector3.up, d);
            foreach (float back in new[] { 0.6f, 1.2f, 1.8f })
                foreach (float lat in new[] { -0.7f, 0f, 0.7f }) vanBack.Add(r + d * back + side * lat);
        }
        groups.Add(new KeyValuePair<string, List<Vector3>>("minibüs arkası", vanBack));
        var ropeBottom = new List<Vector3>();
        foreach (var rope in RopeAnchor.All)
            if (rope != null && rope.climbBottom != null)
            {
                Vector3 b = rope.climbBottom.position;
                ropeBottom.Add(b); ropeBottom.Add(b + Vector3.right * .6f); ropeBottom.Add(b + Vector3.left * .6f);
            }
        groups.Add(new KeyValuePair<string, List<Vector3>>("halat dibi", ropeBottom));

        var lines = new List<string>();
        int totalNew = 0, totalSlide = 0, total = 0;
        var newSteepSpots = new List<string>();
        foreach (var g in groups)
        {
            int n = 0, steepNew = 0, steepOld = 0, slid = 0, slidFlat = 0;
            float maxAngle = 0f;
            foreach (var q in g.Value)
            {
                float ang = SlopeAngle(q, out bool found);
                if (!found) continue;
                n++;
                maxAngle = Mathf.Max(maxAngle, ang);
                if (ang > 45f) steepOld++;
                else if (ang > 35f) { steepNew++; newSteepSpots.Add(g.Key + " " + V(q) + " " + ang.ToString("F0", Inv) + "°"); }
                GroundProbe.Find(q + Vector3.up * 1.5f, 4f, out RaycastHit h);
                int s0 = Player.SlideStarts;
                Player.RespawnAt(h.point + Vector3.up * .03f, Quaternion.identity);
                yield return Seconds(0.6f);
                if (Player.SlideStarts > s0 || Player.IsDown) { slid++; if (ang <= 35f) slidFlat++; }
            }
            total += n; totalNew += steepNew; totalSlide += slid;
            lines.Add(g.Key + ": " + n + " nokta, en dik " + maxAngle.ToString("F0", Inv) + "°, 35–45° (yeni yasak) " + steepNew + ", >45° " + steepOld + ", durunca kayan " + slid + " (≤35° zeminde " + slidFlat + ")");
        }
        foreach (var l in lines) Line("    " + l);
        if (newSteepSpots.Count > 0) Line("    35–45° noktalar: " + string.Join("; ", newSteepSpots));
        // Ölçüm raporu: patika, rampa, kaya çıkıntısı, minibüs arkası ve halat dibinde kayma olmamalı.
        var mustWalk = new[] { "patika", "rampa", "kaya çıkıntısı", "minibüs arkası", "halat dibi", "banket (sedye yeri)", "yol kenarı" };
        bool ok = lines.Where(l => mustWalk.Any(m => l.StartsWith(m + ":"))).All(l => l.EndsWith("zeminde 0)"));
        Check("(E2-2e) eğim 35° etkisi: yol kenarı, banket, patika, rampa, kaya çıkıntısı, minibüs arkası, halat dibi yürünebilir (≤35° zeminde kayma yok)", ok,
            total + " nokta; 35–45° " + totalNew + "; durunca kayan " + totalSlide + " (ayrıntı yukarıda)");
        Player.RespawnAt(parkPosition, Quaternion.identity);
        yield return Seconds(0.3f);
    }

    // =====================================================================================
    // (E2-5) trafik camı: koyu füme; ambulans ve minibüs camı değişmez
    // =====================================================================================
    private static readonly TrafficVehicleType[] E2TrafficTypes = { TrafficVehicleType.Sedan, TrafficVehicleType.Hatchback, TrafficVehicleType.Pickup, TrafficVehicleType.PanelVan, TrafficVehicleType.BoxTruck };

    private static void CheckE2GlassMaterials()
    {
        var smoke = AssetDatabase.LoadAssetAtPath<Material>(Bolum1TrafficBuilder.MaterialFolder + "/M_Traffic_SmokeGlass.mat");
        var parts = new List<string>();
        bool ok = smoke != null;
        if (smoke != null)
        {
            Color c = smoke.GetColor("_BaseColor");
            bool transparent = smoke.GetFloat("_Surface") == 1f && smoke.renderQueue >= 3000 && smoke.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
            bool dark = c.r < 0.12f && c.g < 0.12f && c.b < 0.14f && c.b >= c.r;
            bool opacity = c.a >= 0.75f && c.a <= 0.85f;
            ok &= transparent && dark && opacity;
            parts.Add("M_Traffic_SmokeGlass RGB " + c.r.ToString("F3", Inv) + "/" + c.g.ToString("F3", Inv) + "/" + c.b.ToString("F3", Inv) + " (#"
                + ColorUtility.ToHtmlStringRGB(c) + "), opaklık " + c.a.ToString("F2", Inv) + ", saydam " + transparent + ", parlaklık " + smoke.GetFloat("_Smoothness").ToString("F2", Inv));
        }
        foreach (var t in E2TrafficTypes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabFolder + "/Vehicle_" + t + ".prefab");
            var glass = prefab != null ? prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith("_Glass")) : null;
            var mats = glass != null ? glass.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToList() : new List<Material>();
            bool good = mats.Count > 0 && mats.All(m => m == smoke);
            ok &= good;
            parts.Add(t + " cam " + mats.Count + " malzeme" + (good ? " füme" : " [HATA: " + string.Join(",", mats.Select(m => m != null ? m.name : "null")) + "]"));
        }
        // Ambulans ve minibüs: cam malzemeleri füme değil (bu işte değiştirilmedi)
        var amb = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulancePrefabPath);
        var van = GameObject.Find(Bolum1SceneBuilder.MinivanName);
        foreach (var root in new[] { amb, van })
        {
            if (root == null) { ok = false; parts.Add("araç yok"); continue; }
            var glassMats = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials.Where(m => m != null && (r.name.Contains("Glass") || m.name.Contains("Glass"))))
                .Distinct().ToList();
            bool untouched = glassMats.Count > 0 && glassMats.All(m => m != smoke);
            ok &= untouched;
            parts.Add(root.name + " cam: " + string.Join(", ", glassMats.Select(m => m.name + " #" + ColorUtility.ToHtmlStringRGBA(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color))) + (untouched ? " (değişmedi)" : " [HATA]"));
        }
        Check("(E2-5a) 5 trafik aracında cam koyu füme (%75–85 opak), ambulans ve minibüs camı değişmedi", ok, string.Join("; ", parts));
    }

    // Ekran ölçümü: aynı kare cam açık ve kapalı çizilir; farklı pikseller camdır. Camlı ortalama renk ve camsızla fark.
    private static IEnumerator PhaseE2GlassRender()
    {
        CheckE2GlassMaterials();
        var lane = Traffic.eastBound;
        float s = lane.Length * 0.3f;
        var parts = new List<string>();
        bool ok = true;
        foreach (var t in E2TrafficTypes)
        {
            var v = Traffic.ForceSpawn(true, t);
            v.enabled = false;
            v.Follower.Place(lane, s, 0f);
            yield return null;
            var glass = v.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith("_Glass"));
            var renderers = glass != null ? glass.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            Bounds gb = renderers.Length > 0 ? renderers[0].bounds : new Bounds(v.transform.position + Vector3.up, Vector3.one);
            foreach (var r in renderers) gb.Encapsulate(r.bounds);
            Vector3 side = v.transform.right;
            Vector3 camPos = gb.center + side * (gb.extents.magnitude + 2.2f) + Vector3.up * 0.3f + v.transform.forward * 0.4f;
            yield return RenderPixels(camPos, gb.center, 45f, 480, 270);
            Color[] with = e2Pixels;
            // Aynı karede renderer.enabled değişikliği çizime yansımıyor (ölçüldü); cam görünmez malzemeyle değiştirilir.
            var shown = renderers.Select(r => r.sharedMaterials).ToArray();
            var invisible = new Material(renderers.Length > 0 ? renderers[0].sharedMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit")));
            invisible.SetColor("_BaseColor", Color.clear);
            foreach (var r in renderers) r.sharedMaterials = r.sharedMaterials.Select(_ => invisible).ToArray();
            yield return RenderPixels(camPos, gb.center, 45f, 480, 270);
            Color[] without = e2Pixels;
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = shown[i];
            UnityEngine.Object.Destroy(invisible);
            int n = 0;
            Color sumWith = Color.clear, sumWithout = Color.clear;
            for (int i = 0; i < with.Length; i++)
            {
                if (Mathf.Abs(with[i].r - without[i].r) + Mathf.Abs(with[i].g - without[i].g) + Mathf.Abs(with[i].b - without[i].b) < 0.03f) continue;
                n++;
                sumWith += with[i];
                sumWithout += without[i];
            }
            Color avgWith = n > 0 ? sumWith / n : Color.clear, avgWithout = n > 0 ? sumWithout / n : Color.clear;
            float lumWith = 0.2126f * avgWith.r + 0.7152f * avgWith.g + 0.0722f * avgWith.b;
            float lumWithout = 0.2126f * avgWithout.r + 0.7152f * avgWithout.g + 0.0722f * avgWithout.b;
            bool good = n > 200 && lumWith < 0.2f && avgWith.b >= avgWith.r - 0.01f;
            ok &= good;
            parts.Add(t + ": cam pikseli " + n + ", ort. RGB " + avgWith.r.ToString("F2", Inv) + "/" + avgWith.g.ToString("F2", Inv) + "/" + avgWith.b.ToString("F2", Inv)
                + " (#" + ColorUtility.ToHtmlStringRGB(avgWith) + "), parlaklık camlı " + lumWith.ToString("F2", Inv) + " / camsız " + lumWithout.ToString("F2", Inv) + (good ? "" : " [HATA]"));
            if (t == TrafficVehicleType.Sedan)
                yield return Capture("e2_trafik_cam", camPos - side * 0.6f, gb.center, 45f);
            Traffic.ClearVehicles();
            yield return null;
        }
        if (e2Cam != null) UnityEngine.Object.Destroy(e2Cam.gameObject);
        Check("(E2-5b) trafik camı ekranda koyu gri-mavi; içerisi görünmez (renk ölçümü)", ok, string.Join("; ", parts));
    }

    // Yeni oluşturulan kameranın aynı karedeki ilk çizimi şeffafları atlıyordu (ölçüldü); kamera bir kez kurulur,
    // her çizimden önce iki kare beklenir (zaman durdurulur, araç hareketsiz).
    private static Camera e2Cam;
    private static Color[] e2Pixels;
    private static IEnumerator RenderPixels(Vector3 pos, Vector3 look, float fov, int w, int h)
    {
        if (e2Cam == null)
        {
            var go = new GameObject("E2_GlassCam");
            e2Cam = go.AddComponent<Camera>();
            var main = Camera.main;
            e2Cam.clearFlags = CameraClearFlags.SolidColor;
            e2Cam.backgroundColor = main != null ? main.backgroundColor : Color.gray;
            e2Cam.nearClipPlane = 0.05f;
            e2Cam.farClipPlane = 2000f;
            e2Cam.enabled = false;
        }
        e2Cam.fieldOfView = fov;
        e2Cam.transform.position = pos;
        e2Cam.transform.LookAt(look);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        e2Cam.targetTexture = rt;
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return null;
        yield return null;
        e2Cam.Render();
        Time.timeScale = prevScale;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        e2Pixels = tex.GetPixels();
        e2Cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }
}
