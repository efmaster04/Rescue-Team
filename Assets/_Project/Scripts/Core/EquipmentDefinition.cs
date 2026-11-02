using System;
using UnityEngine;

namespace CompanySim
{
    [CreateAssetMenu(menuName = "Company/Equipment")]
    public sealed class EquipmentDefinition : ScriptableObject
    {
        public string id, displayName, category;
        public float weightKg = 5;
        public Vector3 size = new Vector3(.4f, .3f, .4f);
        public int price = 60;
        public bool portable = true;
        public GameObject visualPrefab;
    }
}
