using System;
using UnityEngine;

namespace CompanySim
{
    [CreateAssetMenu(menuName = "Company/Module")]
    public sealed class ModuleDefinition : ScriptableObject
    {
        public string id, displayName, category, connections;
        public float weightKg, payloadKg = 300;
        public int cargoSlots = 4, price = 1000;
        public bool sellable = true;
        public GameObject visualPrefab;
    }
}
