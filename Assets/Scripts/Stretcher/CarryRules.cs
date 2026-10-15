using UnityEngine;

// Taşıma kuralları (kullanıcı kararı 8). Gereken taşıyıcı sayısı koda gömülmez:
// oyun 2–3 kişiyle oynanabilir, mekanik kişi sayısına göre değişebilir.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Taşıma Kuralları", fileName = "CarryRules")]
public class CarryRules : ScriptableObject
{
    [Tooltip("Sedyenin kalkması için gereken taşıyıcı sayısı.")]
    [Min(1)] public int requiredCarriers = 2;

    [Tooltip("Bütün taşıyıcılar bu süre (sn) içinde birlikte bırakırsa sedye güvenle yere konur, hasta düşmez.")]
    [Min(0f)] public float safeReleaseWindow = 0.5f;

    [Tooltip("Taşırken el (sap) yüksekliği, taşıyıcının ayak hizasından (m).")]
    [Min(0.2f)] public float handHeight = 0.72f;

    [Tooltip("El noktasının taşıyıcının gövdesinden sedyeye doğru uzaklığı (m).")]
    [Min(0f)] public float handReach = 0.3f;

    // Taşıyıcı sayısı yetersizken gösterilen uyarı (sayıya göre metin).
    public string NotEnoughCarriersMessage
    {
        get
        {
            switch (requiredCarriers)
            {
                case 2: return "İki kişi gerekli";
                case 3: return "Üç kişi gerekli";
                case 4: return "Dört kişi gerekli";
                default: return requiredCarriers + " kişi gerekli";
            }
        }
    }
}
