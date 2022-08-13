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
        public bool FireStop = false;
       // KCSFlightController fc;
        Vessel Target;
        public Vector3 LeadVector;

        // Debugging line variables.
        LineRenderer TargetLine, LeadLine;

        /// stored settings
        private int RoundBurst;
        private float BurstSpacing;

        //list of valid launcher modules with ammo
        private List<ModulePartFirework> FireworkLaunchers;

        public void Start()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;
            RoundBurst = (int)part.FindModuleImplementing<ModuleWeaponController>().FWRoundBurst;
            BurstSpacing = part.FindModuleImplementing<ModuleWeaponController>().FWBurstSpacing;

            // initialise debug line renderer
            TargetLine = KCSDebug.CreateLine(Color.magenta);
            LeadLine = KCSDebug.CreateLine(Color.green);

            //get list of fireworks
            FindFireworks(part.parent);

        }

        private IEnumerator FireShells()
        {
            int TempBurst = RoundBurst;
            //fire amount of shells
            for (int i = 0; i < TempBurst; i++)
            {
                //check if launchers are empty and skip if so
                if (FireworkLaunchers.Count.Equals(0))
                {
                    //todo: tell the ship controller and weapons interface that this launcher is empty
                    break;
                }

                yield return new WaitForSeconds(BurstSpacing);
                //get end of launchers list
                ModulePartFirework Launcher = FireworkLaunchers[FireworkLaunchers.Count-1];
                //do the actual firing
                Launcher.LaunchShell();

                //clear expended launchers from the list
                if (Launcher.fireworkShots.Equals(0))
                {
                    //remove empty launcher from list
                    FireworkLaunchers.Remove(Launcher);
                    //add one more round to the burst to substitute missing hsot
                    TempBurst += 1;
                }
            }

            //wait a frame
            yield return null;
            //delete the active module at the end.
            part.RemoveModule(part.GetComponent<ModuleFirework>());
        }

        public void OnDestroy()
        {
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(TargetLine);
        }

        public void Update()
        {
            if (Target != null)
            {
                //recalculate LeadVector
                LeadVector = KCS.TargetLead(Target, part.parent, 100f);
                // Update debug lines.
                KCSDebug.PlotLine(new[] { part.transform.position, Target.transform.position }, TargetLine);
                KCSDebug.PlotLine(new[] { part.transform.position, LeadVector }, LeadLine);
            }

            //todo: skip over the aiming part if not on autopilot
            //todo: add an appropriate aim deviation check
            //(Vector3.AngleBetween(LeadVector, part.parent.forward()) > 1)
            if ((true || Target == null) && FireStop == false)
            {
                FireStop = true;
                //once aligned correctly start the firing sequence
                StartCoroutine(FireShells());
            }
        }

        private void FindFireworks(Part Root)
        {
            //run through all child parts of the controllers parent for fireworks modules
            List<Part> FireworkLauncherParts = Root.FindChildParts<Part>(true).ToList();
            //check the parent itself
            FireworkLauncherParts.Add(Root);
            //spawn empty modules list to add to
            FireworkLaunchers = new List<ModulePartFirework>();


            foreach (Part CurrentPart in FireworkLauncherParts)
            {
                //check for the firework launchers module and add it to the list
                if (CurrentPart.GetComponent<ModulePartFirework>() == null) continue;

                FireworkLaunchers.Add(CurrentPart.GetComponent<ModulePartFirework>());
                //set outbound velocity to maximum
                CurrentPart.GetComponent<ModulePartFirework>().shellVelocity = 100f;

            }
        }

    }
}
