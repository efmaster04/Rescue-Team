using System;
using UnityEngine;

// Boşluğa düşen oyuncu yeniden doğar (2026-10-12 kararı "boşluk → yeniden doğma", şef kararı 8).
// Oyuncu (yerdeyse ragdoll kalçası) FallRespawnPlane'in altına inince PlayerSpawn'da, ayakta ve kontrolü elinde doğar.
// Araçlar yerdeki oyuncuyu itmeye devam eder (bilinçli kaos); uçuruma itilirse burası yakalar.
public class PlayerFallRespawn : MonoBehaviour
{
    public ThirdPersonPlayer player;
    [Tooltip("Yeniden doğma noktası (PlayerSpawn işareti).")]
    public Transform spawnPoint;
    [Tooltip("Bu yüksekliğin altı boşluk sayılır (FallRespawnPlane).")]
    public float killBelowY = -50f;

    public int Respawns { get; private set; }
    public float LastRespawnTime { get; private set; } = -100f;
    public event Action<PlayerFallRespawn> Respawned;

    private Quaternion spawnRotation;

    private void Awake()
    {
        if (player == null) player = GetComponent<ThirdPersonPlayer>();
    }

    private void Start()
    {
        // Doğma yönü: sahnedeki başlangıç yönü (builder oyuncuyu şerit boyunca bakacak şekilde koyar)
        Vector3 f = transform.forward; f.y = 0f;
        spawnRotation = f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
    }

    private void LateUpdate()
    {
        if (player == null || spawnPoint == null) return;
        Vector3 p = player.IsDown && player.knockdown != null ? player.knockdown.GroundPosition : player.transform.position;
        if (p.y < killBelowY) Respawn();
    }

    public void Respawn()
    {
        Vector3 at = spawnPoint.position;
        if (GroundProbe.Find(at + Vector3.up * 2f, 10f, out RaycastHit hit)) at = hit.point + Vector3.up * 0.02f;
        player.RespawnAt(at, spawnRotation);
        Respawns++;
        LastRespawnTime = Time.time;
        Respawned?.Invoke(this);
    }
}
