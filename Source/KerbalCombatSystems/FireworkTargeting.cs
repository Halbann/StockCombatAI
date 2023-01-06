using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    public class ModuleFirework : ModuleWeapon
    {
        public static float fireworkSpeed = 100f;

        // Firework Targetting variables.
        public bool firing = false;
        Vessel target;
        public Vector3 leadVector;
        Part firingPart;
        Vector3 aimVector;
        Vector3 origin;

        // Debugging line variables.
        LineRenderer aimLine;

        // stored settings
        private int roundBurst;
        private float burstSpacing;
        private float burstInterval;
        float accuracyTolerance;

        //list of valid launcher modules with ammo
        private List<ModulePartFirework> fireworkLaunchers;

        ModuleWeaponController controller;

        public override void Setup()
        {
            UpdateSettings();

            // initialise debug line renderer
            aimLine = KCSDebug.CreateLine(new Color(196f / 255f, 208f / 255f, 164f / 255f, 1f));

            //get list of fireworks
            FindFireworks(part.parent);

            if (fireworkLaunchers.Count < 1)
                part.RemoveModule(part.GetComponent<ModuleFirework>());
            else
                controller.aimPart = fireworkLaunchers.First().part;
        }

        public override Vector3 Aim()
        {
            //if there is a ship target run the appropriate aiming angle calculations
            if (target != null)
            {
                if (fireworkLaunchers.Count < 1)
                {
                    controller.canFire = false;
                    return Vector3.zero;
                }

                //get the firework launcher to aim with and where it is currently facing
                ModulePartFirework firstLauncher = fireworkLaunchers.First();

                firingPart = firstLauncher.part;
                controller.aimPart = firingPart;
                aimVector = firingPart.transform.up;
                leadVector = TargetLead(target, firingPart, fireworkSpeed).normalized;

                // Update debug lines.
                origin = firingPart.transform.position;
                KCSDebug.PlotLine(new[] { origin, origin + (aimVector * 15) }, aimLine);
            }

            //once aligned correctly start the firing sequence
            if (!firing && OnTarget(leadVector, aimVector, target.CoM - firingPart.transform.position, controller.targetSize, accuracyTolerance))
            {
                Fire();
            }

            return leadVector;
        }

        public override void Fire()
        {
            if (firing)
                return;

            firing = true;
            UpdateSettings();
            StartCoroutine(FireShells());
        }

        private IEnumerator FireShells()
        {
            int tempBurst = roundBurst;

            //fire amount of shells
            for (int i = 0; i < tempBurst; i++)
            {
                if (fireworkLaunchers.Count < 1)
                {
                    controller.canFire = false;
                    break;
                }

                ModulePartFirework Launcher = fireworkLaunchers.Last();

                //clear expended launchers from the list
                if (Launcher.fireworkShots < 1)
                {
                    fireworkLaunchers.Remove(Launcher);
                    //add one more round to the burst to substitute missing shot
                    tempBurst++;
                    continue;
                }

                // Change firework settings.
                Launcher.variationOnShellDirection = false;
                float oldVel = Launcher.shellVelocity;
                Launcher.shellVelocity = fireworkSpeed;

                // Launch shell.
                Launcher.LaunchShell();

                // Restore firework settings.
                Launcher.variationOnShellDirection = true;
                Launcher.shellVelocity = oldVel;

                // Destroy FX controller to prevent lag from bursts. Trails still work.
                var fx = Launcher.fxController;
                Destroy(fx);

                yield return new WaitForSeconds(burstSpacing);
            }

            yield return new WaitForSeconds(burstInterval);

            firing = false;
        }

        private void FindFireworks(Part Root)
        {
            List<Part> childParts = part.parent.FindChildParts<Part>(true).ToList();
            childParts.Add(part.parent);

            fireworkLaunchers = new List<ModulePartFirework>();
            ModulePartFirework firework;

            foreach (Part CurrentPart in childParts)
            {
                firework = CurrentPart.GetComponent<ModulePartFirework>();
                if (firework == null) continue;

                fireworkLaunchers.Add(firework);
            }
        }

        public void OnDestroy()
        {
            KCSDebug.DestroyLine(aimLine);
        }

        public override void UpdateSettings()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();
            target = controller.target;
            roundBurst = (int)controller.FWRoundBurst;
            burstSpacing = controller.FWBurstSpacing;
            burstInterval = controller.FWBurstInterval;
            accuracyTolerance = controller.accuracyTolerance;
        }
    }
}
