using UnityEngine;
namespace RescueTeam.Bolum1
{
    [CreateAssetMenu(menuName = "Rescue Team/Ambulans takvimi")]
    public sealed class AmbulanceSchedule : ScriptableObject
    {
        [Min(1)] public float interval = 180;
        [Min(2)] public float wait = 15;
        [Min(1)] public float firstArrival = 180;
        [Min(1)] public float cruiseSpeed = 12;
    }
}
