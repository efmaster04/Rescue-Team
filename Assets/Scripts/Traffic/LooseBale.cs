using System.Collections.Generic;
using UnityEngine;

// Pikaptan kopmuş saman balyası (şef kararı 5): Rigidbody'li fizik objesi. Araçlar (kinematik gövde) iter,
// oyuncu yürüyerek iter (ThirdPersonPlayer.OnControllerColliderHit → PushFrom). lifetime sn sonra ya da
// FallRespawnPlane'in altına düşünce yok olur.
[RequireComponent(typeof(Rigidbody))]
public class LooseBale : MonoBehaviour
{
    public static readonly List<LooseBale> All = new List<LooseBale>();

    public float lifetime = 60f;
    [Tooltip("Oyuncunun ittiği balyanın ulaşabileceği en yüksek yatay hız (m/sn).")]
    public float pushSpeed = 1.6f;
    [Tooltip("Bu yüksekliğin altına inen balya yok olur. NaN ise sahnedeki FallRespawnPlane'den okunur.")]
    public float killBelowY = float.NaN;

    public Rigidbody Body { get; private set; }
    public float SpawnTime { get; private set; }
    public int Pushes { get; private set; }

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        SpawnTime = Time.time;
        if (float.IsNaN(killBelowY))
        {
            var plane = GameObject.Find("FallRespawnPlane");
            killBelowY = plane != null ? plane.transform.position.y : float.NegativeInfinity;
        }
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void FixedUpdate()
    {
        if (Time.time - SpawnTime >= lifetime || transform.position.y < killBelowY)
            Destroy(gameObject);
    }

    // Oyuncu yürürken değdi: balyayı yürüme yönünde, en fazla pushSpeed'e kadar hızlandırır (ağır, yavaş kayar).
    public void PushFrom(Vector3 moveDirection, float walkerSpeed)
    {
        Vector3 dir = moveDirection; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f || walkerSpeed < 0.1f) return;
        dir.Normalize();
        float target = Mathf.Min(pushSpeed, walkerSpeed);
        float along = Vector3.Dot(Body.linearVelocity, dir);
        if (along >= target) return;
        Body.AddForce(dir * (target - along), ForceMode.VelocityChange);
        Body.WakeUp();
        Pushes++;
    }
}
