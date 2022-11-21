using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
        LineRenderer AimLine;

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
            AimLine = KCSDebug.CreateLine(new Color(196f / 255f, 208f / 255f, 164f / 255f, 1f));

            //get list of fireworks
            FindFireworks(part.parent);

            if (FireworkLaunchers.Count < 1)
                part.RemoveModule(part.GetComponent<ModuleFirework>());
            else
                controller.aimPart = FireworkLaunchers.First().part;
        }

        public override Vector3 Aim()
        {
            //if there is a ship target run the appropriate aiming angle calculations
            if (Target != null)
            {
                if (FireworkLaunchers.Count < 1)
                {
                    controller.canFire = false;
                    return Vector3.zero;
                }

                //get the firework launcher to aim with and where it is currently facing
                ModulePartFirework firstLauncher = FireworkLaunchers.First();

                FiringPart = firstLauncher.part;
                controller.aimPart = FiringPart;
                AimVector = FiringPart.transform.up;
                LeadVector = TargetLead(Target, FiringPart, 100f).normalized;

                // Update debug lines.
                Origin = FiringPart.transform.position;
                KCSDebug.PlotLine(new[] { Origin, Origin + (AimVector * 15) }, AimLine);
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

                // Change firework settings.
                Launcher.variationOnShellDirection = false;
                float oldVel = Launcher.shellVelocity;
                Launcher.shellVelocity = 100f;

                // Launch shell.
                Launcher.LaunchShell();

                // Restore firework settings.
                Launcher.variationOnShellDirection = true;
                Launcher.shellVelocity = oldVel;

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
            }
        }

        public void OnDestroy()
        {
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
