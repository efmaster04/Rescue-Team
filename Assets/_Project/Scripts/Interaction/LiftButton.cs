namespace CompanySim
{
    public sealed class LiftButton : InteractableBehaviour
    {
        public GarageLift lift;
        public int direction=1;
        public override string Prompt(PlayerInteractor player)
        {
            if(lift.IsBlocked) return "E — Lifti durdur · "+lift.BlockingReason;
            if(lift.IsMoving) return "E — Lifti durdur";
            if(!lift.CanStart(direction,out var reason)) return reason;
            return direction>0?"E — Lifti yükselt":"E — Lifti indir";
        }
        public override void Interact(PlayerInteractor player)
        { if(lift.IsMoving) lift.Stop(); else lift.RequestMotion(direction,player); }
    }
}
