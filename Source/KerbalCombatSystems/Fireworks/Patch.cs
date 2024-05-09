using System.Linq;

using UnityEngine;
using HarmonyLib;
using KSP.FX.Fireworks;
using Object = UnityEngine.Object;

// https://harmony.pardeike.net/articles/annotations.html

namespace KerbalCombatSystems.Fireworks
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class Patcher : MonoBehaviour
    {
        protected void Start()
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
    class LaunchShellPatch
    {
        // Insert harmony patches around LaunchShell to fix the physics, and optionally add effects.

        public static bool replaceShell = true;
        static EffectsOption effectsOption = EffectsOption.Always;

        internal static bool InAtmosphere =>
            FlightGlobals.ActiveVessel.orbit.referenceBody.atmosphere
            && FlightGlobals.ActiveVessel.altitude < FlightGlobals.ActiveVessel.orbit.referenceBody.atmosphereDepth;

        static bool UseEffects =>
            effectsOption == EffectsOption.Always || effectsOption == EffectsOption.Vacuum && !InAtmosphere;

        static bool Prefix(ModulePartFirework __instance)
        {
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
            GameObject shell = FlightGlobals.physicalObjects.Last().gameObject;

            if (!replaceShell)
            {
                Fixer.CorrectStockShell(shell, __instance);
                __instance.variationOnShellDirection = true;
            }

            if (UseEffects)
            {
                // Remove old fx.
                Object.Destroy(shell.GetComponent<FireworkFX>());
                Object.Destroy(shell.GetComponent<AudioSource>());

                // Add custom fx.
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
