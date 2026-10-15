using System.Collections.Generic;
using UnityEngine;

// Şerit doluluk servisi (kullanıcı kararı 3): şeritteki araçları (trafik + ambulans) ve şeridi kapatan konileri bilir.
// Araç takibi, spawn yer kontrolü ve koni önünde durma bu tek servis üzerinden çözülür.
// Bütün mesafeler şerit yol uzunluğu s (m) cinsindendir.
public static class LaneOccupancy
{
    public enum BlockerKind { None, Vehicle, Cone }

    // Önde duran engel: durulması gereken nokta stopS (aracın ön tamponu için), engelin hızı.
    public struct Blocker
    {
        public BlockerKind kind;
        public float edgeS;          // engelin şeride bakan yüzü (öndeki aracın arka tamponu ya da koninin arka kenarı)
        public float speed;
        public LaneFollower vehicle;
        public TrafficCone cone;
    }

    private static readonly List<LaneFollower> followers = new List<LaneFollower>();
    public static IReadOnlyList<LaneFollower> Followers { get { return followers; } }

    public static void Register(LaneFollower f) { if (!followers.Contains(f)) followers.Add(f); }
    public static void Unregister(LaneFollower f) { followers.Remove(f); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { followers.Clear(); }

    // self'in önündeki en yakın araç (aynı şerit, kökü self'ten ileride). Yoksa null.
    public static LaneFollower Leader(LaneFollower self)
    {
        LaneFollower best = null;
        float bestS = float.PositiveInfinity;
        foreach (var f in followers)
        {
            if (f == null || f == self || f.lane != self.lane) continue;
            if (f.Distance <= self.Distance) continue;
            if (f.Distance < bestS) { bestS = f.Distance; best = f; }
        }
        return best;
    }

    // s noktasının önündeki ilk aracın arka tamponu (yoksa +sonsuz). Spawn yer kontrolü için.
    // Arka tamponu s'nin gerisinde kalan ama önü s'yi geçmiş araç da sayılır (spawn noktasını işgal ediyor).
    public static float FirstRearAhead(LanePath lane, float s, out float speed)
    {
        float best = float.PositiveInfinity;
        speed = 0f;
        foreach (var f in followers)
        {
            if (f == null || f.lane != lane) continue;
            if (f.FrontS < s) continue;
            if (f.RearS < best) { best = f.RearS; speed = f.Speed; }
        }
        return best;
    }

    // self'in önündeki en yakın engel: araç ya da şeridi kapatan dik koni (ignored içindekiler hariç).
    public static Blocker NearestBlocker(LaneFollower self, ICollection<TrafficCone> ignored, float coneUprightMaxAngle)
    {
        var result = new Blocker { kind = BlockerKind.None, edgeS = float.PositiveInfinity };
        var leader = Leader(self);
        if (leader != null)
        {
            result.kind = BlockerKind.Vehicle;
            result.edgeS = leader.RearS;
            result.speed = leader.Speed;
            result.vehicle = leader;
        }
        foreach (var c in TrafficCone.All)
        {
            if (c == null || (ignored != null && ignored.Contains(c))) continue;
            if (!c.ClosesLane(self.lane, coneUprightMaxAngle, out float coneS)) continue;
            float edge = coneS - c.radius;
            // Ön tamponun gerisinde kalmış koni (araç zaten üstünden geçiyor) engel değildir.
            if (edge < self.FrontS - 0.3f) continue;
            if (edge < result.edgeS)
            {
                result.kind = BlockerKind.Cone;
                result.edgeS = edge;
                result.speed = 0f;
                result.vehicle = null;
                result.cone = c;
            }
        }
        return result;
    }
}
