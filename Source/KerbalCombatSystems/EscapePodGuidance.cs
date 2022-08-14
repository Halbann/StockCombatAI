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

        //the 
        private List<Part> AIPartList;

        //universal flight controller and toggle
        KCSFlightController fc;
        private bool EngageAutopilot;
        bool Escaped;

        ModuleDecouple decoupler;
        //the ship being escaped from
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
            if (Escaped)
                return;

            //find decoupler
            decoupler = KCS.FindDecoupler(part, "Escape Pod", false);
            Debug.Log("[KCS]: Escaping from " + Parent.GetName());
            StartCoroutine(RunEscapeSequence());
        }
 
        private IEnumerator RunEscapeSequence()
        {
            // try to pop decoupler
            try
            {
                decoupler.Decouple();
            }
            catch
            {
                //notify of error but launch anyway for pods that have lost decoupler
                Debug.Log("[KCS]: Couldn't find decoupler on " + vessel.GetName() + " (Escape Pod)");
            }

            yield return null; // wait a frame

            if (vessel.GetObtVelocity().magnitude > vessel.VesselDeltaV.TotalDeltaVActual/2f)
            {
                //if burn has a chance to deorbit set target orientation to prograde
                BurnDirection = vessel.GetObtVelocity().normalized;

                if (vessel.mainBody.atmosphere)
                {
                    //if planet has an atmosphere set target orientation retrograde
                    BurnDirection = vessel.GetObtVelocity().normalized * -1;
                }
            }
            else
            {
                //set target orientation to away from the vessel by default
                BurnDirection = vessel.transform.position - Parent.transform.position;
                BurnDirection = BurnDirection.normalized;
            }

            // turn on engines
            List<ModuleEngines> engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            foreach (ModuleEngines engine in engines)
            {
                engine.Activate();
            }

            // enable autopilot
            fc = part.gameObject.AddComponent<KCSFlightController>();
            EngageAutopilot = true;

            yield break;
        }

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            //create the appropriate lists
            AIPartList = new List<Part>();
            List<ModuleShipController> AIModules;

            Escaped = false;

            //designate ship that's being escaped from
            Parent = vessel;

            //find ai parts and add to list
            AIModules = vessel.FindPartModulesImplementing<ModuleShipController>();
            foreach (var ModuleShipController in AIModules)
            {
                AIPartList.Add(ModuleShipController.part);
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (EngageAutopilot) 
            {
                fc.attitude = BurnDirection;
                fc.alignmentToleranceforBurn = 20;
                fc.throttle = 1;
                fc.Drive();
            }

            if (!Escaped && AIPartList != null)
            {
                CheckConnection();
            }
        }

        //method to check for the existence of any ship ai
        private void CheckConnection()
        {
            foreach (Part AIPart in AIPartList)
            {
                //if part does not exist / on the same ship
                if (AIPart.vessel == vessel) continue;
                AIPartList.Remove(AIPart);
            }
            
            //if the part list is now empty begin the escape sequence
            if (AIPartList.Count().Equals(0)) 
            {
                Escaped = true;
                BeginEscape();
            }

            if (vessel.VesselDeltaV.TotalDeltaVActual == 0)
            {
                //delete the active module at the end.
                Destroy(fc);
            }


        }
    }
}
