using System;
using System.Collections.Generic;
using UnityEngine;

// Ofis haritasındaki trafik aracı.
// - Fizik kipi: Rigidbody + 4 WheelCollider. Sürücü şeridi "pure pursuit" ile izler, hızı IDM ile ayarlar,
//   tork/fren WheelCollider'lara gider. Tekerlek görselleri WheelCollider pozundan döner.
// - Ray kipi: oyuncudan uzaktaki araçlar kinematik olarak şeridin üstünde kayar (CPU tasarrufu, sıkışma olmaz).
//   Oyuncu yaklaşınca araç hızını koruyarak fizik kipine geçer.
// - Park kipi: AI yok, gövde ve tekerlek küreleriyle uyuyan Rigidbody; çarpılırsa itilir.
namespace OfisHaritasi
{
    public enum OHDriveMode { Rail, Physics, Parked, Wreck }

    [RequireComponent(typeof(Rigidbody))]
    public class OHVehicle : MonoBehaviour
    {
        [Header("Tanım (OHAssetPrep doldurur)")]
        public string typeId;
        public WheelCollider[] wheelColliders = new WheelCollider[0]; // ÖnSol, ÖnSağ, ArkaSol, ArkaSağ
        public Transform[] wheelVisuals = new Transform[0];
        public Vector3[] wheelRest = new Vector3[0];                  // görsellerin araç köküne göre dinlenme konumu
        public Quaternion[] wheelRestRot = new Quaternion[0];         // ... ve dönüşü
        public bool[] steer = new bool[0];
        public bool[] drive = new bool[0];
        public float[] wheelRadius = new float[0];
        public SphereCollider[] parkedWheels = new SphereCollider[0];
        public float wheelBase = 2.6f;
        public float axleMidZ;            // ön-arka aks ortasının yerel z'si
        public float rearAxleZ = -1.3f;
        public float frontExtent = 2.2f;  // aks ortasından ön tampona
        public float rearExtent = 2.2f;   // aks ortasından arka tampona
        public float width = 1.8f;
        public float height = 1.5f;
        public float maxSpeedKmh = 70f;
        public float maxSteer = 35f;
        public float accelMax = 2.0f;
        public float brakeComfort = 2.6f;
        public float latAccel = 2.2f;
        public float massKg = 1200f;
        public Renderer bodyRenderer;
        public int paintIndex = -1, brakeIndex = -1;
        public Color[] paintPalette = new Color[0];
        public bool heavy;

        [NonSerialized] public OHTrafficManager manager;
        [NonSerialized] public OHDriveMode mode = OHDriveMode.Parked;
        [NonSerialized] public readonly List<int> route = new List<int>(8);
        [NonSerialized] public int prevLane = -1;
        [NonSerialized] public float s;
        [NonSerialized] public float speed;
        [NonSerialized] public float lateral;
        [NonSerialized] public int id;
        [NonSerialized] public int committed = -1;
        [NonSerialized] public float waitTimer, stuckTimer, flipTimer, offRoadTimer, wreckTimer, hornCooldown, blockedTimer;
        [NonSerialized] public OHVehicle blockedBy;
        [NonSerialized] public bool braking;
        [NonSerialized] public float cmdAccel;
        [NonSerialized] public string waitReason = "";
        [NonSerialized] public float odometer;
        [NonSerialized] public int teleports;
        [NonSerialized] public float pushTimer, unstickOdo = -1f, reverseTimer;

        // şerit kayıtları (doluluk)
        [NonSerialized] public int regCount;
        [NonSerialized] public readonly int[] regLane = new int[3];
        [NonSerialized] public readonly float[] regRear = new float[3];
        [NonSerialized] public readonly float[] regFront = new float[3];

        Rigidbody rb;
        MaterialPropertyBlock mpb;
        float steerAngle;
        float[] spinDeg;
        float ignoreUntil;
        OHVehicle ignoreVehicle;
        readonly RaycastHit[] castHits = new RaycastHit[8];
        static readonly int BaseId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        Color paint = Color.white;
        bool brakeLit;
        bool hasPaint;

        public Rigidbody Body { get { if (!rb) rb = GetComponent<Rigidbody>(); return rb; } }
        public float Length { get { return frontExtent + rearExtent; } }
        public OHTrafficNetwork Net { get { return manager ? manager.network : null; } }

        void Awake() { rb = GetComponent<Rigidbody>(); }

        // ---------------------------------------------------------------- kurulum

        public void SetPaint(Color c)
        {
            paint = c; hasPaint = true;
            ApplyMaterials(true);
        }

        void ApplyMaterials(bool force)
        {
            if (!bodyRenderer) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            if (hasPaint && paintIndex >= 0 && force)
            {
                mpb.Clear();
                mpb.SetColor(BaseId, paint);
                bodyRenderer.SetPropertyBlock(mpb, paintIndex);
            }
            if (brakeIndex >= 0)
            {
                mpb.Clear();
                if (brakeLit) mpb.SetColor(EmissionId, new Color(1f, .05f, .03f) * 3.2f);
                bodyRenderer.SetPropertyBlock(mpb, brakeIndex);
            }
        }

        void SetBrakeLight(bool on)
        {
            if (on == brakeLit) return;
            brakeLit = on;
            ApplyMaterials(false);
        }

        public void SetMode(OHDriveMode m)
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            var old = mode;
            mode = m;
            bool physics = m == OHDriveMode.Physics || m == OHDriveMode.Wreck;
            bool parked = m == OHDriveMode.Parked;
            foreach (var wc in wheelColliders) if (wc) wc.enabled = physics;
            foreach (var sc in parkedWheels) if (sc) sc.enabled = parked;
            if (m == OHDriveMode.Rail)
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            else
            {
                rb.isKinematic = false;
                rb.interpolation = parked ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
                if (old == OHDriveMode.Rail && physics)
                {
                    rb.linearVelocity = transform.forward * speed;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            if (parked)
            {
                for (int i = 0; i < wheelVisuals.Length; i++) if (wheelVisuals[i]) wheelVisuals[i].SetPositionAndRotation(transform.TransformPoint(wheelRest[i]), transform.rotation * RestRot(i));
                rb.Sleep();
            }
        }

        public void PlaceOnRoute(int lane, float sOnLane, float v)
        {
            route.Clear();
            route.Add(lane);
            prevLane = -1;
            s = sOnLane;
            speed = v;
            committed = -1;
            waitTimer = stuckTimer = flipTimer = offRoadTimer = wreckTimer = blockedTimer = 0;
            blockedBy = null;
            ExtendRoute();
            PoseOnRail(0);
        }

        // ---------------------------------------------------------------- rota

        public void ExtendRoute()
        {
            var net = Net;
            float ahead = net.lanes[route[0]].length - s;
            for (int i = 1; i < route.Count; i++) ahead += net.lanes[route[i]].length;
            int guard = 0;
            while ((ahead < 160f || route.Count < 3) && guard++ < 12)
            {
                var last = net.lanes[route[route.Count - 1]];
                if (last.next.Length == 0) break;
                int pick = manager.PickNext(last);
                route.Add(pick);
                ahead += net.lanes[pick].length;
            }
        }

        void AdvanceLanes()
        {
            var net = Net;
            while (route.Count > 1 && s > net.lanes[route[0]].length)
            {
                s -= net.lanes[route[0]].length;
                int left = route[0];
                if (committed == left) committed = -1;
                net.Unreserve(left, this);
                prevLane = left;
                route.RemoveAt(0);
            }
            // bağlantıya girdik: rezervasyon şerit bitene kadar kalır, "committed" sıradaki kavşak için boşalır
            if (committed >= 0 && route[0] == committed) committed = -1;
            ExtendRoute();
        }

        public Vector3 RoutePoint(float sr, out Vector3 tangent)
        {
            var net = Net;
            int i = 0;
            float local = sr;
            if (local < 0 && prevLane >= 0)
            {
                var p = net.lanes[prevLane];
                tangent = p.TangentAt(p.length + local);
                return p.PointAt(p.length + local);
            }
            while (i < route.Count - 1 && local > net.lanes[route[i]].length) { local -= net.lanes[route[i]].length; i++; }
            var l = net.lanes[route[i]];
            tangent = l.TangentAt(local);
            return l.PointAt(local);
        }

        // ---------------------------------------------------------------- doluluk

        public void RegisterOccupancy()
        {
            var net = Net;
            regCount = 0;
            if (mode == OHDriveMode.Parked || route.Count == 0) return;
            int l0 = route[0];
            float len0 = net.lanes[l0].length;
            float rear = s - rearExtent, front = s + frontExtent;
            Reg(l0, rear, front);
            if (rear < 0 && prevLane >= 0) { float pl = net.lanes[prevLane].length; Reg(prevLane, rear + pl, front + pl); }
            if (front > len0 && route.Count > 1) Reg(route[1], rear - len0, front - len0);
        }

        void Reg(int lane, float rear, float front)
        {
            if (regCount >= 3) return;
            regLane[regCount] = lane; regRear[regCount] = rear; regFront[regCount] = front; regCount++;
            Net.Register(lane, this);
        }

        public bool RearOn(int lane, out float rear)
        {
            for (int i = 0; i < regCount; i++) if (regLane[i] == lane) { rear = regRear[i]; return true; }
            rear = 0; return false;
        }

        // ---------------------------------------------------------------- ana döngü

        public void Tick(float dt, Vector3 focus)
        {
            if (mode == OHDriveMode.Parked) return;
            if (mode == OHDriveMode.Wreck) { TickWreck(dt); return; }
            var net = Net;

            if (mode == OHDriveMode.Physics)
            {
                Vector3 mid = transform.TransformPoint(0, 0, axleMidZ);
                var lane = net.lanes[route[0]];
                float hint = s;
                float ns = lane.Project(mid, hint, out lateral);
                // ileriye doğru projeksiyon; geri kaymayı sınırla
                if (ns > s - 3f) s = ns;
                speed = Vector3.Dot(rb.linearVelocity, transform.forward);
            }
            AdvanceLanes();

            float a = PlanAcceleration(dt, focus);
            cmdAccel = a;

            if (mode == OHDriveMode.Rail)
            {
                speed = Mathf.Max(0, speed + a * dt);
                if (a < -0.1f && speed < 0.05f) speed = 0;
                s += speed * dt;
                odometer += speed * dt;
                AdvanceLanes();
                PoseOnRail(dt);
                SetBrakeLight(a < -0.6f || speed < 0.3f);
            }
            else
            {
                DrivePhysics(a, dt);
                odometer += Mathf.Max(0, speed) * dt;
                CheckHealth(dt);
            }

            var cur = net.lanes[route[0]];
            if (cur.sink && s > cur.length - 4f) manager.RequestTeleport(this);
        }

        void TickWreck(float dt)
        {
            wreckTimer += dt;
            foreach (var wc in wheelColliders) if (wc) { wc.motorTorque = 0; wc.brakeTorque = massKg * 2f; }
            UpdateWheelVisualsPhysics();
            SetBrakeLight(Mathf.Repeat(manager.Clock, 1f) < .5f);
        }

        void CheckHealth(float dt)
        {
            float up = transform.up.y;
            flipTimer = up < 0.45f ? flipTimer + dt : 0;
            offRoadTimer = Mathf.Abs(lateral) > 5.5f ? offRoadTimer + dt : 0;
            Vector3 laneDir; RoutePoint(s, out laneDir);
            reverseTimer = Vector3.Dot(transform.forward, laneDir) < 0.25f ? reverseTimer + dt : 0;
            bool waiting = waitReason.Length > 0;
            stuckTimer = (Mathf.Abs(speed) < 0.25f && !waiting) ? stuckTimer + dt : 0;
            // gitmek isteyip fiziksel olarak takıldıysa (bordür köşesi, direk): kısa süre raya geçip kurtul
            pushTimer = (cmdAccel > 0.3f && Mathf.Abs(speed) < 0.2f && !waiting && up > 0.8f) ? pushTimer + dt : 0;
            if (pushTimer > 5f)
            {
                pushTimer = 0; stuckTimer = 0;
                unstickOdo = odometer + 9f;
                SetMode(OHDriveMode.Rail);
                PoseOnRail(0);
                return;
            }
            if (flipTimer > 3f || offRoadTimer > 4f || stuckTimer > 25f || reverseTimer > 3f)
            {
                manager.OnVehicleFailed(this, flipTimer > 3f ? "devrildi" : offRoadTimer > 4f ? "yoldan cikti" : reverseTimer > 3f ? "ters dondu" : "sikisti");
            }
        }

        public void BecomeWreck()
        {
            Net.UnreserveAll(this);
            committed = -1;
            SetMode(OHDriveMode.Wreck);
            wreckTimer = 0;
        }

        // ---------------------------------------------------------------- hız planı

        float PlanAcceleration(float dt, Vector3 focus)
        {
            var net = Net;
            float vmax = maxSpeedKmh / 3.6f;
            var lane0 = net.lanes[route[0]];
            float v = Mathf.Max(0, speed);
            waitReason = "";

            // serbest yol hızı: hız sınırı + viraj
            float v0 = Mathf.Min(vmax, lane0.speedLimit);
            float look = 12f + v * v / (2f * brakeComfort) + 10f;
            Vector3 tPrev; RoutePoint(s, out tPrev);
            float accumS = 0;
            for (float d = 3f; d <= look; d += 3f)
            {
                Vector3 t; RoutePoint(s + d, out t);
                float ang = Vector3.Angle(tPrev, t) * Mathf.Deg2Rad;
                tPrev = t;
                float k = ang / 3f;
                if (k > 0.004f)
                {
                    float vc = Mathf.Sqrt(latAccel / k);
                    float allowed = Mathf.Sqrt(vc * vc + 2f * brakeComfort * Mathf.Max(0, d - 4f));
                    if (allowed < v0) v0 = allowed;
                }
                accumS = d;
            }
            // ilerideki şeritlerin hız sınırı
            float sumLen = lane0.length - s;
            for (int i = 1; i < route.Count && sumLen < look; i++)
            {
                float lim = net.lanes[route[i]].speedLimit;
                float allowed = Mathf.Sqrt(lim * lim + 2f * brakeComfort * Mathf.Max(0, sumLen));
                if (allowed < v0) v0 = allowed;
                sumLen += net.lanes[route[i]].length;
            }
            v0 = Mathf.Max(v0, 1.5f);

            float aFree = accelMax * (1f - Mathf.Pow(v / v0, 4f));
            float a = aFree;

            // önde giden araç
            float gap; float leaderV;
            OHVehicle leader = FindLeader(90f, out gap, out leaderV);
            if (leader != null)
            {
                float aL = Idm(v, v0, gap, v - leaderV, 2.2f, heavy ? 1.6f : 1.25f);
                if (aL < a) { a = aL; if (aL < 0 && v < 1) waitReason = "trafik"; }
            }

            // kavşak girişi
            float stopGap;
            if (JunctionHold(v, out stopGap))
            {
                float aS = Idm(v, v0, Mathf.Max(0.05f, stopGap), v, 0.8f, 0.6f);
                if (aS < a) { a = aS; if (v < 1.5f) waitReason = "kavsak"; }
            }

            // fiziksel engel (oyuncu, devrik araç, kutu…)
            if (mode == OHDriveMode.Physics)
            {
                float obst; OHVehicle obstVeh; bool isPlayer;
                if (SenseObstacle(v, out obst, out obstVeh, out isPlayer))
                {
                    float aO = Idm(v, v0, Mathf.Max(0.05f, obst), v, 1.2f, 0.7f);
                    if (aO < a) { a = aO; if (v < 1) waitReason = isPlayer ? "oyuncu" : "engel"; }
                    blockedBy = obstVeh;
                    blockedTimer += dt;
                    if (isPlayer && blockedTimer > 2.5f && hornCooldown <= 0) { manager.Honk(this); hornCooldown = 7f; }
                }
                else { blockedBy = null; blockedTimer = 0; }
            }
            hornCooldown -= dt;
            return Mathf.Clamp(a, -8f, accelMax);
        }

        static float Idm(float v, float v0, float gap, float dv, float s0, float T)
        {
            const float aMax = 2.0f, b = 2.6f;
            float sStar = s0 + Mathf.Max(0, v * T + v * dv / (2f * Mathf.Sqrt(aMax * b)));
            float free = 1f - Mathf.Pow(v / Mathf.Max(0.5f, v0), 4f);
            return aMax * (free - (sStar / Mathf.Max(0.05f, gap)) * (sStar / Mathf.Max(0.05f, gap)));
        }

        OHVehicle FindLeader(float maxDist, out float gap, out float leaderV)
        {
            var net = Net;
            gap = float.MaxValue; leaderV = 0;
            OHVehicle best = null;
            float myFront = s + frontExtent;
            float offset = 0;
            for (int i = 0; i < route.Count; i++)
            {
                int L = route[i];
                foreach (var o in net.occupancy[L])
                {
                    if (o == this || o == null) continue;
                    float rear;
                    if (!o.RearOn(L, out rear)) continue;
                    float d = offset + rear - myFront;
                    if (i == 0)
                    {
                        // aynı şeritte: yalnız önümüzdekiler (arka tamponu bizim aks ortamızdan ileride)
                        if (offset + rear + o.Length < s) continue;
                        if (o.s < s - 0.5f && o.route.Count > 0 && o.route[0] == L) continue;
                    }
                    if (d < gap) { gap = Mathf.Max(0.05f, d); best = o; leaderV = Mathf.Max(0, o.speed); }
                }
                offset += net.lanes[L].length;
                if (offset - myFront > maxDist) break;
            }
            return best;
        }

        // Sıradaki bağlantıya girilemiyorsa true ve durma çizgisine mesafe.
        bool JunctionHold(float v, out float stopGap)
        {
            var net = Net;
            stopGap = 0;
            // şu an bağlantı üstündeysek bekleme yok
            int idx = -1;
            float dist = net.lanes[route[0]].length - s - frontExtent;
            for (int i = 1; i < route.Count && i < 4; i++)
            {
                var l = net.lanes[route[i]];
                if (l.IsConnector) { idx = i; break; }
                dist += l.length;
                if (dist > 70f) return false;
            }
            if (idx < 0) return false;
            int conn = route[idx];
            // yol vermeyen akış (ana yol) yeri geç ayırır; yoksa tali yoldaki araçlar hiç boşluk bulamıyor
            bool yields = net.lanes[conn].yields || net.lanes[conn].signalGroup >= 0;
            float decide = yields ? 14f + v * v / (2f * brakeComfort) + v * 1.2f : Mathf.Max(9f, v * 2.6f + 4f);
            if (dist > decide) return false;
            if (committed == conn)
            {
                // sarıda/kırmızıda hâlâ rahat durabiliyorsak vazgeç
                var c = net.lanes[conn];
                if (c.signalGroup >= 0)
                {
                    var st = net.SignalState(c.junction, c.signalGroup);
                    if (st != OHSignalState.Green && dist > v * v / (2f * 3.2f) + 2f && dist > 3f)
                    {
                        net.Unreserve(conn, this); committed = -1;
                        stopGap = dist; return true;
                    }
                }
                return false;
            }
            if (committed >= 0 && committed != conn) { net.Unreserve(committed, this); committed = -1; }
            if (CanEnter(conn, dist, v))
            {
                committed = conn;
                net.Reserve(conn, this);
                waitTimer = 0;
                return false;
            }
            waitTimer += manager.LastDt;
            stopGap = dist;
            return true;
        }

        bool CanEnter(int conn, float dist, float v)
        {
            var net = Net;
            var c = net.lanes[conn];
            if (c.signalGroup >= 0)
            {
                var st = net.SignalState(c.junction, c.signalGroup);
                if (st == OHSignalState.Red) return false;
                if (st == OHSignalState.Yellow && dist > v * v / (2f * 3.5f) - 1f) return false;
            }
            foreach (int k in c.conflicts) if (net.OccupiedByOther(k, this)) return false;
            if (net.OccupiedByOther(conn, this))
            {
                // aynı bağlantıda önümüzde araç varsa takip ederiz; ters yönden giren olamaz
            }
            bool assertive = waitTimer > 14f;
            if (c.yields && !assertive)
            {
                for (int i = 0; i < c.prioIn.Length; i++)
                {
                    int inLane = c.prioIn[i], pc = c.prioConn[i];
                    var L = net.lanes[inLane];
                    foreach (var o in net.occupancy[inLane])
                    {
                        if (o == this || o.route.Count < 2) continue;
                        int oi = o.route.IndexOf(pc);
                        if (oi < 0 || oi > 2) continue;
                        float od = L.length - o.s - o.frontExtent;
                        if (o.route[0] != inLane) continue;
                        float tta = od / Mathf.Max(1.5f, o.speed);
                        if (od < 48f && tta < 5.0f) return false;
                    }
                }
            }
            // çıkış şeridinde yer var mı (kavşağı tıkamamak için)
            if (c.toLane >= 0)
            {
                var exit = net.lanes[c.toLane];
                foreach (var o in net.occupancy[c.toLane])
                {
                    if (o == this) continue;
                    float rear; if (!o.RearOn(c.toLane, out rear)) continue;
                    if (rear < Length + 3f && o.speed < 2.5f) return false;
                }
            }
            return true;
        }

        bool SenseObstacle(float v, out float dist, out OHVehicle veh, out bool isPlayer)
        {
            dist = float.MaxValue; veh = null; isPlayer = false;
            float range = Mathf.Clamp(v * 2.4f + 7f, 7f, 34f);
            Vector3 half = new Vector3(width * 0.42f, 0.38f, 0.25f);
            float travelled = 0;
            float step = 4f;
            Vector3 t0;
            Vector3 p0 = RoutePoint(s + frontExtent, out t0);
            p0.y += 0.95f;
            bool found = false;
            while (travelled < range)
            {
                Vector3 t1;
                Vector3 p1 = RoutePoint(s + frontExtent + travelled + step, out t1);
                p1.y += 0.95f;
                Vector3 dir = p1 - p0;
                float len = dir.magnitude;
                if (len < 0.01f) break;
                dir /= len;
                int n = Physics.BoxCastNonAlloc(p0, half, dir, castHits, Quaternion.LookRotation(dir), len, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = castHits[i];
                    if (h.collider.attachedRigidbody == rb) continue;
                    if (h.distance <= 0 && h.point == Vector3.zero) continue; // başlangıçta içindeyiz
                    var ov = h.collider.attachedRigidbody ? h.collider.attachedRigidbody.GetComponent<OHVehicle>() : null;
                    bool cc = h.collider is CharacterController;
                    if (ov == null && !cc && h.collider.attachedRigidbody == null) continue; // statik dünya (yol, bordür, direk) yok sayılır
                    if (ov != null)
                    {
                        if (ov == ignoreVehicle && manager.Clock < ignoreUntil) continue;
                        // kilitlenme: birbirini bekleyen iki araçtan küçük kimlikli geçer
                        if (ov.blockedBy == this && id < ov.id) { ignoreVehicle = ov; ignoreUntil = manager.Clock + 2.5f; continue; }
                    }
                    var rbo = h.collider.attachedRigidbody;
                    if (rbo != null && ov == null && rbo.mass < 30f) continue; // koni, kutu gibi hafif şeyleri iter
                    float d = travelled + h.distance;
                    if (d < dist) { dist = d; veh = ov; isPlayer = cc; found = true; }
                }
                if (found) break;
                travelled += len;
                p0 = p1;
            }
            if (found) dist = Mathf.Max(0.05f, dist - 1.0f);
            return found;
        }

        // ---------------------------------------------------------------- fizik sürüş

        void DrivePhysics(float a, float dt)
        {
            // direksiyon: arka akstan pure pursuit
            Vector3 rearAxle = transform.TransformPoint(0, 0, rearAxleZ);
            float v = Mathf.Max(0, speed);
            float ld = Mathf.Max(Mathf.Clamp(3.5f + v * 0.55f, 4.5f, 14f), wheelBase * 1.3f);
            Vector3 tt;
            Vector3 target = RoutePoint(s - wheelBase * 0.5f + ld, out tt);
            Vector3 local = transform.InverseTransformPoint(target);
            local.y = 0;
            float alpha = Mathf.Atan2(local.x, Mathf.Max(0.5f, local.z - rearAxleZ));
            float lw = Mathf.Max(1f, new Vector2(local.x, local.z - rearAxleZ).magnitude);
            float delta = Mathf.Atan(2f * wheelBase * Mathf.Sin(alpha) / lw) * Mathf.Rad2Deg;
            float limit = Mathf.Lerp(maxSteer, 9f, Mathf.InverseLerp(6f, 22f, v));
            delta = Mathf.Clamp(delta, -limit, limit);
            steerAngle = Mathf.MoveTowards(steerAngle, delta, 140f * dt);

            int nDrive = 0;
            float rDrive = 0.33f, rAvg = 0;
            for (int i = 0; i < wheelColliders.Length; i++) { rAvg += wheelRadius[i]; if (drive[i]) { nDrive++; rDrive = wheelRadius[i]; } }
            rAvg /= Mathf.Max(1, wheelColliders.Length);
            nDrive = Mathf.Max(1, nDrive);

            float resist = massKg * 9.81f * 0.016f + 0.5f * 1.2f * (heavy ? 5f : 0.75f) * v * v;
            float motor = 0, brake = 0;
            bool holding = a < 0 && v < 0.35f;
            if (speed < -0.3f) { brake = massKg * 4f * rAvg; }
            else if (a >= 0)
            {
                motor = (massKg * a + resist) * rDrive / nDrive;
                motor = Mathf.Min(motor, massKg * 3.2f * rDrive / nDrive);
            }
            else
            {
                float need = massKg * (-a) - resist;
                brake = Mathf.Max(0, need) * rAvg / 4f;
            }
            if (holding) brake = Mathf.Max(brake, massKg * 9.81f * 0.35f * rAvg);

            for (int i = 0; i < wheelColliders.Length; i++)
            {
                var wc = wheelColliders[i];
                if (!wc) continue;
                wc.steerAngle = steer[i] ? steerAngle : 0;
                wc.motorTorque = drive[i] ? motor : 0;
                wc.brakeTorque = brake * (i < 2 ? 1.2f : 0.8f);
            }
            AntiRoll(0, 1); AntiRoll(2, 3);
            // hafif aşağı bastırma: yüksek hızda tekerleri yolda tutar
            rb.AddForce(-transform.up * (v * v * massKg * 0.0025f));
            UpdateWheelVisualsPhysics();
            braking = a < -0.6f || holding;
            SetBrakeLight(braking);
        }

        void AntiRoll(int l, int r)
        {
            if (wheelColliders.Length <= r) return;
            var wl = wheelColliders[l]; var wr = wheelColliders[r];
            if (!wl || !wr) return;
            WheelHit hit;
            float tl = 1, tr = 1;
            bool gl = wl.GetGroundHit(out hit);
            if (gl) tl = (-wl.transform.InverseTransformPoint(hit.point).y - wl.radius) / wl.suspensionDistance;
            bool gr = wr.GetGroundHit(out hit);
            if (gr) tr = (-wr.transform.InverseTransformPoint(hit.point).y - wr.radius) / wr.suspensionDistance;
            float force = (tl - tr) * massKg * 7f;
            if (gl) rb.AddForceAtPosition(wl.transform.up * -force, wl.transform.position);
            if (gr) rb.AddForceAtPosition(wr.transform.up * force, wr.transform.position);
        }

        void UpdateWheelVisualsPhysics()
        {
            for (int i = 0; i < wheelColliders.Length && i < wheelVisuals.Length; i++)
            {
                var wc = wheelColliders[i];
                if (!wc || !wheelVisuals[i]) continue;
                Vector3 p; Quaternion q;
                wc.GetWorldPose(out p, out q);
                wheelVisuals[i].SetPositionAndRotation(p, q * RestRot(i));
            }
        }

        // ---------------------------------------------------------------- ray kipi

        public void PoseOnRail(float dt)
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            Vector3 tf, tr;
            Vector3 front = RoutePoint(s + wheelBase * 0.5f, out tf);
            Vector3 back = RoutePoint(s - wheelBase * 0.5f, out tr);
            Vector3 fwd = front - back;
            if (fwd.sqrMagnitude < 1e-4f) fwd = tf;
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Vector3 mid = (front + back) * 0.5f;
            Vector3 pos = mid - rot * new Vector3(0, 0, axleMidZ);
            if (dt <= 0) { transform.SetPositionAndRotation(pos, rot); rb.position = pos; rb.rotation = rot; }
            else { rb.MovePosition(pos); rb.MoveRotation(rot); }
            // tekerlek görselleri
            float yawRate = Vector3.SignedAngle(tr, tf, Vector3.up);
            float st = Mathf.Clamp(Mathf.Atan2(wheelBase * yawRate * Mathf.Deg2Rad, wheelBase) * Mathf.Rad2Deg * 1.6f, -maxSteer, maxSteer);
            steerAngle = Mathf.MoveTowards(steerAngle, st, 120f * Mathf.Max(dt, 0.02f));
            if (spinDeg == null || spinDeg.Length != wheelVisuals.Length) spinDeg = new float[wheelVisuals.Length];
            for (int i = 0; i < wheelVisuals.Length; i++)
            {
                var w = wheelVisuals[i];
                if (!w) continue;
                float r = i < wheelRadius.Length ? wheelRadius[i] : 0.33f;
                spinDeg[i] = Mathf.Repeat(spinDeg[i] + (speed * dt) / Mathf.Max(0.1f, r) * Mathf.Rad2Deg, 360f);
                w.SetPositionAndRotation(transform.TransformPoint(wheelRest[i]),
                    transform.rotation * Quaternion.Euler(0, steer.Length > i && steer[i] ? steerAngle : 0, 0) * Quaternion.Euler(spinDeg[i], 0, 0) * RestRot(i));
            }
        }

        Quaternion RestRot(int i) { return wheelRestRot != null && i < wheelRestRot.Length ? wheelRestRot[i] : Quaternion.identity; }

        // Fizik kipinden raya: konum şeride iz düşürülür.
        public bool CanSwitchToRail()
        {
            if (mode != OHDriveMode.Physics) return false;
            if (transform.up.y < 0.9f || Mathf.Abs(lateral) > 1.2f) return false;
            if (Net.lanes[route[0]].IsConnector) return false;
            return true;
        }

        void OnDisable()
        {
            if (manager && Net != null) Net.UnreserveAll(this);
        }
    }
}
