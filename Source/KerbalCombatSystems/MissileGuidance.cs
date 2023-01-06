using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;

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

        // Missile guidance variables.

        private Vector3 targetVector;
        private Vector3 targetVectorNormal;
        private Vector3 relVel;
        private Vector3 relVelNrm;
        private float relVelmag;
        public float timeToHit;
        private Vector3 lead;
        private Vector3 interceptVector;
        private float accuracy;
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

        //private float targetSize;
        //private int partCount = -1;

        // Debugging line variables.

        LineRenderer targetLine, rvLine, interceptLine, thrustLine;
        //GameObject prediction;

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
                controller.target = target;

                ModuleShipController firerController = FindController(firer);
                if (firerController != null)
                {
                    // Don't require a ship controller and only consider ship-side limitations
                    // if a ship controller exists. It is probably more fun this way.

                    controller.side = firerController.side;

                    if (firerController.maxDetectionRange == 0)
                        firerController.UpdateDetectionRange();

                    if (FromTo(vessel, target).magnitude > firerController.maxDetectionRange)
                        yield break;
                }

                ModuleShipController targetController = FindController(target);
                if (targetController != null && !targetController.incomingWeapons.Contains(controller))
                    targetController.AddIncoming(controller);

                if (!KCSController.weaponsInFlight.Contains(controller) && !isInterceptor)
                    KCSController.weaponsInFlight.Add(controller);
            }
            else
                target = controller.target;


            // 1. Separate from firer.

            // find decoupler
            seperator = FindDecoupler(part);

            // Store the direction the ship is facing.
            Vector3 firerUp = vessel.ReferenceTransform.up;

            // todo:
            // electric charge check
            // fuel check
            // propulsion check

            if (seperator != null)
            {
                seperator.Separate();

                /*todo: safety checks for thruster backblast
                seperator.part.maxTemp = double.MaxValue;
                seperator.part.skinMaxTemp = double.MaxValue;
                seperator.part.tempExplodeChance = 0;*/
            }
            else
            {
                Debug.Log("[KCS]: Couldn't find decoupler.");
            }


            // 2. Initial setup.

            // turn on engines
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            engines.ForEach(e => e.Activate());

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
            commander.MakeReference();
            propulsionVector = -GetFireVector(engines, rcsThrusters, -vessel.ReferenceTransform.up);
            AlignReference(commander, propulsionVector.normalized);

            // Store the propulsion vector in local space for debugging.
            propulsionVector = vessel.transform.InverseTransformDirection(propulsionVector);

            // Setup flight controller.
            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.alignmentToleranceforBurn = isInterceptor ? 60 : 20;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.lerpAttitude = false;
            fc.throttleLerpRate = 99;
            fc.RCSPower = 20;
            fc.Drive();

            // Turn on reaction wheels.
            var wheels = vessel.FindPartModulesImplementing<ModuleReactionWheel>();
            wheels.ForEach(w => w.wheelState = ModuleReactionWheel.WheelState.Active);

            maxThrust = propulsionVector.magnitude;
            maxAcceleration = maxThrust / vessel.GetTotalMass();
            vessel.targetObject = target;
            shutoffDistance = isInterceptor ? 3 : 10;

            // If we are launching in the direction of the ship's propulsion, or in an enclosed space,
            // then we need to flag this so the ship can throttle down temporarily.
            // todo: wrap this function up with the horizontal launch raycasts

            int frontLaunch = 0;

            if (Vector3.Angle(vessel.ReferenceTransform.up, firerUp) < 50)
                frontLaunch = 1;

            if (frontLaunch == 0)
            {
                Vector3 horizontal;
                Transform vRef = vessel.ReferenceTransform;
                Ray enclosedRay = new Ray(vessel.CoM, Vector3.zero);
                frontLaunch = 2;

                for (int i = 0; i < 4; i++)
                {
                    horizontal = Quaternion.AngleAxis(360f * (i / 4f), vRef.up) * vRef.forward;
                    enclosedRay.direction = horizontal;

                    if (!RayIntersectsVessel(firer, enclosedRay))
                    {
                        frontLaunch = 0;
                        break;
                    }
                }
            }

            controller.frontLaunch = frontLaunch;

            // Had to move this because frontLaunch has to be set before the thread is paused,
            // and frontLaunch requires GetFireVector to modify the reference transform. Not sure if
            // GetFireVector requires a wait? It works as expected in tests.
            yield return new WaitForFixedUpdate();


            // 3. Start moving away from firer.

            // Check if it's a horizontal launch.

            Ray launchRay = new Ray(vessel.ReferenceTransform.position, vessel.ReferenceTransform.up);
            bool horizontalLaunch = RayIntersectsVessel(firer, launchRay);

            if (horizontalLaunch)
            {
                Vector3 horizontal = firer.ReferenceTransform.forward;
                bool foundExit = false;
                Vector3 start = Vector3.ProjectOnPlane(firer.ReferenceTransform.forward, vessel.ReferenceTransform.up);

                // First check directions at 90 degrees to the firer's roll direction.
                for (int i = 0; i < 4; i++)
                {
                    horizontal = Quaternion.AngleAxis(360f * (i / 4f), vessel.ReferenceTransform.up) * start;
                    launchRay.direction = horizontal;

                    if (foundExit = !RayIntersectsVessel(firer, launchRay))
                        break;
                }

                // If we still can't find an exit, check diagonally.
                // We do this second to prioritise straight exits from large openings.
                if (!foundExit)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        horizontal = Quaternion.AngleAxis(360 * i / 4 + 45, vessel.ReferenceTransform.up) * firer.ReferenceTransform.forward;
                        launchRay.direction = horizontal;

                        if (!RayIntersectsVessel(firer, launchRay))
                            break;
                    }
                }

                fc.RCSVector = horizontal.normalized * 200000f; // idk
                float checkInterval = 0.1f;
                float lastChecked = 0;

                while (horizontalLaunch)
                {
                    yield return new WaitForFixedUpdate();

                    fc.Drive();

                    if (Time.time - lastChecked > checkInterval)
                    {
                        lastChecked = Time.time;

                        launchRay.origin = vessel.ReferenceTransform.position;
                        launchRay.direction = vessel.ReferenceTransform.up;
                        horizontalLaunch = CylinderIntersectsVessel(firer, launchRay, 1.25f / 2);
                    }
                }

                yield return new WaitForSeconds(igniteDelay);
            }
            else
            {
                fc.RCSVector = vessel.ReferenceTransform.up;

                yield return new WaitForSeconds(igniteDelay);

                if (!isInterceptor)
                {
                    // Support save files and craft saved before changing to a percentage.
                    if (controller.pulseThrottle < 1)
                        controller.pulseThrottle *= 100;

                    fc.throttle = controller.pulseThrottle / 100f;
                    fc.Drive();

                    yield return new WaitForSeconds(controller.pulseDuration);

                    fc.throttle = 0;
                }
                else
                {
                    fc.throttle = 1;
                }

                fc.RCSVector = Vector3.zero;
                fc.Drive();
            }


            // 4. Get line of sight to the target.

            Ray targetRay = new Ray();
            targetRay.origin = vessel.CoM;
            targetRay.direction = target.CoM - vessel.CoM;
            bool lineOfSight = !RayIntersectsVessel(firer, targetRay);

            Vector3 sideways;
            bool clear = false;
            float previousTolerance = fc.alignmentToleranceforBurn;

            while (!lineOfSight)
            {
                yield return new WaitForSeconds(0.1f);
                if (target == null) break;

                if (!clear) // Latch clear once true.
                {
                    // We don't have line of sight with the target yet, but are we clear of the ship?

                    sideways = vessel.transform.forward;
                    int blockedCount = 0;

                    for (int i = 0; i < 4; i++)
                    {
                        targetRay.origin = vessel.CoM;
                        sideways = Vector3.Cross(sideways, vessel.ReferenceTransform.up);
                        targetRay.direction = sideways;

                        if (RayIntersectsVessel(firer, targetRay))
                            blockedCount++;

                        clear = blockedCount < 2;
                        if (!clear) break;
                    }
                }

                if (clear)
                {
                    // We are clear of the ship but it is blocking line of sight with the target.
                    // Fly towards the target in an arc around the ship until we have line of sight.

                    controller.launched = true; // Trigger early.

                    fc.attitude = Vector3.ProjectOnPlane(FromTo(vessel, target).normalized, FromTo(vessel, firer).normalized);
                    fc.throttle = 0.5f;
                    fc.alignmentToleranceforBurn = 60;
                    fc.Drive();
                }
                else if (frontLaunch != 0)
                {
                    // We are exiting a front facing weapons bay, match the ship's rotation and acceleration until clear of the bay.

                    if (frontLaunch == 1)
                        fc.attitude = firer.ReferenceTransform.up;

                    fc.throttle = firer.acceleration.magnitude > 0 ? 1 : 0;
                    fc.Drive();
                }

                // Do we have line of sight with the target vessel?
                targetRay.origin = vessel.CoM;
                targetRay.direction = target.CoM - vessel.CoM;
                lineOfSight = !RayIntersectsVessel(firer, targetRay);
            }

            fc.alignmentToleranceforBurn = previousTolerance;


            // 5. Finish setting up the missile.

            // Remove end cap
            List<ModuleDecouplerDesignate> decouplers = FindDecouplerChildren(vessel.rootPart);
            decouplers.ForEach(d => d.Separate());

            List<ModuleProceduralFairing> fairings = vessel.FindPartModulesImplementing<ModuleProceduralFairing>();
            fairings.ForEach(f => f.DeployFairing());

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);
            interceptLine = KCSDebug.CreateLine(Color.cyan);
            thrustLine = KCSDebug.CreateLine(new Color(255f / 255f, 165f / 255f, 0f, 1f)); //orange

            // Rename the new vessel.
            string oldName = vessel.vesselName;
            string missileName = controller.weaponCode == "" ? "Missile" : controller.weaponCode;
            string firerName = ShortenName(firer.vesselName);
            vessel.vesselName = !isInterceptor ? $"{missileName} ({firerName} >> {ShortenName(target.vesselName)})" : $"Interceptor ({firerName})";
            GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel, string>(vessel, oldName, vessel.vesselName));

            engageAutopilot = true;
            controller.launched = true;

            //var targetController = FindController(target);
            //targetSize = targetController != null ? targetController.averagedSize : AveragedSize(target);
            //partCount = vessel.parts.Count;

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

            yield break;
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
            maxAcceleration = maxThrust / vessel.GetTotalMass();

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

            fc.Drive();


            // Update debug lines.
            if (KCSDebug.showLines)
            {
                Vector3 origin = vessel.CoM;
                KCSDebug.PlotLine(new[] { origin, origin + (relVelNrm * 15) }, rvLine);
                KCSDebug.PlotLine(new Vector3[] { origin, origin + vessel.transform.TransformDirection(propulsionVector)}, thrustLine);

                if (isInterceptor)
                    KCSDebug.PlotLine(new[] { origin, origin + targetVector }, interceptLine);
                else
                    KCSDebug.PlotLine(new[] { origin, origin + targetVector }, targetLine);
                //if (isInterceptor)
                //    prediction.transform.position = predictedPosWorld;
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

        public void FixedUpdate()
        {
            if (engageAutopilot) UpdateGuidance();

            // Works some of the time. Fix if needed in future.

            //if (!controller.hit 
            //    && target != null 
            //    && FromTo(vessel, target).magnitude < Mathf.Max(targetSize * 3, 5))
            //{
            //    int pc = vessel.parts.Count;
            //    if (pc < partCount)
            //        OnHit();

            //    partCount = pc;
            //}
        }

        public void OnDestroy()
        {
            KCSDebug.DestroyLine(rvLine);
            KCSDebug.DestroyLine(targetLine);
            KCSDebug.DestroyLine(interceptLine);
            KCSDebug.DestroyLine(thrustLine);
            Destroy(fc);
            //Destroy(prediction);
        }

        public void StopGuidance()
        {
            engageAutopilot = false;
            controller.missed = true;
            OnDestroy();
        }

        private void OnHit()
        {
            controller.hit = true;

            if (!isInterceptor)
            {
                string missileName = controller.weaponCode == "" ? "missile" : controller.weaponCode + " missile";
                KCSController.Log($"%1 was hit by a {missileName} fired from %2", target, firer);
            }
            else
                KCSController.Log("%1 intercepted a missile", firer);
        }
    }
}
