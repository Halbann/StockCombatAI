using Expansions.Serenity;
using UnityEngine;
using static Expansions.Serenity.ModuleRoboticController;

namespace KerbalCombatSystems
{
    public class ModuleCombatRobotics : PartModule
    {
        public enum Variant
        {
            Basic,
            Ship,
            Weapon
        }

        [KSPField(isPersistant = true)]
        public string roboticsType; // Basic, Ship, Weapon

        private ModuleRoboticController module;
        private int users = 0;

        public float Duration => module.SequenceLength;
        public string Tag => module.displayName;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            module = part.FindModuleImplementing<ModuleRoboticController>();
        }

        public void Set(bool extend)
        {
            users = extend ? users + 1 : Mathf.Max(users - 1);
            if (users > 1 || !extend && users == 1) return;

            module.SetLoopMode(SequenceLoopOptions.Once);
            module.SetDirection(SequenceDirectionOptions.Forward);
            module.ToggleControllerEnabled(true);

            // Reverse the direction prior to playing if we're retracting.
            if (!extend)
                module.ToggleDirection();

            module.SequencePlay();
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
