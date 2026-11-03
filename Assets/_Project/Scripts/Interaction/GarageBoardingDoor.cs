namespace CompanySim
{
    public sealed class GarageBoardingDoor : InteractableBehaviour
    {
        public CompanyRuntime runtime;

        public override string Prompt(PlayerInteractor player)
        {
            if (!runtime || runtime.Data.phase == JobPhase.None)
                return "Önce bilgisayardan bir iş kabul edin";
            if (runtime.Data.phase != JobPhase.Accepted)
                return "Aktif görevi bilgisayardan takip edin";
            var missing = runtime.MissingPreparation();
            return missing.Count == 0 ? "E — Minibüse bin (göreve çık)" : "Eksik: " + string.Join(", ", missing);
        }

        public override void Interact(PlayerInteractor player)
        {
            if (!runtime) return;
            if (runtime.Data.phase == JobPhase.Accepted) runtime.Depart();
            else runtime.Notify(Prompt(player));
        }
    }
}
