using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Beh = SelVadisi.Beh;
using Role = SelVadisi.Role;
using PropKind = SelVadisi.PropKind;

namespace SelVadisi.EditorBuild
{
    /// Turns the planners' spots into a cast: household templates, best-of-K costume choice with neighbour penalties,
    /// staging on roofs (signal adult in front, kids by their mother, elders seated at the back), pairs, and behaviour pools.
    public static partial class SVBuild
    {
        static readonly Dictionary<string, int> useOuter = new Dictionary<string, int>();
        static readonly Dictionary<string, int> useArchOuter = new Dictionary<string, int>();
        static bool muhtarUsed;

        static string Hex(Color32 c) => c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        static List<CastMember> CastPeople(System.Random r)
        {
            useOuter.Clear(); useArchOuter.Clear(); muhtarUsed = false;
            var cast = new List<CastMember>();
            var byGroup = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < personSpots.Count; i++)
            {
                int g = personSpots[i].group;
                int key = g >= 0 ? g : -1000 - i;
                if (!byGroup.TryGetValue(key, out var l)) { byGroup[key] = l = new List<int>(); order.Add(key); }
                l.Add(i);
            }
            // households first (roof, balcony, tent), then everybody else, the pad last
            int Rank(int key) { var s = personSpots[byGroup[key][0]]; return s.loc == SpotLoc.Roof ? 0 : s.loc == SpotLoc.Balcony ? 1 : s.loc == SpotLoc.Tent ? 2 : s.loc == SpotLoc.Pad ? 5 : 3; }
            order.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            foreach (int key in order)
            {
                var idx = byGroup[key];
                var spots = new List<PersonSpot>();
                foreach (int i in idx) spots.Add(personSpots[i]);
                var archs = ComposeGroup(spots, r);
                var members = new List<CastMember>();
                for (int j = 0; j < idx.Count; j++)
                {
                    var m = BestLook(archs[j].a, archs[j].female, spots[j], cast, members, r);
                    m.spotIndex = idx[j]; m.spot = spots[j];
                    m.pos = spots[j].pos; m.yaw = spots[j].yaw;
                    members.Add(m);
                }
                StageGroup(members, r);
                AssignPools(members, r);
                foreach (var m in members)
                {
                    string o = Hex(m.outer);
                    useOuter[o] = (useOuter.TryGetValue(o, out int u) ? u : 0) + 1;
                    string ao = m.arch + o;
                    useArchOuter[ao] = (useArchOuter.TryGetValue(ao, out int v) ? v : 0) + 1;
                }
                cast.AddRange(members);
            }
            return cast;
        }

        // ------------------------------------------------------------------ group composition

        struct AF { public Arch a; public bool female; public AF(Arch a, bool f) { this.a = a; female = f; } }

        static Arch WeightedArch(System.Random r, params (Arch a, float w)[] t)
        {
            float tot = 0f; foreach (var e in t) tot += e.w;
            float u = (float)r.NextDouble() * tot;
            foreach (var e in t) { u -= e.w; if (u <= 0f) return e.a; }
            return t[0].a;
        }

        static List<AF> ComposeGroup(List<PersonSpot> spots, System.Random r)
        {
            int n = spots.Count;
            var loc = spots[0].loc;
            var list = new List<Arch>();
            switch (loc)
            {
                case SpotLoc.Roof:
                {
                    double u = r.NextDouble();
                    if (u < 0.30) { if (r.NextDouble() < 0.6) list.Add(Arch.Nene); if (r.NextDouble() < 0.5 || list.Count == 0) list.Add(Arch.Dede); list.Add(Arch.Baba); list.Add(Arch.Anne); int k = r.Next(1, 4); for (int i = 0; i < k; i++) list.Add(r.NextDouble() < 0.25 ? Arch.Kucuk : Arch.Cocuk); }
                    else if (u < 0.55) { list.Add(Arch.Baba); list.Add(Arch.Anne); int k = r.Next(1, 3); for (int i = 0; i < k; i++) list.Add(r.NextDouble() < 0.3 ? Arch.Kucuk : Arch.Cocuk); }
                    else if (u < 0.70) { list.Add(Arch.Dede); list.Add(Arch.Nene); if (r.NextDouble() < 0.4) list.Add(Arch.Genc); }
                    else if (u < 0.80) { list.Add(Arch.Genc); list.Add(Arch.GencKiz); list.Add(Arch.Cocuk); if (r.NextDouble() < 0.5) list.Add(Arch.Anne); }
                    else if (u < 0.90) { int k = r.Next(2, 5); for (int i = 0; i < k; i++) list.Add(WeightedArch(r, (Arch.Ciftci, 3), (Arch.Esnaf, 2), (Arch.Balikci, 2), (Arch.Anne, 2), (Arch.Dede, 2))); }
                    else { list.Add(Arch.Esnaf); list.Add(Arch.Anne); list.Add(Arch.Genc); }
                    while (list.Count < n) list.Add(WeightedArch(r, (Arch.Cocuk, 3), (Arch.GencKiz, 1), (Arch.Balikci, 1), (Arch.Ciftci, 1), (Arch.Nene, 1)));
                    break;
                }
                case SpotLoc.Balcony:
                {
                    double u = r.NextDouble();
                    if (n == 1) list.Add(WeightedArch(r, (Arch.Dede, 3), (Arch.Anne, 3), (Arch.Nene, 3), (Arch.GencKiz, 2), (Arch.Baba, 2)));
                    else if (u < 0.3) { list.Add(Arch.Nene); list.Add(Arch.Cocuk); }
                    else if (u < 0.55) { list.Add(Arch.Anne); list.Add(Arch.Cocuk); }
                    else if (u < 0.8) { list.Add(Arch.GencKiz); list.Add(Arch.Anne); }
                    else { list.Add(Arch.Baba); list.Add(Arch.Anne); }
                    while (list.Count < n) list.Add(Arch.Cocuk);
                    break;
                }
                case SpotLoc.Tent:
                {
                    double u = r.NextDouble();
                    if (u < 0.35) { list.Add(Arch.Anne); list.Add(Arch.Cocuk); list.Add(Arch.Nene); }
                    else if (u < 0.6) { list.Add(Arch.Baba); list.Add(Arch.Anne); list.Add(Arch.Cocuk); }
                    else if (u < 0.8) { list.Add(Arch.Dede); list.Add(Arch.Nene); list.Add(Arch.Genc); }
                    else { list.Add(Arch.Baba); list.Add(Arch.Kucuk); list.Add(Arch.GencKiz); }
                    break;
                }
                case SpotLoc.Plateau:
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (spots[i].hint == Beh.Sit) { list.Add(WeightedArch(r, (Arch.Nene, 3), (Arch.Dede, 3), (Arch.Anne, 2))); continue; }
                        if (!muhtarUsed && r.NextDouble() < 0.15) { list.Add(Arch.Muhtar); muhtarUsed = true; continue; }
                        list.Add(WeightedArch(r, (Arch.Baba, 15), (Arch.Genc, 15), (Arch.Dede, 12), (Arch.Ciftci, 12), (Arch.Anne, 10), (Arch.GencKiz, 10), (Arch.Esnaf, 10), (Arch.Nene, 8), (Arch.Cocuk, 8)));
                    }
                    break;
                }
                case SpotLoc.Bridge:
                    for (int i = 0; i < n; i++) list.Add(WeightedArch(r, (Arch.Genc, 25), (Arch.Baba, 20), (Arch.Dede, 15), (Arch.Ciftci, 10), (Arch.GencKiz, 10), (Arch.Anne, 10), (Arch.Cocuk, 10)));
                    break;
                case SpotLoc.Pad:
                    for (int i = 0; i < n; i++)
                    {
                        var s = spots[i];
                        if (s.kind == 1) list.Add(s.hint == Beh.Radio ? Arch.EkipSefi : (s.hint == Beh.Comfort ? Arch.Saglikci : Arch.Kurtarici));
                        else if (s.kind == 2) list.Add(r.NextDouble() < 0.4 ? Arch.Kucuk : Arch.Cocuk);
                        else if (s.kind == 3) list.Add(r.NextDouble() < 0.3 ? Arch.Nene : Arch.Anne);
                        else if (s.group < 0) { if (!muhtarUsed) { list.Add(Arch.Muhtar); muhtarUsed = true; } else list.Add(Arch.Gonullu); }
                        else list.Add(r.NextDouble() < 0.3 ? Arch.Dede : Arch.Baba);
                    }
                    break;
                default: // bank, school yard
                    for (int i = 0; i < n; i++)
                    {
                        var s = spots[i];
                        if (s.kind == 2) list.Add(Arch.Cocuk);
                        else if (s.kind == 3) list.Add(r.NextDouble() < 0.5 ? Arch.Anne : Arch.Nene);
                        else list.Add(WeightedArch(r, (Arch.Ciftci, 25), (Arch.Baba, 15), (Arch.Dede, 15), (Arch.Nene, 10), (Arch.Anne, 10), (Arch.Genc, 10), (Arch.Cocuk, 10), (Arch.Balikci, 5)));
                    }
                    break;
            }
            // shuffle so the template order does not decide who stands where (staging decides)
            var res = new List<AF>();
            for (int i = 0; i < n; i++)
            {
                Arch a = list[i % list.Count];
                res.Add(new AF(a, FemaleArch(a, r)));
            }
            return res;
        }

        // ------------------------------------------------------------------ best of K with neighbour penalties

        static CastMember BestLook(Arch a, bool female, PersonSpot spot, List<CastMember> cast, List<CastMember> group, System.Random r)
        {
            CastMember best = null; int bestScore = int.MaxValue;
            const int K = 16;
            for (int k = 0; k < K; k++)
            {
                var c = SampleLook(a, female, spot.loc, r);
                if (spot.loc == SpotLoc.Plateau || spot.loc == SpotLoc.Tent || (spot.loc == SpotLoc.Pad && spot.kind != 1)) Evacuee(c, spot.loc, r);
                int score = ColourPenalty(c, spot.loc);
                foreach (var n in Neighbours(spot.pos, cast, group, 30f))
                {
                    float d = Vector3.Distance(spot.pos, n.pos);
                    if (d < 6f)
                    {
                        if (n.arch == c.arch && n.outerFam == c.outerFam) score += 100;
                        if (n.headType == c.headType && n.headFam == c.headFam) score += 60;
                        if (d < 3f && n.breaker == c.breaker) score += 25;
                        if (RgbDist(n.outer, c.outer) < 50f) score += 10;
                        if (SVHead.SameLook(n.head, c.head)) score += 300;
                        if (group.Contains(n) && !Neutral(n.outerFam) && !Neutral(c.outerFam) && HueDiff(n.outer, c.outer) < 40f) score += d < 1.5f ? 120 : 40;
                    }
                    if (d < 15f) score -= Mathf.RoundToInt(3f * Mathf.Min(10f, SVHead.Distance(n.head, c.head)));
                    if (n.arch == c.arch && n.outerFam == c.outerFam && n.legsFam == c.legsFam && n.headType == c.headType) score += 200;
                    if (Hex(n.outer) == Hex(c.outer)) score += 40;
                }
                string o = Hex(c.outer);
                score += 15 * Mathf.Max(0, (useOuter.TryGetValue(o, out int u) ? u : 0) - 3);
                score += 30 * Mathf.Max(0, (useArchOuter.TryGetValue(c.arch + o, out int v) ? v : 0) - 1);
                if (score < bestScore) { best = c; bestScore = score; }
            }
            best.score = bestScore;
            return best;
        }

        static IEnumerable<CastMember> Neighbours(Vector3 p, List<CastMember> cast, List<CastMember> group, float range)
        {
            float r2 = range * range;
            foreach (var c in group) yield return c;
            foreach (var c in cast) if ((c.pos - p).sqrMagnitude < r2) yield return c;
        }

        /// Plateau / tent / pad civilians fled their homes: blankets, slippers, tea, wet legs.
        static void Evacuee(CastMember m, SpotLoc loc, System.Random r)
        {
            m.evacuee = true;
            if (m.role == Role.Worker) return;
            if (r.NextDouble() < (loc == SpotLoc.Pad ? 0.7 : 0.35))
            {
                bool foil = r.NextDouble() < (loc == SpotLoc.Pad ? 0.35 : 0.15);
                Color32 c = foil ? H("D4B85A") : Pk(r, "6E7072", "6A5E52", "2E3440", "7A7672", "8E3A30");
                Color32 border = foil ? c : Pk(r, "3A3C40", "A33A33", "E8E2D4");
                bool belled = m.outfit.garments.Exists(x => x.kind == GarmentKind.Skirt || x.kind == GarmentKind.CoatTail);
                var blanket = GBlanket(m.body, c, border, foil);
                if (belled) blanket.y0 = m.body.yHip + 0.03f;
                m.outfit.garments.Add(blanket);
                if (m.head.headwear == "scarf_chin" || m.head.headwear == "hijab") m.head.headwear = "scarf_nape";
                if (!belled)
                {
                    var wrap = GCoatTail(m.body, c, m.body.yKnee + 0.04f, 0.10f, foil ? 0.85f : 0.04f);
                    wrap.band = border;
                    m.outfit.garments.Add(wrap);
                }
                m.blanket = true; m.outer = c; m.outerFam = FamilyOf(c); m.breaker = "blanket";
            }
            if (r.NextDouble() < 0.1) { m.outfit.shoe = "slipper"; m.outfit.shoeCol = Pk(r, "3A5A8A", "7A2E2E", "2F8F6A"); m.outfit.sockCol = Pk(r, "E8DCC8", "B23A3A"); }
            if (r.NextDouble() < 0.3) m.outfit.wetLegs = true;
        }

        // ------------------------------------------------------------------ staging

        static bool IsAdult(CastMember m) => m.role != Role.Child && m.role != Role.Teen;
        static bool IsElder(CastMember m) => m.role == Role.ElderMan || m.role == Role.ElderWoman;

        static void StageGroup(List<CastMember> g, System.Random r)
        {
            var s0 = g[0].spot;
            // per-person randomised habits
            foreach (var m in g) m.domSide = r.NextDouble() < 0.88 ? 1 : -1;
            if (s0.loc == SpotLoc.Roof) { StageRoof(g, r); return; }
            // everywhere else: keep the planner positions; pair children with an adult beside them
            Vector3 help = s0.help;
            foreach (var m in g)
            {
                if (m.spot.hasSeat) { m.hasSeat = true; m.seat = m.spot.seat; m.seatFwd = m.spot.seatFwd; m.seatH = m.spot.seatH; m.yaw = Mathf.Atan2(m.seatFwd.x, m.seatFwd.z) * Mathf.Rad2Deg; }
                if (s0.loc == SpotLoc.Bridge || s0.loc == SpotLoc.Balcony) m.yaw += Rand(r, -10f, 10f);
            }
            AssignBaby(g, r, s0.loc);
            PairUp(g, r, s0.loc);
            if (s0.loc != SpotLoc.Bridge && s0.loc != SpotLoc.Balcony) Separate(g, null, 0.6f);
            if (s0.loc == SpotLoc.Tent || s0.loc == SpotLoc.Plateau)
            {
                // talking clusters face their centroid +-25 deg, unless seated
                Vector3 c = Vector3.zero; foreach (var m in g) c += m.pos; c /= g.Count;
                if (g.Count >= 2 && r.NextDouble() < 0.6)
                    foreach (var m in g)
                    {
                        if (m.hasSeat || m.partner >= 0) continue;
                        Vector3 d = c - m.pos; d.y = 0f;
                        if (d.sqrMagnitude > 0.01f) m.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + Rand(r, -25f, 25f);
                    }
            }
        }

        static void StageRoof(List<CastMember> g, System.Random r)
        {
            var s = g[0].spot;
            Quaternion q = Quaternion.Euler(0f, s.frameYaw, 0f);
            float hw = s.halfW, hd = s.halfD;
            var placed = new List<Vector2>();
            bool Free(Vector2 p, float minD)
            {
                if (p.x < -hw || p.x > hw || p.y < -hd || p.y > hd) return false;
                if (p.y > -0.85f && p.y < -0.15f) return false;     // clothes line
                foreach (var o in placed) if ((o - p).sqrMagnitude < minD * minD) return false;
                return true;
            }
            Vector2 Place(Vector2 want, float minD)
            {
                for (int k = 0; k < 40; k++)
                {
                    Vector2 c = want + (k == 0 ? Vector2.zero : new Vector2(Rand(r, -1f, 1f), Rand(r, -1f, 1f)) * (0.2f + k * 0.06f));
                    c.x = Mathf.Clamp(c.x, -hw, hw); c.y = Mathf.Clamp(c.y, -hd, hd);
                    if (Free(c, minD)) { placed.Add(c); return c; }
                }
                placed.Add(want); return want;
            }
            int side = r.NextDouble() < 0.5 ? -1 : 1;
            // the signal adult: strongest colour, front corner
            CastMember signal = null; float best = -1f;
            foreach (var m in g) if (IsAdult(m) && !IsElder(m)) { float v = Chroma(m.outer) + 0.5f * LStar(m.outer); if (v > best) { best = v; signal = m; } }
            var pos = new Dictionary<CastMember, Vector2>();
            if (signal != null) pos[signal] = Place(new Vector2(side * (hw - 0.2f), -hd + 0.05f), 0.9f);
            // mother (or another adult) beside the signal adult
            CastMember mother = null;
            foreach (var m in g) if (m != signal && (m.arch == Arch.Anne || m.arch == Arch.GencKiz) && mother == null) mother = m;
            if (mother == null) foreach (var m in g) if (m != signal && IsAdult(m) && !IsElder(m) && mother == null) mother = m;
            if (mother != null)
            {
                Vector2 basePos = pos.ContainsKey(signal ?? mother) && signal != null ? pos[signal] : new Vector2(side * (hw - 0.2f), -hd + 0.05f);
                pos[mother] = Place(basePos + new Vector2(-side * Rand(r, 1.8f, 2.6f), Rand(r, 0f, 0.35f)), 0.9f);
            }
            // children next to the mother (or any adult)
            CastMember anchor = mother ?? signal;
            foreach (var m in g)
            {
                if (pos.ContainsKey(m) || m.role != Role.Child) continue;
                Vector2 a = anchor != null && pos.ContainsKey(anchor) ? pos[anchor] : new Vector2(0f, -hd + 0.3f);
                pos[m] = Place(a + new Vector2((r.NextDouble() < 0.5 ? -1f : 1f) * Rand(r, 0.6f, 0.8f), Rand(r, -0.1f, 0.25f)), 0.55f);
            }
            // elders at the back, 40% seated on a chair or crate
            foreach (var m in g)
            {
                if (pos.ContainsKey(m) || !IsElder(m)) continue;
                pos[m] = Place(new Vector2(Rand(r, -hw, hw), Mathf.Min(hd, -hd + Rand(r, 1.5f, 2.5f))), 0.9f);
                if (r.NextDouble() < 0.4) { m.seatProp = true; m.hasSeat = true; m.seatH = r.NextDouble() < 0.6 ? 0.44f : 0.32f; }
            }
            // a second adult (the father, a farmer...) takes the opposite front corner
            foreach (var m in g)
                if (!pos.ContainsKey(m) && IsAdult(m) && !IsElder(m)) { pos[m] = Place(new Vector2(-side * (hw - 0.4f), -hd + Rand(r, 0.2f, 1.0f)), 0.9f); break; }
            // teen at the edge filming, everyone else anywhere free
            foreach (var m in g)
            {
                if (pos.ContainsKey(m)) continue;
                Vector2 want = m.role == Role.Teen ? new Vector2(-side * (hw - 0.1f), -hd + Rand(r, 0f, 0.6f)) : new Vector2(Rand(r, -hw, hw), Rand(r, -hd, Mathf.Min(hd, -hd + 3.5f)));
                pos[m] = Place(want, 0.9f);
            }
            // world positions, facing toward the help source within +-60 deg of the parapet normal
            Vector3 fwd = q * Vector3.back;
            float frontYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            foreach (var m in g)
            {
                Vector2 p = pos[m];
                m.pos = s.frameC + q * new Vector3(p.x, 0f, p.y);
                Vector3 d = s.help - m.pos; d.y = 0f;
                float helpYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float rel = Mathf.Clamp(Mathf.DeltaAngle(frontYaw, helpYaw), -60f, 60f);
                m.yaw = frontYaw + rel + Rand(r, -20f, 20f);
                if (m.hasSeat) { m.seat = m.pos; m.seatFwd = Quaternion.Euler(0f, m.yaw, 0f) * Vector3.forward; }
            }
            AssignBaby(g, r, SpotLoc.Roof);
            PairUp(g, r, SpotLoc.Roof);
            // one pair faces each other a little
            if (g.Count >= 3 && r.NextDouble() < 0.7)
            {
                CastMember a = null, b = null;
                foreach (var m in g) if (IsAdult(m) && m.partner < 0 && !m.hasSeat) { if (a == null) a = m; else if (b == null) b = m; }
                if (a != null && b != null)
                {
                    Vector3 ab = b.pos - a.pos; ab.y = 0f;
                    if (ab.magnitude < 2.5f)
                    {
                        float yab = Mathf.Atan2(ab.x, ab.z) * Mathf.Rad2Deg;
                        a.yaw = Mathf.LerpAngle(a.yaw, yab, 0.55f); b.yaw = Mathf.LerpAngle(b.yaw, yab + 180f, 0.55f);
                    }
                }
            }
            // pairing moved children and comforters next to their partner without looking at anyone else
            Separate(g, w =>
            {
                Vector3 l = Quaternion.Inverse(q) * (w - s.frameC);
                l.x = Mathf.Clamp(l.x, -hw, hw); l.z = Mathf.Clamp(l.z, -hd, hd);
                if (l.z > -0.85f && l.z < -0.15f) l.z = l.z < -0.5f ? -0.85f : -0.15f;   // clothes line
                return s.frameC + q * l;
            }, 0.7f);
        }

        /// Pushes apart people who are not partners until adults keep 0.6 m (0.45 m next to a child) between their roots.
        /// A pair moves as one, people on fixed seats (benches) stay, 'clamp' keeps a position inside the spot and nobody
        /// drifts more than maxShift from where the planner put them.
        static void Separate(List<CastMember> g, System.Func<Vector3, Vector3> clamp, float maxShift)
        {
            int n = g.Count;
            if (n < 2) return;
            var start = new Vector3[n];
            for (int i = 0; i < n; i++) start[i] = g[i].pos;
            bool Fixed(CastMember m) => m.hasSeat && !m.seatProp;
            void Move(int i, Vector3 d)
            {
                var m = g[i];
                int pi = m.partner >= 0 && m.partner < n ? m.partner : -1;
                if (Fixed(m) || (pi >= 0 && Fixed(g[pi]))) return;
                m.pos += d;
                if (pi >= 0) g[pi].pos += d;
            }
            for (int it = 0; it < 16; it++)
            {
                bool any = false;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        CastMember a = g[i], b = g[j];
                        if (a.partner == j || b.partner == i) continue;
                        float need = a.role == Role.Child || b.role == Role.Child ? 0.55f : 0.72f;
                        // a seated person's knees and feet reach ~0.5 m forward of the seat
                        if (a.hasSeat || b.hasSeat) need += 0.35f;
                        Vector3 ca = a.hasSeat ? a.pos + a.seatFwd * 0.3f : a.pos, cb = b.hasSeat ? b.pos + b.seatFwd * 0.3f : b.pos;
                        Vector3 d = cb - ca; d.y = 0f;
                        float dist = d.magnitude;
                        if (dist >= need) continue;
                        Vector3 dir = dist > 1e-3f ? d / dist : Quaternion.Euler(0f, 37f * (i + 1) + 71f * j, 0f) * Vector3.forward;
                        float push = 0.5f * (need - dist) + 0.005f;
                        Move(i, -dir * push); Move(j, dir * push);
                        any = true;
                    }
                for (int i = 0; i < n; i++)
                {
                    var m = g[i];
                    Vector3 c = clamp != null ? clamp(m.pos) : m.pos;
                    Vector3 off = c - start[i]; off.y = 0f;
                    if (off.magnitude > maxShift) { Vector3 lim = start[i] + off.normalized * maxShift; c = new Vector3(lim.x, c.y, lim.z); }
                    m.pos = c;
                }
                if (!any) break;
            }
            foreach (var m in g) if (m.seatProp) m.seat = m.pos;
        }

        /// Children pair with the nearest free adult; sometimes two adults pair for comforting.
        static void PairUp(List<CastMember> g, System.Random r, SpotLoc loc)
        {
            if (loc == SpotLoc.Bridge || loc == SpotLoc.Balcony) return;
            foreach (var c in g)
            {
                if (c.role != Role.Child || c.partner >= 0 || r.NextDouble() > 0.75) continue;
                CastMember best = null; float bd = 1.6f;
                foreach (var a in g)
                {
                    if (a == c || !IsAdult(a) || a.partner >= 0 || a.hasSeat || a.props.Contains(PropKind.Baby)) continue;
                    float d = Vector3.Distance(a.pos, c.pos);
                    if (d < bd) { bd = d; best = a; }
                }
                if (best == null) continue;
                int ia = g.IndexOf(best), ic = g.IndexOf(c);
                best.partner = ic; c.partner = ia;          // group-local for now; resolved to global indices by the builder
                best.adultInPair = true;
                // stand beside the adult's free (non-dominant) hand, cling to a leg, or face them for a hug.
                // Pair behaviours are the partners' whole pool so they never fall out of sync.
                Vector3 right = Quaternion.Euler(0f, best.yaw, 0f) * Vector3.right;
                Vector3 fwd = Quaternion.Euler(0f, best.yaw, 0f) * Vector3.forward;
                double u = r.NextDouble();
                if (u < 0.3)
                {
                    float sd0 = -best.domSide;
                    Vector3 dir = Quaternion.Euler(0f, best.yaw + 50f * sd0, 0f) * Vector3.forward;
                    c.pos = best.pos + dir * (0.30f + 0.06f * c.body.H); c.yaw = best.yaw + 50f * sd0 + 180f;
                    best.yaw += 25f * sd0;
                    c.pool = new[] { Beh.HugChild }; best.pool = new[] { Beh.HugChild };
                }
                else
                {
                    float sd = -best.domSide;
                    bool cling = loc != SpotLoc.Roof && u > 0.7;
                    c.pos = best.pos + right * sd * (cling ? 0.30f : 0.42f) + fwd * (cling ? 0.10f : 0.05f);
                    c.yaw = cling ? best.yaw - 25f * sd : best.yaw + Rand(r, -10f, 10f);
                    if (cling) { c.pool = new[] { Beh.Cling }; best.pool = new[] { Beh.Cling }; }
                    else { c.pool = new[] { Beh.HoldHands }; best.pool = loc == SpotLoc.Roof ? new[] { Beh.HoldHands, Beh.WaveOne } : new[] { Beh.HoldHands }; }
                }
                c.pos.y = best.pos.y;
            }
            // comfort pair: an adult next to an elder or someone in a blanket; on the pad the medic comforts an evacuee
            CastMember medic = null;
            foreach (var m in g) if (m.arch == Arch.Saglikci && m.spot.hint == Beh.Comfort) medic = m;
            if (medic != null || (loc != SpotLoc.Pad && g.Count >= 3 && r.NextDouble() < 0.35))
            {
                CastMember recv = null, giver = medic;
                foreach (var m in g) if (m.partner < 0 && m != giver && m.role != Role.Worker && (IsElder(m) || m.blanket || medic != null) && IsAdult(m) && recv == null) recv = m;
                if (giver == null) foreach (var m in g) if (m.partner < 0 && m != recv && IsAdult(m) && !IsElder(m) && giver == null) giver = m;
                if (recv != null && giver != null && !recv.hasSeat)
                {
                    giver.partner = g.IndexOf(recv); recv.partner = g.IndexOf(giver); giver.adultInPair = true;
                    Vector3 right = Quaternion.Euler(0f, recv.yaw, 0f) * Vector3.right;
                    float off = (r.NextDouble() < 0.5 ? 1f : -1f) * (IsElder(recv) ? 0.33f : 0.36f);
                    giver.pos = recv.pos + right * off; giver.yaw = recv.yaw - 12f * Mathf.Sign(off);
                    giver.pos.y = recv.pos.y;
                    giver.pool = new[] { Beh.Comfort }; recv.pool = new[] { Beh.Comfort };
                }
            }
        }

        // ------------------------------------------------------------------ behaviour pools

        static void AssignPools(List<CastMember> g, System.Random r)
        {
            var loc = g[0].spot.loc;
            int overhead = 0;
            bool anySeated = false;
            foreach (var m in g)
            {
                if (m.partner >= 0)
                {
                    m.poolW = new float[m.pool.Length];
                    for (int i = 0; i < m.poolW.Length; i++) m.poolW[i] = 1f;
                    Expression(m, r);
                    continue;
                }
                if (m.arch == Arch.Anne && m.props.Contains(PropKind.Baby))
                {
                    if (loc == SpotLoc.Roof || loc == SpotLoc.Balcony) { m.pool = new[] { Beh.HoldBaby, Beh.WaveOne }; m.poolW = new[] { 40f, 25f }; }
                    else { m.pool = new[] { Beh.HoldBaby }; m.poolW = new[] { 1f }; }
                    Expression(m, r);
                    continue;
                }
                var w = new Dictionary<Beh, float>();
                bool kid = m.role == Role.Child, teen = m.role == Role.Teen, elder = IsElder(m), worker = m.role == Role.Worker;
                bool hasPhone = m.props.Contains(PropKind.Phone) || teen || (!kid && !elder && r.NextDouble() < 0.4);
                bool pair = m.partner >= 0;
                switch (loc)
                {
                    case SpotLoc.Roof:
                        w[Beh.WaveOne] = 18f; w[Beh.WaveBoth] = elder ? 0f : 22f; w[Beh.CallCupped] = kid ? 0f : 10f;
                        if (hasPhone) w[Beh.Phone] = 9f;
                        w[Beh.Point] = 6f; w[Beh.RestKnees] = elder || kid ? 0f : 5f;
                        w[Beh.HandsOnHead] = 3f; w[Beh.HandOnCheek] = elder ? 4f : 2f; w[Beh.Shiver] = 3f;
                        if (kid) w[Beh.JumpWave] = 7f;
                        if (elder) { w[Beh.Dua] = 6f; w[Beh.Crouch] = m.role == Role.ElderMan ? 4f : 0f; }
                        if (teen) w[Beh.Film] = 12f;
                        if (m.props.Contains(PropKind.Cloth)) w[Beh.SignalCloth] = 30f;
                        break;
                    case SpotLoc.Balcony:
                        w[Beh.LeanRail] = 25f; w[Beh.WaveOne] = 22f; w[Beh.WaveBoth] = elder ? 0f : 12f; w[Beh.CallCupped] = kid ? 0f : 12f;
                        if (hasPhone) w[Beh.Phone] = 12f;
                        w[Beh.HandOnCheek] = 8f; w[Beh.HandsOnHead] = 2f;
                        break;
                    case SpotLoc.Plateau:
                    case SpotLoc.Tent:
                        w[Beh.Stand] = 16f; w[Beh.Talk] = g.Count >= 2 ? 16f : 0f; w[Beh.Shiver] = m.blanket ? 14f : 4f;
                        if (hasPhone) w[Beh.Phone] = 9f;
                        w[Beh.Point] = 4f;
                        if (kid) { w[Beh.Crouch] = 5f; }
                        if (elder) { w[Beh.Dua] = 4f; w[Beh.HandOnCheek] = 4f; }
                        w[Beh.Sit] = m.hasSeat ? 60f : (loc == SpotLoc.Tent ? 14f : 0f);
                        break;
                    case SpotLoc.Bridge:
                        w[Beh.LeanRail] = 28f; w[Beh.Point] = 24f; w[Beh.Stand] = 16f;
                        if (hasPhone) { w[Beh.Film] = 20f; w[Beh.Phone] = 12f; }
                        break;
                    case SpotLoc.Pad:
                        if (worker)
                        {
                            Beh h = m.spot.hint;
                            if (h == Beh.Kneel) { w[Beh.Kneel] = 1f; break; }
                            if (h == Beh.Walk) { w[Beh.Walk] = 1f; if (r.NextDouble() < 0.5) m.props.Add(PropKind.Box); break; }
                            if (h == Beh.Radio) { w[Beh.Radio] = 16f; w[Beh.Point] = 8f; w[Beh.Direct] = 6f; w[Beh.Stand] = 6f; break; }
                            if (h == Beh.Talk) { w[Beh.Talk] = 20f; w[Beh.Stand] = 6f; w[Beh.Radio] = m.props.Contains(PropKind.Radio) ? 4f : 0f; break; }
                            if (h == Beh.Comfort) { w[Beh.Comfort] = 10f; w[Beh.Stand] = 4f; break; }
                            w[Beh.Direct] = 14f; w[Beh.Point] = 10f; w[Beh.Stand] = 8f; if (m.props.Contains(PropKind.Radio)) w[Beh.Radio] = 8f;
                        }
                        else { w[Beh.Shiver] = 30f; w[Beh.Sit] = 18f; w[Beh.Stand] = 8f; if (hasPhone) w[Beh.Phone] = 8f; }
                        break;
                    default: // bank
                        w[Beh.Stand] = 26f; w[Beh.Point] = 18f; w[Beh.HandsOnHead] = 5f; w[Beh.HandOnCheek] = 5f; w[Beh.Talk] = g.Count >= 2 ? 10f : 0f;
                        if (hasPhone) { w[Beh.Film] = 14f; w[Beh.Phone] = 10f; }
                        if (m.role == Role.ElderMan) w[Beh.Crouch] = 10f;
                        break;
                }
                // mothers with a baby, pairs, seats
                if (m.arch == Arch.Nene && !m.blanket && r.NextDouble() < 0.12 && !pair && loc != SpotLoc.Bridge)
                { m.props.Add(PropKind.Baby); w[Beh.HoldBaby] = 40f; }
                if (pair && m.pool.Length == 1 && (m.pool[0] == Beh.HugChild || m.pool[0] == Beh.Comfort)) { w[m.pool[0]] = 40f; }
                else if (pair) { w[Beh.HoldHands] = 30f; if (loc != SpotLoc.Roof && r.NextDouble() < 0.4) w[Beh.Cling] = 15f; }
                if (m.hasSeat) { w[Beh.Sit] = 80f; m.sitStyle = loc == SpotLoc.Plateau ? 1 + r.Next(4) : 1 + r.Next(2); if (m.sitStyle == 3) m.props.Add(PropKind.Tea); }
                else if (loc == SpotLoc.Tent && r.NextDouble() < 0.3) { w[Beh.Sit] = 40f; m.sitStyle = 6; }
                else if (loc == SpotLoc.Pad && !worker && w.ContainsKey(Beh.Sit)) m.sitStyle = 6;
                // umbrellas on the bank and bridge, sometimes on the plateau
                if (!worker && !kid && !pair && !m.hasSeat && (loc == SpotLoc.Bank || loc == SpotLoc.Bridge ? r.NextDouble() < 0.3 : (loc == SpotLoc.Plateau && r.NextDouble() < 0.15)))
                {
                    m.umbrella = true; m.umbrellaSide = -m.domSide;
                    m.umbrellaCol = Pk(r, "1F2226", "1F2226", "C8372D", "2F5FB0", "E3B32E", "3E8A5A", "D8E4EA");
                    foreach (var b in new[] { Beh.WaveBoth, Beh.CallCupped, Beh.HandsOnHead, Beh.Dua, Beh.LeanRail, Beh.Shiver, Beh.Sit, Beh.Crouch, Beh.RestKnees, Beh.HoldBaby, Beh.SignalCloth }) w.Remove(b);
                    if (m.umbrellaSide == m.domSide) { w.Remove(Beh.Point); }
                }
                // at most two people per roof (one per balcony) with both arms overhead
                if (loc == SpotLoc.Roof || loc == SpotLoc.Balcony)
                {
                    bool over = (w.TryGetValue(Beh.WaveBoth, out float wb) && wb > 0f) || w.ContainsKey(Beh.SignalCloth) || w.ContainsKey(Beh.JumpWave);
                    if (over && overhead >= (loc == SpotLoc.Roof ? 2 : 1)) { w.Remove(Beh.WaveBoth); w.Remove(Beh.JumpWave); }
                    else if (over) overhead++;
                }
                // draw 2-3 behaviours without replacement + the Stand filler
                var pool = new List<Beh>(); var pw = new List<float>();
                int want = m.hasSeat || pair && m.pool.Length == 1 ? 1 : (r.NextDouble() < 0.5 ? 2 : 3);
                var keys = new List<Beh>(); var ws = new List<float>();
                foreach (var kv in w) if (kv.Value > 0f) { keys.Add(kv.Key); ws.Add(kv.Value); }
                for (int k = 0; k < want && keys.Count > 0; k++)
                {
                    float tot = 0f; foreach (var x in ws) tot += x;
                    float u = (float)r.NextDouble() * tot; int pick = 0;
                    for (int i = 0; i < ws.Count; i++) { u -= ws[i]; if (u <= 0f) { pick = i; break; } }
                    pool.Add(keys[pick]); pw.Add(ws[pick]); keys.RemoveAt(pick); ws.RemoveAt(pick);
                }
                if (pool.Count == 0) { pool.Add(Beh.Stand); pw.Add(1f); }
                if (m.hasSeat && pool[0] != Beh.Sit)
                {
                    int si = pool.IndexOf(Beh.Sit);
                    if (si >= 0) { pool.RemoveAt(si); pw.RemoveAt(si); }
                    pool.Insert(0, Beh.Sit); pw.Insert(0, 80f);
                }
                bool bound = pool[0] == Beh.Kneel || pool[0] == Beh.Walk || pool[0] == Beh.Sit || pool[0] == Beh.HugChild || pool[0] == Beh.Comfort || pool[0] == Beh.HoldBaby;
                if (!bound && !pool.Contains(Beh.Stand)) { pool.Add(Beh.Stand); pw.Add(Mathf.Max(4f, pw[0] * 0.4f)); }
                m.pool = pool.ToArray(); m.poolW = pw.ToArray();
                if (m.pool[0] == Beh.Sit) anySeated = true;
                // a pointer in a talking cluster turns back toward what he points at (the flooded village), not at his family
                if ((loc == SpotLoc.Tent || loc == SpotLoc.Plateau) && m.pool.Contains(Beh.Point) && !m.hasSeat)
                {
                    Vector3 d = m.spot.water - m.pos; d.y = 0f;
                    if (d.sqrMagnitude > 1f) m.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + Rand(r, -20f, 20f);
                }
                if ((m.pool.Contains(Beh.Phone) || m.pool.Contains(Beh.Film)) && !m.props.Contains(PropKind.Phone)) m.props.Add(PropKind.Phone);
                if (m.pool.Contains(Beh.Radio) && !m.props.Contains(PropKind.Radio)) m.props.Add(PropKind.Radio);
                if (m.pool.Contains(Beh.SignalCloth)) m.clothCol = Pk(r, "F2F0EA", "F2F0EA", "D9302B", "E9C9D6");
                Expression(m, r);
            }
            // at least one person resting low in larger roof groups
            if (loc == SpotLoc.Roof && g.Count >= 4 && !anySeated)
            {
                var cand = g.FindAll(m => m.partner < 0 && !m.hasSeat && !m.props.Contains(PropKind.Baby) && (IsElder(m) || m.role == Role.Woman));
                if (cand.Count > 0)
                {
                    var m = cand[r.Next(cand.Count)];
                    var l = new List<Beh>(m.pool); var lw = new List<float>(m.poolW);
                    int si = l.IndexOf(Beh.Sit);
                    if (si >= 0) { l.RemoveAt(si); lw.RemoveAt(si); }
                    l.Insert(0, Beh.Sit); lw.Insert(0, 30f);
                    m.sitStyle = 6;
                    m.pool = l.ToArray(); m.poolW = lw.ToArray();
                }
            }
        }

        /// Half the roof, balcony, tent, plateau and pad families have a mother carrying a baby (decided before pairing).
        static void AssignBaby(List<CastMember> g, System.Random r, SpotLoc loc)
        {
            if (loc == SpotLoc.Bridge || loc == SpotLoc.Bank || r.NextDouble() > 0.5) return;
            foreach (var m in g)
                if (m.arch == Arch.Anne && !m.hasSeat && !m.umbrella)
                {
                    if (!m.props.Contains(PropKind.Baby)) m.props.Add(PropKind.Baby);
                    return;
                }
        }

        static void Expression(CastMember m, System.Random r)
        {
            Beh b = m.pool[0];
            float u = (float)r.NextDouble();
            string e;
            if (m.role == Role.Child) e = u < 0.3f ? "smile" : u < 0.55f ? "softsmile" : u < 0.85f ? "worried" : "shout";
            else if (m.role == Role.Worker) e = u < 0.5f ? "determined" : u < 0.85f ? "neutral" : "shout";
            else if (b == Beh.WaveBoth || b == Beh.WaveOne || b == Beh.JumpWave || b == Beh.CallCupped || b == Beh.SignalCloth) e = u < 0.5f ? "shout" : u < 0.9f ? "worried" : "neutral";
            else if (b == Beh.Point) e = u < 0.6f ? "worried" : "shout";
            else if (b == Beh.Shiver || m.blanket) e = u < 0.6f ? "tired" : "worried";
            else if (b == Beh.Comfort || m.props.Contains(PropKind.Tea)) e = u < 0.5f ? "softsmile" : "worried";
            else e = u < 0.45f ? "worried" : u < 0.75f ? "neutral" : u < 0.9f ? "tired" : "softsmile";
            if (IsElder(m) && e == "shout") e = "worried";
            m.head.expr = e;
        }
    }
}
