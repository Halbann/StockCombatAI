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
        private Vector3 targetVector;
        private Vector3 targetVectorNormal;
        private Vector3 relVel;
        private Vector3 relVelNrm;
        private float relVelmag;
        Vessel Target;
        // Debugging line variables.

        LineRenderer TargetLine, LeadLine;
        private int RoundBurst;

        /// stored shit
        private List<ModulePartFirework> FireworkLaunchers;
        //public, let the ship AI display maybe
        public int Shells;

        private IEnumerator FireworkAim()
        {

            //until aimed
            //recalculate target vector

            wait until vang(ship: facing:forevector, SteerTo) < 2 AND ship:ANGULARVEL: mag < 0.1.
            set SteerTo to FiringSolution.
            wait 3;

            //once aligned correctly start the firing sequence
            StartCoroutine(FireShells());
        }


        public void Start()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;
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
                yield return new WaitForSeconds(0.25f);
                //do the actual firing
                Launcher.LaunchShell();
                //clear expended launchers from the list
                if (Launcher.fireworkShots == 0)
                {
                    FireworkLaunchers.Remove(Launcher);
                }
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
                    CurrentPart.GetComponent<ModulePartFirework>().shellVelocity = 50f;
                }
            }
        }

    }
}
