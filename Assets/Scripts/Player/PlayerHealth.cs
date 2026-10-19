using System;
using System.Collections.Generic;
using UnityEngine;

// U3 (kullanıcı kararı "Can ve para", 2026-10-19): oyuncu canı.
//  - TakeDamage: can düşer, HitReact klibi + kısa sendeleme (kontrol kaybı, geri itilme). Hasar kaynağı şimdilik yalnız bu API
//    (hırsızlar U4'te çağırır; testler doğrudan çağırır).
//  - Can 0 → mevcut PlayerKnockdown akışıyla yere düşer (ragdoll); ThirdPersonPlayer.KnockDown elindeki her şeyi düşürür
//    (sedye bırakılır, koni/kangal/eşya yere, sırttaki hasta yere).
//  - Arkadaş/bot kaldırırsa (PlayerKnockdown.Revived) can reviveFraction (%50) ile gelir.
//  - respawnSeconds (15 sn) içinde kaldırılmazsa çekicinin yanındaki yeniden doğma noktasında tam canla doğar (geri sayım HUD'da).
//  - Can hasar almadan regenDelay (6 sn) sonra regenPerSecond (5/sn) dolar.
//  - Araç çarpması / 3 m+ düşüş (mevcut kurallar) canı DEĞİŞTİRMEZ ve geri sayım başlatmaz (en sade seçenek).
public class PlayerHealth : MonoBehaviour, IDamageable
{
    public static readonly List<PlayerHealth> All = new List<PlayerHealth>();

    public ThirdPersonPlayer player;
    public HealthRules rules;
    [Tooltip("Can 0 düşüşünden sonra yeniden doğma noktası (çekicinin yanı).")]
    public Transform respawnPoint;
    [Tooltip("HitReact klibi için (boşsa klip oynamaz).")]
    public CrewAnimation crewAnimation;

    public float Current { get; private set; }
    public float Max { get { return rules != null ? rules.maxHealth : 100f; } }
    public float Fraction { get { return Mathf.Clamp01(Current / Max); } }
    // Can 0'la mı yerde (yalnız o zaman geri sayım var)
    public bool DownByHealth { get; private set; }
    // Yeniden doğmaya kalan süre (sn); geri sayım yoksa -1
    public float RespawnRemaining { get { return DownByHealth && player != null && player.IsDown ? Mathf.Max(0f, downAt + RespawnSeconds - Time.time) : -1f; } }
    public float RespawnSeconds { get { return rules != null ? rules.respawnSeconds : 15f; } }
    public float LastDamageTime { get; private set; } = -100f;
    public int Hits { get; private set; }
    public int Deaths { get; private set; }
    public int Respawns { get; private set; }
    public int RevivesWithHealth { get; private set; }

    public event Action<PlayerHealth, float> Damaged;   // (can, miktar)
    public event Action<PlayerHealth> DownedByHealth;
    public event Action<PlayerHealth> Respawned;

    private float downAt = -1f;

    private void Awake()
    {
        if (player == null) player = GetComponent<ThirdPersonPlayer>();
        if (crewAnimation == null) crewAnimation = GetComponent<CrewAnimation>();
        Current = Max;
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (player != null && player.knockdown != null) player.knockdown.Revived += OnRevived;
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (player != null && player.knockdown != null) player.knockdown.Revived -= OnRevived;
    }

    private void Start()
    {
        // knockdown Awake'ten sonra bağlanmış olabilir (builder sırası)
        if (player != null && player.knockdown != null) { player.knockdown.Revived -= OnRevived; player.knockdown.Revived += OnRevived; }
    }

    // Hasar: yerdeyken işlenmez. source: vuranın yeri (sendeleme ondan uzağa); null ise oyuncunun arkasına.
    public bool TakeDamage(float amount, Transform source)
    {
        if (player == null || player.IsDown || amount <= 0f || Current <= 0f) return false;
        Current = Mathf.Max(0f, Current - amount);
        LastDamageTime = Time.time;
        Hits++;
        Vector3 away = source != null ? player.transform.position - source.position : -player.transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) away = -player.transform.forward;
        away.Normalize();
        Damaged?.Invoke(this, amount);
        if (Current <= 0f)
        {
            Deaths++;
            DownByHealth = true;
            downAt = Time.time;
            // Halattaysa önce çözül (ragdoll halatta kalamaz); sonra mevcut düşme akışı elindekini düşürür
            if (player.Rope != null) { var r = player.Rope; r.Detach(player, player.transform.position, player.transform.rotation); }
            player.KnockDown(away * 2.5f + Vector3.up * 1f);
            DownedByHealth?.Invoke(this);
            return true;
        }
        // HitReact + kısa sendeleme
        if (crewAnimation != null) crewAnimation.PlayAction(CrewAnimation.StateHitReact, 0.5f);
        float stagger = rules != null ? rules.staggerSeconds : 0.35f;
        float dist = rules != null ? rules.staggerDistance : 0.25f;
        player.KnockAside(away * dist, stagger);
        return true;
    }

    private void OnRevived(PlayerKnockdown k)
    {
        if (!DownByHealth) return; // araç çarpması: can değişmez
        DownByHealth = false;
        Current = Mathf.Max(Current, Max * (rules != null ? rules.reviveFraction : 0.5f));
        LastDamageTime = Time.time; // kalkınca dolma yine 6 sn bekler
        RevivesWithHealth++;
    }

    private void Update()
    {
        if (player == null) return;
        if (DownByHealth)
        {
            if (!player.IsDown) { DownByHealth = false; return; } // başka yolla kalktı (ör. boşluk yeniden doğması)
            if (Time.time - downAt >= RespawnSeconds) Respawn();
            return;
        }
        if (player.IsDown) return;
        float delay = rules != null ? rules.regenDelay : 6f;
        float rate = rules != null ? rules.regenPerSecond : 5f;
        if (Current < Max && Time.time - LastDamageTime >= delay)
            Current = Mathf.Min(Max, Current + rate * Time.deltaTime);
    }

    // Çekicinin yanında tam canla doğar (mevcut RespawnAt: elindekini bırakır, ayağa kalkar, ışınlanır).
    public void Respawn()
    {
        DownByHealth = false;
        Current = Max;
        LastDamageTime = -100f;
        Respawns++;
        Vector3 at = respawnPoint != null ? respawnPoint.position : player.transform.position;
        Quaternion rot = respawnPoint != null ? respawnPoint.rotation : player.transform.rotation;
        if (GroundProbe.Find(at + Vector3.up * 2f, 10f, out RaycastHit hit)) at = hit.point + Vector3.up * 0.02f;
        player.RespawnAt(at, rot);
        Respawned?.Invoke(this);
    }

    // Test: canı doğrudan ayarlar.
    public void SetHealthForTest(float value)
    {
        Current = Mathf.Clamp(value, 0f, Max);
    }
}
