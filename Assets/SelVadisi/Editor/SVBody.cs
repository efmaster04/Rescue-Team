using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    /// <summary>
    /// Body proportions in metres, measured shod: y = 0 is the shoe sole, +z is the facing direction, +x the character's right.
    /// Same fields and meaning as the prototype's parameter dict (body design spec §2).
    /// </summary>
    public class BodyParams
    {
        public float H, headH, yAnkle, yKnee, yCrotch, yHip, yWaist, yChest, yArmpit, yGH, yNeck, hipX, kneeX, ankleX, ghX, ghZ,
            hipRx, hipZf, hipZb, waistRx, waistZf, waistZb, chestRx, chestZf, chestZb, shTopRx, neckR, Lu, Lf, Lh, arm, leg, belly, bust, butt, pecs, foot, hand;

        /// <summary>Optional override of the top neck ring (ring 13, inside the head) so it fits the head mesh. NaN = prototype rule:
        /// y = chin + .05, centre z = +.006, rx = .93 neckR, rz = .88 (front) / .92 (back) neckR. A set rz is used front and back.</summary>
        public float neckTopY = float.NaN, neckTopZ = float.NaN, neckTopRx = float.NaN, neckTopRz = float.NaN;

        public float Chin => H - headH;

        public BodyParams Clone() => (BodyParams)MemberwiseClone();

        // One preset per row in spec §2 column order: H headH yAnkle yKnee yCrotch yHip yWaist yChest yArmpit yGH yNeck |
        // hipX kneeX ankleX ghX ghZ hipRx hipZf hipZb waistRx waistZf waistZb chestRx chestZf chestZb shTopRx neckR |
        // Lu Lf Lh arm leg belly bust butt pecs foot hand
        static readonly Dictionary<string, float[]> Table = new Dictionary<string, float[]>
        {
            ["AM"] = new[] { 1.76f, .27f, .095f, .478f, .792f, .885f, 1.06f, 1.232f, 1.288f, 1.378f, 1.462f,
                     .09f, .09f, .092f, .184f, -.012f, .176f, .094f, .13f, .15f, .106f, .088f, .174f, .122f, .11f, .168f, .064f,
                     .28f, .245f, .185f, 1.1f, 1.1f, 0f, 0f, 0f, .006f, 1.08f, 1.1f },
            ["AW"] = new[] { 1.64f, .258f, .09f, .448f, .748f, .835f, .99f, 1.142f, 1.188f, 1.274f, 1.356f,
                     .086f, .08f, .082f, .16f, -.01f, .184f, .092f, .128f, .124f, .09f, .082f, .146f, .1f, .09f, .148f, .052f,
                     .262f, .226f, .17f, .92f, 1.04f, .004f, .032f, .012f, 0f, .92f, .95f },
            ["HM"] = new[] { 1.72f, .272f, .095f, .462f, .762f, .858f, 1.036f, 1.21f, 1.256f, 1.342f, 1.42f,
                     .1f, .104f, .106f, .206f, -.015f, .2f, .126f, .138f, .194f, .158f, .116f, .2f, .142f, .122f, .192f, .076f,
                     .276f, .24f, .19f, 1.36f, 1.32f, .04f, .01f, .004f, .004f, 1.12f, 1.18f },
            ["SY"] = new[] { 1.86f, .262f, .095f, .522f, .868f, .966f, 1.15f, 1.326f, 1.382f, 1.474f, 1.57f,
                     .086f, .086f, .088f, .174f, -.01f, .154f, .082f, .106f, .128f, .09f, .082f, .158f, .104f, .092f, .16f, .057f,
                     .302f, .262f, .192f, .92f, .94f, 0f, 0f, 0f, .004f, 1.1f, 1.06f },
            ["EL"] = new[] { 1.66f, .262f, .09f, .455f, .756f, .845f, 1f, 1.16f, 1.204f, 1.29f, 1.372f,
                     .09f, .092f, .094f, .17f, -.004f, .174f, .102f, .114f, .162f, .12f, .096f, .162f, .11f, .108f, .15f, .058f,
                     .272f, .235f, .178f, .98f, .96f, .022f, .01f, 0f, 0f, 1f, 1.05f },
            ["TN"] = new[] { 1.62f, .256f, .09f, .448f, .744f, .832f, .984f, 1.136f, 1.18f, 1.262f, 1.34f,
                     .08f, .08f, .082f, .154f, -.01f, .148f, .08f, .102f, .122f, .084f, .078f, .138f, .092f, .086f, .14f, .051f,
                     .262f, .226f, .172f, .86f, .9f, 0f, 0f, 0f, 0f, 1.02f, 1f },
            ["CH"] = new[] { 1.24f, .25f, .072f, .328f, .515f, .578f, .722f, .832f, .862f, .918f, .968f,
                     .064f, .064f, .066f, .118f, -.006f, .12f, .074f, .084f, .112f, .088f, .07f, .114f, .084f, .074f, .108f, .042f,
                     .18f, .155f, .115f, .74f, .8f, .01f, 0f, 0f, 0f, .74f, .8f },
            ["ELW"] = new[] { 1.56f, .25f, .088f, .425f, .708f, .792f, .935f, 1.07f, 1.122f, 1.205f, 1.288f,
                     .088f, .084f, .086f, .152f, -.004f, .188f, .104f, .126f, .16f, .124f, .096f, .156f, .112f, .1f, .14f, .052f,
                     .252f, .218f, .166f, .96f, 1f, .03f, .034f, .01f, 0f, .9f, .95f },
            ["HW"] = new[] { 1.6f, .256f, .09f, .432f, .725f, .812f, .965f, 1.11f, 1.16f, 1.244f, 1.326f,
                     .094f, .09f, .09f, .17f, -.01f, .208f, .112f, .14f, .17f, .135f, .104f, .17f, .124f, .104f, .158f, .06f,
                     .256f, .222f, .17f, 1.18f, 1.22f, .03f, .04f, .016f, 0f, .96f, 1f },
            ["TG"] = new[] { 1.58f, .252f, .088f, .438f, .728f, .812f, .962f, 1.104f, 1.148f, 1.23f, 1.31f,
                     .08f, .076f, .078f, .146f, -.01f, .16f, .082f, .11f, .114f, .08f, .074f, .13f, .088f, .082f, .134f, .046f,
                     .254f, .218f, .162f, .8f, .92f, 0f, .018f, .008f, 0f, .92f, .9f },
        };

        static BodyParams Row(float[] v)
        {
            int i = 0;   // member initialisers run in order
            return new BodyParams
            {
                H = v[i++], headH = v[i++], yAnkle = v[i++], yKnee = v[i++], yCrotch = v[i++], yHip = v[i++], yWaist = v[i++], yChest = v[i++],
                yArmpit = v[i++], yGH = v[i++], yNeck = v[i++], hipX = v[i++], kneeX = v[i++], ankleX = v[i++], ghX = v[i++], ghZ = v[i++],
                hipRx = v[i++], hipZf = v[i++], hipZb = v[i++], waistRx = v[i++], waistZf = v[i++], waistZb = v[i++], chestRx = v[i++],
                chestZf = v[i++], chestZb = v[i++], shTopRx = v[i++], neckR = v[i++], Lu = v[i++], Lf = v[i++], Lh = v[i++], arm = v[i++],
                leg = v[i++], belly = v[i++], bust = v[i++], butt = v[i++], pecs = v[i++], foot = v[i++], hand = v[i++]
            };
        }

        /// <summary>"AM","AW","HM","SY","EL","TN","CH","ELW","HW","TG" (prototype tables) and "CS" (small child, 3–5 years, ~1.00 m, 4.4 heads).</summary>
        public static BodyParams Preset(string name)
        {
            if (name == "CS")
            {
                // derived from CH: body heights scaled so that yNeck = .754, own widths and lengths
                var c = Preset("CH");
                float f = 0.754f / 0.968f;
                c.H = 1.00f; c.headH = 0.226f;
                c.yAnkle *= f; c.yKnee *= f; c.yCrotch *= f; c.yHip *= f; c.yWaist *= f; c.yChest *= f; c.yArmpit *= f; c.yGH *= f; c.yNeck *= f;
                c.hipX = .055f; c.kneeX = .055f; c.ankleX = .057f; c.ghX = .102f; c.ghZ = -.005f;
                c.hipRx = .106f; c.hipZf = .066f; c.hipZb = .074f; c.waistRx = .100f; c.waistZf = .082f; c.waistZb = .062f;
                c.chestRx = .098f; c.chestZf = .072f; c.chestZb = .064f; c.shTopRx = .092f; c.neckR = .038f;
                c.Lu = .142f; c.Lf = .120f; c.Lh = .092f;
                c.arm = .62f; c.leg = .70f; c.foot = .60f; c.hand = .66f; c.belly = .016f; c.bust = 0f; c.butt = 0f; c.pecs = 0f;
                return c;
            }
            if (!Table.TryGetValue(name, out float[] v)) throw new ArgumentException("Unknown body preset '" + name + "'.");
            return Row(v);
        }

        static readonly FieldInfo[] FloatFields =
            Array.FindAll(typeof(BodyParams).GetFields(BindingFlags.Public | BindingFlags.Instance), f => f.FieldType == typeof(float));

        /// <summary>Linear blend of two presets (every field, as crowd.py); t = 0 gives a, 1 gives b.</summary>
        public static BodyParams Blend(string a, string b, float t)
        {
            BodyParams pa = Preset(a), pb = Preset(b), r = pa.Clone();
            foreach (var f in FloatFields)
            {
                float x = (float)f.GetValue(pa), y = (float)f.GetValue(pb);
                f.SetValue(r, x + (y - x) * t);
            }
            return r;
        }

        /// <summary>Per-person variation around the preset (crowd.py jitter, same draw order); returns a new instance.</summary>
        public BodyParams Jitter(System.Random r)
        {
            float U(float a, float b) => a + (b - a) * (float)r.NextDouble();
            var q = Clone();
            float hs = 1f + U(-0.035f, 0.035f);                                   // overall height
            q.H = H * hs; q.yAnkle = yAnkle * hs; q.yKnee = yKnee * hs; q.yCrotch = yCrotch * hs; q.yHip = yHip * hs; q.yWaist = yWaist * hs;
            q.yChest = yChest * hs; q.yArmpit = yArmpit * hs; q.yGH = yGH * hs; q.yNeck = yNeck * hs; q.Lu = Lu * hs; q.Lf = Lf * hs; q.Lh = Lh * hs;
            float dy = U(-0.015f, 0.015f) * yHip;                                 // leg length ratio: shift everything above the hip
            q.yKnee += 0.5f * dy;
            q.yCrotch += dy; q.yHip += dy; q.yWaist += dy; q.yChest += dy; q.yArmpit += dy; q.yGH += dy; q.yNeck += dy; q.H += dy;
            float w = 1f + U(-0.05f, 0.06f);                                      // girth
            q.hipRx = hipRx * w; q.hipZf = hipZf * w; q.hipZb = hipZb * w; q.waistRx = waistRx * w; q.waistZf = waistZf * w; q.waistZb = waistZb * w;
            q.chestRx = chestRx * w; q.chestZf = chestZf * w; q.chestZb = chestZb * w;
            float sh = 1f + U(-0.04f, 0.04f);                                     // shoulder width
            q.ghX = ghX * sh; q.shTopRx = shTopRx * sh;
            q.waistZf += U(0f, 0.015f) * (H > 1.4f ? 1f : 0.4f);                  // a bit of belly
            q.arm = arm * (1f + U(-0.06f, 0.06f)); q.leg = leg * (1f + U(-0.06f, 0.06f));
            q.headH = headH * (1f + U(-0.03f, 0.03f));
            return q;
        }
    }

    public enum GarmentKind { Stockings, Shirt, Blouse, Tee, Jeans, Trousers, Shorts, Salvar, Skirt, CoatTail, Cardigan, Sweater, Vest, Hoodie, Jacket, Raincoat, Puffer, HiVisVest, Coverall, Blanket }

    public enum Collar { None, Crew, Rib, Stand, Shirt, Turtle }

    /// <summary>One clothing layer. The outermost covering garment (largest t) gives the surface offset and colour (spec §8).</summary>
    public class Garment
    {
        public GarmentKind kind; public Color32 col; public float t = 0.006f;
        public float y0 = float.NaN, y1 = float.NaN;          // torso coverage, absolute y (NaN = not on the torso; y1 NaN = up to the top)
        public float sleeve;                                    // arm coverage measured from GH along the arm (0 = sleeveless)
        public float leg = float.NaN;                           // legs covered from the crotch down to this absolute y
        public float quilt, pitch = 0.085f;                     // puffer channels
        public Color32? cuff, band;                             // cuff colour (last 0.035 m of the sleeve), hem band colour
        public float flare, straight;                           // skirt flare; trouser-leg straightness 0..1
        public float hem = float.NaN;                           // Skirt / CoatTail hem y (separate bell loft, like build_skirt)
        public List<(float y0, float y1, Color32 c)> stripes = new List<(float, float, Color32)>();     // torso colour bands (absolute y)
        public List<(float s0, float s1, Color32 c)> armStripes = new List<(float, float, Color32)>();  // sleeve bands (distance from GH)
        public List<(float y0, float y1, Color32 c)> legStripes = new List<(float, float, Color32)>();  // trouser bands (absolute y)
        public float gloss;                                     // uv0.x for this garment
        public float emitStripes;                               // emission (uv0.y) for stripe colours (reflective tape ~0.25)
        public bool zip;                                        // front zip strip |x|<0.008 raised 0.004, Shade 0.6 (G1)
        public bool placket; public int studs;                  // raincoat front flap 0.05 wide raised 0.006 + press-stud discs (G2)
        public int buttons;                                     // vertical button row (vest / cardigan / shirt) (G4/G5)
        public bool openFront; public float gapTop = 0.02f, gapBottom = 0.10f; public Color32 inner;   // open front shows 'inner' (G5)
        public float vNeckBottom = float.NaN;                   // V-neck opening down to this y showing 'inner'
        public Collar collar;                                   // neckline treatment at the garment's top
        public bool hipPockets, chestPockets, kangaroo;         // flap pockets / hoodie pocket (proud patches)
        public bool hoodDown;                                   // hood resting behind the neck
        public bool belt; public Color32 beltCol;               // 0.035 belt at the top of the trousers (+0.003) with a buckle
        public float yoke = float.NaN;                          // raincoat cape band from this y to the shoulder, +0.008 extra (G2)
    }

    public class BodyOutfit
    {
        public Color32 skin; public List<Garment> garments = new List<Garment>();
        public string shoe = "sneaker";                         // "sneaker","leather","rainboot","workboot","galosh","slipper"
        public Color32? shoeCol, soleCol, sockCol;              // sockCol: visible wool sock above the shoe
        public float bootTop = float.NaN;                       // override the boot shaft height
        public bool wetLegs;                                    // lower legs lerp toward 6B5038 (wardrobe rule 6)
        public bool gloves; public Color32 gloveCol;            // hands and thumb in glove colour, gloss 0.3
    }

    public class BodyResult
    {
        /// <summary>Rest positions (identity rotations, arms hanging) in root space for every body bone; Eyes and Mouth stay zero.</summary>
        public Vector3[] bonePos = new Vector3[SVSkel.Count];
        /// <summary>Root-space IK landmarks on the outer surface (bind = rest for the torso).</summary>
        public Vector3 sternum, belly, smallOfBack, hipSideL, hipSideR, pocketL, pocketR;
        /// <summary>Range of vertices this call appended: [vertexStart, vertexEnd).</summary>
        public int vertexStart, vertexEnd;
    }

    /// <summary>
    /// Lofted, skinned villager body (body design spec §0–§10), ported from the validated Python prototype
    /// (bodyproto/body.py, body2.py). The mesh is generated in the A-pose bind: upper arms rotated ±45° about GH.
    /// </summary>
    public static class SVBody
    {
        /// <summary>Appends the whole body except the head. Needs mb.W != null and the MB matrix stack at identity.</summary>
        public static BodyResult Build(MB mb, BodyParams p, BodyOutfit o)
        {
            if (mb == null || mb.W == null) throw new ArgumentException("SVBody.Build needs an MB with skin weights (mb.W != null).");
            if (mb.Matrix != Matrix4x4.identity) throw new ArgumentException("SVBody.Build needs the MB matrix stack at identity.");
            float gloss = mb.Gloss, emit = mb.Emit, wind = mb.Wind;
            var windFn = mb.WindFn;
            var weight = mb.Weight;
            mb.Wind = 0f; mb.WindFn = null;                    // body vertices never sway
            try { return new Builder(mb, p, o).Run(); }
            finally { mb.Gloss = gloss; mb.Emit = emit; mb.Wind = wind; mb.WindFn = windFn; mb.Weight = weight; }
        }

        /// <summary>A-pose bind rotation: UpArmR Euler(0,0,+45), UpArmL (0,0,−45), ShHlpR/L ±22.5, every other bone identity.</summary>
        public static Quaternion BindLocalRotation(int bone)
        {
            float deg = bone == SVSkel.UpArmR ? SVSkel.APose : bone == SVSkel.UpArmL ? -SVSkel.APose
                      : bone == SVSkel.ShHlpR ? 0.5f * SVSkel.APose : bone == SVSkel.ShHlpL ? -0.5f * SVSkel.APose : 0f;
            float h = 0.5f * deg * Mathf.Deg2Rad;
            return new Quaternion(0f, 0f, Mathf.Sin(h), Mathf.Cos(h));     // = Quaternion.Euler(0, 0, deg)
        }

        /// <summary>Spec §0 "Normals": vertices are welded by position (round(p / 1e-5)), area-weighted face normals are summed per
        /// weld group and every duplicate gets the group normal, so colour seams do not crease the shading.</summary>
        public static Vector3[] WeldedNormals(IList<Vector3> V, IList<int> T)
        {
            int n = V.Count;
            var group = new int[n];
            var ids = new Dictionary<(long, long, long), int>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 v = V[i];
                var key = ((long)Math.Round(v.x / 1e-5), (long)Math.Round(v.y / 1e-5), (long)Math.Round(v.z / 1e-5));
                if (!ids.TryGetValue(key, out int g)) { g = ids.Count; ids.Add(key, g); }
                group[i] = g;
            }
            var sum = new Vector3[ids.Count];
            for (int t = 0; t + 2 < T.Count; t += 3)
            {
                int a = T[t], b = T[t + 1], c = T[t + 2];
                Vector3 fn = Vector3.Cross(V[b] - V[a], V[c] - V[a]);      // |fn| = 2 * area
                sum[group[a]] += fn; sum[group[b]] += fn; sum[group[c]] += fn;
            }
            var N = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 s = Unit(sum[group[i]]);
                N[i] = s.sqrMagnitude > 0f ? s : Vector3.up;               // unreferenced vertices (the 6 arm-hole leftovers)
            }
            return N;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Shared maths (prototype helpers)
        // ---------------------------------------------------------------------------------------------------------------
        const float Deg = Mathf.Deg2Rad;

        static float SS(float a, float b, float x)               // smoothstep; a > b runs backwards, as in the prototype
        {
            if (a == b) return x >= b ? 1f : 0f;
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
        static float Se(float c, float e) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);   // superellipse term sign(c)|c|^e
        static float Lerp(float a, float b, float t) => a + (b - a) * t;                     // unclamped
        static float Wrap(float a) => a - 2f * Mathf.PI * Mathf.Floor((a + Mathf.PI) / (2f * Mathf.PI));   // to [-pi, pi)
        static float G(float a, float w) => Mathf.Exp(-(a / w) * (a / w));
        static Vector3 Unit(Vector3 v) { float m = v.magnitude; return m > 1e-12f ? v / m : Vector3.zero; }   // Vector3.normalized zeroes |v| < 1e-5
        static float RadXZ(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);
        static Color32 Rgb(int hex) => new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);

        // Catmull-Rom between table rows, clamped to ±25% of the neighbour span (spec §0 "Between table rows").
        static float Cr(float a, float b, float c, float d, float u)
        {
            float v = 0.5f * (2f * b + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u * u + (-a + 3f * b - 3f * c + d) * u * u * u);
            float lo = Mathf.Min(b, c), hi = Mathf.Max(b, c), m = 0.25f * (hi - lo);
            return Mathf.Clamp(v, lo - m, hi + m);
        }

        static float[] Unwrap(float[] a)                         // ang_unwrap_monotone
        {
            var r = new float[a.Length];
            r[0] = a[0];
            for (int j = 1; j < a.Length; j++) r[j] = r[j - 1] + Wrap(a[j] - r[j - 1]);
            return r;
        }

        /// Junction angles blended toward uniform spacing by t (spec §4 "Angle matching").
        static float[] Blended(float[] phi0, float t)
        {
            int m = phi0.Length;
            float dir = phi0[m - 1] > phi0[0] ? 1f : -1f, sx = 0f, cx = 0f;
            for (int j = 0; j < m; j++)
            {
                float d = phi0[j] - dir * 2f * Mathf.PI * j / m;
                sx += Mathf.Sin(d); cx += Mathf.Cos(d);
            }
            float off = Mathf.Atan2(sx, cx);                     // circular mean of phi_j - uniform_j
            var r = new float[m];
            for (int j = 0; j < m; j++) r[j] = phi0[j] + Wrap(dir * 2f * Mathf.PI * j / m + off - phi0[j]) * t;
            return r;
        }

        static BoneWeight Wt(int a) => new BoneWeight { boneIndex0 = a, weight0 = 1f };
        static BoneWeight Wt(int a, int b, float tb) => Wt(a, 1f - tb, b, tb);
        /// Two-bone weight, normalised, heavier bone first; a negligible second bone is dropped.
        static BoneWeight Wt(int a, float wa, int b, float wb)
        {
            float s = wa + wb;
            wa /= s; wb /= s;
            if (wb > wa) { (a, b) = (b, a); (wa, wb) = (wb, wa); }
            if (a == b || wb < 1e-4f) return Wt(a);
            return new BoneWeight { boneIndex0 = a, weight0 = wa, boneIndex1 = b, weight1 = wb };
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Ring tables (spec §4–§6)
        // ---------------------------------------------------------------------------------------------------------------
        // limb rows: u|f|h|v|w, rLat, rMed, rFront, rBack, n, offLat (hand: curl toward the palm), offFront
        static readonly float[,] UpperRows =
        {
            { .30f, .058f, .050f, .054f, .055f, 2.2f, .004f, 0f }, { .48f, .050f, .046f, .052f, .051f, 2.2f, .002f, 0f },
            { .66f, .046f, .045f, .053f, .049f, 2.2f, 0f, 0f }, { .84f, .042f, .042f, .046f, .046f, 2.2f, 0f, 0f },
            { 1.00f, .040f, .041f, .039f, .045f, 2.2f, 0f, -.002f },
        };
        static readonly float[,] ForeRows =
        {
            { .14f, .044f, .042f, .042f, .042f, 2.2f, 0f, 0f }, { .34f, .044f, .040f, .041f, .039f, 2.2f, .001f, 0f },
            { .60f, .036f, .033f, .035f, .033f, 2.3f, 0f, 0f }, { .86f, .026f, .024f, .030f, .029f, 2.4f, 0f, 0f },
            { 1.00f, .022f, .021f, .030f, .029f, 2.4f, 0f, 0f },
        };
        // hand (mitten): lateral = back of the hand, medial = palm, front = thumb side, back = little-finger side
        static readonly float[,] HandRows =
        {
            { .10f, .021f, .021f, .036f, .035f, 2.4f, 0f, .002f }, { .30f, .021f, .019f, .046f, .044f, 2.8f, 0f, .003f },
            { .50f, .018f, .017f, .047f, .045f, 3.0f, .002f, .002f }, { .66f, .016f, .015f, .044f, .042f, 3.0f, .010f, 0f },
            { .80f, .015f, .014f, .040f, .038f, 2.8f, .022f, -.001f }, { .92f, .012f, .011f, .031f, .029f, 2.4f, .034f, -.002f },
        };
        const float TipH = .975f, TipCurl = .040f, TipFront = -.002f;
        // first thigh row (v < 0) sits at y = yCrotch - .035k
        static readonly float[,] ThighRows =
        {
            { -1f, .088f, .078f, .088f, .090f, 2.1f, .006f, 0f }, { .40f, .082f, .073f, .082f, .080f, 2.1f, .004f, 0f },
            { .60f, .075f, .067f, .075f, .071f, 2.1f, .002f, 0f }, { .78f, .066f, .061f, .066f, .061f, 2.1f, 0f, .002f },
            { .92f, .058f, .058f, .061f, .055f, 2.1f, 0f, .003f }, { 1.00f, .053f, .055f, .058f, .051f, 2.1f, 0f, .004f },
        };
        static readonly float[,] ShinRows =
        {
            { .08f, .051f, .053f, .050f, .056f, 2.1f, 0f, 0f }, { .24f, .052f, .056f, .046f, .066f, 2.1f, 0f, -.004f },
            { .45f, .046f, .049f, .041f, .056f, 2.1f, 0f, -.003f }, { .68f, .038f, .039f, .036f, .042f, 2.1f, 0f, 0f },
            { .86f, .034f, .035f, .034f, .036f, 2.1f, 0f, 0f }, { 1.02f, .035f, .036f, .040f, .040f, 2.1f, 0f, .004f },
        };

        sealed class ShoeSpec
        {
            public float[,] prof;            // rings heel to toe: z, halfWidth, topY, bottomY (× foot)
            public float sole, gloss;        // sole height (× foot), uv0.x
            public Color32 col, soleCol;
            public float y1, r0, r1, lip;    // boot shaft (y1 = 0: none); y1 is scaled by k^0.7
            public bool open;                // slipper: only the sole and an instep strap, the sock or foot shows elsewhere
        }

        static readonly float[,] GaloshProfile =
        {
            { -.064f, .026f, .050f, .006f }, { -.054f, .036f, .066f, 0f }, { -.030f, .042f, .072f, 0f }, { .010f, .044f, .074f, 0f },
            { .050f, .047f, .072f, 0f }, { .095f, .050f, .064f, 0f }, { .140f, .051f, .054f, .002f }, { .178f, .046f, .048f, .006f },
            { .202f, .036f, .042f, .011f }, { .218f, .020f, .036f, .016f },
        };

        static readonly Dictionary<string, ShoeSpec> Shoes = new Dictionary<string, ShoeSpec>
        {
            ["sneaker"] = new ShoeSpec
            {
                prof = new[,] { { -.070f, .028f, .062f, .006f }, { -.060f, .040f, .092f, 0f }, { -.032f, .046f, .104f, 0f }, { .010f, .048f, .104f, 0f },
                                { .052f, .051f, .090f, 0f }, { .096f, .055f, .074f, 0f }, { .140f, .057f, .064f, .002f }, { .178f, .053f, .058f, .007f },
                                { .204f, .043f, .052f, .014f }, { .222f, .026f, .044f, .022f } },
                sole = .024f, soleCol = Rgb(0xE8E4DA), col = Rgb(0x3B6FD1), gloss = .15f,
            },
            ["leather"] = new ShoeSpec
            {
                prof = new[,] { { -.066f, .026f, .058f, .010f }, { -.056f, .037f, .084f, .006f }, { -.030f, .043f, .094f, .006f }, { .010f, .045f, .094f, 0f },
                                { .050f, .048f, .080f, 0f }, { .095f, .051f, .064f, 0f }, { .140f, .052f, .052f, .002f }, { .180f, .047f, .046f, .006f },
                                { .206f, .036f, .040f, .011f }, { .222f, .018f, .034f, .016f } },
                sole = .016f, soleCol = Rgb(0x2A2420), col = Rgb(0x3B2E26), gloss = .35f,
            },
            ["rainboot"] = new ShoeSpec
            {
                prof = new[,] { { -.072f, .030f, .070f, .006f }, { -.062f, .044f, .100f, 0f }, { -.034f, .050f, .110f, 0f }, { .010f, .052f, .108f, 0f },
                                { .052f, .054f, .092f, 0f }, { .096f, .057f, .078f, 0f }, { .140f, .058f, .068f, .002f }, { .180f, .054f, .062f, .008f },
                                { .206f, .044f, .056f, .015f }, { .224f, .026f, .048f, .022f } },
                sole = .020f, soleCol = Rgb(0x1A1A1C), col = Rgb(0x2F4A35), gloss = .55f, y1 = .33f, r0 = .060f, r1 = .066f, lip = .006f,
            },
            ["workboot"] = new ShoeSpec
            {
                prof = new[,] { { -.074f, .032f, .070f, .010f }, { -.064f, .046f, .104f, 0f }, { -.034f, .052f, .120f, 0f }, { .010f, .054f, .118f, 0f },
                                { .052f, .056f, .100f, 0f }, { .096f, .059f, .084f, 0f }, { .140f, .060f, .074f, .002f }, { .180f, .057f, .068f, .008f },
                                { .208f, .047f, .062f, .016f }, { .226f, .028f, .054f, .024f } },
                sole = .034f, soleCol = Rgb(0x1E1E1E), col = Rgb(0x4A3A2C), gloss = .30f, y1 = .165f, r0 = .058f, r1 = .060f, lip = .004f,
            },
            ["galosh"] = new ShoeSpec { prof = GaloshProfile, sole = .012f, soleCol = Rgb(0x141414), col = Rgb(0x1F1F21), gloss = .55f },
            // slipper (G16): galosh-shaped sole and instep strap in the slipper colour, the rest shows the sock or the foot
            ["slipper"] = new ShoeSpec { prof = GaloshProfile, sole = .020f, soleCol = Rgb(0x2A3E60), col = Rgb(0x3A5A8A), gloss = .05f, open = true },
        };

        struct TRow { public float y, rx, zf, zb, n, cz, tilt; }                       // torso ring (spec §3 table)
        struct LRow { public float key, cx, cz, rl, rm, rf, rb, n, ol, of; }          // limb ring: key = s (arm) or -y (leg); cx/cz legs only

        static LRow InterpL(List<LRow> r, float t)
        {
            int n = r.Count;
            if (t <= r[0].key) { LRow x = r[0]; x.key = t; return x; }
            if (t >= r[n - 1].key) { LRow x = r[n - 1]; x.key = t; return x; }
            int i = 0;
            while (i < n - 2 && r[i + 1].key <= t) i++;
            float u = (t - r[i].key) / (r[i + 1].key - r[i].key);
            LRow a = r[Mathf.Max(i - 1, 0)], b = r[i], c = r[i + 1], d = r[Mathf.Min(i + 2, n - 1)];
            return new LRow
            {
                key = t, cx = Cr(a.cx, b.cx, c.cx, d.cx, u), cz = Cr(a.cz, b.cz, c.cz, d.cz, u),
                rl = Cr(a.rl, b.rl, c.rl, d.rl, u), rm = Cr(a.rm, b.rm, c.rm, d.rm, u), rf = Cr(a.rf, b.rf, c.rf, d.rf, u),
                rb = Cr(a.rb, b.rb, c.rb, d.rb, u), n = Cr(a.n, b.n, c.n, d.n, u), ol = Cr(a.ol, b.ol, c.ol, d.ol, u), of = Cr(a.of, b.of, c.of, d.of, u),
            };
        }

        /// Limb ring point in the limb frame: x = lateral, y = front (spec §0 "Limb ring point").
        static Vector2 LimbLocal(float rl, float rm, float rf, float rb, float n, float ol, float of, float ph)
        {
            float c = Mathf.Cos(ph), s = Mathf.Sin(ph), e = 2f / n;
            return new Vector2(ol + (c >= 0f ? rl : rm) * Se(c, e), of + (s >= 0f ? rf : rb) * Se(s, e));
        }

        struct Look
        {
            public Color32 c; public float gloss, emit;
            public Look(Color32 c, float gloss, float emit = 0f) { this.c = c; this.gloss = gloss; this.emit = emit; }
            public bool Same(Look o) => c.r == o.c.r && c.g == o.c.g && c.b == o.c.b && gloss == o.gloss && emit == o.emit;
        }

        /// One garment surface over a parameter range: torso and legs use y, arms use s (distance from GH).
        sealed class Layer
        {
            public Garment g;                                    // owner (null for the sock)
            public bool main;                                    // the garment's own torso layer (not a yoke / fold / belt)
            public float lo = float.NegativeInfinity, hi = float.PositiveInfinity;
            public float t, quilt, pitch = 1f, q0;               // offset; quilting phase u = frac((q - q0) / pitch)
            public float standFrom = float.PositiveInfinity;     // stand collar: eases outward above this y
            public float ribFrom = float.PositiveInfinity;       // rib band: alternate column shades above this y
            public float gLo, gHi;                               // the garment's torso range (front opening widths)
            public int order;                                    // position of the garment in the outfit list (layer rule)
            public Color32 col; public float gloss;
            public readonly List<(float a, float b, Color32 c, float emit)> bands = new List<(float, float, Color32, float)>();

            public bool Covers(float q) => q >= lo - 1e-6f && q <= hi + 1e-6f;

            public float Offset(float q)
            {
                float off = t;
                if (quilt > 0f)
                {
                    float u = (q - q0) / pitch;
                    off += quilt * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (u - Mathf.Floor(u)))), 0.7f);
                }
                if (q > standFrom) off += 0.006f * SS(standFrom, standFrom + 0.04f, q);
                return off;
            }

            public (Color32 c, float emit) ColourAt(float q)
            {
                foreach (var b in bands) if (q >= b.a - 1e-5f && q <= b.b + 1e-5f) return (b.c, b.emit);
                return (col, 0f);
            }
        }

        sealed class Skirt
        {
            public Garment g;
            public int order;         // position in the outfit list
            public float yTop, yHem;
            public TRow hip;          // torso ring just below the hip where the bell starts to flare
            public Vector3 hemR;      // hem radii (rx, zf, zb)
            public bool Covers(float y) => y >= yHem && y <= yTop;
        }

        sealed class RingData
        {
            public readonly Vector3[] p; public readonly Look[] look; public readonly BoneWeight[] w;
            public Layer layer;       // surface the positions lie on (null = skin)
            public float edge;        // torso: front opening edge angle (columns 1 and 19), 0 = closed
            public float innerOff;    // torso: offset of the layer the opening shows (columns 19, 0, 1)
            public RingData(int n) { p = new Vector3[n]; look = new Look[n]; w = new BoneWeight[n]; }
        }

        sealed class Ring
        {
            public int[] v;           // vertex indices
            public RingData d;
            public bool skipNext;     // no faces to the next ring (duplicate ring at a colour seam or hem step)
            public bool bandNext;     // the faces to the next ring form a hem step band
            public float param;       // torso: table index (salvar extension < 0); arm: s; leg: y
            public TRow rg;           // torso rings: interpolated table row
            public Layer Layer => d?.layer;
        }

        // =================================================================================================================
        sealed class Builder
        {
            const int NT = 20, NA = 12, NL = 12;     // torso ring, arm ring (= hole loop) and leg ring (= split loop) sizes
            const float Eps = 1e-4f;                  // parameter step used to read the layer below / above a boundary
            const float SkinGloss = .12f;
            const float VNeckTop = .09f;              // V-neck opening width at the garment top (× k)

            readonly MB mb; readonly BodyParams p; readonly BodyOutfit o;
            readonly float k, yST, chin, sW, ySplit, gloveStart, shaftTop;
            readonly Look skin, glove;
            readonly Garment salvar;
            readonly ShoeSpec shoe;
            readonly List<TRow> rows;
            readonly List<Layer> torsoL = new List<Layer>(), armL = new List<Layer>(), legL = new List<Layer>();
            readonly List<Skirt> skirts = new List<Skirt>();
            readonly List<Ring> tor = new List<Ring>();
            readonly BodyResult res = new BodyResult();
            int iA, iU, iS;                            // positions in 'tor' of table rows 6, 7, 8 (the arm-hole rows)

            public Builder(MB mb, BodyParams p, BodyOutfit o)
            {
                this.mb = mb; this.p = p; this.o = o;
                k = p.H / 1.76f;
                yST = p.yGH + 0.032f * k;
                chin = p.Chin;
                sW = p.Lu + p.Lf;
                skin = new Look(o.skin.a == 0 ? Rgb(0xE0AC88) : o.skin, SkinGloss);
                glove = new Look(o.gloveCol.a == 0 ? Rgb(0x2E2E30) : o.gloveCol, .3f);
                gloveStart = sW - 0.03f;                                   // a short gauntlet under the cuff
                salvar = o.garments.Find(g => g.kind == GarmentKind.Salvar && !float.IsNaN(g.leg) && !float.IsNaN(g.y0));
                ySplit = p.yKnee + 0.10f * k;
                if (!Shoes.TryGetValue(o.shoe ?? "sneaker", out shoe)) throw new ArgumentException("Unknown shoe '" + o.shoe + "'.");
                shaftTop = shoe.y1 <= 0f ? 0f : float.IsNaN(o.bootTop) ? shoe.y1 * Mathf.Pow(k, 0.7f) : o.bootTop;
                rows = TorsoRows();
                MakeLayers();
            }

            public BodyResult Run()
            {
                res.vertexStart = mb.Count;
                BuildTorso();
                BuildArm(1); BuildArm(-1);
                int crotch = Crotch();
                int legStart = mb.Count;
                BuildLeg(1, crotch); BuildLeg(-1, crotch);
                foreach (var s in skirts) BuildSkirt(s);
                int legEnd = mb.Count;
                foreach (var g in o.garments) Decorate(g);
                if (o.wetLegs) WetLegs(legStart, legEnd);
                BakeAO();
                res.vertexEnd = mb.Count;
                Skeleton();
                Landmarks();
                return res;
            }

            // ---------------------------------------------------------------------------------------------------------
            // Mesh primitives (prototype MB.quad / strip / fan orientation rules)
            // ---------------------------------------------------------------------------------------------------------
            Vector3 P(int i) => mb.V[i];

            int V(Vector3 pos, Look l, BoneWeight w)
            {
                mb.Gloss = l.gloss; mb.Emit = l.emit; mb.Weight = w;
                return mb.Vert(pos, l.c);
            }

            /// True when quad a-b-c-d has to be wound (a,c,b),(a,d,c) so that its normal points away from 'inside'.
            bool Flip(int a, int b, int c, int d, Vector3 inside)
            {
                Vector3 pa = P(a), pb = P(b), pc = P(c), pd = P(d);
                return Vector3.Dot(Vector3.Cross(pc - pa, pd - pb), (pa + pb + pc + pd) * 0.25f - inside) < 0f;
            }

            void Quad(int a, int b, int c, int d, bool flip)
            {
                if (flip) { mb.Tri(a, c, b); mb.Tri(a, d, c); } else { mb.Tri(a, b, c); mb.Tri(a, c, d); }
            }

            void Quad(int a, int b, int c, int d, Vector3 inside) => Quad(a, b, c, d, Flip(a, b, c, d, inside));

            void Strip(int[] A, int[] B, Vector3 inside)
            {
                for (int j = 0; j < A.Length; j++)
                {
                    int j1 = (j + 1) % A.Length;
                    Quad(A[j], A[j1], B[j1], B[j], inside);
                }
            }

            /// Cap fan to an apex, facing from the ring centroid toward the apex.
            void Fan(int[] A, int apex)
            {
                Vector3 outDir = P(apex) - Centroid(A);
                for (int j = 0; j < A.Length; j++)
                {
                    int a = A[j], b = A[(j + 1) % A.Length];
                    Vector3 nn = Vector3.Cross(P(b) - P(a), P(apex) - P(a));
                    if (Vector3.Dot(nn, outDir) >= 0f) mb.Tri(a, b, apex); else mb.Tri(a, apex, b);
                }
            }

            /// Open quad grid (rows × columns) wound consistently, with its middle quad facing along 'outward'.
            void Sheet(int[,] g, Vector3 outward)
            {
                int nr = g.GetLength(0), nc = g.GetLength(1), i0 = (nr - 1) / 2, c0 = (nc - 1) / 2;
                Vector3 n = Vector3.Cross(P(g[i0 + 1, c0 + 1]) - P(g[i0, c0]), P(g[i0 + 1, c0]) - P(g[i0, c0 + 1]));
                bool flip = Vector3.Dot(n, outward) < 0f;
                for (int i = 0; i + 1 < nr; i++)
                    for (int c = 0; c + 1 < nc; c++)
                    {
                        int a = g[i, c], b = g[i, c + 1], cc = g[i + 1, c + 1], d = g[i + 1, c];
                        if (flip) { mb.Tri(a, cc, b); mb.Tri(a, d, cc); } else { mb.Tri(a, b, cc); mb.Tri(a, cc, d); }
                    }
            }

            Vector3 Centroid(int[] A)
            {
                Vector3 c = Vector3.zero;
                foreach (int i in A) c += P(i);
                return c / A.Length;
            }

            Vector3 Centroid(int[] A, int[] B) => (Centroid(A) * A.Length + Centroid(B) * B.Length) / (A.Length + B.Length);

            /// Mean distance of a ring's points from their centroid (which of two rings at one parameter is the outer layer).
            static float Spread(Vector3[] A)
            {
                Vector3 c = Vector3.zero;
                foreach (var v in A) c += v;
                c /= A.Length;
                float s = 0f;
                foreach (var v in A) s += (v - c).magnitude;
                return s / A.Length;
            }

            // ---------------------------------------------------------------------------------------------------------
            // Ring chains with hem steps and colour seams (spec §8 "Hem step", "Colour-only boundary")
            // ---------------------------------------------------------------------------------------------------------
            Ring Push(List<Ring> chain, RingData d, float param, TRow rg = default)
            {
                var v = new int[d.p.Length];
                for (int i = 0; i < v.Length; i++) v[i] = V(d.p[i], d.look[i], d.w[i]);
                var r = new Ring { v = v, d = d, param = param, rg = rg };
                chain.Add(r);
                return r;
            }

            /// Ends the chain's last ring against 'next' (same parameter, the layer on the far side of a boundary). Equal rings add
            /// nothing; a look-only change adds a duplicate ring with no faces in between; a radius change adds a 3-ring step whose
            /// band is coloured like the outer layer: …→outer, band, duplicate (inner radius, outer colour), no faces, inner→…
            void Step(List<Ring> chain, RingData next, float param, TRow rg = default)
            {
                Ring last = chain[chain.Count - 1];
                RingData cur = last.d;
                bool samePos = true, sameLook = true;
                for (int i = 0; i < cur.p.Length; i++)
                {
                    if ((cur.p[i] - next.p[i]).sqrMagnitude > 1e-12f) samePos = false;
                    if (!cur.look[i].Same(next.look[i])) sameLook = false;
                }
                if (samePos)
                {
                    if (sameLook) return;
                    last.skipNext = true;
                    Push(chain, next, param, rg);
                    return;
                }
                if (Spread(cur.p) > Spread(next.p))
                {
                    last.bandNext = true;
                    Push(chain, Mix(next, cur), param, rg).skipNext = true;
                }
                else
                {
                    last.skipNext = true;
                    Push(chain, Mix(cur, next), param, rg).bandNext = true;
                }
                Push(chain, next, param, rg);
            }

            static RingData Mix(RingData pos, RingData look)
            {
                var d = new RingData(pos.p.Length) { layer = pos.layer };
                for (int i = 0; i < pos.p.Length; i++) { d.p[i] = pos.p[i]; d.w[i] = pos.w[i]; d.look[i] = look.look[i]; }
                return d;
            }

            /// Faces between consecutive rings. Regular strips face away from the centroid of their two rings (prototype rule),
            /// decided once for the whole chain by majority: every ring lists its vertices in the same angular order and the chain
            /// runs one way, so one winding is right for all its quads. Single quads where the centroid test is ill-conditioned
            /// (the steep wall beside a wide front opening, an armpit folded by a thick layer on a small body) can then no longer
            /// flip against their neighbours. A hem step band uses the same winding: the step continues the surface's orientation,
            /// which faces jacket and trouser hems down, necklines up and cuffs toward the hand like the spec's "inside point 3 cm
            /// toward the bigger ring", and stays right on skewed loops such as a vest's arm hole.
            /// 'omit(strip, column)' drops quads (the torso arm holes).
            void Stitch(List<Ring> chain, Func<int, int, bool> omit = null)
            {
                int n = chain.Count - 1, votes = 0;
                for (int i = 0; i < n; i++)
                {
                    Ring a = chain[i], b = chain[i + 1];
                    if (a.skipNext || a.bandNext) continue;
                    Vector3 ins = Centroid(a.v, b.v);
                    int m = a.v.Length;
                    for (int j = 0; j < m; j++)
                        if (omit == null || !omit(i, j)) votes += Flip(a.v[j], a.v[(j + 1) % m], b.v[(j + 1) % m], b.v[j], ins) ? 1 : -1;
                }
                bool f = votes > 0;
                for (int i = 0; i < n; i++)
                {
                    Ring a = chain[i], b = chain[i + 1];
                    if (a.skipNext) continue;
                    int m = a.v.Length;
                    for (int j = 0; j < m; j++)
                    {
                        int j1 = (j + 1) % m, qa = a.v[j], qb = a.v[j1], qc = b.v[j1], qd = b.v[j];
                        if (omit != null && omit(i, j)) continue;
                        // band quads on columns a front opening did not move collapse: drop the zero-area halves
                        bool d0 = a.bandNext && (P(qa) - P(qd)).sqrMagnitude < 1e-12f, d1 = a.bandNext && (P(qb) - P(qc)).sqrMagnitude < 1e-12f;
                        if (d0 && d1) continue;
                        if (d0) { if (f) mb.Tri(qa, qc, qb); else mb.Tri(qa, qb, qc); }
                        else if (d1) { if (f) mb.Tri(qa, qd, qc); else mb.Tri(qa, qc, qd); }
                        else Quad(qa, qb, qc, qd, f);
                    }
                }
            }

            RingData ReadBack(int[] v, Layer layer)
            {
                var d = new RingData(v.Length) { layer = layer };
                for (int i = 0; i < v.Length; i++)
                {
                    d.p[i] = mb.V[v[i]];
                    d.look[i] = new Look(mb.C[v[i]], mb.U[v[i]].x, mb.U[v[i]].y);
                    d.w[i] = mb.W[v[i]];
                }
                return d;
            }

            /// Adds a ring parameter (metres), merging values closer than Eps into the first one added (table rows come first);
            /// 'edge' marks a layer or colour boundary (it survives a merge). A sample closer than Eps to a boundary would read its
            /// layer from the far side of it and split the hem step around a sliver ring. As in the prototype, steps and colour
            /// seams are only cut at boundaries; every other sample is a single ring.
            static void AddSample(List<(float v, bool edge)> list, float v, bool edge)
            {
                for (int i = 0; i < list.Count; i++)
                    if (Mathf.Abs(list[i].v - v) < Eps) { list[i] = (list[i].v, list[i].edge || edge); return; }
                list.Add((v, edge));
            }

            // ---------------------------------------------------------------------------------------------------------
            // Garment layers (spec §8): the outermost covering layer (largest t; later wins ties) is the surface
            // ---------------------------------------------------------------------------------------------------------
            static Layer Outer(List<Layer> layers, float q)
            {
                Layer best = null;
                foreach (var L in layers) if (L.Covers(q) && (best == null || L.t >= best.t)) best = L;
                return best;
            }

            /// Moves a torso boundary out of the arm-hole rows (spec §8: the hole rows must never be split).
            float Snap(float y)
            {
                float a = p.yArmpit - 0.01f, b = yST + 0.01f;
                if (y <= a || y >= b) return y;
                return y - a < b - y ? p.yArmpit - 0.012f : yST + 0.012f;
            }

            void MakeLayers()
            {
                for (int i = 0; i < o.garments.Count; i++)
                {
                    Garment g = o.garments[i];
                    if (g.kind == GarmentKind.Skirt || g.kind == GarmentKind.CoatTail) { skirts.Add(MakeSkirt(g, i)); continue; }
                    if (!float.IsNaN(g.y0)) AddTorsoLayers(g, i);
                    if (g.sleeve > 0f)
                    {
                        var A = new Layer { g = g, hi = g.sleeve, t = g.t, quilt = g.quilt, pitch = g.pitch * 0.8f, col = g.col, gloss = g.gloss };
                        if (g.cuff.HasValue) A.bands.Add((g.sleeve - 0.035f, float.PositiveInfinity, g.cuff.Value, 0f));
                        foreach (var s in g.armStripes) A.bands.Add((s.s0, s.s1, s.c, g.emitStripes));
                        armL.Add(A);
                    }
                    if (!float.IsNaN(g.leg))
                    {
                        // Deliberate deviation: a hem within 3 cm of the ankle cannot be cut (boundaries below yAnkle are dropped), so it
                        // faded to skin over the last strip; such trousers now run on into the shoe.
                        var L = new Layer { g = g, lo = g.leg < p.yAnkle + 0.03f ? float.NegativeInfinity : g.leg, t = g.t, quilt = g.quilt, pitch = g.pitch * 0.8f, col = g.col, gloss = g.gloss };
                        foreach (var s in g.legStripes) L.bands.Add((s.y0, s.y1, s.c, g.emitStripes));
                        legL.Add(L);
                    }
                }
                if (o.sockCol.HasValue) legL.Add(new Layer { hi = 0.15f * k, t = 0.004f, col = o.sockCol.Value, gloss = .04f });
            }

            void AddTorsoLayers(Garment g, int order)
            {
                float yN = p.yNeck;
                float top = g.collar switch
                {
                    Collar.Crew => yN - 0.012f,
                    Collar.Rib => yN - 0.012f,
                    Collar.Stand => yN + 0.05f * k,
                    Collar.Turtle => yN + 0.07f * k,
                    _ => float.IsNaN(g.y1) ? float.PositiveInfinity : g.y1,
                };
                float lo = g == salvar ? Mathf.Min(g.y0, ySplit - 0.05f) : g.y0;   // a şalvar also covers the drop-crotch rings
                // Deliberate deviation from the prototype: a garment that does not go down the legs ends 12 mm above the seat
                // ring. At or below that ring no hem was cut, so raincoats and long puffers ran into the crotch and smeared down the thighs.
                if (g != salvar && float.IsNaN(g.leg)) lo = Mathf.Max(lo, rows[0].y + 0.012f);
                var L = new Layer
                {
                    g = g, main = true, lo = Snap(lo), hi = Snap(top), t = g.t, quilt = g.quilt, pitch = g.pitch, q0 = g.y0,
                    order = order, col = g.col, gloss = g.gloss,
                };
                L.gLo = L.lo; L.gHi = L.hi;
                foreach (var s in g.stripes) L.bands.Add((s.y0, s.y1, s.c, g.emitStripes));
                if (g.band.HasValue) L.bands.Add((L.lo, L.lo + 0.035f, g.band.Value, 0f));
                if (g.collar == Collar.Rib) L.ribFrom = L.hi - 0.03f * k;
                if (g.collar == Collar.Stand) L.standFrom = yN - 0.02f;
                torsoL.Add(L);

                Layer Extra(float a, float b, float t, Color32 c, float gloss) => new Layer
                {
                    g = g, lo = Snap(a), hi = Snap(b), t = t, order = order, col = c, gloss = gloss, gLo = L.gLo, gHi = L.gHi,
                };
                if (g.collar == Collar.Turtle) torsoL.Add(Extra(L.hi - 0.03f * k, L.hi, g.t + 0.007f, MB.Shade(g.col, 0.92f), g.gloss));   // folded lip
                if (!float.IsNaN(g.yoke)) torsoL.Add(Extra(g.yoke, L.hi, g.t + 0.008f, MB.Shade(g.col, 0.95f), g.gloss));               // cape band
                if (g.belt)
                {
                    float bt = float.IsNaN(g.y1) ? p.yWaist : g.y1;
                    torsoL.Add(Extra(bt - 0.035f, bt, g.t + 0.003f, g.beltCol.a == 0 ? Rgb(0x3A2C22) : g.beltCol, .3f));
                }
            }

            Look SurfLook(Layer L, float q, int col)
            {
                if (L == null) return skin;
                var (c, e) = L.ColourAt(q);
                if (q >= L.ribFrom && (col & 1) == 1) c = MB.Shade(c, 0.86f);
                return new Look(c, L.gloss, e);
            }

            // ---------------------------------------------------------------------------------------------------------
            // Torso (spec §3)
            // ---------------------------------------------------------------------------------------------------------
            List<TRow> TorsoRows()
            {
                float yC = p.yCrotch, yH = p.yHip, yW = p.yWaist, yCh = p.yChest, yA = p.yArmpit, yN = p.yNeck, nr = p.neckR;
                Vector3 hp = new Vector3(p.hipRx, p.hipZf, p.hipZb), wa = new Vector3(p.waistRx, p.waistZf, p.waistZb), ch = new Vector3(p.chestRx, p.chestZf, p.chestZb);
                var r = new List<TRow>(14);
                void R(float y, float rx, float zf, float zb, float n, float cz = 0f, float tilt = 0f) =>
                    r.Add(new TRow { y = y, rx = rx, zf = zf, zb = zb, n = n, cz = cz, tilt = tilt });
                R(yC + 0.28f * (yH - yC), hp.x * 0.975f, hp.y * 0.93f, hp.z * 0.92f, 2.2f);                                 // 0 seat (leg split)
                R(yH, hp.x, hp.y, hp.z, 2.2f);                                                                              // 1 hip
                R(yH + 0.5f * (yW - yH), Lerp(hp.x, wa.x, 0.55f), Lerp(hp.y, wa.y, 0.6f), Lerp(hp.z, wa.z, 0.72f), 2.3f);   // 2 pelvis
                R(yW, wa.x, wa.y, wa.z, 2.4f);                                                                              // 3 waist
                R(yW + 0.48f * (yCh - yW), Lerp(wa.x, ch.x, 0.55f), Lerp(wa.y, ch.y, 0.55f), Lerp(wa.z, ch.z, 0.55f), 2.5f); // 4 ribs
                R(yCh, ch.x, ch.y, ch.z, 2.6f);                                                                             // 5 chest
                R(yA, ch.x * 1.02f, ch.y * 0.97f, ch.z * 1.02f, 2.7f);                                                      // 6 armpit (hole bottom)
                R(0.5f * (yA + yST), Lerp(ch.x * 1.02f, p.shTopRx, 0.4f), ch.y * 0.90f, ch.z * 0.98f, 2.7f);                // 7 upper chest (hole mid)
                R(yST, p.shTopRx, ch.y * 0.70f, ch.z * 0.86f, 2.5f, 0f, 0.10f);                                             // 8 shoulder top (hole top)
                R(yST + 0.40f * (yN - yST), Lerp(p.shTopRx, nr, 0.42f), Lerp(ch.y * 0.70f, nr, 0.40f), Lerp(ch.z * 0.86f, nr * 1.05f, 0.30f), 2.3f, -0.004f, 0.25f);  // 9 trap1
                R(yST + 0.75f * (yN - yST), Lerp(p.shTopRx, nr, 0.80f), Lerp(ch.y * 0.70f, nr, 0.80f), Lerp(ch.z * 0.86f, nr * 1.05f, 0.75f), 2.15f, -0.010f, 0.35f); // 10 trap2
                R(yN, nr * 1.06f, nr * 0.95f, nr * 1.05f, 2.0f, -0.018f, 0.40f);                                            // 11 neck0
                float y12 = Lerp(yN, chin, 0.5f) + 0.01f;
                R(y12, nr, nr * 0.92f, nr * 0.98f, 2.0f, -0.006f, 0.30f);                                                   // 12 neck1
                float rz = p.neckTopRz;                                                                                      // 13 neck2, inside the head
                R(float.IsNaN(p.neckTopY) ? chin + 0.05f : Mathf.Max(p.neckTopY, y12 + 0.005f),
                  float.IsNaN(p.neckTopRx) ? nr * 0.93f : p.neckTopRx, float.IsNaN(rz) ? nr * 0.88f : rz, float.IsNaN(rz) ? nr * 0.92f : rz,
                  2.0f, float.IsNaN(p.neckTopZ) ? 0.006f : p.neckTopZ, 0.15f);
                return r;
            }

            /// Table row at a fractional row index: y linear, everything else Catmull-Rom.
            TRow InterpT(float ip)
            {
                int n = rows.Count;
                if (ip <= 0f) return rows[0];
                if (ip >= n - 1) return rows[n - 1];
                int i = (int)ip;
                float u = ip - i;
                TRow a = rows[Mathf.Max(i - 1, 0)], b = rows[i], c = rows[i + 1], d = rows[Mathf.Min(i + 2, n - 1)];
                return new TRow
                {
                    y = b.y + (c.y - b.y) * u, rx = Cr(a.rx, b.rx, c.rx, d.rx, u), zf = Cr(a.zf, b.zf, c.zf, d.zf, u), zb = Cr(a.zb, b.zb, c.zb, d.zb, u),
                    n = Cr(a.n, b.n, c.n, d.n, u), cz = Cr(a.cz, b.cz, c.cz, d.cz, u), tilt = Cr(a.tilt, b.tilt, c.tilt, d.tilt, u),
                };
            }

            /// y to fractional row index (piecewise linear between table rows).
            float YToI(float y)
            {
                if (y <= rows[0].y) return 0f;
                for (int i = 0; i + 1 < rows.Count; i++)
                    if (y <= rows[i + 1].y) return i + (y - rows[i].y) / (rows[i + 1].y - rows[i].y);
                return rows.Count - 1;
            }

            TRow RingAtY(float y) => InterpT(YToI(y));

            /// Spec §0 "Torso ring point": superellipse, radial bumps plus 'extra' (garment offset), tilt, then cz.
            /// The tilt uses the body's z (with bumps, without the garment offset): layers offset horizontally from the tilted body
            /// ring, so a thick layer cannot fold the neck-base rings (tilt .10 → .25 within 2 cm) over each other.
            Vector3 TorsoPt(in TRow rg, float th, float extra)
            {
                float s = Mathf.Sin(th), c = Mathf.Cos(th), e = 2f / rg.n;
                float x = rg.rx * Se(s, e), z = (c >= 0f ? rg.zf : rg.zb) * Se(c, e);
                float bump = Bumps(th, rg.y), rr = Mathf.Sqrt(x * x + z * z);
                float body = 1f, outer = 1f;
                if (rr > 1e-6f) { body = (rr + bump) / rr; outer = (rr + bump + extra) / rr; }
                return new Vector3(x * outer, rg.y - z * body * rg.tilt, z * outer + rg.cz);
            }

            /// Spec §3 radial bumps (belly, bust, pecs, buttocks).
            float Bumps(float th, float y)
            {
                float d = 0f;
                if (p.belly > 0f) d += p.belly * G(Wrap(th), 60f * Deg) * G(y - (p.yWaist - 0.035f), 0.085f);
                for (int i = -1; i <= 1; i += 2)
                {
                    if (p.bust > 0f) d += p.bust * G(Wrap(th - i * 30f * Deg), 24f * Deg) * G(y - (p.yChest - 0.012f), 0.05f);
                    if (p.pecs > 0f) d += p.pecs * G(Wrap(th - i * 32f * Deg), 30f * Deg) * G(y - (p.yChest + 0.02f), 0.05f);
                    if (p.butt > 0f) d += p.butt * G(Wrap(th - Mathf.PI - i * 32f * Deg), 28f * Deg) * G(y - (p.yHip - 0.01f), 0.05f);
                }
                return d;
            }

            /// Torso garment over a skirt: offset = max(t, skirt offset + .006) (spec §8 "Layer rule"). A layer is over a skirt when
            /// its garment is listed after it (the prototype outfits list skirts before the tops worn over them).
            float OverSkirt(Layer L, in TRow rg, float th, float off)
            {
                if (L == null) return off;
                if (L.g != null && (L.g.kind == GarmentKind.Trousers || L.g.kind == GarmentKind.Jeans || L.g.kind == GarmentKind.Shorts
                    || L.g.kind == GarmentKind.Stockings || L.g.kind == GarmentKind.Salvar)) return off;   // deviation: bottoms never go over a skirt
                foreach (var S in skirts)
                    if (S.order < L.order && S.Covers(rg.y))
                        off = Mathf.Max(off, RadXZ(SkirtPt(S, rg.y, th)) - RadXZ(TorsoPt(rg, th, 0f) - new Vector3(0f, 0f, rg.cz)) + 0.006f);
                return off;
            }

            float LayerOff(Layer L, in TRow rg, float th) => OverSkirt(L, rg, th, L?.Offset(rg.y) ?? 0f);

            // ---- front openings (open-front jacket / cardigan, V-neck): spec §8 "Open-front", G4/G5 ----

            /// Half-width (x) of the front opening of layer L's garment at y; negative where the front is closed.
            float FrontGapHalf(Layer L, float y)
            {
                Garment g = L?.g;
                if (g == null) return -1f;
                float h = -1f;
                if (g.openFront) h = 0.5f * Lerp(g.gapBottom, g.gapTop, Mathf.Clamp01((y - L.gLo) / Mathf.Max(1e-4f, L.gHi - L.gLo)));
                if (!float.IsNaN(g.vNeckBottom) && y > g.vNeckBottom)
                    h = Mathf.Max(h, 0.5f * VNeckTop * k * Mathf.Clamp01((y - g.vNeckBottom) / Mathf.Max(1e-4f, L.gHi - g.vNeckBottom)));
                return h;
            }

            /// Angle in [0, pi/2] whose surface point has lateral offset |x| (bisection on the analytic surface), signed like x.
            float AngleAtX(in TRow rg, float x, float off)
            {
                float lo = 0f, hi = 0.5f * Mathf.PI, ax = Mathf.Abs(x);
                for (int i = 0; i < 24; i++)
                {
                    float m = 0.5f * (lo + hi);
                    if (TorsoPt(rg, m, off).x < ax) lo = m; else hi = m;
                }
                return Mathf.Sign(x) * 0.5f * (lo + hi);
            }

            /// Opening edge angle; columns 1 and 19 move there (column 2 stays at 36°). At least 2 mm so the V point stays sharp.
            float EdgeAngle(in TRow rg, float half, float off) => Mathf.Min(AngleAtX(rg, Mathf.Max(half, 0.002f), off), 30f * Deg);

            static float ColumnAngle(int c, float thEdge)
            {
                if (thEdge > 0f && c == 1) return thEdge;
                if (thEdge > 0f && c == NT - 1) return 2f * Mathf.PI - thEdge;
                return 2f * Mathf.PI * c / NT;
            }

            /// Outermost torso layer at y that belongs to another garment (what an opening shows).
            Layer Below(Layer L, float y)
            {
                Layer best = null;
                foreach (var x in torsoL) if (x.g != L.g && x.Covers(y) && (best == null || x.t >= best.t)) best = x;
                return best;
            }

            Look InnerLook(Layer L, Layer inner, float y)
            {
                Color32 c = L.g.inner.a != 0 ? L.g.inner : inner != null ? inner.ColourAt(y).c : skin.c;
                return new Look(c, inner?.gloss ?? SkinGloss);
            }

            /// One torso ring on layer L, seen from parameter cq (just below / above a boundary: colours and whether the front opening
            /// has started, so the V point gets its own ring). extH >= 0: şalvar drop-crotch ring (skirt weights).
            RingData TorsoData(in TRow rg, Layer L, float cq, float extH = -1f)
            {
                var d = new RingData(NT) { layer = L };
                float y = rg.y, off = L?.Offset(y) ?? 0f;
                float thE = FrontGapHalf(L, cq) >= 0f ? EdgeAngle(rg, Mathf.Max(0f, FrontGapHalf(L, y)), off) : 0f;
                d.edge = thE;
                // the inner layer is read on the ring's side of a boundary (cq): a vest ending exactly here is not under the opening
                Layer inner = thE > 0f ? Below(L, cq) : null;
                float innerOff = inner?.Offset(y) ?? 0f;
                d.innerOff = innerOff;
                Look innerLook = thE > 0f ? InnerLook(L, inner, cq) : default;
                for (int c = 0; c < NT; c++)
                {
                    float th = ColumnAngle(c, thE);
                    bool gap = thE > 0f && (c <= 1 || c == NT - 1);
                    Vector3 pt = TorsoPt(rg, th, gap ? innerOff : OverSkirt(L, rg, th, off));
                    d.p[c] = pt;
                    d.look[c] = gap ? innerLook : SurfLook(L, cq, c);
                    d.w[c] = extH >= 0f ? SkirtW(extH, th) : TorsoW(pt.y, pt.x);
                }
                return d;
            }

            /// Spec §7 torso weights (by vertex y and x).
            BoneWeight TorsoW(float y, float x)
            {
                float yH = p.yHip, yW = p.yWaist, yCh = p.yChest, yN = p.yNeck;
                if (y < yH + 0.02f) return Wt(SVSkel.Pelvis);
                if (y < yW) return Wt(SVSkel.Pelvis, SVSkel.Spine, SS(yH + 0.02f, yW, y));
                if (y < yCh - 0.02f) return Wt(SVSkel.Spine, SVSkel.Chest, SS(yW, yCh - 0.02f, y));
                float t = SS(yN - 0.03f, yN + 0.03f, y);
                if (t <= 0f)
                {
                    float wc = SS(0.42f * p.ghX, 0.82f * p.ghX, Mathf.Abs(x)) * SS(p.yArmpit - 0.04f, p.yArmpit + 0.04f, y);
                    return Wt(SVSkel.Chest, SVSkel.Clav(x >= 0f ? 1 : -1), wc);
                }
                float t2 = SS(chin - 0.005f, chin + 0.045f, y);
                return t2 > 0f ? Wt(SVSkel.Neck, SVSkel.Head, t2) : Wt(SVSkel.Chest, SVSkel.Neck, t);
            }

            /// Ring parameters (row index, rounded to 4 decimals like the prototype's sample set) with a boundary flag: table rows,
            /// layer boundaries, quilt seams and mid-channels, colour band edges and the V point (spec §8 "Ring sampling").
            List<(float ip, bool edge)> TorsoSamples()
            {
                var set = new SortedDictionary<float, bool>();
                for (int i = 0; i < rows.Count; i++) set[i] = false;
                float bot = rows[0].y, topY = rows[rows.Count - 1].y;
                // only strictly inside the torso; quilt seams and colour edges skip the arm-hole rows [skipLo, skipHi]
                void Add(float y, bool edge, float skipLo = float.PositiveInfinity, float skipHi = float.NegativeInfinity)
                {
                    if (!(y > bot && y < topY)) return;
                    float ii = (float)Math.Round(YToI(y), 4);
                    if (ii >= skipLo && ii <= skipHi) return;
                    set[ii] = edge || set.TryGetValue(ii, out bool e) && e;
                }
                foreach (var L in torsoL) { Add(L.lo, true); Add(L.hi, true); }
                foreach (var S in skirts) Add(S.yTop, false, 6f, 8f);             // the prototype also rings the skirt top (not in the hole rows)
                foreach (var L in torsoL)
                {
                    if (L.quilt > 0f)
                    {
                        float ytop = float.IsInfinity(L.hi) ? rows[8].y : L.hi, yb = L.q0 + L.pitch;
                        for (; yb < ytop - 0.02f; yb += L.pitch) { Add(yb, false, 5.6f, 8.4f); Add(yb - 0.5f * L.pitch, false, 5.6f, 8.4f); }
                        if (yb - 0.5f * L.pitch < ytop) Add(yb - 0.5f * L.pitch, false, 5.6f, 8.4f);
                    }
                    foreach (var b in L.bands) { Add(b.a, true, 5.8f, 8.2f); Add(b.b, true, 5.8f, 8.2f); }
                    if (!float.IsInfinity(L.ribFrom)) Add(L.ribFrom, true, 5.8f, 8.2f);
                    if (L.main && !float.IsNaN(L.g.vNeckBottom)) Add(L.g.vNeckBottom, true, 5.8f, 8.2f);   // the V point
                }
                // a boundary closer than Eps (in y) to its neighbour sample would leave that sample on the wrong side of the
                // boundary and split the hem step around a sliver ring: merge the two, keeping a table row exact
                var list = new List<(float, bool)>();
                foreach (var kv in set)
                {
                    int n = list.Count;
                    if (n > 0 && (kv.Value || list[n - 1].Item2) && InterpT(kv.Key).y - InterpT(list[n - 1].Item1).y < Eps)
                    {
                        float prev = list[n - 1].Item1;
                        list[n - 1] = (prev == Mathf.Round(prev) ? prev : kv.Key, true);
                        continue;
                    }
                    list.Add((kv.Key, kv.Value));
                }
                return list;
            }

            void BuildTorso()
            {
                if (salvar != null) SalvarExtension();
                var samples = TorsoSamples();
                for (int si = 0; si < samples.Count; si++)
                {
                    var (ip, edge) = samples[si];
                    TRow rg = InterpT(ip);
                    float y = rg.y;
                    Layer below = Outer(torsoL, y - Eps), above = Outer(torsoL, y + Eps);
                    if (si == 0 || !edge)   // a single ring on the layer above (the seat ring is never split)
                    {
                        Push(tor, TorsoData(rg, above ?? below, y), ip, rg);
                        continue;
                    }
                    Push(tor, TorsoData(rg, below, y - Eps), ip, rg);
                    Step(tor, TorsoData(rg, above, y + Eps), ip, rg);
                }
                iA = LastAt(6f); iU = LastAt(7f); iS = LastAt(8f);
                // arm holes: columns 3..6 (right) and 13..16 (left) between rows 6-7 and 7-8
                Stitch(tor, (n, c) => (n == iA || n == iU) && (c >= 3 && c < 7 || c >= 13 && c < 17));
                int[] top = tor[tor.Count - 1].v;
                Fan(top, V(Centroid(top) + new Vector3(0f, 0.01f, 0f), skin, Wt(SVSkel.Head)));   // neck cap inside the head
            }

            int LastAt(float ip)
            {
                for (int i = tor.Count - 1; i >= 0; i--) if (tor[i].param == ip) return i;
                throw new InvalidOperationException("missing torso row " + ip);
            }

            /// Şalvar drop crotch (spec §8): extra rings below the seat down to ySplit, flaring to hip + (.03, .035, .025).
            void SalvarExtension()
            {
                TRow seat = rows[0];
                for (int j = 3; j >= 1; j--)       // bottom to top
                {
                    float u = j / 3f, e = 1f - (1f - u) * (1f - u);
                    var rg = new TRow
                    {
                        y = Lerp(seat.y, ySplit, u), rx = Lerp(seat.rx, p.hipRx + 0.03f, e), zf = Lerp(seat.zf, p.hipZf + 0.035f, e),
                        zb = Lerp(seat.zb, p.hipZb + 0.025f, e), n = 2.2f,
                    };
                    Push(tor, TorsoData(rg, Outer(torsoL, rg.y), rg.y, u), -j, rg);
                }
            }

            // ---------------------------------------------------------------------------------------------------------
            // Arms (spec §4, §6 hand): one loft from the hole loop to the fingertip, built in the A-pose
            // ---------------------------------------------------------------------------------------------------------
            BoneWeight ArmW(float sv, int s)
            {
                float Lu = p.Lu;
                if (sv < 0.75f * Lu) return Wt(SVSkel.ShHlp(s), SVSkel.UpArm(s), 0.25f + 0.75f * SS(0.25f * Lu, 0.75f * Lu, sv));
                if (sv < 0.82f * Lu) return Wt(SVSkel.UpArm(s));
                if (sv < Lu) return Wt(SVSkel.UpArm(s), SVSkel.ElHlp(s), SS(0.82f * Lu, Lu, sv));
                if (sv < Lu + 0.16f * p.Lf) return Wt(SVSkel.ElHlp(s), SVSkel.Fore(s), SS(Lu, Lu + 0.16f * p.Lf, sv));
                if (sv < sW - 0.02f) return Wt(SVSkel.Fore(s));
                if (sv < sW + 0.02f) return Wt(SVSkel.Fore(s), SVSkel.Hand(s), SS(sW - 0.02f, sW + 0.02f, sv));
                float h = (sv - sW) / p.Lh;          // mitten: the finger part follows the Fingers bone
                return h < 0.45f ? Wt(SVSkel.Hand(s)) : Wt(SVSkel.Hand(s), SVSkel.Fingers(s), SS(0.45f, 0.62f, h));
            }

            Look ArmLook(Layer L, float q)
            {
                if (L == null) return o.gloves && q >= gloveStart ? glove : skin;
                var (c, e) = L.ColourAt(q);
                return new Look(c, L.gloss, e);
            }

            Look HandLook => o.gloves ? glove : skin;

            static LRow Row(float[,] t, int i, float key, float mul, float olSign) => new LRow
            {
                key = key, rl = t[i, 1] * mul, rm = t[i, 2] * mul, rf = t[i, 3] * mul, rb = t[i, 4] * mul, n = t[i, 5],
                ol = olSign * t[i, 6] * mul, of = t[i, 7] * mul,
            };

            void BuildArm(int s)
            {
                Vector3 J = new Vector3(s * p.ghX, p.yGH, p.ghZ);
                float ang = s * SVSkel.APose * Deg, ca = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                Vector3 Rot(Vector3 v) => new Vector3(ca * v.x - sn * v.y, sn * v.x + ca * v.y, v.z);       // Rz(±45°): the A-pose bind
                Vector3 Unrot(Vector3 v) => new Vector3(ca * v.x + sn * v.y, -sn * v.x + ca * v.y, v.z);
                Vector3 el = new Vector3(s, 0f, 0f), ef = Vector3.forward, ea = Vector3.down;
                Vector3 At(float sax, float lat, float fr) => J + Rot(ea * sax + el * lat + ef * fr);

                // hole boundary loop (12 vertices): shoulder-top row front to back, upper chest, armpit row back to front, upper chest
                int[] cols = s > 0 ? new[] { 3, 4, 5, 6, 7 } : new[] { 17, 16, 15, 14, 13 };
                Ring rS = tor[iS], rU = tor[iU], rA = tor[iA];
                var loop = new int[NA]; var lrg = new TRow[NA]; var lcol = new int[NA];
                void L(int j, Ring r, int c) { loop[j] = r.v[c]; lrg[j] = r.rg; lcol[j] = c; }
                for (int j = 0; j < 5; j++) L(j, rS, cols[j]);
                L(5, rU, cols[4]);
                for (int j = 0; j < 5; j++) L(6 + j, rA, cols[4 - j]);
                L(11, rU, cols[0]);
                var phis = new float[NA];
                for (int j = 0; j < NA; j++)
                {
                    Vector3 d = Unrot(P(loop[j]) - J);
                    phis[j] = Mathf.Atan2(Vector3.Dot(d, ef), Vector3.Dot(d, el));
                }
                phis = Unwrap(phis);

                var chain = new List<Ring> { new Ring { v = loop, d = ReadBack(loop, rU.Layer) } };
                Layer gsh = Outer(armL, 0f);
                if (rU.Layer?.g != gsh?.g)
                {
                    // sleeveless outer top (vest): the loop is duplicated at the sleeve offset and colour with a step strip (spec §8)
                    var side = new RingData(NA) { layer = gsh };
                    float off = gsh?.Offset(0f) ?? 0f;
                    for (int j = 0; j < NA; j++)
                    {
                        side.p[j] = TorsoPt(lrg[j], ColumnAngle(lcol[j], 0f), off);
                        side.look[j] = ArmLook(gsh, 0f);
                        side.w[j] = mb.W[loop[j]];
                    }
                    Step(chain, side, 0f);
                }

                float am = p.arm, hs = p.hand;
                var arow = new List<LRow>(16);
                for (int i = 0; i < 5; i++) arow.Add(Row(UpperRows, i, UpperRows[i, 0] * p.Lu, am, 1f));
                for (int i = 0; i < 5; i++) arow.Add(Row(ForeRows, i, p.Lu + ForeRows[i, 0] * p.Lf, am, 1f));
                for (int i = 0; i < 6; i++) arow.Add(Row(HandRows, i, sW + HandRows[i, 0] * p.Lh, hs, -1f));
                float s0 = arow[0].key, s3 = arow[3].key;

                var svals = new List<(float v, bool edge)>();
                foreach (var r in arow) AddSample(svals, r.key, false);
                void AddS(float v, bool edge) { if (v >= s0 - 1e-6f && v < sW + 0.01f) AddSample(svals, v, edge); }
                foreach (var Ly in armL)
                {
                    if (Ly.hi > 0f) AddS(Ly.hi, true);                                      // sleeve ends
                    if (Ly.quilt > 0f) for (float q = 0.5f * Ly.pitch; q < Ly.hi - 0.02f; q += 0.5f * Ly.pitch) AddS(q, false);
                    foreach (var b in Ly.bands) { AddS(b.a, true); AddS(b.b, true); }       // cuff and stripe edges
                }
                if (o.gloves) AddS(gloveStart, true);
                svals.Sort((a, b) => a.v.CompareTo(b.v));

                RingData Data(in LRow rr, float sv, float[] ph, Layer Ly, float cq)
                {
                    var d = new RingData(NA) { layer = Ly };
                    float off = Ly?.Offset(sv) ?? 0f;
                    if (Ly != null && sv > p.Lu * 0.9f) off += 0.006f * SS(p.Lu, sW, sv) * (Ly.t > 0.012f ? 1f : 0.5f);   // ease toward the cuff
                    float tilt = 0.05f * k * Mathf.Max(0f, 1f - (sv - s0) / (0.11f * k));   // medial tilt (spec §4)
                    Look look = ArmLook(Ly, cq);
                    for (int j = 0; j < NA; j++)
                    {
                        Vector2 lf = LimbLocal(rr.rl + off, rr.rm + off, rr.rf + off, rr.rb + off, rr.n, rr.ol, rr.of, ph[j]);
                        float sv2 = sv + tilt * 0.5f * (1f - Mathf.Cos(ph[j]));
                        d.p[j] = At(sv2, lf.x, lf.y); d.look[j] = look; d.w[j] = ArmW(sv2, s);
                    }
                    return d;
                }

                var rings = new List<Ring>();
                foreach (var (sv, edge) in svals)
                {
                    LRow rr = InterpL(arow, sv);
                    float[] ph = Blended(phis, Mathf.Clamp01((sv - s0) / (s3 - s0)));
                    Layer before = Outer(armL, sv - Eps), after = Outer(armL, sv + Eps);
                    if (!edge) { Push(rings, Data(rr, sv, ph, before ?? after, sv), sv); continue; }
                    Push(rings, Data(rr, sv, ph, before, sv - Eps), sv);
                    Step(rings, Data(rr, sv, ph, after, sv + Eps), sv);
                }

                // bridges (the deltoid cap) between the loop and the first arm ring
                Ring armSide = chain[chain.Count - 1];
                foreach (float bt in new[] { 0.36f, 0.70f })
                {
                    var d = new RingData(NA) { layer = gsh };
                    for (int j = 0; j < NA; j++)
                    {
                        Vector3 pb = P(armSide.v[j]), q = pb + (P(rings[0].v[j]) - pb) * bt;
                        Vector3 dd = Unrot(q - J), radial = Rot(el * Vector3.Dot(dd, el) + ef * Vector3.Dot(dd, ef));
                        float rn = radial.magnitude, c = Mathf.Cos(phis[j]);
                        float bulge = Mathf.Sin(Mathf.PI * bt) * (0.016f * Mathf.Max(0f, c) - 0.004f * Mathf.Max(0f, -c)) * am;
                        if (rn > 1e-6f) q += radial / rn * bulge;
                        d.p[j] = q;
                        d.look[j] = ArmLook(gsh, 0f);
                        d.w[j] = bt < 0.5f ? Wt(SVSkel.Clav(s), 0.5f, SVSkel.ShHlp(s), 0.5f) : Wt(SVSkel.ShHlp(s));
                    }
                    Push(chain, d, 0f);
                }
                chain.AddRange(rings);
                Stitch(chain);
                Fan(chain[chain.Count - 1].v, V(At(sW + TipH * p.Lh, -TipCurl * hs, TipFront * hs), HandLook, Wt(SVSkel.Fingers(s))));

                Thumb(s, At, ef, hs);
                IndexFinger(s, At, arow, ef, hs);
            }

            /// Separate closed thumb loft buried in the palm (spec §6): 4 rings × 8; the base ring and its cap Hand/Thumb 50/50,
            /// the rest Thumb.
            void Thumb(int s, Func<float, float, float, Vector3> At, Vector3 front, float hs)
            {
                float[,] tp = { { .08f, -.004f, .026f }, { .22f, -.012f, .046f }, { .36f, -.021f, .055f }, { .49f, -.029f, .057f } };
                float[] tr = { .019f, .016f, .0135f, .011f };
                var c = new Vector3[4];
                for (int i = 0; i < 4; i++) c[i] = At(sW + tp[i, 0] * p.Lh, tp[i, 1] * hs, tp[i, 2] * hs);
                BoneWeight wBase = Wt(SVSkel.Hand(s), 0.5f, SVSkel.Thumb(s), 0.5f), wTip = Wt(SVSkel.Thumb(s));
                var tr4 = new int[4][];
                for (int i = 0; i < 4; i++)
                {
                    Vector3 nxt = i < 3 ? c[i + 1] : c[i] + (c[i] - c[2]), prv = i > 0 ? c[i - 1] : c[i] - (nxt - c[i]);
                    Vector3 dv = Unit(nxt - prv), u = Unit(Vector3.Cross(dv, front)), w = Vector3.Cross(dv, u);
                    float r = tr[i] * hs;
                    tr4[i] = new int[8];
                    for (int j = 0; j < 8; j++)
                    {
                        float a = 2f * Mathf.PI * j / 8f;
                        tr4[i][j] = V(c[i] + u * (Mathf.Cos(a) * r * 0.85f) + w * (Mathf.Sin(a) * r), HandLook, i == 0 ? wBase : wTip);
                    }
                }
                for (int i = 0; i < 3; i++) Strip(tr4[i], tr4[i + 1], Centroid(tr4[i], tr4[i + 1]));
                Fan(tr4[3], V(c[3] + (c[3] - c[2]) * 0.55f, HandLook, wTip));
                Fan(tr4[0], V(c[0] - (c[1] - c[0]) * 0.5f, HandLook, wBase));
            }

            /// Index finger (Index bone): 3 rings × 6 + tip along the mitten's centre line on the thumb side, inside the mitten
            /// while the hand is flat; it shows when the Fingers bone curls (pointing). The base end stays open inside the palm.
            void IndexFinger(int s, Func<float, float, float, Vector3> At, List<LRow> arow, Vector3 front, float hs)
            {
                float[] h = { .50f, .70f, .88f }, r = { .0100f, .0095f, .0085f };
                const float TipHi = .95f;
                var c = new Vector3[4];
                for (int i = 0; i < 3; i++) c[i] = At(sW + h[i] * p.Lh, InterpL(arow, sW + h[i] * p.Lh).ol, 0.016f * hs);
                // the tip follows the mitten's curl toward its own tip and pulls in toward the axis so it stays inside
                float tipLat = Lerp(arow[arow.Count - 1].ol, -TipCurl * hs, (TipHi - HandRows[5, 0]) / (TipH - HandRows[5, 0]));
                c[3] = At(sW + TipHi * p.Lh, tipLat, 0.008f * hs);
                BoneWeight w = Wt(SVSkel.Index(s));
                var ring = new int[3][];
                for (int i = 0; i < 3; i++)
                {
                    Vector3 dv = Unit(c[i + 1] - (i > 0 ? c[i - 1] : c[i]));
                    Vector3 u = Unit(Vector3.Cross(dv, front)), v = Vector3.Cross(dv, u);
                    ring[i] = new int[6];
                    for (int j = 0; j < 6; j++)
                    {
                        float a = 2f * Mathf.PI * j / 6f;
                        ring[i][j] = V(c[i] + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * (r[i] * hs), HandLook, w);
                    }
                }
                for (int i = 0; i < 2; i++) Strip(ring[i], ring[i + 1], Centroid(ring[i], ring[i + 1]));
                Fan(ring[2], V(c[3], HandLook, w));
            }

            // ---------------------------------------------------------------------------------------------------------
            // Legs (spec §5): pants-split topology from the seat ring plus the shared crotch vertex
            // ---------------------------------------------------------------------------------------------------------
            int Crotch()
            {
                Layer seat = Outer(torsoL, tor[0].rg.y);
                Look look = seat == null ? skin : new Look(seat.col, seat.gloss);
                return salvar != null
                    ? V(new Vector3(0f, ySplit - 0.02f, -0.004f), look, Wt(SVSkel.SkirtF, 0.5f, SVSkel.SkirtB, 0.5f))
                    : V(new Vector3(0f, p.yCrotch, -0.004f), look, Wt(SVSkel.HipHlpL, 0.5f, SVSkel.HipHlpR, 0.5f));   // deviation: was Pelvis only (crotch webbing when seated)
            }

            BoneWeight LegW(float y, int s)
            {
                float yK = p.yKnee, yH = p.yHip, yC = p.yCrotch;
                if (y > yC - 0.06f) return Wt(SVSkel.Pelvis, 0.2f, SVSkel.HipHlp(s), 0.8f);   // deviation: was .45/.55, all the shear in one ring
                if (y > yH - 0.45f * (yH - yK)) return Wt(SVSkel.HipHlp(s), 0.5f, SVSkel.Thigh(s), 0.5f);
                if (y > yK + 0.06f) return Wt(SVSkel.Thigh(s));
                if (y > yK + 0.015f) return Wt(SVSkel.Thigh(s), 0.5f, SVSkel.KnHlp(s), 0.5f);
                if (y > yK - 0.015f) return Wt(SVSkel.KnHlp(s));
                if (y > yK - 0.06f) return Wt(SVSkel.KnHlp(s), 0.5f, SVSkel.Shin(s), 0.5f);
                if (y > p.yAnkle + 0.02f) return Wt(SVSkel.Shin(s));
                return Wt(SVSkel.Shin(s), 0.5f, SVSkel.Foot(s), 0.5f);
            }

            // şalvar leg profile (spec §8): r .095 under the crotch, .085 by the knee, a .07 bulge at y .18, a .045 ankle cuff
            float SalvarFirstY => ySplit - 0.02f - 0.035f * k;
            float CuffTop => salvar.leg + 0.03f * k;
            float BulgeY => Mathf.Max(0.18f * k, CuffTop + 0.04f * k);

            float SalvarR(float y)
            {
                float[] py = { SalvarFirstY, p.yKnee, BulgeY, CuffTop }, pr = { 0.095f, 0.085f, 0.07f, 0.045f };
                if (y >= py[0]) return pr[0];
                for (int i = 0; i + 1 < py.Length; i++)
                    if (y >= py[i + 1]) return Lerp(pr[i + 1], pr[i], (y - py[i + 1]) / (py[i] - py[i + 1]));
                return pr[pr.Length - 1];
            }

            float ShaftR(float y)    // boot shaft radius at y (linear between its rings)
            {
                float f = p.foot, y1 = shaftTop;
                float[] hy = { 0.05f * f, 0.10f * f, 0.5f * (0.10f * f + y1), y1 - 0.01f, y1 };
                float[] rr = { shoe.r0 * f * 0.98f, shoe.r0 * f, Lerp(shoe.r0, shoe.r1, 0.5f) * f, shoe.r1 * f, shoe.r1 * f + shoe.lip };
                if (y <= hy[0]) return rr[0];
                for (int i = 0; i + 1 < hy.Length; i++)
                    if (y <= hy[i + 1]) return Lerp(rr[i], rr[i + 1], (y - hy[i]) / (hy[i + 1] - hy[i]));
                return rr[rr.Length - 1];
            }

            List<LRow> LegRows(int s)
            {
                float lg = p.leg;
                Vector3 hip = new Vector3(s * p.hipX, p.yHip, 0f), knee = new Vector3(s * p.kneeX, p.yKnee, 0.006f), ank = new Vector3(s * p.ankleX, p.yAnkle, -0.012f);
                var r = new List<LRow>(12);
                void Add(float[,] t, int i, Vector3 c)
                {
                    LRow x = Row(t, i, -c.y, lg, 1f);
                    x.cx = c.x; x.cz = c.z;
                    r.Add(x);
                }
                for (int i = 0; i < 6; i++)
                {
                    float v = ThighRows[i, 0] < 0f ? (p.yHip - (p.yCrotch - 0.035f * k)) / (p.yHip - p.yKnee) : ThighRows[i, 0];
                    Add(ThighRows, i, hip + (knee - hip) * v);
                }
                for (int i = 0; i < 6; i++) Add(ShinRows, i, knee + (ank - knee) * ShinRows[i, 0]);
                return r;
            }

            void BuildLeg(int s, int crotch)
            {
                int[] seat = tor[0].v;
                var loop = new int[NL];
                for (int c = 0; c <= NT / 2; c++) loop[c] = seat[s > 0 ? c : (NT / 2 + c) % NT];
                loop[NL - 1] = crotch;
                Vector3 c0 = new Vector3(s * p.hipX, p.yCrotch, 0f);
                var phis = new float[NL];
                for (int j = 0; j < NL; j++) { Vector3 d = P(loop[j]) - c0; phis[j] = Mathf.Atan2(d.z, d.x * s); }
                phis = Unwrap(phis);

                List<LRow> lrows = LegRows(s);
                var ys = new List<(float v, bool edge)>();
                foreach (var r in lrows) AddSample(ys, -r.key, false);
                void Add(float y, bool edge) { if (y > p.yAnkle && y < p.yCrotch) AddSample(ys, y, edge); }
                foreach (var Ly in legL) { Add(Ly.lo, true); Add(Ly.hi, true); foreach (var b in Ly.bands) { Add(b.a, true); Add(b.b, true); } }
                if (salvar != null)
                {
                    ys.RemoveAll(y => y.v > SalvarFirstY + 1e-6f);
                    foreach (float y in new[] { SalvarFirstY, p.yKnee, BulgeY, CuffTop }) if (y <= SalvarFirstY) Add(y, false);
                }
                ys.Sort((a, b) => b.v.CompareTo(a.v));
                float y0v = salvar != null ? -ys[0].v : lrows[0].key, y3v = salvar != null ? -ys[Mathf.Min(3, ys.Count - 1)].v : lrows[3].key;

                RingData Data(in LRow rr, float y, float[] ph, Layer Ly, float cq)
                {
                    var d = new RingData(NL) { layer = Ly };
                    float off = Ly?.Offset(y) ?? 0f;
                    float rl = rr.rl + off, rm = rr.rm + off, rf = rr.rf + off, rb = rr.rb + off, n = rr.n, ol = rr.ol, of = rr.of;
                    Garment g = Ly?.g;
                    if (g != null && g.straight > 0f && y < p.yKnee + 0.05f)
                    {
                        // straight trouser leg: radii stop following the calf and ankle (spec §8 "Trousers")
                        LRow kn = InterpL(lrows, -(p.yKnee + 0.05f));
                        float tgt = 0.5f * (kn.rl + kn.rm) * 0.92f + off, tg2 = Lerp(tgt, tgt * 0.88f, SS(p.yKnee, p.yAnkle, y));
                        rl = Lerp(rl, Mathf.Max(rl, tg2), g.straight); rm = Lerp(rm, Mathf.Max(rm, tg2), g.straight);
                        rf = Lerp(rf, Mathf.Max(rf, tg2), g.straight); rb = Lerp(rb, Mathf.Max(rb, tg2 * 1.02f), g.straight);
                    }
                    if (g != null && g == salvar)
                    {
                        float r = Mathf.Max(SalvarR(y), Mathf.Max(Mathf.Max(rl, rm), Mathf.Max(rf, rb)) + 0.006f);
                        rl = rm = rf = rb = r; n = 2f; ol = of = 0f;
                    }
                    if (shaftTop > 0f && y < shaftTop)
                    {
                        // keep everything inside the boot shaft (spec §8 "Tucked into tall boots")
                        float sr = ShaftR(y) - 0.006f, cz = -0.012f + 0.004f + 0.012f * (1f - y / shaftTop);
                        float mx = sr - Mathf.Abs(rr.cx - s * p.ankleX), mz = sr * 1.08f - Mathf.Abs(rr.cz - cz);
                        rl = Mathf.Min(rl, mx - Mathf.Max(0f, ol)); rm = Mathf.Min(rm, mx + Mathf.Min(0f, ol));
                        rf = Mathf.Min(rf, mz - Mathf.Max(0f, of)); rb = Mathf.Min(rb, mz + Mathf.Min(0f, of));
                    }
                    Look look = skin;
                    if (Ly != null) { var (c, e) = Ly.ColourAt(cq); look = new Look(c, Ly.gloss, e); }
                    BoneWeight w = LegW(y, s);
                    for (int j = 0; j < NL; j++)
                    {
                        Vector2 lf = LimbLocal(rl, rm, rf, rb, n, ol, of, ph[j]);
                        d.p[j] = new Vector3(rr.cx + s * lf.x, y, rr.cz + lf.y); d.look[j] = look; d.w[j] = w;
                    }
                    return d;
                }

                var chain = new List<Ring> { new Ring { v = loop } };
                foreach (var (y, edge) in ys)
                {
                    LRow rr = InterpL(lrows, -y);
                    float[] ph = Blended(phis, Mathf.Clamp01((-y - y0v) / (y3v - y0v)));
                    Layer above = Outer(legL, y + Eps), below = Outer(legL, y - Eps);
                    if (!edge) { Push(chain, Data(rr, y, ph, above ?? below, y), y); continue; }
                    Push(chain, Data(rr, y, ph, above, y + Eps), y);
                    Step(chain, Data(rr, y, ph, below, y - Eps), y);
                }
                Stitch(chain);
                int[] last = chain[chain.Count - 1].v;
                var cap = new Look(mb.C[last[0]], mb.U[last[0]].x, mb.U[last[0]].y);
                Fan(last, V(Centroid(last) + new Vector3(0f, -0.015f, 0f), cap, Wt(SVSkel.Shin(s), 0.5f, SVSkel.Foot(s), 0.5f)));
                BuildShoe(s);
            }

            // ---------------------------------------------------------------------------------------------------------
            // Shoes (spec §6): closed loft along z, 10 rings × 14 plus 2 apexes, Foot = 1; boots add a shaft with an inner lip
            // ---------------------------------------------------------------------------------------------------------
            void BuildShoe(int s)
            {
                float f = p.foot;
                Vector3 b = new Vector3(s * p.ankleX, 0f, -0.012f);
                Look upper = new Look(o.shoeCol ?? shoe.col, shoe.gloss), sole = new Look(o.soleCol ?? shoe.soleCol, shoe.gloss);
                Look foot = o.sockCol.HasValue ? new Look(o.sockCol.Value, .04f) : skin;
                BoneWeight W = Wt(SVSkel.Foot(s));
                const int M = 14;
                int nr = shoe.prof.GetLength(0);
                var ring = new int[nr][];
                for (int i = 0; i < nr; i++)
                {
                    float z = shoe.prof[i, 0] * f, w = shoe.prof[i, 1] * f, ty = shoe.prof[i, 2] * f, by = shoe.prof[i, 3] * f;
                    float cy = by + 0.42f * (ty - by), up = ty - cy, dn = cy - by;
                    ring[i] = new int[M];
                    for (int j = 0; j < M; j++)
                    {
                        float a = 2f * Mathf.PI * j / M, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                        float x = w * Se(c, 2f / 2.4f) * (c * s >= 0f ? 1f : 0.97f);          // inner side slightly straighter
                        float y = cy + (sn >= 0f ? up * Se(sn, 2f / 2.2f) : dn * Se(sn, 2f / 5f));
                        float xo = 0.006f * f * (z / 0.2f) * s;                                 // toes point slightly outward
                        Look l = y < by + shoe.sole * f ? sole : !shoe.open ? upper : y > cy && z > 0.02f * f && z < 0.10f * f ? upper : foot;
                        ring[i][j] = V(b + new Vector3(x + xo, y, z), l, W);
                    }
                }
                for (int i = 0; i + 1 < nr; i++) Strip(ring[i], ring[i + 1], Centroid(ring[i], ring[i + 1]));
                Look ends = shoe.open ? foot : upper;
                Fan(ring[0], V(Centroid(ring[0]) + new Vector3(0f, 0f, -0.007f * f), ends, W));
                Fan(ring[nr - 1], V(Centroid(ring[nr - 1]) + new Vector3(0f, 0f, 0.007f * f), ends, W));
                if (shaftTop <= 0f) return;

                BoneWeight WS = Wt(SVSkel.Shin(s));
                float y1 = shaftTop;
                float[] hy = { 0.05f * f, 0.10f * f, 0.5f * (0.10f * f + y1), y1 - 0.01f, y1 };
                var sr = new int[hy.Length][];
                Vector3 top = Vector3.zero;
                for (int i = 0; i < hy.Length; i++)
                {
                    float r = ShaftR(hy[i]);
                    Vector3 ctr = b + new Vector3(0f, hy[i], 0.004f + 0.012f * (1f - hy[i] / y1));
                    sr[i] = new int[12];
                    for (int j = 0; j < 12; j++)
                    {
                        float a = 2f * Mathf.PI * j / 12f;
                        sr[i][j] = V(ctr + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r * 1.08f), upper, hy[i] > 0.12f ? WS : W);
                    }
                    if (i + 1 == hy.Length) top = ctr;
                }
                for (int i = 0; i + 1 < hy.Length; i++) Strip(sr[i], sr[i + 1], Centroid(sr[i], sr[i + 1]));
                // dark inner lip from the rim down toward the leg
                float ri = ShaftR(y1) - 0.012f;
                var lip = new int[12];
                for (int j = 0; j < 12; j++)
                {
                    float a = 2f * Mathf.PI * j / 12f;
                    lip[j] = V(top + new Vector3(Mathf.Sin(a) * ri, -0.004f, Mathf.Cos(a) * ri * 1.08f), new Look(Rgb(0x101010), shoe.gloss), WS);
                }
                Strip(sr[hy.Length - 1], lip, top + new Vector3(0f, -0.05f, 0f));
            }

            // ---------------------------------------------------------------------------------------------------------
            // Skirt and coat tail (build_skirt): bell loft from yTop to the hem with a hem lip and an inward band
            // ---------------------------------------------------------------------------------------------------------
            Skirt MakeSkirt(Garment g, int order)
            {
                bool tail = g.kind == GarmentKind.CoatTail;
                var S = new Skirt
                {
                    g = g, order = order,
                    yTop = !float.IsNaN(g.y1) ? g.y1 : tail ? p.yHip + 0.02f : p.yWaist - 0.01f,
                    yHem = !float.IsNaN(g.hem) ? g.hem : tail ? p.yKnee + 0.02f : 0.24f * p.H / 1.64f,
                };
                S.yHem = Mathf.Min(S.yHem, S.yTop - 0.05f);           // keep the bell at least 5 cm long
                S.hip = RingAtY(Mathf.Max(Mathf.Min(p.yHip - 0.01f, S.yTop - 0.02f), rows[0].y));
                S.hemR = new Vector3(p.hipRx + g.flare + g.t + 0.010f, p.hipZf + 0.030f + 0.6f * g.flare + g.t, p.hipZb + 0.012f + 0.6f * g.flare + g.t);
                if (tail) S.hemR += new Vector3(0.020f, 0.025f, 0f);   // deviation: bent knees came through the front of coat tails
                return S;
            }

            /// Skirt surface point: hugs the torso + .5t down to the hip, then eases to the hem radii (e = 1 - (1 - tt)^1.4).
            Vector3 SkirtPt(Skirt S, float y, float th)
            {
                float t = S.g.t, rx, zf, zb;
                if (y >= S.hip.y)
                {
                    TRow rg = RingAtY(y);
                    float e = SS(S.yTop, S.hip.y, y);
                    // Deliberate deviation: .5t (.008 for a coat tail) equalled the trousers' offset and z-fought under the jacket hem
                    float h0 = Mathf.Max(0.5f * t, 0.010f);
                    rx = rg.rx + h0; zf = rg.zf + h0 + 0.008f * e; zb = rg.zb + h0 + 0.006f * e;
                }
                else
                {
                    float tt = Mathf.Clamp01((S.hip.y - y) / Mathf.Max(1e-4f, S.hip.y - S.yHem)), e = 1f - Mathf.Pow(1f - tt, 1.4f);
                    rx = Lerp(S.hip.rx + 0.002f + t, S.hemR.x, e); zf = Lerp(S.hip.zf + 0.010f + t, S.hemR.y, e); zb = Lerp(S.hip.zb + 0.008f + t, S.hemR.z, e);
                }
                float n = Lerp(2.2f, 2.0f, (S.yTop - y) / (S.yTop - S.yHem));
                float sn = Mathf.Sin(th), cs = Mathf.Cos(th);
                float x = rx * Se(sn, 2f / n), z = (cs >= 0f ? zf : zb) * Se(cs, 2f / n);
                float d = Bumps(th, y), rr = Mathf.Sqrt(x * x + z * z);   // follows the belly and buttocks so they never poke through
                if (rr > 1e-6f && d != 0f) { x *= (rr + d) / rr; z *= (rr + d) / rr; }
                return new Vector3(x, y, z);
            }

            /// Animation-spec skirt weights: wP = 1 - ss(0,.6,h), wF = (1 - wP)(1 - ss(70°,110°,|θ|)), wB the rest; top two kept.
            static BoneWeight SkirtW(float h, float th)
            {
                // Deliberate deviation from the animation spec (wP = 1 - ss(0,.6,h), blend ss(70,110)): the front now follows a
                // striding thigh from a quarter of the way down, and the front/back blend spans 40-140 deg so a seated bell shears smoothly.
                float a = Mathf.Abs(Wrap(th)) / Deg, back = SS(40f, 140f, a);
                float wP = Mathf.Lerp(1f - SS(0f, 0.28f, h), 1f - SS(0f, 0.6f, h), back), wF = (1f - wP) * (1f - back), wB = 1f - wP - wF;
                if (wP <= wF && wP <= wB) return Wt(SVSkel.SkirtF, wF, SVSkel.SkirtB, wB);
                return wF >= wB ? Wt(SVSkel.Pelvis, wP, SVSkel.SkirtF, wF) : Wt(SVSkel.Pelvis, wP, SVSkel.SkirtB, wB);
            }

            /// Bell weights (deliberate deviation from the animation spec, see the comment above BuildSkirt's use): the top two of
            /// pelvis (top band), SkirtB (back half), and the same-side thigh / shin (front half; the centre front split between the legs).
            BoneWeight BellW(float y, float h, float th)
            {
                float sa = Wrap(th) / Deg, a = Mathf.Abs(sa);
                int side = sa >= 0f ? 1 : -1;                              // +x (sin th > 0) is the right leg
                float back = SS(55f, 115f, a);                             // 0 front .. 1 back
                float wP = Mathf.Lerp(1f - SS(0f, 0.28f, h), 1f - SS(0f, 0.6f, h), back);
                float wB = (1f - wP) * back, wLeg = (1f - wP) * (1f - back);
                var ws = new List<(int b, float w)>(4) { (SVSkel.Pelvis, wP), (SVSkel.SkirtB, wB) };
                float above = SS(p.yKnee - 0.05f, p.yKnee + 0.05f, y);    // 1 above the knee band, 0 below
                if (a < 18f)
                {
                    // centre front: shared by both legs; a hard thigh / shin switch at the knee keeps it to two bones
                    float tR = SS(-18f, 18f, sa);
                    bool thigh = above >= 0.5f;
                    ws.Add((thigh ? SVSkel.Thigh(-1) : SVSkel.Shin(-1), wLeg * (1f - tR)));
                    ws.Add((thigh ? SVSkel.Thigh(1) : SVSkel.Shin(1), wLeg * tR));
                }
                else
                {
                    ws.Add((SVSkel.Thigh(side), wLeg * above));
                    ws.Add((SVSkel.Shin(side), wLeg * (1f - above)));
                }
                ws.Sort((x, z) => z.w.CompareTo(x.w));
                return ws[1].w > 1e-4f ? Wt(ws[0].b, ws[0].w, ws[1].b, ws[1].w) : Wt(ws[0].b);
            }

            void BuildSkirt(Skirt S)
            {
                Garment g = S.g;
                float span = S.yTop - S.yHem, uh = (S.yTop - S.hip.y) / span;
                var us = new SortedSet<float>
                {
                    0f, 1f, 0.5f, 0.68f, 0.84f, Mathf.Clamp01((S.yTop - (S.hip.y + 0.5f * (S.yTop - S.hip.y))) / span), Mathf.Clamp01(uh), Lerp(uh, 1f, 0.18f),
                };
                if (g.band.HasValue) us.Add(0.9f);                     // hem band on the last 10%, crisp edge
                Look LookAt(float u) => new Look(g.band.HasValue && u > 0.9f ? g.band.Value : g.col, g.gloss);
                RingData Data(float u, Look look)
                {
                    var d = new RingData(NT);
                    float y = Lerp(S.yTop, S.yHem, u);
                    for (int c = 0; c < NT; c++)
                    {
                        float th = 2f * Mathf.PI * c / NT;
                        d.p[c] = SkirtPt(S, y, th); d.look[c] = look; d.w[c] = BellW(y, u, th);
                    }
                    return d;
                }
                var rings = new List<Ring>();
                foreach (float u in us)
                {
                    if (!(g.band.HasValue && u == 0.9f)) { Push(rings, Data(u, LookAt(u)), u); continue; }
                    Push(rings, Data(u, LookAt(u - Eps)), u);
                    Step(rings, Data(u, LookAt(u + Eps)), u);
                }
                Stitch(rings);

                // hem lip: an annulus shrunk by .010 facing down, then an inward-facing band shrunk by .012 rising 5 cm
                int[] hem = rings[rings.Count - 1].v;
                var inner = new int[2][];
                for (int r = 0; r < 2; r++)
                {
                    float dy = r == 0 ? 0f : 0.05f, shrink = r == 0 ? 0.010f : 0.012f;
                    inner[r] = new int[NT];
                    for (int c = 0; c < NT; c++)
                    {
                        Vector3 q = P(hem[c]);
                        float rr = RadXZ(q), f = (rr - shrink) / rr;
                        inner[r][c] = V(new Vector3(q.x * f, q.y + dy, q.z * f), new Look(MB.Shade(mb.C[hem[c]], 0.55f), mb.U[hem[c]].x), mb.W[hem[c]]);
                    }
                }
                Strip(hem, inner[0], new Vector3(0f, S.yHem + 0.05f, 0f));
                for (int c = 0; c < NT; c++)
                {
                    int c1 = (c + 1) % NT;
                    Vector3 ctr = (P(inner[0][c]) + P(inner[0][c1]) + P(inner[1][c1]) + P(inner[1][c])) * 0.25f;
                    Quad(inner[0][c], inner[0][c1], inner[1][c1], inner[1][c], 2f * ctr - new Vector3(0f, ctr.y, 0f));   // faces the axis
                }
            }

            // ---------------------------------------------------------------------------------------------------------
            // Decorations: thin proud strips and patches on the analytic surface (spec §8, wardrobe G1–G5, G13), lifted along the
            // analytic normal; edge rows sit 1 mm under the surface so nothing floats. Weights follow the torso rule at each point.
            // ---------------------------------------------------------------------------------------------------------
            /// Outermost layer of garment g at y (its yoke / collar fold / belt where those cover y), or null.
            Layer TopOf(Garment g, float y)
            {
                Layer best = null;
                foreach (var L in torsoL) if (L.g == g && L.Covers(y) && (best == null || L.t >= best.t)) best = L;
                return best;
            }

            Vector3 SurfNormal(float ip, float th, float off)
            {
                const float e = 1e-3f;
                TRow rg = InterpT(ip);
                Vector3 dTh = TorsoPt(rg, th + e, off) - TorsoPt(rg, th - e, off);
                Vector3 dIp = TorsoPt(InterpT(ip + e), th, off) - TorsoPt(InterpT(ip - e), th, off);
                return Unit(Vector3.Cross(dTh, dIp));
            }

            /// Point on layer L at (row parameter, angle), lifted along the analytic normal.
            Vector3 Deco(float ip, float th, Layer L, float lift)
            {
                TRow rg = InterpT(ip);
                float off = LayerOff(L, rg, th);
                return TorsoPt(rg, th, off) + SurfNormal(ip, th, off) * lift;
            }

            int DecoV(Vector3 pt, Look look) => V(pt, look, TorsoW(pt.y, pt.x));

            static Vector3 Outward(Vector3 pt) => new Vector3(pt.x, 0f, pt.z);

            /// Distinct ring parameters of the torso rows whose y lies in [yLo, yHi] (above the drop crotch).
            List<float> RowsIn(float yLo, float yHi)
            {
                var ips = new List<float>();
                foreach (var r in tor)
                    if (r.param >= 0f && r.rg.y >= yLo - 1e-5f && r.rg.y <= yHi + 1e-5f && (ips.Count == 0 || ips[ips.Count - 1] != r.param)) ips.Add(r.param);
                return ips;
            }

            /// Vertical front strip along the torso rows: per row the lateral offsets xs (m) lifted by 'lift'; end rows buried.
            void FrontStrip(Layer main, float[] xs, float[] lift, Look look)
            {
                Garment g = main.g;
                List<float> ips = RowsIn(main.lo, main.hi);
                if (ips.Count < 2) return;
                var grid = new int[ips.Count, xs.Length];
                for (int i = 0; i < ips.Count; i++)
                {
                    TRow rg = InterpT(ips[i]);
                    Layer L = TopOf(g, rg.y) ?? main;
                    float off = L.Offset(rg.y);
                    bool end = i == 0 || i == ips.Count - 1;
                    for (int c = 0; c < xs.Length; c++)
                        grid[i, c] = DecoV(Deco(ips[i], AngleAtX(rg, xs[c], off), L, end ? Mathf.Min(lift[c], -0.001f) : lift[c]), look);
                }
                Sheet(grid, Outward(P(grid[ips.Count / 2, xs.Length / 2])));
            }

            /// Facing bands along a front opening (open front or V-neck): a wall from the inner layer up to the facing top, the
            /// facing over the slope to column 2, buried beyond it.
            void Facings(Garment g, float raise)
            {
                var rowsG = new List<Ring>();   // one torso ring per row parameter where g's opening is cut
                foreach (var r in tor)
                    if (r.param >= 0f && r.Layer?.g == g && r.d.edge > 0f && (rowsG.Count == 0 || rowsG[rowsG.Count - 1].param != r.param)) rowsG.Add(r);
                int n = rowsG.Count;
                if (n < 2) return;
                Look look = new Look(MB.Shade(g.col, 0.85f), g.gloss);
                for (int side = -1; side <= 1; side += 2)
                {
                    var grid = new int[n, 4];
                    for (int i = 0; i < n; i++)
                    {
                        TRow rg = rowsG[i].rg;
                        Layer L = rowsG[i].Layer;
                        float off = L.Offset(rg.y), thE = rowsG[i].d.edge;   // the torso ring's own edge column
                        float inner = rowsG[i].d.innerOff, top = i == 0 || i == n - 1 ? off - 0.001f : off + raise;
                        float[] th = { thE, thE, 34f * Deg, 40f * Deg }, of = { inner - 0.001f, top, top, off - 0.001f };
                        for (int c = 0; c < 4; c++) grid[i, c] = DecoV(TorsoPt(rg, side * th[c], of[c]), look);
                    }
                    Sheet(grid, Outward(P(grid[n / 2, 2])));
                }
            }

            /// Small hexagonal disc (button, press stud) standing on layer L, 13 vertices.
            void Disc(Layer L, float y, float th, float r, float h, float baseLift, Look look)
            {
                float ip = YToI(y);
                Vector3 c0 = Deco(ip, th, L, baseLift - 0.002f), c1 = Deco(ip, th, L, baseLift + h);
                Vector3 n = Unit(c1 - c0), t1 = Unit(Vector3.Cross(Vector3.up, n)), t2 = Vector3.Cross(n, t1);
                var bot = new int[6]; var topR = new int[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    Vector3 d = (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * r;
                    bot[i] = DecoV(c0 + d, look); topR[i] = DecoV(c1 + d, look);
                }
                Strip(bot, topR, Centroid(bot, topR));
                Fan(topR, DecoV(c1 + n * (0.15f * h), look));
            }

            /// Raised 4×4 patch centred at (y, th): outer ring buried, inner points lifted by 'raise'. 'slant' rotates it in the
            /// surface, 'taper' scales the top edge width (kangaroo pocket).
            void Patch(Layer L, float y, float th, float w, float h, float slant, float raise, float taper, Look look)
            {
                TRow rg = RingAtY(y);
                float off = LayerOff(L, rg, th);
                float arc = (TorsoPt(rg, th + 1e-3f, off) - TorsoPt(rg, th - 1e-3f, off)).magnitude / 2e-3f;   // metres per radian
                float e = Mathf.Min(0.0015f, 0.25f * Mathf.Min(w, h));   // deviation: .004 walls carried all the contrast (pockets read as ticks)
                float[] us = { -0.5f * w, -0.5f * w + e, 0.5f * w - e, 0.5f * w }, vs = { -0.5f * h, -0.5f * h + e, 0.5f * h - e, 0.5f * h };
                var grid = new int[4, 4];
                for (int i = 0; i < 4; i++)
                    for (int c = 0; c < 4; c++)
                    {
                        float u = us[c] * Lerp(1f, taper, vs[i] / h + 0.5f);
                        bool edge = i == 0 || i == 3 || c == 0 || c == 3;
                        grid[i, c] = DecoV(Deco(YToI(y + vs[i] + u * Mathf.Tan(slant)), th + u / arc, L, edge ? -0.001f : raise), look);
                    }
                Sheet(grid, Outward(P(grid[1, 1]) + P(grid[2, 2])));
            }

            /// True when garment g is the visible (outermost) torso surface at y.
            bool Visible(Garment g, float y) => Outer(torsoL, y)?.g == g;

            void Decorate(Garment g)
            {
                Layer main = torsoL.Find(L => L.g == g && L.main);
                if (main == null) return;
                float lo = Mathf.Max(main.lo, rows[0].y);   // a şalvar's layer reaches below the seat ring, where Deco cannot place anything
                Look Trim(float f) => new Look(MB.Shade(g.col, f), g.gloss);
                if (g.zip && !g.openFront) FrontStrip(main, new[] { -0.008f, 0f, 0.008f }, new[] { -0.001f, 0.004f, -0.001f }, new Look(MB.Shade(g.col, 0.6f), Mathf.Max(g.gloss, .3f)));
                if (g.placket && !g.openFront)
                {
                    FrontStrip(main, new[] { -0.025f, -0.021f, 0.021f, 0.025f }, new[] { -0.001f, 0.006f, 0.006f, -0.001f }, Trim(0.92f));
                    Spaced(g.studs, lo + 0.08f * k, Mathf.Min(main.hi, p.yNeck) - 0.06f * k, y =>
                    {
                        Layer L = TopOf(g, y);
                        if (L != null) Disc(L, y, 0f, 0.012f, 0.006f, 0.006f, new Look(MB.Shade(g.col, 0.55f), .5f));   // G2: disc r .012, h .006
                    });
                }
                if (g.openFront || !float.IsNaN(g.vNeckBottom)) Facings(g, g.openFront ? 0.006f : 0.003f);   // one facing per opening
                if (g.buttons > 0)
                {
                    float yHi = !float.IsNaN(g.vNeckBottom) ? g.vNeckBottom - 0.015f * k : Mathf.Min(main.hi, p.yChest + 0.1f * k) - 0.04f * k;
                    Color32 bc = g.col.r * 0.3f + g.col.g * 0.59f + g.col.b * 0.11f < 90f ? MB.Lerp(g.col, Rgb(0xE8E4DA), 0.35f) : MB.Shade(g.col, 0.55f);
                    Spaced(g.buttons, lo + 0.05f * k, yHi, y =>
                    {
                        Layer L = TopOf(g, y);
                        if (L == null) return;
                        float half = FrontGapHalf(L, y), lift = g.placket ? 0.006f : 0.002f, th = 0f;
                        if (g.openFront && half >= 0f) { th = 0.5f * (EdgeAngle(RingAtY(y), half, L.Offset(y)) + 34f * Deg); lift = 0.006f; }   // on the right facing
                        Disc(L, y, th, 0.008f, 0.003f, lift, new Look(bc, .3f));
                    });
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    if (g.hipPockets)
                    {
                        float y = Mathf.Clamp(p.yHip - 0.03f, lo + 0.04f, main.hi - 0.1f);
                        if (Visible(g, y)) Patch(TopOf(g, y), y, AngleAtX(RingAtY(y), s * 0.57f * p.hipRx, main.t), 0.12f * k, 0.032f * k, 0f, 0.003f, 1f, Trim(0.70f));
                    }
                    if (g.chestPockets)
                    {
                        float y = p.yChest + 0.045f * k;
                        if (Visible(g, y)) Patch(TopOf(g, y), y, AngleAtX(RingAtY(y), s * 0.46f * p.chestRx, main.t), 0.10f * k, 0.03f, 0f, 0.003f, 1f, Trim(0.70f));
                    }
                }
                if (g.kangaroo)
                {
                    float y = lo + 0.105f * k;
                    if (Visible(g, y)) Patch(TopOf(g, y), y, 0f, 0.26f * p.waistRx / 0.15f, 0.15f * k, 0f, 0.006f, 0.75f, Trim(0.93f));
                }
                if (g.belt)
                {
                    float y = (float.IsNaN(g.y1) ? p.yWaist : g.y1) - 0.0175f;
                    Layer belt = Outer(torsoL, y);
                    if (belt != null && belt.g == g && !belt.main) Patch(belt, y, 0f, 0.035f, 0.028f, 0f, 0.004f, 1f, new Look(Rgb(0xB8B8B0), .7f));
                }
                bool underBlanket = o.garments.FindLastIndex(h => h.kind == GarmentKind.Blanket) > o.garments.IndexOf(g);
                if (g.collar == Collar.Shirt && !underBlanket) ShirtCollar(g);
                if (g.hoodDown && !underBlanket) Hood(g);
            }

            static void Spaced(int n, float y0, float y1, Action<float> place)
            {
                for (int i = 0; i < n; i++) place(n == 1 ? 0.5f * (y0 + y1) : Lerp(y1, y0, i / (n - 1f)));
            }

            /// Shirt collar: 2-ring band from neckR + .012 at yN − .005 out to neckR + .030 at yN − .035, open ±25° at the
            /// front, never inside the surface below it. Double-sided as a closed 1.5 mm slab: top sheet, bottom sheet offset along
            /// the band normal, and the edges between them.
            void ShirtCollar(Garment g)
            {
                const int Nc = 15;
                const float Thick = 0.0015f;
                float[] ys = { p.yNeck - 0.005f, p.yNeck - 0.035f * k }, rs = { p.neckR + 0.012f, p.neckR + 0.030f }, lifts = { 0.003f, 0.006f };
                // a garment worn over g up to the collar hides it (a shirt under a jacket with its own collar, a coat's stand
                // collar); building it anyway would wrap it outside that garment or duplicate the other collar exactly
                Layer over = Outer(torsoL, ys[0]);
                if (over != null && over.g != g) return;
                TRow[] rg = { RingAtY(ys[0]), RingAtY(ys[1]) };
                var between = new List<float> { Lerp(ys[0], ys[1], 0.25f), Lerp(ys[0], ys[1], 0.5f), Lerp(ys[0], ys[1], 0.75f) };
                foreach (var L in torsoL)
                    foreach (float y in new[] { L.lo, L.hi })
                        if (y < ys[0] && y > ys[1]) between.Add(y);
                var pts = new Vector3[2, Nc];
                var off = new float[2];
                for (int c = 0; c < Nc; c++)
                {
                    float th = Lerp(25f, 335f, c / (Nc - 1f)) * Deg;
                    for (int r = 0; r < 2; r++)
                    {
                        float bare = RadXZ(TorsoPt(rg[r], th, 0f) - new Vector3(0f, 0f, rg[r].cz));
                        off[r] = Mathf.Max(LayerOff(Outer(torsoL, ys[r]), rg[r], th) + lifts[r], rs[r] - bare);
                    }
                    // the straight band must clear every layer edge between its rows (a cardigan neckline under the collar),
                    // including the raised facings of an open front
                    float push = 0f;
                    foreach (float y in between)
                    {
                        Layer L = Outer(torsoL, y);
                        float clear = 0.003f + (L != null && FrontGapHalf(L, y) >= 0f ? 0.006f : 0f);
                        push = Mathf.Max(push, LayerOff(L, RingAtY(y), th) + clear - Lerp(off[0], off[1], (y - ys[0]) / (ys[1] - ys[0])));
                    }
                    for (int r = 0; r < 2; r++) pts[r, c] = TorsoPt(rg[r], th, off[r] + push);
                }
                Look look = new Look(g.col, g.gloss);
                var top = new int[2, Nc]; var bot = new int[2, Nc];
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < Nc; c++)
                    {
                        // band normal from the grid, turned away from the neck
                        Vector3 along = pts[1, c] - pts[0, c], around = pts[r, Mathf.Min(c + 1, Nc - 1)] - pts[r, Mathf.Max(c - 1, 0)];
                        Vector3 n = Unit(Vector3.Cross(around, along));
                        if (Vector3.Dot(n, Outward(pts[r, c])) < 0f) n = -n;
                        top[r, c] = DecoV(pts[r, c], look);
                        bot[r, c] = DecoV(pts[r, c] - n * Thick, look);
                    }
                Vector3 outward = Outward(P(top[0, Nc / 2]) + P(top[1, Nc / 2]));
                Sheet(top, outward);
                Sheet(bot, -outward);
                for (int c = 0; c + 1 < Nc; c++)       // inner and outer edges
                    for (int r = 0; r < 2; r++)
                        Quad(top[r, c], top[r, c + 1], bot[r, c + 1], bot[r, c], 0.5f * (P(top[1 - r, c]) + P(top[1 - r, c + 1])));
                for (int e = 0; e < 2; e++)              // the two ends at the front opening
                {
                    int c = e == 0 ? 0 : Nc - 1, cn = e == 0 ? 1 : Nc - 2;
                    Quad(top[0, c], top[1, c], bot[1, c], bot[0, c], 0.5f * (P(top[0, cn]) + P(top[1, cn])));
                }
            }

            /// Hood resting behind the neck: a crescent loft ~0.20 wide × 0.07 high × 0.05 thick, its opening side ×0.65 darker.
            void Hood(Garment g)
            {
                const int Ns = 7, Nc = 8;
                float yc = p.yNeck - 0.025f * k, ip = YToI(yc);
                TRow rg = InterpT(ip);
                Layer L = Outer(torsoL, yc);
                float thick = 0.05f * k, high = 0.07f * k;
                Look outer = new Look(g.col, g.gloss), inside = new Look(MB.Shade(g.col, 0.65f), g.gloss);
                var ring = new int[Ns][]; var cen = new Vector3[Ns];
                Vector3 Base(float th, out Vector3 n)
                {
                    float off = LayerOff(L, rg, th);
                    n = SurfNormal(ip, th, off);
                    return TorsoPt(rg, th, off);
                }
                for (int i = 0; i < Ns; i++)
                {
                    float u = (i + 1f) / (Ns + 1f), th = Lerp(105f, 255f, u) * Deg, sc = Mathf.Sqrt(Mathf.Sin(Mathf.PI * u));
                    Vector3 b = Base(th, out Vector3 n);
                    cen[i] = b + n * (0.5f * thick * sc);
                    ring[i] = new int[Nc];
                    for (int j = 0; j < Nc; j++)
                    {
                        float a = 2f * Mathf.PI * j / Nc, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        Vector3 pt = cen[i] + n * (ca * 0.5f * thick * sc) + Vector3.up * (sa * 0.5f * high * sc);
                        ring[i][j] = DecoV(pt, sa > 0.2f && ca < 0.3f ? inside : outer);
                    }
                }
                for (int i = 0; i + 1 < Ns; i++) Strip(ring[i], ring[i + 1], 0.5f * (cen[i] + cen[i + 1]));
                Fan(ring[0], DecoV(Base(105f * Deg, out Vector3 n0) + n0 * 0.002f, outer));
                Fan(ring[Ns - 1], DecoV(Base(255f * Deg, out Vector3 n1) + n1 * 0.002f, outer));
            }

            // ---------------------------------------------------------------------------------------------------------
            // Finishing: wet lower legs (wardrobe rule 6) and baked AO (spec §8 "Baked AO")
            // ---------------------------------------------------------------------------------------------------------
            void WetLegs(int from, int to)
            {
                Color32 mud = Rgb(0x6B5038);
                for (int i = from; i < to; i++)
                {
                    float y = mb.V[i].y;
                    if (y >= 0.45f) continue;
                    Color32 c = MB.Lerp(mb.C[i], mud, 0.55f * (1f - y / 0.45f));
                    c.a = mb.C[i].a;                                           // alpha is the wind weight (0 on the body)
                    mb.C[i] = c;
                }
            }

            void BakeAO()
            {
                for (int i = res.vertexStart; i < mb.Count; i++)
                {
                    Vector3 v = mb.V[i];
                    float f = Lerp(0.78f, 1f, Mathf.Clamp01(v.y / (0.62f * p.H)));                                     // height
                    f *= 1f - 0.18f * G(Mathf.Abs(v.x), 0.05f) * G(v.y - p.yCrotch, 0.07f);                             // crotch
                    if (Mathf.Abs(v.x) < 0.08f) f *= 1f - 0.22f * G(v.y - (chin - 0.015f), 0.03f);                       // under the chin
                    Color32 c = mb.C[i];
                    mb.C[i] = new Color32((byte)Mathf.Min(255, (int)(c.r * f)), (byte)Mathf.Min(255, (int)(c.g * f)), (byte)Mathf.Min(255, (int)(c.b * f)), c.a);
                }
            }

            // ---------------------------------------------------------------------------------------------------------
            // Skeleton rest pose (spec §1, arms hanging) and IK landmarks (animation spec §2.0, measured on this mesh)
            // ---------------------------------------------------------------------------------------------------------
            void Skeleton()
            {
                Vector3[] b = res.bonePos;
                b[SVSkel.Root] = Vector3.zero;
                b[SVSkel.Pelvis] = new Vector3(0f, p.yHip + 0.045f * k, -0.010f);
                b[SVSkel.Spine] = new Vector3(0f, p.yWaist, -0.020f);
                b[SVSkel.Chest] = new Vector3(0f, p.yChest - 0.020f, -0.030f);
                b[SVSkel.Neck] = new Vector3(0f, p.yNeck - 0.010f, -0.022f);
                b[SVSkel.Head] = new Vector3(0f, chin + 0.040f, -0.008f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 gh = new Vector3(s * p.ghX, p.yGH, p.ghZ);
                    b[SVSkel.Clav(s)] = new Vector3(s * 0.020f, p.yNeck - 0.040f * k, 0.040f * k);
                    b[SVSkel.UpArm(s)] = b[SVSkel.ShHlp(s)] = gh;
                    b[SVSkel.Fore(s)] = b[SVSkel.ElHlp(s)] = gh - new Vector3(0f, p.Lu, 0f);
                    b[SVSkel.Hand(s)] = gh - new Vector3(0f, sW, 0f);
                    b[SVSkel.Fingers(s)] = b[SVSkel.Hand(s)] + new Vector3(0f, -0.5f * p.Lh * p.hand, 0f);   // brief: Hand + (0, -.5 Lh hand, 0)
                    b[SVSkel.Index(s)] = b[SVSkel.Fingers(s)] + new Vector3(0f, 0f, 0.016f * p.hand);
                    b[SVSkel.Thumb(s)] = gh + new Vector3(-s * 0.004f * p.hand, -(sW + 0.08f * p.Lh), 0.026f * p.hand);   // thumb ring 0 centre
                    b[SVSkel.Thigh(s)] = b[SVSkel.HipHlp(s)] = new Vector3(s * p.hipX, p.yHip, 0f);
                    b[SVSkel.Shin(s)] = b[SVSkel.KnHlp(s)] = new Vector3(s * p.kneeX, p.yKnee, 0.006f);
                    b[SVSkel.Foot(s)] = new Vector3(s * p.ankleX, p.yAnkle, -0.012f);
                }
                // Deliberate deviation: the prototype put these 5 cm below the thigh joints, so a forward swing pushed the top of
                // the bell back into the body while the panels' lower half lagged the thighs.
                b[SVSkel.SkirtF] = new Vector3(0f, p.yHip, 0.02f);
                b[SVSkel.SkirtB] = new Vector3(0f, p.yHip, -0.02f);
            }

            /// Outermost surface point at (y, th): torso layer (with the skirt layer rule), or a skirt when it lies further out.
            Vector3 OuterPoint(float y, float th)
            {
                TRow rg = RingAtY(y);
                Vector3 pt = TorsoPt(rg, th, LayerOff(Outer(torsoL, y), rg, th));
                foreach (var S in skirts)
                    if (S.Covers(y)) { Vector3 q = SkirtPt(S, y, th); if (RadXZ(q) > RadXZ(pt)) pt = q; }
                return pt;
            }

            void Landmarks()
            {
                res.sternum = OuterPoint(p.yChest + 0.06f * k, 0f);                 // chest bone + .08 up, on the front
                res.belly = OuterPoint(p.yWaist - 0.02f, 0f);                        // spine bone − .02
                res.smallOfBack = OuterPoint(p.yHip + 0.115f * k, Mathf.PI);         // pelvis bone + .07, on the back
                res.hipSideL = OuterPoint(p.yHip + 0.085f * k, -0.5f * Mathf.PI);    // pelvis bone + .04, on the sides
                res.hipSideR = OuterPoint(p.yHip + 0.085f * k, 0.5f * Mathf.PI);
                res.pocketL = OuterPoint(p.yHip - 0.025f * k, -50f * Deg);          // pelvis bone − .07, front-side
                res.pocketR = OuterPoint(p.yHip - 0.025f * k, 50f * Deg);
            }
        }
    }
}
