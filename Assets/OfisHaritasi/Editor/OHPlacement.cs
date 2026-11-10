using System.Collections.Generic;
using UnityEngine;

// Parseller, sokak donatısı, park, ağaçlar ve park etmiş araçların yerleşimi (deterministik).
namespace OfisHaritasi.EditorTools
{
    public class OHPlaced
    {
        public string id;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
        public float scale = 1f;
        public string group;
        public int seed;
        public int junction = -1, signalGroup = -1;
        public int edge = -1; public bool left; public float s;
    }

    public class OHLot
    {
        public OHAssetInfo a;
        public OHRect rect;
        public Vector3 origin;
        public Quaternion rot;
        public OHEdgeDef e;
        public bool left;
        public float sCenter;
        public string zone;
        public int seed;
    }

    public class OHPlan
    {
        public readonly List<OHLot> lots = new List<OHLot>();
        public readonly List<OHPlaced> props = new List<OHPlaced>();
        public readonly List<OHPlaced> trees = new List<OHPlaced>();
        public readonly List<OHPlaced> parked = new List<OHPlaced>();
        public readonly List<OHRect> reserved = new List<OHRect>();
        public readonly List<OHRect> parkPaths = new List<OHRect>();
        public readonly List<OHRect> sand = new List<OHRect>();
        public readonly List<string> log = new List<string>();
        public readonly HashSet<long> streetSpotsTaken = new HashSet<long>();
    }

    public static class OHPlacement
    {
        static System.Random rng;
        static float R() { return (float)rng.NextDouble(); }
        static float R(float a, float b) { return a + (b - a) * R(); }

        public static readonly Dictionary<string, KeyValuePair<string, float>[]> Zones = new Dictionary<string, KeyValuePair<string, float>[]>
        {
            { "ticari", W("Atolye_A", 3, "Atolye_B", 3, "Atolye_C", 2.5f) },
            { "ticari_kenar", W("Atolye_B", 2, "Atolye_A", 1.5f, "Ev_D", 1) },
            { "ticari_arka", W("Atolye_A", 2, "Atolye_B", 2, "Atolye_C", 2) },
            { "ticari_dogu", W("Atolye_C", 2, "Atolye_B", 2, "Atolye_A", 1.5f) },
            { "cadde_apartman", W("Apartman_B", 3, "Apartman_A", 1.5f, "Apartman_C", 0.8f) },
            { "konut_cadde", W("Apartman_B", 2, "Apartman_A", 2, "Apartman_C", 1) },
            { "konut", W("Apartman_A", 1.3f, "Apartman_C", 1.3f, "Ev_A", 2, "Ev_B", 2, "Ev_C", 1.6f, "Ev_D", 1) },
            { "park_kenar", W("Apartman_A", 1, "Apartman_C", 1, "Ev_A", 2, "Ev_B", 2, "Ev_C", 1.6f) },
            { "ev", W("Ev_A", 2, "Ev_B", 2, "Ev_C", 2, "Ev_D", 1.2f, "Apartman_C", 0.6f) },
            { "konut_kenar", W("Ev_A", 2, "Ev_B", 1.5f, "Ev_C", 2, "Ev_D", 2) },
            { "kirsal", W("Ev_D", 2.5f, "Ev_C", 1.5f, "Ev_A", 1) },
        };

        static KeyValuePair<string, float>[] W(params object[] a)
        {
            var l = new KeyValuePair<string, float>[a.Length / 2];
            for (int i = 0; i < l.Length; i++) l[i] = new KeyValuePair<string, float>((string)a[2 * i], System.Convert.ToSingle(a[2 * i + 1]));
            return l;
        }

        static Vector2 Gap(string zone)
        {
            if (zone.StartsWith("ticari")) return new Vector2(1f, 3.5f);
            if (zone == "konut_kenar") return new Vector2(4f, 14f);
            if (zone == "kirsal") return new Vector2(16f, 42f);
            return new Vector2(0.6f, 2.6f);
        }

        static readonly Dictionary<string, Vector2> DefaultParcel = new Dictionary<string, Vector2>
        {
            { "Atolye_A", new Vector2(24, 32) }, { "Atolye_B", new Vector2(20, 28) }, { "Atolye_C", new Vector2(30, 28) },
            { "Benzinlik", new Vector2(36, 30) }, { "Market", new Vector2(30, 32) },
            { "Apartman_A", new Vector2(22, 22) }, { "Apartman_B", new Vector2(22, 22) }, { "Apartman_C", new Vector2(22, 24) },
            { "Ev_A", new Vector2(18, 22) }, { "Ev_B", new Vector2(18, 22) }, { "Ev_C", new Vector2(18, 22) }, { "Ev_D", new Vector2(18, 22) },
        };

        public static Vector2 Parcel(string id)
        {
            var a = OHAssetPrep.Get(id);
            if (a != null && a.parsel.x > 1) return a.parsel;
            Vector2 d; return DefaultParcel.TryGetValue(id, out d) ? d : new Vector2(20, 20);
        }

        // ---------------------------------------------------------------- parseller

        public static Vector3 SideNormal(OHEdgeDef e, float s, bool left)
        {
            var r = OHPolyline.Right(e.line.Tangent(s));
            return left ? -r : r;
        }

        static float FrontOffset(OHEdgeDef e, bool left) { return e.prof.Half(left) + 0.02f; }

        public static bool AreaFree(OHRect r, OHRoadIndex roads, OHPlan plan, bool checkLots, float tol = -0.05f)
        {
            for (float u = -r.hx + 0.3f; u <= r.hx - 0.3f + 1e-3f; u += Mathf.Max(0.5f, Mathf.Min(2f, (2 * r.hx - 0.6f) / 3f)))
                for (float v = -r.hz + 0.3f; v <= r.hz - 0.3f + 1e-3f; v += Mathf.Max(0.5f, Mathf.Min(2f, (2 * r.hz - 0.6f) / 3f)))
                {
                    Vector3 p = r.c + r.ax * u + r.az * v;
                    OHRoadSeg seg; float lat;
                    if (roads.Clearance(p.x, p.z, 25f, out seg, out lat) < tol) return false;
                    if (OHLayout.Natural(p.x, p.z) > OHLayout.TownGround + 0.75f) return false;
                    if (Mathf.Abs(p.x - OHLayout.CreekX(p.z)) < 16f) return false;
                    if (OHLayout.LegacyZone.Contains(new Vector2(p.x, p.z))) return false;
                    if (OHLayout.ParkRect.Contains(new Vector2(p.x, p.z))) return false;
                    if (p.x < OHLayout.PlayMinX + 10 || p.x > OHLayout.PlayMaxX - 10 || p.z < OHLayout.PlayMinZ + 10 || p.z > OHLayout.PlayMaxZ - 10) return false;
                    foreach (var t in OHLayout.Tunnels) { var rr = t.rect; rr.xMin -= 25; rr.xMax += 25; rr.yMin -= 25; rr.yMax += 25; if (rr.Contains(new Vector2(p.x, p.z))) return false; }
                }
            if (checkLots) foreach (var l in plan.lots) if (l.rect.Overlaps(r, 0.3f)) return false;
            foreach (var q in plan.reserved) if (q.Overlaps(r, 0.2f)) return false;
            return true;
        }

        static OHLot TryLot(OHPlan plan, OHRoadIndex roads, OHEdgeDef e, bool left, float s, string id, string zone)
        {
            var size = Parcel(id);
            float sc = s + size.x * 0.5f;
            if (sc + size.x * 0.5f > e.S1 - 0.5f) return null;
            Vector3 n = SideNormal(e, sc, left);
            Vector3 front = e.line.At(sc) + n * FrontOffset(e, left);
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            var rect = new OHRect(front + n * (size.y * 0.5f), rot, size.x, size.y);
            if (!AreaFree(rect, roads, plan, true)) return null;
            front.y = -0.075f;
            var lot = new OHLot { a = OHAssetPrep.Get(id), rect = rect, origin = front, rot = rot, e = e, left = left, sCenter = sc, zone = zone, seed = rng.Next() };
            return lot;
        }

        static void Commit(OHPlan plan, OHLot lot)
        {
            plan.lots.Add(lot);
            if (lot.a == null) return;
            foreach (var c in lot.a.cuts)
            {
                // yerel x -> kenar s
                float sa, sb;
                if (lot.left) { sa = lot.sCenter + c.x; sb = lot.sCenter + c.y; }
                else { sa = lot.sCenter - c.y; sb = lot.sCenter - c.x; }
                (lot.left ? lot.e.cutsL : lot.e.cutsR).Add(new Vector2(sa - 0.4f, sb + 0.4f));
            }
        }

        static string Pick(string zone, string avoid)
        {
            KeyValuePair<string, float>[] list;
            if (!Zones.TryGetValue(zone, out list)) return null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                float tot = 0;
                foreach (var kv in list) if (OHAssetPrep.Get(kv.Key) != null || OHAssetPrep.Assets.Count == 0) tot += kv.Value;
                if (tot <= 0) return null;
                float r = R() * tot, acc = 0;
                foreach (var kv in list)
                {
                    if (!(OHAssetPrep.Get(kv.Key) != null || OHAssetPrep.Assets.Count == 0)) continue;
                    acc += kv.Value;
                    if (acc >= r) { if (kv.Key != avoid || attempt == 2) return kv.Key; break; }
                }
            }
            return list[0].Key;
        }

        public static void PlaceLots(OHPlan plan, OHRoadIndex roads, int seed)
        {
            rng = new System.Random(seed);
            // otobüs durakları (önce yer ayır)
            BusStop(plan, roads, "AnaCadde_3", false, 85f);
            BusStop(plan, roads, "AnaCadde_3", true, 112f);
            BusStop(plan, roads, "AnaCadde_2", false, 52f);
            BusStop(plan, roads, "AnaCadde_1", true, 64f);
            // özel parseller
            Explicit(plan, roads, "AnaCadde_4", true, "Benzinlik", 0.6f, "ticari");
            Explicit(plan, roads, "AnaCadde_2", false, "Market", 0.6f, "cadde_apartman");
            // kenarlar (ana cadde önce)
            var order = new List<OHEdgeDef>(OHLayout.Edges);
            order.Sort((a, b) => b.prof.cls.CompareTo(a.prof.cls));
            foreach (var e in order)
            {
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    string zone = left ? e.zoneL : e.zoneR;
                    if (string.IsNullOrEmpty(zone) || !Zones.ContainsKey(zone)) continue;
                    float s = e.S0 + 0.5f;
                    float end = e.portalS >= 0 ? (e.tunnelEnd ? Mathf.Min(e.S1, e.portalS - 60f) : e.S1) : e.S1;
                    if (e.portalS >= 0 && e.tunnelStart) s = Mathf.Max(s, e.portalS + 60f);
                    string prev = null;
                    int guard = 0;
                    while (s < end - 8f && guard++ < 400)
                    {
                        string id = Pick(zone, prev);
                        if (id == null) break;
                        var lot = TryLot(plan, roads, e, left, s, id, zone);
                        if (lot == null)
                        {
                            // daha dar bir seçenek dene
                            string alt = Pick(zone, id);
                            if (alt != null && alt != id) lot = TryLot(plan, roads, e, left, s, alt, zone);
                        }
                        if (lot != null)
                        {
                            Commit(plan, lot);
                            var g = Gap(zone);
                            s += Parcel(lot.a != null ? lot.a.id : id).x + R(g.x, g.y);
                            prev = lot.a != null ? lot.a.id : id;
                        }
                        else s += 3f;
                    }
                }
            }
            plan.log.Add("parsel: " + plan.lots.Count);
        }

        static OHEdgeDef EdgeByName(string n) { foreach (var e in OHLayout.Edges) if (e.name == n) return e; return null; }

        static void Explicit(OHPlan plan, OHRoadIndex roads, string edge, bool left, string id, float s, string zone)
        {
            var e = EdgeByName(edge);
            if (e == null || OHAssetPrep.Get(id) == null && OHAssetPrep.Assets.Count > 0) return;
            for (float ds = 0; ds < 30; ds += 1.5f)
            {
                var lot = TryLot(plan, roads, e, left, e.S0 + s + ds, id, zone);
                if (lot != null) { Commit(plan, lot); return; }
            }
            plan.log.Add("yerlesmedi: " + id);
        }

        static void BusStop(OHPlan plan, OHRoadIndex roads, string edge, bool left, float s)
        {
            var e = EdgeByName(edge);
            if (e == null) return;
            Vector3 n = SideNormal(e, s, left);
            Vector3 p = e.line.At(s) + n * (FrontOffset(e, left) + 0.95f);
            Quaternion rot = Quaternion.LookRotation(n);
            var rect = new OHRect(p, rot, 5.0f, 2.4f);
            plan.reserved.Add(rect);
            p.y = OHLayout.TownGround + 0.02f;
            plan.props.Add(new OHPlaced { id = "Otobus_Duragi", pos = p, rot = rot, group = "Duraklar" });
            // durak önü alçak bordür yok; çöp kutusu
            Vector3 t = e.line.Tangent(s);
            Vector3 b = e.line.At(s) + n * (FrontOffset(e, left) - 0.45f) + t * 3.4f; b.y = OHLayout.SidewalkTop;
            plan.props.Add(new OHPlaced { id = "Cop_Kutusu", pos = b, rot = rot, group = "Duraklar" });
            plan.reserved.Add(new OHRect(b, rot, 0.8f, 0.8f));
        }

        // ---------------------------------------------------------------- sokak donatısı

        static bool NearCut(OHEdgeDef e, bool left, float s, float m)
        {
            foreach (var c in left ? e.cutsL : e.cutsR) if (s > c.x - m && s < c.y + m) return true;
            return false;
        }

        static bool NearBridge(List<OHRoadMeshGen.BridgeSpan> br, OHEdgeDef e, float s, float m)
        {
            foreach (var b in br) if (b.e == e && s > b.s0 - m && s < b.s1 + m) return true;
            return false;
        }

        static bool NearReserved(OHPlan plan, Vector3 p, float m)
        {
            foreach (var r in plan.reserved) if (r.Contains(p.x, p.z, m)) return true;
            return false;
        }

        static bool InTunnelZone(OHEdgeDef e, float s, float m)
        {
            if (e.portalS < 0) return false;
            return e.tunnelEnd ? s > e.portalS - m : s < e.portalS + m;
        }

        public static void PlaceStreet(OHPlan plan, OHRoadIndex roads, List<OHRoadMeshGen.BridgeSpan> bridges)
        {
            foreach (var e in OHLayout.Edges)
            {
                var p = e.prof;
                // lambalar
                float spacing = p.name == "Ana" ? 30f : p.name == "Sanayi" ? 34f : p.name == "Mahalle" ? 36f : 0f;
                if (spacing > 0)
                {
                    int k = 0;
                    for (float s = e.S0 + 8f; s < e.S1 - 8f; s += spacing, k++)
                    {
                        bool left = p.name == "Ana" ? (k % 2 == 0) : true;
                        float ss = s;
                        for (int tries = 0; tries < 5 && (NearCut(e, left, ss, 1.8f) || NearBridge(bridges, e, ss, 3f)); tries++) ss += 2.5f;
                        if (NearCut(e, left, ss, 1.8f) || NearBridge(bridges, e, ss, 3f) || InTunnelZone(e, ss, 6f)) continue;
                        Vector3 n = SideNormal(e, ss, left);
                        Vector3 pos = e.line.At(ss) + n * (p.Half(left) - (left ? p.swL : p.swR) + 0.45f);
                        if (e.name == "AnaCadde_3" && left && pos.x > -14 && pos.x < 46) continue; // ofis önü
                        if (NearReserved(plan, pos, 0.6f)) continue;
                        pos.y = p.VergeTop;
                        plan.props.Add(new OHPlaced { id = "Sokak_Lambasi", pos = pos, rot = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up), group = "Lambalar", edge = e.id, left = left, s = ss });
                    }
                }
                // elektrik direkleri
                float ps = p.name == "Mahalle" ? 38f : p.name == "Kirsal" ? 42f : (e.portalS >= 0 ? 44f : 0f);
                if (ps > 0)
                {
                    bool left = p.name == "Mahalle" ? false : true;
                    for (float s = e.S0 + 14f; s < e.S1 - 10f; s += ps)
                    {
                        float ss = s;
                        if (NearCut(e, left, ss, 1.8f)) ss += 4f;
                        if (NearCut(e, left, ss, 1.8f) || NearBridge(bridges, e, ss, 6f) || InTunnelZone(e, ss, 12f)) continue;
                        Vector3 n = SideNormal(e, ss, left);
                        float off = p.raised ? p.Half(left) - 0.3f : p.Half(left) + 0.9f;
                        Vector3 pos = e.line.At(ss) + n * off;
                        if (NearReserved(plan, pos, 0.8f)) continue;
                        pos.y = p.raised ? p.VergeTop : OHLayout.TownGround;
                        plan.props.Add(new OHPlaced { id = "Elektrik_Diregi", pos = pos, rot = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up), group = "Direkler", edge = e.id, left = left, s = ss });
                    }
                }
            }
            // kavşaklar: ışıklar, tabelalar, babalar
            foreach (var j in OHLayout.Junctions)
            {
                for (int d = 0; d < 4; d++)
                {
                    var A = j.arms[d];
                    if (A == null) continue;
                    Vector3 a = A.A, n = A.N;
                    if (j.signalized)
                    {
                        Vector3 pos = j.C + a * (A.start + A.stop + 0.7f) + n * (A.left + A.curb + 0.5f);
                        pos.y = A.raised ? OHLayout.SidewalkTop : OHLayout.ShoulderTop;
                        plan.props.Add(new OHPlaced { id = "Trafik_Isigi_Kollu", pos = pos, rot = Quaternion.LookRotation(-a), group = "Trafik_Isiklari", junction = j.id, signalGroup = d % 2 == 0 ? 0 : 1 });
                        // karşı köşede yaya geçidi tabelası
                        Vector3 tp = j.C + a * (A.start + 0.3f) - n * (A.right + A.curb + 0.6f);
                        tp.y = OHLayout.SidewalkTop;
                        plan.props.Add(new OHPlaced { id = "Tabela_YayaGecidi", pos = tp, rot = Quaternion.LookRotation(-a), group = "Tabelalar" });
                        // köşe babaları
                        var B = j.arms[(d + 1) % 4];
                        if (B != null)
                        {
                            float R = OHNetBuild.CornerRadius(A, B);
                            Vector3 K = j.C + a * B.right + n * A.left;
                            Vector3 O = K + a * R + n * R;
                            for (int q = 1; q <= 3; q++)
                            {
                                float ang = Mathf.Atan2(-n.z, -n.x) + Mathf.DeltaAngle(Mathf.Atan2(-n.z, -n.x) * Mathf.Rad2Deg, Mathf.Atan2(-a.z, -a.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad * q / 4f;
                                Vector3 bp = O + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (R - A.curb - 0.45f);
                                bp.y = OHLayout.SidewalkTop;
                                plan.props.Add(new OHPlaced { id = "Baba", pos = bp, group = "Babalar" });
                            }
                        }
                    }
                    else if (!OHRoadMeshGen.IsMajor(j, A))
                    {
                        bool stop = j.name.Contains("Kirsal") || j.name.Contains("OkulGuney") || j.name.Contains("Bahce");
                        Vector3 pos = j.C + a * (A.start + A.stop + 1.2f) + n * (A.left + A.curb + (A.raised ? 0.45f : 0.9f));
                        pos.y = A.raised ? OHLayout.SidewalkTop : OHLayout.ShoulderTop;
                        plan.props.Add(new OHPlaced { id = stop ? "Tabela_Dur" : "Tabela_YolVer", pos = pos, rot = Quaternion.LookRotation(-a), group = "Tabelalar" });
                    }
                }
            }
            // köprü yaklaşımlarında bariyer
            foreach (var b in bridges)
            {
                var e = b.e; var p = e.prof;
                foreach (bool left in new[] { true, false })
                    foreach (float dir in new[] { -1f, 1f })
                    {
                        float s0 = dir < 0 ? b.s0 - 3.5f : b.s1 + 3.5f;
                        for (int k = 0; k < 4; k++)
                        {
                            float s = s0 + dir * k * 4f;
                            float sA = dir < 0 ? s - 4f : s;
                            if (sA < e.S0 + 2 || sA + 4 > e.S1 - 2) continue;
                            Vector3 n = SideNormal(e, sA, left);
                            Vector3 pos = e.line.At(sA) + n * (p.Half(left) - (p.raised ? (left ? p.swL : p.swR) - 0.35f : -0.3f));
                            pos.y = p.VergeTop;
                            Vector3 t = e.line.Tangent(sA + 2f);
                            // segment +X yönüne uzanır, ön yüz -Z (yola bakar)
                            Quaternion rot = Quaternion.LookRotation(n, Vector3.up) * Quaternion.identity;
                            Vector3 localX = rot * Vector3.right;
                            if (Vector3.Dot(localX, t) < 0) { pos += t * 4f; }
                            plan.props.Add(new OHPlaced { id = "Bariyer_Otoyol", pos = pos, rot = rot, group = "Bariyerler" });
                        }
                    }
            }
            plan.log.Add("donati: " + plan.props.Count);
        }

        // ---------------------------------------------------------------- park

        public static void PlacePark(OHPlan plan)
        {
            var r = OHLayout.ParkRect;
            float y = OHLayout.TownGround + 0.035f;
            float cx = r.center.x, cz = r.center.y;
            // yollar
            plan.parkPaths.Add(new OHRect(new Vector3(cx, y, cz), Quaternion.identity, r.width, 2.6f));
            plan.parkPaths.Add(new OHRect(new Vector3(cx + 14f, y, cz), Quaternion.identity, 2.6f, r.height));
            plan.parkPaths.Add(new OHRect(new Vector3(r.xMin + 22f, y, cz), Quaternion.identity, 2.6f, r.height));
            // oyun alanı (kum)
            var sandR = new OHRect(new Vector3(r.xMin + 9f, y - 0.01f, cz - 7f), Quaternion.identity, 13f, 9f);
            plan.sand.Add(sandR);
            plan.props.Add(new OHPlaced { id = "Oyun_Kaydirak", pos = new Vector3(r.xMin + 6f, OHLayout.TownGround + 0.03f, cz - 7.5f), rot = Quaternion.Euler(0, 90, 0), group = "Park" });
            plan.props.Add(new OHPlaced { id = "Oyun_Salincak", pos = new Vector3(r.xMin + 12.5f, OHLayout.TownGround + 0.03f, cz - 6.5f), rot = Quaternion.Euler(0, 0, 0), group = "Park" });
            plan.props.Add(new OHPlaced { id = "Cesme", pos = new Vector3(cx + 14f + 2.4f, OHLayout.TownGround + 0.03f, cz + 2.6f), rot = Quaternion.Euler(0, 180, 0), group = "Park" });
            // banklar ve park lambaları
            for (float x = r.xMin + 30f; x < r.xMax - 4f; x += 11f)
            {
                if (Mathf.Abs(x - (cx + 14f)) < 3f) continue;
                plan.props.Add(new OHPlaced { id = "Bank", pos = new Vector3(x, y, cz + 2.3f), rot = Quaternion.Euler(0, 180, 0), group = "Park" });
                plan.props.Add(new OHPlaced { id = "Bank", pos = new Vector3(x + 5f, y, cz - 2.3f), rot = Quaternion.identity, group = "Park" });
            }
            for (float x = r.xMin + 4f; x < r.xMax; x += 21f)
                plan.props.Add(new OHPlaced { id = "Park_Lambasi", pos = new Vector3(x, y, cz + 1.9f), group = "Park" });
            plan.props.Add(new OHPlaced { id = "Cop_Kutusu", pos = new Vector3(cx + 14f + 2.0f, y, cz - 2.2f), group = "Park" });
            // çalı çit (kenar)
            for (float x = r.xMin + 1f; x < r.xMax - 2f; x += 2f)
            {
                if (Mathf.Abs(x + 1f - (cx + 14f)) < 2.5f || Mathf.Abs(x + 1f - (r.xMin + 22f)) < 2.5f) continue;
                plan.props.Add(new OHPlaced { id = "Cali_Cit", pos = new Vector3(x, OHLayout.TownGround, r.yMin + 0.6f), rot = Quaternion.identity, group = "Park" });
                plan.props.Add(new OHPlaced { id = "Cali_Cit", pos = new Vector3(x, OHLayout.TownGround, r.yMax - 0.6f), rot = Quaternion.identity, group = "Park" });
            }
            // ağaçlar
            rng = new System.Random(77);
            for (float x = r.xMin + 4f; x < r.xMax - 3f; x += 9.5f)
                for (float z = r.yMin + 4f; z < r.yMax - 3f; z += 8.5f)
                {
                    var p = new Vector3(x + R(-2, 2), 0, z + R(-2, 2));
                    bool bad = false;
                    foreach (var q in plan.parkPaths) if (q.Contains(p.x, p.z, 2.2f)) bad = true;
                    if (sandR.Contains(p.x, p.z, 4f)) bad = true;
                    if (bad || R() < 0.2f) continue;
                    p.y = OHLayout.TownGround;
                    plan.trees.Add(new OHPlaced { id = R() < 0.75f ? "Agac_Cinar" : "Agac_Kavak", pos = p, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.85f, 1.15f), group = "Park_Agaclari" });
                }
        }

        // ---------------------------------------------------------------- doğa

        static bool TreeFree(OHPlan plan, OHRoadIndex roads, Vector3 p, float roadMargin)
        {
            OHRoadSeg seg; float lat;
            if (roads.Clearance(p.x, p.z, 25f, out seg, out lat) < roadMargin) return false;
            foreach (var l in plan.lots) if (l.rect.Contains(p.x, p.z, 0.8f)) return false;
            foreach (var q in plan.reserved) if (q.Contains(p.x, p.z, 1.0f)) return false;
            if (OHLayout.LegacyZone.Contains(new Vector2(p.x, p.z))) return false;
            var pr = OHLayout.ParkRect; pr.xMin -= 1; pr.yMin -= 1; pr.xMax += 1; pr.yMax += 1;
            if (pr.Contains(new Vector2(p.x, p.z))) return false;
            foreach (var t in OHLayout.Tunnels) { var rr = t.rect; rr.xMin -= 18; rr.xMax += 18; rr.yMin -= 18; rr.yMax += 18; if (rr.Contains(new Vector2(p.x, p.z))) return false; }
            return true;
        }

        public static void PlaceNature(OHPlan plan, OHRoadIndex roads, OHTerrain ter, int seed)
        {
            rng = new System.Random(seed);
            int hill = 0, creek = 0, gaps = 0, orchard = 0, rural = 0, lotTrees = 0;
            // tepeler
            for (int i = 0; i < 16000 && hill < 1050; i++)
            {
                float x = R(OHLayout.PlayMinX - 120, OHLayout.PlayMaxX + 120), z = R(OHLayout.PlayMinZ - 120, OHLayout.PlayMaxZ + 120);
                float hn = OHLayout.Natural(x, z) - OHLayout.TownGround;
                float p = Mathf.Clamp01((hn - 1.5f) / 9f) * 0.6f + (x > 300 ? 0.02f : 0);
                if (R() > p) continue;
                if (Mathf.Abs(x - OHLayout.CreekX(z)) < 11f) continue;
                var pos = new Vector3(x, 0, z);
                if (!TreeFree(plan, roads, pos, 4f)) continue;
                float h = ter.Height(x, z, ter.InTunnelRect(x, z));
                float hx = ter.Height(x + 2, z, false) - ter.Height(x - 2, z, false), hz = ter.Height(x, z + 2, false) - ter.Height(x, z - 2, false);
                if (Mathf.Sqrt(hx * hx + hz * hz) / 4f > 0.85f) continue;
                pos.y = h - 0.05f;
                string id;
                float q = R();
                bool northWest = z > 60 || x < -380;
                if (x > 300 && z < 150 && z > -300) id = q < 0.35f ? "Agac_Cinar" : q < 0.6f ? "Agac_Meyve" : q < 0.8f ? "Agac_Kavak" : "Agac_Cam";
                else if (northWest) id = q < 0.68f ? "Agac_Cam" : q < 0.9f ? "Agac_Cinar" : "Cali_Yuvarlak";
                else id = q < 0.42f ? "Agac_Cam" : q < 0.82f ? "Agac_Cinar" : "Cali_Yuvarlak";
                plan.trees.Add(new OHPlaced { id = id, pos = pos, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.8f, 1.25f), group = "Tepe_Agaclari" });
                hill++;
            }
            // dere kavakları
            for (float z = OHLayout.PlayMinZ + 10; z < OHLayout.PlayMaxZ - 10; z += R(7.5f, 11f))
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    if (R() < 0.18f) continue;
                    float x = OHLayout.CreekX(z) + side * R(11f, 14f);
                    var pos = new Vector3(x, 0, z);
                    if (!TreeFree(plan, roads, pos, 4.5f)) continue;
                    pos.y = ter.Height(x, z, false) - 0.05f;
                    plan.trees.Add(new OHPlaced { id = R() < 0.8f ? "Agac_Kavak" : "Agac_Cinar", pos = pos, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.85f, 1.15f), group = "Dere_Agaclari" });
                    creek++;
                }
            }
            // kırsal yol kenarı kavaklar
            foreach (var e in OHLayout.Edges)
            {
                if (e.prof.name != "Kirsal" && e.name != "AnaCadde_DoguTunel" && e.name != "AnaCadde_BatiTunel") continue;
                for (float s = e.S0 + 12; s < e.S1 - 12; s += R(10f, 13f))
                {
                    if (InTunnelZone(e, s, 40f)) continue;
                    foreach (bool left in new[] { true, false })
                    {
                        Vector3 n = SideNormal(e, s, left);
                        Vector3 pos = e.line.At(s) + n * (e.prof.Half(left) + R(2.6f, 3.6f));
                        if (!TreeFree(plan, roads, pos, 1.8f)) continue;
                        if (Mathf.Abs(pos.x - OHLayout.CreekX(pos.z)) < 12f) continue;
                        pos.y = ter.Height(pos.x, pos.z, false) - 0.05f;
                        plan.trees.Add(new OHPlaced { id = "Agac_Kavak", pos = pos, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.85f, 1.1f), group = "Yol_Agaclari" });
                        rural++;
                    }
                }
            }
            // mahalle aralıkları
            for (float x = -320; x < 260; x += 12f)
                for (float z = -330; z < 70; z += 12f)
                {
                    float px = x + R(-4, 4), pz = z + R(-4, 4);
                    if (OHLayout.Natural(px, pz) > OHLayout.TownGround + 1.2f) continue;
                    if (R() > (pz > -20 ? 0.13f : 0.26f)) continue;
                    if (Mathf.Abs(px - OHLayout.CreekX(pz)) < 12f) continue;
                    var pos = new Vector3(px, 0, pz);
                    if (!TreeFree(plan, roads, pos, 2.5f)) continue;
                    pos.y = ter.Height(px, pz, false) - 0.04f;
                    float q = R();
                    plan.trees.Add(new OHPlaced { id = q < 0.4f ? "Agac_Cinar" : q < 0.75f ? "Agac_Meyve" : q < 0.85f ? "Agac_Kavak" : "Cali_Yuvarlak", pos = pos, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.8f, 1.15f), group = "Mahalle_Agaclari" });
                    gaps++;
                }
            // meyve bahçeleri (doğu tarla ızgarası)
            float ang = 12f * Mathf.Deg2Rad;
            for (float x = 310; x < 470; x += 6f)
                for (float z = -320; z < 190; z += 6f)
                {
                    float u = (x * Mathf.Cos(ang) + z * Mathf.Sin(ang)) / 46f, v = (-x * Mathf.Sin(ang) + z * Mathf.Cos(ang)) / 68f;
                    float hsh = OHNoise.Hash(Mathf.Floor(u), Mathf.Floor(v));
                    if (hsh < 0.86f) continue;
                    float eu = Mathf.Min(u - Mathf.Floor(u), 1 - (u - Mathf.Floor(u))) * 46f, ev = Mathf.Min(v - Mathf.Floor(v), 1 - (v - Mathf.Floor(v))) * 68f;
                    if (eu < 3f || ev < 3f) continue;
                    if (x > OHLayout.EastLine(z) - 15f) continue;
                    if (OHLayout.Natural(x, z) - OHLayout.TownGround > 4.5f) continue;
                    var pos = new Vector3(x, 0, z);
                    if (!TreeFree(plan, roads, pos, 3f)) continue;
                    pos.y = ter.Height(x, z, false) - 0.04f;
                    plan.trees.Add(new OHPlaced { id = "Agac_Meyve", pos = pos, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.85f, 1.05f), group = "Meyve_Bahceleri" });
                    orchard++;
                }
            // parsel içi ağaçlar
            foreach (var l in plan.lots)
            {
                if (l.a == null) continue;
                foreach (var t in l.a.trees)
                {
                    var pos = l.origin + l.rot * t;
                    pos.y = OHLayout.LotGround - 0.03f;
                    plan.trees.Add(new OHPlaced { id = R() < 0.55f ? "Agac_Meyve" : "Cali_Yuvarlak", pos = pos, rot = Quaternion.Euler(0, R(0, 360), 0), scale = R(0.8f, 1.05f), group = "Bahce_Agaclari" });
                    lotTrees++;
                }
            }
            plan.log.Add($"agac: tepe {hill}, dere {creek}, yol {rural}, mahalle {gaps}, meyve bahcesi {orchard}, bahce {lotTrees}");
        }

        // ---------------------------------------------------------------- park etmiş araçlar

        static readonly KeyValuePair<string, float>[] ParkCars = W("Arac_Hatchback", 3, "Arac_Sedan", 3, "Arac_Taksi", 0.5f, "Arac_SUV", 2, "Arac_Pickup", 1.1f, "Arac_HafifTicari", 1.4f, "Arac_Minibus", 0.4f);
        static readonly KeyValuePair<string, float>[] YardCars = W("Arac_HafifTicari", 2.5f, "Arac_Pickup", 2, "Arac_Kamyon", 1.2f, "Arac_Sedan", 1, "Arac_Minibus", 0.8f, "Arac_Hatchback", 0.8f);
        static readonly KeyValuePair<string, float>[] FarmCars = W("Arac_Traktor", 2, "Arac_Pickup", 2, "Arac_Sedan", 0.6f);

        static string PickCar(KeyValuePair<string, float>[] list)
        {
            float tot = 0;
            foreach (var kv in list) if (OHAssetPrep.Get(kv.Key) != null) tot += kv.Value;
            if (tot <= 0) return null;
            float r = R() * tot, acc = 0;
            foreach (var kv in list) { if (OHAssetPrep.Get(kv.Key) == null) continue; acc += kv.Value; if (acc >= r) return kv.Key; }
            return null;
        }

        public static void PlaceParked(OHPlan plan, int seed)
        {
            rng = new System.Random(seed);
            int lotCars = 0, street = 0, bins = 0;
            foreach (var l in plan.lots)
            {
                if (l.a == null) continue;
                bool yard = l.a.id.StartsWith("Atolye");
                bool farm = l.zone == "kirsal";
                float fill = l.a.id == "Benzinlik" ? 0.45f : yard ? 0.7f : l.a.id == "Market" ? 0.6f : 0.55f;
                foreach (var pk in l.a.parks)
                {
                    if (R() > fill) continue;
                    string id = PickCar(farm ? FarmCars : yard ? YardCars : ParkCars);
                    if (id == null) continue;
                    var pos = l.origin + l.rot * pk.Key;
                    pos.y = l.origin.y + 0.04f;
                    plan.parked.Add(new OHPlaced { id = id, pos = pos, rot = l.rot * Quaternion.Euler(0, pk.Value + (R() < 0.5f ? 0 : 180f) * (l.a.id == "Benzinlik" ? 0 : 1) * 0, 0), seed = rng.Next(), group = "Parsel_Araclari" });
                    lotCars++;
                }
            }
            foreach (var e in OHLayout.Edges)
            {
                var p = e.prof;
                if (p.parkR <= 0) continue;
                float off = p.cwR - p.parkR * 0.5f;
                int k = 0;
                for (float s = e.S0 + 13f; s < e.S1 - 13f; s += 6.3f, k++)
                {
                    if (NearCut(e, false, s, 3.2f)) continue;
                    Vector3 c = e.line.At(s) + OHPolyline.Right(e.line.Tangent(s)) * off;
                    if (NearReserved(plan, c, 2f)) continue;
                    Quaternion rot = Quaternion.LookRotation(e.line.Tangent(s));
                    if (k % 17 == 8)
                    {
                        Vector3 cpos = c; cpos.y = OHLayout.RoadTop;
                        plan.props.Add(new OHPlaced { id = "Cop_Konteyneri", pos = cpos - rot * Vector3.forward * 0.8f, rot = rot * Quaternion.Euler(0, 90, 0), group = "Konteynerler" });
                        plan.props.Add(new OHPlaced { id = "Cop_Konteyneri", pos = cpos + rot * Vector3.forward * 0.8f, rot = rot * Quaternion.Euler(0, 90, 0), group = "Konteynerler" });
                        bins += 2;
                        continue;
                    }
                    if (R() > 0.42f) continue;
                    string id = PickCar(ParkCars);
                    if (id == null) continue;
                    c.y = OHLayout.RoadTop;
                    plan.parked.Add(new OHPlaced { id = id, pos = c + rot * Vector3.forward * R(-0.4f, 0.4f), rot = rot, seed = rng.Next(), group = "Sokak_Araclari" });
                    street++;
                }
            }
            plan.log.Add($"park arac: parsel {lotCars}, sokak {street}, konteyner {bins}");
        }
    }
}
