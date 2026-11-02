using System;
using UnityEngine;

namespace CompanySim
{
    public sealed class CompanyCatalog : MonoBehaviour
    {
        public EconomyConfig economy;
        public JobDefinition[] jobs;
        public ModuleDefinition[] modules;
        public EquipmentDefinition[] equipment;

        public JobDefinition Job(string id) => Array.Find(jobs, x => x && x.id == id);
        public ModuleDefinition Module(string id) => Array.Find(modules, x => x && x.id == id);
        public EquipmentDefinition Item(string id) => Array.Find(equipment, x => x && x.id == id);
    }
}
