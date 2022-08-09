using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;
using Expansions.Serenity;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleCombatRobotics : PartModule
    {
        private ModuleRoboticController KAL;
        
        //access the sequence internals
        public ModuleRoboticController.SequenceDirectionOptions Forward { get; private set; }
        public ModuleRoboticController.SequenceLoopOptions Once { get; private set; }

        public virtual void Start()
        {
            KAL = part.FindModuleImplementing<ModuleRoboticController>();
            KAL.SetLength(1);
            KAL.SetLoopMode(Once);
            KAL.ToggleControllerEnabled(true);
            //reset the combat robotics on load
            PassiveTrigger();
        }

        //these buttons are just for debugging
        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Trigger Combat Mode")]
        public void CombatTrigger()
        {
            Debug.Log("[KCS]: Extending KAL-500 Position");
            //trigger robotics to end of sequence(trigger Combat)
            KAL.SetDirection(Forward);
            KAL.SequencePlay();
        }

        //these buttons are just for debugging
        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Trigger Passive Mode")]
        public void PassiveTrigger()
        {
            Debug.Log("[KCS]: Resetting KAL-500 Position");
            //trigger robotics to start of sequence(trigger Passive)
            KAL.SetDirection(Forward);
            //Reverse doens't work as advertised so setting as forward then reversing
            KAL.ToggleDirection();
            KAL.SequencePlay();
        }

    }
}
