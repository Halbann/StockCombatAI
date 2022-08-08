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

        private List<Part> AIPartList;

        KCSFlightController fc;
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
                //notify of error but launch anyway for pods that have lost decoupler
                Debug.Log("Couldn't find decoupler on " + vessel.name + "escape pod");
            }

            yield return null; // wait a frame

            // todo: set control point to first probe ya find

            if (vessel.mainBody.atmosphere)
            {
                //if planet has an atmosphere set target orientation retrograde
                BurnDirection = vessel.GetObtVelocity().normalized * -1;
            }
            else if (vessel.GetObtVelocity().magnitude > vessel.VesselDeltaV.TotalDeltaVActual/2f)
            {
                //if burn has a chance to deorbit set target orientation to prograde
                BurnDirection = vessel.GetObtVelocity().normalized;
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
        
        private void Start()
        {
            //create the appropriate lists
            AIPartList = new List<Part>();
            List<ModuleShipController> AIModules;
            //find ai parts and add to list

            AIModules = vessel.FindPartModulesImplementing<ModuleShipController>();
            foreach (var ModuleShipController in AIModules)
            {
                AIPartList.Add(ModuleShipController.part);
            }

        }

        private void FixedUpdate()
        {
            if (EngageAutopilot) 
            {
                fc.attitude = BurnDirection;
                fc.alignmentToleranceforBurn = 20;
                //burn baby burn
                fc.throttle = 1;
                fc.Drive();
            }
            CheckConnection();
        }

        //method to check for the existence of any ship ai
        private void CheckConnection()
        {
            foreach (Part AIPart in AIPartList)
            {
                //if part does not exist on the same ship
                if (AIPart.vessel != vessel)
                {
                    //remove part from list
                    AIPartList.Remove(AIPart);
                }
            }
            
            //if the part list is now empty begin the escape sequence
            if(AIPartList.Count() == 0) BeginEscape();
        }

        ModuleDecouple FindDecoupler(Part origin)
        {
            Part currentPart = origin.parent;
            ModuleDecouple Decoupler;
            //ModuleDecouplerDesignate DecouplerType;	
            
            for (int i = 0; i < 99; i++)
            {
                //Get Decoupler type from designation module
                string DecouplerType = currentPart.GetComponent<ModuleDecouplerDesignate>().DecouplerType;
                //check if it is a decoupler and the correct type
                if (currentPart.isDecoupler(out Decoupler) && DecouplerType == "Escape Pod") return Decoupler;
                currentPart = currentPart.parent;
            }

            return null;
        }
    }
}
