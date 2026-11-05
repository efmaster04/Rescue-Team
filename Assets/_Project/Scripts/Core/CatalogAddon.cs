using System;
using UnityEngine;

namespace CompanySim
{
    // Görev köprüsü: sahneye dokunmadan kataloğa eklenen tanımlar (ör. halat, sedye, koni; Sel Vadisi işi).
    // Resources/CompanyCatalogAddon.asset olarak durur; CompanyCatalog.Awake bunu kendi listesine katar.
    [CreateAssetMenu(menuName = "Company/Catalog Addon")]
    public sealed class CatalogAddon : ScriptableObject
    {
        public const string ResourcePath = "CompanyCatalogAddon";
        public EquipmentDefinition[] equipment = Array.Empty<EquipmentDefinition>();
        public JobDefinition[] jobs = Array.Empty<JobDefinition>();
    }
}
