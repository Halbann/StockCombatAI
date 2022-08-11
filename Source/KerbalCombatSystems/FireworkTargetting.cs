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
    public class ModuleFirework : PartModule
    {
        // Firework Targetting variables.
        public bool OverrideAutopilot = false;
        KCSFlightController fc;
        Vessel Target;
        private Vector3 LeadVector;

        // Debugging line variables.
        LineRenderer TargetLine, LeadLine;

        /// stored settings
        private int RoundBurst;
        private float BurstSpacing;

        //list of valid launcher modules with ammo
        private List<ModulePartFirework> FireworkLaunchers;

        private IEnumerator FireworkAim()
        {
            //initially calculate aim vector
            LeadVector = KCS.TargetLead(Target, part.parent, 100f);

            while(/*directive vector doesn't match the aim vector*/)
            {
                LeadVector = KCS.TargetLead(Target, part.parent, 100f);
                //recalculate 5 times a second, get as low as is accurate
                yield return new WaitForSeconds(0.2f);
            }

            //once aligned correctly start the firing sequence
            StartCoroutine(FireShells());
        }


        public void Start()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;
            RoundBurst = (int)part.FindModuleImplementing<ModuleWeaponController>().FWRoundBurst;
            RoundBurst = (int)part.FindModuleImplementing<ModuleWeaponController>().FWRoundBurst;

            //get list of fireworks
            FindFireworks();

            StartCoroutine(FireworkAim());


        }

        private IEnumerator FireShells()
        {
            //fire amount of shells
            for (int i = 0; i < RoundBurst; i++)
            {
                //get end of launchers list
                ModulePartFirework Launcher = FireworkLaunchers[FireworkLaunchers.Count()-1];
                yield return new WaitForSeconds(BurstSpacing);
                //do the actual firing
                Launcher.LaunchShell();
                //clear expended launchers from the list
                if (Launcher.fireworkShots == 0)
                {
                    FireworkLaunchers.Remove(Launcher);
                }
                //continue to update the lead vector while firing
                LeadVector = KCS.TargetLead(Target, part.parent, 100f);
            }
        }


        public void OnDestroy()
        {
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(TargetLine);
        }

        public static void FindFireworks()
        {
            Part Root = part.parent;

            //run through all child parts of the object the controller is attached to for fireworks modules
            List<Part> FireworkLauncherParts = Root.FindChildParts<Part>(true).ToList();
            //spawn empty modules list to add to
            List<ModulePartFirework> FireworkLaunchers = new List<ModulePartFirework>();


            foreach (Part CurrentPart in FireworkLauncherParts)
            {
                //check for the firework launchers module and add it to the list
                if (CurrentPart.GetComponent<ModulePartFirework>())
                {
                    FireworkLaunchers.Add(CurrentPart.GetComponent<ModulePartFirework>());
                    //set outbound velocity to maximum
                    CurrentPart.GetComponent<ModulePartFirework>().shellVelocity = 100f;
                }
            }
        }

    }
}
