using System;
using UnityEngine;

namespace CompanySim
{
    [CreateAssetMenu(menuName = "Company/Job")]
    public sealed class JobDefinition : ScriptableObject
    {
        public string id, title, client, description, category, location, specialTerms;
        public float distanceKm;
        public int payment, difficulty, risk, durationMinutes;
        public int minimumReputation;
        public string contractTitle;
        public int contractBonus;
        public float contractMaxEquipmentWear = 10;
        public float contractMaxVehicleWear = 10;
        public string requiredModule;
        public string[] requiredEquipment = Array.Empty<string>();
        public string[] recommendedEquipment = Array.Empty<string>();
    }
}
