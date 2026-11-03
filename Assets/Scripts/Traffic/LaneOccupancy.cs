using System.Collections.Generic;
using UnityEngine;

// Şeride giren dış engel (LaneFollower olmayan): oyuncunun sürdüğü / park ettiği servis minibüsü (ServiceVanDriver).
// Kendini LaneOccupancy'ye kaydeder; trafik aracı önünde bu engeli görünce durur, korna çalar, süre dolunca çarpar (Ram).
public interface ILaneObstacle
{
    // Şeridi kapatıyor mu? edgeS: engelin şeritteki en küçük s'si (yaklaşan aracın gördüğü yüz), farS: en büyük s'si,
    // speedAlong: engelin şerit boyunca hızı (m/sn, teğet yönünde +).
    bool BlocksLane(LanePath lane, out float edgeS, out float farS, out float speedAlong);
    // Araç engele çarpıyor (bekleme süresi doldu, ilerliyor); her fizik adımında çağrılır, engel temasa göre tepki verir.
    void Ram(TrafficVehicle by);
}

// Şerit doluluk servisi (kullanıcı kararı 3): şeritteki araçları (trafik + ambulans), şeridi kapatan konileri ve dış engelleri
// (servis minibüsü) bilir. Araç takibi, spawn yer kontrolü ve koni / engel önünde durma bu tek servis üzerinden çözülür.
// Bütün mesafeler şerit yol uzunluğu s (m) cinsindendir.
public static class LaneOccupancy
{
    public enum BlockerKind { None, Vehicle, Cone, Obstacle }

    // Önde duran engel: durulması gereken nokta stopS (aracın ön tamponu için), engelin hızı.
    public struct Blocker
    {
        public BlockerKind kind;
        public float edgeS;          // engelin şeride bakan yüzü (öndeki aracın arka tamponu ya da koninin arka kenarı)
        public float farS;           // dış engelin uzak yüzü (Obstacle); öbürlerinde edgeS
        public float speed;
        public LaneFollower vehicle;
        public TrafficCone cone;
        public ILaneObstacle obstacle;
    }

    private static readonly List<LaneFollower> followers = new List<LaneFollower>();
    private static readonly List<ILaneObstacle> obstacles = new List<ILaneObstacle>();
    public static IReadOnlyList<LaneFollower> Followers { get { return followers; } }
    public static IReadOnlyList<ILaneObstacle> Obstacles { get { return obstacles; } }

    public static void Register(LaneFollower f) { if (!followers.Contains(f)) followers.Add(f); }
    public static void Unregister(LaneFollower f) { followers.Remove(f); }
    public static void RegisterObstacle(ILaneObstacle o) { if (!obstacles.Contains(o)) obstacles.Add(o); }
    public static void UnregisterObstacle(ILaneObstacle o) { obstacles.Remove(o); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { followers.Clear(); obstacles.Clear(); }

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
    // Dış engel (minibüs) de sayılır: doğma noktasının önünde şeridi kapatıyorsa yeni araç onun gerisine sığmalı.
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
        foreach (var o in obstacles)
        {
            if (o == null || !o.BlocksLane(lane, out float edge, out float far, out float v)) continue;
            if (far < s) continue;
            if (edge < best) { best = edge; speed = v; }
        }
        return best;
    }

    // self'in önündeki en yakın engel: araç, şeridi kapatan dik koni (ignored içindekiler hariç) ya da dış engel
    // (ignoredObstacle hariç: araç o engele çarpmaya karar vermişse artık onun için durmaz).
    public static Blocker NearestBlocker(LaneFollower self, ICollection<TrafficCone> ignored, float coneUprightMaxAngle, ILaneObstacle ignoredObstacle = null)
    {
        var result = new Blocker { kind = BlockerKind.None, edgeS = float.PositiveInfinity, farS = float.PositiveInfinity };
        var leader = Leader(self);
        if (leader != null)
        {
            result.kind = BlockerKind.Vehicle;
            result.edgeS = leader.RearS;
            result.farS = leader.FrontS;
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
                result.farS = coneS + c.radius;
                result.speed = 0f;
                result.vehicle = null;
                result.cone = c;
            }
        }
        foreach (var o in obstacles)
        {
            if (o == null || o == ignoredObstacle) continue;
            if (!o.BlocksLane(self.lane, out float edge, out float far, out float v)) continue;
            // Engelin uzak yüzü ön tamponun gerisindeyse araç onu geçmiştir.
            if (far < self.FrontS - 0.3f) continue;
            if (edge < result.edgeS)
            {
                result.kind = BlockerKind.Obstacle;
                result.edgeS = edge;
                result.farS = far;
                result.speed = v;
                result.vehicle = null;
                result.cone = null;
                result.obstacle = o;
            }
        }
        return result;
    }
}
