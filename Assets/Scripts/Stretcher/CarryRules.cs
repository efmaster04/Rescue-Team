using UnityEngine;

// Taşıma kuralları (kullanıcı kararı 8). Gereken taşıyıcı sayısı koda gömülmez:
// oyun 2–3 kişiyle oynanabilir, mekanik kişi sayısına göre değişebilir.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Taşıma Kuralları", fileName = "CarryRules")]
public class CarryRules : ScriptableObject
{
    [Tooltip("Sedyenin kalkması için gereken taşıyıcı sayısı.")]
    [Min(1)] public int requiredCarriers = 2;

    [Tooltip("Boş sedyenin kalkması için gereken taşıyıcı sayısı.")]
    [Min(1)] public int emptyRequiredCarriers = 1;

    [Tooltip("Bütün taşıyıcılar bu süre (sn) içinde birlikte bırakırsa sedye güvenle yere konur, hasta düşmez.")]
    [Min(0f)] public float safeReleaseWindow = 0.5f;

    [Tooltip("Taşırken el (sap) yüksekliği, taşıyıcının ayak hizasından (m).")]
    [Min(0.2f)] public float handHeight = 0.72f;

    [Tooltip("El noktasının taşıyıcının gövdesinden sedyeye doğru uzaklığı (m).")]
    [Min(0f)] public float handReach = 0.3f;

    // ---- Taşıma sınıfları (kullanıcı isteği 2026-11-05: eşya boyutuna göre poz, ağırlığına göre hız) ----
    // Sınıf = ağırlık + "çevre" (eşyanın en büyük iki boyutundan küçüğü; uzun ama ince koni tek elde kalır).
    //  Hafif: ≤ lightMaxKg ve çevre ≤ lightMaxGirth → tek el (oyuncunun kendi yürüme/koşu hızı, koni hissi).
    //  Ağır:  > heavyMinKg veya çevre > heavyMinGirth → kucakta (heavyWalkSpeed; koşu, zıplama, eğilme yok).
    //  Orta:  arası → iki el göğüs önünde (mediumWalk/RunSpeed). Kangal da orta hızlarla taşınır (kendi klibi).
    // Kimliğe göre istisna classOverrides'ta (EquipmentDefinition.id, ör. "toolbox"). Değerler Inspector'dan değiştirilebilir.
    [Header("Taşıma sınıfları")]
    [Tooltip("Hafif sınıfın üst ağırlık sınırı (kg).")]
    public float lightMaxKg = 5f;
    [Tooltip("Hafif sınıfın üst çevre sınırı (m): en büyük iki boyutun küçüğü.")]
    public float lightMaxGirth = 0.45f;
    [Tooltip("Bu ağırlığın (kg) üstü ağır.")]
    public float heavyMinKg = 15f;
    [Tooltip("Bu çevrenin (m) üstü ağır.")]
    public float heavyMinGirth = 0.6f;
    [Tooltip("Orta sınıf yürüme / koşu hızı (m/sn). Klipler Carry_TwoHand_Walk 2.0, _Run 2.6 için yazıldı; farklıysa oynatma hızı ölçeklenir.")]
    public float mediumWalkSpeed = 2.0f;
    public float mediumRunSpeed = 2.6f;
    [Tooltip("Ağır sınıf yürüme hızı (m/sn; Carry_Heavy_Walk 1.4). Koşu yok.")]
    public float heavyWalkSpeed = 1.4f;
    [Tooltip("Kimliğe göre sınıf istisnası (EquipmentDefinition.id ya da eşya adı).")]
    public ClassOverride[] classOverrides = new ClassOverride[0];

    [System.Serializable]
    public class ClassOverride { public string id; public CarryClass carryClass = CarryClass.Light; }

    private static CarryRules fallback;
    // Sahnede kural asset'i bağlı değilse (test rigleri) varsayılan değerler.
    public static CarryRules Fallback { get { if (fallback == null) fallback = CreateInstance<CarryRules>(); return fallback; } }

    // Eşyanın sınıfı: önce kimlik istisnası, sonra ağırlık ve çevre.
    public CarryClass ClassFor(string id, float weightKg, Vector3 size)
    {
        if (!string.IsNullOrEmpty(id) && classOverrides != null)
            foreach (var o in classOverrides) if (o != null && o.id == id && o.carryClass != CarryClass.None) return o.carryClass;
        float a = Mathf.Abs(size.x), b = Mathf.Abs(size.y), c = Mathf.Abs(size.z);
        float largest = Mathf.Max(a, Mathf.Max(b, c)), smallest = Mathf.Min(a, Mathf.Min(b, c));
        float girth = a + b + c - largest - smallest; // ortanca boyut
        if (weightKg > heavyMinKg || girth > heavyMinGirth) return CarryClass.Heavy;
        if (weightKg > lightMaxKg || girth > lightMaxGirth) return CarryClass.Medium;
        return CarryClass.Light;
    }

    public bool CanRun(CarryClass c) { return c != CarryClass.Heavy; }
    public bool CanJump(CarryClass c) { return c != CarryClass.Heavy; }
    public bool CanCrouch(CarryClass c) { return c != CarryClass.Heavy; }

    // Sınıfın hızı; hafifte oyuncunun kendi yürüme / koşu hızı (lightWalk / lightRun) geçerlidir.
    public float Speed(CarryClass c, bool run, float lightWalk, float lightRun)
    {
        switch (c)
        {
            case CarryClass.Medium: return run ? mediumRunSpeed : mediumWalkSpeed;
            case CarryClass.Heavy: return heavyWalkSpeed;
            default: return run ? lightRun : lightWalk;
        }
    }

    // Taşıyıcı sayısı yetersizken gösterilen uyarı (sayıya göre metin).
    public string NotEnoughCarriersMessage
    {
        get { return NotEnoughCarriersMessageFor(requiredCarriers); }
    }

    public string NotEnoughCarriersMessageFor(int count)
    {
        switch (count)
        {
            case 2: return "İki kişi gerekli";
            case 3: return "Üç kişi gerekli";
            case 4: return "Dört kişi gerekli";
            default: return count + " kişi gerekli";
        }
    }
}
