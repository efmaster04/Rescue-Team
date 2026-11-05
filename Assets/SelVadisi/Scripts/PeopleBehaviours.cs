using UnityEngine;

namespace SelVadisi
{
    /// The behaviour library: each behaviour writes a Pose on top of the standing base.
    /// Formulas follow the animation spec (B01..B28); t is absolute time, bt the time since the behaviour started.
    public partial class PeopleAnimator
    {
        const float TAU = Mathf.PI * 2f;

        static ref ArmCmd Arm(ref Pose ps, int side) { if (side < 0) return ref ps.armL; return ref ps.armR; }
        static ref LegCmd Leg(ref Pose ps, int side) { if (side < 0) return ref ps.legL; return ref ps.legR; }

        static ArmCmd FK(float plane, float elev, float twist, float flex)
            => new ArmCmd { plane = plane, elev = elev, twist = twist, flex = flex, curl = 30f, index = 25f, thumb = 15f };

        static ArmCmd IK(byte sp, Vector3 palm, byte dsp, Vector3 F, Vector3 P, byte esp, Vector3 elbow)
            => new ArmCmd { ik = true, aim = true, palm = palm, palmSp = sp, F = F, P = P, dirSp = dsp, elbow = elbow, elbowSp = esp, curl = 30f, index = 25f, thumb = 15f };

        static void HandShape(ref ArmCmd a, float curl, float index, float thumb) { a.curl = curl; a.index = index; a.thumb = thumb; }
        static void Open(ref ArmCmd a) => HandShape(ref a, 4f, 0f, 8f);
        static void Grip(ref ArmCmd a) => HandShape(ref a, 80f, 80f, 55f);
        static void Flat(ref ArmCmd a) { HandShape(ref a, 8f, 6f, 12f); a.wflex = -15f; }

        static float N(Person p, float f, float t, int k) => RigMath.Noise(t * f, p.seed * 31 + k);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Pulse01(float x) => Mathf.Max(0f, x);

        static ArmCmd LerpArm(ArmCmd a, ArmCmd b, float w)
        {
            a.plane = Lerp(a.plane, b.plane, w); a.elev = Lerp(a.elev, b.elev, w); a.twist = Lerp(a.twist, b.twist, w);
            a.flex = Lerp(a.flex, b.flex, w); a.wdev = Lerp(a.wdev, b.wdev, w); a.curl = Lerp(a.curl, b.curl, w);
            a.index = Lerp(a.index, b.index, w); a.thumb = Lerp(a.thumb, b.thumb, w);
            return a;
        }

        /// Burst envelope: on for 'on' seconds, off for 'off', smoothed over 'fade'.
        static float Burst(float t, float on, float off, float phase, float fade = 0.35f)
        {
            float cyc = on + off;
            float u = Mathf.Repeat(t + phase * cyc, cyc);
            if (u >= on) return 0f;
            return RigMath.SS(0f, fade, u) * (1f - RigMath.SS(on - fade, on, u));
        }

        // ------------------------------------------------------------------ base

        Pose EvalFull(Person p, State s, Beh b, float t, float bt, float[] r, int index)
        {
            var ps = StandBase(p, s, t);
            bool cane = (ps.props & (1 << (int)PropKind.Cane)) != 0;
            Vector3 grip = cane ? Arm(ref ps, p.domSide).palm : Vector3.zero;
            Behave(b, p, s, ref ps, t, bt, r, index);
            if (cane)
            {
                ref ArmCmd d = ref Arm(ref ps, p.domSide);
                if (!d.ik || d.palmSp != SpR || (d.palm - grip).sqrMagnitude > 1e-6f) ps.props &= ~(1 << (int)PropKind.Cane);
            }
            if (p.umbrella && b != Beh.Sit && b != Beh.Kneel && b != Beh.Crouch && b != Beh.RestKnees && b != Beh.Walk)
            {
                int u = p.umbrellaSide;
                var a = IK(SpC, new Vector3(u * 0.17f, -0.22f, 0.20f) * p.s, SpC, new Vector3(-u * 0.1f, -0.2f, 1f), new Vector3(-u, 0f, 0f), SpC, new Vector3(u * 0.45f, -0.85f, -0.25f));
                a.aim = false; a.pron = 70f;
                HandShape(ref a, 85f, 85f, 50f);
                Arm(ref ps, u) = a;
                ps.headPitch += 3f; ps.armL.shrug += 1.5f; ps.armR.shrug += 1.5f;
                ps.props |= 1 << (int)PropKind.Umbrella;
            }
            AddLayers(p, s, ref ps, t);
            return ps;
        }

        static bool Elder(Person p) => p.role == Role.ElderMan || p.role == Role.ElderWoman;

        Pose StandBase(Person p, State s, float t)
        {
            var ps = new Pose();
            float sl = p.slouch;
            ps.pelvisOfs = new Vector3(0f, -0.012f * p.s, 0f);
            ps.pelvisTilt = Elder(p) ? 6f : 3f;
            ps.spineBend = -2f + 0.4f * sl;
            ps.chestBend = 0.6f * sl;
            ps.neckBend = 4f + 0.3f * sl;
            ps.headPitch = -5f - 0.9f * sl + p.habitPitch;
            ps.headRoll = p.habitRoll;
            ps.lookW = 1f; ps.shiftW = 1f; ps.yawLimit = 75f; ps.eyesOpen = 1f; ps.worry = p.worry;
            ps.legL.toeOut = ps.legR.toeOut = p.toeOut;
            ps.armL = RestArm(p, s, s.restStyle, -1, t);
            ps.armR = RestArm(p, s, s.restStyle, 1, t);
            if (p.props != null)
                for (int i = 0; i < p.props.Length; i++)
                    if (p.props[i] != null && p.props[i].kind == PropKind.Cane) { CaneArm(p, ref ps); break; }
            return ps;
        }

        static int PickRestStyle(Person p, System.Random r)
        {
            // Hang, Pockets, BehindBack, ArmsCrossed, ClaspedFront, HandsOnHips, OnePocket+Hang
            float[] w;
            switch (p.role)
            {
                case Role.Woman: w = new float[] { 20, 5, 2, 18, 30, 10, 15 }; break;
                case Role.ElderMan: w = new float[] { 10, 10, 40, 12, 8, 0, 20 }; break;
                case Role.ElderWoman: w = new float[] { 15, 0, 10, 15, 35, 5, 20 }; break;
                case Role.Teen: w = new float[] { 25, 35, 0, 20, 0, 10, 10 }; break;
                case Role.Child: w = new float[] { 45, 15, 0, 10, 10, 5, 15 }; break;
                case Role.Worker: w = new float[] { 15, 10, 5, 20, 0, 35, 15 }; break;
                default: w = new float[] { 20, 25, 8, 20, 5, 12, 10 }; break;
            }
            if (!p.pockets) { w[4] += w[1]; w[1] = 0f; w[4] += w[6]; w[6] = 0f; }
            float tot = 0f; foreach (var x in w) tot += x;
            float u = (float)r.NextDouble() * tot;
            for (int i = 0; i < w.Length; i++) { u -= w[i]; if (u <= 0f) return i; }
            return 0;
        }

        ArmCmd RestArm(Person p, State s, int style, int side, float t)
        {
            int sD = p.domSide;
            ArmCmd a;
            switch (style)
            {
                case 1: // pockets
                    a = IK(SpP, (side < 0 ? s.pocketLP : s.pocketRP) + new Vector3(0f, -0.02f, -0.01f), SpR, new Vector3(0f, -1f, 0.25f), new Vector3(-side, 0f, 0.3f), SpR, new Vector3(side * 0.6f, -0.35f, -0.7f));
                    a.curl = 40f; a.index = 40f; return a;
                case 2: // behind the back
                    if (side == sD) a = IK(SpP, s.smallBackP + new Vector3(sD * 0.03f, -0.05f, -0.03f), SpR, new Vector3(-sD, -0.3f, 0f), new Vector3(0f, 0f, 1f), SpR, new Vector3(sD * 0.55f, -0.3f, -0.8f));
                    else a = IK(SpP, s.smallBackP + new Vector3(-sD * 0.02f, -0.08f, -0.035f), SpR, new Vector3(sD * 0.3f, -1f, 0f), new Vector3(0f, 0f, 1f), SpR, new Vector3(side * 0.55f, -0.3f, -0.8f));
                    return a;
                case 3: // arms crossed
                    if (side == sD) a = IK(SpC, s.sternumC + new Vector3(-sD * 0.17f, -0.07f, -0.03f) * p.s, SpC, new Vector3(-sD, -0.15f, 0f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sD * 0.45f, -0.8f, 0.4f));
                    else a = IK(SpC, s.sternumC + new Vector3(sD * 0.14f, -0.15f, -0.05f) * p.s, SpC, new Vector3(0f, -0.2f, -1f), new Vector3(-sD, 0f, 0f), SpC, new Vector3(-sD * 0.45f, -0.8f, 0.35f));
                    a.curl = 45f; a.index = 45f; return a;
                case 4: // hands clasped in front
                    a = IK(SpS, s.bellyS + new Vector3(side * 0.03f, -0.12f, 0.05f) * p.s, SpR, new Vector3(-side * 0.3f, -0.9f, 0.2f), new Vector3(-side, 0f, 0.3f), SpR, new Vector3(side * 0.35f, -0.85f, -0.35f));
                    a.curl = 45f; return a;
                case 5: // hands on hips
                    a = IK(SpP, (side < 0 ? s.hipSideLP : s.hipSideRP) + new Vector3(side * 0.035f, 0.02f, 0f), SpR, new Vector3(-side * 0.15f, -0.55f, 0.82f), new Vector3(-side, 0f, 0f), SpR, new Vector3(side, -0.15f, -0.35f));
                    a.thumb = 60f; a.curl = 20f; return a;
                case 6: // one pocket, other hanging
                    if (side == sD && p.pockets) return RestArm(p, s, 1, side, t);
                    return RestArm(p, s, 0, side, t);
                default: // hang
                    a = FK(-4f, 4f + p.hipClear, -8f, 12f + 4f * N(p, 0.2f, t, side + 5));
                    return a;
            }
        }

        static void CaneArm(Person p, ref Pose ps)
        {
            int sD = p.domSide;
            Vector3 tip = new Vector3(sD * 0.24f, 0f, 0.32f) * p.s;
            Vector3 grip = tip + new Vector3(0f, 0.86f * p.s, -0.04f);
            var a = IK(SpR, grip, SpR, new Vector3(-sD * 0.2f, 0f, 1f), new Vector3(0f, -1f, 0f), SpR, new Vector3(sD * 0.6f, -0.6f, -0.5f));
            Grip(ref a);
            Arm(ref ps, sD) = a;
            ps.chestLean += sD * 2f; ps.pelvisOfs.x += sD * 0.015f;
            ps.props |= 1 << (int)PropKind.Cane;
        }

        Vector3 Shoulder(State s, int side) => s.restRS[SVSkel.UpArm(side)];

        /// Point on a partner's skeleton as currently posed (offset in the partner's root space), in my root space.
        /// Before the partner is first posed its bones sit at rest, which gives the rest-skeleton point.
        Vector3 PartnerPoint(Person me, int partner, int bone, Vector3 offset)
        {
            var o = people[partner];
            Transform bt = o.bones[bone];
            Vector3 w = bt != null ? bt.position + o.root.rotation * offset : o.root.TransformPoint(st[partner].restRS[bone] + offset);
            return ToRoot(me, w);
        }

        // ------------------------------------------------------------------ behaviours

        void Behave(Beh b, Person p, State s, ref Pose ps, float t, float bt, float[] r, int index)
        {
            int sD = p.domSide, sO = -sD;
            float k = p.tempo, a = p.amp, E = s.energy;
            float ph0 = (p.seed % 997) * 0.61803f;
            switch (b)
            {
                case Beh.Stand: break;

                case Beh.WaveBoth:
                {
                    float f = (0.85f + 0.5f * E) * k;
                    float phi = TAU * f * (t + ph0);
                    float phiO = TAU * f * 1.06f * (t + ph0) + 0.6f + 1.6f * r[3];
                    float u = 0.5f - 0.5f * Mathf.Cos(phi), uO = 0.5f - 0.5f * Mathf.Cos(phiO - 0.25f), rock = Mathf.Sin(phi * 0.5f);
                    var d = FK(22f + 6f * u, 138f + 37f * u * a, 72f, 10f + 42f * u * u); d.wdev = 14f * Mathf.Sin(phi + 1.9f); Open(ref d);
                    var o = FK(12f + 4f * uO, 132f + 33f * uO * a * 0.8f, 90f, 22f + 35f * uO * uO); o.wdev = 14f * Mathf.Sin(phiO + 1.65f); Open(ref o);
                    Arm(ref ps, sD) = d; Arm(ref ps, sO) = o;
                    ps.pelvisOfs += new Vector3(0.02f * rock, -0.035f * (1f - u) * a, 0f);
                    ps.pelvisRoll += 3f * rock;
                    ps.legL.plantar = ps.legR.plantar = 8f * RigMath.SS(0.75f, 1f, u);
                    ps.shiftW = 0f;
                    ps.spineBend += -3f - 3f * u; ps.spineLean += 2f * rock;
                    ps.chestBend += -4f - 3f * u; ps.chestLean += 2.5f * rock; ps.chestTwist += 4f * Mathf.Sin(phi * 0.5f + 0.5f);
                    ps.headPitch = -12f + 3f * Mathf.Sin(phi + 1.2f); ps.headRoll += -1.5f * rock;
                    ps.lookW = 0.7f; ps.worry = 0.8f;
                    float shout = Burst(bt, Lerp(1.5f, 3f, r[0]), Lerp(1f, 2f, r[1]), r[2], 0.15f);
                    ps.mouth = Mathf.Lerp(0.05f, 0.55f + 0.45f * Pulse01(Mathf.Sin(phi)), shout);
                    break;
                }

                case Beh.WaveOne:
                {
                    float f = (0.9f + 0.5f * E) * k;
                    ArmCmd act;
                    if (r[0] < 0.6f)
                    {
                        float u = RigMath.Pump(f, t, ph0);
                        act = FK(20f, 125f + 50f * u * a, 80f, 12f + 30f * u); act.wdev = 18f * Mathf.Sin(TAU * f * t + 1.9f);
                    }
                    else
                    {
                        float f2 = Lerp(1.6f, 2.4f, r[1]) * k;
                        act = FK(25f + 4f * N(p, 0.3f, t, 1), 150f + 6f * N(p, 0.25f, t, 2), 85f, 35f + 30f * Mathf.Sin(TAU * f2 * t) * a);
                        act.wdev = -20f * Mathf.Cos(TAU * f2 * t);
                    }
                    Open(ref act);
                    // bursts: wave 2.5-6 s, then rest the arm at shoulder height 1.5-4 s
                    var rest = FK(30f, 80f + 20f * r[4], 60f, 75f); rest.curl = 25f;
                    float env = Burst(bt, Lerp(2.5f, 6f, r[2]), Lerp(1.5f, 4f, r[3]), r[5]);
                    Arm(ref ps, sD) = LerpArm(rest, act, env);
                    ps.pelvisOfs.x += sD * 0.02f;
                    ps.chestLean += -sD * 4f * env + 2f * Mathf.Sin(Mathf.PI * f * t);
                    ps.spineLean += -sD * 2f * env;
                    ps.headRoll += -sD * 4f * env; ps.headPitch = -8f;
                    ps.lookW = 0.8f; ps.worry = 0.7f;
                    ps.mouth = (Mathf.FloorToInt(f * t) % 3 == 0 ? 0.5f : 0.05f) * env;
                    // the other arm: rail, baby, child's hand, hand on chest, or hanging (the phone stays in the dominant hand)
                    if (p.hasRail) Arm(ref ps, sO) = RailArm(p, s, sO);
                    else if (HasProp(p, PropKind.Baby)) { Arm(ref ps, sO) = CradleArm(p, s, sO); ps.props |= 1 << (int)PropKind.Baby; }
                    else if (p.partner >= 0 && p.adultInPair && people[p.partner].role == Role.Child) Arm(ref ps, sO) = HoldHandArm(p, s, sO, t, true);
                    else if (r[6] < 0.5f)
                    {
                        var hc = IK(SpC, s.sternumC + new Vector3(-0.06f * sO, -0.04f, 0.03f) * p.s, SpC, new Vector3(-sO * 0.5f, 0.85f, 0f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sO * 0.6f, -0.8f, 0f));
                        Flat(ref hc); Arm(ref ps, sO) = hc;
                    }
                    else Arm(ref ps, sO) = FK(0f, 8f + p.hipClear, 0f, 15f + 5f * Mathf.Sin(TAU * f * t + 1f));
                    break;
                }

                case Beh.JumpWave:
                {
                    int n = 2 + Mathf.FloorToInt(r[0] * 3.99f);
                    float Tj = Lerp(0.9f, 1.25f, r[1]) / k;
                    float pause = Lerp(3f, 6f, r[2]);
                    float cyc = n * Tj + pause;
                    float u = Mathf.Repeat(bt, cyc);
                    if (u >= n * Tj) { Behave(Beh.WaveBoth, p, s, ref ps, t, bt, r, index); ps.mouth *= 0.5f; break; }
                    float pj = Mathf.Repeat(u / Tj, 1f);
                    float tAir = 0.38f * Tj;
                    float h = Mathf.Min(9.81f * tAir * tAir / 8f, 0.09f * p.s);
                    float hk = h / 0.18f;
                    if (pj < 0.38f)
                    {
                        float v = pj / 0.38f;
                        float lift = 4f * h * v * (1f - v), tuck = 0.06f * p.s * Mathf.Sin(Mathf.PI * v) * hk;
                        ps.pelvisOfs.y += lift;
                        ps.legL.target.y += lift + tuck; ps.legR.target.y += lift + tuck;
                        ps.legL.plantar = ps.legR.plantar = 25f;
                        float el = 172f + 6f * Mathf.Sin(Mathf.PI * v), fl = 30f + 15f * Mathf.Sin(Mathf.PI * v);
                        var d = FK(22f, el, 72f, fl); var o = FK(12f, el, 90f, fl); Open(ref d); Open(ref o);
                        Arm(ref ps, sD) = d; Arm(ref ps, sO) = o;
                        ps.mouth = 0.8f + 0.2f * Mathf.Sin(Mathf.PI * v);
                    }
                    else
                    {
                        float q = (pj - 0.38f) / 0.62f;
                        float comp = q < 0.3f ? RigMath.SS(0f, 1f, q / 0.3f) : (q < 0.7f ? 1f : 1f - RigMath.SS(0f, 1f, (q - 0.7f) / 0.3f));
                        ps.pelvisOfs += new Vector3(0f, -0.11f * p.s * comp * hk, -0.02f * comp);
                        ps.spineBend += 3f + 12f * comp * hk; ps.chestBend += -4f + 6f * comp;
                        ps.headPitch = -10f - 6f * comp + 5f * comp * (1f - q);
                        float el = 172f - 40f * comp, fl = 20f + 25f * comp;
                        var d = FK(22f, el, 72f, fl); var o = FK(12f, el, 90f, fl); Open(ref d); Open(ref o);
                        Arm(ref ps, sD) = d; Arm(ref ps, sO) = o;
                        ps.legL.plantar = ps.legR.plantar = 30f * (1f - RigMath.SS(0f, 1f, q / 0.12f)) + 30f * RigMath.SS(0f, 1f, (q - 0.7f) / 0.3f);
                        ps.mouth = 0.3f;
                    }
                    ps.shiftW = 0f; ps.lookW = 0.6f; ps.worry = 0.2f;
                    break;
                }

                case Beh.CallCupped:
                {
                    float ri = Lerp(0.4f, 0.6f, r[0]), rs = Lerp(1.2f, 2.0f, r[1]), rl = Lerp(1.5f, 3f, r[2]);
                    float c = Mathf.Repeat(bt, ri + rs + rl);
                    bool listen = c >= ri + rs;
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        Vector3 cheek = sd < 0 ? s.cheekLH : s.cheekRH;
                        Vector3 palm = cheek + new Vector3(-sd * 0.015f, -0.035f, 0.045f) * p.hs;
                        if (listen) palm += new Vector3(sd * 0.03f, -0.09f, 0.0f) * p.hs;
                        var ac = IK(SpH, palm, SpH, new Vector3(-sd * 0.35f, 0.25f, 1f), new Vector3(-sd, 0f, 0.2f), SpC, new Vector3(sd * 0.9f, -0.45f, -0.1f));
                        HandShape(ref ac, 25f, 20f, 10f);
                        Arm(ref ps, sd) = ac;
                    }
                    ps.yawLimit = 35f;
                    if (c < ri)
                    {
                        float w = c / ri;
                        ps.chestBend += -4f * w; ps.armL.shrug += 6f * w; ps.armR.shrug += 6f * w; ps.headPitch += -6f * w;
                    }
                    else if (!listen)
                    {
                        float x = c - ri;
                        float e = Mathf.Min(1f, x / 0.15f) * (1f - RigMath.SS(rs - 0.25f, rs, x));
                        ps.spineBend += 6f * e; ps.chestBend += 4f * e; ps.neckBend += 8f * e; ps.headPitch += -12f * e;
                        ps.pelvisOfs.y += -0.02f * e;
                        ps.mouth = e * (0.9f + 0.1f * Mathf.Sin(TAU * 7f * t));
                        ps.worry = 1f;
                        ps.lookW = 0.8f;
                    }
                    else
                    {
                        ps.headYaw += 15f * Mathf.Sin(TAU * 0.25f * t); ps.headRoll += 6f; ps.mouth = 0.05f; ps.lookW = 0.5f;
                    }
                    break;
                }

                case Beh.Phone:
                {
                    int sp = sD, so = -sp;
                    Vector3 ear = sp < 0 ? s.earLH : s.earRH;
                    var ph = IK(SpH, ear + new Vector3(sp * 0.025f, -0.02f, 0.025f) * p.hs, SpH, new Vector3(0f, 0.9f, -0.4f), new Vector3(-sp, 0f, 0f), SpC, new Vector3(sp * 0.35f, -0.85f, 0.4f));
                    HandShape(ref ph, 55f, 50f, 40f);
                    Arm(ref ps, sp) = ph;
                    ps.firstArm = sp;
                    if (r[1] < 0.35f)
                    {
                        var o = IK(SpOtherElbow, new Vector3(-sp * 0.03f, -0.04f, 0.03f), SpC, new Vector3(sp, 0f, 0.15f), new Vector3(0f, 1f, 0f), SpC, new Vector3(so * 0.6f, -0.8f, 0.1f));
                        Arm(ref ps, so) = o;
                    }
                    else if (r[1] < 0.6f) Arm(ref ps, so) = RestArm(p, s, 5, so, t);
                    else if (r[1] < 0.8f)
                    {
                        float beat = Pulse01(Mathf.Sin(TAU * t / Lerp(0.6f, 1.4f, r[2])));
                        var g = IK(SpS, s.bellyS + new Vector3(so * 0.12f, 0.06f + 0.04f * N(p, 0.4f, t, 3) - 0.04f * beat * beat, 0.22f + 0.05f * N(p, 0.35f, t, 4)) * p.s,
                                   SpR, new Vector3(-so * 0.2f, -0.3f, 1f), new Vector3(-so * 0.3f, 1f, 0f), SpR, new Vector3(so * 0.6f, -0.8f, -0.1f));
                        Open(ref g); Arm(ref ps, so) = g;
                    }
                    ps.headRoll += 7f * sp; ps.headPitch = 6f + 4f * N(p, 0.1f, t, 5); ps.headYaw = 20f * N(p, 0.08f, t, 6);
                    ps.lookW = 0.3f; ps.yawLimit = 35f;
                    float talk = Burst(bt, Lerp(2f, 6f, r[3]), Lerp(1f, 4f, r[4]), r[5], 0.2f);
                    ps.mouth = talk * (0.35f + 0.35f * Mathf.Abs(Mathf.Sin(TAU * 4.3f * t))) * (N(p, 3f, t, 7) > -0.3f ? 1f : 0.2f);
                    if (talk < 0.1f && r[6] < 0.5f) ps.headPitch += 4f * Mathf.Sin(TAU * 1.5f * t);
                    ps.props |= 1 << (int)PropKind.Phone;
                    break;
                }

                case Beh.Point:
                {
                    Vector3 targetW = p.pointTarget != Vector3.zero ? p.pointTarget : p.waterPoint;
                    Vector3 tgt = ToRoot(p, targetW);
                    Vector3 sh = Shoulder(s, sD);
                    Vector3 dir = (tgt - sh).normalized;
                    dir.y = Mathf.Clamp(dir.y, -0.6f, 0.5f); dir.Normalize();
                    float period = Lerp(1.2f, 2.5f, r[0]);
                    float jt = Mathf.Repeat(bt, period);
                    float jab = jt < 0.1f ? Mathf.Sin(jt / 0.1f * Mathf.PI * 0.5f) : (jt < 0.35f ? 1.08f * (1f - RigMath.SS(0.1f, 0.35f, jt)) : 0f);
                    float reach = s.l1 + s.l2 + 0.42f * s.lh;
                    var d = IK(SpR, sh + dir * reach * (0.9f + 0.06f * jab), SpR, dir, Vector3.down, SpC, new Vector3(sD * 0.3f, -0.9f, -0.2f));
                    HandShape(ref d, 88f, 0f, 45f);
                    Arm(ref ps, sD) = d;
                    float yawTo = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    ps.chestTwist += Mathf.Clamp(yawTo * 0.35f, -25f, 25f);
                    ps.pelvisYaw += Mathf.Clamp(yawTo * 0.15f, -10f, 10f);
                    ps.spineBend += 4f + 3f * jab;
                    ps.lookAtTarget = true; ps.lookTarget = targetW; ps.lookW = 1f;
                    ps.worry = 0.6f;
                    if (r[1] < 0.3f)
                    {
                        var m = IK(SpH, s.mouthH + new Vector3(0f, -0.01f, 0.045f) * p.hs, SpH, new Vector3(-sO * 0.4f, 0.8f, 0.4f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sO * 0.6f, -0.8f, 0.2f));
                        Flat(ref m); Arm(ref ps, sO) = m;
                    }
                    else if (r[1] < 0.6f) Arm(ref ps, sO) = RestArm(p, s, 5, sO, t);
                    ps.mouth = jab > 0.5f && r[2] < 0.5f ? 0.4f : 0.05f;
                    break;
                }

                case Beh.HugChild:
                {
                    if (p.partner < 0 || people[p.partner].root == null) break;
                    var o = people[p.partner];
                    if (!p.adultInPair)
                    {
                        // child: face against the adult's belly, hands on the lower back
                        ps.spineBend += 8f; ps.chestBend += 4f; ps.headYaw = (r[0] < 0.5f ? -1f : 1f) * 70f; ps.headRoll += (r[0] < 0.5f ? -10f : 10f);
                        ps.pelvisOfs.z += 0.03f; ps.lookW = 0f;
                        for (int i = 0; i < 2; i++)
                        {
                            int sd = Sides[i];
                            Vector3 palm = PartnerPoint(p, p.partner, SVSkel.Spine, new Vector3(-sd * (o.waistRx + 0.02f), 0.02f, -o.waistRz * 0.6f));
                            var ac = IK(SpR, palm, SpR, new Vector3(-sd * 0.5f, -0.2f, 0.8f), new Vector3(0f, 0f, 1f), SpR, new Vector3(sd, -0.4f, 0.2f));
                            ac.aim = false; ac.curl = 40f;
                            Arm(ref ps, sd) = ac;
                        }
                    }
                    else
                    {
                        ps.spineBend += 12f; ps.chestBend += 10f; ps.neckBend += 10f; ps.headPitch = 15f; ps.lookW = 0.2f;
                        Vector3 back = PartnerPoint(p, p.partner, SVSkel.Chest, new Vector3(sD * 0.05f, 0.05f, -o.chestRz - 0.02f));
                        var d = IK(SpR, back, SpR, new Vector3(-sD * 0.6f, -0.3f, -0.7f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sD * 0.9f, -0.2f, 0.3f));
                        d.aim = false; d.curl = 35f;
                        Arm(ref ps, sD) = d;
                        Vector3 head = PartnerPoint(p, p.partner, SVSkel.Head, new Vector3(0.015f * Mathf.Cos(TAU * 0.6f * t), 0.2f, -0.03f + 0.015f * Mathf.Sin(TAU * 0.6f * t)));
                        Vector3 low = PartnerPoint(p, p.partner, SVSkel.Spine, new Vector3(sO * 0.05f, 0f, -o.waistRz - 0.02f));
                        var oa = IK(SpR, r[1] < 0.5f ? head : low, SpR, new Vector3(0f, -0.5f, 1f), Vector3.down, SpC, new Vector3(sO * 0.9f, -0.3f, 0.2f));
                        oa.aim = false; Flat(ref oa);
                        Arm(ref ps, sO) = oa;
                    }
                    ps.pelvisOfs.x += 0.02f * Mathf.Sin(TAU * 0.35f * t); ps.chestLean += 2f * Mathf.Sin(TAU * 0.35f * t + 0.3f);
                    ps.shiftW = 0.3f; ps.worry = 0.5f;
                    break;
                }

                case Beh.Comfort:
                {
                    if (p.partner < 0 || people[p.partner].root == null) break;
                    var o = people[p.partner];
                    Vector3 pr = ToRoot(p, o.root.position);
                    int toward = pr.x >= 0f ? 1 : -1;
                    if (p.adultInPair)
                    {
                        // arm around the partner's far shoulder
                        Vector3 far = PartnerPoint(p, p.partner, SVSkel.UpArm(toward), new Vector3(0f, 0f, -0.05f));
                        float pat = Pulse01(Mathf.Sin(TAU * 1.6f * t)) * Burst(bt, 1.5f, Lerp(3f, 6f, r[0]), r[1], 0.2f);
                        far.y += 0.02f * pat;
                        var a1 = IK(SpR, far, SpR, new Vector3(toward * 0.2f, -0.7f, 0.3f), Vector3.down, SpC, new Vector3(toward * 0.2f, 0.2f, -1f));
                        a1.aim = false; Flat(ref a1);
                        Arm(ref ps, toward) = a1;
                        ps.chestLean += 4f * toward; ps.headRoll += 5f * toward;
                        if (r[2] < 0.3f) { ps.lookAtTarget = true; ps.lookTarget = o.root.position + Vector3.up * (st[p.partner].restRS[SVSkel.Head].y + 0.05f); }
                    }
                    else
                    {
                        ps.headRoll += 10f * toward; ps.headPitch = 15f; ps.armL.shrug += 6f; ps.armR.shrug += 6f; ps.spineBend += 5f;
                        ps.lookW = 0.2f; ps.worry = 1f;
                        if (r[3] < 0.5f)
                            for (int i = 0; i < 2; i++)
                            {
                                int sd = Sides[i];
                                var hf = IK(SpH, s.mouthH + new Vector3(sd * 0.035f, 0.05f, 0.03f) * p.hs, SpH, new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sd * 0.5f, -0.8f, 0.2f));
                                Flat(ref hf); Arm(ref ps, sd) = hf;
                            }
                        float sob = Burst(bt, Lerp(2f, 4f, r[4]), Lerp(2f, 5f, r[5]), r[6], 0.3f);
                        float sw = Mathf.Abs(Mathf.Sin(TAU * 2.4f * t)) * sob;
                        ps.armL.shrug += 2.5f * sw; ps.armR.shrug += 2.5f * sw; ps.chestBend += 2f * sw; ps.headPitch += 2f * Mathf.Sin(TAU * 2.4f * t) * sob;
                        ps.eyesOpen = 0.4f;
                    }
                    ps.shiftW = 0.5f;
                    break;
                }

                case Beh.Shiver:
                {
                    ps.armL = RestArm(p, s, 3, -1, t); ps.armR = RestArm(p, s, 3, 1, t);
                    ref ArmCmd d = ref Arm(ref ps, sD);
                    d.palm = s.sternumC + new Vector3(-sD * 0.17f, -0.09f, -0.02f) * p.s;
                    HandShape(ref d, 70f, 70f, 50f);
                    if (r[0] < 0.4f) { float rub = 0.06f * Mathf.Sin(TAU * 1.6f * t); ps.armL.palm.y += rub; ps.armR.palm.y -= rub; }
                    float tr = 1.5f * N(p, 9f, t, 8);
                    ps.armL.shrug += 12f + tr; ps.armR.shrug += 12f + tr; ps.armL.protract += 10f; ps.armR.protract += 10f;
                    ps.spineBend += 6f; ps.chestBend += 5f; ps.headPitch += 10f; ps.pelvisOfs.y += -0.02f;
                    ps.legL.target.x += 0.03f; ps.legR.target.x -= 0.03f;
                    ps.chestTwist += 1.2f * Mathf.Sin(TAU * 8.5f * t); ps.headYaw += 0.6f * N(p, 7f, t, 9);
                    ps.mouth = 0.08f + 0.07f * Mathf.Sign(Mathf.Sin(TAU * 11f * t));
                    float bounce = Burst(bt, 1.5f, Lerp(4f, 8f, r[1]), r[2], 0.3f);
                    ps.pelvisOfs.y += 0.01f * Mathf.Sin(TAU * 2.5f * t) * bounce;
                    ps.worry = 0.9f; ps.lookW = 0.6f;
                    s.arousal = Mathf.Max(s.arousal, 0.3f);
                    break;
                }

                case Beh.Sit: SitPose(p, s, ref ps, t, bt, r); break;

                case Beh.Kneel:
                {
                    int kneel = sO;
                    float hipY = 0.06f + s.t1 * Mathf.Cos(12f * Mathf.Deg2Rad);
                    ps.pelvisOfs = new Vector3(0f, hipY - s.restRS[SVSkel.ThighR].y, -0.06f);
                    ps.pelvisTilt = 10f; ps.spineBend = 20f; ps.chestBend = 12f; ps.neckBend = 8f; ps.headPitch = 15f; ps.lookW = 0.3f; ps.shiftW = 0f;
                    ref LegCmd kl = ref Leg(ref ps, kneel);
                    kl.mode = 2; kl.plane = 90f; kl.elev = 12f; kl.flex = 100f; kl.plantar = -10f;
                    ref LegCmd fl = ref Leg(ref ps, sD);
                    fl.mode = 1; fl.target = new Vector3(sD * 0.13f, s.restRS[SVSkel.FootR].y, 0.36f * p.s); fl.toeOut = 10f; fl.knee = new Vector3(0f, 0.4f, 1f);
                    Vector3 knob = p.hasKnob ? ToRoot(p, p.knob) : new Vector3(sD * 0.05f, 0.55f, 0.55f);
                    float fiddle = Burst(bt, Lerp(2f, 4f, r[0]), Lerp(1f, 2f, r[1]), r[2], 0.3f);
                    knob += 0.012f * fiddle * new Vector3(Mathf.Cos(TAU * 1.5f * t), 0f, Mathf.Sin(TAU * 1.5f * t));
                    var d = IK(SpR, knob, SpR, new Vector3(0f, -0.6f, 0.8f), new Vector3(-sD, 0f, 0f), SpC, new Vector3(sD * 0.7f, -0.6f, -0.2f));
                    d.aim = false; Grip(ref d);
                    Arm(ref ps, sD) = d;
                    var oa = IK(sD < 0 ? SpKneeL : SpKneeR, new Vector3(0f, 0.06f, 0.04f), SpR, new Vector3(0f, -0.3f, 1f), Vector3.down, SpC, new Vector3(sO * 0.5f, -0.7f, 0.2f));
                    oa.aim = false; Flat(ref oa);
                    Arm(ref ps, sO) = oa;
                    float look = Burst(bt, Lerp(2f, 3f, r[3]), Lerp(6f, 12f, r[4]), r[5], 0.5f);
                    ps.spineBend = Mathf.Lerp(20f, 8f, look); ps.chestBend = Mathf.Lerp(12f, 4f, look); ps.headPitch = Mathf.Lerp(15f, -5f, look);
                    ps.lookW = Mathf.Lerp(0.3f, 1f, look);
                    ps.worry = -0.3f;
                    break;
                }

                case Beh.Crouch:
                {
                    ps.pelvisOfs = new Vector3(0.01f * Mathf.Sin(TAU * 0.2f * t), 0.40f * p.s - s.restRS[SVSkel.ThighR].y, -0.10f * p.s);
                    ps.shiftW = 0f;
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        ref LegCmd l = ref Leg(ref ps, sd);
                        l.mode = 1; l.target = new Vector3(sd * 0.15f * p.s, s.restRS[SVSkel.FootR].y, 0.05f * p.s); l.toeOut = 18f; l.knee = new Vector3(sd * 0.45f, 0.3f, 1f);
                        var ac = IK(sd < 0 ? SpKneeL : SpKneeR, new Vector3(-sd * 0.04f, -0.09f, 0.10f) * p.s, SpR, new Vector3(0f, -1f, 0.2f), new Vector3(-sd, 0f, 0f), SpC, new Vector3(sd * 0.5f, -0.5f, 0.6f));
                        ac.aim = false; ac.curl = 35f;
                        Arm(ref ps, sd) = ac;
                    }
                    ps.spineBend = 25f; ps.chestBend = 12f; ps.neckBend = 5f; ps.headPitch = -28f; ps.lookW = 0.7f; ps.yawLimit = 40f;
                    ps.chestLean += 1.5f * Mathf.Sin(TAU * 0.2f * t + 1f);
                    if (HasProp(p, PropKind.Beads))
                    {
                        float flick = Mathf.Repeat(t, Lerp(1f, 1.4f, r[0]));
                        Arm(ref ps, sD).wflex = flick < 0.18f ? Mathf.Lerp(-15f, 10f, flick / 0.18f) : -15f;
                        ps.props |= 1 << (int)PropKind.Beads;
                    }
                    break;
                }

                case Beh.Walk: WalkPose(p, s, ref ps, t); break;

                case Beh.Carry: CarryArms(p, s, ref ps); break;

                case Beh.Direct:
                {
                    float f = 0.9f * k;
                    if (r[0] < 0.35f)
                    {
                        float u = RigMath.Pump(f, t, ph0);
                        var d = FK(55f, 92f + 5f * Mathf.Sin(TAU * f * t), -15f, 25f + 70f * u); d.pron = -80f; d.curl = d.index = 10f + 60f * u;
                        Arm(ref ps, sD) = d;
                        if (r[1] < 0.5f) { var o = d; Arm(ref ps, sO) = o; }
                        ps.spineBend += 3f - 6f * u;
                    }
                    else if (r[0] < 0.6f)
                    {
                        var d = FK(42f + 38f * Mathf.Cos(TAU * 0.7f * t), 85f, 0f, 10f); Open(ref d);
                        Arm(ref ps, sD) = d; ps.headYaw = sD * 25f * Mathf.Cos(TAU * 0.7f * t); ps.lookW = 0.4f;
                    }
                    else if (r[0] < 0.85f)
                    {
                        var d = FK(88f, 88f + 4f * Mathf.Sin(TAU * 1.2f * t), 0f, 6f); d.pron = 90f; d.wflex = -75f; Open(ref d);
                        Arm(ref ps, sD) = d; Arm(ref ps, sO) = RestArm(p, s, 5, sO, t);
                    }
                    else
                    {
                        var d = FK(216f * t, 155f, 85f, 15f); Open(ref d);
                        Arm(ref ps, sD) = d;
                    }
                    ps.worry = -0.3f; ps.mouth = Burst(bt, 1.2f, 2.5f, r[2], 0.15f) * 0.6f;
                    break;
                }

                case Beh.Radio:
                {
                    float talk = Burst(bt, Lerp(1.5f, 4f, r[0]), Lerp(2f, 5f, r[1]), r[2], 0.3f);
                    var tk = IK(SpH, s.mouthH + new Vector3(sD * 0.045f, -0.035f, 0.055f) * p.hs, SpH, new Vector3(-sD * 0.25f, 0.9f, 0.35f), new Vector3(-sD, 0f, 0.4f), SpC, new Vector3(sD * 0.45f, -0.88f, 0.15f));
                    var ls = IK(SpC, s.sternumC + new Vector3(sD * 0.10f, -0.06f, 0.10f) * p.s, SpH, new Vector3(-sD * 0.25f, 0.9f, 0.35f), new Vector3(-sD, 0f, 0.4f), SpC, new Vector3(sD * 0.5f, -0.88f, 0.15f));
                    var d = talk > 0.5f ? tk : ls;
                    Grip(ref d);
                    Arm(ref ps, sD) = d;
                    ps.mouth = talk * (0.3f + 0.4f * Mathf.Abs(Mathf.Sin(TAU * 4.5f * t))) * (N(p, 2f, t, 10) > -0.4f ? 1f : 0.2f);
                    ps.headPitch = Mathf.Lerp(12f, 6f, talk); ps.chestBend += 3f * talk; ps.yawLimit = 35f;
                    ps.headRoll += sD * 8f * (1f - talk); ps.lookW = Mathf.Lerp(0.2f, 0.6f, talk);
                    if (r[3] < 0.4f) Arm(ref ps, sO) = RestArm(p, s, 5, sO, t);
                    ps.props |= 1 << (int)PropKind.Radio;
                    ps.worry = -0.3f;
                    break;
                }

                case Beh.RestKnees:
                {
                    ps.pelvisOfs += new Vector3(0f, -0.07f * p.s, -0.06f * p.s);
                    ps.spineBend = 32f; ps.chestBend = 18f; ps.neckBend = 6f;
                    float alt = Mathf.Repeat(bt, 2f * Lerp(1.5f, 3f, r[0])) < Lerp(1.5f, 3f, r[0]) ? -20f : 15f;
                    ps.headPitch = alt; ps.lookW = alt < 0f ? 0.6f : 0f;
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        var ac = IK(sd < 0 ? SpKneeL : SpKneeR, new Vector3(-sd * 0.02f, 0.07f, 0.07f) * p.s, SpR, new Vector3(-sd * 0.2f, -0.4f, 0.9f), Vector3.down, SpC, new Vector3(sd * 0.6f, -0.3f, -0.2f));
                        Flat(ref ac); Arm(ref ps, sd) = ac;
                    }
                    s.arousal = Mathf.Max(s.arousal, 0.9f);
                    ps.mouth = 0.3f + 0.2f * Mathf.Sin(TAU * 0.6f * t);
                    ps.shiftW = 0f;
                    break;
                }

                case Beh.HandsOnHead:
                {
                    if (r[0] < 0.4f)
                    {
                        var fh = IK(SpH, s.foreheadH + new Vector3(0f, 0f, 0.03f) * p.hs, SpH, new Vector3(-sD * 0.9f, 0.3f, 0f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sD * 0.8f, -0.4f, 0.4f));
                        Flat(ref fh); Arm(ref ps, sD) = fh; ps.headPitch = 12f;
                    }
                    else
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            int sd = Sides[i];
                            var ac = IK(SpH, s.crownH + new Vector3(sd * 0.07f, -0.01f, -0.02f) * p.hs, SpH, new Vector3(-sd * 0.85f, 0.1f, -0.5f), new Vector3(0f, -1f, 0f), SpC, new Vector3(sd * 0.8f, 0.25f, 0.55f));
                            Flat(ref ac); Arm(ref ps, sd) = ac;
                        }
                        ps.headPitch = 8f;
                    }
                    ps.spineBend += 4f; ps.mouth = 0.2f; ps.lookW = 0.3f; ps.worry = 1f;
                    ps.headYaw += 12f * Mathf.Sin(TAU * 0.4f * t) * Burst(bt, Lerp(2f, 3f, r[1]), 3f, r[2], 0.3f);
                    break;
                }

                case Beh.HandOnCheek:
                {
                    Vector3 cheek = sD < 0 ? s.cheekLH : s.cheekRH;
                    var d = IK(SpH, cheek + new Vector3(sD * 0.012f, -0.01f, 0.016f) * p.hs, SpH, new Vector3(-sD * 0.1f, 0.95f, -0.3f), new Vector3(-sD * 0.6f, 0f, -0.8f), SpC, new Vector3(sD * 0.15f, -0.95f, 0.3f));
                    Flat(ref d); Arm(ref ps, sD) = d;
                    var o = IK(SpOtherElbow, new Vector3(0f, -0.04f, 0.02f), SpC, new Vector3(sD * 0.9f, 0f, 0.3f), new Vector3(0f, 1f, 0f), SpC, new Vector3(sO * 0.5f, -0.8f, 0.3f));
                    Arm(ref ps, sO) = o;
                    ps.firstArm = sD;
                    ps.headRoll += sD * 10f; ps.yawLimit = 25f; ps.worry = 1f; ps.lookW = 0.6f;
                    ps.headYaw += 8f * Mathf.Sin(TAU * 0.5f * t) * Burst(bt, 2f, Lerp(4f, 8f, r[0]), r[1], 0.3f);
                    ps.mouth = Mathf.Max(0f, 0.1f + 0.1f * N(p, 5f, t, 11));
                    break;
                }

                case Beh.Film:
                {
                    // phone held up in the dominant hand, screen toward the face
                    var d = IK(SpH, new Vector3(sD * 0.04f, -0.03f, 0.30f + 0.06f * p.s) * p.hs, SpH, new Vector3(0f, 1f, 0.15f), new Vector3(0f, 0.1f, -1f), SpR, new Vector3(sD * 0.7f, -0.65f, 0.1f));
                    HandShape(ref d, 55f, 50f, 40f); Arm(ref ps, sD) = d;
                    if (r[0] < 0.35f)
                    {
                        // the other hand steadies the filming arm's elbow
                        var o = IK(SpOtherElbow, new Vector3(-sD * 0.03f, -0.04f, 0.03f), SpC, new Vector3(sD, 0f, 0.15f), new Vector3(0f, 1f, 0f), SpC, new Vector3(sO * 0.6f, -0.8f, 0.1f));
                        Arm(ref ps, sO) = o; ps.firstArm = sD;
                    }
                    ps.lookAtTarget = true;
                    ps.lookTarget = p.waterPoint + p.root.right * (8f * Mathf.Sin(0.1f * t + p.seed));
                    ps.lookW = 0.9f; ps.yawLimit = 45f;
                    ps.mouth = Burst(bt, 2f, 4f, r[2], 0.2f) * 0.35f * Mathf.Abs(Mathf.Sin(TAU * 4f * t));
                    ps.props |= 1 << (int)PropKind.Phone;
                    break;
                }

                case Beh.HoldBaby:
                {
                    int sC = sO;
                    Arm(ref ps, sC) = CradleArm(p, s, sC);
                    if (r[0] < 0.6f)
                    {
                        Vector3 bundle = new Vector3(sC * 0.04f, -0.17f, 0.20f) * p.s + s.sternumC * 0f;
                        var pat = IK(SpC, bundle + new Vector3(0f, 0.11f + 0.015f * Pulse01(Mathf.Sin(TAU * 2f * t)), 0.03f), SpC, new Vector3(-sD * 0.3f, -0.4f, 0.8f), Vector3.down, SpC, new Vector3(sD * 0.6f, -0.7f, 0.2f));
                        pat.aim = false; Flat(ref pat); Arm(ref ps, sD) = pat;
                    }
                    else
                    {
                        Behave(Beh.WaveOne, p, s, ref ps, t, bt, r, index);
                        Arm(ref ps, sC) = CradleArm(p, s, sC);
                    }
                    ps.chestTwist += 4f * Mathf.Sin(TAU * 0.4f * t);
                    ps.pelvisOfs.x += 0.015f * Mathf.Sin(TAU * 0.4f * t + 0.6f);
                    ps.pelvisOfs.y += -0.008f * (0.5f + 0.5f * Mathf.Sin(TAU * 0.8f * t));
                    if (r[1] < 0.6f && r[0] < 0.6f) { ps.lookW = 0f; ps.headPitch = 20f; }
                    ps.props |= 1 << (int)PropKind.Baby;
                    break;
                }

                case Beh.HoldHands:
                case Beh.Cling:
                {
                    if (p.partner < 0 || people[p.partner].root == null) break;
                    var o = people[p.partner];
                    Vector3 pr = ToRoot(p, o.root.position);
                    int toward = pr.x >= 0f ? 1 : -1;
                    if (b == Beh.Cling && !p.adultInPair)
                    {
                        // child hugs the adult's thigh
                        int legSide = ToRoot(p, o.root.TransformPoint(st[p.partner].restRS[SVSkel.ThighR])).x < pr.x ? 1 : -1;
                        for (int i = 0; i < 2; i++)
                        {
                            int sd = Sides[i];
                            Vector3 thigh = PartnerPoint(p, p.partner, SVSkel.Thigh(legSide), new Vector3(sd * 0.05f, -0.4f * st[p.partner].t1, 0f));
                            var ac = IK(SpR, thigh, SpR, new Vector3(toward, -0.2f, 0.3f), new Vector3(-toward, 0f, 0f), SpR, new Vector3(sd * 0.6f, -0.4f, -0.3f));
                            ac.aim = false; ac.curl = 50f; Arm(ref ps, sd) = ac;
                        }
                        ps.headRoll += 12f * toward; ps.lookW = 0.3f; ps.spineBend += 4f;
                    }
                    else Arm(ref ps, toward) = HoldHandArm(p, s, toward, t, p.adultInPair);
                    if (b == Beh.Cling && p.adultInPair)
                    {
                        Vector3 crown = PartnerPoint(p, p.partner, SVSkel.Head, new Vector3(0f, 0.2f, 0f));
                        var hc = IK(SpR, crown, SpR, new Vector3(0f, -0.3f, 1f), Vector3.down, SpC, new Vector3(toward * 0.7f, -0.6f, 0.2f));
                        hc.aim = false; Flat(ref hc); Arm(ref ps, toward) = hc;
                    }
                    ps.shiftW = 0.5f;
                    break;
                }

                case Beh.Talk:
                {
                    int g = p.group;
                    int members = 0, mine = 0;
                    for (int j = 0; j < people.Count; j++) if (people[j].group == g) { if (j == index) mine = members; members++; }
                    float period = 3f + 5f * RigMath.Hash01(g, 5);
                    int speaker = members > 0 ? Mathf.FloorToInt((t + g * 3.1f) / period) % members : 0;
                    if (speaker == mine)
                    {
                        float beat = Pulse01(Mathf.Sin(TAU * t / Lerp(0.5f, 1.2f, r[0])));
                        for (int i = 0; i < 2; i++)
                        {
                            int sd = Sides[i];
                            if (i == 1 && r[1] < 0.5f) break;
                            Vector3 palm = s.bellyS + new Vector3(sd * 0.16f, 0.10f, 0.24f) * p.s + new Vector3(0.06f * N(p, 0.7f, t, 20 + i), 0.05f * N(p, 0.9f, t, 22 + i) - 0.05f * beat * beat, 0.05f * N(p, 0.6f, t, 24 + i));
                            var ac = IK(SpS, palm, SpR, new Vector3(-sd * 0.3f, -0.2f, 1f), new Vector3(-sd * 0.3f, 1f, 0f), SpR, new Vector3(sd * 0.6f, -0.8f, -0.1f));
                            Open(ref ac); Arm(ref ps, sd) = ac;
                        }
                        ps.mouth = 0.3f + 0.35f * Mathf.Abs(Mathf.Sin(TAU * 4.2f * t)) * (N(p, 3f, t, 26) > -0.3f ? 1f : 0.2f);
                        ps.lookW = 0.8f;
                    }
                    else
                    {
                        // listeners look at the speaker and nod now and then
                        int c = 0;
                        for (int j = 0; j < people.Count; j++)
                        {
                            if (people[j].group != g) continue;
                            if (c == speaker && people[j].root != null)
                            {
                                ps.lookAtTarget = true;
                                ps.lookTarget = people[j].root.position + Vector3.up * (st[j].restRS[SVSkel.Head].y + 0.1f);
                                break;
                            }
                            c++;
                        }
                        ps.headPitch += 5f * Mathf.Sin(TAU * 1.6f * t) * Burst(bt, 1.2f, 6f, r[2], 0.2f);
                        ps.lookW = 1f;
                    }
                    break;
                }

                case Beh.LeanRail:
                {
                    for (int i = 0; i < 2; i++) Arm(ref ps, Sides[i]) = RailArm(p, s, Sides[i]);
                    if (p.bridgeRail) { ps.pelvisOfs += new Vector3(0f, -0.02f, -0.04f * p.s); ps.spineBend = 22f; ps.chestBend = 10f; }
                    else { ps.spineBend = 10f; ps.chestBend = 6f; }
                    ps.headPitch = 25f; ps.lookW = 0.6f;
                    if (r[0] < 0.2f) { ref LegCmd fl = ref Leg(ref ps, sO); fl.target += new Vector3(sD * 0.1f, 0.0f, -0.12f); fl.plantar = 40f; }
                    break;
                }

                case Beh.Dua:
                {
                    bool amin = s.nextSwitch - t < 0.8f && s.cur == Beh.Dua;
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        ArmCmd ac;
                        if (!amin) { ac = IK(SpC, s.sternumC + new Vector3(sd * 0.11f, -0.04f, 0.24f) * p.s, SpC, new Vector3(-sd * 0.15f, 0.45f, 0.88f), new Vector3(0f, 0.9f, -0.45f), SpC, new Vector3(sd * 0.6f, -0.8f, 0.2f)); Open(ref ac); }
                        else
                        {
                            float sl = Mathf.Clamp01(1f - (s.nextSwitch - t) / 0.8f);
                            ac = IK(SpH, s.mouthH + new Vector3(sd * 0.03f, 0.06f - 0.06f * sl, 0.03f) * p.hs, SpH, new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sd * 0.6f, -0.8f, 0.2f));
                            Flat(ref ac);
                        }
                        Arm(ref ps, sd) = ac;
                    }
                    ps.headPitch = 12f; ps.eyesOpen = 0.5f; ps.lookW = 0f;
                    ps.mouth = Mathf.Max(0f, 0.1f + 0.08f * N(p, 5f, t, 12));
                    break;
                }

                case Beh.SignalCloth:
                {
                    float f = 0.55f * k;
                    var d = FK(15f + 35f * Mathf.Sin(TAU * f * (t + ph0)), 150f + 12f * Mathf.Sin(TAU * 2f * f * t + 1f), 70f, 18f + 10f * Mathf.Sin(TAU * f * t + 0.5f));
                    d.curl = 80f; d.index = 80f; d.thumb = 55f;
                    Arm(ref ps, sD) = d;
                    if (r[0] < 0.5f) Arm(ref ps, sO) = RestArm(p, s, 5, sO, t);
                    ps.chestLean += -sD * 3f + 3f * Mathf.Sin(TAU * f * t); ps.pelvisOfs.x += sD * 0.02f;
                    ps.headPitch = -10f; ps.lookW = 0.7f; ps.shiftW = 0.3f; ps.worry = 0.8f;
                    ps.mouth = Burst(bt, Lerp(1.5f, 3f, r[1]), Lerp(1f, 2f, r[2]), r[3], 0.15f) * 0.8f;
                    ps.props |= 1 << (int)PropKind.Cloth;
                    break;
                }
            }
        }

        static bool HasProp(Person p, PropKind k)
        {
            if (p.props == null) return false;
            for (int i = 0; i < p.props.Length; i++) if (p.props[i] != null && p.props[i].kind == k) return true;
            return false;
        }

        ArmCmd CradleArm(Person p, State s, int sC)
        {
            Vector3 bundle = new Vector3(sC * 0.04f, -0.17f, 0.20f) * p.s;
            var c = IK(SpC, bundle + new Vector3(-sC * 0.16f, -0.08f, 0f), SpC, new Vector3(-sC * 0.9f, 0.1f, 0.3f), new Vector3(0f, 1f, 0f), SpC, new Vector3(sC * 0.9f, -0.4f, 0.1f));
            c.curl = 40f;
            return c;
        }

        ArmCmd RailArm(Person p, State s, int side)
        {
            if (!p.hasRail) return RestArm(p, s, 0, side, 0f);
            Vector3 a = ToRoot(p, p.railA), b = ToRoot(p, p.railB);
            Vector3 n = DirToRootW(p, p.railN);
            Vector3 want = new Vector3(side * 0.24f * p.s, 0f, 0f);
            Vector3 ab = b - a;
            float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(want - a, ab) / ab.sqrMagnitude) : 0.5f;
            Vector3 rp = Vector3.Lerp(a, b, u) + new Vector3(0f, 0.035f, 0f);
            rp.y = ToRoot(p, new Vector3(0f, p.railTop, 0f) + p.root.position).y + 0.035f;
            if (p.bridgeRail) rp += n * 0.12f;
            var c = IK(SpR, rp, SpR, (0.9f * n + 0.3f * Vector3.down).normalized, Vector3.down, SpR, new Vector3(side * 0.7f, -0.6f, -0.4f));
            HandShape(ref c, 70f, 70f, 50f);
            return c;
        }

        ArmCmd HoldHandArm(Person p, State s, int side, float t, bool adult)
        {
            var o = people[p.partner];
            var so = st[p.partner];
            Person child = adult ? o : p; State cs = adult ? so : s;
            Vector3 mid = (p.root.position + o.root.position) * 0.5f;
            float h = child.root.position.y + cs.restRS[SVSkel.UpArmR].y - 0.15f * child.s - 0.25f * child.s;
            Vector3 H = new Vector3(mid.x, h, mid.z) + p.root.right * (0.02f * Mathf.Sin(TAU * 0.4f * t)) + Vector3.up * (0.015f * Mathf.Sin(TAU * 0.8f * t));
            Vector3 hr = ToRoot(p, H);
            ArmCmd a = adult
                ? IK(SpR, hr + new Vector3(0f, 0.02f, 0f), SpR, new Vector3(0f, -1f, 0.3f), new Vector3(-side, 0f, 0f), SpR, new Vector3(side, -0.6f, -0.4f))
                : IK(SpR, hr, SpR, new Vector3(0f, 0.2f, 1f), new Vector3(-side, 0f, 0f), SpR, new Vector3(side, -0.6f, -0.4f));
            a.aim = false; a.curl = 45f;
            return a;
        }

        void CarryArms(Person p, State s, ref Pose ps)
        {
            for (int i = 0; i < 2; i++)
            {
                int sd = Sides[i];
                var ac = IK(SpC, new Vector3(sd * 0.235f, -0.27f * p.s - 0.05f, 0.30f * p.s), SpC, new Vector3(0f, -0.5f, 0.85f), new Vector3(-sd, 0f, 0f), SpC, new Vector3(sd * 0.6f, -0.75f, -0.25f));
                HandShape(ref ac, 60f, 60f, 45f);
                Arm(ref ps, sd) = ac;
            }
            ps.spineBend += -4f; ps.chestBend += -3f;
            ps.props |= 1 << (int)PropKind.Box;
        }

        void SitPose(Person p, State s, ref Pose ps, float t, float bt, float[] r)
        {
            bool ground = p.sitStyle == 6 || !p.hasSeat;
            float ankleY = s.restRS[SVSkel.FootR].y;
            float hipRestY = s.restRS[SVSkel.ThighR].y;
            ps.shiftW = 0f;
            if (ground)
            {
                // on the ground, hugging the knees
                ps.pelvisOfs = new Vector3(0f, 0.13f * p.s - hipRestY, -0.05f * p.s);
                ps.pelvisTilt = -12f; ps.spineBend = 18f; ps.chestBend = 10f; ps.headPitch = 10f;
                for (int i = 0; i < 2; i++)
                {
                    int sd = Sides[i];
                    ref LegCmd l = ref Leg(ref ps, sd);
                    l.mode = 1; l.target = new Vector3(sd * 0.10f * p.s, ankleY, 0.38f * p.s); l.knee = new Vector3(sd * 0.2f, 1f, 0.6f); l.toeOut = 8f;
                    var ac = IK(sd < 0 ? SpKneeL : SpKneeR, new Vector3(-sd * 0.04f, -0.12f, 0.08f) * p.s, SpR, new Vector3(-sd, -0.2f, 0.1f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sd * 0.7f, -0.5f, 0.3f));
                    ac.aim = false; ac.curl = 55f; Arm(ref ps, sd) = ac;
                }
                ps.lookW = 0.6f;
                return;
            }
            bool slump = r[0] < 0.55f;
            float seatY = p.seatH;
            ps.pelvisOfs = new Vector3(0f, seatY + 0.085f * p.s - hipRestY, 0.02f);
            ps.pelvisTilt = slump ? -8f : 2f; ps.spineBend = slump ? 8f : 0f; ps.chestBend = slump ? 6f : -2f;
            float spread = (p.skirt ? 0.06f : Lerp(0.10f, 0.16f, r[1])) * p.s;
            float reachZ = (0.42f + 0.06f * r[2]) * p.s;
            for (int i = 0; i < 2; i++)
            {
                int sd = Sides[i];
                ref LegCmd l = ref Leg(ref ps, sd);
                Vector3 tgt = new Vector3(sd * spread, ankleY, reachZ);
                Vector3 hip = new Vector3(sd * Mathf.Abs(s.restRS[SVSkel.ThighR].x), seatY + 0.085f * p.s, 0.02f);
                if ((tgt - hip).magnitude > (s.t1 + s.t2) * 0.98f)
                {
                    // feet do not reach the ground: legs swing
                    l.mode = 2; l.plane = 90f; l.elev = 85f; l.flex = 70f + 15f * Mathf.Sin(TAU * 0.7f * t + (sd > 0 ? 0f : Mathf.PI)); l.plantar = 20f;
                }
                else { l.mode = 1; l.target = tgt; l.toeOut = Lerp(8f, 15f, r[3]); l.knee = new Vector3(sd * 0.15f, 0f, 1f); }
            }
            int variant = p.sitStyle > 0 ? p.sitStyle : 1 + Mathf.FloorToInt(r[4] * 3.99f);
            switch (variant)
            {
                case 1: // elbows on knees, hands clasped
                    ps.spineBend = 22f; ps.chestBend = 10f; ps.headPitch = -8f;
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        var ac = IK(sd < 0 ? SpKneeL : SpKneeR, new Vector3(-sd * 0.07f, -0.04f, 0.10f) * p.s, SpR, new Vector3(-sd, -0.3f, 0.3f), new Vector3(0f, 0f, -1f), SpR, new Vector3(sd * 0.4f, -0.6f, 0.2f));
                        ac.aim = false; ac.curl = 50f; Arm(ref ps, sd) = ac;
                    }
                    break;
                case 2: // hands on the thighs
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        var ac = IK(sd < 0 ? SpKneeL : SpKneeR, new Vector3(0f, 0.05f, -0.14f) * p.s, SpR, new Vector3(0f, -0.2f, 0.9f), Vector3.down, SpR, new Vector3(sd * 0.5f, -0.6f, -0.2f));
                        ac.aim = false; Flat(ref ac); Arm(ref ps, sd) = ac;
                    }
                    break;
                case 3: // tea glass
                {
                    int sD = p.domSide;
                    float cyc = Lerp(8f, 15f, r[5]);
                    float u = Mathf.Repeat(bt, cyc);
                    float sip = u < 0.5f ? u / 0.5f : (u < 1.1f ? 1f : (u < 1.6f ? 1f - (u - 1.1f) / 0.5f : 0f));
                    sip = RigMath.SS(0f, 1f, sip);
                    var low = IK(SpC, s.sternumC + new Vector3(sD * 0.06f, -0.22f, 0.22f) * p.s, SpR, new Vector3(0f, 0.3f, 1f), new Vector3(-sD, 0f, 0f), SpC, new Vector3(sD * 0.6f, -0.8f, 0f));
                    var high = IK(SpH, s.mouthH + new Vector3(0f, -0.03f, 0.06f) * p.hs, SpR, new Vector3(0f, 0.3f, 1f), new Vector3(-sD, 0f, 0f), SpC, new Vector3(sD * 0.7f, -0.6f, 0f));
                    var d = sip > 0.5f ? high : low;
                    d.aim = false; HandShape(ref d, 60f, 55f, 45f);
                    Arm(ref ps, sD) = d;
                    ps.headPitch = Mathf.Lerp(8f, -10f, sip);
                    var o = IK(SpC, s.sternumC + new Vector3(-sD * 0.02f, -0.26f, 0.2f) * p.s, SpR, new Vector3(sD * 0.8f, 0f, 0.6f), new Vector3(0f, 1f, 0f), SpC, new Vector3(-sD * 0.6f, -0.8f, 0f));
                    o.aim = false; Flat(ref o); Arm(ref ps, -sD) = o;
                    ps.props |= 1 << (int)PropKind.Tea;
                    ps.worry = 0.2f;
                    break;
                }
                case 4: // holding a blanket closed at the chest
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Sides[i];
                        var ac = IK(SpC, s.sternumC + new Vector3(sd * 0.035f, -0.03f, 0.05f) * p.s, SpC, new Vector3(-sd * 0.2f, 0.9f, 0.2f), new Vector3(0f, 0f, -1f), SpC, new Vector3(sd * 0.6f, -0.8f, 0.1f));
                        ac.aim = false; HandShape(ref ac, 95f, 95f, 70f); Arm(ref ps, sd) = ac;
                    }
                    ps.armL.shrug += 6f; ps.armR.shrug += 6f;
                    break;
                default: // rocking (elders, grief)
                    ps.pelvisTilt += 2.5f * Mathf.Sin(TAU * 0.25f * t); ps.spineBend += 2f * Mathf.Sin(TAU * 0.25f * t + 0.4f);
                    break;
            }
            ps.lookW = 0.8f;
        }

        void WalkPose(Person p, State s, ref Pose ps, float t)
        {
            bool carry = HasProp(p, PropKind.Box);
            bool elder = Elder(p), child = p.role == Role.Child, worker = p.role == Role.Worker;
            float A = carry ? 0.016f : (elder ? 0.010f : (child ? 0.018f : (worker ? 0.022f : 0.020f)));
            float X = carry ? 0.025f : (elder ? 0.025f : (child ? 0.015f : (worker ? 0.018f : 0.020f)));
            float W = carry ? 0f : (elder ? 8f : (child ? 22f : (worker ? 22f : 18f)));
            float lean = carry ? -4f : (elder ? 8f : (worker ? 3f : 2f));
            float phi = s.gaitPhase;
            float c2 = Mathf.Cos(TAU * phi), s2 = Mathf.Sin(TAU * phi), c4 = Mathf.Cos(2f * TAU * phi);
            ps.pelvisOfs = new Vector3(X * Mathf.Sin(TAU * phi - 0.3f), -0.015f * p.s - A * (1f + c4) * 0.5f, 0f);
            ps.pelvisYaw = -6f * c2; ps.pelvisRoll = 3f * s2; ps.pelvisTilt = 4f + 1.5f * c4;
            ps.spineTwist = 4f * c2; ps.chestTwist = 5f * c2; ps.chestLean = -2f * s2;
            ps.spineBend = lean * 0.5f; ps.chestBend = lean * 0.5f;
            ps.headYaw = -0.85f * (ps.pelvisYaw + ps.spineTwist + ps.chestTwist); ps.headPitch = 4f; ps.headRoll = -0.5f * ps.pelvisRoll;
            ps.lookW = 0.6f; ps.shiftW = 0f;
            for (int i = 0; i < 2; i++)
            {
                int sd = Sides[i];
                float pp = Mathf.Repeat(phi + (sd < 0 ? 0.5f : 0f), 1f);
                float el = 3f - W * Mathf.Cos(TAU * (pp + 0.03f));
                var ac = FK(90f, el, -5f, 14f + 0.6f * Mathf.Max(0f, el));
                ac.abd = 6f + p.hipClear; ac.wflex = 6f * Mathf.Sin(TAU * pp + 2f);
                Arm(ref ps, sd) = ac;
                ref LegCmd l = ref Leg(ref ps, sd);
                l.mode = 0; l.target = Vector3.zero;
                l.plantar = pp < 0.08f ? Mathf.Lerp(-15f, 0f, pp / 0.08f) : (pp < 0.45f ? 0f : (pp < 0.62f ? Mathf.Lerp(0f, 32f, (pp - 0.45f) / 0.17f) : (pp < 0.78f ? Mathf.Lerp(32f, 6f, (pp - 0.62f) / 0.16f) : Mathf.Lerp(6f, -15f, (pp - 0.78f) / 0.22f))));
                l.toeOut = 0f;
            }
            if (carry) CarryArms(p, s, ref ps);
        }
    }
}
