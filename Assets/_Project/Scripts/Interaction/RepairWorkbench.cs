using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    public sealed class RepairWorkbench : InteractableBehaviour
    {
        public CompanyRuntime runtime;
        public override string Prompt(PlayerInteractor player) => player.HeldItem
            ? "E — Elde tutulan ekipmanı tamir et" : "E — Servis vanına bakım yap";
        public override void Interact(PlayerInteractor player)
        {
            if (player.HeldItem) runtime.Repair(player.HeldItem);
            else runtime.RepairVehicle();
        }
    }
}
