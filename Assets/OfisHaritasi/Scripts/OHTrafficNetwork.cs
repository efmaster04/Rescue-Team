using System;
using System.Collections.Generic;
using UnityEngine;

// Ofis haritasının trafik ağı. Editördeki OHMapBuilder doldurur; çalışma anında yalnız okunur.
// - Şeritler: yol kenarındaki düz şeritler ve kavşak içindeki bağlantılar (connector).
// - Kavşaklar: ışıklı (iki fazlı) ya da öncelikli (tali yol yol verir).
// - Doluluk: her FixedUpdate'te OHTrafficManager araçları şeritlere kaydeder.
namespace OfisHaritasi
{
    [Serializable]
    public class OHLane
    {
        public int id;
        public Vector3[] pts = new Vector3[0];
        public float[] cum = new float[0];
        public float length;
        public float speedLimit = 13.9f;     // m/sn
        public int[] next = new int[0];
        public int junction = -1;            // bağlantıysa kavşak indeksi
        public int signalGroup = -1;         // ışıklı kavşakta faz grubu (0 doğu-batı, 1 kuzey-güney)
        public int[] conflicts = new int[0]; // aynı kavşakta kesişen ya da aynı şeride birleşen bağlantılar
        public bool yields;                  // öncelikli akışa yol verir
        public int[] prioIn = new int[0];    // öncelikli gelen şeritler
        public int[] prioConn = new int[0];  // ... ve o şeritten geçen öncelikli bağlantılar
        public bool sink;                    // tünel içinde biten şerit: araç başka tünele ışınlanır
        public bool source;                  // tünel içinde başlayan şerit
        public int edge = -1;
        public int turn;                     // 0 düz, 1 sol, 2 sağ
        public int fromLane = -1, toLane = -1;
        public float weight = 1f;            // rota seçiminde ağırlık (ana yol daha çekici)

        public bool IsConnector { get { return junction >= 0; } }

        public void Rebuild()
        {
            if (pts == null || pts.Length < 2) { cum = new float[0]; length = 0; return; }
            cum = new float[pts.Length];
            float c = 0;
            for (int i = 1; i < pts.Length; i++) { c += Vector3.Distance(pts[i - 1], pts[i]); cum[i] = c; }
            length = c;
        }

        public Vector3 PointAt(float s)
        {
            if (s <= 0) return pts[0] + (pts[1] - pts[0]).normalized * s;
            if (s >= length) { var d = (pts[pts.Length - 1] - pts[pts.Length - 2]).normalized; return pts[pts.Length - 1] + d * (s - length); }
            int i = Seg(s);
            float t = (s - cum[i]) / Mathf.Max(1e-5f, cum[i + 1] - cum[i]);
            return Vector3.LerpUnclamped(pts[i], pts[i + 1], t);
        }

        public Vector3 TangentAt(float s)
        {
            int i = Seg(Mathf.Clamp(s, 0, length));
            var d = pts[i + 1] - pts[i]; d.y = 0;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        public int Seg(float s)
        {
            int lo = 0, hi = pts.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (cum[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // Konumu şeride iz düşürür. hint civarında arar; bulamazsa bütün şeridi tarar.
        public float Project(Vector3 p, float hint, out float lateral)
        {
            int n = pts.Length - 1;
            int c = Seg(Mathf.Clamp(hint, 0, length));
            float best = float.MaxValue, bestS = 0, bestLat = 0;
            int a = Mathf.Max(0, c - 6), b = Mathf.Min(n - 1, c + 8);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = a; i <= b; i++)
                {
                    Vector3 p0 = pts[i], p1 = pts[i + 1];
                    Vector2 s0 = new Vector2(p0.x, p0.z), s1 = new Vector2(p1.x, p1.z), q = new Vector2(p.x, p.z);
                    Vector2 d = s1 - s0;
                    float L2 = Mathf.Max(1e-6f, d.sqrMagnitude);
                    float t = Vector2.Dot(q - s0, d) / L2;
                    float tc = Mathf.Clamp01(t);
                    if (i == 0 && t < 0) tc = t;            // şeridin başından önce
                    if (i == n - 1 && t > 1) tc = t;        // şeridin sonundan sonra
                    Vector2 proj = s0 + d * tc;
                    float dist = (q - proj).sqrMagnitude;
                    if (dist < best)
                    {
                        best = dist;
                        bestS = cum[i] + tc * Mathf.Sqrt(L2);
                        float cross = d.x * (q.y - s0.y) - d.y * (q.x - s0.x);
                        bestLat = -Mathf.Sign(cross) * Mathf.Sqrt(dist); // sağa pozitif
                    }
                }
                if (best < 25f || (a == 0 && b == n - 1)) break;
                a = 0; b = n - 1;
            }
            lateral = bestLat;
            return bestS;
        }
    }

    [Serializable]
    public class OHJunction
    {
        public string name;
        public Vector3 center;
        public bool signalized;
        public float green = 17f, yellow = 3f, allRed = 2f, offset;
        public int[] connectors = new int[0];
        public float Cycle { get { return 2f * (green + yellow + allRed); } }
    }

    public enum OHSignalState { Green, Yellow, Red }

    public class OHTrafficNetwork : MonoBehaviour
    {
        public List<OHLane> lanes = new List<OHLane>();
        public List<OHJunction> junctions = new List<OHJunction>();
        public int[] sources = new int[0];
        public int[] sinks = new int[0];

        // çalışma anı
        [NonSerialized] public List<OHVehicle>[] occupancy;
        [NonSerialized] public List<OHVehicle>[] reservations;
        [NonSerialized] public int[][] prev;
        readonly List<OHSignalLight> lights = new List<OHSignalLight>();
        float signalClock;

        public float SignalClock { get { return signalClock; } }

        public void Init()
        {
            if (occupancy != null && occupancy.Length == lanes.Count) return;
            foreach (var l in lanes) if (l.cum == null || l.cum.Length != l.pts.Length) l.Rebuild();
            occupancy = new List<OHVehicle>[lanes.Count];
            reservations = new List<OHVehicle>[lanes.Count];
            var pl = new List<int>[lanes.Count];
            for (int i = 0; i < lanes.Count; i++) { occupancy[i] = new List<OHVehicle>(4); reservations[i] = new List<OHVehicle>(2); pl[i] = new List<int>(); }
            foreach (var l in lanes) foreach (var n in l.next) pl[n].Add(l.id);
            prev = new int[lanes.Count][];
            for (int i = 0; i < lanes.Count; i++) prev[i] = pl[i].ToArray();
            lights.Clear();
            lights.AddRange(GetComponentsInChildren<OHSignalLight>(true));
            if (lights.Count == 0 && transform.parent) lights.AddRange(transform.parent.GetComponentsInChildren<OHSignalLight>(true));
        }

        void Awake() { Init(); }

        public void TickSignals(float dt)
        {
            signalClock += dt;
            foreach (var l in lights)
            {
                if (l == null || l.junction < 0 || l.junction >= junctions.Count) continue;
                l.Apply(SignalState(l.junction, l.group));
            }
        }

        public OHSignalState SignalState(int j, int group)
        {
            var J = junctions[j];
            if (!J.signalized || group < 0) return OHSignalState.Green;
            float t = Mathf.Repeat(signalClock + J.offset, J.Cycle);
            float half = J.green + J.yellow + J.allRed;
            if (group == 1) t = Mathf.Repeat(t - half, J.Cycle);
            if (t < J.green) return OHSignalState.Green;
            if (t < J.green + J.yellow) return OHSignalState.Yellow;
            return OHSignalState.Red;
        }

        // Faz grubunun bir sonraki yeşile kalan süresi (sn). Yeşildeyse 0.
        public float TimeToGreen(int j, int group)
        {
            var J = junctions[j];
            if (!J.signalized || group < 0) return 0;
            float t = Mathf.Repeat(signalClock + J.offset, J.Cycle);
            float half = J.green + J.yellow + J.allRed;
            if (group == 1) t = Mathf.Repeat(t - half, J.Cycle);
            return t < J.green ? 0 : J.Cycle - t;
        }

        public void ClearOccupancy()
        {
            for (int i = 0; i < occupancy.Length; i++) occupancy[i].Clear();
        }

        public void Register(int lane, OHVehicle v)
        {
            if (lane < 0 || lane >= occupancy.Length) return;
            var list = occupancy[lane];
            if (!list.Contains(v)) list.Add(v);
        }

        public bool Reserve(int conn, OHVehicle v)
        {
            var r = reservations[conn];
            if (!r.Contains(v)) r.Add(v);
            return true;
        }

        public void Unreserve(int conn, OHVehicle v)
        {
            if (conn >= 0 && conn < reservations.Length) reservations[conn].Remove(v);
        }

        public void UnreserveAll(OHVehicle v)
        {
            if (reservations == null) return;
            for (int i = 0; i < reservations.Length; i++) reservations[i].Remove(v);
        }

        public bool OccupiedByOther(int lane, OHVehicle self)
        {
            foreach (var o in occupancy[lane]) if (o != self) return true;
            foreach (var o in reservations[lane]) if (o != self && o != null && o.isActiveAndEnabled) return true;
            return false;
        }
    }
}
