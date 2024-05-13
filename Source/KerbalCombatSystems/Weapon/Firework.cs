using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using static KerbalCombatSystems.Utils;
using KerbalCombatSystems.Debug2;
using KerbalCombatSystems.Fireworks;

namespace KerbalCombatSystems.Weapon
{
    public class ModuleFirework : ModuleWeapon
    {
        public ModuleWeaponController controller;

        // State.

        public int AmmoCount =>
            (int)launchers.Sum(l => l == null ? 0 : l.fireworkShots);

        private readonly List<ModulePartFirework> launchers = new List<ModulePartFirework>();
        private bool firing = false;
        private Transform muzzleTransform;
        private ModulePartFirework launcher;

        public override Part AimPart
        {
            get
            {
                if (launcher == null || launcher.vessel != vessel)
                    launcher = FindLauncher(launchers);

                return launcher.part;
            }
        }

        // Targeting.

        private Vessel Target => controller.target;
        private Vessel targetLast;
        private Vector3 targetPerturbationLast;
        private Vector3 leadDirection;
        private Vector3 muzzleDirection;
        private Vector3 sasDirection;
        private Lead lead;


        // SAS integral term.
        // todo: very scuffed, needs redoing in Unity.
        // it's better than nothing.

        public static float sasIntegralGain = 1.5f;
        public static float sasIntegralSaturation = 22.5f;
        private readonly IntegrationLayer xIntegral = new IntegrationLayer();
        private readonly IntegrationLayer yIntegral = new IntegrationLayer();


        public override void OnAwake()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();
            UpdateLaunchers();
            launcher = FindLauncher(launchers);
        }

        #region Weapon Generic

        public override Vector3 Aim()
        {
            if (Target == null)
                return Vector3.zero;

            launcher = FindLauncher(launchers);
            if (launcher == null)
                return Vector3.zero;

            muzzleTransform = launcher.gameObject.GetChild(launcher.cannonName).transform;
            muzzleDirection = muzzleTransform.up;

            // Reset perturbation if target changes.
            if (targetPerturbationLast == Vector3.zero || Target != targetLast)
                targetPerturbationLast = Target.perturbation;

            targetLast = Target;

            // Calculate lead.
            lead = TargetLead(Target, vessel, launcher.shellVelocity, muzzleTransform, targetPerturbationLast);
            leadDirection = lead.direction.normalized;

            // Debug jerk.
            if (Debug.Visible)
                DebugPerturbation(Target.perturbation, targetPerturbationLast);

            // Update perturbation.
            targetPerturbationLast = Target.perturbation;

            // Lead SAS using integral term.
            sasDirection = LeadSAS(leadDirection);

            // Start a firing sequence when correctly aligned.
            if (!firing && OnTarget(
                leadDirection, muzzleDirection, Target.CoM - muzzleTransform.position,
                controller.targetSize, controller.accuracyTolerance))
            {
                Fire();
            }

            // Debug.
            if (Debug.Visible)
                DebugAim();

            return sasDirection;
        }

        private Vector3 LeadSAS(Vector3 leadDirection)
        {
            xIntegral.saturation = sasIntegralSaturation;
            yIntegral.saturation = sasIntegralSaturation;
            xIntegral.gain = sasIntegralGain;
            yIntegral.gain = sasIntegralGain;

            Transform control = vessel.ReferenceTransform;
            Vector3 sasLead = leadDirection;

            float xError = Vector3.SignedAngle(muzzleDirection, leadDirection, control.forward);
            sasLead = Quaternion.AngleAxis(xIntegral.Update(xError, Time.fixedDeltaTime), control.forward) * sasLead;

            float yError = Vector3.SignedAngle(muzzleDirection, leadDirection, control.right);
            sasLead = Quaternion.AngleAxis(yIntegral.Update(yError, Time.fixedDeltaTime), control.right) * sasLead;

            return sasLead;
        }

        public override void Fire()
        {
            if (firing)
                return;

            firing = true;
            StartCoroutine(FireShells());
        }

        #endregion

        #region Functions

        private IEnumerator FireShells()
        {
            int burst = Mathf.RoundToInt(controller.FWRoundBurst);
            var wait = new WaitForSeconds(60f / controller.FWBurstSpacing);

            for (int i = 0; i < burst; i++)
            {
                launcher = FindLauncher(launchers);
                if (launcher == null)
                    break;

                if (controller.volleyFire)
                {
                    launchers.ForEach(l => l.LaunchShell());
                }
                else
                {
                    launcher.LaunchShell();
                }

                yield return wait;
            }

            if (controller.FWBurstInterval > 0f)
                yield return new WaitForSeconds(controller.FWBurstInterval);

            firing = false;
        }

        private void UpdateLaunchers()
        {
            List<Part> childParts = part.parent.FindChildParts<Part>(true).ToList();
            childParts.Add(part.parent);

            launchers.Clear();
            ModulePartFirework firework;

            foreach (Part part in childParts)
            {
                firework = part.FindModuleImplementing<ModulePartFirework>();
                if (firework == null)
                    continue;

                launchers.Add(firework);
            }

            if (launchers.Count < 1)
                controller.canFire = false;
        }

        private ModulePartFirework FindLauncher(List<ModulePartFirework> launchers)
        {
            ModulePartFirework launcher = null;
            bool found = false;

            for (int i = launchers.Count - 1; i >= 0; i--)
            {
                launcher = launchers[i];

                if (launcher == null || launcher.vessel != vessel)
                {
                    launchers.Remove(launcher);
                    continue;
                }

                if (launcher.fireworkShots < 1)
                    continue;

                found = true;
                break;
            }

            controller.canFire = found;
            return found ? launcher : null;
        }

        #endregion

        #region Debug

        private static Material debugProjectileMat;
        public static float debugProjectileSize = 1f;
        public static float debugLineSize = 0.3f;
        public static float debugLineAlpha = 0.3f;
        public static bool debugShell = false;

        private void DebugPerturbation(Vector3 perturbation, Vector3 perturbationLast)
        {
            // Debug

            Vector3 pertRate = (perturbation - perturbationLast) / Time.fixedDeltaTime;
            Vector3 jerkOffset = 1f / 6f * pertRate * Mathf.Pow(lead.time, 3);
            Line.Draw(Target.CoM, jerkOffset, Color.cyan, 0.5f, 1f);
        }

        private void DebugAim()
        {
            Vector3 origin = muzzleTransform.position;
            Color lime = new Color(196f / 255f, 208f / 255f, 164f / 255f, 1f);

            Line.Draw(origin, muzzleDirection, 15f, lime, debugLineAlpha, debugLineSize);
            Line.Draw(origin, leadDirection, 15f, Color.red, debugLineAlpha, debugLineSize);
            Line.Draw(origin, sasDirection, 15f, Color.blue, debugLineAlpha, debugLineSize);

            float error = Vector3.Angle(muzzleDirection, leadDirection);
            float tolerance = GetTolerance(Target.CoM - muzzleTransform.position, controller.targetSize, controller.accuracyTolerance);
            string text = $"Error: {error:F4}\n Tolerance: {tolerance:F4}";
            Debug.DrawDebugLabel(text, transform.position);

            if (debugShell)
                DebugShell(origin, leadDirection.normalized * launcher.shellVelocity, lead.time);
        }

        private void DebugShell(Vector3 position, Vector3 velocity, float lifetime)
        {
            // Spawn a collision disabled sphere that follows the path that a projectile would.

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = position;
            sphere.transform.localScale = Vector3.one * debugProjectileSize;
            sphere.transform.up = velocity.normalized;

            if (debugProjectileMat == null)
            {
                debugProjectileMat = new Material(Shader.Find("Unlit/Color"));
                debugProjectileMat.color = Color.cyan;
            }

            sphere.GetComponent<Renderer>().material = debugProjectileMat;

            var po = physicalObject.ConvertToPhysicalObject(part, sphere);
            po.origDrag = 0;
            po.maxDistance = 1000;

            Destroy(sphere.GetComponent<SphereCollider>());

            var firerVelocity = vessel.rb_velocity;

            Rigidbody rb = po.rb;
            rb.mass = 0.3f;
            rb.velocity = firerVelocity + velocity;
            rb.useGravity = false;
            rb.drag = 0;
            rb.angularDrag = 0;
            rb.detectCollisions = false;

            Destroy(sphere, lifetime);
        }

        #endregion
    }
}
