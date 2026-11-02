using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    public sealed class OfficeComputer : InteractableBehaviour
    {
        public ComputerUI ui;
        public override string Prompt(PlayerInteractor player) => "E — Şirket bilgisayarını kullan";
        public override void Interact(PlayerInteractor player) => ui.Open("Desktop");
    }
}
