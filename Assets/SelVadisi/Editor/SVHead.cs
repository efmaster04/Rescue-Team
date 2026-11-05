using System;
using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public enum HeadArch { Man, Woman, OldMan, OldWoman, Boy, Girl, Worker }

    /// Shape morphs of the ring-loft head (spec 2) and every per-person face parameter (spec 4) after the sampler's
    /// jitter (spec 10). Feature heights are reference-head values (y = 0 is the eye line); the builder scales them by
    /// cranH above the eye line and by faceL below it, exactly like the rows. Field defaults are the male values.
    public class HeadGenome
    {
        // shape morphs
        public float cranH = 1f, faceL = 1f, headW = 1f, jawW = 1f, chinW = 1f, chinProj, chinSq, cheek, brow, foreSlope, occ, jowl;
        // eyes: centre (ex, ey), half-width, lid heights / exponents / skews, tilt, gaze offset, iris and pupil, highlights, lashes, crease
        public float ex = 0.0335f, ey, eyeA = 0.0185f, Uc = 0.0072f, Lc = 0.0060f, pu = 0.62f, pl = 0.85f, skU = 0.16f, skL = 0.12f,
            tilt = 0.0010f, gx, gy = 0.0002f, ri = 0.0078f, pupil = 0.48f, hlx = 0.36f, hly = 0.40f, hlr = 0.30f,
            lashT = 0.0021f, flick, flickUp, lowLine, crease;
        // brows: inner / outer x and y, arch, thickness inner -> outer
        public float browX0 = 0.011f, browX1 = 0.055f, browY0 = 0.0180f, browY1 = 0.0172f, browArch = 0.0022f, browT0 = 0.0074f, browT1 = 0.0028f;
        // nose: root / tip / base heights, half-widths, projections, hump, profile sharpness, wing size, tip redness
        public float noseYRoot = -0.003f, noseYTip = -0.027f, noseYBase = -0.033f, noseWRoot = 0.0050f, noseWTip = 0.0085f,
            nosePRoot = 0.0004f, nosePTip = 0.0170f, noseHump, noseSharp = 2f, noseWing = 0.0062f, noseRed = 0.18f;
        // mouth (smile, corners and the opening come from the expression)
        public float mouthY = -0.0520f, mouthW = 0.0200f, mouthTh = 0.0020f, lowLip = 0.0040f, upLip, teeth = 0.0026f;
        // ears
        public float earY = -0.010f, earDz = -0.012f, earH = 0.056f, earW = 0.031f, earFlare = 22f, earTilt = 12f, earThick = 0.007f;

        public HeadGenome Clone() => (HeadGenome)MemberwiseClone();
    }

    /// One person's head. SVHead.Sample fills it; callers may override fields before Build (e.g. hwMain = coat colour for a hood).
    public class HeadSpec
    {
        public HeadArch arch;
        public bool female;                     // also true for female workers
        public HeadGenome g = new HeadGenome();
        public Color32 skin, hair, iris;
        public int skinIndex;                   // index into the skin palette (uniqueness distance)
        public string hairStyle = "crop";       // crop buzz sidepart slick quiff curly balding long bob ponytail bun lowbun braid pigtails kidmessy none
        public bool partLeft;                   // sidepart: parting on the character's left (-x) instead of the right
        public bool curtainFringe;              // long: curtain fringe instead of the plain female hairline
        public string facial = "none";          // none classic chevron walrus fullbeard longbeard goatee (thin also builds)
        public float stubble;                   // 0..0.5; only drawn when there is no beard
        public string headwear = "none";        // none scarf_chin scarf_nape hijab hood beanie flatcap baseball helmet
        public Color32 hwMain, hwAccent;
        public string scarfPattern = "plain";   // dots border plain
        public bool glasses;
        public Color32 glassesCol;
        public bool headlamp;                   // helmet lamp
        public string expr = "neutral";         // neutral worried shout smile softsmile determined tired
        public float blush;
        public bool elder;                      // lid crease, eye bags, wrinkles, lighter facial hair
        public int seed;                        // build-time randomness: curly bumps, kid tufts, scarf dots

        /// Chin-to-crown height at headScale 1.
        public float RefHeight => 0.124f * g.cranH + 0.1108f * g.faceL;
    }

    /// Where a built head sits, in root space.
    public class HeadInfo
    {
        public Vector3 origin; public float scale;          // head-local origin O (eye line, mid-sagittal) and headScale
        public Vector3 headBone, eyesBone, mouthBone;       // pivots: Head; Eyes (blink scales its local y); Mouth (open-mouth decal scales about it)
        public Vector3 mouth, earL, earR, cheekL, cheekR, forehead, crown, eyeCentre;   // IK hand targets
        public Vector3 neckTopCentre; public float neckTopRx, neckTopRz;               // top ring of the body's neck tube (spec 1)
        public int vertexStart, vertexEnd;                  // emitted vertex range in the MB (end exclusive)
    }

    /// Villager heads, head system v2 (ported from the Python prototype in headproto/): a smooth ring-loft skull, crisp face
    /// decals projected onto an analytic face surface, and hair / headwear shells with clean hairlines. Build writes skinned
    /// vertices into an MB; Sample draws a varied, appealing spec; Distance and SameLook keep neighbours different.
    public static class SVHead
    {
        const float TwoPi = Mathf.PI * 2f, D2R = Mathf.Deg2Rad;
        const int BudgetLimit = 2500;           // heads above this are rebuilt with the budget settings (spec 1)

        static readonly BoneWeight HeadBone = MB.Bone(SVSkel.Head);

        // ================================================================== palettes (spec 9)
        static readonly string[] Skins = { "F6D2BA", "EEC2A2", "E8B994", "DDAA84", "D09A72", "C08660", "A87252" };
        static readonly float[] SkinW = { .10f, .22f, .20f, .18f, .15f, .10f, .05f };
        static readonly float[] SkinWChild = { .20f, .30f, .25f, .15f, .10f };
        static readonly string[] HairYoung = { "1F1814", "2E2119", "3E2B1F", "5A3D2A", "6E4128", "8A6A4A", "A88A5E" };
        static readonly float[] HairYoungW = { .16f, .24f, .22f, .16f, .10f, .08f, .04f };
        static readonly string[] HairOld = { "6F6C69", "8E8A85", "A9A59F", "C9C5BE", "DEDBD5" };
        static readonly string[] Henna = { "7A3420", "8E3B22", "6A2E22" };
        static readonly string[] Irises = { "2E1C12", "4A2E1C", "5C3E24", "6B5530", "4F6A48", "56708A" };
        static readonly float[] IrisW = { .34f, .30f, .16f, .10f, .05f, .05f };
        static readonly string[] ScarfMain = { "C24A6A", "4F6FB0", "E8E2D4", "7A4F9E", "D9A441", "3E8A6A", "A23B3B", "2E3A59", "F2EEE6", "8C6A4E" };
        static readonly string[] ScarfAccent = { "F2E6D8", "E8D27A", "6B8E5A", "F2E6D8", "8E3B22", "F2E6D8", "E8D27A", "D8D2C4", "C24A6A", "E8E2D4" };
        static readonly string[] CapCols = { "4A4038", "5A544C", "3E4448", "2A2B2E", "6B6458" };
        static readonly string[] BeanieMain = { "C8463B", "2F6DB5", "E3B93A", "3E9E57", "7C57B8", "2E3A59" };
        static readonly string[] BeanieCuff = { "E9E6DF", "E9E6DF", "2E3A59", "F2EEE6", "F2C230", "C8463B" };
        static readonly string[] HoodCols = { "F2C230", "D9473B", "2F6DB5", "3E9E57", "2B3550", "6B7A3E" };   // raincoats; the assembler may set the coat colour
        static readonly string[] Baseballs = { "2E3A59", "C8372D", "1F2226", "E9E6DF", "3E8A5A" };
        static readonly string[] GlassesCols = { "3A2A20", "1E1E20", "B89A5A" };

        // shape morph ranges per kind (spec 2), in the order jawW, chinW, chinProj, chinSq, cheek, headW, faceL, cranH, brow
        static readonly float[] MaleLo = { .97f, .95f, -.001f, 0f, 0f, .94f, .94f, .96f, 0f }, MaleHi = { 1.12f, 1.15f, .006f, .6f, .8f, 1.07f, 1.08f, 1.04f, .0035f };
        static readonly float[] FemaleLo = { .90f, .86f, -.002f, 0f, .2f, .93f, .93f, .97f, 0f }, FemaleHi = { 1.02f, 1.00f, .003f, 0f, 1f, 1.03f, 1.04f, 1.04f, 0f };
        static readonly float[] ChildLo = { .93f, .90f, 0f, 0f, .75f, .98f, .85f, 1.05f, 0f }, ChildHi = { .98f, .96f, 0f, 0f, 1f, 1.03f, .90f, 1.09f, 0f };

        // ================================================================== curves (degrees, mirrored; spec 6, 7, 8)
        static readonly AngCurve HlMale = new AngCurve(0, .064f, 25, .062f, 38, .057f, 48, .046f, 58, .030f, 66, .012f, 72, -.012f, 82, -.014f,
            89, .010f, 96, .027f, 112, .027f, 121, .008f, 132, -.022f, 147, -.042f, 165, -.052f, 180, -.054f);
        static readonly AngCurve HlFemale = new AngCurve(0, .062f, 25, .060f, 40, .054f, 52, .040f, 62, .022f, 70, 0f, 80, -.006f,
            89, .012f, 96, .026f, 112, .026f, 121, .006f, 132, -.026f, 150, -.048f, 180, -.056f);
        static readonly AngCurve HlRecede = new AngCurve(0, .076f, 18, .076f, 30, .080f, 40, .066f, 50, .046f, 60, .028f, 68, .008f, 74, -.008f, 82, -.010f,
            89, .010f, 96, .027f, 112, .027f, 121, .008f, 132, -.022f, 147, -.042f, 180, -.052f);
        static readonly AngCurve HlKid = new AngCurve(0, .058f, 25, .056f, 40, .050f, 52, .038f, 62, .020f, 70, .002f, 80, 0f,
            89, .014f, 96, .026f, 112, .026f, 121, .006f, 132, -.024f, 150, -.044f, 180, -.050f);
        static readonly AngCurve BaldLine = new AngCurve(0, .06f, 58, .024f, 66, .006f, 72, -.002f, 82, -.004f, 89, .012f, 96, .026f, 112, .026f,
            121, .008f, 132, -.022f, 147, -.042f, 180, -.052f);
        static readonly AngCurve BaldTop = new AngCurve(0, 0f, 55, 0f, 64, .022f, 90, .030f, 130, .024f, 180, .018f);
        static readonly AngCurve LongEnd = new AngCurve(64, -.135f, 90, -.165f, 125, -.235f, 180, -.270f);
        static readonly AngCurve BobEnd = new AngCurve(58, -.070f, 90, -.084f, 180, -.078f);
        static readonly AngCurve BeardTop = new AngCurve(0, -.059f, 9, -.059f, 14, -.050f, 20, -.043f, 32, -.031f, 48, -.020f, 64, -.008f, 80, .002f, 98, .010f);
        static readonly AngCurve BeardBot = new AngCurve(0, -.1045f, 30, -.101f, 55, -.088f, 72, -.068f, 86, -.040f, 98, -.012f);
        static readonly AngCurve GoateeTop = new AngCurve(0, -.059f, 8, -.059f, 17, -.056f);
        static readonly AngCurve GoateeBot = new AngCurve(0, -.1045f, 8, -.102f, 13, -.096f, 17, -.086f);

        // ================================================================== sampling (spec 2, 4, 9, 10)

        /// Draws one head for an archetype: genome ranges, feature jitter, palettes, hair / facial hair / headwear tables and the
        /// appeal guardrails. 'female' only matters for workers; the other archetypes imply it. Deterministic for a given r.
        public static HeadSpec Sample(HeadArch arch, bool female, System.Random r)
        {
            bool child = arch == HeadArch.Boy || arch == HeadArch.Girl;
            bool old = arch == HeadArch.OldMan || arch == HeadArch.OldWoman;
            bool fem = arch == HeadArch.Woman || arch == HeadArch.OldWoman || arch == HeadArch.Girl || (arch == HeadArch.Worker && female);
            var s = new HeadSpec { arch = arch, female = fem, elder = old };
            Kind kind = KindOf(s);
            HeadGenome g = s.g;

            // colours: weathered elder men, +-2 % per skin channel
            s.skinIndex = PickIdx(r, child ? SkinWChild : SkinW);
            Color skin = Hx(Skins[s.skinIndex]);
            if (arch == HeadArch.OldMan) skin = Lc(skin, Hx("D98A7A"), .08f);
            s.skin = new Color(skin.r * U(r, .98f, 1.02f), skin.g * U(r, .98f, 1.02f), skin.b * U(r, .98f, 1.02f), 1f);
            s.iris = Hx(Irises[PickIdx(r, IrisW)]);
            string hairHex;
            if (old)
            {
                hairHex = HairOld[r.Next(HairOld.Length)];
                if (arch == HeadArch.OldWoman && r.NextDouble() < .35) hairHex = Henna[r.Next(Henna.Length)];
            }
            else if (child) hairHex = HairYoung[1 + r.Next(5)];
            else
            {
                hairHex = HairYoung[PickIdx(r, HairYoungW)];
                if (arch == HeadArch.Woman && r.NextDouble() < .15) hairHex = Henna[r.Next(Henna.Length)];
            }
            s.hair = Hx(hairHex);

            // shape, then the elder changes
            SampleShape(g, kind, r);
            if (old) { g.jowl = U(r, .001f, .003f); g.cheek -= .2f; g.faceL += .02f; }

            // features: archetype defaults plus per-person jitter
            FaceDefaults(g, kind);
            float sz = U(r, .90f, 1.10f);           // eye-size factor, never below 0.9 (guardrail 6 for elders)
            g.eyeA *= sz; g.ri *= sz * U(r, .97f, 1.03f); g.Uc *= U(r, .92f, 1.06f); g.ex *= U(r, .97f, 1.03f); g.tilt += U(r, -.0006f, .0006f);
            g.browT0 *= U(r, .85f, 1.2f); g.browArch *= U(r, .7f, 1.3f); g.browY0 += U(r, -.0012f, .0012f); g.browY1 = g.browY0 + U(r, -.0015f, .0008f);
            g.nosePTip *= U(r, .82f, 1.22f) * (old ? 1.08f : 1f); g.noseWTip *= U(r, .9f, 1.12f); g.noseWing *= U(r, .92f, 1.1f);
            if (kind == Kind.Male && r.NextDouble() < .25) g.noseHump = U(r, .0015f, .003f);
            g.mouthW *= U(r, .88f, 1.12f); g.lowLip *= U(r, .75f, 1.3f);
            if (old) { g.Uc *= .88f; g.crease = .0012f; g.earH *= 1.1f; g.lowLip *= .75f; }
            s.blush = (kind == Kind.Male ? .16f : kind == Kind.Female ? .30f : .42f) * U(r, .8f, 1.25f);

            // hair style, facial hair, headwear (spec 10)
            string hw = "none";
            switch (arch)
            {
                case HeadArch.Man:
                    s.hairStyle = Pick(r, new[] { "crop", "sidepart", "slick", "quiff", "curly", "buzz" }, .28f, .24f, .14f, .12f, .10f, .12f);
                    s.facial = Pick(r, new[] { "none", "classic", "walrus", "chevron", "fullbeard", "goatee" }, .30f, .27f, .08f, .15f, .12f, .08f);
                    s.stubble = U(r, .15f, .45f);
                    hw = Pick(r, new[] { "none", "flatcap", "beanie", "baseball", "hood" }, .50f, .12f, .12f, .12f, .14f);
                    break;
                case HeadArch.OldMan:
                    s.hairStyle = Pick(r, new[] { "balding", "crop", "slick" }, .5f, .3f, .2f);
                    s.facial = Pick(r, new[] { "walrus", "classic", "none", "fullbeard", "longbeard" }, .40f, .30f, .15f, .075f, .075f);
                    s.stubble = U(r, .1f, .35f);
                    hw = Pick(r, new[] { "none", "flatcap", "beanie", "glasses" }, .30f, .40f, .10f, .20f);
                    break;
                case HeadArch.Woman:
                    s.hairStyle = Pick(r, new[] { "long", "bob", "ponytail", "bun", "braid" }, .25f, .15f, .25f, .2f, .15f);
                    hw = Pick(r, new[] { "none", "scarf_chin", "scarf_nape", "hood" }, .40f, .25f, .20f, .15f);
                    break;
                case HeadArch.OldWoman:
                    s.hairStyle = "lowbun";
                    hw = Pick(r, new[] { "scarf_chin", "scarf_nape", "none" }, .55f, .35f, .10f);
                    break;
                case HeadArch.Boy:
                    s.hairStyle = Pick(r, new[] { "kidmessy", "crop", "sidepart", "curly" }, .4f, .25f, .2f, .15f);
                    hw = Pick(r, new[] { "none", "beanie", "hood", "baseball" }, .5f, .2f, .2f, .1f);
                    break;
                case HeadArch.Girl:
                    s.hairStyle = Pick(r, new[] { "pigtails", "ponytail", "bob", "braid", "long" }, .25f, .2f, .25f, .15f, .15f);
                    hw = Pick(r, new[] { "none", "beanie", "hood" }, .6f, .2f, .2f);
                    break;
                default:   // worker: ponytail under the helmet for women
                    s.hairStyle = fem ? "ponytail" : "buzz";
                    hw = "helmet";
                    if (!fem)
                    {
                        s.facial = Pick(r, new[] { "none", "classic", "chevron", "fullbeard" }, .45f, .25f, .15f, .15f);
                        s.stubble = U(r, .2f, .5f);
                    }
                    break;
            }
            int sc = r.Next(ScarfMain.Length), bn = r.Next(BeanieMain.Length);
            s.scarfPattern = Pick(r, new[] { "dots", "border", "plain" }, .55f, .10f, .35f);
            string cap = CapCols[r.Next(CapCols.Length)], hood = HoodCols[r.Next(HoodCols.Length)], bb = Baseballs[r.Next(Baseballs.Length)];
            string helmet = r.NextDouble() < .6 ? "F2F0EA" : "F2C230";
            s.glassesCol = Hx(GlassesCols[r.Next(GlassesCols.Length)]);
            if (hw == "glasses") { hw = "none"; s.glasses = true; }
            s.headwear = hw;
            switch (hw)
            {
                case "scarf_chin": case "scarf_nape": SetHw(s, ScarfMain[sc], ScarfAccent[sc]); break;
                case "hood": SetHw(s, hood, hood); break;
                case "beanie": SetHw(s, BeanieMain[bn], BeanieCuff[bn]); break;
                case "flatcap": SetHw(s, cap, cap); break;
                case "baseball": SetHw(s, bb, bb); break;
                case "helmet": SetHw(s, helmet, helmet); s.headlamp = true; break;
            }

            // expression; half of the elders look calm (guardrail 6), children never look determined (guardrail 7)
            if (arch == HeadArch.Worker) s.expr = Pick(r, new[] { "determined", "neutral", "shout" }, .50f, .35f, .15f);
            else if (child) s.expr = Pick(r, new[] { "smile", "softsmile", "worried", "shout" }, .30f, .25f, .30f, .15f);
            else if (old) s.expr = r.NextDouble() < .5 ? Pick(r, new[] { "neutral", "softsmile" }, .25f, .10f)
                                                       : Pick(r, new[] { "worried", "shout", "tired" }, .30f, .25f, .10f);
            else s.expr = Pick(r, new[] { "worried", "shout", "neutral", "softsmile", "tired" }, .30f, .25f, .25f, .10f, .10f);
            s.partLeft = r.NextDouble() < .5;
            s.curtainFringe = r.NextDouble() < .5;
            s.seed = r.Next(0, 1 << 30);

            // appeal guardrails 1-4 (5 is in SampleShape, 8 in the moustache builder)
            if (g.ex < 1.70f * g.eyeA || g.ex > 1.95f * g.eyeA) g.ex = 1.82f * g.eyeA;
            float gap = g.browY0 - (g.Uc + g.lashT);
            float shift = gap < .0065f ? .0065f - gap : gap > .0125f ? .0125f - gap : 0f;
            g.browY0 += shift; g.browY1 += shift;
            g.nosePTip = Mathf.Min(g.nosePTip, kind == Kind.Male ? .021f : kind == Kind.Female ? .016f : .012f);
            g.mouthW = Mathf.Min(g.mouthW, .68f * g.ex);
            return s;
        }

        static void SampleShape(HeadGenome g, Kind kind, System.Random r)
        {
            float[] lo = kind == Kind.Male ? MaleLo : kind == Kind.Female ? FemaleLo : ChildLo;
            float[] hi = kind == Kind.Male ? MaleHi : kind == Kind.Female ? FemaleHi : ChildHi;
            var v = new float[lo.Length];
            for (int i = 0; i < v.Length; i++) v[i] = U(r, lo[i], hi[i]);
            // guardrail 5: no wide jaw on a wide chin, and at most two morphs above their 85th percentile
            if (v[0] > 1.08f && v[1] > 1.10f) v[1] = 1.06f;
            float Pct(int i) => (v[i] - lo[i]) / (hi[i] - lo[i]);
            var high = new List<int>();
            for (int i = 0; i < v.Length; i++) if (hi[i] > lo[i] && Pct(i) > .85f) high.Add(i);
            high.Sort((a, b) => Pct(b).CompareTo(Pct(a)));
            for (int k = 2; k < high.Count; k++) v[high[k]] = lo[high[k]] + .85f * (hi[high[k]] - lo[high[k]]);
            g.jawW = v[0]; g.chinW = v[1]; g.chinProj = v[2]; g.chinSq = v[3]; g.cheek = v[4]; g.headW = v[5]; g.faceL = v[6]; g.cranH = v[7]; g.brow = v[8];
        }

        /// Archetype face parameters (person.py default_face): the field defaults are male; female and child override some.
        static void FaceDefaults(HeadGenome g, Kind k)
        {
            if (k == Kind.Female)
            {
                g.eyeA = .0192f; g.Uc = .0080f; g.Lc = .0062f; g.ri = .0080f; g.lashT = .0027f; g.flick = .0038f; g.flickUp = .0022f; g.tilt = .0013f; g.lowLine = .0005f;
                g.browY0 = .0210f; g.browY1 = .0212f; g.browArch = .0045f; g.browT0 = .0046f; g.browT1 = .0015f; g.browX1 = .054f;
                g.nosePTip = .0135f; g.noseWTip = .0072f; g.noseWing = .0052f; g.noseSharp = 2.1f;
                g.mouthY = -.0505f; g.mouthW = .0185f; g.upLip = .0034f; g.lowLip = .0048f; g.mouthTh = .0017f;
                g.earH = .052f;
            }
            else if (k == Kind.Child)
            {
                g.eyeA = .0190f; g.Uc = .0088f; g.Lc = .0074f; g.ri = .0090f; g.lashT = .0021f; g.hlr = .36f; g.ex = .033f;
                g.browY0 = .0230f; g.browY1 = .0225f; g.browT0 = .0040f; g.browT1 = .0015f; g.browArch = .0038f; g.browX0 = .012f; g.browX1 = .050f;
                g.noseYRoot = -.008f; g.noseYTip = -.024f; g.noseYBase = -.029f; g.nosePTip = .0100f; g.noseWTip = .0070f; g.noseWRoot = .004f;
                g.nosePRoot = .0005f; g.noseWing = .0052f; g.noseSharp = 2.3f;
                g.mouthY = -.047f; g.mouthW = .0165f; g.lowLip = .0036f;
                g.earH = .050f; g.earW = .030f;
            }
        }

        static void SetHw(HeadSpec s, string main, string accent) { s.hwMain = Hx(main); s.hwAccent = Hx(accent); }

        // ================================================================== uniqueness (spec 10)

        /// Weighted look distance between two heads. Covers every head term of the spec table; the clothing-colour term
        /// (weight 2) belongs to the body and must be added by the caller. Glasses count as part of the headwear type.
        public static float Distance(HeadSpec a, HeadSpec b)
        {
            float d = 0f;
            if (a.headwear != b.headwear || a.glasses != b.glasses) d += 4f;
            if (a.hairStyle != b.hairStyle) d += 3f;
            if (a.headwear != "none" && b.headwear != "none") d += 2f * ColDist(a.hwMain, b.hwMain);
            d += 2f * ColDist(a.hair, b.hair);
            if (a.facial != b.facial) d += 2f;
            d += Mathf.Abs(a.skinIndex - b.skinIndex) / (float)(Skins.Length - 1);
            d += ShapeDist(a.g, b.g);
            if (a.expr != b.expr) d += .5f;
            return d;
        }

        /// Hard rule tuple: same headwear (and glasses), headwear colour, hair style, hair colour and facial hair.
        public static bool SameLook(HeadSpec a, HeadSpec b)
            => a.headwear == b.headwear && a.glasses == b.glasses && (a.headwear == "none" || ColEq(a.hwMain, b.hwMain))
            && a.hairStyle == b.hairStyle && ColEq(a.hair, b.hair) && a.facial == b.facial;

        static float ColDist(Color32 a, Color32 b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db) / 255f;
        }

        static bool ColEq(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        /// L2 distance of the shape vectors, each morph divided by its spread over all archetypes.
        static float ShapeDist(HeadGenome a, HeadGenome b)
        {
            float Term(float x, float y, float spread) => Sq((x - y) / spread);
            return Mathf.Sqrt(Term(a.cranH, b.cranH, .13f) + Term(a.faceL, b.faceL, .25f) + Term(a.headW, b.headW, .14f) + Term(a.jawW, b.jawW, .22f)
                + Term(a.chinW, b.chinW, .29f) + Term(a.chinProj, b.chinProj, .008f) + Term(a.chinSq, b.chinSq, .6f) + Term(a.cheek, b.cheek, 1.2f)
                + Term(a.brow, b.brow, .0035f) + Term(a.jowl, b.jowl, .003f));
        }

        // ================================================================== building

        /// Builds the head into mb (mb.W must be set) in root space: O.y = crownY - 0.124*cranH*headScale,
        /// O.z = neckCentreZ + 0.020*headScale, every vertex = O + headScale * (head-local point).
        public static HeadInfo Build(MB mb, HeadSpec s, float crownY, float neckCentreZ, float headScale)
        {
            if (mb.W == null) throw new ArgumentException("SVHead.Build needs a skinned MB (mb.W != null).", nameof(mb));
            var hb = new HeadBuilder(s, false);
            if (hb.Count > BudgetLimit) hb = new HeadBuilder(s, true);
            var info = new HeadInfo
            {
                origin = new Vector3(0f, crownY - 0.124f * s.g.cranH * headScale, neckCentreZ + 0.020f * headScale),
                scale = headScale,
                vertexStart = mb.Count
            };
            hb.CopyTo(mb, info.origin, headScale);
            info.vertexEnd = mb.Count;
            hb.Landmarks(info);
            return info;
        }

        // ================================================================== small helpers
        enum Kind { Male, Female, Child }

        static Kind KindOf(HeadSpec s) => s.arch == HeadArch.Boy || s.arch == HeadArch.Girl ? Kind.Child : s.female ? Kind.Female : Kind.Male;

        static float U(System.Random r, float a, float b) => a + (b - a) * (float)r.NextDouble();

        static int PickIdx(System.Random r, float[] w)
        {
            float sum = 0f;
            foreach (float x in w) sum += x;
            double t = r.NextDouble() * sum;
            for (int i = 0; i < w.Length; i++) { t -= w[i]; if (t < 0) return i; }
            return w.Length - 1;
        }

        static string Pick(System.Random r, string[] items, params float[] w) => items[PickIdx(r, w)];

        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }   // also for a > b
        static double SSd(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
        static double Bump(double y, double c, double w) { double d = (y - c) / w; return Math.Exp(-d * d); }
        static float Pw(float x, float e) => x <= 0f ? 0f : Mathf.Pow(x, e);       // power of a base that rounding may push below 0
        static float Sq(float x) => x * x;
        static float Wrap180(float d) => d - 360f * Mathf.Floor((d + 180f) / 360f);
        static float AbsDeg(float phi) => Mathf.Abs(Wrap180(phi * Mathf.Rad2Deg));
        static float Gauss(float phi, float cDeg, float wDeg) { float d = Wrap180(phi * Mathf.Rad2Deg - cDeg) / wDeg; return Mathf.Exp(-d * d); }
        static float Phi(int j, int n) => TwoPi * j / n;
        /// Alternating stripes 'deg' wide (exact column angles are nudged so float rounding cannot flip a stripe).
        static bool Stripe(float phi, float deg) => (int)(phi * Mathf.Rad2Deg / deg + 1e-3f) % 2 == 1;
        static Vector3 Unit(Vector3 v) { float m = v.magnitude; return m > 1e-12f ? v / m : Vector3.zero; }   // Unity's normalize zeroes vectors < 1e-5
        static Vector3 NeckRing(float y, float phi, float rx, float rz, float zc) => new Vector3(rx * Mathf.Sin(phi), y, zc + rz * Mathf.Cos(phi));
        static bool Near(List<Vector3> pts, Vector3 q, float d) { foreach (var o in pts) if ((q - o).magnitude < d) return true; return false; }
        static float[] Same(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }
        static Color[,] Same(int r, int k, Color c) { var a = new Color[r, k]; for (int i = 0; i < r; i++) for (int j = 0; j < k; j++) a[i, j] = c; return a; }
        static Vector3[] Add(Vector3[] a, Vector3 d) { var o = new Vector3[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = a[i] + d; return o; }
        static Vector3[] Row(Vector3[,] P, int i) { var o = new Vector3[P.GetLength(1)]; for (int k = 0; k < o.Length; k++) o[k] = P[i, k]; return o; }

        /// Cap edge line (spec 8): front, side and back heights.
        static AngCurve EdgeLine(float f, float s, float b) => new AngCurve(0, f, 40, f - .35f * (f - s), 90, s, 140, s + .7f * (b - s), 180, b);

        static BoneWeight HeadChest(float headW)
            => headW >= .5f ? MB.Bones(SVSkel.Head, SVSkel.Chest, 1f - headW) : MB.Bones(SVSkel.Chest, SVSkel.Head, headW);

        // colours are worked in float (0..1) like the prototype and clamped once, when written to the MB
        static Color Hx(string h) => new Color(Convert.ToInt32(h.Substring(0, 2), 16) / 255f, Convert.ToInt32(h.Substring(2, 2), 16) / 255f,
                                               Convert.ToInt32(h.Substring(4, 2), 16) / 255f, 1f);
        static Color Sh(Color c, float f) => new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), 1f);
        static Color Mul(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);
        static Color Mul(Color c, float r, float g, float b) => new Color(c.r * r, c.g * g, c.b * b, 1f);
        static Color Lc(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f); }

        struct Expr { public float bIn, bOut, bArch, bThick, uc, lc, mW, smile, corner, asym, mouthDy, openTop, openBot; public bool open; }

        /// Expression deltas (spec 5): brow inner/outer/arch, brow thickness, lid factors, mouth width, smile, corners, opening.
        static Expr Expression(string name)
        {
            switch (name)
            {
                case "worried": return new Expr { bIn = .0045f, bOut = -.0015f, bArch = -.0015f, bThick = 1f, uc = 1.12f, lc = 1f, mW = .85f, smile = -.0004f, corner = -.0026f, asym = .0005f };
                case "shout": return new Expr { bIn = .0060f, bOut = .0030f, bArch = .0010f, bThick = 1f, uc = 1.28f, lc = 1.06f, mW = .95f, mouthDy = -.003f, open = true, openTop = .0085f, openBot = .0150f };
                case "smile": return new Expr { bIn = .0018f, bOut = .0018f, bArch = .0010f, bThick = 1f, uc = .92f, lc = .62f, mW = 1.08f, smile = .0042f, corner = .0010f, open = true, openTop = .0016f, openBot = .0085f };
                case "softsmile": return new Expr { bIn = .0010f, bOut = .0012f, bArch = .0005f, bThick = 1f, uc = .96f, lc = .80f, mW = 1.05f, smile = .0030f, corner = .0012f };
                case "determined": return new Expr { bIn = -.0035f, bOut = .0008f, bArch = -.0012f, bThick = 1.15f, uc = .84f, lc = .95f, mW = .95f, corner = -.0010f };
                case "tired": return new Expr { bIn = .0020f, bOut = -.0010f, bArch = -.0010f, bThick = 1f, uc = .72f, lc = 1f, mW = .95f, corner = -.0012f };
                default: return new Expr { bThick = 1f, uc = 1f, lc = 1f, mW = 1f, smile = .0012f };   // neutral
            }
        }

        // ------------------------------------------------------------------ icospheres (levels 0..2, outward winding)
        static readonly List<Vector3[]> icoV = new List<Vector3[]>();
        static readonly List<int[]> icoT = new List<int[]>();

        static void IcoMesh(int sub, out Vector3[] v, out int[] t)
        {
            if (icoV.Count == 0)
            {
                float g = (1f + Mathf.Sqrt(5f)) / 2f;
                var vs = new List<Vector3>
                {
                    new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                    new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                    new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1)
                };
                for (int i = 0; i < vs.Count; i++) vs[i] = vs[i].normalized;
                var fs = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
                };
                for (int lvl = 0; lvl <= 2; lvl++)
                {
                    if (lvl > 0)
                    {
                        var v2 = new List<Vector3>(vs);
                        var f2 = new List<int>();
                        var mid = new Dictionary<long, int>();
                        int Mid(int a, int b)
                        {
                            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                            if (mid.TryGetValue(key, out int m)) return m;
                            v2.Add(((v2[a] + v2[b]) * 0.5f).normalized);
                            return mid[key] = v2.Count - 1;
                        }
                        for (int i = 0; i < fs.Count; i += 3)
                        {
                            int a = fs[i], b = fs[i + 1], c = fs[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                            f2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                        }
                        vs = v2; fs = f2;
                    }
                    for (int i = 0; i < fs.Count; i += 3)
                    {
                        Vector3 a = vs[fs[i]], b = vs[fs[i + 1]], c = vs[fs[i + 2]];
                        if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { int tmp = fs[i + 1]; fs[i + 1] = fs[i + 2]; fs[i + 2] = tmp; }
                    }
                    icoV.Add(vs.ToArray()); icoT.Add(fs.ToArray());
                }
            }
            v = icoV[sub]; t = icoT[sub];
        }

        // ------------------------------------------------------------------ angle curves
        /// Catmull-Rom curve over the loft angle in degrees, mirrored about the front midline; each value is clamped to its
        /// segment's control range +-4 mm so it never overshoots (hairlines, beard lines, cap edges, drape ends).
        sealed class AngCurve
        {
            readonly float[] xs, ys;

            public AngCurve(params float[] xy)
            {
                xs = new float[xy.Length / 2]; ys = new float[xs.Length];
                for (int i = 0; i < xs.Length; i++) { xs[i] = xy[2 * i]; ys[i] = xy[2 * i + 1]; }
            }

            /// This curve's points from fromDeg on, preceded by other control points (bob and curtain fringes).
            public AngCurve After(float fromDeg, params float[] head)
            {
                var xy = new List<float>(head);
                for (int i = 0; i < xs.Length; i++) if (xs[i] >= fromDeg) { xy.Add(xs[i]); xy.Add(ys[i]); }
                return new AngCurve(xy.ToArray());
            }

            public float At(float phi)
            {
                float d = AbsDeg(phi);
                int n = xs.Length;
                if (d <= xs[0]) return ys[0];
                if (d >= xs[n - 1]) return ys[n - 1];
                int i = 0;
                while (i < n - 2 && xs[i + 1] < d) i++;
                float t = (d - xs[i]) / (xs[i + 1] - xs[i]);
                float p0 = ys[Mathf.Max(i - 1, 0)], p1 = ys[i], p2 = ys[i + 1], p3 = ys[Mathf.Min(i + 2, n - 1)];
                float v = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                return Mathf.Clamp(v, Mathf.Min(p1, p2) - 0.004f, Mathf.Max(p1, p2) + 0.004f);
            }
        }

        // ------------------------------------------------------------------ head surface (spec 2)
        /// Ring-loft head: each row is a front superellipse (W, F, nF) and a back one (W, B, nB) centred at z = cz. The morphed
        /// rows are densified with a uniform Catmull-Rom (601 samples) and evaluated in double precision.
        sealed class Surface
        {
            // y, cz, W, F, B, nF, nB of the adult reference head; y = 0 is the eye line
            static readonly double[,] Rows =
            {
                { 0.1240, -0.020, 0.0000, 0.000, 0.000, 2.00, 2.0 },   // crown pole
                { 0.1212, -0.020, 0.0270, 0.026, 0.029, 2.00, 2.0 },
                { 0.1148, -0.019, 0.0480, 0.047, 0.052, 2.00, 2.0 },
                { 0.1038, -0.018, 0.0640, 0.066, 0.072, 2.10, 2.0 },
                { 0.0890, -0.017, 0.0760, 0.082, 0.087, 2.20, 2.1 },
                { 0.0705, -0.015, 0.0820, 0.094, 0.096, 2.30, 2.1 },   // upper forehead
                { 0.0502, -0.013, 0.0845, 0.099, 0.099, 2.40, 2.2 },   // widest skull
                { 0.0298, -0.012, 0.0835, 0.101, 0.097, 2.50, 2.2 },
                { 0.0130, -0.010, 0.0820, 0.100, 0.094, 2.60, 2.2 },   // brow
                { 0.0000, -0.010, 0.0800, 0.097, 0.091, 2.70, 2.2 },   // eye line
                { -0.0145, -0.008, 0.0795, 0.098, 0.087, 2.60, 2.2 },  // cheekbone
                { -0.0305, -0.006, 0.0765, 0.098, 0.082, 2.50, 2.2 },  // nose base
                { -0.0445, -0.006, 0.0730, 0.098, 0.074, 2.40, 2.2 },  // upper lip
                { -0.0570, -0.006, 0.0695, 0.095, 0.065, 2.30, 2.2 },  // mouth
                { -0.0695, -0.006, 0.0655, 0.090, 0.055, 2.25, 2.2 },  // lower lip
                { -0.0820, -0.005, 0.0595, 0.085, 0.044, 2.25, 2.2 },  // jaw angle
                { -0.0930, 0.004, 0.0505, 0.078, 0.036, 2.30, 2.2 },   // chin
                { -0.1020, 0.014, 0.0385, 0.065, 0.030, 2.30, 2.2 },   // chin bottom
                { -0.1080, 0.020, 0.0240, 0.047, 0.024, 2.10, 2.2 },   // under chin
                { -0.1108, 0.020, 0.0000, 0.000, 0.000, 2.00, 2.0 },   // bottom pole (inside the neck)
            };
            const int Dense = 600;
            readonly double[,] D = new double[Dense + 1, 7];
            readonly float[] rowY;                    // morphed ring heights
            public readonly float yTop, yBot;

            struct RingP { public double cz, W, F, B, nF, nB; }

            public Surface(HeadGenome g)
            {
                int m = Rows.GetLength(0);
                var R = new double[m, 7];
                rowY = new float[m];
                for (int r = 0; r < m; r++)
                {
                    double y = Rows[r, 0], W = Rows[r, 2];   // morphs are keyed on the reference y
                    double cb = Bump(y, -0.032, 0.022), chin = Bump(y, -0.095, 0.012);
                    double W2 = W * g.headW * (1 + (g.jawW - 1) * SSd(-0.026, -0.076, y)) * (1 + (g.chinW - 1) * SSd(-0.076, -0.100, y))
                              + 0.0045 * g.cheek * cb + g.jowl * Bump(y, -0.074, 0.016);
                    double F2 = Rows[r, 3] + g.chinProj * chin + 0.0020 * g.cheek * cb + g.brow * Bump(y, 0.016, 0.010) - g.foreSlope * SSd(0.03, 0.10, y);
                    double B2 = Rows[r, 4] + g.occ * Bump(y, 0.05, 0.045);
                    if (W == 0) W2 = F2 = B2 = 0;
                    R[r, 0] = y * (y > 0 ? g.cranH : g.faceL);
                    R[r, 1] = Rows[r, 1]; R[r, 2] = W2; R[r, 3] = F2; R[r, 4] = B2;
                    R[r, 5] = Rows[r, 5] + 0.25 * g.cheek * cb + g.chinSq * chin;
                    R[r, 6] = Rows[r, 6];
                    rowY[r] = (float)R[r, 0];
                }
                // uniform Catmull-Rom over the row index; W, F, B never below 0
                for (int k = 0; k <= Dense; k++)
                {
                    double t = (double)k / Dense * (m - 1);
                    int i = Math.Min((int)t, m - 2), i0 = Math.Max(i - 1, 0), i3 = Math.Min(i + 2, m - 1);
                    double f = t - i;
                    for (int c = 0; c < 7; c++)
                    {
                        double p0 = R[i0, c], p1 = R[i, c], p2 = R[i + 1, c], p3 = R[i3, c];
                        double v = 0.5 * (2 * p1 + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f + (-p0 + 3 * p1 - 3 * p2 + p3) * f * f * f);
                        D[k, c] = c >= 2 && c <= 4 ? Math.Max(v, 0) : v;
                    }
                }
                yTop = (float)D[0, 0]; yBot = (float)D[Dense, 0];
            }

            RingP At(double y)
            {
                int i; double t;
                if (y >= D[0, 0]) { i = 0; t = 0; }
                else if (y <= D[Dense, 0]) { i = Dense - 1; t = 1; }
                else
                {
                    int lo = 0, hi = Dense;                  // D[lo].y > y >= D[hi].y (the y column descends)
                    while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (D[mid, 0] > y) lo = mid; else hi = mid; }
                    i = lo; t = (y - D[i, 0]) / (D[i + 1, 0] - D[i, 0]);
                }
                double L(int c) => D[i, c] + (D[i + 1, c] - D[i, c]) * t;
                return new RingP { cz = L(1), W = L(2), F = L(3), B = L(4), nF = L(5), nB = L(6) };
            }

            void SD(double y, double phi, out double x, out double z)
            {
                RingP r = At(y);
                double s = Math.Sin(phi), c = Math.Cos(phi);
                double E = c >= 0 ? r.F : r.B, n = c >= 0 ? r.nF : r.nB;
                x = r.W * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n);
                z = r.cz + E * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n);
            }

            /// Surface point at height y and parametric angle phi (0 = front midline, +pi/2 = +x side).
            public Vector3 S(float y, float phi) { SD(y, phi, out double x, out double z); return new Vector3((float)x, y, (float)z); }

            /// Outward unit normal from central differences.
            public Vector3 N(float y, float phi)
            {
                const double da = 1e-3, dy = 1e-4;
                SD(y, phi + da, out double x1, out double z1); SD(y, phi - da, out double x0, out double z0);
                SD(y + dy, phi, out double x3, out double z3); SD(y - dy, phi, out double x2, out double z2);
                double ax = x1 - x0, az = z1 - z0, bx = x3 - x2, by = 2 * dy, bz = z3 - z2;   // a = dS/dphi (a.y = 0), b = dS/dy
                double nx = -az * by, ny = az * bx - ax * bz, nz = ax * by;                   // a x b
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-12) return new Vector3(0f, y > 0 ? 1f : -1f, 0f);
                SD(y, phi, out double px, out double pz);
                if (nx * px + ny * 0.7 * y + nz * (pz - At(y).cz) < 0) len = -len;           // point away from (0, 0.3y, cz)
                return new Vector3((float)(nx / len), (float)(ny / len), (float)(nz / len));
            }

            double ZF(double x, double y)
            {
                RingP r = At(y);
                if (r.W <= 1e-6) return r.cz;
                double u = Math.Min(Math.Abs(x) / r.W, 0.999);
                return r.cz + r.F * Math.Pow(1 - Math.Pow(u, r.nF), 1 / r.nF);
            }

            /// Front height field z(x, y) of the face.
            public float ZFace(float x, float y) => (float)ZF(x, y);

            public Vector3 FacePoint(float x, float y, float off) => FacePoint(x, y, off, out _);

            /// Point on the face height field at (x, y), pushed 'off' along the field's normal.
            public Vector3 FacePoint(float x, float y, float off, out Vector3 n)
            {
                const double h = 5e-4;
                double dx = (ZF(x + h, y) - ZF(x - h, y)) / (2 * h), dy = (ZF(x, y + h) - ZF(x, y - h)) / (2 * h);
                double l = Math.Sqrt(dx * dx + dy * dy + 1);
                n = new Vector3((float)(-dx / l), (float)(-dy / l), (float)(1 / l));
                return new Vector3(x, y, (float)ZF(x, y)) + n * off;
            }

            public float Cz(float y) => (float)At(y).cz;

            public void RingAt(float y, out float cz, out float W, out float B) { RingP r = At(y); cz = (float)r.cz; W = (float)r.W; B = (float)r.B; }

            /// Heights of the skin rings: the morphed rows between the poles.
            public float[] InnerRings() { var a = new float[rowY.Length - 2]; Array.Copy(rowY, 1, a, 0, a.Length); return a; }

            /// n ring heights spread evenly (in row index) over the same span, read from the dense table (budget skin).
            public float[] EvenRings(int n)
            {
                int m = rowY.Length;
                var a = new float[n];
                for (int i = 0; i < n; i++)
                {
                    double k = (1 + (m - 3) * (double)i / (n - 1)) / (m - 1) * Dense;
                    int k0 = Math.Min((int)k, Dense - 1);
                    a[i] = (float)(D[k0, 0] + (D[k0 + 1, 0] - D[k0, 0]) * (k - k0));
                }
                return a;
            }
        }

        // ------------------------------------------------------------------ head-local mesh buffer
        /// Mirrors the prototype's mesh builder in head-local space; CopyTo writes it into an MB at O + scale * p.
        sealed class Buf
        {
            public readonly List<Vector3> V = new List<Vector3>(3000);
            public readonly List<Color> C = new List<Color>(3000);
            readonly List<Vector2> ge = new List<Vector2>(3000);        // gloss, emission
            readonly List<float> wind = new List<float>(3000);
            readonly List<BoneWeight> bw = new List<BoneWeight>(3000);
            readonly List<int> T = new List<int>(9000);
            public float Gloss, Emit, Wind;
            public BoneWeight Weight = HeadBone;

            public int Count => V.Count;

            public void Reset() { Gloss = 0f; Emit = 0f; Wind = 0f; Weight = HeadBone; }

            public int Vert(Vector3 p, Color c)
            {
                V.Add(p); C.Add(c); ge.Add(new Vector2(Gloss, Emit)); wind.Add(Wind); bw.Add(Weight);
                return V.Count - 1;
            }

            public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }

            /// Triangle wound so its face normal points along o.
            public void TriOut(int a, int b, int c, Vector3 o)
            {
                if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), o) < 0f) Tri(a, c, b); else Tri(a, b, c);
            }

            /// One shared vertex per grid point; 'before' may set the vertex state (weight, wind, gloss) per point.
            public int[,] Verts(Vector3[,] P, Color[,] col, Action<int, int> before = null)
            {
                int R = P.GetLength(0), K = P.GetLength(1);
                var idx = new int[R, K];
                for (int i = 0; i < R; i++)
                    for (int j = 0; j < K; j++)
                    {
                        before?.Invoke(i, j);
                        idx[i, j] = Vert(P[i, j], col[i, j]);
                    }
                return idx;
            }

            /// Quads between grid rows (wrapping in columns if asked). The winding is global: flipped as a whole when the
            /// summed face normals point against outward(centroid).
            public void GridIdx(int[,] idx, bool wrap, Func<Vector3, Vector3> outward)
            {
                int R = idx.GetLength(0), K = idx.GetLength(1), kmax = wrap ? K : K - 1;
                double sum = 0;
                for (int i = 0; i < R - 1; i++)
                    for (int j = 0; j < kmax; j++)
                    {
                        Vector3 a = V[idx[i, j]], b = V[idx[i, (j + 1) % K]], c = V[idx[i + 1, (j + 1) % K]];
                        sum += Vector3.Dot(Vector3.Cross(b - a, c - a), outward((a + b + c) / 3f));
                    }
                bool flip = sum < 0;
                for (int i = 0; i < R - 1; i++)
                    for (int j = 0; j < kmax; j++)
                    {
                        int j1 = (j + 1) % K, a = idx[i, j], b = idx[i, j1], c = idx[i + 1, j1], d = idx[i + 1, j];
                        if (flip) { Tri(a, c, b); Tri(a, d, c); } else { Tri(a, b, c); Tri(a, c, d); }
                    }
            }

            public int[,] Grid(Vector3[,] P, Color[,] col, bool wrap, Func<Vector3, Vector3> outward)
            {
                int[,] idx = Verts(P, col);
                GridIdx(idx, wrap, outward);
                return idx;
            }

            /// Two-row grid with one colour per row.
            public void Strip(Vector3[] r0, Vector3[] r1, Color c0, Color c1, bool wrap, Func<Vector3, Vector3> outward)
            {
                int K = r0.Length;
                var P = new Vector3[2, K];
                var col = new Color[2, K];
                for (int k = 0; k < K; k++) { P[0, k] = r0[k]; P[1, k] = r1[k]; col[0, k] = c0; col[1, k] = c1; }
                Grid(P, col, wrap, outward);
            }

            /// Icosphere (level 'sub') scaled per axis; jitter scales each vertex radially by 1 +- jitter.
            public void Ico(Vector3 c, Vector3 scale, Color col, int sub, float jitter = 0f, System.Random jr = null, Func<Vector3, Color> colFn = null)
            {
                IcoMesh(sub, out Vector3[] vs, out int[] ts);
                int start = Count;
                foreach (Vector3 v in vs)
                {
                    float j = jitter > 0f && jr != null ? 1f + ((float)jr.NextDouble() * 2f - 1f) * jitter : 1f;
                    Vert(c + Vector3.Scale(v, scale) * j, colFn != null ? colFn(v) : col);
                }
                for (int i = 0; i < ts.Length; i += 3) Tri(start + ts[i], start + ts[i + 1], start + ts[i + 2]);
            }

            /// Variable-radius tube with shared ring vertices and a parallel-transported frame. flatten squashes the cross
            /// section along an axis by flatF; colFn(t, angle) colours per vertex; ring(index, t) runs before each ring
            /// (and before each cap) so the caller can set the weight or wind for it.
            public void Tube(IList<Vector3> pts, IList<float> radii, int seg, Color col, bool cap0, bool cap1, Vector3? flatten = null,
                             float flatF = 1f, Func<float, float, Color> colFn = null, Vector3? upHint = null, Action<int, float> ring = null)
            {
                int n = pts.Count;
                var rings = new int[n, seg];
                Vector3 prevU = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    Vector3 d = Unit(i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1]);
                    Vector3 u = i == 0 ? Unit(Vector3.Cross(d, upHint ?? (Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right)))
                                       : Unit(prevU - d * Vector3.Dot(prevU, d));
                    prevU = u;
                    Vector3 v = Vector3.Cross(d, u);
                    float t = i / (float)(n - 1);
                    ring?.Invoke(i, t);
                    for (int k = 0; k < seg; k++)
                    {
                        float a = TwoPi * k / seg;
                        Vector3 off = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radii[i];
                        if (flatten.HasValue) off -= flatten.Value * (Vector3.Dot(off, flatten.Value) * (1f - flatF));
                        rings[i, k] = Vert(pts[i] + off, colFn != null ? colFn(t, a) : col);
                    }
                }
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 axis = (pts[i] + pts[i + 1]) * 0.5f;
                    for (int k = 0; k < seg; k++)
                    {
                        int k1 = (k + 1) % seg, a0 = rings[i, k], a1 = rings[i, k1], b0 = rings[i + 1, k], b1 = rings[i + 1, k1];
                        Vector3 o = (V[a0] + V[a1] + V[b0] + V[b1]) * 0.25f - axis;
                        TriOut(a0, b0, b1, o); TriOut(a0, b1, a1, o);
                    }
                }
                void Lid(int i, Vector3 outDir)
                {
                    ring?.Invoke(i, i / (float)(n - 1));
                    int c = Vert(pts[i], C[rings[i, 0]]);
                    for (int k = 0; k < seg; k++) TriOut(c, rings[i, k], rings[i, (k + 1) % seg], outDir);
                }
                if (cap0) Lid(0, pts[0] - pts[1]);
                if (cap1) Lid(n - 1, pts[n - 1] - pts[n - 2]);
            }

            public void CopyTo(MB mb, Vector3 o, float scale)
            {
                float g0 = mb.Gloss, e0 = mb.Emit, w0 = mb.Wind;
                var fn = mb.WindFn;
                BoneWeight bw0 = mb.Weight;
                mb.WindFn = null;
                int start = mb.Count;
                for (int i = 0; i < V.Count; i++)
                {
                    mb.Gloss = ge[i].x; mb.Emit = ge[i].y; mb.Wind = wind[i]; mb.Weight = bw[i];
                    mb.Vert(o + V[i] * scale, C[i]);
                }
                for (int i = 0; i < T.Count; i += 3) mb.Tri(start + T[i], start + T[i + 1], start + T[i + 2]);
                mb.Gloss = g0; mb.Emit = e0; mb.Wind = w0; mb.WindFn = fn; mb.Weight = bw0;
            }
        }

        sealed class CapShell { public int[,] idx; public Vector3[,] P; public int NL, NS; }

        // ------------------------------------------------------------------ the builder
        /// Builds one head in head-local space (metres, O at the eye line on the mid-sagittal plane, +z = face).
        sealed class HeadBuilder
        {
            const float OpenLift = 0.0008f;                    // open-mouth layers sit 0.4 mm above the closed decal's top (1.3 mm) layer
            const float TalkTop = 0.0045f, TalkBot = 0.0080f;  // medium talking opening for expressions whose mouth is closed

            static readonly Func<Vector3, Vector3> DecalOut = q => new Vector3(q.x * 0.3f, 0f, 1f);
            static readonly float[] EyeU = { -1f, -0.88f, -0.66f, -0.4f, -0.13f, 0.13f, 0.4f, 0.66f, 0.88f, 1f };
            static readonly float[] NoseT = { 0f, .3f, .6f, .82f, .95f, 1f, 1.12f, 1.26f };
            static readonly float[] NoseU = { -1f, -.66f, -.33f, 0f, .33f, .66f, 1f };
            static readonly float[] ChinScarfRows = { .118f, .106f, .090f, .070f, .050f, .032f, .016f, 0f, -.016f, -.032f, -.048f, -.064f, -.080f, -.094f, -.106f };
            static readonly float[] NapeScarfRows = { .118f, .106f, .090f, .070f, .050f, .032f, .016f, 0f, -.016f, -.032f, -.046f, -.058f, -.068f, -.076f };
            static readonly float[] ScarfDrapeT = { .15f, .35f, .55f, .75f, 1f };
            static readonly float[] HoodRows = { .118f, .104f, .088f, .070f, .050f, .030f, .012f, -.006f, -.024f, -.042f, -.060f, -.078f, -.094f, -.110f, -.130f, -.150f };
            const int HoodCollar = 3;                          // the last three hood rows are the collar

            readonly HeadSpec spec;
            readonly HeadGenome f;              // feature parameters with the expression applied and heights scaled to this head
            readonly Surface hs;
            readonly Buf buf = new Buf();
            readonly System.Random rng;
            readonly Kind kind;
            readonly Expr ex;
            readonly bool budget, hairShown;
            readonly Color skin, hair, iris;
            readonly float wNeutral;            // mouth half-width before the expression; moustaches keep it (spec 5)
            Vector3 mouthPivot;

            public int Count => buf.Count;

            public HeadBuilder(HeadSpec s, bool budget)
            {
                spec = s;
                this.budget = budget;
                kind = KindOf(s);
                hs = new Surface(s.g);
                ex = Expression(s.expr);
                skin = s.skin; hair = s.hair; iris = s.iris;
                rng = new System.Random(s.seed);
                hairShown = s.headwear == "none" || s.headwear == "flatcap" || s.headwear == "baseball";
                // expression deltas are reference-head values (expr.py); then every feature height is scaled like the rows
                f = s.g.Clone();
                wNeutral = f.mouthW;
                f.browY0 += ex.bIn; f.browY1 += ex.bOut; f.browArch = Mathf.Max(0f, f.browArch + ex.bArch);
                f.browT0 *= ex.bThick; f.browT1 *= ex.bThick;
                f.Uc *= ex.uc; f.Lc *= ex.lc;
                f.mouthW *= ex.mW; f.mouthY += ex.mouthDy;
                f.ey = Sy(f.ey); f.browY0 = Sy(f.browY0); f.browY1 = Sy(f.browY1);
                f.noseYRoot = Sy(f.noseYRoot); f.noseYTip = Sy(f.noseYTip); f.noseYBase = Sy(f.noseYBase);
                f.mouthY = Sy(f.mouthY); f.earY = Sy(f.earY);
                Run();
            }

            /// Reference height -> this head (cranH above the eye line, faceL below).
            float Sy(float y) => y * (y > 0f ? spec.g.cranH : spec.g.faceL);

            void Run()
            {
                string hw = spec.headwear, st = spec.hairStyle;
                bool covered = hw == "scarf_chin" || hw == "scarf_nape" || hw == "hijab" || hw == "hood";
                bool ears = !covered && !(hairShown && (st == "long" || st == "bob"));
                Skin();
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Eye(sd);
                    Brow(sd);
                    if (ears) Ear(sd);
                }
                Nose();
                MouthClosed();
                MouthOpen();
                if (spec.elder) Wrinkles();
                if (hairShown) Hair();
                FacialHair();
                switch (hw)
                {
                    case "scarf_chin": Headscarf(true, false); break;
                    case "hijab": Headscarf(true, true); break;
                    case "scarf_nape": Headscarf(false, false); break;
                    case "hood": Hood(); break;
                    case "beanie": Beanie(); break;
                    case "flatcap": Flatcap(); break;
                    case "baseball": Baseball(); break;
                    case "helmet": Helmet(); break;
                }
                if (spec.glasses) Glasses();
            }

            public void CopyTo(MB mb, Vector3 o, float scale) => buf.CopyTo(mb, o, scale);

            public void Landmarks(HeadInfo info)
            {
                Vector3 o = info.origin;
                float sc = info.scale;
                Vector3 R(Vector3 p) => o + p * sc;
                float eyeMid = f.ey + 0.5f * (f.Uc - f.Lc);    // vertical centre of the eye opening (u = 0): blinks collapse onto it
                info.headBone = R(new Vector3(0f, -0.065f, -0.020f));
                info.eyesBone = R(new Vector3(0f, eyeMid, hs.ZFace(f.ex, eyeMid)));
                info.mouthBone = R(mouthPivot);
                info.mouth = R(hs.FacePoint(0f, f.mouthY, 0f));
                info.earL = R(EarCentre(-1));
                info.earR = R(EarCentre(1));
                info.cheekL = R(hs.FacePoint(-0.043f, -0.024f, 0f));    // blush centres
                info.cheekR = R(hs.FacePoint(0.043f, -0.024f, 0f));
                info.forehead = R(hs.FacePoint(0f, Sy(0.045f), 0f));
                info.crown = R(hs.S(hs.yTop, 0f));
                info.eyeCentre = R(hs.FacePoint(0f, eyeMid, 0f));
                info.neckTopCentre = R(new Vector3(0f, -0.035f, -0.020f));
                info.neckTopRx = (kind == Kind.Male ? 0.046f : kind == Kind.Female ? 0.040f : 0.036f) * sc;
                info.neckTopRz = (kind == Kind.Male ? 0.044f : kind == Kind.Female ? 0.038f : 0.034f) * sc;
            }

            // ============================================================== skin (spec 2, 3)
            void Skin()
            {
                buf.Reset();
                int nl = budget ? 20 : 24;
                float[] ringY = budget ? hs.EvenRings(16) : hs.InnerRings();
                string fh = spec.facial;
                float stub = fh == "none" || fh == "classic" || fh == "walrus" || fh == "chevron" || fh == "thin" ? spec.stubble : 0f;
                Color stubCol = Mul(Lc(skin, hair, 0.45f), 0.92f, 0.95f, 1.02f);
                bool scalp = spec.hairStyle == "balding" && hairShown;
                var P = new Vector3[ringY.Length, nl];
                var C = new Color[ringY.Length, nl];
                for (int i = 0; i < ringY.Length; i++)
                    for (int j = 0; j < nl; j++)
                    {
                        P[i, j] = hs.S(ringY[i], Phi(j, nl));
                        C[i, j] = Paint(P[i, j], stub, stubCol);
                    }
                int[,] idx = buf.Verts(P, C, (i, j) => buf.Gloss = SkinGloss(P[i, j].y, scalp));
                buf.GridIdx(idx, true, q => q - new Vector3(0f, 0f, -0.01f));
                Vector3 top = hs.S(hs.yTop, 0f), bot = hs.S(hs.yBot, 0f);
                buf.Gloss = SkinGloss(top.y, scalp);
                int it = buf.Vert(top, Paint(top, stub, stubCol));
                buf.Gloss = 0.10f;
                int ib = buf.Vert(bot, Paint(bot, stub, stubCol));
                int last = ringY.Length - 1;
                for (int j = 0; j < nl; j++)
                {
                    int j1 = (j + 1) % nl;
                    buf.TriOut(it, idx[0, j], idx[0, j1], Vector3.up);
                    buf.TriOut(ib, idx[last, j], idx[last, j1], Vector3.down);
                }
            }

            /// Skin gloss 0.10; a bald scalp shines (0.45) above the forehead.
            float SkinGloss(float y, bool scalp) => scalp ? 0.10f + 0.35f * SS(Sy(0.040f), Sy(0.075f), y) : 0.10f;

            /// Skin vertex paint (spec 3): blush, eye sockets, elder eye bags, stubble, under-chin shade.
            Color Paint(Vector3 p, float stub, Color stubCol)
            {
                float x = p.x, y = p.y, z = p.z;
                Color c = skin;
                float front = SS(0f, 0.05f, z);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float bx = (x - sd * 0.043f) / 0.019f, by = (y + 0.024f) / 0.015f;
                    c = Lc(c, Hx("E07B6E"), spec.blush * Mathf.Exp(-(bx * bx + by * by)) * front);
                }
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float sx = (x - sd * 0.032f) / 0.020f, sy = (y - 0.006f) / 0.010f;
                    c = Mul(c, 1f - 0.07f * Mathf.Exp(-(sx * sx + sy * sy)) * front);
                }
                if (spec.elder)
                {
                    Color bag = Sh(Lc(skin, Hx("8C6A72"), 0.4f), 0.85f);
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        float ux = (x - sd * 0.033f) / 0.016f, uy = (y + 0.012f) / 0.006f;
                        c = Lc(c, bag, 0.5f * Mathf.Exp(-(ux * ux + uy * uy)) * front);
                    }
                }
                if (stub > 0f)
                {
                    float line = -0.034f + 0.026f * SS(0.018f, 0.064f, Mathf.Abs(x));     // top of the beard area
                    float m = SS(line + 0.004f, line - 0.006f, y);
                    float lx = x / (f.mouthW * 1.05f), ly = (y - f.mouthY) / 0.0055f;
                    m *= 1f - 0.85f * Mathf.Exp(-lx * lx * lx * lx - ly * ly * ly * ly);   // keep the lips clean
                    if (y < -0.096f) m *= 0.65f + 0.35f * SS(-0.112f, -0.096f, y);
                    m *= SS(-0.03f, 0f, z + 0.006f);                                       // not on the nape
                    c = Lc(c, stubCol, stub * m);
                }
                if (y < -0.095f) c = Mul(c, 1f - 0.12f * SS(-0.095f, -0.12f, y));
                return c;
            }

            // ============================================================== decals (spec 4)
            void DecalStrip(Vector2[] lo, Vector2[] hi, Color col, float off, float gloss = 0f)
            {
                var P = new Vector3[2, lo.Length];
                for (int k = 0; k < lo.Length; k++) { P[0, k] = hs.FacePoint(lo[k].x, lo[k].y, off); P[1, k] = hs.FacePoint(hi[k].x, hi[k].y, off); }
                buf.Gloss = gloss; buf.Emit = 0f;
                buf.Grid(P, Same(2, lo.Length, col), false, DecalOut);
            }

            void DecalRows(Vector2[][] rows, Color[] cols, float off, float gloss)
            {
                int R = rows.Length, K = rows[0].Length;
                var P = new Vector3[R, K];
                var C = new Color[R, K];
                for (int i = 0; i < R; i++)
                    for (int k = 0; k < K; k++) { P[i, k] = hs.FacePoint(rows[i][k].x, rows[i][k].y, off); C[i, k] = cols[i]; }
                buf.Gloss = gloss; buf.Emit = 0f;
                buf.Grid(P, C, false, DecalOut);
            }

            void DecalFan(Vector2 c, Vector2[] ring, Color cc, Color cr, float off, float gloss = 0f, float emit = 0f)
            {
                buf.Gloss = gloss; buf.Emit = emit;
                int ci = buf.Vert(hs.FacePoint(c.x, c.y, off), cc), r0 = buf.Count;
                foreach (Vector2 p in ring) buf.Vert(hs.FacePoint(p.x, p.y, off), cr);
                Vector3 o = new Vector3(0.3f * c.x, 0f, 1f);
                for (int k = 0; k < ring.Length; k++) buf.TriOut(ci, r0 + k, r0 + (k + 1) % ring.Length, o);
                buf.Emit = 0f;
            }

            /// Strip of sinusoidal thickness along a polyline (wrinkles).
            void ThinLine(Vector2[] pts, float th, Color col)
            {
                int n = pts.Length;
                var lo = new Vector2[n];
                var hi = new Vector2[n];
                for (int k = 0; k < n; k++)
                {
                    float w = th * Mathf.Sin(Mathf.PI * (0.1f + 0.8f * k / (n - 1)));
                    lo[k] = new Vector2(pts[k].x, pts[k].y - w * 0.5f); hi[k] = new Vector2(pts[k].x, pts[k].y + w * 0.5f);
                }
                if (pts[0].x > pts[n - 1].x) { Array.Reverse(lo); Array.Reverse(hi); }
                DecalStrip(lo, hi, col, 0.0010f);
            }

            // ------------------------------------------------------------------ eyes
            /// Upper and lower lid offsets at u in [-1 (inner), 1 (outer)], relative to the eye centre.
            void Lids(float u, out float up, out float lo)
            {
                float uu = 1f - u * u;
                up = f.Uc * Pw(uu, f.pu) * (1f - f.skU * u) + f.tilt * u;
                lo = -f.Lc * Pw(uu, f.pl) * (1f + f.skL * u) + f.tilt * u;
            }

            Vector2 ClampToLids(int side, float x, float y, float margin)
            {
                Lids(Mathf.Clamp((x * side - f.ex) / f.eyeA, -1f, 1f), out float up, out float lo);
                return new Vector2(x, Mathf.Min(Mathf.Max(y, f.ey + lo + margin), f.ey + up - margin));
            }

            void Eye(int side)
            {
                buf.Reset();
                buf.Weight = MB.Bone(SVSkel.Eyes);           // every eye decal blinks with the Eyes bone
                float X(float u) => side * (f.ex + f.eyeA * u);
                void Order(params Vector2[][] rows) { if (side < 0) foreach (var r in rows) Array.Reverse(r); }
                int n = EyeU.Length;
                var lo = new Vector2[n];
                var mid = new Vector2[n];
                var up = new Vector2[n];
                for (int k = 0; k < n; k++)
                {
                    Lids(EyeU[k], out float uy, out float ly);
                    float x = X(EyeU[k]);
                    lo[k] = new Vector2(x, f.ey + ly); up[k] = new Vector2(x, f.ey + uy); mid[k] = new Vector2(x, f.ey + 0.45f * uy + 0.55f * ly);
                }
                Order(lo, mid, up);
                Color scl = Hx("F1ECE6");
                DecalRows(new[] { lo, mid, up }, new[] { scl, scl, Sh(scl, 0.80f) }, 0.0008f, 0.35f);   // top row: lid shadow

                // iris (clamped to the lids, dark limbal rim) and pupil
                Vector2 ic = new Vector2(side * (f.ex + f.gx), f.ey + f.gy);
                var ring = new Vector2[16];
                for (int k = 0; k < 16; k++) { float t = TwoPi * k / 16f; ring[k] = ClampToLids(side, ic.x + f.ri * Mathf.Cos(t), ic.y + f.ri * Mathf.Sin(t), 0.0002f); }
                Order(ring);
                DecalFan(ic, ring, Sh(iris, 1.12f), Sh(iris, 0.50f), 0.0012f, 0.4f);
                float rp = f.ri * f.pupil;
                ring = new Vector2[10];
                for (int k = 0; k < 10; k++) { float t = TwoPi * k / 10f; ring[k] = ClampToLids(side, ic.x + rp * Mathf.Cos(t), ic.y + rp * Mathf.Sin(t), 0.0003f); }
                Order(ring);
                Color pupil = Hx("120C0A");
                DecalFan(ic, ring, pupil, pupil, 0.0015f, 0.4f);

                // two highlights, placed the same on both eyes (not mirrored)
                Color white = Color.white;
                for (int h = 0; h < 2; h++)
                {
                    float hx = h == 0 ? f.hlx : -f.hlx * 0.8f, hy = h == 0 ? f.hly : -f.hly * 0.9f, hr = h == 0 ? f.hlr : f.hlr * 0.45f;
                    Vector2 c = ic + new Vector2(hx, hy) * f.ri;
                    float rr = hr * f.ri;
                    var hex = new Vector2[6];
                    for (int k = 0; k < 6; k++) { float t = TwoPi * k / 6f; hex[k] = c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * rr; }
                    DecalFan(c, hex, white, white, 0.0019f, 0f, 0.12f);
                }

                // upper lash line, thickest toward the outer corner, plus the outer flick point
                var lo2 = new Vector2[n + 1];
                var hi2 = new Vector2[n + 1];
                for (int k = 0; k < n; k++)
                {
                    float u = EyeU[k];
                    Lids(u, out float uy, out _);
                    float tk = f.lashT * (0.35f + 0.65f * Pw(Mathf.Sin(Mathf.PI * (u + 1f) / 2f * 0.92f + 0.12f), 0.8f));
                    lo2[k] = new Vector2(X(u), f.ey + uy - 0.0004f); hi2[k] = new Vector2(X(u), f.ey + uy + tk);
                }
                Lids(1f, out float upE, out _);
                lo2[n] = new Vector2(X(1f) + side * 0.0002f, f.ey + upE - 0.0004f + f.flickUp * 0.3f);
                hi2[n] = new Vector2(X(1f + f.flick / f.eyeA), f.ey + upE + f.flickUp);
                Order(lo2, hi2);
                DecalStrip(lo2, hi2, Hx("211613"), 0.0017f);

                if (f.lowLine > 0f)   // subtle lower lid line (women), from u = -0.4 outward
                {
                    var lo3 = new Vector2[n - 3];
                    var hi3 = new Vector2[n - 3];
                    for (int k = 3; k < n; k++)
                    {
                        Lids(EyeU[k], out _, out float ly);
                        lo3[k - 3] = new Vector2(X(EyeU[k]), f.ey + ly - f.lowLine); hi3[k - 3] = new Vector2(X(EyeU[k]), f.ey + ly + 0.0002f);
                    }
                    Order(lo3, hi3);
                    DecalStrip(lo3, hi3, Sh(skin, 0.72f), 0.0012f);
                }
                if (f.crease > 0f)    // lid crease (elders), u from -0.66 to 0.88
                {
                    var lo4 = new Vector2[n - 3];
                    var hi4 = new Vector2[n - 3];
                    for (int k = 2; k < n - 1; k++)
                    {
                        float u = EyeU[k];
                        Lids(u * 0.95f, out float uy, out _);
                        float yv = f.ey + uy + f.lashT + f.crease + 0.0008f * (1f - u * u);
                        lo4[k - 2] = new Vector2(X(u * 0.95f), yv); hi4[k - 2] = new Vector2(X(u * 0.95f), yv + 0.0009f * (1f - 0.5f * u * u));
                    }
                    Order(lo4, hi4);
                    DecalStrip(lo4, hi4, Sh(skin, 0.80f), 0.0010f);
                }
            }

            // ------------------------------------------------------------------ brows
            void Brow(int side)
            {
                buf.Reset();
                // grey (low chroma, not near black) or blonde (light) hair takes a neutral grey so the brows stay visible (spec 4)
                float avg = (hair.r + hair.g + hair.b) / 3f, chroma = Mathf.Max(hair.r, hair.g, hair.b) - Mathf.Min(hair.r, hair.g, hair.b);
                bool light = avg >= 110f / 255f || (chroma < 0.06f && avg > 0.35f);
                Color col = light ? Lc(hair, Hx("6A6560"), 0.35f) : Lc(hair, Hx("2A1E18"), 0.25f);
                var lo = new Vector2[9];
                var hi = new Vector2[9];
                for (int k = 0; k <= 8; k++)
                {
                    float t = k / 8f;
                    float x = f.browX0 + (f.browX1 - f.browX0) * t;
                    float y = f.browY0 + (f.browY1 - f.browY0) * t + f.browArch * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t / 0.62f) * 0.5f) * (1f - SS(0.62f, 1f, t) * 0.75f);
                    float th = (f.browT0 * (1f - t) + f.browT1 * t) * (1f + 0.15f * Mathf.Sin(Mathf.PI * t)) * (t < 0.08f ? 0.75f : 1f);
                    lo[k] = new Vector2(side * x, y - th * 0.45f); hi[k] = new Vector2(side * x, y + th * 0.55f);
                }
                if (side < 0) { Array.Reverse(lo); Array.Reverse(hi); }
                DecalStrip(lo, hi, col, 0.0016f);
            }

            // ------------------------------------------------------------------ nose
            void Nose()
            {
                buf.Reset();
                Color tipCol = Hx("D9796E");
                var P = new Vector3[NoseT.Length, NoseU.Length];
                var C = new Color[NoseT.Length, NoseU.Length];
                for (int i = 0; i < NoseT.Length; i++)
                {
                    float t = NoseT[i], y, hw, pr;
                    if (t <= 1f)
                    {
                        y = f.noseYRoot + (f.noseYTip - f.noseYRoot) * t;
                        hw = f.noseWRoot + (f.noseWTip - f.noseWRoot) * Mathf.Pow(t, 1.3f);
                        pr = f.nosePRoot + (f.nosePTip - f.nosePRoot) * Mathf.Pow(t, 1.5f) + f.noseHump * Mathf.Sin(Mathf.PI * t) * (1f - t * 0.3f);
                    }
                    else if (t <= 1.12f) { y = f.noseYTip - 0.0045f; hw = f.noseWTip * 1.02f; pr = f.nosePTip * 0.72f; }   // tip underside
                    else { y = f.noseYBase; hw = f.noseWTip * 0.85f; pr = 0f; }                                             // tucks into the lip
                    for (int j = 0; j < NoseU.Length; j++)
                    {
                        float u = NoseU[j], au = Mathf.Abs(u), x = u * hw;
                        float prof = Pw(1f - Mathf.Pow(au, f.noseSharp), 0.5f);
                        P[i, j] = new Vector3(x, y, hs.ZFace(x, y) + pr * prof - (j == 0 || j == NoseU.Length - 1 ? 0.0012f : 0f));
                        Color c = skin;
                        if (t >= 0.82f && t <= 1.12f) c = Lc(skin, tipCol, f.noseRed * (1f - au));
                        if (t > 1.05f && au > 0.2f && au < 0.8f) c = Sh(c, 0.72f);    // nostril hint
                        C[i, j] = c;
                    }
                }
                buf.Gloss = 0.18f;
                buf.Grid(P, C, false, q => new Vector3(0f, -0.2f, 1f));
                Color wing = Sh(skin, 0.98f), under = Sh(skin, 0.80f);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float x = sd * (f.noseWTip + f.noseWing * 0.10f), y = f.noseYTip - 0.0015f;
                    buf.Ico(new Vector3(x, y, hs.ZFace(x, y) - 0.0028f), new Vector3(f.noseWing, f.noseWing * 0.82f, f.noseWing * 0.95f), wing, 1,
                            colFn: v => v.y > -0.4f ? wing : under);
                }
            }

            // ------------------------------------------------------------------ mouth
            float MouthLine(float u) => f.mouthY + ex.smile * u * u + ex.corner * Mathf.Abs(u * u * u) + ex.asym * u;

            Color LipBase => Hx(kind == Kind.Female ? "C46A62" : "B9705F");
            Color LowLipCol => Lc(skin, LipBase, kind == Kind.Female ? 0.85f : 0.62f);

            /// The closed mouth, always on the Head bone: lower lip, women's upper lip with a cupid's bow, mouth line.
            void MouthClosed()
            {
                buf.Reset();
                float w = f.mouthW;
                var lo = new Vector2[11];
                var hi = new Vector2[11];
                if (f.lowLip > 0f)
                {
                    for (int k = 0; k < 11; k++)
                    {
                        float u = (k - 5) / 5f, yl = MouthLine(u);
                        lo[k] = new Vector2(u * w * 0.82f, yl - f.lowLip * Pw(1f - u * u, 0.7f) - 0.0004f); hi[k] = new Vector2(u * w * 0.82f, yl);
                    }
                    DecalStrip(lo, hi, LowLipCol, 0.0009f, 0.3f);
                }
                if (f.upLip > 0f)
                {
                    for (int k = 0; k < 11; k++)
                    {
                        float u = (k - 5) / 5f, yl = MouthLine(u);
                        float bow = f.upLip * Pw(1f - u * u, 0.6f) - 0.0010f * Mathf.Exp(-Sq(u / 0.16f));
                        lo[k] = new Vector2(u * w * 0.9f, yl); hi[k] = new Vector2(u * w * 0.9f, yl + bow);
                    }
                    DecalStrip(lo, hi, Mul(Lc(skin, LipBase, 0.75f), 0.92f), 0.0009f, 0.3f);
                }
                for (int k = 0; k < 11; k++)
                {
                    float u = (k - 5) / 5f, yl = MouthLine(u), th = f.mouthTh * (1f - 0.6f * u * u);
                    lo[k] = new Vector2(u * w, yl - th * 0.5f); hi[k] = new Vector2(u * w, yl + th * 0.5f);
                }
                DecalStrip(lo, hi, Lc(Hx("6E2E2A"), skin, 0.15f), 0.0013f);
            }

            /// The open mouth (spec 4) on the Mouth bone, lifted above the closed decal: the animator scales the bone from 0
            /// (hidden) to 1 (open) about mouthPivot. Shout and smile use their own opening, everything else a talking size.
            void MouthOpen()
            {
                buf.Reset();
                buf.Weight = MB.Bone(SVSkel.Mouth);
                float top = ex.open ? ex.openTop : TalkTop, bot = ex.open ? ex.openBot : TalkBot;
                float w = f.mouthW, cy = f.mouthY;
                Vector2[] Outline(float sw, float sh)
                {
                    var o = new Vector2[17];
                    for (int k = 0; k < 16; k++)
                    {
                        float t = TwoPi * k / 16f, x = Mathf.Cos(t) * w * sw, s = Mathf.Sin(t);
                        o[k] = new Vector2(x, cy + (s > 0f ? s * top : s * bot) * sh + ex.smile * Sq(x / w) * 0.6f);
                    }
                    o[16] = o[0];
                    return o;
                }
                Vector2[] outer = Outline(1f, 1f), inner = Outline(0.80f, 0.72f);
                Color lip = LowLipCol, inside = Hx("4A1A1E"), tongue = Hx("B85C5C");
                DecalRows(new[] { inner, outer }, new[] { lip, lip }, 0.0009f + OpenLift, 0.3f);
                var innerRing = new Vector2[16];
                Array.Copy(inner, innerRing, 16);
                DecalFan(new Vector2(0f, cy - bot * 0.1f), innerRing, inside, Sh(inside, 0.8f), 0.0011f + OpenLift);
                var tlo = new Vector2[7];
                var thi = new Vector2[7];
                for (int k = 0; k < 7; k++)   // upper teeth hanging under the top of the inner outline
                {
                    float u = -0.62f + 1.24f * k / 6f, x = u * w * 0.80f;
                    float yt = cy + top * 0.72f * Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(u / 0.8f))) + ex.smile * u * u * 0.6f;
                    thi[k] = new Vector2(x, yt + 0.0003f); tlo[k] = new Vector2(x, yt - f.teeth);
                }
                DecalStrip(tlo, thi, Hx("EEE8DE"), 0.0013f + OpenLift);
                var tr = new Vector2[10];
                for (int k = 0; k < 10; k++) { float a = TwoPi * k / 10f; tr[k] = new Vector2(Mathf.Cos(a) * w * 0.42f, cy - bot * 0.45f + Mathf.Sin(a) * bot * 0.28f); }
                DecalFan(new Vector2(0f, cy - bot * 0.45f), tr, tongue, Sh(tongue, 0.85f), 0.0013f + OpenLift);
                mouthPivot = hs.FacePoint(0f, cy, 0.0013f + OpenLift);   // in front of every closed layer, so a half-scaled mouth stays visible
            }

            // ------------------------------------------------------------------ ears
            void EarFrame(int side, out Vector3 basePt, out Vector3 bkv, out Vector3 upv, out Vector3 outv)
            {
                hs.RingAt(f.earY, out float cz, out float W, out _);
                basePt = new Vector3(side * (W - 0.002f), f.earY, cz + f.earDz);
                float fl = f.earFlare * D2R, tl = f.earTilt * D2R;
                Vector3 back = new Vector3(side * Mathf.Sin(fl), 0f, -Mathf.Cos(fl));     // flared outward, then tilted back
                outv = new Vector3(side * Mathf.Cos(fl), 0f, Mathf.Sin(fl));
                upv = Vector3.up * Mathf.Cos(tl) + back * Mathf.Sin(tl);
                bkv = back * Mathf.Cos(tl) - Vector3.up * Mathf.Sin(tl);
            }

            Vector3 EarCentre(int side)
            {
                EarFrame(side, out Vector3 bp, out Vector3 bkv, out _, out Vector3 outv);
                return bp + bkv * (f.earW * 0.5f) + outv * (f.earThick * 0.5f);
            }

            void Ear(int side)
            {
                buf.Reset();
                buf.Gloss = 0.10f;
                EarFrame(side, out Vector3 bp, out Vector3 bkv, out Vector3 upv, out Vector3 outv);
                float h = f.earH, wd = f.earW, th = f.earThick;
                Color rimC = Lc(skin, Hx("E39A8C"), 0.12f), inC = Sh(skin, 0.86f), rootC = Sh(skin, 0.85f);
                const int n = 12;
                var rim = new int[n];
                var inner = new int[n];
                var root = new int[n];
                for (int k = 0; k < n; k++)
                {
                    float t = TwoPi * k / n, c = Mathf.Cos(t), s = Mathf.Sin(t);
                    float rr = wd * (s > -0.3f ? 1f : 0.82f);           // wider top, narrower lobe
                    rim[k] = buf.Vert(bp + bkv * (wd * 0.5f + c * rr * 0.5f) + upv * (s * h * 0.5f) + outv * (th * (0.9f + 0.1f * s)), rimC);
                    inner[k] = buf.Vert(bp + bkv * (wd * 0.5f + c * rr * 0.5f * 0.58f) + upv * (s * h * 0.5f * 0.6f + 0.002f) + outv * (th * 0.55f), inC);
                    root[k] = buf.Vert(bp + bkv * (wd * 0.5f + c * rr * 0.5f * 0.55f) + upv * (s * h * 0.5f * 0.7f) - outv * 0.003f, rootC);
                }
                int concha = buf.Vert(bp + bkv * (wd * 0.42f) - upv * 0.002f + outv * 0.0015f, Sh(skin, 0.62f));
                Vector3 ctr = bp + bkv * (wd * 0.5f);
                for (int k = 0; k < n; k++)
                {
                    int k1 = (k + 1) % n;
                    buf.TriOut(rim[k], rim[k1], inner[k1], outv); buf.TriOut(rim[k], inner[k1], inner[k], outv);
                    buf.TriOut(inner[k], inner[k1], concha, outv);
                    Vector3 d = (buf.V[rim[k]] + buf.V[root[k]]) * 0.5f - ctr - outv * 0.002f;   // back side faces backward / inward
                    buf.TriOut(rim[k], root[k], root[k1], d); buf.TriOut(rim[k], root[k1], rim[k1], d);
                }
            }

            // ------------------------------------------------------------------ elder wrinkles
            void Wrinkles()
            {
                buf.Reset();
                Color c = Lc(skin, Mul(Sh(skin, 0.70f), 1f, 0.92f, 0.92f), 0.8f);
                for (int l = 0; l < 2; l++)    // forehead lines
                {
                    float yy = Sy(l == 0 ? 0.040f : 0.050f), w = l == 0 ? 0.030f : 0.026f;
                    var pts = new Vector2[7];
                    for (int k = 0; k < 7; k++) { float x = -w + 2f * w * k / 6f; pts[k] = new Vector2(x, yy + 0.003f * (1f - Sq(x / w))); }
                    ThinLine(pts, 0.0011f, c);
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    // nasolabial fold, bowed outward
                    float x0 = s * (f.noseWTip + f.noseWing * 1.1f), y0 = f.noseYTip + 0.002f, x1 = s * f.mouthW * 1.30f, y1 = f.mouthY - 0.004f;
                    var fold = new Vector2[6];
                    for (int k = 0; k < 6; k++) { float t = k / 5f; fold[k] = new Vector2(x0 + (x1 - x0) * t + s * 0.004f * Mathf.Sin(Mathf.PI * t), y0 + (y1 - y0) * t); }
                    ThinLine(fold, 0.0014f, c);
                    // crow's feet: three 7 mm rays from the outer corner
                    float ox = s * (f.ex + f.eyeA + 0.003f);
                    foreach (float ang in new[] { -25f, 0f, 22f })
                    {
                        float a = ang * D2R;
                        var ray = new Vector2[4];
                        for (int k = 0; k < 4; k++) { float d = 0.007f * k / 3f; ray[k] = new Vector2(ox + s * Mathf.Cos(a) * d, f.ey + Mathf.Sin(a) * d); }
                        ThinLine(ray, 0.0008f, c);
                    }
                    // under-eye arc
                    var arc = new Vector2[6];
                    for (int k = 0; k < 6; k++)
                    {
                        float u = -0.6f + 1.4f * k / 5f;
                        arc[k] = new Vector2(s * (f.ex + f.eyeA * u * 0.9f), f.ey - f.Lc - 0.0040f - 0.0015f * (1f - u * u));
                    }
                    ThinLine(arc, 0.0009f, c);
                }
            }

            // ============================================================== hair (spec 6)
            static Func<float, float> HL(AngCurve c, float dy = 0f) => p => c.At(p) + dy;

            /// Hair shell: rows from the hairline (or yR on drape columns) to the crown, offset along the normal by thick(phi, s)
            /// (ramped in from 'edge' near the hairline), a crown pole, and an edge wall of duplicated vertices for a crisp hairline.
            /// While hair is built under a flat cap or baseball cap: maximum shell thickness at (phi, head-local y), else null.
            Func<float, float, float> hairClamp;

            CapShell HairCap(Func<float, float> hairline, Func<float, float, float> thick, Color col, int NL = 48, int NS = 10, float edge = 0.0025f,
                             Func<float, bool> drapeIn = null, float drapeY = 0f, Func<float, float, Color, Color> colFn = null, float gloss = 0.3f,
                             bool wall = true, float wallDark = 0.55f, Func<Vector3, float, float, Vector3> post = null)
            {
                if (budget) { NL = NL > 48 ? 56 : Mathf.Min(NL, 40); NS = NS > 10 ? 12 : Mathf.Min(NS, 8); }
                float yTop = hs.yTop;
                var P = new Vector3[NS, NL];
                var C = new Color[NS, NL];
                var n0 = new Vector3[NL];
                var yLow = new float[NL];
                for (int j = 0; j < NL; j++)
                {
                    float phi = Phi(j, NL);
                    bool dr = drapeIn != null && drapeIn(phi);
                    yLow[j] = Sy(dr ? drapeY : hairline(phi));
                    for (int i = 0; i < NS; i++)
                    {
                        float s = i / (float)(NS - 1);
                        float y = yLow[j] + (yTop - 0.0015f - yLow[j]) * Mathf.Pow(s, 0.85f);
                        Vector3 n = hs.N(y, phi);
                        float t = thick(phi, s);
                        if (!dr) t = edge + (t - edge) * SS(0f, 0.14f, s);
                        if (hairClamp != null) t = Mathf.Min(t, hairClamp(phi, y));
                        Vector3 q = hs.S(y, phi) + n * t;
                        P[i, j] = post != null ? post(q, phi, s) : q;
                        if (i == 0) n0[j] = n;
                        Color c = Sh(col, (0.80f + 0.30f * SS(0f, 0.7f, s)) * (j % 2 == 0 ? 1.035f : 0.965f));
                        C[i, j] = colFn != null ? colFn(phi, s, c) : c;
                    }
                }
                buf.Gloss = gloss;
                int[,] idx = buf.Grid(P, C, true, q => q - new Vector3(0f, 0f, -0.015f));
                float tTop = thick(0f, 1f);
                if (hairClamp != null) tTop = Mathf.Min(tTop, hairClamp(0f, yTop));
                Vector3 tp = hs.S(yTop, 0f) + new Vector3(0f, tTop, 0f);
                int pole = buf.Vert(post != null ? post(tp, 0f, 1f) : tp, Sh(col, 1.12f));
                for (int j = 0; j < NL; j++) buf.TriOut(pole, idx[NS - 1, j], idx[NS - 1, (j + 1) % NL], Vector3.up);
                if (wall)
                {
                    // normal mode: four fresh vertices per quad (crisp); budget mode: two vertices per column, shared by neighbours
                    Color wa = Sh(col, wallDark), wb = Sh(col, wallDark * 0.8f);
                    var topV = new int[NL];
                    var lowV = new int[NL];
                    for (int j = 0; j < NL; j++) topV[j] = lowV[j] = -1;
                    int WallVert(int[] cache, int j, Vector3 p, Color c)
                    {
                        if (!budget) return buf.Vert(p, c);
                        if (cache[j] < 0) cache[j] = buf.Vert(p, c);
                        return cache[j];
                    }
                    Vector3 Low(int j) => hs.S(yLow[j], Phi(j, NL)) - n0[j] * 0.0015f;
                    for (int j = 0; j < NL; j++)
                    {
                        if (drapeIn != null && drapeIn(Phi(j, NL)) && drapeIn(Phi(j + 1, NL))) continue;
                        int j1 = (j + 1) % NL;
                        int a = WallVert(topV, j, P[0, j], wa), a1 = WallVert(topV, j1, P[0, j1], wa);
                        int c0 = WallVert(lowV, j, Low(j), wb), c1 = WallVert(lowV, j1, Low(j1), wb);
                        Vector3 down = P[0, j] - P[1, j];
                        buf.TriOut(a, a1, c1, down); buf.TriOut(a, c1, c0, down);
                    }
                }
                return new CapShell { idx = idx, P = P, NL = NL, NS = NS };
            }

            /// Curtain of hair hanging from the cap's lowest row over the contiguous run of drape columns: outer surface
            /// (row 0 shares the cap vertices), inner surface, bottom rim and two side walls. attrs(t) sets weight / wind.
            void HairDrape(CapShell cap, Color col, Func<float, bool> inRange, AngCurve yEnd, float flare, float curl, float thickD, int ND, float gloss,
                           Func<float, Vector3> lean = null, float taper = 0f, float raggy = 0f, float endLight = 0f, Action<float> attrs = null)
            {
                int NL = cap.NL;
                var order = new List<int>();
                for (int j0 = 0; j0 < NL && order.Count == 0; j0++)
                    if (inRange(Phi(j0, NL)) && !inRange(Phi((j0 + NL - 1) % NL, NL)))
                        for (int j = j0; inRange(Phi(j, NL)) && order.Count < NL; j = (j + 1) % NL) order.Add(j);
                int K = order.Count;
                if (K < 2) return;
                var idxO = new int[ND + 1, K];
                var idxI = new int[ND + 1, K];
                var Pout = new Vector3[ND + 1, K];
                var Pin = new Vector3[ND + 1, K];
                var C = new Color[ND + 1, K];
                buf.Gloss = gloss;
                for (int kk = 0; kk < K; kk++)
                {
                    int j = order[kk];
                    float phi = Phi(j, NL);
                    Vector3 p0 = cap.P[0, j];
                    Vector3 h = new Vector3(p0.x, 0f, p0.z - hs.Cz(p0.y));
                    h /= Mathf.Max(h.magnitude, 1e-6f);
                    float ye = Sy(yEnd.At(phi)) + raggy * (((j * 7919) % 13) / 12f - 0.5f);   // ragged ends
                    float stripe = j % 2 == 0 ? 1.05f : 0.95f;
                    for (int k = 0; k <= ND; k++)
                    {
                        float t = k / (float)ND;
                        float sx = 1f - taper * SS(0f, 0.55f, t) + taper * 0.35f * SS(0.6f, 1f, t);
                        Vector3 q = new Vector3(p0.x * sx, p0.y + (ye - p0.y) * t, p0.z) + h * (flare * Mathf.Sin(t * Mathf.PI / 2f)) - h * (curl * SS(0.6f, 1f, t));
                        if (lean != null) q += lean(phi) * t;
                        Pout[k, kk] = q;
                        Pin[k, kk] = q - h * (thickD * (0.3f + 0.7f * SS(0f, 0.3f, t)));
                        C[k, kk] = Sh(col, (0.98f - 0.12f * t + endLight * t) * stripe);
                        attrs?.Invoke(t);
                        idxO[k, kk] = k == 0 ? cap.idx[0, j] : buf.Vert(q, C[k, kk]);
                        idxI[k, kk] = buf.Vert(Pin[k, kk], Mul(C[k, kk], 0.55f));
                    }
                }
                buf.GridIdx(idxO, false, q => new Vector3(q.x, 0f, q.z + 0.015f));
                buf.GridIdx(idxI, false, q => new Vector3(-q.x, 0f, -q.z - 0.015f));
                var Pr = new Vector3[2, K];
                var Cr = new Color[2, K];
                for (int kk = 0; kk < K; kk++)
                {
                    Pr[0, kk] = Pout[ND, kk]; Pr[1, kk] = Pin[ND, kk];
                    Cr[0, kk] = Mul(C[ND, kk], 0.8f); Cr[1, kk] = Mul(C[ND, kk], 0.6f);
                }
                attrs?.Invoke(1f);
                buf.Grid(Pr, Cr, false, q => Vector3.down);
                for (int side = 0; side < 2; side++)    // side walls at the face edges
                {
                    int kk = side == 0 ? 0 : K - 1, nb = side == 0 ? 1 : K - 2;
                    var Pw2 = new Vector3[2, ND + 1];
                    var Cw = new Color[2, ND + 1];
                    for (int k = 0; k <= ND; k++)
                    {
                        Pw2[0, k] = Pout[k, kk]; Pw2[1, k] = Pin[k, kk];
                        Cw[0, k] = Mul(C[k, kk], 0.85f); Cw[1, k] = Mul(C[k, kk], 0.65f);
                    }
                    Vector3 tang = Pout[0, kk] - Pout[0, nb];
                    int[,] idx = buf.Verts(Pw2, Cw, attrs == null ? null : (Action<int, int>)((i, k) => attrs(k / (float)ND)));
                    buf.GridIdx(idx, false, q => tang);
                }
            }

            void Hair()
            {
                buf.Reset();
                Color col = hair;
                AngCurve capEdge = spec.headwear == "flatcap" ? EdgeLine(0.062f, 0.040f, 0.020f) : spec.headwear == "baseball" ? EdgeLine(0.056f, 0.032f, 0.010f) : null;
                float capMax = spec.headwear == "flatcap" ? 0.0035f : 0.0070f;
                hairClamp = capEdge == null ? null : (Func<float, float, float>)((phi, y) =>
                {
                    float e = Sy(capEdge.At(phi)) - 0.004f;
                    return y > e ? capMax : capMax + 0.08f * (e - y);   // ~4 mm at the temple, ~8 mm at the nape
                });
                switch (spec.hairStyle)
                {
                    case "crop":
                        HairCap(HL(HlMale), (p, s) => 0.0045f + 0.0050f * SS(0.15f, 0.9f, s) + 0.0025f * Gauss(p, 0f, 50f) * SS(0f, 0.4f, s), col);
                        break;
                    case "buzz":
                        HairCap(HL(HlMale, 0.002f), (p, s) => 0.0018f + 0.0012f * s, Lc(col, skin, 0.35f), edge: 0.0012f, gloss: 0.1f, wall: false,
                                colFn: (p, s, c) => Lc(c, skin, 0.45f * (1f - SS(0f, 0.25f, s))));
                        break;
                    case "sidepart": SidePart(col); break;
                    case "slick":
                        HairCap(HL(HlRecede, -0.004f), (p, s) => 0.0040f + 0.0045f * Pw(s, 0.7f) + 0.0045f * Gauss(p, 180f, 70f) * (1f - s), col, gloss: 0.6f,
                                colFn: (p, s, c) => Mul(c, 1f + 0.13f * Mathf.Sin(16f * p)));    // comb stripes
                        break;
                    case "quiff":
                        HairCap(HL(HlMale), (p, s) => 0.0050f + 0.0050f * Pw(s, 0.8f)
                                + 0.0200f * Gauss(p, 0f, 32f) * Pw(Mathf.Sin(Mathf.PI * Mathf.Min(1f, s / 0.6f)), 1.5f) * (1f - SS(0.6f, 0.9f, s)), col);
                        break;
                    case "curly": Curly(col); break;
                    case "balding": Balding(col); break;
                    case "kidmessy": KidMessy(col); break;
                    case "long": case "bob": case "ponytail": case "bun": case "lowbun": case "braid": case "pigtails": LongStyles(col); break;
                }
                hairClamp = null;
                buf.Reset();
            }

            void SidePart(Color col)
            {
                float part = spec.partLeft ? -38f : 38f, sg = Mathf.Sign(part);
                HairCap(HL(HlMale), (p, s) =>
                {
                    float heavy = SS(-0.2f, 0.6f, -sg * Mathf.Sin(p));      // fuller on the side away from the parting
                    float t = 0.0055f + 0.0080f * Pw(s, 0.6f) + 0.0050f * heavy * Gauss(p, 0f, 70f) * (1f - s * 0.5f);
                    t -= 0.0045f * Gauss(p, part, 7f) * (1f - SS(0.55f, 0.85f, s));
                    return Mathf.Max(t, 0.0025f);
                }, col, colFn: (p, s, c) => Lc(c, Mul(c, 0.55f), Gauss(p, part, 5f) * (1f - SS(0.5f, 0.8f, s))));
            }

            void Curly(Color col)
            {
                // 90 random bumps (angle, row, height) added to the shell thickness, capped at 7.5 mm
                var bp = new float[90];
                var bs = new float[90];
                var ba = new float[90];
                for (int k = 0; k < 90; k++)
                {
                    bp[k] = (float)rng.NextDouble() * 360f - 180f; bs[k] = (float)rng.NextDouble(); ba[k] = 0.0035f + 0.0030f * (float)rng.NextDouble();
                }
                float Bumps(float p, float s)
                {
                    float d = p * Mathf.Rad2Deg, sum = 0f;
                    for (int k = 0; k < 90; k++)
                    {
                        float dd = Wrap180(d - bp[k]) / 9f, ds = (s - bs[k]) / 0.075f;
                        sum += ba[k] * Mathf.Exp(-dd * dd - ds * ds);
                    }
                    return Mathf.Min(sum, 0.0075f);
                }
                HairCap(HL(HlMale, -0.004f), (p, s) => 0.0085f + 0.0070f * s + Bumps(p, s), col, NL: 72, NS: 16, edge: 0.004f, gloss: 0.12f,
                        colFn: (p, s, c) => Sh(c, 0.86f + 0.24f * Mathf.Min(1f, Bumps(p, s) / 0.006f)));
            }

            /// Side and back band only (|phi| >= 60), puffed over the ears, fading into the skin at its top.
            void Balding(Color col)
            {
                int NL = budget ? 40 : 48;
                const int NSb = 5;
                var js = new List<int>();   // ascending j runs from phi = 60 deg round the back to 300 deg
                for (int j = 0; j < NL; j++) if (AbsDeg(Phi(j, NL)) >= 60f - 1e-3f) js.Add(j);   // float puts 300 deg at 59.99997
                var P = new Vector3[NSb, js.Count];
                var C = new Color[NSb, js.Count];
                for (int kk = 0; kk < js.Count; kk++)
                {
                    float phi = Phi(js[kk], NL), yb = Sy(BaldLine.At(phi)), yt = Sy(BaldTop.At(phi)), fade = SS(60f, 80f, AbsDeg(phi));
                    yt = yb + (yt - yb) * Mathf.Max(fade, 0.15f);
                    float puff = 0.55f + 0.45f * Gauss(phi, 95f, 30f) + 0.45f * Gauss(phi, -95f, 30f);
                    for (int i = 0; i < NSb; i++)
                    {
                        float s = i / (float)(NSb - 1), y = yb + (yt - yb) * s;
                        float t = 0.0010f + 0.0060f * puff * fade * Pw(Mathf.Sin(Mathf.PI * (0.12f + 0.76f * s)), 0.7f);
                        P[i, kk] = hs.S(y, phi) + hs.N(y, phi) * t;
                        C[i, kk] = Lc(Sh(col, (0.9f + 0.15f * s) * (kk % 2 == 1 ? 1.05f : 0.95f)), skin, 0.30f * SS(0.7f, 1f, s) + 0.3f * (1f - fade));
                    }
                }
                buf.Gloss = 0.2f;
                buf.Grid(P, C, false, q => q - new Vector3(0f, 0.01f, -0.015f));
            }

            void KidMessy(Color col)
            {
                CapShell cap = HairCap(HL(HlKid, -0.006f), (p, s) => 0.0085f + 0.0075f * s, col, edge: 0.0045f);
                // up to four tufts swept back along the surface (never standing up), at least 35 mm apart
                var placed = new List<Vector3>();
                buf.Gloss = 0.3f;
                for (int k = 0; k < 60 && placed.Count < 4 && hairClamp == null; k++)
                {
                    int j = rng.Next(0, cap.NL), i = rng.Next(6, cap.NS - 1);
                    Vector3 q = cap.P[i, j];
                    if (Near(placed, q, 0.035f)) continue;
                    placed.Add(q);
                    Vector3 n = Unit(q - new Vector3(0f, 0f, hs.Cz(q.y)));
                    Vector3 back = new Vector3(0f, 0.25f, -1f);
                    back = Unit(back - n * Vector3.Dot(back, n));
                    float L = 0.026f + 0.010f * (float)rng.NextDouble();
                    Color tc = Sh(col, 0.97f + 0.08f * (float)rng.NextDouble());
                    buf.Tube(new[] { q - n * 0.004f, q + back * (L * 0.45f) + n * 0.003f, q + back * L + n * 0.006f }, new[] { 0.012f, 0.009f, 0.0030f }, 6, tc,
                             false, true, n, 0.55f);
                }
                // three fringe tufts over the forehead
                float y0 = Sy(0.050f);
                float[] xo = { -0.024f, -0.004f, 0.017f }, dx = { -0.004f, 0.003f, 0.006f };
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = hs.FacePoint(xo[k], y0, 0.006f, out Vector3 n), tip = hs.FacePoint(xo[k] + dx[k], y0 - 0.017f, 0.0045f);
                    buf.Tube(new[] { p + new Vector3(0f, 0.006f, 0f), (p + tip) * 0.5f + n * 0.002f, tip }, new[] { 0.0110f, 0.0075f, 0.0018f }, 6, col, false, true);
                }
            }

            /// Wind on ponytail and braid tips.
            float TipWind(float t) => 0.15f * SS(0.4f, 1f, t);

            void LongStyles(Color col)
            {
                string st = spec.hairStyle;
                bool tight = st != "long" && st != "bob";
                AngCurve baseLine = kind == Kind.Child ? HlKid : HlFemale;
                AngCurve line = st == "bob" ? baseLine.After(52f, 0, .032f, 20, .033f, 34, .036f, 44, .042f)                            // blunt fringe
                              : st == "long" && spec.curtainFringe ? baseLine.After(52f, 0, .050f, 12, .048f, 25, .042f, 40, .038f)   // curtain fringe
                              : baseLine;
                Func<float, float, float> part = (p, s) => 0.0030f * Gauss(p, 0f, 6f) * (1f - SS(0.4f, 0.8f, s));   // thinner along the centre parting
                Func<float, float, float> th;
                if (tight)
                {
                    bool gathered = st == "bun" || st == "ponytail";   // extra volume toward the tie
                    th = (p, s) => 0.0032f + 0.0030f * s + (gathered ? 0.0035f * Gauss(p, 180f, 50f) * SS(0.1f, 0.5f, s) : 0f) - part(p, s) * 0.5f;
                }
                else if (st == "bob") th = (p, s) => 0.0060f + 0.0045f * Pw(s, 0.7f) + 0.003f * (1f - Gauss(p, 0f, 60f));
                else th = (p, s) => 0.0055f + 0.0040f * Pw(s, 0.7f) + 0.0025f * (1f - Gauss(p, 0f, 60f)) - part(p, s);
                float phi0 = st == "long" ? 64f : 58f;
                Func<float, bool> drapeIn = tight ? null : (Func<float, bool>)(p => AbsDeg(p) >= phi0);
                Func<float, float, Color, Color> partLine = st == "bob" || st == "pigtails" ? null
                    : (Func<float, float, Color, Color>)((p, s, c) => Lc(c, Mul(c, 0.55f), Gauss(p, 0f, 4f) * (1f - SS(0.35f, 0.75f, s))));
                CapShell cap = HairCap(HL(line), th, col, edge: st == "bob" ? 0.0040f : tight ? 0.0014f : 0.0022f, drapeIn: drapeIn,
                                       drapeY: st == "long" ? 0.040f : 0.036f, colFn: partLine, wallDark: st == "bob" ? 0.55f : 0.7f);
                switch (st)
                {
                    case "long":   // drape blends to the chest (head weight 1 - 0.85 SS(.15, 1, t)) and sways at the ends
                        HairDrape(cap, col, drapeIn, LongEnd, 0.003f, 0.003f, 0.010f, 8, 0.3f, p => new Vector3(0f, 0f, -0.028f * SS(0.2f, 1f, -Mathf.Cos(p))),
                                  0.16f, 0.016f, 0.16f, t => { buf.Weight = HeadChest(1f - 0.85f * SS(0.15f, 1f, t)); buf.Wind = 0.15f * SS(0.35f, 1f, t); });
                        break;
                    case "bob":
                        HairDrape(cap, col, drapeIn, BobEnd, 0.006f, 0.008f, 0.010f, 5, 0.3f);
                        break;
                    case "ponytail": Ponytail(col); break;
                    case "bun": case "lowbun": Bun(col, st == "bun"); break;
                    case "braid": Braid(col); break;
                    case "pigtails": Pigtails(col); break;
                }
                buf.Reset();
            }

            void Ponytail(Color col)
            {
                float yb = Sy(0.040f);
                hs.RingAt(yb, out float cz, out _, out float B);
                Vector3 band = new Vector3(0f, yb, cz - B - 0.005f);
                buf.Gloss = 0f;
                buf.Ico(band, new Vector3(0.013f, 0.012f, 0.010f), Hx("B03A48"), 1);
                Vector3[] pts =
                {
                    band + new Vector3(0f, 0.006f, 0.004f), band + new Vector3(0f, 0f, -0.020f), band + new Vector3(0f, -0.032f, -0.042f),
                    band + new Vector3(0f, -0.080f, -0.045f), band + new Vector3(0f, -0.125f, -0.036f), band + new Vector3(0f, -0.158f, -0.028f)
                };
                buf.Gloss = 0.3f;
                buf.Tube(pts, new[] { 0.012f, 0.022f, 0.026f, 0.022f, 0.014f, 0.004f }, 9, col, true, false,
                         colFn: (t, a) => Sh(col, (0.95f - 0.1f * t) * (1f + 0.06f * Mathf.Cos(a * 3f))), ring: (i, t) => buf.Wind = TipWind(t));
            }

            void Bun(Color col, bool high)
            {
                float y = Sy(high ? 0.080f : -0.008f), r = high ? 0.040f : 0.034f;   // the low bun is typical for older women
                hs.RingAt(y, out float cz, out _, out float B);
                buf.Gloss = 0.3f;
                buf.Ico(new Vector3(0f, y, cz - B - r * 0.45f), new Vector3(r, r * 0.86f, r * 0.82f), col, 2,
                        colFn: v => Sh(col, 0.90f + 0.14f * Mathf.Sin(Mathf.Atan2(v.y, v.x) * 3f + v.z * 5f)));   // swirl
            }

            void Braid(Color col)
            {
                hs.RingAt(Sy(-0.030f), out float cz, out _, out float B);
                Vector3 start = new Vector3(0f, Sy(-0.026f), cz - B - 0.002f);
                const int NB = 22;
                var pts = new Vector3[NB + 1];
                var rad = new float[NB + 1];
                for (int k = 0; k <= NB; k++)
                {
                    float t = k / (float)NB;
                    pts[k] = start + new Vector3(0f, -0.175f * t, -0.020f * Mathf.Sin(t * 1.4f) - 0.006f);
                    rad[k] = (0.0165f - 0.006f * t) * (0.80f + 0.20f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 7f)));   // plaited lobes
                }
                rad[NB] = 0.004f;
                void Attrs(float t) { buf.Weight = HeadChest(1f - SS(0.3f, 1f, t)); buf.Wind = TipWind(t); }
                buf.Gloss = 0.3f;
                buf.Tube(pts, rad, 8, col, true, false, colFn: (t, a) => Sh(col, 0.80f + 0.22f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 7f + 0.6f * Mathf.Cos(a)))),
                         ring: (i, t) => Attrs(t));
                Attrs((NB - 2) / (float)NB);   // the tie moves with the braid
                buf.Gloss = 0f;
                buf.Ico(pts[NB - 2], new Vector3(0.0085f, 0.0065f, 0.0085f), Hx("C0394A"), 1);
            }

            void Pigtails(Color col)
            {
                float yb = Sy(0.012f);
                hs.RingAt(yb, out float cz, out float W, out _);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 bp = new Vector3(sg * (W - 0.004f), yb, cz - 0.040f);
                    buf.Gloss = 0f;
                    buf.Ico(bp + new Vector3(sg * 0.008f, 0f, 0f), new Vector3(0.008f, 0.010f, 0.010f), Hx("E0B020"), 1);
                    Vector3[] pts =
                    {
                        bp + new Vector3(sg * 0.004f, 0.004f, 0f), bp + new Vector3(sg * 0.020f, -0.006f, -0.006f), bp + new Vector3(sg * 0.028f, -0.036f, -0.010f),
                        bp + new Vector3(sg * 0.026f, -0.068f, -0.008f), bp + new Vector3(sg * 0.020f, -0.090f, -0.004f)
                    };
                    buf.Gloss = 0.3f;
                    buf.Tube(pts, new[] { 0.010f, 0.016f, 0.017f, 0.012f, 0.003f }, 8, col, true, false);
                }
            }

            // ============================================================== facial hair (spec 7)
            void FacialHair()
            {
                buf.Reset();
                Color col = spec.elder ? Lc(hair, Hx("E8E4DE"), 0.2f) : hair;
                switch (spec.facial)
                {
                    case "classic": case "chevron": case "walrus": case "thin": Moustache(col, spec.facial); break;
                    case "fullbeard": Beard(col, 0.0060f, 0.004f, false); Moustache(col, "classic"); break;
                    case "longbeard": Beard(col, 0.0080f, 0.026f, false); Moustache(col, "walrus"); break;
                    case "goatee": Beard(col, 0.0045f, 0.003f, true); Moustache(col, "classic"); break;
                }
            }

            /// Shell over the lower face: rows from under the jaw (s = 0) to the top line (s = 1), soft toward the cheeks.
            void Beard(Color col, float T, float length, bool goatee)
            {
                AngCurve top = goatee ? GoateeTop : BeardTop, bot = goatee ? GoateeBot : BeardBot;
                int K = goatee ? 9 : 26;
                const int NS = 7;
                float span = goatee ? 17f : 98f;
                var phis = new float[K];
                for (int j = 0; j < K; j++) phis[j] = (-span + 2f * span * j / (K - 1)) * D2R;
                float Side(float p) => goatee ? Pw(Mathf.Cos(Mathf.Min(1f, Mathf.Abs(p) * Mathf.Rad2Deg / 17.5f) * Mathf.PI / 2f), 0.45f)
                                              : 0.55f + 0.45f * SS(98f * D2R, 70f * D2R, Mathf.Abs(p));
                var P = new Vector3[NS, K];
                var C = new Color[NS, K];
                for (int j = 0; j < K; j++)
                {
                    float phi = phis[j], yt = Sy(top.At(phi)), yb = Sy(bot.At(phi)), fr = Sq(Mathf.Cos(phi));
                    for (int i = 0; i < NS; i++)
                    {
                        float s = i / (float)(NS - 1), y = yb + (yt - yb) * s;
                        float prof = (0.65f + 0.35f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, s / 0.7f))) * (1f - SS(0.72f, 1f, s) * 0.88f);
                        Vector3 q = hs.S(y, phi) + hs.N(y, phi) * (0.0007f + T * prof * Side(phi));
                        P[i, j] = q + new Vector3(0f, -length * fr * Mathf.Pow(1f - s, 1.5f), length * 0.35f * fr * Sq(1f - s));   // length hangs at the chin
                        C[i, j] = Lc(Sh(col, (0.92f + 0.14f * s) * (j % 2 == 0 ? 1.045f : 0.955f)), skin, 0.40f * SS(0.75f, 1f, s));
                    }
                }
                buf.Gloss = 0.2f;
                buf.Grid(P, C, false, q => q - new Vector3(0f, -0.02f, -0.01f));
                Color wa = Sh(col, 0.7f), wb = Sh(col, 0.55f);
                for (int j = 0; j < K - 1; j++)   // bottom wall
                {
                    int a = buf.Vert(P[0, j], wa), a1 = buf.Vert(P[0, j + 1], wa);
                    float y0 = Sy(bot.At(phis[j])), y1 = Sy(bot.At(phis[j + 1]));
                    int c0 = buf.Vert(hs.S(y0, phis[j]) - hs.N(y0, phis[j]) * 0.0015f, wb);
                    int c1 = buf.Vert(hs.S(y1, phis[j + 1]) - hs.N(y1, phis[j + 1]) * 0.0015f, wb);
                    Vector3 o = P[0, j] - P[1, j];
                    buf.TriOut(a, a1, c1, o); buf.TriOut(a, c1, c0, o);
                }
            }

            /// Projected moustache patch (13 x 6) over the upper lip; never narrower than 22.5 mm half-width (guardrail 8).
            void Moustache(Color col, string style)
            {
                float k, Tm, inset;
                Func<float, float> bot;   // bottom edge relative to the mouth line; thin hangs a fixed height below the top
                switch (style)
                {
                    case "walrus": k = 1.28f; Tm = .0058f; inset = .0010f; bot = u => -.0018f - .0105f * Sq(u * u); break;
                    case "chevron": k = 1.22f; Tm = .0050f; inset = .0012f; bot = u => .0004f - .0075f * Sq(u * u); break;
                    case "thin": k = 1.10f; Tm = .0024f; inset = .0020f; bot = null; break;
                    default: k = 1.45f; Tm = .0042f; inset = .0016f; bot = u => -.0006f - .0085f * Sq(u * u); break;   // classic: full bıyık, corners drop past the mouth
                }
                const int NU = 13, NV = 6;
                float wm = Mathf.Max(wNeutral * k, style == "thin" ? 0.0225f : 0.029f);
                var P = new Vector3[NV, NU];
                var C = new Color[NV, NU];
                for (int j = 0; j < NU; j++)
                {
                    float u = -1f + 2f * j / (NU - 1), x = u * wm, au = Mathf.Abs(u);
                    float yT = f.noseYBase - inset - 0.0035f * u * u - 0.0060f * Sq(u * u);
                    float yB = bot != null ? f.mouthY + bot(u) : yT - 0.0050f - 0.0010f * u * u;
                    float kk = SS(0.65f, 1f, au) * 0.65f;     // drooping, tapered ends
                    yT += (yB - yT) * kk * 0.6f;
                    yB += (yT - yB) * kk * 0.25f;
                    for (int i = 0; i < NV; i++)
                    {
                        float v = i / (float)(NV - 1), y = yB + (yT - yB) * v;
                        float t = (0.0006f + Tm * Pw(Mathf.Sin(Mathf.PI * (0.18f + 0.82f * v)), 0.55f) * (1f - Mathf.Pow(au, 6f) * 0.75f)) * (1f - SS(0.80f, 1f, v) * 0.85f);
                        P[i, j] = hs.FacePoint(x, y, t);
                        C[i, j] = Sh(col, (0.92f + 0.16f * v) * (j % 2 == 0 ? 1.05f : 0.95f));
                    }
                }
                buf.Gloss = 0.15f;
                buf.Grid(P, C, false, q => new Vector3(q.x * 0.5f, -0.2f, 1f));
                Color wa = Sh(col, 0.6f), wb = Sh(col, 0.45f);
                Vector3 o = new Vector3(0f, -1f, 0.4f);
                for (int j = 0; j < NU - 1; j++)   // bottom lip wall down into the skin
                {
                    int a = buf.Vert(P[0, j], wa), a1 = buf.Vert(P[0, j + 1], wa);
                    int c0 = buf.Vert(hs.FacePoint(P[0, j].x, P[0, j].y + 0.0004f, -0.0008f), wb);
                    int c1 = buf.Vert(hs.FacePoint(P[0, j + 1].x, P[0, j + 1].y + 0.0004f, -0.0008f), wb);
                    buf.TriOut(a, a1, c1, o); buf.TriOut(a, c1, c0, o);
                }
            }

            // ============================================================== headwear (spec 8)
            /// Open-front sleeve: R rows x K columns spanning [phiO, 2pi - phiO], so columns 0 and K-1 trace the face opening.
            Vector3[,] Sleeve(int R, int K, float[] phiO, Func<int, float, float> rowY, Func<int, float, float, Vector3> support,
                              Func<int, int, float, float, Color> colFn, float gloss, Func<int, float> headW)
            {
                var P = new Vector3[R, K];
                var C = new Color[R, K];
                for (int i = 0; i < R; i++)
                    for (int k = 0; k < K; k++)
                    {
                        float phi = phiO[i] + (TwoPi - 2f * phiO[i]) * k / (K - 1), y = rowY(i, phi);
                        P[i, k] = support(i, y, phi);
                        C[i, k] = colFn(i, k, y, phi);
                    }
                buf.Gloss = gloss;
                int[,] idx = buf.Verts(P, C, (i, k) => buf.Weight = HeadChest(headW(i)));
                buf.GridIdx(idx, false, q => q - new Vector3(0f, -0.02f, -0.015f));
                buf.Weight = HeadBone;
                return P;
            }

            /// Closed tube along a polyline of edge points (scarf hem, hood rim); weights follow the rows the points came from.
            void EdgeTube(List<Vector3> pts, List<float> headW, bool close, float radius, int seg, Color col)
            {
                var p2 = new List<Vector3>();
                var w2 = new List<float>();
                for (int i = 0; i < pts.Count; i++)
                    if (p2.Count == 0 || (pts[i] - p2[p2.Count - 1]).magnitude > 1e-4f) { p2.Add(pts[i]); w2.Add(headW[i]); }
                if (close)
                {
                    // the hood's last edge point coincides with its first (both on the front midline): drop it so the loop
                    // has no zero-length segment, whose direction would come from float noise at 2*pi
                    if ((p2[p2.Count - 1] - p2[0]).magnitude <= 1e-4f) { p2.RemoveAt(p2.Count - 1); w2.RemoveAt(w2.Count - 1); }
                    p2.Add(p2[0]); w2.Add(w2[0]);
                }
                buf.Tube(p2, Same(p2.Count, radius), seg, col, false, false, ring: (i, t) => buf.Weight = HeadChest(w2[i]));
                buf.Weight = HeadBone;
            }

            /// Headscarf: chin-tied basortusu (knot, tails, shoulder drape), the hijab variant (no knot, longer front drape),
            /// or the nape-tied yazma (throat free, knot with a flap and ties on the upper back).
            void Headscarf(bool chinTied, bool hijab)
            {
                buf.Reset();
                Color col = spec.hwMain, col2 = spec.hwAccent;
                bool border = spec.scarfPattern == "border";
                int K = budget ? 29 : 37;
                float yTop = hs.yTop, y0d = Sy(-0.106f), yoTop = Sy(0.046f);
                float[] rows = chinTied ? ChinScarfRows : NapeScarfRows;
                int nH = rows.Length + 1, R = nH + (chinTied ? ScarfDrapeT.Length : 0);
                var headY = new float[nH];
                headY[0] = yTop + 0.0005f;
                for (int r = 0; r < rows.Length; r++) headY[r + 1] = Sy(rows[r]);
                // drape end: a triangular point at the back; the hijab also hangs lower in front
                float YEnd(float phi)
                {
                    float c = Mathf.Cos(phi), back = Mathf.Max(0f, -c);
                    return -0.205f - 0.010f * c - 0.115f * back * back * back - (hijab ? 0.040f * Sq(Mathf.Max(0f, c)) : 0f);
                }
                float RowY(int i, float phi) => i < nH ? headY[i] : y0d + (YEnd(phi) - y0d) * ScarfDrapeT[i - nH];
                float HeadW(int i) => i < nH ? 1f : 1f - SS(0f, 0.6f, ScarfDrapeT[i - nH]);
                // face opening half-angle per row
                var phiO = new float[R];
                float yoBot = Sy(-0.104f), yc = (yoTop + yoBot) / 2f, ho = (yoTop - yoBot) / 2f;
                for (int i = 0; i < nH; i++)
                {
                    float y = headY[i];
                    if (chinTied)
                    {
                        float u = (y - yc) / ho;
                        phiO[i] = Mathf.Abs(u) >= 1f ? 0f : 64f * D2R * Pw(1f - u * u, 0.42f);
                    }
                    else if (y < yoTop)   // the lower edge runs back along the jaw to the nape
                    {
                        float u = Mathf.Min(1f, (yoTop - y) / 0.020f);
                        phiO[i] = (64f + 106f * SS(Sy(-0.020f), Sy(-0.076f), y)) * D2R * Pw(1f - Sq(1f - u), 0.5f);
                    }
                }
                // volume over the head, with gathered hair at the back
                float Vol(float phi, float y) => 0.0100f + 0.0035f * SS(0.02f, 0.10f, y) + 0.014f * Gauss(phi, 180f, 55f) * Mathf.Exp(-Sq(y / 0.045f))
                                                 + 0.003f * (1f - SS(0.02f, 0.05f, y));
                Vector3 Support(int i, float y, float phi)
                {
                    if (i < nH)
                    {
                        if (y > yTop - 1e-4f) return hs.S(yTop, 0f) + new Vector3(0f, Vol(0f, y) + 0.002f, 0f);
                        Vector3 q = hs.S(y, phi) + hs.N(y, phi) * Vol(phi, y);
                        float w = chinTied ? SS(-0.060f, -0.104f, y) : 0f;   // closes into a ring around the neck
                        return w > 0f ? Vector3.Lerp(q, NeckRing(y, phi, 0.068f, Mathf.Cos(phi) < 0f ? 0.066f : 0.078f, -0.015f), w) : q;
                    }
                    float t = (y0d - y) / Mathf.Max(1e-4f, y0d - YEnd(phi)), g = SS(0f, 0.85f, t);
                    float rz = Mathf.Cos(phi) > 0f ? 0.078f + (hijab ? 0.065f : 0.050f) * g : 0.066f + 0.070f * g;
                    return NeckRing(y, phi, 0.068f + 0.105f * g, rz, -0.015f - 0.010f * t);
                }
                Color ColFn(int i, int k, float y, float phi)
                {
                    Color c = border && (k <= 1 || k >= K - 2) && phiO[i] > 0.01f ? col2 : col;
                    c = Sh(c, 0.93f + 0.10f * SS(-0.05f, 0.12f, y));
                    return i >= nH ? Sh(c, 0.95f - 0.08f * (i - nH) / 5f) : c;
                }
                Vector3[,] P = Sleeve(R, K, phiO, RowY, Support, ColFn, 0.08f, HeadW);
                if (spec.scarfPattern == "dots") ScarfDots(P, chinTied ? 34 : 24, col2, HeadW);

                // rolled hem around the face opening
                var open = new List<int>();
                for (int i = 0; i < nH; i++) if (phiO[i] > 0.01f) open.Add(i);
                int i0 = Mathf.Max(0, open[0] - 1), i1 = chinTied ? Mathf.Min(nH - 1, open[open.Count - 1] + 1) : open[open.Count - 1];
                var hem = new List<Vector3>();
                for (int i = i1; i >= i0; i--) hem.Add(P[i, 0]);
                for (int i = i0; i <= i1; i++) hem.Add(P[i, K - 1]);
                buf.Gloss = 0.08f;
                EdgeTube(hem, new List<float>(Same(hem.Count, 1f)), !chinTied, 0.0042f, 6, border ? col2 : Sh(col, 0.9f));

                if (chinTied && !hijab)
                {
                    Vector3 kc = new Vector3(0.010f, Sy(-0.118f), 0.062f);
                    buf.Ico(kc, new Vector3(0.015f, 0.011f, 0.012f), Sh(col, 0.97f), 1);
                    for (int sg = 1; sg >= -1; sg -= 2)   // two tails hanging from the knot, swaying at the ends
                    {
                        Vector3 a = kc + new Vector3(0.004f * sg, -0.006f, 0.002f), b = a + new Vector3(0.008f * sg + 0.004f, -0.036f, 0.012f), c = b + new Vector3(0.004f * sg, -0.034f, 0.004f);
                        buf.Tube(new[] { a, b, c }, new[] { 0.009f, 0.012f, 0.004f }, 6, Sh(col, 0.97f), true, true, Vector3.forward, 0.35f, ring: (i, t) => buf.Wind = 0.25f * t);
                    }
                }
                else if (!chinTied)
                {
                    hs.RingAt(Sy(-0.060f), out float cz, out _, out float B);
                    Vector3 kc = new Vector3(0f, Sy(-0.066f), cz - B - 0.012f);
                    buf.Ico(kc, new Vector3(0.016f, 0.012f, 0.012f), Sh(col, 0.9f), 1);
                    // double-sided triangular flap on the upper back: head weight 1, 0.5, 0 and wind 0 -> 0.25 by row
                    float[] xs = { -0.030f, -0.015f, 0f, 0.015f, 0.030f };
                    var Pf = new Vector3[3, 5];
                    var Pb = new Vector3[3, 5];
                    var Cf = new Color[3, 5];
                    var Cb = new Color[3, 5];
                    for (int m = 0; m < 5; m++)
                    {
                        Pf[0, m] = kc + new Vector3(xs[m], 0.002f, -0.004f);
                        Pf[1, m] = kc + new Vector3(xs[m] * 0.55f, -0.060f, -0.022f);
                        Pf[2, m] = kc + new Vector3(xs[m] * 0.08f, -0.125f, -0.036f);
                        for (int i = 0; i < 3; i++)
                        {
                            Pb[i, m] = Pf[i, m] + new Vector3(0f, 0f, 0.003f);
                            Cf[i, m] = border && (m == 0 || m == 4) ? col2 : Sh(col, 0.92f);
                            Cb[i, m] = Mul(Cf[i, m], 0.6f);
                        }
                    }
                    void FlapRow(int i, int m) { buf.Weight = HeadChest(1f - 0.5f * i); buf.Wind = 0.125f * i; }
                    buf.GridIdx(buf.Verts(Pf, Cf, FlapRow), false, q => new Vector3(0f, 0.2f, -1f));
                    buf.GridIdx(buf.Verts(Pb, Cb, FlapRow), false, q => new Vector3(0f, -0.2f, 1f));
                    buf.Weight = HeadBone; buf.Wind = 0f;
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        Vector3 a = kc + new Vector3(0.006f * sg, -0.004f, -0.006f), b = a + new Vector3(0.010f * sg, -0.040f, -0.014f);
                        buf.Tube(new[] { a, b }, new[] { 0.008f, 0.004f }, 6, Sh(col, 0.95f), true, true, Vector3.forward, 0.4f, ring: (i, t) => buf.Wind = 0.25f * t);
                    }
                }
                buf.Reset();
            }

            /// Hexagon dots on random scarf quads, at least 22 mm apart; on the drape they take the drape's skinning.
            void ScarfDots(Vector3[,] P, int n, Color col2, Func<int, float> headW)
            {
                int R = P.GetLength(0), K = P.GetLength(1);
                var placed = new List<Vector3>();
                buf.Gloss = 0.05f;
                for (int tries = 0; placed.Count < n && tries < 600; tries++)
                {
                    int i = rng.Next(1, R - 2), k = rng.Next(1, K - 2);
                    Vector3 a = P[i, k], b = P[i, k + 1], c = P[i + 1, k + 1], d = P[i + 1, k];
                    float u = (float)rng.NextDouble() * 0.6f + 0.2f, v = (float)rng.NextDouble() * 0.6f + 0.2f;
                    Vector3 q = (a * (1f - u) + b * u) * (1f - v) + (d * (1f - u) + c * u) * v;
                    if (Near(placed, q, 0.022f)) continue;
                    Vector3 nrm = Vector3.Cross(c - a, b - d);
                    if (Vector3.Dot(nrm, q - new Vector3(0f, -0.02f, -0.015f)) < 0f) nrm = -nrm;
                    nrm = Unit(nrm);
                    Vector3 t1 = Vector3.Cross(nrm, Vector3.up);
                    t1 /= Mathf.Max(t1.magnitude, 1e-6f);
                    Vector3 t2 = Vector3.Cross(nrm, t1), ctr = q + nrm * 0.0009f;
                    placed.Add(q);
                    buf.Weight = HeadChest(Mathf.Lerp(headW(i), headW(i + 1), v));
                    int ci = buf.Vert(ctr, col2), r0 = buf.Count;
                    for (int m = 0; m < 6; m++) { float ang = TwoPi * m / 6f; buf.Vert(ctr + (t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang)) * 0.0042f, col2); }
                    for (int m = 0; m < 6; m++) buf.TriOut(ci, r0 + m, r0 + (m + 1) % 6, nrm);
                }
                buf.Weight = HeadBone;
            }

            /// Raincoat hood: roomy shell with an oval face opening, collar rows on the shoulders (0.3 head / 0.7 chest),
            /// and a thick rim whose top pushes forward into a small peak.
            void Hood()
            {
                buf.Reset();
                Color col = spec.hwMain;
                const int K = 33;
                float yTop = hs.yTop, yNeck = Sy(-0.094f);
                int R = HoodRows.Length + 1, nH = R - HoodCollar;
                var rowY = new float[R];
                rowY[0] = yTop + 0.0005f;
                for (int r = 0; r < HoodRows.Length; r++) rowY[r + 1] = Sy(HoodRows[r]);
                float yoTop = Sy(0.064f), yoBot = Sy(-0.100f), yc = (yoTop + yoBot) / 2f, ho = (yoTop - yoBot) / 2f;
                var phiO = new float[R];
                for (int i = 0; i < R; i++)
                {
                    float u = (rowY[i] - yc) / ho;
                    phiO[i] = Mathf.Abs(u) >= 1f ? 0f : 68f * D2R * Pw(1f - u * u, 0.40f);
                }
                float Vol(float phi, float y) => 0.024f + 0.010f * SS(0f, 0.10f, y) + 0.010f * Gauss(phi, 180f, 60f);
                Vector3 Support(int i, float y, float phi)
                {
                    if (y > yTop - 1e-4f) return hs.S(yTop, 0f) + new Vector3(0f, Vol(0f, y) + 0.004f, -0.004f);
                    if (y > yNeck)
                    {
                        Vector3 q = hs.S(y, phi) + hs.N(y, phi) * Vol(phi, y);
                        float w = SS(-0.050f, -0.094f, y);
                        return w > 0f ? Vector3.Lerp(q, NeckRing(y, phi, 0.086f, Mathf.Cos(phi) > 0f ? 0.088f : 0.084f, -0.016f), w) : q;
                    }
                    float t = (yNeck - y) / 0.06f;
                    return NeckRing(y, phi, 0.088f + 0.030f * t, 0.090f + 0.020f * t, -0.018f);
                }
                float HeadW(int i) => i >= nH ? 0.3f : 1f;
                Vector3[,] P = Sleeve(R, K, phiO, (i, phi) => rowY[i], Support, (i, k, y, phi) => Sh(col, 0.95f + 0.08f * SS(-0.05f, 0.12f, y)), 0.35f, HeadW);
                var open = new List<int>();
                for (int i = 0; i < R; i++) if (phiO[i] > 0.01f) open.Add(i);
                int i0 = Mathf.Max(0, open[0] - 1), i1 = Mathf.Min(R - 1, open[open.Count - 1] + 1);
                var rim = new List<Vector3>();
                var w2 = new List<float>();
                Vector3 Peak(Vector3 q) => q + new Vector3(0f, 0.002f, 0.010f) * SS(0.02f, 0.06f, q.y);
                for (int i = i1; i >= i0; i--) { rim.Add(Peak(P[i, 0])); w2.Add(HeadW(i)); }
                for (int i = i0; i <= i1; i++) { rim.Add(Peak(P[i, K - 1])); w2.Add(HeadW(i)); }
                buf.Gloss = 0.35f;
                EdgeTube(rim, w2, true, 0.0075f, 7, Sh(col, 0.9f));
            }

            void Beanie()
            {
                buf.Reset();
                Color col = spec.hwMain, col2 = spec.hwAccent;
                AngCurve bottom = EdgeLine(0.050f, 0.020f, -0.010f);
                HairCap(bottom.At, (p, s) => 0.0130f + 0.0060f * s, col, NS: 9, edge: 0.0130f, wallDark: 0.75f, gloss: 0.02f,
                        colFn: (p, s, c) => Sh(c, Stripe(p, 7.5f) ? 1.07f : 0.93f));    // ribs
                // folded cuff: three rings from 3 mm below the edge, with an underside lip down to the skin
                const int NC = 48;
                float[] dy = { 0f, 0.017f, 0.034f }, tt = { 0.0175f, 0.0205f, 0.0170f };
                var Pc = new Vector3[3, NC];
                var Cc = new Color[3, NC];
                var lip = new Vector3[NC];
                for (int j = 0; j < NC; j++)
                {
                    float phi = Phi(j, NC), yb = Sy(bottom.At(phi)) - 0.003f;
                    for (int i = 0; i < 3; i++)
                    {
                        Pc[i, j] = hs.S(yb + dy[i], phi) + hs.N(yb + dy[i], phi) * tt[i];
                        Cc[i, j] = Sh(col2, (i != 1 ? 0.92f : 1.04f) * (j % 2 == 1 ? 1.06f : 0.94f));
                    }
                    lip[j] = hs.S(yb, phi) - hs.N(yb, phi) * 0.001f;
                }
                buf.Gloss = 0.02f;
                buf.Grid(Pc, Cc, true, q => new Vector3(q.x, 0f, q.z + 0.015f));
                buf.Strip(Row(Pc, 0), lip, Sh(col2, 0.7f), Sh(col2, 0.6f), true, q => Vector3.down);
                if ((spec.seed & 3) < 2)   // half the beanies have a pompom
                    buf.Ico(hs.S(hs.yTop, 0f) + new Vector3(0f, 0.039f, -0.004f), new Vector3(0.026f, 0.024f, 0.026f), col2, 1, 0.12f, new System.Random(3));
            }

            /// Flat cap (kasket): flat crown tilted forward and overhanging a short stiff visor.
            void Flatcap()
            {
                buf.Reset();
                Color col = spec.hwMain;
                AngCurve bottom = EdgeLine(0.062f, 0.040f, 0.020f);
                float yFlat = hs.yTop + 0.004f;     // crown height at z = 0
                Vector3 Post(Vector3 q, float phi, float s)
                {
                    q.z += 0.022f * Pw(Mathf.Max(0f, Mathf.Cos(phi)), 1.5f) * SS(0.25f, 0.85f, s);   // the crown front overhangs
                    q.x *= 1f + 0.10f * SS(0.3f, 0.9f, s);                                             // crown wider than the band
                    float yp = yFlat - 0.14f * Mathf.Max(0f, q.z);                                       // flat top, tilted down at the front
                    if (q.y > yp) q.y = yp + (q.y - yp) * 0.12f;
                    return q;
                }
                HairCap(bottom.At, (p, s) => 0.0085f + 0.004f * s, col, NL: 40, NS: 9, edge: 0.008f, wallDark: 0.7f, gloss: 0.05f,
                        colFn: (p, s, c) => Mul(Sh(c, 0.94f + 0.10f * s), Stripe(p, 12f) ? 1.04f : 0.96f), post: Post);   // tweed stripes
                const int NV = 9;
                var pin = new Vector3[NV];
                var pout = new Vector3[NV];
                for (int k = 0; k < NV; k++)
                {
                    float u = -1f + 2f * k / (NV - 1), a = 56f * u * D2R, y = Sy(bottom.At(a)) + 0.001f;
                    pin[k] = hs.S(y, a) + hs.N(y, a) * 0.008f;
                    pout[k] = pin[k] + Unit(new Vector3(Mathf.Sin(a) * 0.3f, 0f, 1f)) * (0.034f * (1f - 0.55f * u * u)) + new Vector3(0f, -0.010f * (1f - 0.3f * u * u), 0f);
                }
                Vector3 th = new Vector3(0f, -0.0028f, 0f);
                buf.Gloss = 0.05f;
                buf.Strip(pin, pout, Sh(col, 0.88f), Sh(col, 0.88f), false, q => new Vector3(0f, 1f, 0.25f));
                buf.Strip(Add(pin, th), Add(pout, th), Sh(col, 0.55f), Sh(col, 0.55f), false, q => new Vector3(0f, -1f, -0.25f));
                buf.Strip(pout, Add(pout, th), Sh(col, 0.75f), Sh(col, 0.75f), false, q => Vector3.forward);
                Vector3 button = Post(hs.S(hs.yTop, 0f) + new Vector3(0f, 0.012f, 0f), 0f, 1f);
                buf.Ico(button + new Vector3(0f, 0.001f, 0.004f), new Vector3(0.006f, 0.003f, 0.006f), Sh(col, 0.9f), 1);
            }

            void Baseball()
            {
                buf.Reset();
                Color col = spec.hwMain, col2 = spec.hwAccent;
                AngCurve bottom = EdgeLine(0.056f, 0.032f, 0.010f);
                float Th(float p, float s) { float c = Mathf.Cos(p); return 0.010f + 0.006f * s + (c > 0f ? 0.006f * c * c * SS(0.1f, 0.7f, s) : 0f); }   // raised front panels
                bool Seam(float p) { float d = p * Mathf.Rad2Deg; for (int a = 0; a < 360; a += 60) if (Mathf.Abs(Wrap180(d - a)) < 2.5f) return true; return false; }
                HairCap(bottom.At, Th, col, NS: 9, edge: 0.009f, wallDark: 0.7f, gloss: 0.08f, colFn: (p, s, c) => Seam(p) ? Sh(c, 0.78f) : c);
                buf.Gloss = 0.08f;
                buf.Ico(hs.S(hs.yTop, 0f) + new Vector3(0f, Th(0f, 1f), 0f), new Vector3(0.007f, 0.004f, 0.007f), col, 1);
                // curved bill: three rows (depth 0 / 35 / 68 mm) of nine columns over +-62 deg, ends curling down
                const int NV = 9;
                float[] depth = { 0f, 0.035f, 0.068f };
                var Pv = new Vector3[3, NV];
                var Pu = new Vector3[3, NV];
                for (int r = 0; r < 3; r++)
                    for (int k = 0; k < NV; k++)
                    {
                        float a = (-62f + 124f * k / (NV - 1)) * D2R, y = Sy(bottom.At(a)) + 0.004f, u = 2f * k / (NV - 1) - 1f, dd = depth[r] / 0.068f;
                        Vector3 fwd = Unit(new Vector3(Mathf.Sin(a) * 0.25f, 0f, 1f));
                        Pv[r, k] = hs.S(y, a) + hs.N(y, a) * 0.010f + fwd * (depth[r] * (1f - 0.35f * u * u)) - new Vector3(0f, (0.010f + 0.008f * Mathf.Abs(u)) * dd, 0f);
                        Pu[r, k] = Pv[r, k] + new Vector3(0f, -0.003f, 0f);
                    }
                buf.Grid(Pv, Same(3, NV, col2), false, q => new Vector3(0f, 1f, 0.1f));
                buf.Gloss = 0.05f;
                buf.Grid(Pu, Same(3, NV, Sh(col2, 0.55f)), false, q => new Vector3(0f, -1f, -0.1f));
                buf.Strip(Row(Pv, 2), Row(Pu, 2), Sh(col2, 0.8f), Sh(col2, 0.8f), false, q => Vector3.forward);
            }

            /// Rescue helmet: shell, brim, crest rib, reflective band, chin strap and an optional box-shaped headlamp.
            void Helmet()
            {
                buf.Reset();
                Color col = spec.hwMain, dark = Hx("2A2B2E");
                AngCurve bottom = EdgeLine(0.044f, 0.020f, -0.004f);
                HairCap(bottom.At, (p, s) => 0.020f + 0.006f * s, col, NS: 9, edge: 0.018f, wallDark: 0.6f, gloss: 0.55f, colFn: (p, s, c) => Sh(c, 0.92f + 0.12f * s));
                const int NB = 48;
                var inner = new Vector3[NB];
                var outer = new Vector3[NB];
                var band0 = new Vector3[NB];
                var band1 = new Vector3[NB];
                for (int j = 0; j < NB; j++)
                {
                    float phi = Phi(j, NB), y = Sy(bottom.At(phi)), c = Mathf.Cos(phi);
                    Vector3 bp = hs.S(y, phi) + hs.N(y, phi) * 0.017f;
                    float w = 0.010f + 0.022f * Sq(Mathf.Max(0f, c)) + 0.014f * Sq(Mathf.Max(0f, -c));   // brim wider at the front and back
                    inner[j] = bp;
                    outer[j] = bp + Unit(new Vector3(bp.x, 0f, bp.z + 0.015f)) * w + new Vector3(0f, -0.006f * (w / 0.032f), 0f);
                    band0[j] = hs.S(y + 0.010f, phi) + hs.N(y + 0.010f, phi) * 0.0215f;
                    band1[j] = hs.S(y + 0.020f, phi) + hs.N(y + 0.020f, phi) * 0.0215f;
                }
                Vector3 down = new Vector3(0f, -0.004f, 0f);
                Func<Vector3, Vector3> radial = q => new Vector3(q.x, 0f, q.z + 0.015f);
                buf.Gloss = 0.5f;
                buf.Strip(inner, outer, Sh(col, 0.95f), Sh(col, 0.95f), true, q => Vector3.up);
                buf.Gloss = 0.3f;
                buf.Strip(Add(inner, down), Add(outer, down), Sh(col, 0.55f), Sh(col, 0.55f), true, q => Vector3.down);
                buf.Strip(outer, Add(outer, down), Sh(col, 0.8f), Sh(col, 0.8f), true, radial);
                // crest rib along the midline, from the front over the crown to the back
                const int NR = 15;
                var rp = new Vector3[NR];
                float yF = Sy(0.050f), yB = Sy(0.004f), yT = hs.yTop;
                for (int k = 0; k < NR; k++)
                {
                    float t = k / (float)(NR - 1);
                    bool front = t < 0.5f;
                    float y = front ? yF + (yT - yF) * Mathf.Pow(t / 0.5f, 0.6f) : yT - (yT - yB) * Mathf.Pow((t - 0.5f) / 0.5f, 1.6f);
                    y = Mathf.Min(y, yT - 2e-4f);
                    float phi = front ? 0f : Mathf.PI;
                    rp[k] = hs.S(y, phi) + hs.N(y, phi) * 0.0235f;
                }
                buf.Gloss = 0.55f;
                buf.Tube(rp, Same(NR, 0.0072f), 8, col, true, true);
                buf.Gloss = 0.7f;
                Color bandCol = Hx("D8DCDC");
                buf.Strip(band0, band1, bandCol, bandCol, true, radial);
                buf.Gloss = 0f;
                Vector3 On(float y, float deg, float off) { float phi = deg * D2R; return hs.S(y, phi) + hs.N(y, phi) * off; }
                for (int sg = -1; sg <= 1; sg += 2)
                    buf.Tube(new[] { On(Sy(0.015f), sg * 84f, 0.004f), On(Sy(-0.060f), sg * 70f, 0.004f), On(Sy(-0.104f), sg * 25f, 0.004f),
                                     new Vector3(0f, Sy(-0.111f) - 0.003f, 0.030f) }, Same(4, 0.0035f), 5, dark, false, false);
                if (!spec.headlamp) return;
                // lamp as two flattened square tubes (a round lamp reads as an eye), lens emissive, strap to the sides
                Vector3 p = hs.FacePoint(0f, Sy(0.072f), 0.031f, out Vector3 n), diag = new Vector3(1f, 1f, 0f) / 1.4142f;
                n = Unit(new Vector3(n.x, 0f, n.z));
                buf.Tube(new[] { p - n * 0.012f, p + n * 0.010f }, Same(2, 0.0150f), 4, dark, false, true, Vector3.up, 0.62f, upHint: diag);
                buf.Emit = 0.9f;
                buf.Tube(new[] { p + n * 0.0102f, p + n * 0.0104f }, Same(2, 0.0110f), 4, Hx("FFF4C8"), false, true, Vector3.up, 0.50f, upHint: diag);
                buf.Emit = 0f;
                for (int sg = -1; sg <= 1; sg += 2)
                    buf.Tube(new[] { hs.FacePoint(sg * 0.012f, Sy(0.072f), 0.026f), On(Sy(0.060f), sg * 70f, 0.026f) }, Same(2, 0.003f), 4, dark, false, false);
            }

            /// Reading glasses: two projected rings, bridge and temple arms.
            void Glasses()
            {
                buf.Reset();
                Color col = spec.glassesCol;
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    float cx = sg * f.ex, cy = f.ey - 0.001f;
                    var ring = new Vector3[15];
                    for (int k = 0; k < 14; k++)
                    {
                        float a = TwoPi * k / 14f, sa = Mathf.Sin(a);
                        ring[k] = hs.FacePoint(cx + Mathf.Cos(a) * 0.0225f, cy + sa * 0.0165f * (sa > 0f ? 0.92f : 1.05f), 0.010f);
                    }
                    ring[14] = ring[0];
                    buf.Tube(ring, Same(15, 0.0016f), 5, col, false, false);
                    hs.RingAt(cy + 0.004f, out float cz, out float W, out _);
                    buf.Tube(new[] { hs.FacePoint(sg * (f.ex + 0.0215f), cy + 0.004f, 0.010f), new Vector3(sg * (W + 0.004f), cy + 0.006f, cz + 0.020f),
                                     new Vector3(sg * (W + 0.002f), cy - 0.004f, cz - 0.022f) }, Same(3, 0.0014f), 4, col, false, false);
                }
                buf.Tube(new[] { hs.FacePoint(-f.ex + 0.0215f, f.ey + 0.002f, 0.010f), hs.FacePoint(0f, f.ey + 0.004f, 0.012f), hs.FacePoint(f.ex - 0.0215f, f.ey + 0.002f, 0.010f) },
                         Same(3, 0.0014f), 4, col, false, false);
            }
        }
    }
}
