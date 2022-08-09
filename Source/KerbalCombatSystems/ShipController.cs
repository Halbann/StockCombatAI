using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    public class ModuleShipController : PartModule
    {
        // User parameters changed via UI.

        const string shipControllerGroupName = "Ship AI";
        private bool controllerRunning = false;
        public float updateInterval = 0.1f;

        // Ship AI variables.

        private Coroutine shipControllerCoroutine;
        private KCSFlightController fc;
        private KCSController controller;
        public Vessel target;
        public List<ModuleMissileGuidance> missiles;
        float lastFired;
        float fireInterval;

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Toggle AI",
                  groupName = shipControllerGroupName,
                  groupDisplayName = shipControllerGroupName)]
        public void ToggleAI()
        {
            if (!controllerRunning)
            {
                CheckWeapons();
                shipControllerCoroutine = StartCoroutine(ShipController());
            }
            else
            {
                StopCoroutine(shipControllerCoroutine);
            }

            controllerRunning = !controllerRunning;
        }

        public void CheckWeapons()
        {
            missiles = vessel.FindPartModulesImplementing<ModuleMissileGuidance>();
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                fc = part.gameObject.AddComponent<KCSFlightController>();
                controller = FindObjectOfType<KCSController>();
            }
        }

        private IEnumerator ShipController()
        {
            while (true)
            {
                // Find target.

                FindTarget();

                // Engage. todo: Need to separate update of target position from main coroutine.

                if (target != null)
                {
                    fc.attitude = (target.ReferenceTransform.position - vessel.ReferenceTransform.position).normalized;
                    fc.throttle = 1;

                    if (Time.time - lastFired > fireInterval)
                    {
                        lastFired = Time.time;
                        fireInterval = UnityEngine.Random.Range(5, 15);

                        if (missiles.Count > 0)
                        {
                            var missileIndex = UnityEngine.Random.Range(0, missiles.Count - 1);
                            missiles[missileIndex].FireMissile();

                            yield return null;
                            CheckWeapons();
                        }
                    }  
                } 
                else
                {
                    fc.throttle = 0;
                }

                yield return new WaitForSeconds(updateInterval);
            } 
        }

        void FindTarget()
        {
            // todo: how to exlcude self?
            target = controller.ships.OrderBy(s => KCS.VesselDistance(s.v, vessel)).First().v;
        }

        public void FixedUpdate()
        {
            if (controllerRunning) fc.Drive();
        }
    }
}
