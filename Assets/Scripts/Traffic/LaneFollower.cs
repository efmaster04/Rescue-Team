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
    [Tooltip("Ön tekerleklerin en büyük direksiyon açısı (derece).")]
    public float maxSteerAngle = 35f;
    [Tooltip("Direksiyonun üstel yumuşatma süresi (sn). <= 0 ise anında döner.")]
    public float steerSmoothTime = 0.2f;
    [Tooltip("Gövde yönünün üstel yumuşatma süresi (sn). <= 0 ise şerit yönüne anında döner.")]
    public float headingSmoothTime = 0.15f;
    [Tooltip("Gövde yönünün hedef yönden en büyük sapması (derece). 6 m'den uzun araçlarda uzunlukla ters orantılı azalır.")]
    public float maxHeadingLagDeg = 1.0f;

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
    public float CurrentSteerAngle { get; private set; } // sağa pozitif, derece
    public int FrontWheelCount { get; private set; }
    public bool HasStop { get { return !float.IsPositiveInfinity(stopDistance); } }
    public float StopDistance { get { return stopDistance; } }
    // Durak noktasına varıp tamamen durdu mu?
    public bool AtStop { get { return HasStop && Distance >= stopDistance - 0.0005f && Speed <= 0.0001f; } }
    // Şeridin sonuna geldi mi?
    public bool AtEnd { get { return lane != null && Distance >= lane.Length - 0.0005f; } }

    private float stopDistance = float.PositiveInfinity;
    private Rigidbody body;
    private Vector3[] wheelAxes; // başlangıç tekerlek uzayında aks ekseni
    private Vector3[] wheelUpAxes;
    private Quaternion[] baseLocalRotations;
    private bool[] frontWheels;
    private float spinDegrees;
    private float frontAxleOffset;

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
        wheelAxes = new Vector3[wheels.Length];
        wheelUpAxes = new Vector3[wheels.Length];
        baseLocalRotations = new Quaternion[wheels.Length];
        frontWheels = new bool[wheels.Length];
        FrontWheelCount = 0;
        frontAxleOffset = 0f;
        float meanZ = 0f;
        int count = 0;
        for (int i = 0; i < wheels.Length; i++)
            if (wheels[i] != null)
            {
                meanZ += transform.InverseTransformPoint(wheels[i].position).z;
                count++;
            }
        if (count > 0) meanZ /= count;

        for (int i = 0; i < wheels.Length; i++)
        {
            Transform wheel = wheels[i];
            if (wheel == null) continue;
            baseLocalRotations[i] = wheel.localRotation;
            // FBX ara ebeveynlerini de içeren kök-göreli dönüş: dünya yönüne bağımlı değil.
            // Böylece Awake eğimli/dönmüş araçta çalışsa da eksenler başlangıç tekerlek uzayında kalır.
            Quaternion rootRelative = wheel.localRotation;
            for (Transform parent = wheel.parent; parent != null && parent != transform; parent = parent.parent)
                rootRelative = parent.localRotation * rootRelative;
            Quaternion toWheel = Quaternion.Inverse(rootRelative);
            wheelAxes[i] = toWheel * Vector3.right;
            wheelUpAxes[i] = toWheel * Vector3.up;
            float z = transform.InverseTransformPoint(wheel.position).z;
            frontWheels[i] = z > meanZ;
            if (frontWheels[i])
            {
                FrontWheelCount++;
                frontAxleOffset += z;
            }
        }
        if (FrontWheelCount > 0) frontAxleOffset = Mathf.Max(0f, frontAxleOffset / FrontWheelCount);
    }

    private void ApplyWheelRotations()
    {
        for (int i = 0; i < wheels.Length; i++)
            if (wheels[i] != null)
                wheels[i].localRotation = baseLocalRotations[i]
                    * Quaternion.AngleAxis(frontWheels[i] ? CurrentSteerAngle : 0f, wheelUpAxes[i])
                    * Quaternion.AngleAxis(spinDegrees, wheelAxes[i]);
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
        if (wheelAxes == null || wheelAxes.Length != wheels.Length) CacheWheelAxes();
        CurrentSteerAngle = 0f;
        ApplyWheelRotations();
    }

    public void StopAt(float s) { stopDistance = s; }
    public void ClearStop() { stopDistance = float.PositiveInfinity; }

    // Dingil mesafesi (m): ön-arka aks arası; dingil bilgisi yoksa görsel ön aksın kökten uzaklığının iki katı, o da yoksa 3 m.
    public float Wheelbase
    {
        get
        {
            if (axleHalfSpan > 0.01f) return axleHalfSpan * 2f;
            if (wheelAxes == null || wheelAxes.Length != wheels.Length) CacheWheelAxes();
            return frontAxleOffset > 0.1f ? frontAxleOffset * 2f : 3f;
        }
    }

    // Şerit dışı manevra (hırsız kamyonetinin park / kalkış manevrası, ThiefVan): konum ve yön dışarıdan verilir, burada yalnız
    // gövde taşınır ve tekerlek görseli (dönüş + direksiyon) uygulanır. travelled: bu adımda kat edilen yol (m; geri gidişte
    // negatif, tekerlek geri döner), steerDeg: ön tekerlek açısı (sağa +), laneS: kökün şerit izdüşümü (şerit doluluk servisi
    // Distance / FrontS / RearS'i buradan okur). Normal sürüş (Tick) bu çağrıdan etkilenmez; şeride dönüşte Place kullanılır.
    public void ApplyExternalPose(Vector3 position, Quaternion rotation, float travelled, float steerDeg, float laneS, float dt)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (lane != null) Distance = Mathf.Clamp(laneS, 0f, lane.Length);
        Speed = dt > 1e-5f ? Mathf.Abs(travelled) / dt : 0f;
        body.MovePosition(position);
        body.MoveRotation(rotation);
        float limit = Mathf.Abs(maxSteerAngle);
        CurrentSteerAngle = Mathf.Clamp(steerDeg, -limit, limit);
        if (wheelAxes == null || wheelAxes.Length != wheels.Length) CacheWheelAxes();
        float spinStep = travelled / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg;
        ExternalWheelSpinTotal += Mathf.Abs(spinStep);
        spinDegrees = Mathf.Repeat(spinDegrees + spinStep, 360f);
        ApplyWheelRotations();
    }

    // Tekerlek dönüşü (derece, 0–360) ve şerit dışı manevrada biriken toplam dönüş (derece; testler okur)
    public float WheelSpinDegrees { get { return spinDegrees; } }
    public float ExternalWheelSpinTotal { get; private set; }

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
        if (headingSmoothTime > 0f)
        {
            Quaternion smoothed = Quaternion.Slerp(body.rotation, r, 1f - Mathf.Exp(-dt / headingSmoothTime));
            float length = frontOverhang + rearOverhang;
            float lagLimit = Mathf.Max(0f, maxHeadingLagDeg);
            // Uzun gövdenin uçları virajda daha geniş alan süpürür; 6 m üstünde sapmayı
            // uzunlukla ters orantılı azaltarak karşı şeride taşma riskini sınırlarız.
            if (length > 6f) lagLimit *= 6f / length;
            r = Quaternion.RotateTowards(r, smoothed, lagLimit);
        }
        body.MovePosition(p);
        body.MoveRotation(r);

        // Tekerlekler: kat edilen yol / yarıçap (radyan → derece).
        float degrees = (s - old) / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg;
        if (wheelAxes == null || wheelAxes.Length != wheels.Length) CacheWheelAxes();
        if (s > old)
        {
            // Ön aks teğeti ile uygulanacak gövde yönünün yatay açısı: yumuşatılan gövdenin
            // virajda geride kalan yönünü de karşılar; dingil bilgisi olmayan ambulansta görsel aks kullanılır.
            float ahead = axleHalfSpan > 0f ? axleHalfSpan : frontAxleOffset;
            Vector3 forward = Vector3.ProjectOnPlane(r * Vector3.forward, Vector3.up);
            Vector3 tangent = Vector3.ProjectOnPlane(lane.TangentAt(s + ahead), Vector3.up);
            float limit = Mathf.Abs(maxSteerAngle);
            float target = Mathf.Clamp(Vector3.SignedAngle(forward, tangent, Vector3.up), -limit, limit);
            float blend = steerSmoothTime > 0f ? 1f - Mathf.Exp(-dt / steerSmoothTime) : 1f;
            CurrentSteerAngle = Mathf.Lerp(CurrentSteerAngle, target, blend);
        }
        // Dururken direksiyon korunur; spin her seferinde başlangıç pozundan kurulduğu için eksen kaymaz.
        spinDegrees = Mathf.Repeat(spinDegrees + degrees, 360f);
        ApplyWheelRotations();
    }
}
