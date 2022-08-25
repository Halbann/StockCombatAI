using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;
using static KerbalCombatSystems.KCS;

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
        private bool Escaped = false;

        ModuleDecouple Decoupler;
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
            //find decoupler
            Decoupler = FindDecoupler(part, "Escape Pod", false);
            Debug.Log("[KCS]: Escaping from " + Parent.GetName());
            StartCoroutine(RunEscapeSequence());
        }
 
        private IEnumerator RunEscapeSequence()
        {
            //todo: pull all crew, infighting should equalize

            // try to pop decoupler
            try
            {
                Decoupler.Decouple();
            }
            catch
            {
                //notify of error but launch anyway for pods that have lost decoupler
                Debug.Log("[KCS]: Couldn't find decoupler on " + vessel.GetName() + " (Escape Pod)");
            }

            yield return null; // wait a frame

            //get a list of onboard control points once seperated and control from the first found
            List<ModuleCommand> ProbeCores = vessel.FindPartModulesImplementing<ModuleCommand>();
            if (ProbeCores.Count != 0)
            {
                ModuleCommand ProbeCore = ProbeCores[0];
                ProbeCore.ChangeControlPoint();
            }


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
            List<ModuleShipController> AIModulesList;

            //designate ship that's being escaped from
            Parent = vessel;

            //find ai parts and add to list
            AIModulesList = vessel.FindPartModulesImplementing<ModuleShipController>();
            foreach (var ModuleShipController in AIModulesList)
            {
                AIPartList.Add(ModuleShipController.part);
            }
        }

        public void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (EngageAutopilot) 
            {
                fc.attitude = BurnDirection;
                fc.alignmentToleranceforBurn = 20;
                fc.throttle = 1;
                fc.Drive();
                FuelCheck();
            }

            if (!Escaped)
            {
                CheckConnection();
            }
        }

        //method to check for the existence of any ship ai onboard
        private void CheckConnection()
        {
            /*foreach (Part AIPart in AIPartList)
            {
                //if part itself is destroyed
                if (AIPart == null) continue;
                //if part does not exist / on the same ship
                if (AIPart.vessel == vessel) continue;
                AIPartList.Remove(AIPart);
            }
            
            //if the part list is now empty begin the escape sequence
            if (AIPartList.Count == 0) 
            {
                Escaped = true;
                BeginEscape();
            }*/
        }

        //method to disable the autopilot once out of fuel
        private void FuelCheck()
        {
            if (vessel.VesselDeltaV.TotalDeltaVActual == 0)
            {
                //delete the active module at the end.
                Destroy(fc);
                //deactivate autopilot entirely
                EngageAutopilot = false;
            }
        }
    }
}
