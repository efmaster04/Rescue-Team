using System.Collections.Generic;
using UnityEngine;

// Trafik konisi (kullanıcı kararı 4). Oyuncu E ile alır, elinde taşır, E ile baktığı yere dik koyar.
// Dik duran (eğim ≤ 30°) ve yol yüzeyinde olan koni, bulunduğu şeridi o noktada kapatır (LaneOccupancy sorar).
// Araç koniye çarpınca koni fizikle savrulur ve çarpma sesi çalar. FallRespawnPlane'in altına düşen koni yok olur
// (geri getirme mekaniği YOK).
[DefaultExecutionOrder(60)] // taşıyıcı hareket ettikten sonra elde konumlanır
public class TrafficCone : MonoBehaviour
{
    public static readonly List<TrafficCone> All = new List<TrafficCone>();

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

    [Header("Savrulma (araç çarpınca)")]
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

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
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
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public Vector3 WorldUp { get { return transform.rotation * localUp.normalized; } }
    public float TiltAngle { get { return Vector3.Angle(WorldUp, Vector3.up); } }
    // Tabanın merkezi (dünya)
    public Vector3 BasePoint { get { return transform.position - WorldUp * baseOffset; } }

    public bool IsUpright(float maxAngle) { return TiltAngle <= maxAngle; }

    // Koni bu şeridi kapatıyor mu? s: koninin şeritteki yeri.
    public bool ClosesLane(LanePath lane, float maxAngle, out float s)
    {
        s = 0f;
        if (lane == null || IsHeld || !IsUpright(maxAngle)) return false;
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

    // ---- Taşıma (yalnız PlayerInteraction çağırır) ----
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
        Vector3 pos = groundPoint + Vector3.up * (baseOffset + 0.01f);
        transform.SetPositionAndRotation(pos, rot);
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
            Body.position = transform.position;
            Body.rotation = transform.rotation;
            Body.isKinematic = false;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.linearVelocity = velocity;
            Body.angularVelocity = Vector3.zero;
            Body.WakeUp();
        }
        Physics.SyncTransforms();
    }

    private void LateUpdate()
    {
        if (holder != null) FollowHolder();
    }

    private void FixedUpdate()
    {
        if (!IsHeld && transform.position.y < killBelowY)
            Destroy(gameObject);
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
        transform.SetPositionAndRotation(basePos + Vector3.up * baseOffset, rot);
    }

    // Araç çarpması: ses (her araç, ambulans dahil) ve trafik aracıysa savrulma itkisi.
    private void OnCollisionEnter(Collision collision)
    {
        if (IsHeld) return;
        var follower = collision.collider.GetComponentInParent<LaneFollower>();
        if (follower == null) return;
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
        float side = Vector3.Dot(transform.position - v.position, right) >= 0f ? 1f : -1f;
        float speed = Mathf.Max(follower.Speed * flingForwardFactor, flingMinSpeed);
        Body.linearVelocity = fwd * speed + right * side * flingSideSpeed + Vector3.up * flingUpSpeed;
        Body.angularVelocity = right * side * 6f;
    }
}
