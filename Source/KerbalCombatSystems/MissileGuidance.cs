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
        private ModuleWeaponController targetWeapon;
        private bool isInterceptor;
        private int shutoffDistance;

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
        private float targetSize;

        // Components

        private KCSFlightController fc;
        private ModuleDecouplerDesignate seperator;
        private ModuleWeaponController controller;
        private List<ModuleEngines> engines;
        private ModuleEngines mainEngine;
        private Part mainEnginePart;
        private int partCount = -1;

        // Debugging line variables.

        LineRenderer targetLine, rvLine, interceptLine;
        //GameObject prediction;

        private IEnumerator Launch()
        {
            // 1. Separate from firer.

            // find decoupler
            seperator = FindDecoupler(part, "Default", false);

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

            // todo: use firevector instead.
            // We are launching in the direction of the ship's propulsion, we need to flag this so the ship can throttle down temporarily.
            bool frontLaunch = Vector3.Angle(vessel.ReferenceTransform.up, firerUp) < 50;
            controller.frontLaunch = frontLaunch;


            // 2. Initial setup.

            // turn on engines
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            engines.ForEach(e => e.Activate());
            mainEngine = engines.First();
            mainEnginePart = mainEngine.part;

            // Setup flight controller.
            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.alignmentToleranceforBurn = isInterceptor ? 60 : 20;
            fc.lerpAttitude = false;
            fc.throttleLerpRate = 99;
            fc.RCSPower = 20;
            fc.Drive();

            //get an onboard probe core orientation to control from
            //commented out due to people building dodgy missiles with incorrect probe core orientations
            //best option is using the direction of the missiles thrust vector but may be resource intensive
            //FindCommand(vessel).MakeReference();

            //temporary measure to use simplest version but still one that causes bugs
            fc.attitude = vessel.ReferenceTransform.up;

            //enable RCS for translation
            if (!vessel.ActionGroups[KSPActionGroup.RCS])
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

            // turn on rcs thrusters
            var Thrusters = vessel.FindPartModulesImplementing<ModuleRCS>();
            Thrusters.ForEach(t => t.rcsEnabled = true);

            // Turn on reaction wheels.
            var wheels = vessel.FindPartModulesImplementing<ModuleReactionWheel>();
            wheels.ForEach(w => w.wheelState = ModuleReactionWheel.WheelState.Active);

            maxThrust = GetMaxThrust(vessel);
            maxAcceleration = maxThrust / vessel.GetTotalMass();

            vessel.targetObject = target;


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
                    fc.throttle = controller.pulseThrottle;
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
            targetRay.origin = vessel.ReferenceTransform.position;
            targetRay.direction = target.transform.position - vessel.transform.position;
            bool lineOfSight = !RayIntersectsVessel(firer, targetRay);

            Vector3 sideways;
            bool clear = false;
            float previousTolerance = fc.alignmentToleranceforBurn;

            while (!lineOfSight)
            {
                yield return new WaitForSeconds(0.1f);
                if (target == null) break;

                targetRay.origin = vessel.ReferenceTransform.position;
                targetRay.direction = target.transform.position - vessel.transform.position;

                if (!clear) // Latch clear once true.
                {
                    // We don't have line of sight with the target yet, but are we clear of the ship?

                    sideways = vessel.transform.forward;
                    int blockedCount = 0;

                    for (int i = 0; i < 4; i++)
                    {
                        targetRay.origin = vessel.ReferenceTransform.position;
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
                else if (frontLaunch)
                {
                    // We are exiting a front facing weapons bay, match the ship's rotation and acceleration until clear of the bay.

                    fc.attitude = firer.ReferenceTransform.up;
                    fc.throttle = firer.acceleration.magnitude > 0 ? 1 : 0;
                    fc.Drive();
                }

                // Do we have line of sight with the target vessel?
                lineOfSight = !RayIntersectsVessel(firer, targetRay);
            }

            fc.alignmentToleranceforBurn = previousTolerance;


            // 5. Finish setting up the missile.

            // Remove end cap. todo: will need to change to support cluster missiles.
            List<ModuleDecouple> decouplers = vessel.FindPartModulesImplementing<ModuleDecouple>();
            decouplers.ForEach(d => d.Decouple());

            List<ModuleProceduralFairing> fairings = vessel.FindPartModulesImplementing<ModuleProceduralFairing>();
            fairings.ForEach(f => f.DeployFairing());

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);
            interceptLine = KCSDebug.CreateLine(Color.cyan);

            // enable autopilot
            engageAutopilot = true;

            shutoffDistance = isInterceptor ? 3 : 10;
            var targetController = FindController(target);
            targetSize = targetController != null ? targetController.averagedSize : AveragedSize(target);
            partCount = vessel.parts.Count;

            string oldName = vessel.vesselName;
            string missileName = controller.weaponCode == "" ? "Missile" : controller.weaponCode;
            string firerName = ShortenName(firer.vesselName);
            vessel.vesselName = !isInterceptor ? $"{missileName} ({firerName} >> {ShortenName(target.vesselName)})" : $"Interceptor ({firerName})";
            GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel, string>(vessel, oldName, vessel.vesselName));

            controller.launched = true;

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

            accuracy = Vector3.Dot(targetVectorNormal, relVelNrm);
            if (targetVector.magnitude < shutoffDistance
                || ((mainEngine == null
                || !mainEngine.isOperational
                || mainEngine == null
                || mainEnginePart.vessel != vessel)
                && accuracy < 0.99))
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
            Vector3 origin = vessel.CoM;
            KCSDebug.PlotLine(new[] { origin, origin + (relVelNrm * 15) }, rvLine);

            if (isInterceptor)
                KCSDebug.PlotLine(new[] { origin, origin + targetVector }, interceptLine);
            else
                KCSDebug.PlotLine(new[] { origin, origin + targetVector }, targetLine);

            //if (isInterceptor)
            //    prediction.transform.position = predictedPosWorld;
        }

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();

            if (controller.target == null && vessel.targetObject == null) return;
            target = controller.target ?? vessel.targetObject.GetVessel();

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
