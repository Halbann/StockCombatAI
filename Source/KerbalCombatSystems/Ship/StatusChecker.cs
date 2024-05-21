using System;
using System.Collections;
using System.Reflection;

using UnityEngine;

namespace KerbalCombatSystems
{
    // todo: Don't like this pattern, how to work with components that need coroutines?
    internal class ShipComponent : MonoBehaviour
    {
        protected ModuleShipController ship;

        protected Vessel Vessel =>
            ship.vessel;

        internal static T Create<T>(ModuleShipController ship) where T : ShipComponent
        {
            var checker = ship.gameObject.AddComponent<T>();
            checker.ship = ship;

            return checker;
        }
    }

    internal class StatusChecker : ShipComponent
    {
        private ModuleRCS statusRCS;
        private ModuleEngines statusEngine;
        private ModuleWeaponController statusWeapon;
        private float lastInControl;
        private const float controlTimeout = 10;

        internal void Start()
        {
            StartCoroutine(Checker());

            // Delta V.
            StartCoroutine(DeltaVChecker());
        }

        #region Status

        private IEnumerator Checker()
        {
            // There are certain meta things we need to track even if the AI isn't running
            // so that other ships can still interact with it.
            // Alive status, heat signature, what weapons are incoming to us as a target, etc.

            var wait = new WaitForSeconds(ModuleShipController.combatUpdateInterval);

            while (true)
            {
                if (ship == null || Vessel == null)
                {
                    Destroy(this);
                    break;
                }

                bool wasAlive = ship.alive;
                CheckStatus();

                if (!ship.alive && wasAlive)
                {
                    ship.DeathMessage();
                    ship.StopAI();
                    Vessel.ActionGroups.SetGroup(KSPActionGroup.Abort, true);
                }

                ship.CalculateHeatSignature();
                ship.RefreshIncoming();

                yield return wait;
            }
        }

        internal void CheckStatus()
        {
            if (ship == null)
                return;

            bool hasControl = CheckControl();
            ship.hasControl = hasControl;

            // Optimised check for parts.
            bool hasPropulsion = false;
            bool hasWeapons = false;
            CheckParts(ref hasPropulsion, ref hasWeapons);

            bool dead = (!hasPropulsion && !hasWeapons) || !hasControl;

            ship.alive = !dead;
            ship.hasPropulsion = hasPropulsion;
            ship.hasWeapons = hasWeapons;
        }

        private bool CheckControl()
        {
            // Check for control.
            // todo: could use GameEvents.onVesselControlStateChange if hooking up other status checks to events.

            bool spunOut = false;
            if (Vessel.angularVelocity.magnitude > 50)
            {
                if (Time.time - lastInControl > controlTimeout)
                    spunOut = true;
            }
            else
                lastInControl = Time.time;

            return !spunOut && Vessel.IsControllable;
        }


        private bool Has(PartModule module)
        {
            return module != null && module.vessel == Vessel;
        }

        internal static bool Healthy(ModuleEngines engine) =>
            engine.EngineIgnited && engine.isOperational;

        private static bool Healthy(ModuleRCS rcs) =>
            rcs.rcsEnabled && !rcs.flameout && rcs.useThrottle;

        private bool Healthy(ModuleWeaponController weapon)
        {
            return weapon.weaponType switch
            {
                "Firework" => weapon.canFire && ship.hasControl,
                _          => weapon.canFire,
            };
        }

        private void CheckParts(ref bool hasPropulsion, ref bool hasWeapons)
        {
            hasPropulsion = false;
            hasWeapons = false;

            // First we check to see if our cached parts are functional.
            // If they're all present and functional then we can skip the search.

            bool validCache = (Has(statusRCS) || Has(statusEngine)) && Has(statusWeapon);

            if (validCache)
            {
                hasPropulsion = Has(statusEngine) && Healthy(statusEngine);
                bool hasRCSFore = Has(statusRCS) && Healthy(statusRCS);

                hasPropulsion = hasPropulsion || hasRCSFore;
                hasWeapons = Healthy(statusWeapon);
            }

            if (!hasWeapons || !hasPropulsion)
                SearchForParts(ref hasPropulsion, ref hasWeapons);
        }

        private void SearchForParts(ref bool hasPropulsion, ref bool hasWeapons)
        {
            // Micro optimised function to search for RCS, Weapons,
            // and Propulsion modules in one loop.
            // todo: engines must already be listed in the vessel for staging, right?
            // todo: weapons can be self reported by KCS.

            Part part;
            PartModuleList modules;
            PartModule module;

            ModuleRCS rcs;
            ModuleEngines engine;
            ModuleWeaponController weapon;

            int partCount = Vessel.parts.Count;
            int moduleCount, j;

            // Using FindPartModulesImplementing loops over the whole vessel,
            // so we combine the loops and do it all in one go for each module
            // we're looking for.

            // For every part.
            for (int i = 0; i < partCount; i++)
            {
                if (hasWeapons && hasPropulsion)
                    break;

                part = Vessel.parts[i];
                modules = part.Modules;

                // For every module in the part.
                moduleCount = modules.Count;
                for (j = 0; j < moduleCount; j++)
                {
                    module = modules[j];

                    Type moduleType = module.GetType();

                    if (!hasPropulsion && typeof(ModuleEngines).IsAssignableFrom(moduleType))
                    {
                        engine = (ModuleEngines)module;

                        if (hasPropulsion = Healthy(engine))
                            statusEngine = engine;
                    }
                    else if (!hasPropulsion && typeof(ModuleRCS).IsAssignableFrom(moduleType))
                    {
                        rcs = (ModuleRCS)module;

                        if (hasPropulsion = Healthy(rcs))
                            statusRCS = rcs;
                    }
                    else if (!hasWeapons && typeof(ModuleWeaponController).IsAssignableFrom(moduleType))
                    {
                        weapon = (ModuleWeaponController)module;

                        if (hasWeapons = Healthy(weapon))
                            statusWeapon = weapon;
                    }
                }
            }
        }

        #endregion

        #region Delta V

        private IEnumerator DeltaVChecker()
        {
            // We have to continuously call this checker because the calc is async and we may need it at any time.
            var wait = new WaitForSeconds(10f);

            while (true)
            {
                if (Vessel.persistentId != FlightGlobals.ActiveVessel.persistentId)
                    ForceDeltaVCheck(Vessel);

                yield return wait;
            }
        }

        public static void ForceDeltaVCheck(Vessel vessel)
        {
            if (vessel == null || vessel.VesselDeltaV == null)
                return;

            // This is so sad smh.
            var activeVessel = FlightGlobals.ActiveVessel;
            FlightGlobals.fetch.activeVessel = vessel;

            var dv = vessel.VesselDeltaV;
            MethodInfo method = dv.GetType().GetMethod("CheckDirtyAndRun", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(dv, null);

            // Reset active vessel.
            FlightGlobals.fetch.activeVessel = activeVessel;
        }

        #endregion
    }
}