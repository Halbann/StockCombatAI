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

        // todo: we will need to reset when the state of on-board cargo containers changes.
        private readonly Dictionary<uint, bool> canReload = new Dictionary<uint, bool>();

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
        private Vector3 muzzleDirection;
        private Vector3 sasDirection;
        private Lead lead;

        //private Vector3d targetPerturbationLast;
        //private readonly Queue<Vector3d> jerkQueue = new Queue<Vector3d>(); // lol
        //public float lastMeasuredJerkTime;
        //public static float jerkSmoothTime = 2f;
        //public static float jerkMultiplier = 0f;

        public static float accSmoothTime = 0.2f;
        private Vector3 accSmoothSpeed;
        private Vector3 accSmoothed;
        private float lastAimTime;

        // SAS integral term.
        // todo: very scuffed, needs redoing in Unity.
        // it's better than nothing.

        public ModuleShipController shipController;

        public ModuleShipController ShipController =>
            shipController ?? FindController(vessel);

        public float SASIntegralGain =>
                ShipController?.sasIntegralGain ?? 0;

        public float SASIntegralSaturation =>
            ShipController?.sasIntegralSaturation ?? 0;

        private readonly IntegrationLayer xIntegral = new IntegrationLayer();
        private readonly IntegrationLayer yIntegral = new IntegrationLayer();


        public override void OnAwake()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();
            UpdateLaunchers();
            launcher = FindLauncher(launchers);

            if (HighLogic.LoadedSceneIsFlight)
                StartCoroutine(OverheatMonitor());
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

            // Calculate lead.

            //Vector3 jerk = jerkMultiplier * MeasureJerk();
            Vector3 targetAcc = MeasureAcceleration();
            lead = TargetLead(Target, vessel, launcher.shellVelocity, muzzleTransform, targetAcc);
            targetLast = Target;

            // Lead SAS using integral term.
            sasDirection = LeadSAS(lead.direction);

            // Start a firing sequence when correctly aligned.
            if (!firing && OnTarget(
                lead.direction, muzzleDirection, Target.CoM - muzzleTransform.position,
                controller.targetSize, controller.accuracyTolerance))
            {
                Fire();
            }

            // Debug.
            if (Debug.Visible)
                DebugAim();

            return sasDirection;
        }

        private Vector3 MeasureAcceleration()
        {
            // lambda function with no return to reset smoothed acceleration.

            if (accSmoothed == Vector3.zero)
                ResetTargetAcceleration();

            if (Time.fixedTime - lastAimTime > Time.fixedTime)
                ResetTargetAcceleration();

            if (Target != targetLast)
                ResetTargetAcceleration();

            accSmoothed = Vector3.SmoothDamp(accSmoothed, Target.perturbation, ref accSmoothSpeed, accSmoothTime);
            lastAimTime = Time.fixedTime;

            return accSmoothed;
        }

        private void ResetTargetAcceleration()
        {
            accSmoothed = Target.perturbation;
            accSmoothSpeed = Vector3.zero;
        }

        /*private Vector3 MeasureJerk()
        {
            // Measure the average jerk over the last jerkSmoothTime seconds.

            if (targetPerturbationLast == Vector3d.zero || Target != targetLast || Time.fixedTime - lastMeasuredJerkTime > Time.fixedTime)
            {
                // Reset.

                targetPerturbationLast = Target.perturbation;
                jerkQueue.Clear();
            }

            targetLast = Target;

            Vector3d jerkFrame = (Target.perturbation - targetPerturbationLast) / Time.fixedDeltaTime;
            jerkQueue.Enqueue(jerkFrame);
            if (jerkQueue.Count > jerkSmoothTime / Time.fixedUnscaledDeltaTime)
                jerkQueue.Dequeue();

            // skull emoji.
            Vector3d meanJerk = jerkQueue.Aggregate(Vector3d.zero, (acc, j) => acc + j) / jerkQueue.Count;

            if (Debug.Visible)
            {
                Vector3 jerkOffset = 1f / 6f * meanJerk * Mathf.Pow(lead.time, 3);
                Line.Draw(Target.CoM, jerkOffset, Color.cyan, 0.5f, 0.5f);
            }

            // Update perturbation.
            targetPerturbationLast = Target.perturbation;
            lastMeasuredJerkTime = Time.fixedTime;

            return meanJerk;
        }*/

        private Vector3 LeadSAS(Vector3 leadDirection)
        {
            xIntegral.saturation = SASIntegralSaturation;
            yIntegral.saturation = SASIntegralSaturation;
            xIntegral.gain = SASIntegralGain;
            yIntegral.gain = SASIntegralGain;

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

                // todo: very silly code. smh my head.
                if (controller.volleyFire)
                {
                    foreach (ModulePartFirework launcher in launchers)
                    {
                        if (TooHot(launcher))
                            continue;

                        if (launcher.fireworkShots < 1)
                            TryReload(launcher);

                        launcher.LaunchShell();
                    }
                }
                else
                {
                    if (!TooHot(launcher))
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

                // Register for ammo changes.
                var reloader = firework.GetComponent<ModuleLauncherReload>();
                if (reloader)
                {
                    reloader.OnShotCountChanged -= OnAmmoChanged;
                    reloader.OnShotCountChanged += OnAmmoChanged;
                }

                part.OnJustAboutToDie -= OnLauncherDie;
                part.OnJustAboutToDie += OnLauncherDie;
            }

            if (launchers.Count < 1)
                controller.canFire = false;
        }

        private void OnLauncherDie()
        {
            // canFire needs to update in the event that a launcher is destroyed,
            // even if the controller is not being polled for aim.
            launcher = FindLauncher(launchers);
        }

        private ModulePartFirework FindLauncher(List<ModulePartFirework> launchers)
        {
            ModulePartFirework launcher = null;
            ModulePartFirework overheatLauncher = null;
            bool found = false;

            for (int i = launchers.Count - 1; i >= 0; i--)
            {
                launcher = launchers[i];

                if (launcher == null || launcher.vessel != vessel || launcher.part.State == PartStates.DEACTIVATED)
                {
                    launchers.Remove(launcher);
                    continue;
                }

                if (launcher.fireworkShots < 1)
                {
                    if (!TryReload(launcher))
                        continue;
                }

                if (TooHot(launcher))
                {
                    overheatLauncher = launcher;
                    continue;
                }

                found = true;
                break;
            }

            if (!found && overheatLauncher != null)
            {
                launcher = overheatLauncher;
                found = true;
            }

            controller.canFire = found;
            return found ? launcher : null;
        }

        public void OnAmmoChanged(ModulePartFirework launcher)
        {
            if (launcher == null || launcher.vessel != vessel)
                return;

            if (!HighLogic.LoadedSceneIsFlight)
                Debug.LogError("Tried to reload a firework launcher in the editor?");

            if (firing)
                return;

            controller.canFire = launcher.fireworkShots > 0 || AmmoCount > 0;
        }

        public bool TryReload(ModulePartFirework launcher)
        {
            if (canReload.TryGetValue(launcher.part.persistentId, out bool reloadable) && !reloadable)
                return false;

            var reloader = launcher.GetComponent<ModuleLauncherReload>();
            if (reloader == null)
                return false;

            bool reloaded = reloader.Reload();
            canReload[launcher.part.persistentId] = reloaded;

            return reloaded;
        }

        private bool TooHot(ModulePartFirework launcher)
        {
            return launcher.part.maxTemp - launcher.part.temperature < LaunchShell.shotHeat * controller.FWRoundBurst + 1;
        }

        private IEnumerator OverheatMonitor()
        {
            // This covers the scenario where all launchers are unavailable,
            // but some may become available when they cool down.

            // This design hints that it might be better if can fire was a property invoked by the ship controller?
            // But how to make it performant?

            var wait = new WaitForSeconds(ModuleShipController.combatUpdateInterval);

            while (true)
            {
                yield return wait;

                if (!controller.canFire && launchers.Count > 0)
                    FindLauncher(launchers);
            }
        }

        #endregion

        #region Debug

        private static Material debugProjectileMat;
        public static float debugProjectileSize = 0.5f;
        public static float debugLineSize = 0.2f;
        public static float debugLineAlpha = 0.3f;
        public static bool debugShell = true;

        private void DebugAim()
        {
            Vector3 origin = muzzleTransform.position;
            Color lime = new Color(196f / 255f, 208f / 255f, 164f / 255f, 1f);

            Line.Draw(origin, muzzleDirection, 15f, lime, debugLineAlpha, debugLineSize);
            Line.Draw(origin, lead.direction, 15f, Color.red, debugLineAlpha, debugLineSize);
            Line.Draw(origin, sasDirection, 15f, Color.blue, debugLineAlpha, debugLineSize);

            float error = Vector3.Angle(muzzleDirection, lead.direction);
            float tolerance = GetTolerance(Target.CoM - muzzleTransform.position, controller.targetSize, controller.accuracyTolerance);
            string text = $"Error: {error:F4}\n Tolerance: {tolerance:F4}";
            Debug.DrawDebugLabel(text, transform.position);

            if (debugShell)
                DebugShell(origin, lead.direction * launcher.shellVelocity, lead.time);
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
