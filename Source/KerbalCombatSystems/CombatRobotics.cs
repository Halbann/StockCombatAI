using Expansions.Serenity;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleCombatRobotics : PartModule
    {
        private ModuleRoboticController KAL;

        [KSPField(isPersistant = true)]
        public string RoboticsType; //Basic, Ship, Weapon

        //access the sequence internals
        public ModuleRoboticController.SequenceDirectionOptions Forward { get; private set; }
        public ModuleRoboticController.SequenceLoopOptions Once { get; private set; }
        public int SequenceLength { get; internal set; }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            KAL = part.FindModuleImplementing<ModuleRoboticController>();

            string Name = KAL.GetModuleDisplayName();
            Debug.Log("KAL Name" + Name);
        }

        //KCS KALs only have two states
        public void KALTrigger(bool Extend)
        {
            KAL.SetLoopMode(Once);
            KAL.ToggleControllerEnabled(true);
            KAL.SetDirection(Forward);

            if (Extend)
            {
                //send robotics to end of sequence(trigger Combat)
                Debug.Log("[KCS]: Extending KAL Position");
            }
            else
            {
                //send robotics to start of sequence(trigger Passive)
                Debug.Log("[KCS]: Resetting KAL Position");
                //Reverse doesn't work as advertised so setting as forward then reversing
                KAL.ToggleDirection();
            }

            KAL.SequencePlay();
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
