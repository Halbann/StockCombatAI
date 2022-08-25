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
    public class ModuleRocket : ModuleWeapon
    {
        // Status variables
        public bool Firing = false;
        Vessel Target;

        // Targetting variables.
        public Vector3 LeadVector;
        private float AvgVel = 100f; //rough first guess
        Vector3 AimVector;

        // Debugging line variables.
        LineRenderer TargetLine, LeadLine, AimLine;

        // Rocket decoupler variables
        private List<ModuleDecouple> RocketBases;
        ModuleDecouple Decoupler;
        Vector3 Origin;


        public override void Setup()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;

            // initialise debug line renderer
            //TargetLine = KCSDebug.CreateLine(new Color(209f / 255f, 77f / 255f, 81f / 255f, 1f));
            //LeadLine = KCSDebug.CreateLine(new Color(167f / 255f, 103f / 255f, 104f / 255f, 1f));
            AimLine = KCSDebug.CreateLine(new Color(232f / 255f, 167f / 255f, 169f / 255f, 1f));

            //find a decoupler associated with the weapon
            RocketBases = FindDecouplerChildren(part.parent, "Weapon", false);
            Decoupler = RocketBases[RocketBases.Count() - 1];
        }

        public override Vector3 Aim()
        {
            //if there is a ship target run the appropriate aiming angle calculations
            if (Target != null)
            {
                //get where the weapon is currently pointing
                Origin = Decoupler.part.transform.position;
                AimVector = GetAwayVector(Decoupler.part);

                //recalculate LeadVector due to change in average rocket velocity
                LeadVector = TargetLead(Target, Decoupler.part, AvgVel);
                //recalculate Average Velocity over distance due to lead recalculation
                AvgVel = RocketVelocity(LeadVector, Decoupler.part);

                // Update debug lines.
                //KCSDebug.PlotLine(new[] { Origin, Target.transform.position }, TargetLine);
                //KCSDebug.PlotLine(new[] { Origin, LeadVector }, LeadLine);
                KCSDebug.PlotLine(new[] { Origin, AimVector }, AimLine);
            }

            //once aligned correctly start the firing sequence
            if (((Vector3.Angle(Origin - AimVector, Origin - LeadVector) < 1f) || Target == null) && !Firing)
            {
                Fire();
            }

            return LeadVector.normalized;
        }

        public override void Fire()
        {
            Firing = true;
            StartCoroutine(FireRocket());
        }

        private IEnumerator FireRocket()
        {
            //run through all child parts of the controllers parent for engine modules
            List<Part> DecouplerChildParts = Decoupler.part.FindChildParts<Part>(true).ToList();

            foreach (Part CurrentPart in DecouplerChildParts)
            {
                ModuleEngines Module = CurrentPart.GetComponent<ModuleEngines>();

                //check for engine modules on the part and stop if not found
                if (Module == null) continue;

                //activate the engine and force it to full capped thrust incase of ship throttle
                Module.Activate();
                Module.throttleLocked = true;
            }

            //wait a frame before decoupling to ensure engine activation(may not be required)
            yield return null;
            Decoupler.Decouple();

            Firing = false;
        }

        private float RocketVelocity(Vector3 TargetPos, Part RocketBase)
        {
            //get distance expected to travel
            float FlightDistance = Vector3.Distance(TargetPos, RocketBase.transform.position);
            //get child parts for weight and thrust
            List<Part> RocketPartList = RocketBase.FindChildParts<Part>(true).ToList();
            //get child engines
            List<Part> RocketEngineList = RocketBase.FindChildParts<Part>(true).ToList();

            //run through all child parts of the decoupler
            foreach (Part CurrentPart in RocketPartList)
            {
                if (CurrentPart.GetComponent<ModuleEngines>() != null)
                {
                    ModuleEngines Engine = CurrentPart.GetComponent<ModuleEngines>();
                    Debug.Log("thrust: " + Engine.maxThrust + "/nisp: " + Engine.realIsp);
                }

                foreach (PartResource resource in part.Resources)
                {
                    Debug.Log(resource + " quantity: " + resource.amount);
                }


                if (CurrentPart.GetComponent<ModuleEngines>() != null)
                {

                }

                CurrentPart.GetResourceMass();
            }


            //calculate 
            /*Need to get average velocity in a given distance. To benefit the engines are always running on full.
            Knowing fuel mass, engine isp, and capped thrust the total vessel mass over time can be got(hopefully without iteration)
            Knowing mass over time we can then get twr and from that the acceleration
            I really want to avoid second by second iterative because that's not very accurate 
            Once you yet the average velocity then that gets passed to the lead vector, the lead distance changes the given distance and so that repeats until fire*/
            return 55;
        }

        public void OnDestroy()
        {
            //KCSDebug.DestroyLine(TargetLine);
            //KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(AimLine);
        }
        
    }
}