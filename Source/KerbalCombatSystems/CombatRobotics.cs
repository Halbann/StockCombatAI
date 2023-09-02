using Expansions.Serenity;
using static Expansions.Serenity.ModuleRoboticController;

namespace KerbalCombatSystems
{
    class ModuleCombatRobotics : PartModule
    {
        [KSPField(isPersistant = true)]
        public string roboticsType; //Basic, Ship, Weapon

        private ModuleRoboticController KAL;

        public float SequenceLength => KAL.SequenceLength;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            KAL = part.FindModuleImplementing<ModuleRoboticController>();
        }

        //KCS KALs only have two states
        public void KALTrigger(bool extend)
        {
            KAL.SetLoopMode(SequenceLoopOptions.Once);
            KAL.ToggleControllerEnabled(true);
            KAL.SetDirection(SequenceDirectionOptions.Forward);

            // Reverse the direction prior to playing if we're retracting.
            if (!extend)
                KAL.ToggleDirection();

            KAL.SequencePlay();
        }

        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            roboticsType = variant.Name;
        }

        internal void OnDestroy()
        {
            GameEvents.onEditorVariantApplied.Remove(OnVariantApplied);
        }
    }
}
