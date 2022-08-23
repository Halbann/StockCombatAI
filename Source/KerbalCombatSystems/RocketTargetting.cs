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
    public class ModuleRocket : PartModule
    {
        // Targetting variables.
        public bool FireStop = false;
        Vessel Target;
        public Vector3 LeadVector;
       
        // Debugging line variables.
        LineRenderer TargetLine, LeadLine, AimLine;
        
        //rocket decoupler variables
        private List<ModuleDecouple> RocketBases;
        ModuleDecouple Decoupler;
        
        

        public void Start()
        {
            /*
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;

            // initialise debug line renderer
            TargetLine = KCSDebug.CreateLine(new Color(209f / 255f, 77f / 255f, 81f / 255f, 1f)); 
            LeadLine = KCSDebug.CreateLine(new Color(167f / 255f, 103f / 255f, 104f / 255f, 1f));
            AimLine = KCSDebug.CreateLine(new Color(232f / 255f, 167f / 255f, 169f / 255f, 1f));

            //find a decoupler associated with the weapon
            RocketBases = KCS.FindDecouplerChildren(part.parent, "Weapon", false);
            Decoupler = RocketBases[RocketBases.Count() - 1];
            */


        }

        public void LateUpdate()
        {
            /*
            //get where the weapon is currently pointing
            Vector3 AimVector = KCS.GetAwayVector(Decoupler.part);
            //get the aiming part
            Vector3 Origin = Decoupler.part.transform.position;

            if (Target != null)
            {
                //recalculate Average Velocity over distance
                float AvgVel = RocketVelocity(Target, Decoupler.part);
                //recalculate LeadVector
                LeadVector = KCS.TargetLead(Target, Decoupler.part, AvgVel);

                // Update debug lines.
                KCSDebug.PlotLine(new[] { Origin, Target.transform.position }, TargetLine);
                KCSDebug.PlotLine(new[] { Origin, LeadVector }, LeadLine);
                KCSDebug.PlotLine(new[] { Origin, AimVector }, AimLine);
            }

            //once aligned correctly start the firing sequence
            if (((Vector3.Angle(Origin - AimVector, Origin - LeadVector) < 1f) || Target == null) && FireStop == false)
            {
                FireStop = true;
                StartCoroutine(FireRocket());
            }
            */
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
            //delete the active module at the end.
            part.RemoveModule(part.GetComponent<ModuleRocket>());
        }

        private float RocketVelocity(Vessel Target, Part RocketBase)
        {
            //get distance

            //calculate 

            return 55;
        }
        
        public void OnDestroy()
        {
            /*
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(TargetLine);
            KCSDebug.DestroyLine(AimLine);
            */
        }
    }
}
