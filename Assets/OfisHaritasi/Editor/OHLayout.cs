using System.Collections.Generic;
using UnityEngine;

// Ofis haritasının yerleşim tanımı. Bütün sayılar dünya koordinatında (x doğu, z kuzey, metre).
// Ofis + garaj orijinde, önü güneye (−Z) bakar; önündeki Ana Cadde z = −10.02'de doğu-batı uzanır.
// Harita oynanabilir alanı x −500…500, z −420…280 (1000 × 700 m). Dışında 150 m tepe kuşağı var.
namespace OfisHaritasi.EditorTools
{
    public class OHProfile
    {
        public string name;
        public float cwL, cwR;          // şerit alanı yarı genişlikleri (sol/sağ, kenar yönüne göre)
        public float curb = 0.22f;
        public float swL, swR;          // kaldırım (ya da banket) genişliği
        public bool raised = true;      // true: bordürlü kaldırım, false: düz banket (kırsal)
        public float laneOff = 1.43f;
        public float speed = 13.9f;
        public int cls = 2;
        public float parkR;             // sağ kenarda park şeridi (şerit alanı içinde)
        public bool edgeLines;
        public float VergeTop { get { return raised ? OHLayout.SidewalkTop : OHLayout.ShoulderTop; } }
        public float Half(bool left) { return (left ? cwL + swL : cwR + swR) + curb; }
    }

    public class OHEdgeDef
    {
        public int id;
        public string name;
        public OHProfile prof;
        public List<Vector2> ctrl;
        public float fillet;
        public int jStart = -1, jEnd = -1;
        public bool tunnelStart, tunnelEnd;
        public float weight = 1f;
        public string zoneL = "", zoneR = "";
        // hesaplanan
        public OHPolyline line;
        public float trimStart, trimEnd;
        public float stopStart = 1f, stopEnd = 1f;
        public float portalS = -1f;     // tünel ağzının kenar üzerindeki s'si
        public int portalIndex = -1;
        public readonly List<Vector2> cutsL = new List<Vector2>(), cutsR = new List<Vector2>(); // alçak bordür aralıkları (s)
        public float Length { get { return line.Length; } }
        public float S0 { get { return trimStart; } }
        public float S1 { get { return Length - trimEnd; } }
    }

    public class OHArm
    {
        public int dir;          // 0 doğu, 1 kuzey, 2 batı, 3 güney
        public OHEdgeDef edge;
        public bool atStart;
        public float left, right, swLeft, swRight, curb;
        public bool raised;
        public float start;      // kavşak merkezinden kol başlangıcına uzaklık
        public float stop = 1f;  // kol başından durma çizgisine
        public Vector3 A { get { return OHLayout.DirVec(dir); } }
        public Vector3 N { get { return OHLayout.DirVec(dir + 1); } }
    }

    public class OHJunctionDef
    {
        public int id;
        public string name;
        public Vector2 pos;
        public bool signalized;
        public string major;     // "EW", "NS" ya da null (sınıfa göre)
        public readonly OHArm[] arms = new OHArm[4];
        public Vector3 C { get { return new Vector3(pos.x, OHLayout.RoadTop, pos.y); } }
        public int ArmCount { get { int n = 0; foreach (var a in arms) if (a != null) n++; return n; } }
    }

    public class OHTunnelDef
    {
        public string name;
        public OHEdgeDef edge;
        public bool atEnd;
        public Vector3 portal;   // ağız merkezi (yol yüksekliğinde)
        public Vector3 inward;   // tünelin içine doğru
        public float length = 45f;
        public float halfWidth = 5.2f;
        public float height = 5.9f;
        public Rect rect;        // XZ ızgaraya hizalı iç dikdörtgen (arazi deliği)
    }

    public static class OHLayout
    {
        public const float RoadTop = -0.12f, CurbTop = -0.02f, SidewalkTop = -0.07f, ShoulderTop = -0.125f;
        public const float TownGround = -0.16f, LegacyGround = -0.31f, LotGround = -0.08f;
        public const float PlayMinX = -500, PlayMaxX = 500, PlayMinZ = -420, PlayMaxZ = 280;
        public const float TerMinX = -650, TerMaxX = 650, TerMinZ = -570, TerMaxZ = 430, Cell = 5f;
        public static readonly Rect LegacyZone = new Rect(-22f, -6f, 66f, 30f);   // x −22…44, z −6…24
        public static readonly Rect OfficeBlock = new Rect(-10.5f, -5.6f, 20.5f, 18.4f);
        public static readonly Rect ParkRect = new Rect(-12f, -160f, 110f, 36f);

        public static Vector3 DirVec(int d)
        {
            switch (((d % 4) + 4) % 4) { case 0: return Vector3.right; case 1: return Vector3.forward; case 2: return Vector3.left; default: return Vector3.back; }
        }

        public static OHProfile Ana = new OHProfile { name = "Ana", cwL = 2.86f, cwR = 2.86f, curb = 0.22f, swL = 1.32f, swR = 1.70f, laneOff = 1.43f, speed = 13.9f, cls = 3 };
        public static OHProfile Sanayi = new OHProfile { name = "Sanayi", cwL = 3.1f, cwR = 3.1f, curb = 0.2f, swL = 1.6f, swR = 1.6f, laneOff = 1.55f, speed = 11.1f, cls = 2 };
        public static OHProfile Mahalle = new OHProfile { name = "Mahalle", cwL = 3.0f, cwR = 5.2f, curb = 0.2f, swL = 2.0f, swR = 2.0f, laneOff = 1.5f, speed = 8.9f, cls = 2, parkR = 2.2f };
        public static OHProfile Kirsal = new OHProfile { name = "Kirsal", cwL = 3.0f, cwR = 3.0f, curb = 0f, swL = 1.2f, swR = 1.2f, raised = false, laneOff = 1.5f, speed = 13.9f, cls = 1, edgeLines = true };

        // ---------------------------------------------------------------- dere

        public static float CreekX(float z) { return 285f + 18f * Mathf.Sin(z / 85f) + 8f * Mathf.Sin(z / 31f + 1f); }
        public static float CreekBed(float z)
        {
            float b = TownGround - 2.4f;
            if (z > 110f) b += (z - 110f) * 0.075f;
            if (z < -250f) b -= (-250f - z) * 0.01f;
            return b;
        }
        public const float CreekHalfBed = 4f, CreekBank = 0.5f, CreekWaterDepth = 0.6f;

        public static List<Vector3> CreekLine()
        {
            var l = new List<Vector3>();
            for (float z = TerMaxZ + 20; z >= TerMinZ - 20; z -= 3f) l.Add(new Vector3(CreekX(z), CreekBed(z) + CreekWaterDepth, z));
            return l;
        }

        // ---------------------------------------------------------------- tepe (doğal arazi)

        static float SmoothRamp(float t) { if (t <= 0) return 0; if (t >= 1) return 1f + 0.35f * (t - 1f); return t * t * (3 - 2 * t); }

        public static float NorthLine(float x) { return 100f + 22f * OHNoise.N(x / 170f, 3.1f) + (Mathf.Abs(x) < 90 ? -8f : 0f); }
        public static float SouthLine(float x) { return -262f - 18f * OHNoise.N(x / 150f, 8.4f); }
        public static float WestLine(float z) { return -425f + 14f * OHNoise.N(1.7f, z / 120f); }
        public static float EastLine(float z) { return 432f + 18f * OHNoise.N(6.2f, z / 110f); }

        public static float Natural(float x, float z)
        {
            float dN = z - NorthLine(x), dS = SouthLine(x) - z, dW = WestLine(z) - x, dE = x - EastLine(z);
            float h = 0;
            h += 38f * SmoothRamp(dN / 165f);
            h += 24f * SmoothRamp(dS / 120f);
            h += 30f * SmoothRamp(dW / 115f);
            h += 26f * SmoothRamp(dE / 105f);
            float mod = 0.72f + 0.55f * (OHNoise.Fbm(x / 140f, z / 140f) * 0.5f + 0.5f);
            h *= mod;
            // tepelerde ayrıntı
            float rough = Mathf.Clamp01(h / 8f);
            h += rough * 3.2f * OHNoise.Fbm(x / 45f + 11f, z / 45f - 4f, 3);
            // doğu tarlaları: yumuşak dalga
            if (x > 300f)
            {
                float w = Mathf.Clamp01((x - 300f) / 40f);
                h += w * 1.6f * (OHNoise.Fbm(x / 90f + 5f, z / 90f + 2f, 2) + 0.4f);
            }
            // oynanabilir alanın dışında yükselen kuşak (dünyanın ucu görünmesin)
            float ox = Mathf.Max(PlayMinX - x, x - PlayMaxX, 0), oz = Mathf.Max(PlayMinZ - z, z - PlayMaxZ, 0);
            float o = Mathf.Sqrt(ox * ox + oz * oz);
            h += 34f * Mathf.Clamp01(o / 140f) * (0.8f + 0.3f * OHNoise.N(x / 60f, z / 60f));
            return TownGround + Mathf.Max(0, h);
        }

        // ---------------------------------------------------------------- yol ağı

        public static readonly List<OHEdgeDef> Edges = new List<OHEdgeDef>();
        public static readonly List<OHJunctionDef> Junctions = new List<OHJunctionDef>();
        public static readonly List<OHTunnelDef> Tunnels = new List<OHTunnelDef>();

        static OHJunctionDef J(string name, float x, float z, bool sig = false, string major = null)
        {
            var j = new OHJunctionDef { id = Junctions.Count, name = name, pos = new Vector2(x, z), signalized = sig, major = major };
            Junctions.Add(j);
            return j;
        }

        static OHEdgeDef E(string name, OHProfile p, OHJunctionDef a, OHJunctionDef b, float fillet, float weight, string zl, string zr, params float[] xz)
        {
            var e = new OHEdgeDef { id = Edges.Count, name = name, prof = p, fillet = fillet, weight = weight, zoneL = zl, zoneR = zr, ctrl = new List<Vector2>() };
            for (int i = 0; i < xz.Length; i += 2) e.ctrl.Add(new Vector2(xz[i], xz[i + 1]));
            if (a != null) { e.jStart = a.id; e.ctrl.Insert(0, a.pos); }
            if (b != null) { e.jEnd = b.id; e.ctrl.Add(b.pos); }
            Edges.Add(e);
            return e;
        }

        public static void Define()
        {
            Edges.Clear(); Junctions.Clear(); Tunnels.Clear();
            var J1 = J("J1_BatiKavsak", -150, -10.02f, true);
            var J5 = J("J5_CarsiAgzi", -45, -10.02f);
            var J2 = J("J2_DoguKavsak", 130, -10.02f, true);
            var J14 = J("J14_BahceAgzi", -290, -10.02f);
            var J12 = J("J12_KirsalAgzi", 340, -10.02f);
            var J3 = J("J3_MahalleOrta", -150, -95, false, "NS");
            var J6 = J("J6_CarsiOrta", -45, -95, false, "EW");
            var J10 = J("J10_OkulOrta", 130, -95, false, "NS");
            var J4 = J("J4_MahalleGuney", -150, -180, false, "NS");
            var J7 = J("J7_CarsiGuney", -45, -180, false, "EW");
            var J11 = J("J11_OkulGuney", 130, -180, false, "EW");
            var J8 = J("J8_SanayiKuzey", -150, 75, false, "NS");

            // Ana Cadde (batıdan doğuya; sol = kuzey)
            var a0 = E("AnaCadde_BatiTunel", Ana, null, J14, 150, 0.8f, "ticari_kenar", "konut_kenar", -540, -32.5f, -430, -32.5f, -330, -10.02f);
            a0.tunnelStart = true;
            E("AnaCadde_1", Ana, J14, J1, 0, 1.6f, "ticari", "konut_cadde");
            E("AnaCadde_2", Ana, J1, J5, 0, 1.6f, "ticari", "cadde_apartman");
            E("AnaCadde_3", Ana, J5, J2, 0, 1.6f, "ticari", "cadde_apartman");
            E("AnaCadde_4", Ana, J2, J12, 0, 1.6f, "ticari_dogu", "ticari_dogu");
            var a5 = E("AnaCadde_DoguTunel", Ana, J12, null, 60, 0.8f, "kirsal", "kirsal", 385, -10.02f, 450, 42.5f, 540, 42.5f);
            a5.tunnelEnd = true;

            // Sanayi yolu ve arka yol
            E("SanayiYolu_1", Sanayi, J1, J8, 0, 1.0f, "ticari", "ticari");
            var s2 = E("SanayiYolu_KuzeyTunel", Sanayi, J8, null, 70, 0.8f, "", "", -150, 120, -277.5f, 215, -277.5f, 320);
            s2.tunnelEnd = true;
            E("ArkaSanayi", Sanayi, J8, J2, 25, 0.9f, "", "ticari_arka", 130, 75);

            // Mahalle caddesi ve güney tüneli
            E("MahalleCad_1", Mahalle, J1, J3, 0, 0.9f, "konut", "konut");
            E("MahalleCad_2", Mahalle, J3, J4, 0, 0.9f, "konut", "konut");
            var m3 = E("MahalleCad_GuneyTunel", Sanayi, J4, null, 80, 0.8f, "konut_kenar", "konut_kenar", -150, -250, -102.5f, -345, -102.5f, -460);
            m3.tunnelEnd = true;

            E("CarsiSok_1", Mahalle, J5, J6, 0, 0.8f, "konut", "konut");
            E("CarsiSok_2", Mahalle, J6, J7, 0, 0.8f, "konut", "konut");
            E("OkulSok_1", Mahalle, J2, J10, 0, 0.8f, "konut", "konut");
            E("OkulSok_2", Mahalle, J10, J11, 0, 0.8f, "konut", "park_kenar");
            E("BahceSok", Mahalle, J14, J3, 20, 0.7f, "konut", "konut", -290, -95);
            E("OrtaSok_1", Mahalle, J3, J6, 0, 0.8f, "konut", "konut");
            E("OrtaSok_2", Mahalle, J6, J10, 0, 0.8f, "konut", "park_kenar");
            E("GuneySok_1", Mahalle, J4, J7, 0, 0.8f, "konut", "ev");
            E("GuneySok_2", Mahalle, J7, J11, 0, 0.8f, "park_kenar", "ev");
            E("KirsalYol", Kirsal, J11, J12, 30, 0.7f, "kirsal", "kirsal", 340, -180);

            AddTunnel("BatiTuneli", a0, false);
            AddTunnel("DoguTuneli", a5, true);
            AddTunnel("KuzeyTuneli", s2, true);
            AddTunnel("GuneyTuneli", m3, true);
        }

        static void AddTunnel(string name, OHEdgeDef e, bool atEnd)
        {
            var t = new OHTunnelDef { name = name, edge = e, atEnd = atEnd };
            if (atEnd) e.tunnelEnd = true; else e.tunnelStart = true;
            Tunnels.Add(t);
        }

        public static OHJunctionDef Junction(int id) { return id >= 0 ? Junctions[id] : null; }

        public static Vector3 Flat(Vector2 p, float y = RoadTop) { return new Vector3(p.x, y, p.y); }
    }
}
