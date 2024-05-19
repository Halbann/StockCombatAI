using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using static KerbalCombatSystems.Utils;

namespace KerbalCombatSystems
{
    public class ModuleMissile : ModuleWeapon
    {
        // Settings

        public bool engageAutopilot = false;
        private float maxThrust;
        private Vessel target;
        private Vessel firer;
        private float igniteDelay;
        private float terminalVelocity;
        private bool isInterceptor;
        private int shutoffDistance;
        private ModuleWeaponController targetWeapon;
        private bool separated = false;


        // Missile guidance variables.

        public string phase = "Pre-launch";
        private Vector3 targetVector;
        private Vector3 targetVectorNormal;
        private Vector3 relVel;
        private Vector3 relVelNrm;
        private float relVelmag;
        public float timeToHit;
        private Vector3 lead;
        private Vector3 interceptVector;
        public float accuracy;
        private bool drift;
        public float maxAcceleration;
        private Vector3 rcs;
        private Vector3 propulsionVector;


        // Components

        private KCSFlightController fc;
        private ModuleDecouplerDesignate seperator;
        private ModuleWeaponController controller;
        private List<ModuleRCSFX> rcsThrusters;
        private List<ModuleEngines> engines;


        // Debugging variables.

        public float Throttle => fc?.throttleActual ?? 0;
        LineRenderer targetLine, rvLine, interceptLine, thrustLine;


        private IEnumerator Launch()
        {
            // 0. Failsafes for manual fire.
            // todo: some of this should probably transferred to the weapon controller.

            if (controller.target == null && vessel.targetObject == null)
                yield break;

            if (controller.target == null)
            {
                // The missile was fired manually.

                target = vessel.targetObject.GetVessel();

                // We don't require a ship controller and only consider ship-side limitations
                // if a ship controller exists. It is probably more fun this way.

                ModuleShipController firerController = FindController(firer);
                if (firerController != null)
                {
                    controller.side = firerController.side;

                    if (firerController.maxLockRange == 0)
                        firerController.UpdateLockRange();

                    if (VesselDistance(vessel, firer) > firerController.maxLockRange)
                    {
                        FlightManager.OnWeaponFailed($"{ShortenName(target.GetName())} is out of lock range.");

                        yield break;
                    }
                }

                controller.target = target;


                // Meta/flight manager.

                ModuleShipController targetController = FindController(target);
                if (targetController != null && !targetController.incomingWeapons.Contains(controller))
                    targetController.AddIncoming(controller);

                if (!FlightManager.weaponsInFlight.Contains(controller) && !isInterceptor)
                    FlightManager.weaponsInFlight.Add(controller);
            }
            else
            {
                target = controller.target;
            }


            // 1. Separate from firer.

            // Store the firer's direction for later use.
            Vector3 firerUp = vessel.ReferenceTransform.up;

            // todo: resource checks

            // Separate.
            seperator = FindDecoupler(part);
            seperator?.Separate();


            // 2. Initial setup.

            // Enable resources.
            vessel.parts.ForEach(p => p.Resources.ToList().ForEach(r => r.flowState = true));

            // Turn on all engines in the highest stage.
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            if (engines.Count > 0)
            {
                int highestStage = engines.Max(e => e.part.inverseStage); // Stages are numbered so that 0 = last stage.
                engines = engines.FindAll(e => e.part.inverseStage == highestStage);
                engines.ForEach(e => e.Activate());
            }

            // Get and enable RCS thrusters.
            rcsThrusters = vessel.FindPartModulesImplementing<ModuleRCSFX>();
            rcsThrusters.ForEach(t => t.rcsEnabled = true);

            // Remove unused thrusters.
            engines.RemoveAll(e => !e.EngineIgnited || e.flameout);
            rcsThrusters.RemoveAll(r => !r.useThrottle || !r.isEnabled || r.flameout);

            // Enable RCS group
            if (!vessel.ActionGroups[KSPActionGroup.RCS])
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

            // Get a probe core and align its reference transform with the propulsion vector.
            ModuleCommand commander = FindCommand(vessel);
            if (commander == null)
                yield break;

            commander.MakeReference();
            propulsionVector = -GetFireVector(engines, rcsThrusters, -vessel.ReferenceTransform.up);
            AlignReference(commander, propulsionVector.normalized);

            // Store the propulsion vector in local space for debugging.
            propulsionVector = vessel.transform.InverseTransformDirection(propulsionVector);

            // Setup flight controller.
            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.alignmentToleranceforBurn = isInterceptor ? 80 : 25;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.lerpAttitude = false;
            fc.lerpThrottle = false;
            fc.RCSPower = 20;
            fc.Drive();

            // Turn on reaction wheels.
            var wheels = vessel.FindPartModulesImplementing<ModuleReactionWheel>();
            wheels.ForEach(w => w.wheelState = ModuleReactionWheel.WheelState.Active);

            maxThrust = propulsionVector.magnitude;
            maxAcceleration = maxThrust / vessel.GetTotalMass();
            vessel.targetObject = target;
            shutoffDistance = isInterceptor ? 3 : 10;

            // Rename the new vessel.
            string oldName = vessel.vesselName;
            string missileName = controller.weaponCode == "" ? "Missile" : controller.weaponCode;
            string firerName = ShortenName(firer.vesselName);
            vessel.vesselName = !isInterceptor ? $"{missileName} ({firerName} >> {ShortenName(target.vesselName)})" : $"Interceptor ({firerName})";
            GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel, string>(vessel, oldName, vessel.vesselName));

            // Enable continuous collision detection.
            MakeRigidbodiesContinuous();

            phase = "Separated";
            separated = true;


            // 2.5 Check launch type.

            // If we are launching in the direction of the ship's propulsion, or in an enclosed space,
            // then we need to flag this so the ship can throttle down temporarily.

            controller.launchType = CheckLaunchType(firerUp);


            // Had to move this because frontLaunch has to be set before the thread is paused,
            // and frontLaunch requires GetFireVector to modify the reference transform. Not sure if
            // GetFireVector requires a wait? It works as expected in tests.
            yield return new WaitForFixedUpdate();


            // 3. Start moving away from firer.

            Ray launchRay = new Ray(vessel.ReferenceTransform.position, vessel.ReferenceTransform.up);

            if (RayIntersectsVessel(firer, launchRay))
            {
                // A horizontal launch is when the missile is blocked from moving forwards.
                // Therefore we need to move the missile horizontally until we can move forwards.

                phase = "Horizontal Exit";
                yield return StartCoroutine(HorizontalExit());
            }
            else
            {
                // Normal away procedure.
                // We are able to leave the ship by simply moving forwards.

                // Skip the kick phase in poor conditions and go straight to clearing.
                if (!(controller.launchType != LaunchType.Radial && firer.perturbation.magnitude > 1))
                {
                    phase = "Kick";
                    yield return StartCoroutine(Kick());
                }
            }

            phase = "Clearing";
            yield return StartCoroutine(GetClearance());

            controller.launched = true;


            // 4. Get line of sight to the target.

            phase = "Acquiring LOS";
            yield return StartCoroutine(AcquireLOS());


            // 5. Finish setting up the missile.

            // Remove end cap.
            List<ModuleDecouplerDesignate> decouplers = FindDecouplerChildren(vessel.rootPart);
            decouplers.ForEach(d => d.Separate());

            // Deploy fairings.
            List<ModuleProceduralFairing> fairings = vessel.FindPartModulesImplementing<ModuleProceduralFairing>();
            fairings.ForEach(f => f.DeployFairing());

            // Enter guidance.
            phase = "Guidance";
            engageAutopilot = true;

            SetupDebugVisuals();
        }

        private void SetupDebugVisuals()
        {
            // Debug lines
            targetLine = Debug.CreateLine(Color.magenta);
            rvLine = Debug.CreateLine(Color.green);
            interceptLine = Debug.CreateLine(Color.cyan);
            thrustLine = Debug.CreateLine(new Color(255f / 255f, 165f / 255f, 0f, 1f)); //orange


            // Show a sphere where the interceptor thinks it will hit the target.

            //if (isInterceptor)
            //{
            //    prediction = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            //    var mr = prediction.GetComponent<MeshRenderer>();

            //    Material sphereMat = new Material(Shader.Find("Unlit/Color"));
            //    sphereMat.color = Color.magenta;

            //    mr.material = sphereMat;

            //    prediction.transform.localScale = prediction.transform.localScale * 6;
            //    Destroy(prediction.GetComponent<SphereCollider>());
            //}
        }

        private void UpdateGuidance()
        {
            if (target == null || (isInterceptor && (targetWeapon == null || targetWeapon.missed)))
            {
                StopGuidance();
                return;
            }

            targetVector = target.CoM - vessel.CoM;
            relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
            relVelNrm = relVel.normalized;
            relVelmag = relVel.magnitude;
            maxAcceleration = maxThrust / (float)vessel.totalMass;

            if (!isInterceptor)
            {
                timeToHit = SolveTime(targetVector.magnitude, maxAcceleration, Vector3.Dot(relVel, targetVector.normalized));
                lead = (relVelNrm * -1) * timeToHit * relVelmag;
                interceptVector = (target.CoM + lead) - vessel.CoM;

                controller.timeToHit = timeToHit;
            }
            else
            {
                Vector3 acceleration = vessel.ReferenceTransform.up * maxAcceleration;
                Vector3 relvela = target.GetObtVelocity() - vessel.GetObtVelocity();

                timeToHit = ClosestTimeToCPA(targetVector, relvela, target.acceleration - acceleration, 30);
                interceptVector = PredictPosition(targetVector, relvela, target.acceleration - acceleration * 0.5f, timeToHit);
                interceptVector = interceptVector.normalized;

                if (Vector3.Dot(interceptVector, targetVector.normalized) < 0)
                    interceptVector = targetVector.normalized;

                controller.timeToHit = timeToHit;
            }

            targetVectorNormal = interceptVector.normalized;


            // Terminal warhead separation check. The design isn't ready yet.

            /*if (!terminal && timeToHit < controller.terminalTime)
            {
                terminal = true;

                FindDecouplerChildren(vessel.rootPart, "Warhead").ForEach(s => s.Separate());

                engines = vessel.FindPartModulesImplementing<ModuleEngines>();
                engines.ForEach(e => e.Activate());
                engines.RemoveAll(e => !e.EngineIgnited || e.flameout);

                ModuleCommand commander = FindCommand(vessel);
                propulsionVector = -GetFireVector(engines, rcsThrusters, -vessel.ReferenceTransform.up);
                AlignReference(commander, propulsionVector.normalized);

                maxThrust = propulsionVector.magnitude;
                maxAcceleration = maxThrust / vessel.GetTotalMass();

                propulsionVector = vessel.transform.InverseTransformDirection(propulsionVector);
            }*/


            //remove engines and thrusters that have been enabled and are dry, destroyed, or disconnected
            engines.RemoveAll(e => e == null || (e.EngineIgnited && e.flameout) || e.vessel != vessel);
            rcsThrusters.RemoveAll(r =>  r == null || !r.useThrottle || (r.isEnabled && r.flameout) || r.vessel != vessel);

            accuracy = Vector3.Dot(targetVectorNormal, relVelNrm);
            if (targetVector.magnitude < shutoffDistance || (!engines.Any() && !rcsThrusters.Any()) && accuracy < 0.99)
            {
                StopGuidance();
                return;
            }

            drift = accuracy > 0.999999
                && (Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity || isInterceptor);

            rcs = Vector3.ProjectOnPlane(relVel, vessel.ReferenceTransform.up) * -1;

            fc.throttle = drift ? 0 : 1;
            fc.attitude = targetVectorNormal;
            fc.RCSVector = rcs;


            // Update debug lines.
            if (Debug.Visible)
            {
                Vector3 origin = vessel.CoM;

                Debug.PlotLine(new[] { origin, origin + (relVelNrm * 15) }, rvLine);
                Debug.PlotLine(new Vector3[] { origin, origin + vessel.transform.TransformDirection(propulsionVector)}, thrustLine);

                if (isInterceptor)
                    Debug.PlotLine(new[] { origin, origin + targetVector }, interceptLine);
                else
                    Debug.PlotLine(new[] { origin, origin + targetVector }, targetLine);
            }
        }

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();

            terminalVelocity = controller.terminalVelocity;
            isInterceptor = controller.isInterceptor;
            targetWeapon = controller.targetWeapon;
            firer = vessel;
            igniteDelay = controller.igniteDelay;
        }

        public override void Fire()
        {
            StartCoroutine(Launch());
        }

        internal void FixedUpdate()
        {
            if (engageAutopilot)
                UpdateGuidance();

            if (separated)
                fc?.Drive();
        }

        public void OnDestroy()
        {
            Debug.DestroyLine(rvLine);
            Debug.DestroyLine(targetLine);
            Debug.DestroyLine(interceptLine);
            Debug.DestroyLine(thrustLine);
            Destroy(fc);
            fc = null;
        }

        public void StopGuidance()
        {
            engageAutopilot = false;
            controller.missed = true;
            OnDestroy();

            // Mark the vessel as debris
            vessel.vesselType = VesselType.Debris;
            GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel, string>(vessel, vessel.name, vessel.name));
        }

        private void MakeRigidbodiesContinuous()
        {
            foreach (Part p in vessel.parts)
            {
                if (part.physicalSignificance == Part.PhysicalSignificance.NONE)
                    continue;

                Rigidbody rb = p.Rigidbody;
                if (rb == null)
                    continue;

                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        private IEnumerator HorizontalExit()
        {
            // Sequence responsible for moving a fowards blocked missile horizontally until it can move forwards.


            Vector3 horizontal = firer.ReferenceTransform.forward;
            bool foundExit = false;
            Vector3 start = Vector3.ProjectOnPlane(firer.ReferenceTransform.forward, vessel.ReferenceTransform.up);

            Ray ray = new Ray(vessel.ReferenceTransform.position, Vector3.zero);

            // First check directions at 90 degrees to the firer's roll direction.
            for (int i = 0; i < 4; i++)
            {
                horizontal = Quaternion.AngleAxis(360f * (i / 4f), vessel.ReferenceTransform.up) * start;
                ray.direction = horizontal;

                if (foundExit = !RayIntersectsVessel(firer, ray))
                    break;
            }

            // If we still can't find an exit, check diagonally.
            // We do this second to prioritise straight exits from large openings.
            if (!foundExit)
            {
                for (int i = 0; i < 4; i++)
                {
                    horizontal = Quaternion.AngleAxis(360 * i / 4 + 45, vessel.ReferenceTransform.up) * firer.ReferenceTransform.forward;
                    ray.direction = horizontal;

                    if (!RayIntersectsVessel(firer, ray))
                        break;
                }
            }

            // Translate in the exit direction until forwards path is clear.
            fc.RCSVector = horizontal.normalized * 200000f; // idk
            fc.attitude = vessel.ReferenceTransform.up;
            fc.Drive();

            float checkInterval = 0.1f;
            float lastChecked = 0;
            bool clear = false;
            var wait = new WaitForFixedUpdate();

            while (!clear)
            {
                yield return wait;

                if (Time.time - lastChecked > checkInterval)
                {
                    lastChecked = Time.time;

                    ray.origin = vessel.ReferenceTransform.position;
                    ray.direction = vessel.ReferenceTransform.up;
                    clear = !CylinderIntersectsVessel(firer, ray, 1.25f / 2);
                }
            }

            // This little wait should give some margin for us to be sure that we're clear to move forwards.
            fc.Stability(true);
            yield return new WaitForSeconds(igniteDelay);
            fc.Stability(false);

            fc.RCSVector = Vector3.zero;
            fc.Drive();
        }

        private IEnumerator Kick()
        {
            // Sequence responsible for performing a kick.

            fc.RCSVector = vessel.ReferenceTransform.up * 2;
            fc.attitude = vessel.ReferenceTransform.up;

            if (!isInterceptor)
            {
                fc.throttle = 0;
                fc.Drive();


                // Support save files and craft saved before changing to a percentage.
                if (controller.pulseThrottle < 1)
                    controller.pulseThrottle *= 100;

                yield return new WaitForSeconds(igniteDelay);

                fc.throttle = controller.pulseThrottle / 100f;
                fc.Drive();

                yield return new WaitForSeconds(controller.pulseDuration);

                fc.throttle = 0;
            }
            else
            {
                // Interceptors don't use a kick, they just go!
                fc.throttle = 1;
            }

            fc.Drive();
        }

        private bool CheckClearance()
        {
            // Perform raycasts in all cardinal directions to check if the missile is clear of the ship.

            Vector3 start = firer.ReferenceTransform.forward;
            Ray ray = new Ray(vessel.ReferenceTransform.position, Vector3.zero);
            bool hit = false;

            // First check directions at 90 degrees to the firer's roll direction.
            for (int i = 0; i < 4; i++)
            {
                ray.direction = Quaternion.AngleAxis(360f * (i / 4f), vessel.ReferenceTransform.up) * start;
                hit = RayIntersectsVessel(firer, ray);

                if (hit) break;
            }

            return !hit;
        }

        private IEnumerator GetClearance()
        {
            // Sequence responsible for getting a separated missile clear of the ship.

            float checkTime = 0;
            float timeLimit = Time.fixedTime + 5f;
            float checkInterval = 0.1f;
            bool clear = false;
            var wait = new WaitForFixedUpdate();

            // Full throttle if the ship is accelerating, otherwise use the kick throttle.
            double firerAcceleration = firer?.perturbation.magnitude ?? 0;
            float throttle = firerAcceleration > 1 ? 1f : controller.pulseThrottle / 100f;
            fc.throttle = throttle;
            fc.attitude = vessel.ReferenceTransform.up;

            while (true)
            {
                if (Time.fixedTime > checkTime)
                {
                    if (firer == null)
                        yield break;

                    checkTime = Time.fixedTime + checkInterval;
                    clear = CheckClearance();
                }

                if (clear || Time.fixedTime > timeLimit)
                    break;

                yield return wait;
            }

            fc.RCSVector = Vector3.zero;
            fc.throttle = 0;
            fc.Drive();
        }

        private IEnumerator AcquireLOS()
        {
            // Sequence responsible for taking a missile from a position where it is
            // clear of the ship, to having permanent line of sight with the target.

            float previousTolerance = fc.alignmentToleranceforBurn;
            float losManoeuvreBurnTolerance = 60;
            var wait = new WaitForFixedUpdate();
            Ray targetRay = new Ray();
            Vector3 firerCentre, toTarget, toFirer, proj, firerToTarget, sphereEdge;

            // We can't rely on .vesselSize because it sometimes expands to hundreds of metres after losing parts.
            Bounds vesselBounds = VesselBounds.GetBoundsLocal(firer);
            float firerRadius = vesselBounds.size.magnitude / 2;

            Debug.DrawVesselBounds(firer, true);

            while (true)
            {
                if (target == null || firer == null)
                    break;

                // Does our path intersect a safety bubble around the firer?

                targetRay.origin = vessel.CoM;
                targetRay.direction = target.CoM - vessel.CoM;
                firerCentre = firer.transform.TransformPoint(vesselBounds.center);

                if (RayIntersectSphere(targetRay, firerCentre, firerRadius))
                {
                    toTarget = FromTo(vessel, target).normalized;
                    toFirer = Vector3.Normalize(firerCentre - vessel.CoM);

                    if (Vector3.Distance(firerCentre, vessel.CoM) < firerRadius)
                    {
                        // We are inside the safety bubble.
                        // Burn towards the target and away from the centre of the bubble.

                        fc.attitude = Vector3.Slerp(toTarget, toFirer * -1, 0.25f);
                    }
                    else
                    {
                        // We are behind the safety bubble as seen from the target.
                        // Burn towards the edge of the bubble in the direction of the target.

                        proj = Vector3.ProjectOnPlane(toTarget, toFirer).normalized;
                        firerToTarget = Vector3.Normalize(target.CoM - firerCentre);
                        sphereEdge = firerCentre + Vector3.ProjectOnPlane(proj, firerToTarget).normalized * firerRadius;

                        fc.attitude = Vector3.Normalize(sphereEdge - vessel.CoM);   
                    }

                    fc.throttle = 0.5f;
                    fc.alignmentToleranceforBurn = losManoeuvreBurnTolerance;

                    yield return wait;
                }
                else
                {
                    // We have LOS.

                    break;
                }
            }

            fc.alignmentToleranceforBurn = previousTolerance;
            fc.RCSVector = Vector3.zero;
            fc.throttle = 0;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.Drive();

            Debug.DrawVesselBounds(firer, false);
        }

        private LaunchType CheckLaunchType(Vector3 firerDirection)
        {
            // Front launch - aligned with the direction of the ship's propulsion.
            // Radial launch - not aligned with the direction of the ship's propulsion.
            // Enclosed launch - the missile is not aligned with propulsion but is walled in on all sides.

            LaunchType launchType;

            if (Vector3.Angle(vessel.ReferenceTransform.up, firerDirection) < 50)
            {
                launchType = LaunchType.Front;
            }
            else
            {
                Vector3 horizontal;
                Transform vRef = vessel.ReferenceTransform;
                Ray enclosedRay = new Ray(vessel.CoM, Vector3.zero);
                launchType = LaunchType.Enclosed;

                for (int i = 0; i < 4; i++)
                {
                    horizontal = Quaternion.AngleAxis(360f * (i / 4f), vRef.up) * vRef.forward;
                    enclosedRay.direction = horizontal;

                    // If the raycast doesn't hit the firer then we are not in an enclosed space.
                    if (!RayIntersectsVessel(firer, enclosedRay))
                    {
                        launchType = LaunchType.Radial;
                        break;
                    }
                }
            }

            return launchType;
        }

        /*private void OnHit()
        {
            controller.hit = true;

            if (!isInterceptor)
            {
                string missileName = controller.weaponCode == "" ? "missile" : controller.weaponCode + " missile";
                FlightManager.Log($"%1 was hit by a {missileName} fired from %2", target, firer);
            }
            else
                FlightManager.Log("%1 intercepted a missile", firer);
        }*/

        /*private void HitCheck()
        {
            // todo: Hit check for battle logs. Works some of the time. Fix if needed in future.
            // Call in fixedupdate.
            // get part count once during launch.

            if (!controller.hit
                && target != null
                && FromTo(vessel, target).magnitude < Mathf.Max(targetSize * 3, 5))
            {
                int pc = vessel.parts.Count;
                if (pc < partCount)
                    OnHit();

                partCount = pc;
            }
        }*/
    }

    // The distinction is necessary for the ship to know how to behave while firing.
    public enum LaunchType
    {
        Radial,
        Front,
        Enclosed,
    }
}
