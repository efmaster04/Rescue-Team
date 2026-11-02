namespace CompanySim
{
    public sealed class ModuleMountPoint : InteractableBehaviour
    {
        public ManualModuleSystem system;
        public override string Prompt(PlayerInteractor player) => system.MountPrompt(player);
        public override void Interact(PlayerInteractor player)
        {
            if(system.Current) system.TakeMounted(player);
            else if(player.HeldItem) system.Stage(player.HeldItem.GetComponent<ManualModulePackage>(),player);
        }
    }
}
