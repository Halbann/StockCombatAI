using System.Linq;

using UnityEngine;
using HarmonyLib;

using KerbalCombatSystems.Data;

// https://harmony.pardeike.net/articles/annotations.html

namespace KerbalCombatSystems.Fireworks
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class Patcher : MonoBehaviour
    {
        public void Start()
        {
            var harmony = new Harmony("KCS");
            harmony.PatchAll();
        }
    }

    public enum EffectsOption
    {
        Never,
        Vacuum,
        Always
    }

    [HarmonyPatch(typeof(ModulePartFirework))]
    [HarmonyPatch(nameof(ModulePartFirework.LaunchShell))]
    [Settings(category = "Fireworks", displayName = "Fireworks")]
    class LaunchShell
    {
        // Insert harmony patches around LaunchShell to fix the physics, and optionally add effects.

        // Most belong in something that is part-specific. 
        [Setting] public static EffectsOption effectsOption = EffectsOption.Always;
        public static bool applyHeat = true;
        public static bool replaceShell = false;
        public static float shotHeat = 20f;

        internal static bool InAtmosphere =>
            FlightGlobals.ActiveVessel.orbit.referenceBody.atmosphere
            && FlightGlobals.ActiveVessel.altitude < FlightGlobals.ActiveVessel.orbit.referenceBody.atmosphereDepth;

        static bool UseEffects =>
            effectsOption == EffectsOption.Always || effectsOption == EffectsOption.Vacuum && !InAtmosphere;

        private static int physicalsCount = 0;

        static bool Prefix(ModulePartFirework __instance)
        {
            // Before LaunchShell.

            physicalsCount = FlightGlobals.physicalObjects.Count;

            if (replaceShell)
            {
                Replacement.ReplaceStockShell(__instance);
                return false;
            }

            __instance.variationOnShellDirection = false;
            return true;
        }

        static void Postfix(ModulePartFirework __instance)
        {
            // After LaunchShell.

            if (FlightGlobals.physicalObjects.Count == physicalsCount)
                return;

            ModulePartFirework launcher = __instance;
            GameObject shell = FlightGlobals.physicalObjects.Last().gameObject;

            if (!replaceShell)
            {
                Fixer.CorrectStockShell(shell, launcher);
                launcher.variationOnShellDirection = true;
            }

            shell.AddComponent<Fixer>();

            if (applyHeat)
                launcher.part.temperature += shotHeat;

            launcher.GetComponent<ModuleStockLauncherAnimation>()?.Spin();
            launcher.GetComponent<ModuleLauncherReload>()?.OnLauncherFired();

            if (UseEffects)
            {
                ShellEffects.RemoveStockEffects(shell);
                ShellEffects.AddEffects(shell, launcher);

                launcher.GetComponent<ModuleMuzzleFlash>()?.MuzzleEffect();
            }
        }
    }

    // todo: move to a separate file, along with patcher addon.
    [HarmonyPatch(typeof(ModuleDecouple))]
    [HarmonyPatch(nameof(ModuleDecouple.OnDecouple))]
    class DecouplerFXFix
    {
        public static bool enableFix = true;

        static void Prefix(ModuleDecouple __instance)
        {
            var gameObject = __instance.part.gameObject;
            foreach (var ps in gameObject.GetComponentsInChildren<ParticleSystem>())
            {
                if (!ps.name.ToLower().Contains("gasburst"))
                    continue;

                var main = ps.main;
                var space = enableFix ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
                main.simulationSpace = space;
            }
        }
    }

    // Ongoing issue:
    // - Shells have phantom motion when drift is occuring.
    // - It happens even when the shell velocity is the same as the vessel velocity.
    // - It doesn't happen when drift correction is disabled (pretty sure).

    /*[HarmonyPatch(typeof(FlightIntegrator))]
    [HarmonyPatch("IntegratePhysicalObjects")]
    class FixPhysicalObjectIntegration
    {
        static void Prefix(FlightIntegrator __instance)
        {
            var activePrecalc = __instance.Vessel.precalc;
            Vector3d v1 = activePrecalc.preIntegrationVelocityOffset;

            if (v1 != Vector3d.zero)
            {
                Debug.Log($"{Time.fixedTime} Applying pre-int: {v1:N2} ");

                foreach (var phys in FlightGlobals.physicalObjects)
                {
                    phys.rb.velocity -= v1;
                }
            }
        }

        static void Postfix(FlightIntegrator __instance)
        {
            var activePrecalc = __instance.Vessel.precalc;
            Vector3d v2 = activePrecalc.postIntegrationVelocityCorrection;

            if (v2 != Vector3d.zero)
            {
                Debug.Log($"{Time.fixedTime} Applying post-int: {v2:N2} ");

                foreach (var phys in FlightGlobals.physicalObjects)
                {
                    phys.rb.velocity += v2;
                }
            }
        }
    }*/
}
