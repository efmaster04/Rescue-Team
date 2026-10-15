using UnityEngine;
namespace RescueTeam.Bolum1
{
    [CreateAssetMenu(menuName = "Rescue Team/Taşıma kuralları")]
    public sealed class CarryRules : ScriptableObject
    {
        [Min(1)] public int requiredCarriers = 2;
        [Min(0)] public float safeReleaseWindow = 0.5f;
        [Min(0.2f)] public float handHeight = 0.85f;
        public string RequiredMessage => requiredCarriers == 2 ? "İki kişi gerekli" : requiredCarriers + " kişi gerekli";
    }
}
