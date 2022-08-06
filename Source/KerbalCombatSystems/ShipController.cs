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


        // Ship AI variables.

        private Coroutine shipControllerCoroutine;
        Vessel target;

        // Movement variables

        Quaternion desiredRotation;
        float desiredThrottle;

        Quaternion lck;
        Quaternion currentLock;
        Quaternion progressiveLock;
        bool facingDesiredRotation;
        float throttle;

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

        private IEnumerator ShipController()
        {
            target = vessel.targetObject.GetVessel();

            if (target != null)
            {
                desiredRotation = Quaternion.LookRotation((target.ReferenceTransform.position - vessel.ReferenceTransform.position).normalized, vessel.up);
                desiredThrottle = 1;
            } 
            else
            {
                desiredThrottle = 0;
            }
            yield return new WaitForSeconds(1);
        }

        public void FixedUpdate()
        {
            if (controllerRunning)
            {
                MovementHandler();
            }
        }

        private void MovementHandler()
        {
            lck = desiredRotation * Quaternion.Euler(90, 0, 0);
            currentLock = vessel.Autopilot.SAS.lockedRotation;
            var a2 = Quaternion.Angle(currentLock, lck);
            progressiveLock = Quaternion.Slerp(currentLock, lck, 0.1f * (a2 / 100));
            vessel.Autopilot.SAS.LockRotation(progressiveLock);

            var a = Quaternion.Angle(vessel.ReferenceTransform.rotation * Quaternion.Euler(-90, 0, 0), desiredRotation);
            facingDesiredRotation = Quaternion.Angle(vessel.ReferenceTransform.rotation * Quaternion.Euler(-90, 0, 0), desiredRotation) < 5;
            throttle = facingDesiredRotation ? desiredThrottle : 0;

            vessel.ctrlState.mainThrottle = throttle;
            if (FlightGlobals.ActiveVessel != null && vessel == FlightGlobals.ActiveVessel)
                FlightInputHandler.state.mainThrottle = throttle; //so that the on-screen throttle gauge reflects the autopilot throttle
        }
    }
}
