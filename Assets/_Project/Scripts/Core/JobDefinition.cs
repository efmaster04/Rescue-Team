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
        // Sözleşmenin saha süresi sınırı (dakika). 0 = süre koşulu yok.
        public float contractMaxFieldMinutes = 0;
        public string requiredModule;
        public string[] requiredEquipment = Array.Empty<string>();
        public string[] recommendedEquipment = Array.Empty<string>();
        // Görev köprüsü: işin saha sahnesi (Build Settings'teki sahne adı, ör. "Bolum1_Ucurum").
        // Boşsa iş listede "Yakında" olarak kilitli görünür ve kabul edilemez.
        [Tooltip("Saha görevi sahnesinin adı. Boşsa iş kilitli (Yakında).")]
        public string fieldScene = "";
        public bool HasFieldScene => !string.IsNullOrEmpty(fieldScene);
    }
}
