using UnityEngine;

namespace CompanySim
{
    [CreateAssetMenu(menuName = "Company/Economy")]
    public sealed class EconomyConfig : ScriptableObject
    {
        public int startingBalance = 2500;
        public int startingSpareParts = 3;
        public int sparePartsPackPrice = 85;
        public int sparePartsPerPack = 2;
        public int equipmentRepairCost = 45;
        public int equipmentRepairParts = 1;
        public int vehicleRepairCost = 140;
        public int vehicleRepairParts = 2;
        public float minimumVehicleCondition = 30;
        public int inspectionReward = 70;
        public int inspectionFine = 40;
        public float inspectionMinimumCondition = 70;
        public int storageUpgradePrice = 450;
        public int lightingUpgradePrice = 320;
        public int operationsUpgradePrice = 600;

        public int UpgradePrice(string id) => id == "storage" ? storageUpgradePrice :
            id == "lighting" ? lightingUpgradePrice :
            id == "operations" ? operationsUpgradePrice : -1;
    }
}
