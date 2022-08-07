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
        Vessel target;
        KCSFlightController fc;

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Toggle AI",
                  groupName = shipControllerGroupName,
                  groupDisplayName = shipControllerGroupName)]
        public void ToggleAI()
        {
            if (!controllerRunning)
            {
                shipControllerCoroutine = StartCoroutine(ShipController());
            }
            else
            {
                StopCoroutine(shipControllerCoroutine);
            }

            controllerRunning = !controllerRunning;
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                fc = new KCSFlightController(part.vessel);
            }
        }

        private IEnumerator ShipController()
        {
            while (true)
            {
                target = vessel.targetObject.GetVessel();

                if (target != null)
                {
                    fc.attitude = (target.ReferenceTransform.position - vessel.ReferenceTransform.position).normalized;
                    fc.throttle = 1;
                } 
                else
                {
                    fc.throttle = 0;
                }
                yield return new WaitForSeconds(updateInterval);
            } 
        }

        public void FixedUpdate()
        {
            if (controllerRunning) fc.Update();
        }
    }
}
