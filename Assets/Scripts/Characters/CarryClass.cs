using UnityEngine;

// Taşıma sınıfı (kullanıcı isteği 2026-11-05: eşya boyutuna göre poz, ağırlığına göre hız farkı).
// Sınıf, eşyanın ağırlık ve boyutundan CarryRules.ClassFor ile türetilir; kimliğe göre istisna CarryRules.classOverrides'ta.
//  - Light  (hafif): tek el, eşya sağ avuca asılı. Hız oyuncunun kendi yürüme / koşusu (koni hissi korunur: 2.4 / 3.2).
//  - Medium (orta):  iki el göğüs önünde, eşya avuçların üstünde gövdeye dayalı. CarryRules medium* hızları (2.0 / 2.6).
//  - Heavy  (ağır):  kucakta, dizler kırık. CarryRules heavyWalkSpeed (1.4); koşu, zıplama ve eğilme yok.
// Animator Carry parametresi: Light 2 (CarryCone), Medium 6 (CarryTwoHand), Heavy 7 (CarryHeavy); kangal 5 ayrı kalır.
public enum CarryClass
{
    None = 0,
    Light = 1,
    Medium = 2,
    Heavy = 3,
}

// Oyuncunun tek başına taşıdığı eşya (koni, değerli eşya, ofis-garaj eşyası). CarryHandIK avuçları eşyaya oturtur ve eşyayı
// hesaplanmış bir noktaya değil AVUÇLARA bağlar: eşyanın merkezi her kare PlaceHeld ile verilir.
public interface ICarriedProp
{
    CarryClass CarryClass { get; }
    // Tutulurken eşyanın boyutu (x genişlik: iki elin arası, y yükseklik: dünya yukarı, z derinlik: taşıyana doğru), m.
    Vector3 HeldSize { get; }
    // Eşyanın sınır kutusunun merkezini verilen noktaya, dönüşünü verilen dönüşe koyar (yalnız görsel; collider'lar elde kapalı).
    void PlaceHeld(Vector3 center, Quaternion rotation);
}
