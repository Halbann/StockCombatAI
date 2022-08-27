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
        
        [KSPField(isPersistant = true)]
        public string RoboticsType;
        public static string[] Types = { "Generic", "Combat Robotics", "Turret Controller", "Weapons Robotics" };

        //access the sequence internals
        public ModuleRoboticController.SequenceDirectionOptions Forward { get; private set; }
        public ModuleRoboticController.SequenceLoopOptions Once { get; private set; }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            if (Types.IndexOf(RoboticsType) == -1)
                RoboticsType = part.variants.SelectedVariant.Name;

            KAL = part.FindModuleImplementing<ModuleRoboticController>();

            string Name = KAL.GetModuleDisplayName();
            Debug.Log("KAL Name" + Name);

        }

        public void CombatTrigger()
        {
            KAL.SetLoopMode(Once);
            KAL.ToggleControllerEnabled(true);
            Debug.Log("[KCS]: Extending KAL-500 Position");
            //trigger robotics to end of sequence(trigger Combat)
            KAL.SetDirection(Forward);
            KAL.SequencePlay();
        }

        public void PassiveTrigger()
        {
            KAL.SetLoopMode(Once);
            KAL.ToggleControllerEnabled(true);
            Debug.Log("[KCS]: Resetting KAL-500 Position");
            //trigger robotics to start of sequence(trigger Passive)
            KAL.SetDirection(Forward);
            //Reverse doesn't work as advertised so setting as forward then reversing
            KAL.ToggleDirection();
            KAL.SequencePlay();
        }

        public void WeaponsTrigger()
        {

        }
            

        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            RoboticsType = variant.Name;
        }

        private void OnDestroy()
        {
            GameEvents.onEditorVariantApplied.Remove(OnVariantApplied);
        }
    }
}
