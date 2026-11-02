using UnityEngine;

namespace CompanySim
{
    [RequireComponent(typeof(CarryableItem))]
    public sealed class ManualModulePackage : MonoBehaviour
    {
        public ManualModuleSystem system;
        public ModulePackageRecord record;
        public CarryableItem Item { get; private set; }
        public string DisplayName => record!=null && system?system.runtime.catalog.Module(record.moduleId)?.displayName??record.moduleId:"Servis modülü";
        public bool Secured => record!=null && record.mounted && record.bolts==15 && record.connected;
        void Awake() { Item=GetComponent<CarryableItem>(); }
        public string Prompt(PlayerInteractor player)
        {
            if(record==null || !system) return "";
            if(record.mounted) return system.MountPrompt(player);
            return player.HeldItem?"":"E — "+DisplayName+" al";
        }
        public void Interact(PlayerInteractor player)
        {
            if(record.mounted) system.TakeMounted(player);
            else if(!player.HeldItem) player.Hold(Item);
        }
    }
}
