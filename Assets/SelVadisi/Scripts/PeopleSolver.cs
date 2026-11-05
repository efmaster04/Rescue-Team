using UnityEngine;

namespace SelVadisi
{
    /// Root-space solver, life layers (weight shift, breathing, gaze, blink), output and walking.
    public partial class PeopleAnimator
    {
        static readonly int[] Sides = { -1, 1 };

        // ------------------------------------------------------------------ spaces

        static Vector3 PointTo(State s, byte sp, Vector3 v, int side)
        {
            switch (sp)
            {
                case SpP: return s.pos[SVSkel.Pelvis] + s.rot[SVSkel.Pelvis] * v;
                case SpS: return s.pos[SVSkel.Spine] + s.rot[SVSkel.Spine] * v;
                case SpC: return s.pos[SVSkel.Chest] + s.rot[SVSkel.Chest] * v;
                case SpH: return s.pos[SVSkel.Head] + s.rot[SVSkel.Head] * v;
                case SpOtherElbow: return s.pos[SVSkel.Fore(-side)] + s.rot[SVSkel.Chest] * v;
                case SpKneeL: return s.pos[SVSkel.ShinL] + v;
                case SpKneeR: return s.pos[SVSkel.ShinR] + v;
                default: return v;
            }
        }

        static Vector3 DirTo(State s, byte sp, Vector3 v)
        {
            switch (sp)
            {
                case SpP: return s.rot[SVSkel.Pelvis] * v;
                case SpS: return s.rot[SVSkel.Spine] * v;
                case SpC: case SpOtherElbow: return s.rot[SVSkel.Chest] * v;
                case SpH: return s.rot[SVSkel.Head] * v;
                default: return v;
            }
        }

        static void FK(Person p, State s, Quaternion[] q, int b)
        {
            int pa = SVSkel.Parent[b];
            s.pos[b] = s.pos[pa] + s.rot[pa] * p.restPos[b];
            s.rot[b] = s.rot[pa] * q[b];
        }

        static Vector3 ToRoot(Person p, Vector3 world) => Quaternion.Inverse(p.root.rotation) * (world - p.root.position);
        static Vector3 DirToRootW(Person p, Vector3 dir) => Quaternion.Inverse(p.root.rotation) * dir;

        // ------------------------------------------------------------------ solve

        void Solve(Person p, State s, ref Pose ps, Quaternion[] q, out Vector3 pelvisLocal, float dt, bool primary)
        {
            var P = s.pos; var R = s.rot;
            for (int i = 0; i < SVSkel.Count; i++) q[i] = Quaternion.identity;
            P[0] = Vector3.zero; R[0] = Quaternion.identity;

            // pelvis, spine, chest
            pelvisLocal = p.restPos[SVSkel.Pelvis] + ps.pelvisOfs;
            q[SVSkel.Pelvis] = RigMath.Pelvis(ps.pelvisTilt, ps.pelvisYaw, ps.pelvisRoll);
            P[SVSkel.Pelvis] = pelvisLocal; R[SVSkel.Pelvis] = q[SVSkel.Pelvis];
            float assist = 0f;
            float gy = s.gazeYaw * ps.lookW;
            if (Mathf.Abs(gy) > 60f) assist = Mathf.Sign(gy) * Mathf.Min(20f, (Mathf.Abs(gy) - 60f) * 0.6f);
            q[SVSkel.Spine] = RigMath.Up(ps.spineBend, ps.spineLean, ps.spineTwist);
            FK(p, s, q, SVSkel.Spine);
            q[SVSkel.Chest] = RigMath.Up(ps.chestBend, ps.chestLean, ps.chestTwist + assist);
            FK(p, s, q, SVSkel.Chest);

            // gaze in the chest frame
            if (primary)
            {
                Vector3 target = ps.lookAtTarget ? ps.lookTarget : s.gazeWorld;
                Vector3 tRS = ToRoot(p, target);
                Vector3 eye = P[SVSkel.Chest] + R[SVSkel.Chest] * (s.restRS[SVSkel.Head] - s.restRS[SVSkel.Chest] + new Vector3(0f, 0.1f * p.hs, 0.06f));
                Vector3 l = Quaternion.Inverse(R[SVSkel.Chest]) * (tRS - eye);
                float lim = ps.yawLimit > 0f ? ps.yawLimit : 75f;
                float yaw = Mathf.Clamp(Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg, -lim, lim);
                float pitch = Mathf.Clamp(-Mathf.Atan2(l.y, new Vector2(l.x, l.z).magnitude) * Mathf.Rad2Deg, -35f, 45f);
                if (l.z < -0.2f && Mathf.Abs(l.x) < 0.3f) yaw = s.gazeYaw;                     // behind: hold
                float tau = p.role == Role.Child ? 0.12f : (p.role == Role.ElderMan || p.role == Role.ElderWoman ? 0.25f : 0.16f);
                if (dt <= 0f) { s.gazeYaw = yaw; s.gazePitch = pitch; }
                else
                {
                    s.gazeYaw = Mathf.SmoothDamp(s.gazeYaw, yaw, ref s.gazeYawV, tau, Mathf.Infinity, dt);
                    s.gazePitch = Mathf.SmoothDamp(s.gazePitch, pitch, ref s.gazePitchV, tau, Mathf.Infinity, dt);
                }
            }
            float hy = Mathf.Lerp(ps.headYaw, s.gazeYaw - ps.neckTwist, ps.lookW);
            float hp = Mathf.Lerp(ps.headPitch, s.gazePitch - ps.neckBend - ps.chestBend * 0.3f, ps.lookW);
            q[SVSkel.Neck] = RigMath.Up(ps.neckBend + 0.35f * hp, 0f, ps.neckTwist + 0.4f * hy);
            FK(p, s, q, SVSkel.Neck);
            q[SVSkel.Head] = RigMath.HeadQ(0.65f * hp, 0.6f * hy, ps.headRoll);
            FK(p, s, q, SVSkel.Head);

            // legs before arms (hands may target the knees)
            SolveLeg(p, s, ref ps.legL, -1, q);
            SolveLeg(p, s, ref ps.legR, 1, q);
            if (ps.firstArm > 0) { SolveArm(p, s, ref ps.armR, 1, q, ps.breath); SolveArm(p, s, ref ps.armL, -1, q, ps.breath); }
            else { SolveArm(p, s, ref ps.armL, -1, q, ps.breath); SolveArm(p, s, ref ps.armR, 1, q, ps.breath); }

            // skirt helpers follow the most forward / most backward thigh
            if (p.skirt)
            {
                // sagittal plane only: in a wide stance the abduction rolled the whole front panel toward one leg and the other came through
                Vector3 dL = Sagittal(q[SVSkel.ThighL] * Vector3.down), dR = Sagittal(q[SVSkel.ThighR] * Vector3.down);
                Vector3 front = dL.z > dR.z ? dL : dR, back = dL.z > dR.z ? dR : dL;
                if (front.z < -0.17f) front = new Vector3(front.x, front.y, -0.17f);
                var qf = Quaternion.FromToRotation(Vector3.down, front);
                float af = Quaternion.Angle(Quaternion.identity, qf);
                if (af > 100f) qf = Quaternion.Slerp(Quaternion.identity, qf, 100f / af);   // deep crouch: the front panel stops at 100 deg
                q[SVSkel.SkirtF] = qf;
                var qb = Quaternion.FromToRotation(Vector3.down, back);
                float ab = Quaternion.Angle(Quaternion.identity, qb);
                if (back.z > 0f && ab > 40f) qb = Quaternion.Slerp(Quaternion.identity, qb, 40f / ab);
                q[SVSkel.SkirtB] = qb;
            }
        }

        static Vector3 Sagittal(Vector3 d)
        {
            d.x = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
        }

        void SolveArm(Person p, State s, ref ArmCmd a, int side, Quaternion[] q, float breath)
        {
            var P = s.pos; var R = s.rot;
            int clav = SVSkel.Clav(side), up = SVSkel.UpArm(side), fo = SVSkel.Fore(side), ha = SVSkel.Hand(side);
            float elev = a.elev, plane = a.plane;
            Vector3 wristT = Vector3.zero; Quaternion handRS = Quaternion.identity;
            if (a.ik)
            {
                Vector3 palm = PointTo(s, a.palmSp, a.palm, side);
                Vector3 F = DirTo(s, a.dirSp, a.F), Pn = DirTo(s, a.dirSp, a.P);
                if (!a.aim) { F = Vector3.down; Pn = -side * Vector3.right; }
                // keep hands out of the torso: push the palm out of an inflated ellipse around the chest
                palm = PushOut(p, s, palm);
                handRS = RigMath.HandAim(F, Pn, side);
                wristT = palm - handRS * new Vector3(0f, -0.42f * s.lh, 0f);
                Vector3 sh = P[SVSkel.Chest] + R[SVSkel.Chest] * (s.restRS[up] - s.restRS[SVSkel.Chest]);
                Vector3 d = Quaternion.Inverse(R[SVSkel.Chest]) * (wristT - sh);
                if (d.sqrMagnitude > 1e-6f)
                {
                    d.Normalize();
                    elev = Mathf.Acos(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
                    plane = Mathf.Atan2(d.z, side * d.x) * Mathf.Rad2Deg;
                }
            }
            // scapular rhythm
            float shrug = Mathf.Clamp((elev - 70f) * 0.28f, 0f, 22f) + a.shrug + 0.9f * breath;
            float protract = Mathf.Clamp(Mathf.Sin(plane * Mathf.Deg2Rad) * elev * 0.12f, -6f, 14f) + a.protract + p.round;
            q[clav] = RigMath.Clav(shrug, protract, side);
            FK(p, s, q, clav);
            if (!a.ik)
            {
                var sw = RigMath.Swing(a.plane, Mathf.Min(a.elev, p.maxElev), a.twist, a.abd, side);
                q[up] = Quaternion.Inverse(q[clav]) * sw;
                FK(p, s, q, up);
                q[fo] = RigMath.Elbow(Mathf.Clamp(a.flex, 0f, 150f));
                FK(p, s, q, fo);
                q[ha] = RigMath.HandFK(a.pron, a.wflex, a.wdev, side);
            }
            else
            {
                Vector3 A = P[clav] + R[clav] * p.restPos[up];
                Vector3 pole = DirTo(s, a.elbowSp, a.elbow);
                if (pole.sqrMagnitude < 1e-6f) pole = new Vector3(side * 0.5f, -0.6f, -0.4f);
                RigMath.TwoBone(A, s.l1, s.l2, wristT, pole, false, out Quaternion upRS, out float flex, out Vector3 joint);
                // elbows must not sink into the torso: rotate the pole outward
                for (int k = 0; k < 4 && InsideTorso(p, s, joint); k++)
                {
                    pole = Quaternion.AngleAxis(-side * 12f, Vector3.forward) * pole + new Vector3(side * 0.3f, 0f, 0f);
                    RigMath.TwoBone(A, s.l1, s.l2, wristT, pole, false, out upRS, out flex, out joint);
                }
                q[up] = Quaternion.Inverse(R[clav]) * upRS;
                FK(p, s, q, up);
                q[fo] = RigMath.Elbow(Mathf.Clamp(flex, 2f, 150f));
                FK(p, s, q, fo);
                Quaternion hl = a.aim ? Quaternion.Inverse(R[fo]) * handRS : RigMath.HandFK(a.pron, a.wflex, a.wdev, side);
                hl.ToAngleAxis(out float ang, out Vector3 ax);
                if (ang > 180f) ang -= 360f;
                if (Mathf.Abs(ang) > 75f && ax.sqrMagnitude > 0.5f) hl = Quaternion.AngleAxis(Mathf.Sign(ang) * 75f, ax);
                q[ha] = hl;
            }
            FK(p, s, q, ha);
            q[SVSkel.Fingers(side)] = RigMath.Curl(a.curl);
            q[SVSkel.Index(side)] = RigMath.Curl(a.index);
            q[SVSkel.Thumb(side)] = RigMath.ThumbQ(a.thumb, side);
            FK(p, s, q, SVSkel.Fingers(side));
            q[SVSkel.ShHlp(side)] = Quaternion.Slerp(Quaternion.identity, q[up], 0.5f);
            q[SVSkel.ElHlp(side)] = Quaternion.Slerp(Quaternion.identity, q[fo], 0.5f);
        }

        static bool InsideTorso(Person p, State s, Vector3 j)
        {
            Vector3 c = s.pos[SVSkel.Spine];
            Vector3 l = Quaternion.Inverse(s.rot[SVSkel.Spine]) * (j - c);
            if (l.y < -0.25f || l.y > 0.35f) return false;
            float rx = p.waistRx + 0.02f, rz = p.waistRz + 0.02f;
            return (l.x * l.x) / (rx * rx) + (l.z * l.z) / (rz * rz) < 1f;
        }

        static Vector3 PushOut(Person p, State s, Vector3 palm)
        {
            Vector3 c = s.pos[SVSkel.Spine];
            Quaternion r = s.rot[SVSkel.Spine];
            Vector3 l = Quaternion.Inverse(r) * (palm - c);
            if (l.y < -0.3f || l.y > 0.42f) return palm;
            float rx = p.waistRx + 0.035f, rz = Mathf.Max(p.waistRz, p.chestRz) + 0.035f;
            float e = (l.x * l.x) / (rx * rx) + (l.z * l.z) / (rz * rz);
            if (e >= 1f) return palm;
            float k = 1f / Mathf.Sqrt(Mathf.Max(e, 1e-4f));
            l.x *= k; l.z *= k;
            return c + r * l;
        }

        void SolveLeg(Person p, State s, ref LegCmd l, int side, Quaternion[] q)
        {
            var P = s.pos; var R = s.rot;
            int th = SVSkel.Thigh(side), sh = SVSkel.Shin(side), ft = SVSkel.Foot(side);
            if (l.mode == 2)
            {
                q[th] = RigMath.Swing(l.plane, l.elev, l.twist, l.abd, side);
                q[sh] = RigMath.Knee(l.flex);
                q[ft] = RigMath.Foot(l.plantar, l.toeOut, side);
                FK(p, s, q, th); FK(p, s, q, sh); FK(p, s, q, ft);
            }
            else
            {
                Vector3 hip = P[SVSkel.Pelvis] + R[SVSkel.Pelvis] * p.restPos[th];
                var foot = s.feet[side < 0 ? 0 : 1];
                Vector3 ankle; float yaw;
                if (l.mode == 0) { ankle = ToRoot(p, foot.plant) + l.target; yaw = foot.yaw + l.yaw; }
                else { ankle = l.target; yaw = l.yaw + side * l.toeOut; }
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 pole = l.knee.sqrMagnitude > 1e-6f ? l.knee : fwd + new Vector3(side * 0.1f, 0.15f, 0f);
                RigMath.TwoBone(hip, s.t1, s.t2, ankle, pole, true, out Quaternion thRS, out float flex, out Vector3 j);
                q[th] = Quaternion.Inverse(R[SVSkel.Pelvis]) * thRS;
                FK(p, s, q, th);
                q[sh] = RigMath.Knee(Mathf.Clamp(flex, 0f, 150f));
                FK(p, s, q, sh);
                Quaternion footRS = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(l.plantar, 0f, 0f);
                q[ft] = Quaternion.Inverse(R[sh]) * footRS;
                FK(p, s, q, ft);
            }
            q[SVSkel.HipHlp(side)] = Quaternion.Slerp(Quaternion.identity, q[th], 0.5f);
            q[SVSkel.KnHlp(side)] = Quaternion.Slerp(Quaternion.identity, q[sh], 0.5f);
        }

        // ------------------------------------------------------------------ gaze targets

        void UpdateGaze(Person p, State s, int index, float t, float dt, ref Pose ps, Vector3 cam, float camD2, bool live)
        {
            if (t >= s.lookTogetherStart && t < s.lookTogetherUntil) { s.gazeWorld = s.lookTogetherAt + Vector3.up * 1.2f; return; }
            if (t < s.gazeNext && live) return;
            float dwell = p.gazeDwell * (0.5f + (float)s.rng.NextDouble());
            s.gazeNext = t + dwell;
            bool kid = p.role == Role.Child, worker = p.role == Role.Worker;
            bool waver = s.cur == Beh.WaveBoth || s.cur == Beh.WaveOne || s.cur == Beh.SignalCloth;
            float camRange = waver ? 40f : 18f;
            float wCam = live && camD2 < camRange * camRange ? (kid ? 40f : worker ? 10f : 25f) : 0f;
            float wHelp = worker ? 10f : 40f, wWater = worker ? 35f : 25f, wFam = p.group >= 0 ? 20f : 0f, wSky = worker ? 0f : 6f;
            float total = wCam + wHelp + wWater + wFam + wSky;
            float r = (float)s.rng.NextDouble() * total;
            Vector3 head = p.root.position + Vector3.up * (s.restRS[SVSkel.Head].y + 0.1f);
            if ((r -= wCam) < 0f) { s.gazeWorld = cam; s.gazeKind = 3; s.gazeNext = t + 1.5f + 2.5f * (float)s.rng.NextDouble(); }
            else if ((r -= wHelp) < 0f) { s.gazeWorld = p.helpSource + Vector3.up * 1.2f; s.gazeKind = 0; }
            else if ((r -= wWater) < 0f)
            {
                Vector3 jitter = new Vector3((float)s.rng.NextDouble() - 0.5f, 0f, (float)s.rng.NextDouble() - 0.5f) * 10f;
                s.gazeWorld = p.waterPoint + jitter; s.gazeKind = 1;
            }
            else if ((r -= wFam) < 0f)
            {
                int pick = -1, seen = 0;
                for (int j = 0; j < people.Count; j++)
                    if (j != index && people[j].group == p.group && people[j].root != null && s.rng.Next(++seen) == 0) pick = j;
                if (pick >= 0)
                {
                    var o = people[pick];
                    s.gazeWorld = o.root.position + Vector3.up * (st[pick].restRS[SVSkel.Head].y + 0.08f);
                    s.gazeKind = 2;
                }
                else s.gazeWorld = p.helpSource + Vector3.up;
            }
            else { s.gazeWorld = head + p.root.forward * 10f + Vector3.up * 6f; s.gazeKind = 4; }
            if (p.pointTarget != Vector3.zero && s.cur == Beh.Point) s.gazeWorld = p.pointTarget;
            // 30% of gaze switches blink
            if (s.rng.NextDouble() < 0.3 && s.blinkT < 0f) s.blinkT = 0f;
        }

        // ------------------------------------------------------------------ write

        void Write(Person p, State s, ref Pose ps, float t, float dt)
        {
            var bones = p.bones;
            for (int b = 1; b < SVSkel.Count; b++)
            {
                if (b == SVSkel.Eyes || b == SVSkel.Mouth || bones[b] == null) continue;
                bones[b].localRotation = s.q[b];
            }
            if (bones[SVSkel.Pelvis] != null) bones[SVSkel.Pelvis].localPosition = s.pelvisLocal;

            // blink: ramp 0.06 s, hold 0.03 s, open 0.11 s
            float closed = 0f;
            if (s.blinkT >= 0f)
            {
                s.blinkT += dt;
                float bt = s.blinkT;
                closed = bt < 0.06f ? bt / 0.06f : (bt < 0.09f ? 1f : Mathf.Max(0f, 1f - (bt - 0.09f) / 0.11f));
                if (bt > 0.2f)
                {
                    s.blinkT = -1f;
                    if (s.doubleBlink) { s.doubleBlink = false; s.blinkT = -0.05f; }
                }
            }
            else if (t >= s.blinkNext && dt > 0f)
            {
                s.blinkT = 0f;
                s.doubleBlink = s.rng.NextDouble() < 0.12;
                s.blinkNext = t + Mathf.Min(7f, 0.8f + p.blinkInterval * (-Mathf.Log(Mathf.Max(0.01f, (float)s.rng.NextDouble())) * 0.6f));
            }
            float open = Mathf.Min(ps.eyesOpen, 1f - closed);
            if (bones[SVSkel.Eyes] != null) bones[SVSkel.Eyes].localScale = new Vector3(1f, Mathf.Lerp(0.08f, 1f, open), 1f);
            float m = Mathf.Clamp01(ps.mouth);
            if (bones[SVSkel.Mouth] != null) bones[SVSkel.Mouth].localScale = m < 0.05f ? Vector3.zero : new Vector3(0.75f + 0.25f * m, m, 1f);

            // props
            for (int i = 0; i < p.props.Length; i++)
            {
                var pr = p.props[i];
                if (pr == null || pr.t == null) continue;
                bool on = pr.always || (ps.props & (1 << (int)pr.kind)) != 0;
                if (pr.t.gameObject.activeSelf != on) pr.t.gameObject.SetActive(on);
                if (!on) continue;
                if (pr.kind == PropKind.Umbrella || pr.kind == PropKind.Tea)
                {
                    // keep upright in the world (umbrella leans a little into the wind)
                    float n = RigMath.Noise(t * 0.3f + p.seed, p.seed);
                    Vector3 upW = pr.kind == PropKind.Umbrella ? (Vector3.up + new Vector3(0.6f, 0f, 0.8f) * Mathf.Tan((6f + 4f * n) * Mathf.Deg2Rad)).normalized : Vector3.up;
                    Vector3 fwdW = Vector3.ProjectOnPlane(p.root.forward, upW);
                    pr.t.rotation = Quaternion.LookRotation(fwdW, upW);
                }
            }
        }

        // ------------------------------------------------------------------ layers

        void UpdateLayers(Person p, State s, float t, float dt)
        {
            if (dt <= 0f) return;
            if (t >= s.shiftNext)
            {
                double r = s.rng.NextDouble();
                s.sigma = r < 0.35 ? 0f : -Mathf.Sign(s.sigma == 0f ? (s.rng.NextDouble() < 0.5 ? 1f : -1f) : s.sigma);
                s.shiftNext = t + p.shiftPeriod * (0.6f + 0.8f * (float)s.rng.NextDouble());
            }
            float k = 1f - Mathf.Exp(-dt / 0.45f);
            s.omega += (s.sigma - s.omega) * k;
            if (t >= s.restStyleNext)
            {
                s.restStyleNext = t + 20f + 20f * (float)s.rng.NextDouble();
                if (s.rng.NextDouble() < 0.5) s.restStyle = p.restStyle; else s.restStyle = PickRestStyle(p, s.rng);
            }
        }

        static void AddLayers(Person p, State s, ref Pose ps, float t)
        {
            float sc = p.s;
            // contrapposto
            float w = s.omega * ps.shiftW;
            float ys = (p.seed & 1) == 0 ? 1f : -1f;
            ps.pelvisOfs.x += 0.035f * sc * w;
            ps.pelvisRoll += 4.5f * w;
            ps.pelvisYaw += 3f * w * ys;
            ps.spineLean += 3.5f * w;
            ps.chestLean += 3f * w;
            ps.chestTwist += -2f * w * ys;
            ps.headRoll += -2f * w;
            // breathing
            float rate = p.role == Role.Child ? 0.33f : (p.role == Role.ElderMan || p.role == Role.ElderWoman ? 0.22f : 0.25f);
            rate *= (0.85f + 0.3f * RigMath.Hash01(p.seed, 3)) * (1f + 1.2f * s.arousal);
            float A = 1f + 1.5f * s.arousal;
            float ph = 2f * Mathf.PI * rate * t + p.seed;
            float bw = Mathf.Sin(ph + 0.3f * Mathf.Sin(ph));
            ps.chestBend += -0.8f * A * bw;
            ps.breath = bw * A;
            ps.spineBend += -0.3f * A * bw;
            ps.headPitch += -0.4f * A * bw;
            ps.mouth += 0.15f * s.arousal;
            // idle noise (incommensurate rates)
            ps.chestTwist += 2f * RigMath.Noise(t * 0.07f, p.seed + 11);
            if (ps.lookW < 1f) ps.headYaw += 5f * RigMath.Noise(t * 0.1f, p.seed + 12) * (1f - ps.lookW);
            if (!ps.armL.ik) ps.armL.elev += 2f * RigMath.Noise(t * 0.15f, p.seed + 13);
            if (!ps.armR.ik) ps.armR.elev += 2f * RigMath.Noise(t * 0.15f, p.seed + 14);
            ps.armL.curl += 8f * RigMath.Noise(t * 0.2f, p.seed + 15);
            ps.armR.curl += 8f * RigMath.Noise(t * 0.2f, p.seed + 16);
            // startle (lightning): fast flinch, then look at the sky
            float se = t - s.startleT;
            if (se >= 0f && se < 3f)
            {
                float e = se < 0.12f ? se / 0.12f : Mathf.Max(0f, 1f - (se - 0.12f) / 0.58f);
                ps.armL.shrug += 15f * e; ps.armR.shrug += 15f * e;
                ps.headPitch += 12f * e; ps.spineBend += 6f * e; ps.pelvisOfs.y -= 0.04f * e;
                ps.armL.flex += 20f * e; ps.armR.flex += 20f * e;
                if (se < 0.25f) ps.eyesOpen = 0f;
                if (se > 0.7f) { ps.lookAtTarget = true; ps.lookTarget = p.root.position + p.root.forward * 8f + Vector3.up * 30f; ps.lookW = Mathf.Max(ps.lookW, 0.8f); ps.mouth = Mathf.Max(ps.mouth, 0.3f); }
            }
            float le = t - s.lookUpT;
            if (le >= 0f && le < 1.6f) { ps.lookAtTarget = true; ps.lookTarget = p.root.position + p.root.forward * 6f + Vector3.up * 40f; ps.lookW = Mathf.Max(ps.lookW, 0.9f); }
            // no umbrella: hunch a little against the rain
            if (!p.umbrella && p.role != Role.Worker) { ps.armL.shrug += 2f; ps.armR.shrug += 2f; ps.headPitch += 3f; }
        }

        // ------------------------------------------------------------------ walking on a lane

        void StepLane(Person p, State s, float dt)
        {
            if (dt <= 0f) return;
            var lane = p.lane;
            float total = 0f;
            for (int i = 0; i + 1 < lane.Length; i++) total += Vector3.Distance(lane[i], lane[i + 1]);
            if (total < 0.5f) return;
            bool carry = System.Array.IndexOf(p.pool, Beh.Carry) >= 0 && p.role == Role.Worker;
            float vmax = p.role == Role.Worker ? (carry ? 0.95f : 1.35f) : (p.role == Role.Child ? 1.0f : 1.15f);
            float dist = s.laneS * total;
            float remain = s.laneDir > 0 ? total - dist : dist;
            float target = remain < 1.2f ? 0.3f : vmax;
            s.laneSpeed = Mathf.MoveTowards(s.laneSpeed, target, 1.5f * dt);
            dist += s.laneDir * s.laneSpeed * dt;
            if (dist >= total) { dist = total; s.laneDir = -1; }
            if (dist <= 0f) { dist = 0f; s.laneDir = 1; }
            s.laneS = dist / total;
            // point and tangent on the polyline
            float acc = 0f; Vector3 pos = lane[0], tan = Vector3.forward;
            for (int i = 0; i + 1 < lane.Length; i++)
            {
                float seg = Vector3.Distance(lane[i], lane[i + 1]);
                if (dist <= acc + seg || i + 2 == lane.Length)
                {
                    float f = seg > 1e-4f ? Mathf.Clamp01((dist - acc) / seg) : 0f;
                    pos = Vector3.Lerp(lane[i], lane[i + 1], f);
                    tan = (lane[i + 1] - lane[i]).normalized * s.laneDir;
                    break;
                }
                acc += seg;
            }
            float yawT = Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg;
            s.rootYaw = Mathf.SmoothDampAngle(s.rootYaw, yawT, ref s.rootYawV, 0.25f, 150f, dt);
            pos.y = Valley.TerrainHeight(pos.x, pos.z);
            p.root.SetPositionAndRotation(pos, Quaternion.Euler(0f, s.rootYaw, 0f));
            float fc = p.role == Role.Child ? 1.15f : (carry ? 0.825f : (p.role == Role.Worker ? 0.95f : 0.875f));
            fc *= Mathf.Lerp(0.8f, 1f, s.laneSpeed / Mathf.Max(0.1f, vmax));
            s.gaitPhase = Mathf.Repeat(s.gaitPhase + fc * dt, 1f);
            // feet: world-locked stance, planned swing
            Vector3 vel = tan * s.laneSpeed;
            float stride = s.laneSpeed / fc;
            for (int k = 0; k < 2; k++)
            {
                int side = k == 0 ? -1 : 1;
                float ph = Mathf.Repeat(s.gaitPhase + (side < 0 ? 0.5f : 0f), 1f);
                var f = s.feet[k];
                const float stanceEnd = 0.62f;
                if (ph < stanceEnd)
                {
                    if (f.swing) { f.swing = false; f.plant = f.land; f.yaw = f.landYaw; }
                }
                else
                {
                    if (!f.swing) { f.swing = true; f.from = f.plant; f.fromYaw = f.yaw; }
                    float w = (ph - stanceEnd) / (1f - stanceEnd), tLeft = (1f - ph) / fc;
                    f.land = pos + vel * tLeft + Quaternion.Euler(0f, s.rootYaw, 0f) * new Vector3(side * p.stance, 0f, stride * stanceEnd * 0.5f);
                    f.land.y = Valley.TerrainHeight(f.land.x, f.land.z) + s.restRS[SVSkel.Foot(side)].y;
                    f.landYaw = 0f;
                    float e = w * w * (3f - 2f * w);
                    Vector3 sw = Vector3.Lerp(f.from, f.land, e);
                    sw.y += 0.1f * p.s * Mathf.Pow(Mathf.Sin(Mathf.PI * w), 0.8f);
                    f.plant = sw;
                    f.yaw = Mathf.LerpAngle(f.fromYaw, f.landYaw, e);
                }
            }
        }
    }
}
