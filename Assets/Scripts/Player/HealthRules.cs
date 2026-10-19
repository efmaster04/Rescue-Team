using UnityEngine;

// U3 (kullanıcı kararı "Can ve para", 2026-10-19): oyuncu canı kuralları. Sayılar koda gömülmez, buradan ayarlanır.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Can Kuralları", fileName = "HealthRules")]
public class HealthRules : ScriptableObject
{
    [Tooltip("En yüksek can.")]
    [Min(1f)] public float maxHealth = 100f;
    [Tooltip("Arkadaş/bot kaldırınca can bu oranda gelir (0.5 = %50).")]
    [Range(0.05f, 1f)] public float reviveFraction = 0.5f;
    [Tooltip("Can 0'la yere düşen oyuncu bu kadar sn içinde kaldırılmazsa yeniden doğar.")]
    [Min(1f)] public float respawnSeconds = 15f;
    [Tooltip("Son hasardan bu kadar sn sonra can dolmaya başlar.")]
    [Min(0f)] public float regenDelay = 6f;
    [Tooltip("Dolma hızı (can/sn).")]
    [Min(0f)] public float regenPerSecond = 5f;
    [Tooltip("Her darbede kontrol kaybı (sn) — kısa sendeleme (HitReact klibi 0.5 sn).")]
    [Min(0f)] public float staggerSeconds = 0.35f;
    [Tooltip("Darbede geri itilme (m).")]
    [Min(0f)] public float staggerDistance = 0.25f;
}
