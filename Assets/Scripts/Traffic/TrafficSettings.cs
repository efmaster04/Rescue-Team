using System;
using UnityEngine;

// Trafik araç tipleri (kullanıcı kararı 1). Sedan ve Hatchback "binek" sayılır.
public enum TrafficVehicleType { Sedan, Hatchback, Pickup, PanelVan, BoxTruck }

// Trafik ayarları (kullanıcı kararları 2–5). Bütün tempo, olasılık ve hız değerleri burada; koda gömülmez.
// Testler bu asset'i değiştirmez, çalışma zamanında kopyasını (Instantiate) kullanır.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Trafik Ayarları", fileName = "TrafficSettings")]
public class TrafficSettings : ScriptableObject
{
    [Serializable]
    public class TypeEntry
    {
        public TrafficVehicleType type;
        [Tooltip("Seçilme ağırlığı (göreli). Binek %55 = Sedan + Hatchback toplamı.")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("Seyir hızı (m/sn).")]
        [Min(1f)] public float cruiseSpeed = 12f;
    }

    [Header("Tempo (her yön bağımsız)")]
    [Tooltip("Sakin aralığın süresi (sn), bu aralıkta rastgele.")]
    public Vector2 calmSeconds = new Vector2(20f, 40f);
    [Tooltip("Sakin aralıkta tek bir aracın geçme olasılığı (en fazla bir araç).")]
    [Range(0f, 1f)] public float calmSingleVehicleChance = 0.5f;
    [Tooltip("Dalgadaki araç sayısı (en az, en çok; ikisi de dahil).")]
    public Vector2Int waveSize = new Vector2Int(3, 4);
    [Tooltip("Dalgada iki araç arası (sn).")]
    public Vector2 waveGapSeconds = new Vector2(2f, 4f);

    [Header("Araç tipleri")]
    public TypeEntry[] types =
    {
        new TypeEntry { type = TrafficVehicleType.Sedan, weight = 27.5f, cruiseSpeed = 13f },
        new TypeEntry { type = TrafficVehicleType.Hatchback, weight = 27.5f, cruiseSpeed = 13f },
        new TypeEntry { type = TrafficVehicleType.Pickup, weight = 15f, cruiseSpeed = 12f },
        new TypeEntry { type = TrafficVehicleType.PanelVan, weight = 15f, cruiseSpeed = 11.5f },
        new TypeEntry { type = TrafficVehicleType.BoxTruck, weight = 15f, cruiseSpeed = 10f },
    };

    [Header("Takip (kullanıcı kararı 3)")]
    [Tooltip("Öndeki araçla zaman aralığı (sn): '2 saniye kuralı'.")]
    [Min(0.1f)] public float timeHeadway = 2f;
    [Tooltip("Öndeki araçla en küçük boşluk (m), dururken de korunur.")]
    [Min(0.5f)] public float minGap = 4f;
    public float acceleration = 3f;
    public float braking = 6f;

    [Header("Koni (kullanıcı kararı 4)")]
    [Tooltip("Araç koninin önünde durduktan bu kadar sonra (sn) koniye çarpıp devam eder.")]
    [Min(0f)] public float coneWaitSeconds = 20f;
    [Tooltip("Ön tamponun koniden durma payı (m).")]
    [Min(0.2f)] public float coneStopMargin = 1.2f;
    [Tooltip("Eğimi bundan büyük koni devrik sayılır, şeridi kapatmaz (derece).")]
    public float coneUprightMaxAngle = 30f;

    [Header("Oyuncu önündeyken (kullanıcı kararı 5)")]
    [Tooltip("Ön tampondan ileriye bakılan mesafe (m).")]
    public float lookAhead = 14f;
    [Tooltip("Önde taşıyıcı varken hız, seyir hızının en fazla bu oranı (durmaz).")]
    [Range(0.05f, 1f)] public float yieldSpeedFactor = 0.4f;
    [Tooltip("Çarpılan oyuncuya / hastaya verilen itki: aracın hızına çarpan ve en az hız (m/sn).")]
    public float hitSpeedMultiplier = 1.2f;
    public float hitMinSpeed = 3f;
    [Tooltip("Düşürmek için hedefe doğru asgari araç hızı (m/sn).")]
    [Min(0f)] public float knockdownSpeedThreshold = 1f;
    public float hitUpSpeed = 1.5f;
    public float hitSideSpeed = 1.5f;

    [Header("Servis minibüsü şeridi kapatınca (kullanıcı kararı, 2026-11-04)")]
    [Tooltip("Minibüsün gerisinde durduktan bu kadar sonra (sn) araç minibüse çarpıp ilerler.")]
    [Min(0f)] public float vanWaitSeconds = 5f;
    [Tooltip("Ön tamponun minibüsten durma payı (m).")]
    [Min(0.5f)] public float vanStopMargin = 2.5f;
    [Tooltip("Minibüsün gerisinde beklerken iki korna arası (sn).")]
    [Min(0.5f)] public float vanHornInterval = 2f;
    [Tooltip("Çarparken en yüksek hız (m/sn).")]
    [Min(1f)] public float vanRamSpeed = 4f;
    [Tooltip("Çarpmanın minibüse verdiği hasar (%).")]
    [Min(0f)] public float vanRamDamage = 12f;
    [Tooltip("Çarpmanın minibüse verdiği itme hızı (m/sn): aracın gidiş yönünde + şeridin dışına doğru.")]
    [Min(0f)] public float vanRamPushSpeed = 3f;
    [Tooltip("Çarpma en fazla bu kadar sürer (sn); sonra araç yeniden değerlendirir (hâlâ kapalıysa yine bekler).")]
    [Min(1f)] public float vanRamMaxSeconds = 12f;
    [Tooltip("Çarparken ön tampon minibüs kenarından bu kadar geride tutulur (m): kinematik araç minibüsü fizikle önüne katmaz, itme ILaneObstacle.Ram'dan gelir.")]
    [Min(0.05f)] public float vanRamContactMargin = 0.3f;

    [Header("Ambulans (kullanıcı kararı 8)")]
    [Tooltip("Ambulansın doğmasından bu kadar sn önce EastBound'a araç çıkmaz.")]
    public float ambulanceClearSeconds = 20f;

    [Header("Test")]
    [Tooltip("Açıkken her oyunda aynı sıra (deterministik test modu).")]
    public bool useFixedSeed = false;
    public int seed = 12345;

    public TypeEntry Entry(TrafficVehicleType t)
    {
        foreach (var e in types) if (e != null && e.type == t) return e;
        return null;
    }

    public static bool IsPassengerCar(TrafficVehicleType t) { return t == TrafficVehicleType.Sedan || t == TrafficVehicleType.Hatchback; }
}
