using System.Collections.Generic;
using UnityEngine;

// Trafik konisi (kullanıcı kararı 4). Oyuncu E ile alır, elinde taşır, E ile baktığı yere dik koyar.
// Dik duran (eğim ≤ 30°) ve yol yüzeyinde olan koni, bulunduğu şeridi o noktada kapatır (LaneOccupancy sorar).
// Araç koniye çarpınca koni fizikle savrulur ve çarpma sesi çalar. FallRespawnPlane'in altına düşen koni yok olur
// (geri getirme mekaniği YOK).
//
// Bu bileşen koninin kendisinde değil, ayrı bir kontrol objesinde durur ve koniyi (target) yönetir: koniler
// çekici modelinin parçasıdır, çekici hiyerarşisine oyun betiği eklenmez. Kontrol objesi koniyi izler (ses kaynağı onunla gider).
[DefaultExecutionOrder(60)] // taşıyıcı hareket ettikten sonra elde konumlanır
public class TrafficCone : MonoBehaviour
{
    public static readonly List<TrafficCone> All = new List<TrafficCone>();

    [Tooltip("Yönetilen koni (Rigidbody + convex MeshCollider).")]
    public Transform target;
    [Tooltip("Koninin 'yukarı' ekseni, kendi yerel uzayında (FBX eksenleri Blender'a göre olabilir; builder ölçer).")]
    public Vector3 localUp = Vector3.up;
    [Tooltip("Pivotun taban düzleminden yüksekliği, yukarı ekseni boyunca (m).")]
    public float baseOffset = 0f;
    [Tooltip("Taban yarıçapı (m): araç ön tamponu koninin bu kadar gerisinde durur (+ durma payı).")]
    public float radius = 0.2f;
    public float height = 0.7f;
    [Tooltip("Yol yüzeyi (SM_B1_Road). Koni yalnız bunun üstündeyse şeridi kapatır.")]
    public Transform road;
    [Tooltip("Bu yüksekliğin altına inen koni yok olur (FallRespawnPlane).")]
    public float killBelowY = float.NegativeInfinity;

    [Header("Savrulma (trafik aracı çarpınca)")]
    public float flingForwardFactor = 1.5f;
    public float flingMinSpeed = 5f;
    public float flingSideSpeed = 2f;
    public float flingUpSpeed = 2.5f;

    public AudioSource hitSource;

    public bool IsHeld { get { return holder != null; } }
    public ThirdPersonPlayer Holder { get { return holder; } }
    public Rigidbody Body { get; private set; }
    // Araç çarpma sayısı (test ve hata ayıklama; batchmode'da isPlaying güvenilmez)
    public int VehicleHits { get; private set; }
    public float LastHitTime { get; private set; } = -100f;

    private ThirdPersonPlayer holder;
    private Collider[] colliders;
    private float nextSoundTime;
    private readonly HashSet<LaneFollower> touching = new HashSet<LaneFollower>();

    private void Awake()
    {
        if (target == null) target = transform;
        Body = target.GetComponent<Rigidbody>();
        colliders = target.GetComponentsInChildren<Collider>(true);
        if (hitSource == null) hitSource = gameObject.AddComponent<AudioSource>();
        hitSource.clip = SynthClips.Cached("ConeHit", SynthClips.ConeHit);
        hitSource.playOnAwake = false;
        hitSource.loop = false;
        hitSource.spatialBlend = 1f;  // 3D
        hitSource.dopplerLevel = 1f;  // Doppler açık
        hitSource.rolloffMode = AudioRolloffMode.Logarithmic;
        hitSource.minDistance = 3f;
        hitSource.maxDistance = 120f;
        hitSource.volume = 0.8f;
        SyncSelf();
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public Vector3 WorldUp { get { return target.rotation * localUp.normalized; } }
    public float TiltAngle { get { return Vector3.Angle(WorldUp, Vector3.up); } }
    // Tabanın merkezi (dünya)
    public Vector3 BasePoint { get { return target.position - WorldUp * baseOffset; } }
    public Vector3 Center { get { return BasePoint + WorldUp * (height * 0.4f); } }

    public bool IsUpright(float maxAngle) { return TiltAngle <= maxAngle; }

    // Koni bu şeridi kapatıyor mu? s: koninin şeritteki yeri.
    public bool ClosesLane(LanePath lane, float maxAngle, out float s)
    {
        s = 0f;
        if (lane == null || target == null || IsHeld || !IsUpright(maxAngle)) return false;
        if (Body != null && !Body.isKinematic && Body.linearVelocity.sqrMagnitude > 0.25f) return false; // hâlâ savruluyor
        if (!OnRoad()) return false;
        Vector3 p = BasePoint;
        s = lane.Project(p, out float lateral, out Vector3 onLane);
        if (Mathf.Abs(p.y - onLane.y) > 1.5f) return false;
        return Mathf.Abs(lateral) <= lane.width * 0.5f;
    }

    // Tabanın hemen altında yol yüzeyi var mı?
    public bool OnRoad()
    {
        if (road == null) return true;
        if (!GroundProbe.Find(BasePoint + Vector3.up * 0.4f, 1.0f, out RaycastHit hit)) return false;
        return hit.transform.IsChildOf(road);
    }

    // ---- Taşıma (yalnız PlayerInteraction ve ThirdPersonPlayer çağırır) ----
    public void PickUp(ThirdPersonPlayer by)
    {
        holder = by;
        if (Body != null)
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
        }
        foreach (var c in colliders) if (c != null) c.enabled = false;
        FollowHolder();
    }

    // Verilen zemin noktasına dik koyar.
    public void PlaceAt(Vector3 groundPoint, float yaw)
    {
        holder = null;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.FromToRotation(localUp.normalized, Vector3.up);
        target.SetPositionAndRotation(groundPoint + Vector3.up * (baseOffset + 0.01f), rot);
        Release(Vector3.zero);
    }

    // Test ve editör: koniyi verilen pozla fiziğe bırakır.
    public void PlaceWithPose(Vector3 position, Quaternion rotation)
    {
        holder = null;
        target.SetPositionAndRotation(position, rotation);
        Release(Vector3.zero);
    }

    // Elden düşer (taşıyıcı çarpıldı): olduğu yerden, verilen hızla fiziğe bırakılır.
    public void Drop(Vector3 velocity)
    {
        holder = null;
        Release(velocity);
    }

    private void Release(Vector3 velocity)
    {
        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (Body != null)
        {
            Body.position = target.position;
            Body.rotation = target.rotation;
            Body.isKinematic = false;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.linearVelocity = velocity;
            Body.angularVelocity = Vector3.zero;
            Body.WakeUp();
        }
        Physics.SyncTransforms();
        SyncSelf();
    }

    private void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        if (holder != null) FollowHolder();
        SyncSelf();
    }

    private void FixedUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        if (IsHeld) return;
        if (target.position.y < killBelowY)
        {
            Destroy(target.gameObject);
            Destroy(gameObject);
            return;
        }
        DetectVehicleContacts();
    }

    // Kontrol objesi (ses kaynağı) koninin yerinde durur.
    private void SyncSelf()
    {
        if (target != null && target != transform) transform.position = target.position;
    }

    // Elde: taşıyıcının önünde, bel hizasında, dik.
    private void FollowHolder()
    {
        if (holder == null) return;
        Transform r = holder.transform;
        Vector3 fwd = r.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.FromToRotation(localUp.normalized, Vector3.up);
        Vector3 basePos = r.position + fwd.normalized * 0.45f + Vector3.up * 0.35f;
        target.SetPositionAndRotation(basePos + Vector3.up * baseOffset, rot);
    }

    // Araç teması (gövde collider'ı koniye değdi): ses (her araç, ambulans dahil) ve trafik aracıysa savrulma itkisi.
    // Fizik itişini kinematik gövde zaten yapar; burada yalnız çarpma anı yakalanır.
    private void DetectVehicleContacts()
    {
        Vector3 c = Center;
        foreach (var f in LaneOccupancy.Followers)
        {
            if (f == null) continue;
            var box = f.GetComponent<BoxCollider>();
            if (box == null) continue;
            bool touch = Vector3.Distance(box.ClosestPoint(c), c) <= radius + 0.08f;
            if (!touch) { touching.Remove(f); continue; }
            if (!touching.Add(f)) continue; // temas sürüyor
            OnVehicleHit(f);
        }
    }

    private void OnVehicleHit(LaneFollower follower)
    {
        VehicleHits++;
        LastHitTime = Time.time;
        if (Time.time >= nextSoundTime)
        {
            nextSoundTime = Time.time + 0.3f;
            hitSource.Play();
        }
        var vehicle = follower.GetComponent<TrafficVehicle>();
        if (vehicle == null || Body == null || Body.isKinematic) return;
        Transform v = follower.transform;
        Vector3 fwd = v.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float side = Vector3.Dot(target.position - v.position, right) >= 0f ? 1f : -1f;
        float speed = Mathf.Max(follower.Speed * flingForwardFactor, flingMinSpeed);
        Body.linearVelocity = fwd * speed + right * side * flingSideSpeed + Vector3.up * flingUpSpeed;
        Body.angularVelocity = right * side * 6f;
    }
}
