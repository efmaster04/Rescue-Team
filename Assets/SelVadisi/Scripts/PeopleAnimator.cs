using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi
{
    /// What a person can be doing. Each person gets a small pool of these from their location and role.
    public enum Beh
    {
        Stand, WaveBoth, WaveOne, JumpWave, CallCupped, Phone, Point, HugChild, Comfort, Shiver, Sit, Kneel, Crouch,
        Walk, Direct, Radio, RestKnees, HandsOnHead, HandOnCheek, Film, HoldBaby, HoldHands, Cling, Talk, LeanRail,
        Dua, SignalCloth, Carry, Count
    }

    public enum PropKind { None, Phone, Radio, Cane, Tea, Baby, Box, Umbrella, Cloth, Beads, Megaphone, Clipboard, Count }

    public enum Role { Man, Woman, ElderMan, ElderWoman, Teen, Child, Worker }

    [System.Serializable]
    public class PropRef
    {
        public Transform t;
        public PropKind kind;
        public int side = 1;          // hand that holds it
        public bool always;           // shown regardless of the behaviour (cane, beads, umbrella)
        public Vector3 gripLocal;     // grip point in the prop's own space
    }

    /// Everything the generator bakes for one character. Positions are in the person's root space unless noted.
    [System.Serializable]
    public class Person
    {
        public Transform root;
        public Transform[] bones = new Transform[SVSkel.Count];
        public Vector3[] restPos = new Vector3[SVSkel.Count];   // rest local positions (identity rotations)
        public SkinnedMeshRenderer smr;
        public int id, seed;
        public Role role;
        public int domSide = 1;
        public float s = 1f, hs = 1f;                          // body size H/1.75 and head size (head height / 0.26)
        public float tempo = 1f, amp = 1f, energy = 0.6f, slouch, stance = 0.12f, toeOut = 10f, habitPitch, habitRoll, round, hipClear = 5f;
        public float shiftPeriod = 7f, gazeDwell = 3f, blinkInterval = 4f, maxElev = 178f, worry = 0.4f;
        public bool skirt, female, umbrella, pockets = true;
        public int umbrellaSide = 1;
        public Beh[] pool = { Beh.Stand };
        public float[] poolWeight = { 1f };
        public int restStyle;
        public int group = -1, partner = -1;
        public bool adultInPair;
        // landmarks (rest, root space)
        public Vector3 lmSternum, lmBelly, lmSmallOfBack, lmHipSideL, lmHipSideR, lmPocketL, lmPocketR;
        public Vector3 lmMouth, lmEarL, lmEarR, lmCheekL, lmCheekR, lmForehead, lmCrown;
        public float waistRx = 0.15f, waistRz = 0.1f, chestRz = 0.12f;
        // anchors (world space)
        public bool hasSeat; public Vector3 seatPos, seatFwd; public float seatH = 0.45f; public int sitStyle;
        public bool hasRail; public Vector3 railA, railB, railN; public float railTop; public bool bridgeRail;
        public bool hasKnob; public Vector3 knob;
        public Vector3[] lane;
        public Vector3 helpSource, waterPoint, pointTarget;
        public PropRef[] props = new PropRef[0];
        public float startEnergy = 0.6f;
    }

    // ------------------------------------------------------------------ pose records

    public struct ArmCmd
    {
        public bool ik, aim;
        public float plane, elev, twist, flex, abd;      // FK swing (chest space)
        public Vector3 palm, F, P, elbow;                 // IK palm target, finger dir, palm normal, elbow pole
        public byte palmSp, dirSp, elbowSp;               // spaces (see PeopleAnimator.Sp*)
        public float pron, wflex, wdev;                   // wrist
        public float curl, index, thumb;                  // hand shape
        public float shrug, protract;                     // extra clavicle command
    }

    public struct LegCmd
    {
        public byte mode;                                 // 0 planted (+offset), 1 root-space target, 2 FK
        public Vector3 target, knee;
        public float plantar, toeOut, yaw;
        public float plane, elev, twist, flex, abd;
    }

    public struct Pose
    {
        public Vector3 pelvisOfs; public float pelvisTilt, pelvisYaw, pelvisRoll;
        public float spineBend, spineLean, spineTwist, chestBend, chestLean, chestTwist, neckBend, neckTwist;
        public float headPitch, headYaw, headRoll;
        public ArmCmd armL, armR;
        public LegCmd legL, legR;
        public float lookW, yawLimit, shiftW, mouth, worry, eyesOpen, breath;
        public int props;                                 // bitmask of visible PropKinds
        public int firstArm;                              // side solved first (+1 / -1) when one arm targets the other
        public bool lookAtTarget; public Vector3 lookTarget;   // forced gaze (world)
    }

    /// Procedural animation for every villager and rescue worker. Behaviour -> Pose -> layers -> root-space solver
    /// (FK swings + analytic two-bone IK, planted feet) -> one localRotation write per bone.
    [DefaultExecutionOrder(50)]
    public partial class PeopleAnimator : MonoBehaviour
    {
        public List<Person> people = new List<Person>();

        public const byte SpR = 0, SpP = 1, SpS = 2, SpC = 3, SpH = 4, SpOtherElbow = 5, SpKneeL = 6, SpKneeR = 7;

        class Foot { public Vector3 plant, from, land; public float yaw, fromYaw, landYaw; public bool swing; }

        class State
        {
            public Vector3[] restRS = new Vector3[SVSkel.Count];
            public float l1, l2, lh, t1, t2;
            // landmarks in their bone spaces
            public Vector3 sternumC, bellyS, smallBackP, hipSideLP, hipSideRP, pocketLP, pocketRP;
            public Vector3 mouthH, earLH, earRH, cheekLH, cheekRH, foreheadH, crownH;
            // solver scratch
            public Vector3[] pos = new Vector3[SVSkel.Count];
            public Quaternion[] rot = new Quaternion[SVSkel.Count];
            public Quaternion[] q = new Quaternion[SVSkel.Count], q2 = new Quaternion[SVSkel.Count];
            public Vector3 pelvisLocal, pelvisLocal2;
            // brain
            public Beh cur = Beh.Stand, prev = Beh.Stand;
            public float curStart, nextSwitch, blendStart = -10f, blendDur = 0.6f;
            public Beh blendFrom = Beh.Stand; public bool blending;
            public float[] r = new float[8], rPrev = new float[8];
            public float energy, arousal;
            public System.Random rng;
            // layers
            public float omega, sigma, shiftNext;
            public float gazeYaw, gazePitch, gazeYawV, gazePitchV, gazeNext;
            public Vector3 gazeWorld; public int gazeKind;
            public float blinkT = -1f, blinkNext; public bool doubleBlink;
            public float startleT = -10f, lookUpT = -10f, wipeT = -10f, wipeNext;
            public Foot[] feet = { new Foot(), new Foot() };
            public Vector3 plantL, plantR;   // world
            // walking
            public float laneS, laneSpeed; public int laneDir = 1, laneSeg; public float gaitPhase, rootYaw, rootYawV;
            public Vector3 rootPos;
            public float restStyleNext; public int restStyle;
            public float pendingDt; public bool init;
            public float wristLagL, wristLagR, prevForeL, prevForeR;
            // group beats
            public Beh forcedNext = Beh.Count;
            public float lookTogetherUntil = -1f, lookTogetherStart; public Vector3 lookTogetherAt;
        }

        State[] st;
        static readonly List<PeopleAnimator> live = new List<PeopleAnimator>();
        float eventNext;

        void OnEnable() { live.Add(this); LightningFlash.Struck += OnLightning; }
        void OnDisable() { live.Remove(this); LightningFlash.Struck -= OnLightning; }

        void Start() { InitAll(); }

        void InitAll()
        {
            st = new State[people.Count];
            for (int i = 0; i < people.Count; i++)
            {
                st[i] = new State();
                if (people[i].root != null) Init(people[i], st[i], i, Time.time);
            }
        }

        // ------------------------------------------------------------------ setup

        static void Init(Person p, State s, int index, float now)
        {
            s.rng = new System.Random(p.seed * 7919 + 17);
            var rp = p.restPos;
            s.restRS[SVSkel.Root] = Vector3.zero;
            for (int b = 1; b < SVSkel.Count; b++) s.restRS[b] = s.restRS[SVSkel.Parent[b]] + rp[b];
            s.l1 = Vector3.Distance(s.restRS[SVSkel.UpArmR], s.restRS[SVSkel.ForeR]);
            s.l2 = Vector3.Distance(s.restRS[SVSkel.ForeR], s.restRS[SVSkel.HandR]);
            s.lh = 2f * Vector3.Distance(s.restRS[SVSkel.HandR], s.restRS[SVSkel.FingersR]);
            if (s.lh < 0.05f) s.lh = 0.18f * p.s;
            s.t1 = Vector3.Distance(s.restRS[SVSkel.ThighR], s.restRS[SVSkel.ShinR]);
            s.t2 = Vector3.Distance(s.restRS[SVSkel.ShinR], s.restRS[SVSkel.FootR]);
            s.sternumC = p.lmSternum - s.restRS[SVSkel.Chest];
            s.bellyS = p.lmBelly - s.restRS[SVSkel.Spine];
            s.smallBackP = p.lmSmallOfBack - s.restRS[SVSkel.Pelvis];
            s.hipSideLP = p.lmHipSideL - s.restRS[SVSkel.Pelvis];
            s.hipSideRP = p.lmHipSideR - s.restRS[SVSkel.Pelvis];
            s.pocketLP = p.lmPocketL - s.restRS[SVSkel.Pelvis];
            s.pocketRP = p.lmPocketR - s.restRS[SVSkel.Pelvis];
            Vector3 h = s.restRS[SVSkel.Head];
            s.mouthH = p.lmMouth - h; s.earLH = p.lmEarL - h; s.earRH = p.lmEarR - h;
            s.cheekLH = p.lmCheekL - h; s.cheekRH = p.lmCheekR - h; s.foreheadH = p.lmForehead - h; s.crownH = p.lmCrown - h;
            // planted feet (world): stance width and toe-out
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 a = s.restRS[SVSkel.Foot(side)];
                float fwd = side == -p.domSide ? (float)s.rng.NextDouble() * 0.06f * p.s : 0f;
                Vector3 local = new Vector3(side * Mathf.Max(Mathf.Abs(a.x), p.stance), a.y, a.z + fwd);
                var f = s.feet[side < 0 ? 0 : 1];
                f.plant = p.root.TransformPoint(local);
                f.yaw = side * p.toeOut;
            }
            s.rootPos = p.root.position; s.rootYaw = p.root.eulerAngles.y;
            s.energy = p.startEnergy;
            s.cur = p.pool.Length > 0 ? p.pool[0] : Beh.Stand;
            s.curStart = now - (float)s.rng.NextDouble() * 4f;
            s.nextSwitch = now + Duration(s.cur, p, s) * (0.3f + 0.7f * (float)s.rng.NextDouble());
            Roll(s.r, s.rng);
            s.shiftNext = now + p.shiftPeriod * (float)s.rng.NextDouble();
            s.sigma = s.rng.NextDouble() < 0.5 ? -1f : 1f; s.omega = s.sigma;
            s.blinkNext = now + (float)s.rng.NextDouble() * p.blinkInterval;
            s.gazeNext = now;
            s.wipeNext = now + 20f + 40f * (float)s.rng.NextDouble();
            s.restStyle = p.restStyle; s.restStyleNext = now + 20f + 20f * (float)s.rng.NextDouble();
            if (p.lane != null && p.lane.Length > 1) { s.laneS = (float)s.rng.NextDouble(); s.laneDir = 1; }
            s.init = true;
        }

        static void Roll(float[] r, System.Random rng) { for (int i = 0; i < r.Length; i++) r[i] = (float)rng.NextDouble(); }

        // ------------------------------------------------------------------ main loop

        void LateUpdate()
        {
            if (st == null || st.Length != people.Count) InitAll();
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            var camT = Camera.main != null ? Camera.main.transform : null;
            Vector3 cam = camT != null ? camT.position : Vector3.zero;
            GroupEvents(t);
            int frame = Time.frameCount;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i]; var s = st[i];
                if (p.root == null || !s.init) continue;
                Brain(p, s, i, t);
                float d2 = (p.root.position - cam).sqrMagnitude;
                int every = d2 > 90f * 90f ? 4 : (d2 > 45f * 45f ? 2 : 1);
                bool visible = p.smr == null || p.smr.isVisible;
                if (!visible || (frame + i) % every != 0) { s.pendingDt += dt; continue; }
                float pdt = dt + s.pendingDt; s.pendingDt = 0f;
                Animate(p, s, i, t, pdt, cam, d2, true);
            }
        }

        /// Poses one character directly (editor bake and the first frame).
        public void Bake(int index, float t)
        {
            if (st == null || st.Length != people.Count)
            {
                // partners read each other's rest skeleton, so every state is initialised up front
                st = new State[people.Count];
                for (int i = 0; i < people.Count; i++) { st[i] = new State(); if (people[i].root != null) Init(people[i], st[i], i, 0f); }
            }
            var p = people[index]; var s = st[index];
            Init(p, s, index, 0f);
            s.cur = p.pool.Length > 0 ? p.pool[0] : Beh.Stand;
            s.curStart = -t;
            Animate(p, s, index, 0f, 0f, p.root.position + p.root.forward * 30f, 900f, false);
        }

        void Animate(Person p, State s, int index, float t, float dt, Vector3 cam, float camD2, bool live)
        {
            UpdateLayers(p, s, t, dt);
            // walking moves the root first
            if (s.cur == Beh.Walk && p.lane != null && p.lane.Length > 1) StepLane(p, s, dt);

            var pose = EvalFull(p, s, s.cur, t, t - s.curStart, s.r, index);
            UpdateGaze(p, s, index, t, dt, ref pose, cam, camD2, live);
            Solve(p, s, ref pose, s.q, out s.pelvisLocal, dt, true);
            if (s.blending)
            {
                float w = RigMath.SS(0f, 1f, (t - s.blendStart) / s.blendDur);
                if (w >= 1f) s.blending = false;
                else
                {
                    var pose2 = EvalFull(p, s, s.blendFrom, t, t - s.blendStart + 4f, s.rPrev, index);
                    pose2.lookW = pose.lookW;
                    Solve(p, s, ref pose2, s.q2, out s.pelvisLocal2, dt, false);
                    for (int b = 0; b < SVSkel.Count; b++) s.q[b] = Quaternion.Slerp(s.q2[b], s.q[b], w);
                    s.pelvisLocal = Vector3.Lerp(s.pelvisLocal2, s.pelvisLocal, w);
                    // props switch half way
                    if (w < 0.4f) pose.props = pose2.props;
                    pose.mouth = Mathf.Lerp(pose2.mouth, pose.mouth, w);
                }
            }
            Write(p, s, ref pose, t, dt);
        }

        // ------------------------------------------------------------------ brain

        static float Duration(Beh b, Person p, State s)
        {
            float u = (float)s.rng.NextDouble();
            switch (b)
            {
                case Beh.WaveBoth: return Mathf.Lerp(4f, 10f, u);
                case Beh.WaveOne: return Mathf.Lerp(4f, 12f, u);
                case Beh.JumpWave: return Mathf.Lerp(4f, 8f, u);
                case Beh.CallCupped: return Mathf.Lerp(5f, 10f, u);
                case Beh.Phone: return Mathf.Lerp(10f, 30f, u);
                case Beh.Point: return Mathf.Lerp(3f, 8f, u);
                case Beh.HugChild: case Beh.Comfort: return Mathf.Lerp(8f, 20f, u);
                case Beh.Shiver: return Mathf.Lerp(8f, 20f, u);
                case Beh.Sit: return Mathf.Lerp(30f, 120f, u);
                case Beh.Kneel: return Mathf.Lerp(20f, 60f, u);
                case Beh.Crouch: return Mathf.Lerp(15f, 40f, u);
                case Beh.Walk: return Mathf.Lerp(15f, 40f, u);
                case Beh.Direct: return Mathf.Lerp(4f, 10f, u);
                case Beh.Radio: return Mathf.Lerp(6f, 15f, u);
                case Beh.RestKnees: return Mathf.Lerp(3f, 6f, u);
                case Beh.HandsOnHead: return Mathf.Lerp(3f, 7f, u);
                case Beh.HandOnCheek: return Mathf.Lerp(5f, 12f, u);
                case Beh.Film: return Mathf.Lerp(6f, 15f, u);
                case Beh.HoldBaby: return Mathf.Lerp(10f, 30f, u);
                case Beh.HoldHands: case Beh.Cling: return Mathf.Lerp(10f, 25f, u);
                case Beh.Talk: return Mathf.Lerp(10f, 40f, u);
                case Beh.LeanRail: return Mathf.Lerp(8f, 25f, u);
                case Beh.Dua: return Mathf.Lerp(6f, 12f, u);
                case Beh.SignalCloth: return Mathf.Lerp(5f, 12f, u);
                case Beh.Carry: return Mathf.Lerp(10f, 25f, u);
                default: return Mathf.Lerp(5f, 12f, u);
            }
        }

        static bool HighEnergy(Beh b) => b == Beh.WaveBoth || b == Beh.JumpWave || b == Beh.CallCupped || b == Beh.SignalCloth;
        static bool IsRest(Beh b) => b == Beh.Stand || b == Beh.RestKnees || b == Beh.Sit || b == Beh.Crouch || b == Beh.LeanRail;

        void Brain(Person p, State s, int index, float t)
        {
            float dt = Time.deltaTime;
            // energy: frantic behaviours drain, rest recovers
            if (HighEnergy(s.cur)) s.energy = Mathf.Max(0f, s.energy - 0.06f * dt);
            else s.energy = Mathf.Min(1f, s.energy + (s.cur == Beh.RestKnees ? 0.12f : 0.03f) * dt);
            s.arousal = Mathf.Max(0f, s.arousal - dt / 20f);
            if (t < s.nextSwitch || p.pool.Length <= 1) return;
            // pick the next behaviour
            float total = 0f;
            int n = p.pool.Length;
            var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                Beh b = p.pool[i];
                float x = i < p.poolWeight.Length ? p.poolWeight[i] : 1f;
                if (HighEnergy(b)) x *= Mathf.Max(0.05f, s.energy * s.energy);
                if (IsRest(b)) x *= Mathf.Max(0.15f, (1f - s.energy) * (1f - s.energy) + 0.3f);
                if (b == s.cur) x *= 0.15f;
                if (b == s.prev) x *= 0.6f;
                w[i] = x; total += x;
            }
            float pick = (float)s.rng.NextDouble() * total;
            Beh next = p.pool[0];
            for (int i = 0; i < n; i++) { pick -= w[i]; if (pick <= 0f) { next = p.pool[i]; break; } }
            Switch(p, s, next, t);
        }

        void Switch(Person p, State s, Beh next, float t)
        {
            float dur = 0.6f;
            if (next == Beh.Sit || s.cur == Beh.Sit) dur = 1.3f;
            else if (next == Beh.Kneel || next == Beh.Crouch || s.cur == Beh.Kneel || s.cur == Beh.Crouch) dur = 1.1f;
            else if (next == Beh.WaveBoth || next == Beh.WaveOne || next == Beh.SignalCloth) dur = 0.45f;
            s.blendFrom = s.cur; s.blendStart = t; s.blendDur = dur; s.blending = true;
            System.Array.Copy(s.r, s.rPrev, s.r.Length);
            s.prev = s.cur; s.cur = next;
            Roll(s.r, s.rng);
            s.curStart = t;
            s.nextSwitch = t + Duration(next, p, s) * (p.role == Role.ElderMan || p.role == Role.ElderWoman ? 1.3f : 1f);
        }

        /// Family beats: every few seconds a group does something together (wave together, look at the same thing).
        void GroupEvents(float t)
        {
            if (t < eventNext) return;
            eventNext = t + 2f;
            var seen = new HashSet<int>();
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.group < 0 || seen.Contains(p.group)) continue;
                seen.Add(p.group);
                var s = st[i];
                if (s == null || s.rng == null) continue;
                if (s.rng.NextDouble() > 0.18) continue;     // about every 11 s per group
                double roll = s.rng.NextDouble();
                int rank = 0;
                for (int j = 0; j < people.Count; j++)
                {
                    if (people[j].group != p.group || st[j] == null || !st[j].init) continue;
                    var pj = people[j]; var sj = st[j];
                    if (roll < 0.4)
                    {
                        // all wave: those who can, start waving staggered
                        Beh wb = System.Array.IndexOf(pj.pool, Beh.WaveBoth) >= 0 ? Beh.WaveBoth : (System.Array.IndexOf(pj.pool, Beh.WaveOne) >= 0 ? Beh.WaveOne : Beh.Count);
                        if (wb != Beh.Count && sj.cur != wb && sj.energy > 0.25f && !Bound(sj.cur))
                        {
                            sj.nextSwitch = t + 0.3f + 0.35f * rank + 0.3f * (float)sj.rng.NextDouble();
                            sj.forcedNext = wb;
                        }
                    }
                    else if (roll < 0.75)
                    {
                        // look together at the help source / something on the water
                        sj.lookTogetherUntil = t + 2f + 2f * (float)sj.rng.NextDouble();
                        sj.lookTogetherAt = p.helpSource;
                        sj.lookTogetherStart = t + 0.1f + 0.15f * rank;
                    }
                    rank++;
                }
            }
            // apply forced switches
            for (int i = 0; i < people.Count; i++)
            {
                var s = st[i];
                if (s == null || s.forcedNext == Beh.Count || t < s.nextSwitch) continue;
                Switch(people[i], s, s.forcedNext, t);
                s.forcedNext = Beh.Count;
            }
        }

        static bool Bound(Beh b) => b == Beh.Sit || b == Beh.Kneel || b == Beh.HoldBaby || b == Beh.HugChild || b == Beh.HoldHands || b == Beh.Cling || b == Beh.Walk || b == Beh.Carry;

        void OnLightning()
        {
            if (st == null) return;
            float t = Time.time;
            for (int i = 0; i < people.Count; i++)
            {
                var s = st[i];
                if (s == null || s.rng == null) continue;
                float d = 0.08f + 0.25f * (float)s.rng.NextDouble();
                double r = s.rng.NextDouble();
                if (r < 0.5) s.startleT = t + d;
                else if (r < 0.8) s.lookUpT = t + d;
                s.arousal = Mathf.Min(1f, s.arousal + 0.4f);
                s.energy = Mathf.Min(1f, s.energy + 0.2f);
            }
        }
    }
}
