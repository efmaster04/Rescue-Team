using System.Collections.Generic;
using UnityEngine;
using Beh = SelVadisi.Beh;
using Role = SelVadisi.Role;
using PropKind = SelVadisi.PropKind;

namespace SelVadisi.EditorBuild
{
    /// Village archetypes (costume + body + head + behaviour defaults).
    public enum Arch { Nene, Dede, Baba, Anne, Ciftci, Esnaf, Balikci, Genc, GencKiz, Cocuk, Kucuk, Muhtar, Kurtarici, EkipSefi, Saglikci, Gonullu }

    /// One generated character before it is built.
    public class CastMember
    {
        public int spotIndex; public PersonSpot spot;
        public Arch arch; public bool female; public Role role;
        public BodyParams body; public BodyOutfit outfit; public HeadSpec head;
        public List<PropKind> props = new List<PropKind>();
        public bool backpack, shoulderBag, lifeVest;
        public Color32 outer, legsCol, accent, packCol;
        public string outerFam = "", legsFam = "", headType = "", headFam = "", breaker = "";
        public bool evacuee, blanket, umbrella; public int umbrellaSide = 1;
        public Vector3 pos; public float yaw;
        public Beh[] pool = { Beh.Stand }; public float[] poolW = { 1f };
        public int partner = -1; public bool adultInPair;
        public bool hasSeat; public Vector3 seat, seatFwd; public float seatH = 0.45f; public int sitStyle;
        public bool seatProp;            // build a chair / crate under them (roof elders)
        public int domSide = 1;
        public float slouch;
        public Vector3 pointTarget;
        public Color32 umbrellaCol, clothCol;
        public int score;
    }

    public static partial class SVBuild
    {
        // ------------------------------------------------------------------ colour science (sRGB in, CIE L*, HSV chroma proxy)

        static float Lin(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        static float LStar(Color32 c)
        {
            float y = 0.2126f * Lin(c.r / 255f) + 0.7152f * Lin(c.g / 255f) + 0.0722f * Lin(c.b / 255f);
            return y > 0.008856f ? 116f * Mathf.Pow(y, 1f / 3f) - 16f : 903.3f * y;
        }
        static void Hsv(Color32 c, out float h, out float s, out float v) => Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out h, out s, out v);
        static float Chroma(Color32 c) { Hsv(c, out _, out float s, out float v); return s * v * 100f; }
        static float HueDeg(Color32 c) { Hsv(c, out float h, out _, out _); return h * 360f; }
        static float HueDiff(Color32 a, Color32 b) { float d = Mathf.Abs(HueDeg(a) - HueDeg(b)); return Mathf.Min(d, 360f - d); }
        static float RgbDist(Color32 a, Color32 b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        static readonly Dictionary<string, string[]> Families = new Dictionary<string, string[]>
        {
            { "RED", new[] { "C8372D", "D33A2F", "D9302B", "B8262B", "8E2F2F" } },
            { "WINE", new[] { "7A1F24", "6E2433", "8E2F3A", "7A2F3D", "5A2E2E" } },
            { "ORANGE", new[] { "E96F2A", "E8661C", "F27A1C", "F28A2E" } },
            { "YELLOW", new[] { "F2C230", "E8B923", "F2B33D", "D8B35A" } },
            { "OLIVE", new[] { "6B7A3E", "4A5A3A", "6B6A45", "7D7354", "5B6B3A" } },
            { "GREEN", new[] { "2E6B45", "3FAF6A", "3E6B45", "2F4A35", "3F5A4A", "6E8B5A", "B9C08F" } },
            { "TEAL", new[] { "2F5A54", "2F4F4A", "3E9E9A", "2E4A4A" } },
            { "BLUE", new[] { "2F7AD9", "2F6DB5", "3A6FB8", "4A6A94", "6B9AC4", "9DB0C9", "7E9CC0" } },
            { "NAVY", new[] { "1F2A3A", "23314F", "2A3550", "2F3550", "1F2C45", "22304A", "2E3440", "3D4F6A" } },
            { "PURPLE", new[] { "8A5AC8", "6E4A7A", "5E4A6A", "4A3A52", "3B3550", "6B2F4F" } },
            { "PINK", new[] { "E86AA6", "C86A9A", "C7A2B6", "A0485A", "8B3A62", "E9C9D6" } },
            { "CREAM", new[] { "F2F0EA", "EFEADF", "E8E2D4", "E9E9E4", "D9CDB8" } },
            { "GREY", new[] { "8A8F94", "8F8A80", "7A8088", "5A5F66", "C8C4BC", "4A4A52" } },
            { "BLACK", new[] { "1E1F22", "232629", "2B2F33", "2E2E30", "3A3A3C", "1C1D20" } },
            { "BROWN", new[] { "6B4E3D", "5B4636", "7A5A38", "4A4038", "3A2A20", "8A7A60", "BFA98A", "5A564E", "6B5E4A" } },
        };
        static List<(Color32 c, string fam)> famIndex;

        static string FamilyOf(Color32 c)
        {
            if (famIndex == null)
            {
                famIndex = new List<(Color32, string)>();
                foreach (var kv in Families) foreach (var hx in kv.Value) famIndex.Add((H(hx), kv.Key));
            }
            string best = "GREY"; float bd = float.MaxValue;
            foreach (var e in famIndex) { float d = RgbDist(c, e.c); if (d < bd) { bd = d; best = e.fam; } }
            return best;
        }
        static bool Neutral(string fam) => fam == "BLACK" || fam == "GREY" || fam == "CREAM";

        static Color32 Pk(System.Random r, params string[] hexes) => H(hexes[r.Next(hexes.Length)]);
        static bool Chance(System.Random r, double p) => r.NextDouble() < p;

        /// Wardrobe rule 7: hue +-4 deg, value +-5%; elders and farmers slightly washed out.
        static Color32 Wear(Color32 c, System.Random r, bool washed)
        {
            Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out float h, out float s, out float v);
            h = Mathf.Repeat(h + Rand(r, -4f, 4f) / 360f, 1f);
            v = Mathf.Clamp01(v * Rand(r, 0.95f, 1.05f));
            Color o = Color.HSVToRGB(h, s, v);
            Color32 res = new Color32((byte)(o.r * 255f), (byte)(o.g * 255f), (byte)(o.b * 255f), 255);
            if (washed) res = MB.Lerp(res, H("9A958C"), 0.10f);
            return res;
        }

        // ------------------------------------------------------------------ archetype roles and body

        static Role RoleOf(Arch a, bool female)
        {
            switch (a)
            {
                case Arch.Nene: return Role.ElderWoman;
                case Arch.Dede: return Role.ElderMan;
                case Arch.Anne: return Role.Woman;
                case Arch.GencKiz: return Role.Woman;
                case Arch.Genc: return Role.Teen;
                case Arch.Cocuk: case Arch.Kucuk: return Role.Child;
                case Arch.Kurtarici: case Arch.EkipSefi: case Arch.Saglikci: return Role.Worker;
                case Arch.Gonullu: return female ? Role.Woman : Role.Man;
                default: return Role.Man;
            }
        }

        static bool FemaleArch(Arch a, System.Random r)
        {
            switch (a)
            {
                case Arch.Nene: case Arch.Anne: case Arch.GencKiz: return true;
                case Arch.Cocuk: case Arch.Kucuk: return r.NextDouble() < 0.5;
                case Arch.Kurtarici: return r.NextDouble() < 0.15;
                case Arch.Saglikci: return r.NextDouble() < 0.5;
                case Arch.Gonullu: return r.NextDouble() < 0.4;
                default: return false;
            }
        }

        /// build: 0 slim, 1 average, 2 sturdy, 3 heavy (archetype weights)
        static int SampleBuild(Arch a, System.Random r)
        {
            float[] w;
            switch (a)
            {
                case Arch.Nene: w = new float[] { 5, 25, 40, 30 }; break;
                case Arch.Dede: w = new float[] { 30, 35, 25, 10 }; break;
                case Arch.Baba: w = new float[] { 15, 40, 30, 15 }; break;
                case Arch.Anne: w = new float[] { 20, 45, 25, 10 }; break;
                case Arch.Ciftci: w = new float[] { 10, 35, 40, 15 }; break;
                case Arch.Esnaf: w = new float[] { 10, 40, 30, 20 }; break;
                case Arch.Balikci: w = new float[] { 5, 35, 45, 15 }; break;
                case Arch.Genc: w = new float[] { 60, 30, 10, 0 }; break;
                case Arch.GencKiz: w = new float[] { 45, 40, 10, 5 }; break;
                case Arch.Cocuk: case Arch.Kucuk: w = new float[] { 30, 50, 20, 0 }; break;
                case Arch.Muhtar: w = new float[] { 0, 30, 60, 10 }; break;
                default: w = new float[] { 15, 45, 35, 5 }; break;
            }
            float tot = w[0] + w[1] + w[2] + w[3], u = (float)r.NextDouble() * tot;
            for (int i = 0; i < 4; i++) { u -= w[i]; if (u <= 0f) return i; }
            return 1;
        }

        static float Gauss(System.Random r, float mean, float sd)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            float z = (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
            return mean + Mathf.Clamp(z, -2f, 2f) * sd;
        }

        /// Scales every height-like value so the shod height becomes H (head height scales much less).
        static BodyParams ToHeight(BodyParams p, float H)
        {
            var q = p.Clone();
            float f = H / p.H;
            q.H = H; q.headH = p.headH * Mathf.Pow(f, 0.3f);
            // body below the chin scales with what is left after the head
            float bodyOld = p.H - p.headH, bodyNew = q.H - q.headH;
            float g = bodyNew / bodyOld;
            q.yAnkle *= g; q.yKnee *= g; q.yCrotch *= g; q.yHip *= g; q.yWaist *= g; q.yChest *= g; q.yArmpit *= g; q.yGH *= g; q.yNeck *= g;
            q.Lu *= g; q.Lf *= g; q.Lh *= Mathf.Lerp(1f, g, 0.7f);
            return q;
        }

        static BodyParams SampleBody(Arch a, bool female, int build, System.Random r)
        {
            string main, slim, heavy; float mean, sd;
            switch (a)
            {
                case Arch.Nene: main = "ELW"; slim = "AW"; heavy = "HW"; mean = 1.52f; sd = 0.04f; break;
                case Arch.Dede: main = "EL"; slim = "EL"; heavy = "AM"; mean = 1.68f; sd = 0.04f; break;
                case Arch.Anne: main = "AW"; slim = "TG"; heavy = "HW"; mean = 1.62f; sd = 0.045f; break;
                case Arch.GencKiz: main = "TG"; slim = "TG"; heavy = "AW"; mean = 1.63f; sd = 0.05f; break;
                case Arch.Genc: main = "TN"; slim = "TN"; heavy = "SY"; mean = 1.72f; sd = 0.07f; break;
                case Arch.Cocuk: main = "CH"; slim = "CH"; heavy = "TN"; mean = Rand(r, 1.16f, 1.42f); sd = 0.03f; break;
                case Arch.Kucuk: main = "CS"; slim = "CS"; heavy = "CS"; mean = Rand(r, 0.95f, 1.08f); sd = 0.02f; break;
                case Arch.Balikci: main = "AM"; slim = "SY"; heavy = "HM"; mean = 1.76f; sd = 0.05f; break;
                default:
                    if (female) { main = "AW"; slim = "TG"; heavy = "HW"; mean = 1.65f; sd = 0.05f; }
                    else { main = "AM"; slim = "SY"; heavy = "HM"; mean = a == Arch.Ciftci ? 1.72f : (a == Arch.Esnaf ? 1.71f : 1.75f); sd = 0.05f; }
                    break;
            }
            BodyParams p;
            if (build == 0) p = BodyParams.Blend(main, slim, Rand(r, 0.4f, 0.9f));
            else if (build == 2) p = BodyParams.Blend(main, heavy, Rand(r, 0.3f, 0.6f));
            else if (build == 3) p = BodyParams.Blend(main, heavy, Rand(r, 0.7f, 1.0f));
            else p = BodyParams.Blend(main, a == Arch.Cocuk ? "TN" : main, a == Arch.Cocuk ? Rand(r, 0f, 0.2f) : 0f);
            p = p.Jitter(r);
            return ToHeight(p, Gauss(r, mean, sd));
        }

        // ------------------------------------------------------------------ garment helpers (all heights from the body)

        static float SeatY(BodyParams p) => p.yCrotch + 0.28f * (p.yHip - p.yCrotch);
        static float NeckY(BodyParams p) => p.yNeck - 0.012f;
        static float SleeveLen(BodyParams p) => p.Lu + p.Lf;
        static float Kh(BodyParams p) => p.H / 1.76f;

        static Garment GTrousers(BodyParams p, Color32 c, float straight = 0.8f, bool belt = false, float hemUp = 0.11f)
            => new Garment { kind = GarmentKind.Trousers, col = c, t = 0.008f, y0 = SeatY(p) - 0.01f, y1 = p.yWaist - 0.03f, leg = hemUp * Kh(p), straight = straight, belt = belt, beltCol = H("3A2C22"), gloss = 0.08f };
        static Garment GJeans(BodyParams p, Color32 c, float straight = 0.7f)
            => new Garment { kind = GarmentKind.Jeans, col = c, t = 0.007f, y0 = SeatY(p) - 0.01f, y1 = p.yWaist - 0.04f, leg = 0.095f * Kh(p), straight = straight, gloss = 0.1f };
        static Garment GShorts(BodyParams p, Color32 c)
            => new Garment { kind = GarmentKind.Shorts, col = c, t = 0.008f, y0 = SeatY(p) - 0.01f, y1 = p.yWaist - 0.02f, leg = p.yKnee + 0.07f };
        static Garment GSalvar(BodyParams p, Color32 c)
            => new Garment { kind = GarmentKind.Salvar, col = c, t = 0.010f, y0 = SeatY(p) - 0.01f, y1 = p.yWaist - 0.02f, leg = 0.12f * Kh(p), gloss = 0.05f };
        static Garment GStockings(BodyParams p, Color32 c)
            => new Garment { kind = GarmentKind.Stockings, col = c, t = 0.003f, y0 = SeatY(p) - 0.01f, y1 = p.yWaist - 0.02f, leg = 0.10f };
        static Garment GSkirt(BodyParams p, Color32 c, float hem, float flare, Color32? band = null)
            => new Garment { kind = GarmentKind.Skirt, col = c, t = 0.010f, y1 = p.yWaist - 0.01f, hem = hem, flare = flare, band = band, gloss = 0.05f };
        static Garment GShirt(BodyParams p, Color32 c, bool rolled = false)
            => new Garment { kind = GarmentKind.Shirt, col = c, t = 0.005f, y0 = p.yWaist - 0.02f, y1 = NeckY(p) + 0.01f, sleeve = rolled ? p.Lu + 0.55f * p.Lf : SleeveLen(p) - 0.015f, collar = Collar.Shirt, cuff = MB.Shade(c, 0.9f), gloss = 0.05f };
        static Garment GBlouse(BodyParams p, Color32 c)
            => new Garment { kind = GarmentKind.Blouse, col = c, t = 0.005f, y0 = p.yWaist - 0.02f, y1 = NeckY(p) + 0.005f, sleeve = SleeveLen(p) - 0.02f, collar = Collar.Crew, gloss = 0.05f };
        static Garment GTee(BodyParams p, Color32 c)
            => new Garment { kind = GarmentKind.Tee, col = c, t = 0.004f, y0 = p.yHip, y1 = NeckY(p), sleeve = 0.55f * p.Lu, collar = Collar.Crew };
        static Garment GSweater(BodyParams p, Color32 c, Collar collar, System.Random r)
            => new Garment { kind = GarmentKind.Sweater, col = c, t = 0.013f, y0 = p.yHip + Rand(r, -0.03f, 0.03f), y1 = NeckY(p) + (collar == Collar.Turtle ? 0.03f : 0.01f), sleeve = SleeveLen(p) - Rand(r, 0f, 0.03f), cuff = MB.Shade(c, 0.82f), band = MB.Shade(c, 0.85f), collar = collar, gloss = 0.05f };
        static Garment GVest(BodyParams p, Color32 c, Color32 inner, bool knit)
            => new Garment { kind = GarmentKind.Vest, col = c, t = 0.014f, y0 = p.yHip + 0.04f, y1 = NeckY(p) - 0.02f, sleeve = 0f, buttons = 4, vNeckBottom = p.yChest - 0.07f, inner = inner, band = knit ? MB.Shade(c, 0.85f) : (Color32?)null, gloss = 0.05f };
        static Garment GCardigan(BodyParams p, Color32 c, Color32 inner, bool longHem)
            => new Garment { kind = GarmentKind.Cardigan, col = c, t = 0.012f, y0 = longHem ? SeatY(p) - 0.012f : p.yHip - 0.03f, y1 = NeckY(p), sleeve = SleeveLen(p) - 0.03f, cuff = MB.Shade(c, 0.85f), openFront = true, gapTop = 0.02f, gapBottom = 0.03f, inner = inner, buttons = 5, hipPockets = true, gloss = 0.05f };
        static Garment GJacket(BodyParams p, Color32 c, bool open, Color32 inner, float gloss = 0.1f)
            => new Garment { kind = GarmentKind.Jacket, col = c, t = 0.017f, y0 = p.yHip + 0.005f, y1 = NeckY(p) + 0.012f, sleeve = SleeveLen(p) - 0.012f, cuff = MB.Shade(c, 0.85f), zip = !open, openFront = open, gapTop = 0.06f, gapBottom = 0.06f, inner = inner, collar = Collar.Stand, hipPockets = true, chestPockets = true, gloss = gloss };
        static Garment GPuffer(BodyParams p, Color32 c, System.Random r, bool longHem = false)
            => new Garment { kind = GarmentKind.Puffer, col = c, t = 0.020f, y0 = longHem ? SeatY(p) - 0.012f : p.yHip - 0.04f, y1 = NeckY(p) + 0.03f, sleeve = SleeveLen(p) - 0.015f, quilt = 0.012f, pitch = Rand(r, 0.07f, 0.095f), cuff = H("1E1E1E"), zip = true, collar = Collar.Stand, gloss = 0.35f };
        static Garment GRaincoat(BodyParams p, Color32 c, bool hoodUp, float gloss = 0.6f)
            => new Garment { kind = GarmentKind.Raincoat, col = c, t = 0.018f, y0 = Mathf.Max(p.yHip - 0.035f, SeatY(p) + 0.004f), y1 = NeckY(p) + 0.02f, sleeve = SleeveLen(p) - 0.01f, cuff = MB.Shade(c, 0.85f), placket = true, studs = 5, collar = Collar.Stand, hoodDown = !hoodUp, yoke = p.yChest + 0.02f, hipPockets = true, gloss = gloss };
        static Garment GCoatTail(BodyParams p, Color32 c, float hem, float flare, float gloss)
            => new Garment { kind = GarmentKind.CoatTail, col = c, t = 0.016f, y1 = p.yHip + 0.02f, hem = hem, flare = flare, band = MB.Shade(c, 0.85f), gloss = gloss };
        static Garment GHoodie(BodyParams p, Color32 c, bool hoodUp)
            => new Garment { kind = GarmentKind.Hoodie, col = c, t = 0.016f, y0 = p.yHip + 0.01f, y1 = NeckY(p) + 0.01f, sleeve = SleeveLen(p) - 0.005f, cuff = MB.Shade(c, 0.8f), band = MB.Shade(c, 0.8f), kangaroo = true, hoodDown = !hoodUp, collar = Collar.Crew, gloss = 0.05f };
        static Garment GCoverall(BodyParams p, Color32 c)
        {
            var g = new Garment { kind = GarmentKind.Coverall, col = c, t = 0.014f, y0 = SeatY(p) - 0.012f, y1 = NeckY(p) + 0.02f, sleeve = SleeveLen(p) - 0.012f, leg = 0.16f * Kh(p), straight = 1f, zip = true, collar = Collar.Stand, chestPockets = true, cuff = MB.Shade(c, 0.85f), gloss = 0.1f, emitStripes = 0.3f };
            Color32 tape = H("E6E9EB");
            g.stripes.Add((p.yWaist + 0.06f, p.yWaist + 0.11f, tape));
            g.stripes.Add((p.yChest + 0.02f, p.yChest + 0.07f, tape));
            g.armStripes.Add((p.Lu + 0.62f * p.Lf, p.Lu + 0.62f * p.Lf + 0.04f, tape));
            g.armStripes.Add((p.Lu + 0.78f * p.Lf, p.Lu + 0.78f * p.Lf + 0.04f, tape));
            g.legStripes.Add((0.30f * Kh(p), 0.34f * Kh(p), tape));
            g.legStripes.Add((0.40f * Kh(p), 0.44f * Kh(p), tape));
            return g;
        }
        static Garment GHiVis(BodyParams p, Color32 c, Color32 tape, float t = 0.026f)
        {
            var g = new Garment { kind = GarmentKind.HiVisVest, col = c, t = t, y0 = p.yHip + 0.02f, y1 = NeckY(p) - 0.025f, sleeve = 0f, zip = true, gloss = 0.25f, emitStripes = 0.25f };
            g.stripes.Add((p.yWaist - 0.01f, p.yWaist + 0.035f, tape));
            g.stripes.Add((p.yChest - 0.035f, p.yChest + 0.01f, tape));
            return g;
        }
        static Garment GBlanket(BodyParams p, Color32 c, Color32 border, bool foil)
        {
            var g = new Garment { kind = GarmentKind.Blanket, col = c, t = 0.045f, y0 = Mathf.Max(p.yHip - 0.035f, SeatY(p) + 0.004f), y1 = NeckY(p) + 0.03f, sleeve = p.Lu + (foil ? 0.35f : 0.6f) * p.Lf, cuff = border, collar = Collar.Turtle, gloss = foil ? 0.85f : 0.04f, openFront = true, gapTop = 0.05f, gapBottom = 0.18f };
            if (!foil) { g.stripes.Add((p.yWaist + 0.10f, p.yWaist + 0.14f, border)); g.stripes.Add((p.yWaist + 0.18f, p.yWaist + 0.20f, border)); }
            else for (float y = p.yWaist - 0.12f; y < p.yNeck - 0.05f; y += 0.045f) g.stripes.Add((y, y + 0.015f, MB.Shade(c, 0.78f)));   // crinkles
            return g;
        }

        // ------------------------------------------------------------------ costume sampling per archetype

        static HeadArch HeadArchOf(Arch a, bool female)
        {
            switch (a)
            {
                case Arch.Nene: return HeadArch.OldWoman;
                case Arch.Dede: return HeadArch.OldMan;
                case Arch.Anne: case Arch.GencKiz: return HeadArch.Woman;
                case Arch.Cocuk: case Arch.Kucuk: return female ? HeadArch.Girl : HeadArch.Boy;
                case Arch.Kurtarici: case Arch.EkipSefi: case Arch.Saglikci: return HeadArch.Worker;
                case Arch.Gonullu: return female ? HeadArch.Woman : HeadArch.Man;
                default: return HeadArch.Man;
            }
        }

        static void Scarf(HeadSpec h, string kind, Color32 main, Color32 accent, System.Random r)
        {
            h.headwear = kind; h.hwMain = main; h.hwAccent = accent;
            double u = r.NextDouble();
            h.scarfPattern = u < 0.55 ? "dots" : (u < 0.65 ? "border" : "plain");
        }

        /// One candidate costume for an archetype at a location. Returns false if the colour rules reject it.
        static CastMember SampleLook(Arch a, bool female, SpotLoc loc, System.Random r)
        {
            var m = new CastMember { arch = a, female = female, role = RoleOf(a, female) };
            int build = SampleBuild(a, r);
            var p = SampleBody(a, female, build, r);
            m.body = p;
            var o = new BodyOutfit { skin = H("E0AC88") };
            var hd = SVHead.Sample(HeadArchOf(a, female), female, r);
            o.skin = hd.skin;
            m.outfit = o; m.head = hd;
            if (hd.headwear == "hood") hd.headwear = "none";
            var G = o.garments;
            bool roof = loc == SpotLoc.Roof || loc == SpotLoc.Balcony;
            bool washed = a == Arch.Nene || a == Arch.Dede || a == Arch.Ciftci;
            Color32 W(Color32 c) => Wear(c, r, washed);
            float k = Kh(p);
            switch (a)
            {
                case Arch.Nene:
                {
                    // headscarf almost always: Black Sea back-tie, chin-tie or a modern wrap
                    double u = r.NextDouble();
                    Color32 sc = Pk(r, "EFEADF", "1E1F22", "C7A2B6", "9DB0C9", "B9C08F", "D8B35A", "8B3A62", "6E8B5A");
                    Color32 oya = sc.r + sc.g + sc.b < 200 ? H("E8E2D4") : Pk(r, "D9302B", "C8463B", "E5C14A");
                    if (u < 0.55) Scarf(hd, "scarf_nape", W(sc), oya, r);
                    else if (u < 0.85) Scarf(hd, "scarf_chin", W(sc), oya, r);
                    else if (u < 0.95) Scarf(hd, "hijab", W(sc), oya, r);
                    Color32 blouse = Pk(r, "E8E2D4", "C7A2B6", "9DB0C9");
                    G.Add(GBlouse(p, blouse));
                    Color32 top;
                    if (Chance(r, 0.7))
                    {
                        top = W(Pk(r, "4A4A52", "6B4E3D", "7A2F3D", "2F4F4A", "3D4F6A", "5E4A6A"));
                        G.Add(GCardigan(p, top, blouse, true));
                        G.Add(GCoatTail(p, top, p.yKnee + Rand(r, 0.12f, 0.2f) * k, 0.02f, 0.05f));
                        m.breaker = "cardigan";
                    }
                    else { top = W(Pk(r, "3E3A36", "5B4636", "2F3A4A", "6A6A5A", "7B2E2E")); G.Add(GVest(p, top, blouse, true)); m.breaker = "vest"; }
                    if (Chance(r, 0.5))
                    {
                        Color32 sk = W(Pk(r, "2F3550", "3A3A3C", "4A3A52", "3F5A4A", "1E1F22"));
                        G.Add(GStockings(p, H("4A3F3A")));
                        G.Add(GSkirt(p, sk, 0.14f * k, Rand(r, 0.04f, 0.07f), Chance(r, 0.3) ? Pk(r, "8E1E24", "E8C55A", "3B6FD1") : (Color32?)null));
                        m.legsCol = sk;
                    }
                    else { Color32 sv = W(Pk(r, "3B3550", "4A4038", "5A2E2E", "3F5A4A")); G.Add(GSalvar(p, sv)); m.legsCol = sv; }
                    double f = r.NextDouble();
                    if (f < 0.6) { o.shoe = "galosh"; o.shoeCol = H("1C1D20"); o.sockCol = Pk(r, "B23A3A", "E8DCC8", "3A5A8A"); }
                    else if (f < 0.75) { o.shoe = "galosh"; o.shoeCol = H("22304A"); o.sockCol = Pk(r, "B23A3A", "E8DCC8"); }
                    else { o.shoe = "rainboot"; o.shoeCol = H("2F4A35"); }
                    m.outer = top;
                    if (Chance(r, 0.3)) m.props.Add(PropKind.Cane);
                    if (Chance(r, 0.15)) m.props.Add(PropKind.Beads);
                    m.slouch = Rand(r, 10f, 16f);
                    break;
                }
                case Arch.Dede:
                {
                    double u = r.NextDouble();
                    if (u < 0.58) { hd.headwear = "flatcap"; hd.hwMain = W(Pk(r, "4A4038", "5A564E", "6B5E4A", "8A7A62", "4F5A66")); }
                    else if (u < 0.75) { hd.headwear = "beanie"; hd.hwMain = Pk(r, "3A3A3C", "5A2E2E", "2F4A5E"); hd.hwAccent = MB.Shade(hd.hwMain, 0.9f); }
                    else { hd.headwear = "none"; if (Chance(r, 0.6)) hd.hairStyle = "balding"; }
                    if (Chance(r, 0.85)) hd.facial = Chance(r, 0.55) ? "walrus" : "classic";
                    if (Chance(r, 0.25)) hd.facial = Chance(r, 0.5) ? "fullbeard" : "longbeard";
                    Color32 shirt = Pk(r, "E8E2D4", "B8C4CC", "8A9AAA");
                    G.Add(GShirt(p, shirt));
                    Color32 top;
                    if (Chance(r, 0.6)) { top = W(Pk(r, "3E3A36", "5B4636", "2F3A4A", "6A6A5A", "7B2E2E")); G.Add(GVest(p, top, shirt, Chance(r, 0.5))); m.breaker = "vest"; }
                    else { top = W(Pk(r, "3A3632", "4A4A48", "2E3440", "5A4E40")); var j = GJacket(p, top, true, shirt); j.zip = false; j.buttons = 3; j.collar = Collar.Shirt; G.Add(j); m.breaker = "jacket"; }
                    Color32 tr = W(Pk(r, "3A3A3C", "4A4038", "2E3440", "5A5048"));
                    G.Add(GTrousers(p, tr, 1f, true, 0.07f));
                    double f = r.NextDouble();
                    if (f < 0.5) { o.shoe = "leather"; o.shoeCol = Pk(r, "2A2420", "1E1F22"); }
                    else if (f < 0.7) { o.shoe = "galosh"; o.shoeCol = H("1C1D20"); o.sockCol = Pk(r, "B23A3A", "3A5A8A", "E8DCC8"); }
                    else { o.shoe = "rainboot"; o.shoeCol = H("1E2022"); }
                    m.outer = top; m.legsCol = tr;
                    if (Chance(r, 0.4)) m.props.Add(PropKind.Beads);
                    if (Chance(r, 0.35)) m.props.Add(PropKind.Cane);
                    hd.glasses = hd.glasses || Chance(r, 0.3);
                    m.slouch = Rand(r, 8f, 15f);
                    break;
                }
                case Arch.Baba:
                case Arch.Gonullu when !female:
                {
                    if (Chance(r, 0.2)) { hd.headwear = "beanie"; hd.hwMain = Pk(r, "2E2E30", "3A4A5A", "5A2E2E"); hd.hwAccent = MB.Shade(hd.hwMain, 0.9f); }
                    Color32 inner = Pk(r, "8A8F94", "7A2F3D", "2F4A6E", "E8E2D4", "3F5A4A");
                    double u = r.NextDouble();
                    Color32 top;
                    if (u < 0.5) { top = W(Pk(r, "1F2A3A", "2B2F33", "4A5A3A", "2E4F6E", "5A2E2E", "C8372D", "2F6DB5")); G.Add(GTee(p, inner)); G.Add(GPuffer(p, top, r)); m.breaker = "puffer"; }
                    else if (u < 0.7) { top = W(Pk(r, "4A5A6A", "3F5A4A", "6B6A45")); G.Add(GSweater(p, inner, Collar.Crew, r)); G.Add(GJacket(p, top, Chance(r, 0.3), inner)); m.breaker = "jacket"; }
                    else if (u < 0.9) { top = W(Pk(r, "23314F", "2E6B45", "E96F2A", "F2C230")); bool hood = Chance(r, 0.2); G.Add(GSweater(p, inner, Collar.Crew, r)); G.Add(GRaincoat(p, top, hood, 0.5f)); if (hood) { hd.headwear = "hood"; hd.hwMain = top; } m.breaker = "raincoat"; }
                    else { top = H("2A2420"); G.Add(GSweater(p, inner, Collar.Rib, r)); G.Add(GJacket(p, top, true, inner, 0.45f)); m.breaker = "leather"; }
                    Color32 lg = Chance(r, 0.6) ? W(Pk(r, "2F4A73", "3A3F48", "3C5478")) : W(H("4A4A40"));
                    G.Add(Chance(r, 0.6) ? GJeans(p, lg) : GTrousers(p, lg));
                    double f = r.NextDouble();
                    if (f < 0.4) { o.shoe = "sneaker"; o.shoeCol = Pk(r, "2A2624", "3A3A3C", "24304A"); }
                    else if (f < 0.8) { o.shoe = Chance(r, 0.5) ? "rainboot" : "workboot"; o.shoeCol = Pk(r, "1F1F21", "2F4A35", "3B2E26"); }
                    else { o.shoe = "leather"; o.shoeCol = Pk(r, "2A2420", "1E1F22"); }
                    m.outer = top; m.legsCol = lg;
                    if (roof && Chance(r, 0.3)) m.props.Add(PropKind.Cloth);
                    if (Chance(r, 0.3)) m.props.Add(PropKind.Phone);
                    if (Chance(r, 0.15)) m.backpack = true;
                    if (a == Arch.Gonullu) { G.Add(GHiVis(p, Pk(r, "F2A33A", "C8D82E"), H("DADDE0"), 0.032f)); hd.headwear = "baseball"; hd.hwMain = Pk(r, "2E3A59", "1F2226"); m.props.Add(PropKind.Box); m.breaker = "hivis"; }
                    break;
                }
                case Arch.Anne:
                case Arch.Gonullu when female:
                {
                    double hu = r.NextDouble();
                    if (hu < 0.45) Scarf(hd, ScarfKind(r), W(Pk(r, "3A4A6A", "8A6E5A", "A0485A", "2E4A4A", "D9CDB8", "5A6E8A")), Pk(r, "F2E6D8", "E8D27A", "C24A6A"), r);
                    else if (hu < 0.6) { /* hood decided with the coat */ }
                    Color32 inner = Pk(r, "E8E2D4", "C7A2B6", "9DB0C9", "D8D2B8");
                    double u = r.NextDouble();
                    Color32 top; bool skirt = Chance(r, 0.5);
                    if (u < 0.4)
                    {
                        top = W(Pk(r, "2A3550", "1E1F22", "6E2433", "7D7354", "8F8A80", "3A5A5A", "D9473B", "2F6DB5"));
                        bool hood = hu >= 0.45 && hu < 0.6;
                        G.Add(GBlouse(p, inner)); G.Add(GRaincoat(p, top, hood, 0.45f));
                        G.Add(GCoatTail(p, top, p.yKnee + 0.02f, 0.04f, 0.45f));
                        if (hood) { hd.headwear = "hood"; hd.hwMain = top; }
                        m.breaker = "longcoat";
                    }
                    else if (u < 0.7)
                    {
                        top = W(Pk(r, "6B2F4F", "2F5A54", "8E2F3A", "1F2A3A", "C8C4BC"));
                        G.Add(GBlouse(p, inner)); G.Add(GPuffer(p, top, r, true));
                        G.Add(GCoatTail(p, top, p.yKnee, 0.03f, 0.35f));
                        m.breaker = "longpuffer";
                    }
                    else if (u < 0.85) { top = W(Pk(r, "E86AA6", "2F6DB5", "C8372D", "3E9E9A", "E8E2D4")); G.Add(GBlouse(p, inner)); G.Add(GPuffer(p, top, r)); skirt = true; m.breaker = "puffer"; }
                    else { top = W(Pk(r, "6E5440", "6E2F3A", "7E7A74", "34405E", "3D5244", "8A6E8E")); G.Add(GBlouse(p, inner)); G.Add(GCardigan(p, top, inner, false)); skirt = true; m.breaker = "cardigan"; }
                    Color32 lg;
                    if (skirt) { lg = W(Pk(r, "2E3550", "2A2A2E", "5A4636", "3E4A3A", "5A3E5E")); G.Add(GStockings(p, Pk(r, "4A3F3A", "2B2F33"))); G.Add(GSkirt(p, lg, Rand(r, 0.22f, 0.30f) * k, Rand(r, 0.04f, 0.07f))); }
                    else { lg = W(Pk(r, "3C5478", "2C3E5C", "2A2B2E", "5A5C60")); G.Add(GJeans(p, lg, 0.6f)); }
                    double f = r.NextDouble();
                    if (f < 0.35) { o.shoe = "leather"; o.shoeCol = H("2A2420"); }
                    else if (f < 0.65) { o.shoe = "sneaker"; o.shoeCol = Pk(r, "E9E6DF", "2A2624", "3B6FD1"); }
                    else { o.shoe = "rainboot"; o.shoeCol = Pk(r, "1F1F21", "25304A", "6B2F4F"); }
                    m.outer = top; m.legsCol = lg;
                    if (Chance(r, 0.35)) m.shoulderBag = true;
                    if (Chance(r, 0.3)) m.props.Add(PropKind.Phone);
                    if (a == Arch.Gonullu) { G.Add(GHiVis(p, Pk(r, "F2A33A", "C8D82E"), H("DADDE0"), 0.032f)); m.props.Add(PropKind.Box); m.breaker = "hivis"; }
                    break;
                }
                case Arch.Ciftci:
                {
                    double u = r.NextDouble();
                    if (u < 0.35) { hd.headwear = "beanie"; hd.hwMain = Pk(r, "3A3A3C", "5A4A3A", "2F4A35"); hd.hwAccent = MB.Shade(hd.hwMain, 0.9f); }
                    else if (u < 0.65) { hd.headwear = "flatcap"; hd.hwMain = W(Pk(r, "4A4038", "5A544C", "3E4448", "6B6458")); }
                    if (Chance(r, 0.6) && hd.facial == "none") hd.facial = "classic";
                    hd.stubble = Mathf.Max(hd.stubble, 0.25f);
                    // plaid flannel shirt: alternating bands
                    Color32 fl = Pk(r, "8E3A2E", "3A5A7A", "4A6A3A");
                    var shirt = GShirt(p, fl);
                    for (float y = p.yWaist; y < p.yNeck - 0.05f; y += 0.06f) shirt.stripes.Add((y, y + 0.025f, MB.Shade(fl, 0.72f)));
                    G.Add(shirt);
                    Color32 top;
                    if (Chance(r, 0.65)) { top = W(Pk(r, "6B6A45", "7A5A38", "3F5A4A", "4A5560")); var j = GJacket(p, top, Chance(r, 0.4), fl); j.collar = Collar.Shirt; G.Add(j); m.breaker = "workjacket"; }
                    else { top = W(Pk(r, "2F4A35", "3A3A3C", "4A4038")); var v = GVest(p, top, fl, false); v.quilt = 0.008f; v.pitch = 0.08f; v.vNeckBottom = float.NaN; v.zip = true; v.buttons = 0; G.Add(v); m.breaker = "vest"; }
                    Color32 tr = W(Pk(r, "4A4A40", "5A5048"));
                    G.Add(GTrousers(p, tr, 1f));
                    o.shoe = "rainboot"; o.shoeCol = Chance(r, 0.55) ? H("2F4A35") : H("1E2022"); o.wetLegs = true;
                    m.outer = top; m.legsCol = tr;
                    break;
                }
                case Arch.Esnaf:
                {
                    if (Chance(r, 0.3)) hd.hairStyle = "balding";
                    hd.glasses = hd.glasses || Chance(r, 0.5);
                    if (Chance(r, 0.6)) hd.facial = "classic";
                    Color32 shirt = Pk(r, "E8E2D4", "B8C4CC");
                    G.Add(GShirt(p, shirt));
                    Color32 top;
                    double u = r.NextDouble();
                    if (u < 0.6) { top = W(Pk(r, "2F3A4A", "5A2E2E", "4A4A52", "6A5A48")); var sw = GSweater(p, top, Collar.Crew, r); sw.vNeckBottom = p.yChest - 0.05f; sw.inner = shirt; G.Add(sw); m.breaker = "vneck"; }
                    else if (u < 0.9) { top = W(Pk(r, "7A8088", "4A5A7A")); var j = GJacket(p, top, false, shirt, 0.08f); j.zip = false; j.buttons = 5; j.collar = Collar.Shirt; G.Add(j); G.Add(GCoatTail(p, top, p.yKnee + 0.04f, 0.02f, 0.08f)); m.breaker = "dustcoat"; }
                    else { top = H("F2F0EA"); var j = GJacket(p, top, false, shirt, 0.05f); j.zip = false; j.buttons = 5; G.Add(j); G.Add(GCoatTail(p, top, p.yKnee + 0.04f, 0.02f, 0.05f)); hd.headwear = "baseball"; hd.hwMain = H("F2F0EA"); m.breaker = "baker"; }
                    Color32 tr = H("3A3A3C");
                    G.Add(GTrousers(p, tr, 1f, true));
                    o.shoe = "leather"; o.shoeCol = H("1E1F22");
                    m.outer = top; m.legsCol = tr;
                    if (Chance(r, 0.4)) m.props.Add(PropKind.Phone);
                    break;
                }
                case Arch.Balikci:
                {
                    double u = r.NextDouble();
                    if (u < 0.5) { hd.headwear = "beanie"; hd.hwMain = Pk(r, "22304A", "8E2F2F", "E8E2D4"); hd.hwAccent = MB.Shade(hd.hwMain, 0.85f); }
                    if (Chance(r, 0.5)) hd.facial = "fullbeard";
                    Color32 top = W(Pk(r, "F2C230", "E8B923", "E96F2A", "2E6B45"));
                    Color32 inner = Pk(r, "22304A", "8A8F94", "E8E2D4");
                    G.Add(GSweater(p, inner, Collar.Turtle, r));
                    var rc = GRaincoat(p, top, false, 0.65f); rc.y0 = p.yHip - 0.06f; rc.hoodDown = Chance(r, 0.5);
                    G.Add(rc);
                    Color32 tr = Pk(r, "2E3440", "3A3A3C");
                    G.Add(GTrousers(p, tr, 1f));
                    o.shoe = "rainboot"; o.shoeCol = Chance(r, 0.5) ? H("E9E6DF") : H("1E2022"); o.wetLegs = true;
                    m.outer = top; m.legsCol = tr; m.breaker = "oilskin";
                    if (roof && Chance(r, 0.4)) m.props.Add(PropKind.Cloth);
                    break;
                }
                case Arch.Genc:
                {
                    double u = r.NextDouble();
                    Color32 top;
                    if (u < 0.6)
                    {
                        top = W(Pk(r, "8A8F94", "1E1F22", "2E4F6E", "7A1F24", "3E6B45", "E8E2D4", "E96F2A"));
                        bool hood = Chance(r, 0.3);
                        G.Add(GHoodie(p, top, hood));
                        if (hood) { hd.headwear = "hood"; hd.hwMain = top; }
                        m.breaker = hood ? "hoodup" : "hoodie";
                    }
                    else if (u < 0.8) { top = W(Pk(r, "1E1F22", "2F6DB5", "C8372D")); G.Add(GTee(p, Pk(r, "E9E6DF", "8A8F94"))); G.Add(GPuffer(p, top, r)); m.breaker = "puffer"; }
                    else
                    {
                        top = H("1F2A3A"); var j = GJacket(p, top, false, H("E9E6DF"), 0.15f); j.collar = Collar.Stand;
                        j.armStripes.Add((0.05f, SleeveLen(p) - 0.05f, H("F2F0EA")));
                        G.Add(GTee(p, H("E9E6DF"))); G.Add(j); m.breaker = "track";
                    }
                    if (hd.headwear == "none" && Chance(r, 0.15)) { hd.headwear = "baseball"; hd.hwMain = Pk(r, "1E1F22", "C8372D", "2F6DB5"); }
                    Color32 lg = Chance(r, 0.75) ? W(Pk(r, "4A6A94", "2F4A73", "232629")) : H("3A3A3C");
                    G.Add(GJeans(p, lg, 0.5f));
                    o.shoe = "sneaker"; o.shoeCol = Chance(r, 0.5) ? H("E9E6DF") : Pk(r, "1E1F22", "C8372D");
                    m.outer = top; m.legsCol = lg;
                    if (Chance(r, 0.7)) m.props.Add(PropKind.Phone);
                    if (Chance(r, 0.3)) m.backpack = true;
                    break;
                }
                case Arch.GencKiz:
                {
                    if (Chance(r, 0.3)) Scarf(hd, "hijab", W(Pk(r, "A0485A", "3A6A8A", "D9CDB8", "6E4A7A", "2E4A4A")), Pk(r, "F2E6D8", "E8D27A"), r);
                    double u = r.NextDouble();
                    Color32 top; Color32 inner = Pk(r, "E9E6DF", "C7A2B6", "232629");
                    if (u < 0.35) { top = W(Pk(r, "4A5A3A", "2E3440", "8E2F3A")); G.Add(GTee(p, inner)); var pk = GJacket(p, top, false, inner, 0.15f); pk.collar = Collar.Stand; G.Add(pk); G.Add(GCoatTail(p, top, p.yKnee + 0.15f * k, 0.03f, 0.15f)); m.breaker = "parka"; }
                    else if (u < 0.7) { top = W(Pk(r, "E8E2D4", "E86AA6", "6B9AC4")); G.Add(GTee(p, inner)); G.Add(GPuffer(p, top, r)); m.breaker = "puffer"; }
                    else
                    {
                        top = W(Pk(r, "F2C230", "D33A2F", "3E9E9A"));
                        bool hood = hd.headwear == "none" && Chance(r, 0.4);
                        G.Add(GTee(p, inner)); G.Add(GRaincoat(p, top, hood));
                        if (hood) { hd.headwear = "hood"; hd.hwMain = top; }
                        m.breaker = "raincoat";
                    }
                    Color32 lg;
                    if (hd.headwear == "hijab" && Chance(r, 0.5)) { lg = W(Pk(r, "2F3550", "5E4A6A", "6B5E4A")); G.Add(GStockings(p, H("2B2F33"))); G.Add(GSkirt(p, lg, Rand(r, 0.12f, 0.2f) * k, 0.05f)); }
                    else { lg = Chance(r, 0.5) ? H("7E9CC0") : H("232629"); G.Add(GJeans(p, lg, 0.3f)); }
                    double f = r.NextDouble();
                    if (f < 0.5) { o.shoe = "sneaker"; o.shoeCol = H("E9E6DF"); }
                    else if (f < 0.8) { o.shoe = "leather"; o.shoeCol = H("2A2420"); }
                    else { o.shoe = "rainboot"; o.shoeCol = Pk(r, "1F1F21", "6B2F4F", "25304A"); }
                    m.outer = top; m.legsCol = lg;
                    if (Chance(r, 0.6)) m.props.Add(PropKind.Phone);
                    if (Chance(r, 0.4)) m.backpack = true; else if (Chance(r, 0.3)) m.shoulderBag = true;
                    break;
                }
                case Arch.Cocuk:
                case Arch.Kucuk:
                {
                    bool small = a == Arch.Kucuk;
                    if (hd.headwear == "beanie" || hd.headwear == "hood") hd.headwear = "none";
                    double u = r.NextDouble();
                    Color32 top;
                    bool hood = Chance(r, small ? 0.3 : 0.15);
                    if (u < (small ? 0.4 : 0.45))
                    {
                        top = Pk(r, "F2C230", "F2C230", "D9302B", "2F7AD9", "3FAF6A", "E86AA6", "F28A2E", "8A5AC8");
                        if (small) { var suit = GCoverall(p, top); suit.stripes.Clear(); suit.armStripes.Clear(); suit.legStripes.Clear(); suit.gloss = 0.55f; suit.leg = 0.11f; G.Add(suit); }
                        else
                        {
                            G.Add(GTee(p, Pk(r, "E9E6DF", "8A8F94")));
                            var rc = GRaincoat(p, top, hood, 0.6f);
                            G.Add(rc);
                            var tail = GCoatTail(p, top, p.yKnee + 0.03f, 0.04f, 0.6f);
                            tail.band = H("E6E9EB");
                            G.Add(tail);
                        }
                        m.breaker = "raincoat";
                    }
                    else if (u < 0.85) { top = Pk(r, "E86AA6", "2F7AD9", "F2B33D", "3FAF6A", "D9302B", "8A5AC8"); G.Add(GTee(p, H("E9E6DF"))); G.Add(GPuffer(p, top, r)); m.breaker = "puffer"; }
                    else { top = Pk(r, "8A8F94", "C8463B", "3B6FD1", "D982A8", "2E2F33"); G.Add(GHoodie(p, top, hood)); m.breaker = "hoodie"; }
                    if (hood) { hd.headwear = "hood"; hd.hwMain = top; }
                    else if (Chance(r, 0.2)) { hd.headwear = "beanie"; hd.hwMain = Pk(r, "D9302B", "2F7AD9", "F2B33D", "E86AA6", "3FAF6A"); hd.hwAccent = Chance(r, 0.5) ? MB.Shade(hd.hwMain, 0.85f) : Pk(r, "E9E6DF", "F2C230"); }
                    Color32 lg = Pk(r, "4A6A94", "2E3A5A", "C86A9A", "5A5F66");
                    if (!(small && u < 0.4)) G.Add(Chance(r, 0.7) ? GJeans(p, lg, 0.3f) : GTrousers(p, lg, 0.2f));
                    if (Chance(r, 0.6))
                    {
                        // boots contrast the coat
                        string fam = FamilyOf(top);
                        string[] opts = fam == "YELLOW" ? new[] { "D9302B", "2F7AD9", "3FAF6A" } : fam == "RED" ? new[] { "F2C230", "22304A" } : fam == "BLUE" ? new[] { "F2C230", "D9302B" } : fam == "GREEN" ? new[] { "F2C230", "D9302B" } : new[] { "8A5AC8", "F2C230" };
                        o.shoe = "rainboot"; o.shoeCol = Pk(r, opts);
                    }
                    else { o.shoe = "sneaker"; o.shoeCol = Pk(r, "E9E6DF", "D9302B", "2F7AD9"); }
                    m.outer = top; m.legsCol = lg;
                    if (!small && Chance(r, 0.4)) m.backpack = true;
                    hd.facial = "none"; hd.stubble = 0f;
                    break;
                }
                case Arch.Muhtar:
                {
                    if (Chance(r, 0.6)) { hd.headwear = "flatcap"; hd.hwMain = H("3A3632"); }
                    hd.facial = "walrus"; hd.hair = Pk(r, "9A9692", "C9C6C0");
                    Color32 shirt = H("E8E2D4");
                    G.Add(GShirt(p, shirt));
                    var sw = GSweater(p, H("4A4A52"), Collar.Crew, r); sw.vNeckBottom = p.yChest - 0.05f; sw.inner = shirt; G.Add(sw);
                    Color32 top = Pk(r, "2E2E30", "3A3632");
                    var coat = GJacket(p, top, true, H("4A4A52"), 0.06f); coat.zip = false; coat.buttons = 4; coat.collar = Collar.Shirt; coat.t = 0.02f;
                    G.Add(coat); G.Add(GCoatTail(p, top, p.yKnee + 0.05f, 0.03f, 0.06f));
                    G.Add(GTrousers(p, H("2E3440"), 1f, true));
                    o.shoe = "leather"; o.shoeCol = H("1E1F22");
                    m.outer = top; m.legsCol = H("2E3440"); m.breaker = "overcoat";
                    m.props.Add(PropKind.Phone);
                    break;
                }
                case Arch.Kurtarici:
                {
                    double hu = r.NextDouble();
                    if (hu < 0.85) { hd.headwear = "helmet"; hd.hwMain = hu < 0.45 ? H("F27A1C") : (hu < 0.75 ? H("F2C230") : H("B8262B")); hd.headlamp = Chance(r, 0.3); }
                    else { hd.headwear = "beanie"; hd.hwMain = H("22304A"); hd.hwAccent = H("1F2C45"); }
                    double u = r.NextDouble();
                    Color32 suit;
                    if (u < 0.45) { suit = H("E8661C"); var c = GCoverall(p, suit); c.stripes.Clear(); c.stripes.Add((p.yWaist + 0.06f, p.yWaist + 0.11f, H("E6E9EB"))); G.Add(c); }
                    else if (u < 0.9) { suit = H("1F2C45"); var c = GCoverall(p, suit); c.stripes.Add((p.yGH - 0.08f, p.yNeck, H("E8661C"))); G.Add(c); }
                    else { suit = H("B8262B"); var c = GCoverall(p, suit); c.gloss = 0.35f; G.Add(c); }
                    if (Chance(r, 0.6))
                    {
                        string fam = FamilyOf(suit);
                        Color32 lv = fam == "ORANGE" ? Pk(r, "E8C21C", "D02B2B") : (fam == "RED" ? Pk(r, "F25C05", "E8C21C") : Pk(r, "F25C05", "E8C21C", "D02B2B"));
                        var vest = GHiVis(p, lv, H("1E1F21"), 0.045f); vest.emitStripes = 0f; vest.collar = Collar.Turtle;
                        G.Add(vest); m.lifeVest = true;
                    }
                    o.shoe = "workboot"; o.shoeCol = H("1E1F21"); o.wetLegs = Chance(r, 0.5);
                    o.gloves = Chance(r, 0.85); o.gloveCol = Pk(r, "2E2E30", "2E2E30", "E07020", "E8C21C");
                    if (Chance(r, 0.6)) m.props.Add(PropKind.Radio);
                    m.outer = suit; m.legsCol = suit; m.breaker = "coverall";
                    break;
                }
                case Arch.EkipSefi:
                {
                    hd.headwear = "helmet"; hd.hwMain = H("F2F2EE");
                    if (Chance(r, 0.5)) hd.facial = "classic";
                    Color32 j = H("1F2C45");
                    G.Add(GJacket(p, j, false, H("2E3440")));
                    G.Add(GHiVis(p, H("D7E33A"), H("DADDE0")));
                    G.Add(GTrousers(p, j, 1f));
                    o.shoe = "workboot"; o.shoeCol = H("1E1F21");
                    m.props.Add(PropKind.Radio);
                    m.outer = H("D7E33A"); m.legsCol = j; m.breaker = "hivis";
                    break;
                }
                case Arch.Saglikci:
                {
                    hd.headwear = Chance(r, 0.5) ? "baseball" : "none"; hd.hwMain = H("22304A");
                    Color32 teal = H("2E4A5A");
                    G.Add(GJacket(p, teal, false, teal));
                    if (Chance(r, 0.6)) { var over = GHiVis(p, H("C8262B"), H("F2F0EA"), 0.02f); G.Add(over); m.outer = H("C8262B"); }
                    else { G.Add(GHiVis(p, H("C8D82E"), H("DADDE0"))); m.outer = H("C8D82E"); }
                    G.Add(GTrousers(p, teal, 1f));
                    o.shoe = "sneaker"; o.shoeCol = H("2A2624");
                    o.gloves = true; o.gloveCol = H("5A8ED9");
                    m.legsCol = teal; m.breaker = "medic";
                    break;
                }
            }
            if (hd.headwear == "hijab" || hd.headwear == "scarf_chin" || hd.headwear == "scarf_nape") foreach (var g in G) g.hoodDown = false;
            // a short dark moustache under dark side-parted hair must never read as a caricature: give it a walrus instead
            if (hd.facial == "classic" && hd.hairStyle == "sidepart" && LStar(hd.hair) < 30f) hd.facial = "walrus";
            // leg garments first: SVBody's layer rule lays a garment listed after a skirt / coat tail over it, which pushed
            // trousers out through long coats (jagged black band under the jacket hem, stripes down the thigh)
            var legs = G.FindAll(g => IsLegGarment(g.kind));
            if (legs.Count > 0) { G.RemoveAll(g => IsLegGarment(g.kind)); G.InsertRange(0, legs); }
            // value / visibility rules decide later (scoring); record families
            m.outerFam = FamilyOf(m.outer);
            m.legsFam = FamilyOf(m.legsCol);
            m.headType = hd.headwear == "none" ? "hair:" + hd.hairStyle : hd.headwear;
            m.headFam = hd.headwear == "none" ? FamilyOf(hd.hair) : FamilyOf(hd.hwMain);
            m.accent = hd.hwAccent;
            if (m.backpack) m.packCol = Pk(r, "2F6DB5", "D9302B", "1E1F22", "3E7A4A", "E8B923");
            return m;
        }

        static string ScarfKind(System.Random r)
        {
            double v = r.NextDouble();
            return v < 0.5 ? "scarf_nape" : v < 0.8 ? "scarf_chin" : "hijab";
        }

        static bool IsLegGarment(GarmentKind k)
            => k == GarmentKind.Stockings || k == GarmentKind.Trousers || k == GarmentKind.Jeans || k == GarmentKind.Shorts || k == GarmentKind.Salvar;

        /// Colour rules R1-R5 (wardrobe 6.4). Returns a penalty (0 = fine, large = reject).
        static int ColourPenalty(CastMember m, SpotLoc loc)
        {
            int pen = 0;
            float L = LStar(m.outer), C = Chroma(m.outer), hue = HueDeg(m.outer);
            bool roof = loc == SpotLoc.Roof || loc == SpotLoc.Balcony;
            if (m.role != Role.Worker)
            {
                if (roof && L > 50f && C < 25f) pen += 400;                                   // light neutrals vanish on the pale terraces
                if (loc == SpotLoc.Bridge && L > 40f && L < 70f && C < 20f) pen += 250;     // grey on the grey deck
                if (roof && hue >= 20f && hue <= 60f && C > 15f && C < 45f && L > 30f && L < 60f && m.breaker != "vest") pen += 300;
                if ((loc == SpotLoc.Plateau || loc == SpotLoc.Tent) && L > 30f && L < 50f && C < 20f) pen += 200;
                if (loc == SpotLoc.Pad && L > 58f && C < 20f) pen += 200;
                if (Mathf.Abs(L - LStar(m.legsCol)) < 15f) pen += 150;
                Color32 headC = m.head.headwear == "none" ? m.head.hair : m.head.hwMain;
                if (m.head.headwear != "hood" && Mathf.Abs(LStar(headC) - L) < 12f && (FamilyOf(headC) == m.outerFam || HueDiff(headC, m.outer) < 60f)) pen += 200;
                if (m.role == Role.Child && C < 45f) pen += 60;
            }
            return pen;
        }
    }
}
