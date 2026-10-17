using UnityEngine;

// Ambulans takvimi (kullanıcı kararı 1). Bütün süreler görev başlangıcından itibaren saniye.
// Zamanlama DURAĞA VARIŞ anına göredir: spawn zamanı = varış zamanı − şeritteki yolculuk süresi.
[CreateAssetMenu(menuName = "Rescue Team/Bölüm 1/Ambulans Takvimi", fileName = "AmbulanceSchedule")]
public class AmbulanceSchedule : ScriptableObject
{
    [Tooltip("İki varış arasındaki süre (sn).")]
    [Min(1f)] public float interval = 120f;

    [Tooltip("Durakta bekleme süresi (sn).")]
    [Min(2f)] public float wait = 15f;

    [Tooltip("İlk varış zamanı (sn).")]
    [Min(0f)] public float firstArrival = 120f;

    // k. varışın (0'dan başlar) planlanan zamanı
    public float ArrivalTime(int k)
    {
        return firstArrival + k * interval;
    }
}
