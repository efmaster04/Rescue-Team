using System.Collections.Generic;
using UnityEngine;

// Pikabın kasasındaki iki saman balyası (şef kararı 5). Normalde araca bağlı (çocuk obje, fizik yok).
// Pikap koni önünde 20 sn bekleyip koniye çarparak geçtiğinde (TrafficVehicle.ConeRammed, sonra koniye temas)
// balyalar araçtan kopar: Rigidbody olur (~25 kg) ve itkiyle savrulur. Yolda kalan balya fizik objesidir
// (oyuncu iter, araçlar iter); 60 sn sonra ya da FallRespawnPlane altına düşünce yok olur (LooseBale).
[RequireComponent(typeof(TrafficVehicle))]
public class HayCargo : MonoBehaviour
{
    [Tooltip("Kasadaki balyalar (SM_Pickup_Hay_01, _02).")]
    public Transform[] bales = new Transform[0];
    [Tooltip("Balya kütlesi (kg).")]
    public float baleMass = 25f;
    [Tooltip("Yolda kalma süresi (sn).")]
    public float lifetime = 60f;
    [Tooltip("Savrulma: yukarı (m/sn), aracın gerisine (m/sn), yana (m/sn), dönme (rad/sn).")]
    public float launchUp = 4.5f;
    public float launchBack = 2.5f;
    public float launchSide = 1.6f;
    public float launchSpin = 3f;
    [Tooltip("Ram kararından sonra koniye temas bu kadar sürede olmazsa (sn) balyalar yine kopar.")]
    public float fallbackSeconds = 2f;

    public bool Detached { get; private set; }
    public float DetachTime { get; private set; } = -1f;
    public readonly List<LooseBale> Loose = new List<LooseBale>();

    private TrafficVehicle vehicle;
    private TrafficCone rammedCone;
    private float ramTime = -1f;

    private void Awake()
    {
        vehicle = GetComponent<TrafficVehicle>();
        vehicle.ConeRammed += OnConeRammed;
    }

    private void OnDestroy()
    {
        if (vehicle != null) vehicle.ConeRammed -= OnConeRammed;
    }

    private void OnConeRammed(TrafficVehicle v, TrafficCone cone, float waited)
    {
        if (Detached || ramTime >= 0f) return;
        rammedCone = cone;
        ramTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (Detached || ramTime < 0f) return;
        bool hit = rammedCone == null || rammedCone.LastHitTime >= ramTime;
        if (hit || Time.time - ramTime >= fallbackSeconds) Detach();
    }

    // Balyaları koparır (test de çağırabilir).
    public void Detach()
    {
        if (Detached) return;
        Detached = true;
        DetachTime = Time.time;
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float speed = vehicle != null && vehicle.Follower != null ? vehicle.Follower.Speed : 0f;
        for (int i = 0; i < bales.Length; i++)
        {
            var b = bales[i];
            if (b == null) continue;
            b.SetParent(null, true);
            var col = b.GetComponent<BoxCollider>();
            if (col == null) col = b.gameObject.AddComponent<BoxCollider>(); // MeshFilter sınırlarına oturur
            var rb = b.gameObject.AddComponent<Rigidbody>();
            rb.mass = baleMass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            float side = i % 2 == 0 ? 1f : -1f;
            // Araç öne atılırken balyalar kasadan geriye ve havaya fırlar (eylemsizlik + çarpma sarsıntısı)
            rb.linearVelocity = fwd * (speed - launchBack * (1f + 0.3f * i)) + right * side * launchSide + Vector3.up * (launchUp + 0.6f * i);
            rb.angularVelocity = (right * side * 0.6f + fwd * 0.4f + Vector3.up * side * 0.3f).normalized * launchSpin;
            var lb = b.gameObject.AddComponent<LooseBale>();
            lb.lifetime = lifetime;
            Loose.Add(lb);
        }
    }
}
