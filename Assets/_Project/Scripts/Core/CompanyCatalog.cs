using System;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    [DefaultExecutionOrder(-1000)]
    public sealed class CompanyCatalog : MonoBehaviour
    {
        public EconomyConfig economy;
        public JobDefinition[] jobs;
        public ModuleDefinition[] modules;
        public EquipmentDefinition[] equipment;

        // Görev köprüsü: sahnedeki katalogda olmayan ek ekipman tanımları kayıt yüklenmeden önce eklenir.
        void Awake() => MergeAddons();

        public void MergeAddons()
        {
            var addon = Resources.Load<CatalogAddon>(CatalogAddon.ResourcePath);
            if (!addon || addon.equipment == null) return;
            var list = (equipment ?? Array.Empty<EquipmentDefinition>()).ToList();
            foreach (var def in addon.equipment)
                if (def && !list.Any(x => x && x.id == def.id)) list.Add(def);
            equipment = list.ToArray();
        }

        public JobDefinition Job(string id) => Array.Find(jobs, x => x && x.id == id);
        public ModuleDefinition Module(string id) => Array.Find(modules, x => x && x.id == id);
        public EquipmentDefinition Item(string id) => Array.Find(equipment, x => x && x.id == id);
    }
}
