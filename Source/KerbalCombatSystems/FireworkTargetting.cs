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
       // KCSFlightController fc;
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
            // initialise debug line renderer
            TargetLine = KCSDebug.CreateLine(Color.magenta);
            LeadLine = KCSDebug.CreateLine(Color.green);

            //initially calculate aim vector
            LeadVector = KCS.TargetLead(Target, part.parent, 100f);

            //todo: add an appropriate aim deviation check

            //while (Vector3.AngleBetween(LeadVector, part.parent.forward()) > 1)
            while (0 > 1)
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
            Debug.Log($"[KCS]: start");
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;
            RoundBurst = (int)part.FindModuleImplementing<ModuleWeaponController>().FWRoundBurst;
            BurstSpacing = (int)part.FindModuleImplementing<ModuleWeaponController>().FWBurstSpacing;

            //get list of fireworks
            FindFireworks(part.parent);

            //todo: skip over the aiming part if not on autopilot

            //StartCoroutine(FireworkAim());

            StartCoroutine(FireShells());
        }

        private IEnumerator FireShells()
        {
            Debug.Log($"[KCS]: fireworks launchers " + FireworkLaunchers.Count());
            //fire amount of shells
            for (int i = 0; i < RoundBurst; i++)
            {
                //check if launchers are empty and skip if so
                if(FireworkLaunchers.Count.Equals(0)) continue;

                //get end of launchers list
                ModulePartFirework Launcher = FireworkLaunchers[FireworkLaunchers.Count-1];
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
            if (Target == null) return;
            // Update debug lines.
            KCSDebug.PlotLine(new[] { part.transform.position, Target.transform.position }, TargetLine);
            KCSDebug.PlotLine(new[] { part.transform.position, LeadVector }, LeadLine);

        }

        private void FindFireworks(Part Root)
        {
            Debug.Log($"[KCS]: finding firework modules");
            //run through all child parts of the controllers parent for fireworks modules
            List<Part> FireworkLauncherParts = Root.FindChildParts<Part>(true).ToList();
            //check the parent itself
            FireworkLauncherParts.Add(Root);
            //spawn empty modules list to add to
            FireworkLaunchers = new List<ModulePartFirework>();

            Debug.Log($"[KCS]: parts searched " + FireworkLauncherParts.Count());

            foreach (Part CurrentPart in FireworkLauncherParts)
            {
                //check for the firework launchers module and add it to the list
                if (CurrentPart.GetComponent<ModulePartFirework>() != null)
                {
                    Debug.Log($"[KCS]: adding firework module");
                    FireworkLaunchers.Add(CurrentPart.GetComponent<ModulePartFirework>());
                    //set outbound velocity to maximum
                    CurrentPart.GetComponent<ModulePartFirework>().shellVelocity = 100f;
                }
            }
        }

    }
}
