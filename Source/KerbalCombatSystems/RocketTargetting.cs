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
        // KCSFlightController fc;
        Vessel Target;
        private Vector3 LeadVector;

        // Debugging line variables.
        LineRenderer TargetLine, LeadLine;
        
        //list of valid launcher modules with ammo
        private List<ModuleDecouple> RocketBases;

        public void Start()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;

            // initialise debug line renderer
            TargetLine = KCSDebug.CreateLine(Color.magenta);
            LeadLine = KCSDebug.CreateLine(Color.green);

            //get list of fireworks
            RocketBases = KCS.FindDecouplerChildren(part.parent, "Weapon", false);

        }

        private IEnumerator FireRocket()
        {
               
            yield return null;
            //delete the active module at the end.
            part.RemoveModule(part.GetComponent<ModuleRocket>());
        }

        public void OnDestroy()
        {
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(TargetLine);
        }
    }
}
