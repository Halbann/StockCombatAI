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
    public class ModuleFirework : ModuleWeapon
    {
        // Firework Targetting variables.
        public bool firing = false;
        Vessel Target;
        public Vector3 LeadVector;
        Part FiringPart;
        Vector3 AimVector;
        Vector3 Origin;

        // Debugging line variables.
        LineRenderer TargetLine, LeadLine, AimLine;

        // stored settings
        private int RoundBurst;
        private float BurstSpacing;

        //list of valid launcher modules with ammo
        private List<ModulePartFirework> FireworkLaunchers;

        ModuleWeaponController controller;

        public override void Setup()
        {
            UpdateSettings();

            // initialise debug line renderer
            //TargetLine = KCSDebug.CreateLine(new Color(135f / 255f, 160f / 255f, 70f / 255f, 1f));
            LeadLine = KCSDebug.CreateLine(new Color(131f / 255f, 143f / 255f, 99f / 255f, 1f));
            AimLine = KCSDebug.CreateLine(new Color(196f / 255f, 208f / 255f, 164f / 255f, 1f));

            //get list of fireworks
            FindFireworks(part.parent);

            if (FireworkLaunchers.Count < 1)
                part.RemoveModule(part.GetComponent<ModuleFirework>());
        }

        public override Vector3 Aim()
        {
            //if there is a ship target run the appropriate aiming angle calculations
            if (Target != null)
            {
                //get the firework launcher to aim with and where it is currently facing
                FiringPart = FireworkLaunchers[0].part;
                AimVector = GetAwayVector(FiringPart);
                Origin = FiringPart.transform.position;

                LeadVector = TargetLead(Target, FiringPart, 100f);

                // Update debug lines.
                //KCSDebug.PlotLine(new[] { Origin, Target.transform.position }, TargetLine);
                KCSDebug.PlotLine(new[] { Origin, Origin + (LeadVector * 15)}, LeadLine);
                KCSDebug.PlotLine(new[] { Origin, Origin + (AimVector * 15)}, AimLine);
            }

            //once aligned correctly start the firing sequence
            if (!firing && ((Vector3.Angle(AimVector, LeadVector) < 1f) || Target == null))
            {
                Fire();
            }

            return LeadVector;
        }

        public override void Fire()
        {
            firing = true;
            UpdateSettings();
            StartCoroutine(FireShells());
        }

        private IEnumerator FireShells()
        {
            int TempBurst = RoundBurst;
            //fire amount of shells
            for (int i = 0; i < TempBurst; i++)
            {
                if (FireworkLaunchers.Count < 1)
                {
                    controller.canFire = false;
                    break;
                }

                yield return new WaitForSeconds(BurstSpacing);
                ModulePartFirework Launcher = FireworkLaunchers.Last();
                Launcher.LaunchShell();

                // Destroy FX controller to prevent lag from bursts. Trails still work.
                var fx = Launcher.fxController;
                Destroy(fx);

                //clear expended launchers from the list
                if (Launcher.fireworkShots < 1)
                {
                    FireworkLaunchers.Remove(Launcher);
                    //add one more round to the burst to substitute missing shot
                    TempBurst++;
                }
            }

            firing = false;
        }

        private void FindFireworks(Part Root)
        {
            List<Part> childParts = part.parent.FindChildParts<Part>(true).ToList();
            childParts.Add(part.parent);

            FireworkLaunchers = new List<ModulePartFirework>();
            ModulePartFirework Firework;

            foreach (Part CurrentPart in childParts)
            {
                Firework = CurrentPart.GetComponent<ModulePartFirework>();
                if (Firework == null) continue;

                FireworkLaunchers.Add(Firework);
                Firework.variationOnShellDirection = false;
                Firework.shellVelocity = 100f;
            }
        }

        public void OnDestroy()
        {
            //KCSDebug.DestroyLine(TargetLine);
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(AimLine);
        }

        public void UpdateSettings()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();
            Target = controller.target;
            RoundBurst = (int)controller.FWRoundBurst;
            BurstSpacing = controller.FWBurstSpacing;
        }
    }
}
