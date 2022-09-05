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

        private List<Part> AIPartList;
        private List<ModuleEngines> Engines;

        //universal flight controller and toggle
        KCSFlightController fc;
        private bool Escaped = false;
        private double minSafeAltitude;

        //ModuleDecouple Decoupler;
        Seperator seperator;
        private Vessel Parent;

        //escape guidance is called when the button is pressed, when no ship controller can be found onboard the ship, or when the ship controller dictates an evacuation
        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Eject",
                  groupName = EscapeGuidanceGroupName,
                  groupDisplayName = EscapeGuidanceGroupName)]
        public void BeginEscape()
        {
            //find decoupler
            seperator = FindDecoupler(part, "Escape Pod", false);
            Debug.Log("[KCS]: Escaping from " + Parent.GetName());
            //set the refresh rate
            StartCoroutine(RunEscapeSequence());
        }

        [KSPAction("Fire Escape Pod", KSPActionGroup.Abort)]
        public void ManualEscape(KSPActionParam param)
        {
            BeginEscape();
        }

        private IEnumerator RunEscapeSequence()
        {
            //stop the status checker temporarily
            StopCoroutine(StatusRoutine());

            // try to pop decoupler
            try
            {
                //Decoupler.Decouple();
                seperator.Separate();
            }
            catch
            {
                //notify of error but launch anyway for pods that have lost decoupler
                Debug.Log("[KCS]: Couldn't find decoupler on " + vessel.GetName() + " (Escape Pod)");
            }

            // set target orientation to away from the vessel by default
            fc = part.gameObject.AddComponent<KCSFlightController>();

            yield return new WaitForFixedUpdate(); // wait a frame
            FindCommand(vessel).MakeReference();

            // turn on engines and create list
            Engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            foreach (ModuleEngines Engine in Engines)
            {
                Engine.Activate();
            }

            // enable autopilot and set target orientation to away from the vessel by default
            fc.throttleLerpRate = 100;
            fc.throttle = 1;
            fc.alignmentToleranceforBurn = 10;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.Drive();

            // turn on engines
            List<ModuleParachute> Parachutes = vessel.FindPartModulesImplementing<ModuleParachute>();
            foreach (ModuleParachute Parachute in Parachutes)
            {
                Parachute.Deploy();
            }

            yield return new WaitForSeconds(1f);

            StartCoroutine(EscapeRoutine());

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
            foreach (var Controller in AIModulesList)
            {
                AIPartList.Add(Controller.part);
            }

            StartCoroutine(StatusRoutine());
        }

        IEnumerator StatusRoutine()
        {
            if (!Escaped)
            {
                CheckConnection();
            }

            yield return new WaitForSeconds(5f);
            StartCoroutine(StatusRoutine());
            yield break;
        }

        IEnumerator EscapeRoutine()
        {
            while (vessel.FindPartModulesImplementing<ModuleEngines>().FindAll(e => e.EngineIgnited && e.isOperational).Count > 0)
            {

                if (CheckOrbitUnsafe())
                {
                    Orbit o = vessel.orbit;
                    double UT = Planetarium.GetUniversalTime();

                    // Execute a burn to circularize our orbit at the current altitude.
                    Vector3d fvel, deltaV = Vector3d.up * 100;

                    while (deltaV.magnitude > 2)
                    {
                        yield return new WaitForFixedUpdate();

                        UT = Planetarium.GetUniversalTime();
                        fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                        deltaV = fvel - vessel.GetObtVelocity();

                        fc.attitude = deltaV.normalized;
                        fc.throttle = Mathf.Lerp(0, 1, (float)(deltaV.magnitude / 10));
                        fc.Drive();
                    }
                }
                else
                {
                    Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                    bool facingNorth = Vector3.Angle(vessel.ReferenceTransform.up, orbitNormal) < 90;
                    fc.attitude = orbitNormal * (facingNorth ? 1 : -1);
                    fc.throttle = 1;
                    fc.Drive();
                }
                
                yield return new WaitForSeconds(5.0f);
            }

            Debug.Log("[KCS]: Escape Sequence Ending on " + vessel.GetName() + " (Escape Pod)");
            //remove the flight controller and allow the guidance to cease
            Destroy(part.gameObject.GetComponent<KCSFlightController>());
            yield break;
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            CelestialBody body = o.referenceBody;
            PQS pqs = body.pqsController;
            double maxTerrainHeight = pqs.radiusMax - pqs.radius;
            minSafeAltitude = maxTerrainHeight;
            return o.PeA < minSafeAltitude;
        }

        //method to check for the existence of any ship ai onboard
        public void CheckConnection()
        {
            for (int i = AIPartList.Count - 1; i >= 0; i--)
            {
                    //if part does not exist / on the same ship
                    if (AIPartList[i] == null || AIPartList[i].vessel.id != part.vessel.id)
                    AIPartList.RemoveAt(i);
            }

            if (AIPartList.Count == 0) 
            {
                 Escaped = true;
                 BeginEscape();
            }
        }

        
    }
}
