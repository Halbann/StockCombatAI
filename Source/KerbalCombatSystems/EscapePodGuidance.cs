using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleEscapePodGuidance : PartModule
    {
        const string EscapeGuidanceGroupName = "Escape Pod Guidance";


        private bool EngageAutopilot;

        private Vessel Parent;
        //target burn direction
        private Vector3 BurnDirection;

        //escape guidance is called when the button is pressed, when no ship controller can be found onboard the ship, or when the ship controller dictates an evacuation
        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Eject",
                  groupName = EscapeGuidanceGroupName,
                  groupDisplayName = EscapeGuidanceGroupName)]
        public void BeginEscape()
        {

            StartCoroutine(Escape());
        }
        

        public IEnumerator Escape()
        {
            //designate ship that's being escaped from
            Parent = vessel;

            // find decoupler
            ModuleDecouple decoupler = FindDecoupler(part);

            // try to pop decoupler
            try
            {
                decoupler.Decouple();
            }
            catch
            {
                //notify of error but launch anyway for freefloating missiles
                Debug.Log("Couldn't find decoupler.");
            }



            if (vessel.mainBody.atmosphere)
            {
                //if planet has an atmosphere set target orientation retrograde
                BurnDirection = -vessel.GetObtVelocity();
            }
            else if (vessel.GetObtVelocity().magnitude > vessel.VesselDeltaV.TotalDeltaVActual/2f)
            {
                //if burn has a chance to deorbit set target orientation to prograde
                BurnDirection = vessel.GetObtVelocity();
            }
            else
            {
                //set target orientation to away from the vessel by default
                Vector3 targetVector = vessel.transform.position - Parent.transform.position;
                Vector3 BurnDirection = targetVector.normalized;
            }


            // enable autopilot
            EngageAutopilot = true;

            yield break;
        }
        public void FixedUpdate()
        {
            if (EngageAutopilot)
            {

            }

        }


        ModuleDecouple FindDecoupler(Part origin)
        {
            Part currentPart = origin.parent;
            ModuleDecouple decoupler;

            //ModuleDecouplerDesignate DecouplerType;	


            for (int i = 0; i < 99; i++)
            {
                if (currentPart.isDecoupler(out decoupler)) return decoupler;
                currentPart = currentPart.parent;
            }

            return null;
        }
    }
}
