using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    public sealed class DispatchBoard : InteractableBehaviour
    {
        public ComputerUI ui;
        public override string Prompt(PlayerInteractor player) => "E — Hazırlık ve çıkış kontrolü";
        public override void Interact(PlayerInteractor player) => ui.Open("Dispatch");
    }
}
