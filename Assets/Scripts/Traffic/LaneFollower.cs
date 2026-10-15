using UnityEngine;

// Genel şerit takipçisi: bir aracı LanePath üzerinde sürer. İleride bütün trafik araçları bunu kullanacak;
// araca özel davranış (ambulansın durağı, kornası vb.) ayrı bir sınıfta durur (bkz. AmbulanceController).
//
// - Hız, ivme, frenleme ve "s mesafesinde dur" desteği var. Durağa yumuşak frenle yaklaşır.
// - Konum ve yükseklik polyline'dan, yön şerit teğetinden gelir (kök pivotu = 4 tekerleğin yere değdiği dikdörtgenin merkezi).
// - Tekerlekler kat edilen yol / yarıçap kadar döner.
// - Rigidbody kinematiktir: fizik aracı itemez, ama araç önündeki dinamik cisimleri (koni) iter.
[RequireComponent(typeof(Rigidbody))]
public class LaneFollower : MonoBehaviour
{
    public LanePath lane;

    [Tooltip("Seyir hızı (m/sn).")]
    public float cruiseSpeed = 12f;
    [Tooltip("Hızlanma (m/sn²).")]
    public float acceleration = 4f;
    [Tooltip("Frenleme (m/sn²). Durağa bu yavaşlamayla yaklaşır.")]
    public float braking = 6f;

    [Tooltip("Dönen tekerlek objeleri (pivot aks merkezinde).")]
    public Transform[] wheels = new Transform[0];
    [Tooltip("Tekerlek yarıçapı (m).")]
    public float wheelRadius = 0.38f;

    [Tooltip("Kökten ön tampona / arka tampona uzaklık (m). Şerit doluluk servisi (araç takibi) kullanır.")]
    public float frontOverhang = 2.5f;
    public float rearOverhang = 2.5f;
    [Tooltip("Ön ve arka aksın kökten uzaklığı (m, yarım dingil mesafesi). > 0 ise iki aks da şerit çizgisinde durur ve kök onların ortasına gelir (virajda uzun araç dışa taşmaz). 0 = kök şerit çizgisinde, yön teğet (ambulans).")]
    public float axleHalfSpan = 0f;
    [Tooltip("Şerit çizgisinden sağa kayma (m). Geniş araçlar (kamyon) şeridin sağına yakın gider; karşı şeritteki araçla virajda boşluk kalır. 0 = ortada.")]
    public float lateralOffset = 0f;

    // Şerit üzerinde ön ve arka tamponun s değeri
    public float FrontS { get { return Distance + frontOverhang; } }
    public float RearS { get { return Distance - rearOverhang; } }

    public float Distance { get; private set; }
    public float Speed { get; private set; }
    public bool HasStop { get { return !float.IsPositiveInfinity(stopDistance); } }
    public float StopDistance { get { return stopDistance; } }
    // Durak noktasına varıp tamamen durdu mu?
    public bool AtStop { get { return HasStop && Distance >= stopDistance - 0.0005f && Speed <= 0.0001f; } }
    // Şeridin sonuna geldi mi?
    public bool AtEnd { get { return lane != null && Distance >= lane.Length - 0.0005f; } }

    private float stopDistance = float.PositiveInfinity;
    private Rigidbody body;
    private Vector3[] wheelAxes; // her tekerleğin kendi yerel uzayında aks ekseni

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        CacheWheelAxes();
    }

    // Şerit doluluk servisine kayıt: aynı şeritteki araçlar birbirini (ambulans dahil) buradan görür.
    private void OnEnable() { LaneOccupancy.Register(this); }
    private void OnDisable() { LaneOccupancy.Unregister(this); }

    private void CacheWheelAxes()
    {
        // Aks ekseni = aracın sağ yönü; tekerleğin kendi uzayına çevrilir (FBX'te tekerlek eksenleri Blender'a göre olabilir).
        wheelAxes = new Vector3[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
            if (wheels[i] != null)
                wheelAxes[i] = Quaternion.Inverse(wheels[i].rotation) * transform.right;
    }

    // Aracı şeridin s noktasına koyar (ışınlama) ve başlangıç hızını verir.
    public void Place(LanePath path, float s, float startSpeed)
    {
        lane = path;
        if (body == null) body = GetComponent<Rigidbody>();
        Distance = Mathf.Clamp(s, 0f, lane.Length);
        Speed = Mathf.Max(0f, startSpeed);
        Pose(Distance, out Vector3 p, out Quaternion r);
        transform.SetPositionAndRotation(p, r);
        body.position = p;
        body.rotation = r;
    }

    public void StopAt(float s) { stopDistance = s; }
    public void ClearStop() { stopDistance = float.PositiveInfinity; }

    // s'deki kök pozu. axleHalfSpan > 0: aks noktaları şeritte, kök aralarında, yön akstan aksa.
    public void Pose(float s, out Vector3 position, out Quaternion rotation)
    {
        float h = axleHalfSpan;
        if (h > 0.01f && s - h >= 0f && s + h <= lane.Length)
        {
            Vector3 front = lane.PointAt(s + h), rear = lane.PointAt(s - h);
            position = (front + rear) * 0.5f + lane.RightAt(s) * lateralOffset;
            rotation = Quaternion.LookRotation(front - rear, Vector3.up);
            return;
        }
        position = lane.PointAt(s) + (lateralOffset != 0f ? lane.RightAt(s) * lateralOffset : Vector3.zero);
        rotation = Quaternion.LookRotation(lane.TangentAt(s), Vector3.up);
    }

    // Tek adımlık hız/konum integratörü. Tahmin (TravelTime) ile gerçek sürüş AYNI fonksiyonu kullanır;
    // böylece varış zamanı, frenleme dahil, kare kare aynı çıkar.
    public static void Step(ref float s, ref float v, float stop, float maxSpeed, float accel, float brake, float dt)
    {
        float remaining = stop - s;
        if (remaining <= 0.0005f)
        {
            s = stop;
            v = 0f;
            return;
        }
        // Kalan yolda durabileceğimiz en yüksek hız: v² = 2·a·d
        float target = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * brake * remaining));
        float next = v > target ? Mathf.Max(target, v - brake * dt) : Mathf.Min(target, v + accel * dt);
        float move = (v + next) * 0.5f * dt;
        if (move >= remaining)
        {
            s = stop;
            v = 0f;
        }
        else
        {
            s += move;
            v = next;
        }
    }

    // from → to arasının, startSpeed ile başlayıp to'da durarak kaç saniye süreceği (dt adımlarıyla).
    public float TravelTime(float from, float to, float startSpeed, float dt)
    {
        float s = from, v = startSpeed, t = 0f;
        while (s < to - 0.0005f && t < 600f)
        {
            Step(ref s, ref v, to, cruiseSpeed, acceleration, braking, dt);
            t += dt;
        }
        return t;
    }

    // Fizik adımında çağrılır. speedLimit: bu adım için üst hız sınırı (m/sn), ör. önde yaya varsa düşük.
    public void Tick(float dt, float speedLimit)
    {
        if (lane == null) return;
        float old = Distance;
        float s = Distance, v = Speed;
        float stop = HasStop ? stopDistance : float.PositiveInfinity;
        Step(ref s, ref v, stop, Mathf.Min(cruiseSpeed, speedLimit), acceleration, braking, dt);
        if (s > lane.Length) s = lane.Length;
        Distance = s;
        Speed = v;

        Pose(s, out Vector3 p, out Quaternion r);
        body.MovePosition(p);
        body.MoveRotation(r);

        // Tekerlekler: kat edilen yol / yarıçap (radyan → derece).
        float degrees = (s - old) / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg;
        if (wheelAxes == null || wheelAxes.Length != wheels.Length) CacheWheelAxes();
        for (int i = 0; i < wheels.Length; i++)
            if (wheels[i] != null)
                wheels[i].localRotation = wheels[i].localRotation * Quaternion.AngleAxis(degrees, wheelAxes[i]);
    }
}
