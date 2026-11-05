using UnityEngine;

namespace SelVadisi
{
    /// Bone layout shared by the character generator and the animator.
    /// Axis contract: the root sits on the ground under the feet, faces +Z, +X is the character's right.
    /// Every bone's rest localRotation is identity: arms hang (palms toward the thighs), legs are straight,
    /// limbs extend along local -Y, the spine and head extend +Y. The mesh is bound in an A-pose (upper arms
    /// at 45 degrees), which only changes the bind matrices, not this rest convention.
    public static class SVSkel
    {
        public const int Root = 0, Pelvis = 1, Spine = 2, Chest = 3, Neck = 4, Head = 5, Eyes = 6, Mouth = 7,
            ClavL = 8, UpArmL = 9, ForeL = 10, HandL = 11, FingersL = 12, IndexL = 13, ThumbL = 14,
            ClavR = 15, UpArmR = 16, ForeR = 17, HandR = 18, FingersR = 19, IndexR = 20, ThumbR = 21,
            ThighL = 22, ShinL = 23, FootL = 24, ThighR = 25, ShinR = 26, FootR = 27,
            ShHlpL = 28, ShHlpR = 29, ElHlpL = 30, ElHlpR = 31, HipHlpL = 32, HipHlpR = 33, KnHlpL = 34, KnHlpR = 35,
            SkirtF = 36, SkirtB = 37, Count = 38;

        public static readonly string[] Names =
        {
            "Kok", "Kalca", "Bel", "Gogus", "Boyun", "Bas", "Gozler", "Agiz",
            "SolKopruck", "SolOmuz", "SolDirsek", "SolEl", "SolParmak", "SolIsaret", "SolBasparmak",
            "SagKopruck", "SagOmuz", "SagDirsek", "SagEl", "SagParmak", "SagIsaret", "SagBasparmak",
            "SolUyluk", "SolDiz", "SolAyak", "SagUyluk", "SagDiz", "SagAyak",
            "SolOmuzYrd", "SagOmuzYrd", "SolDirsekYrd", "SagDirsekYrd", "SolKalcaYrd", "SagKalcaYrd", "SolDizYrd", "SagDizYrd",
            "EtekOn", "EtekArka"
        };

        public static readonly int[] Parent =
        {
            -1, Root, Pelvis, Spine, Chest, Neck, Head, Head,
            Chest, ClavL, UpArmL, ForeL, HandL, HandL, HandL,
            Chest, ClavR, UpArmR, ForeR, HandR, HandR, HandR,
            Pelvis, ThighL, ShinL, Pelvis, ThighR, ShinR,
            ClavL, ClavR, UpArmL, UpArmR, Pelvis, Pelvis, ThighL, ThighR,
            Pelvis, Pelvis
        };

        // side = -1 left, +1 right
        public static int Clav(int s) => s < 0 ? ClavL : ClavR;
        public static int UpArm(int s) => s < 0 ? UpArmL : UpArmR;
        public static int Fore(int s) => s < 0 ? ForeL : ForeR;
        public static int Hand(int s) => s < 0 ? HandL : HandR;
        public static int Fingers(int s) => s < 0 ? FingersL : FingersR;
        public static int Index(int s) => s < 0 ? IndexL : IndexR;
        public static int Thumb(int s) => s < 0 ? ThumbL : ThumbR;
        public static int Thigh(int s) => s < 0 ? ThighL : ThighR;
        public static int Shin(int s) => s < 0 ? ShinL : ShinR;
        public static int Foot(int s) => s < 0 ? FootL : FootR;
        public static int ShHlp(int s) => s < 0 ? ShHlpL : ShHlpR;
        public static int ElHlp(int s) => s < 0 ? ElHlpL : ElHlpR;
        public static int HipHlp(int s) => s < 0 ? HipHlpL : HipHlpR;
        public static int KnHlp(int s) => s < 0 ? KnHlpL : KnHlpR;

        public const float APose = 45f;
    }

    /// Small rotation toolkit. All angles in degrees.
    public static class RigMath
    {
        const float D2R = Mathf.Deg2Rad, R2D = Mathf.Rad2Deg;

        public static Quaternion Pelvis(float tilt, float yaw, float roll) => Quaternion.Euler(tilt, yaw, roll);
        /// bend+ forward, lean+ top toward the right, twist+ turn right
        public static Quaternion Up(float bend, float lean, float twist) => Quaternion.Euler(bend, twist, -lean);
        /// pitch+ looks down, yaw+ looks right, roll+ right ear toward the right shoulder
        public static Quaternion HeadQ(float pitch, float yaw, float roll) => Quaternion.Euler(pitch, yaw, -roll);
        public static Quaternion Clav(float shrug, float protract, int side) => Quaternion.Euler(0f, -side * protract, side * shrug);
        public static Quaternion Elbow(float flex) => Quaternion.Euler(-flex, 0f, 0f);
        public static Quaternion Knee(float flex) => Quaternion.Euler(flex, 0f, 0f);
        public static Quaternion Foot(float plantar, float toeOut, int side) => Quaternion.Euler(plantar, side * toeOut, 0f);
        public static Quaternion Curl(float deg) => Quaternion.Euler(-deg, 0f, 0f);
        public static Quaternion ThumbQ(float deg, int side) => Quaternion.AngleAxis(-side * deg, Vector3.up) * Quaternion.Euler(-deg * 0.35f, 0f, 0f);

        /// Upper arm (in chest space) or thigh (in pelvis space); the bone hangs along -Y at rest.
        /// elev: 0 down .. 90 horizontal .. 180 up. plane: 0 sideways, +90 forward, -90 backward, 120..150 across.
        /// twist+ is external rotation (palm turns forward when hanging).
        public static Quaternion Swing(float plane, float elev, float twist, float abd, int side)
        {
            float p = plane * D2R;
            Vector3 h = new Vector3(side * Mathf.Cos(p), 0f, Mathf.Sin(p));
            Vector3 axis = Vector3.Cross(Vector3.down, h);
            return Quaternion.AngleAxis(side * abd, Vector3.forward)
                 * Quaternion.AngleAxis(elev, axis)
                 * Quaternion.AngleAxis(side * twist, Vector3.up);
        }

        /// pron+ palm turns back/down, wflex+ toward the palm, wdev+ toward the thumb
        public static Quaternion HandFK(float pron, float wflex, float wdev, int side)
            => Quaternion.AngleAxis(-side * pron, Vector3.up)
             * Quaternion.AngleAxis(-side * wflex, Vector3.forward)
             * Quaternion.AngleAxis(-wdev, Vector3.right);

        /// Hand frame from directions: fingers = -Y, palm normal = -side*X, thumb side = +Z.
        public static Quaternion HandAim(Vector3 fingerDir, Vector3 palmDir, int side)
        {
            Vector3 f = fingerDir.sqrMagnitude > 1e-8f ? fingerDir.normalized : Vector3.down;
            Vector3 p = Vector3.ProjectOnPlane(palmDir, f);
            if (p.sqrMagnitude < 1e-6f) p = Vector3.ProjectOnPlane(-side * Vector3.right, f);
            p.Normalize();
            return Quaternion.LookRotation(side * Vector3.Cross(p, f), -f);
        }

        /// Analytic two-bone IK in root space. A = shoulder or hip, T = wrist or ankle target, pole = where the elbow/knee points.
        public static void TwoBone(Vector3 A, float l1, float l2, Vector3 T, Vector3 pole, bool leg,
                                   out Quaternion upperRS, out float flex, out Vector3 joint)
        {
            Vector3 toT = T - A; float L = (l1 + l2) * 0.999f, d = toT.magnitude;
            Vector3 dir = d > 1e-5f ? toT / d : Vector3.down;
            float dSoft = 0.96f * L;
            if (d > dSoft) d = dSoft + (L - dSoft) * (1f - Mathf.Exp(-(d - dSoft) / (L - dSoft)));
            d = Mathf.Max(d, Mathf.Abs(l1 - l2) + 0.01f);
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f), sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 bn = Vector3.ProjectOnPlane(pole, dir);
            if (bn.sqrMagnitude < 1e-6f) bn = Vector3.ProjectOnPlane(leg ? Vector3.forward : Vector3.back, dir);
            bn.Normalize();
            Vector3 upDir = dir * cosA + bn * sinA; joint = A + upDir * l1;
            Vector3 n1 = bn * cosA - dir * sinA;
            upperRS = Quaternion.LookRotation(leg ? n1 : -n1, -upDir);
            float cosE = Mathf.Clamp((l1 * l1 + l2 * l2 - d * d) / (2f * l1 * l2), -1f, 1f);
            flex = 180f - Mathf.Acos(cosE) * R2D;
        }

        public static float SS(float a, float b, float x)
        {
            if (Mathf.Approximately(a, b)) return x >= b ? 1f : 0f;
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static float Osc(float f, float t, float ph) => Mathf.Sin(2f * Mathf.PI * (f * t + ph));
        public static float Pump(float f, float t, float ph = 0f) => 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (f * t + ph));

        /// Smooth value noise in [-1, 1] (deterministic per seed).
        public static float Noise(float x, int seed)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            float a = Hash(i, seed), b = Hash(i + 1, seed);
            float u = f * f * (3f - 2f * f);
            return Mathf.Lerp(a, b, u);
        }

        public static float Hash(int i, int seed)
        {
            unchecked
            {
                uint h = (uint)(i * 374761393 + seed * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
            }
        }

        public static float Hash01(int i, int seed) => 0.5f + 0.5f * Hash(i, seed);
    }
}
