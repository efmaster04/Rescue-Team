using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    public sealed class ModuleStation : InteractableBehaviour
    {
        public ServiceVehicle vehicle;
        public Transform storageStand;
        public ComputerUI ui;
        public bool Busy { get; set; }
        public override string Prompt(PlayerInteractor player) => Busy ? "Modül değişimi sürüyor" : "E — Modül istasyonunu kullan";
        public override void Interact(PlayerInteractor player) { if (!Busy) ui.Open("FleetManager"); }
    }
}
