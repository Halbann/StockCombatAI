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
    public class ModuleMissile : ModuleWeapon
    {
        // Settings

        public bool engageAutopilot = false;
        private Vessel target;
        private Vessel firer;
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

        //private float correctionAmount;
        //private Vector3 correction;

        public float timeToHit;
        private Vector3 lead;
        private Vector3 predictedPos;
        private Vector3 interceptVector;

        private float accuracy;
        private bool drift;
        private float maxAcceleration;
        private Vector3 rcs;

        // Components

        private KCSFlightController fc;
        //private ModuleDecouple decoupler;
        private Seperator seperator;
        private ModuleWeaponController controller;
        private List<ModuleEngines> engines;
        private ModuleEngines mainEngine;
        private Part mainEnginePart;

        // Debugging line variables.

        LineRenderer targetLine, rvLine, interceptLine, rcsLine;

        private IEnumerator Launch()
        {
            // find decoupler
            //decoupler = FindDecoupler(part, "Weapon", true);
            seperator = FindDecoupler(part, "Weapon", true);

            bool frontLaunch = Vector3.Dot(seperator.transform.up, vessel.ReferenceTransform.up) > 0.99;
            controller.frontLaunch = frontLaunch;

            // todo:
            // electric charge check
            // fuel check
            // propulsion check

            double separatorMaxtemp = 2000;

            // try to pop decoupler
            if (seperator != null)
            {
                seperator.Separate();
                separatorMaxtemp = part.maxTemp;
                seperator.part.maxTemp = double.MaxValue;
            }
            else
                Debug.Log("[KCS]: Couldn't find decoupler.");

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
            fc.RCSVector = vessel.ReferenceTransform.up;
            fc.Drive();

            //get an onboard probe core to control from
            FindCommand(vessel).MakeReference();
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

            // wait to try to prevent destruction of decoupler.
            // todo: could increase heat tolerance temporarily or calculate a lower throttle.
            yield return new WaitForSeconds(0.2f);

            seperator.part.maxTemp = separatorMaxtemp;
            
            if (!isInterceptor)
            {
                // pulse to 5 m/s.
                float burnTime = 0.5f;
                float driftVelocity = 5;
                fc.throttle = driftVelocity / burnTime / GetMaxAcceleration(vessel);
                fc.Drive();

                yield return new WaitForSeconds(burnTime);

                fc.throttle = 0;
            }
            else
            {
                fc.throttle = 1;
            }
            fc.RCSVector = Vector3.zero;
            fc.Drive();

            Ray targetRay = new Ray();
            Vector3 sideways;
            bool lineOfSight = false;
            bool clear = false;
            float previousTolerance = fc.alignmentToleranceforBurn;

            while (!lineOfSight)
            {
                yield return new WaitForSeconds(0.1f);
                if (target == null) break;
                
                targetRay.origin = vessel.ReferenceTransform.position;
                targetRay.direction = target.transform.position - vessel.transform.position;

                // Do we have line of sight with the target vessel?
                lineOfSight = !RayIntersectsVessel(firer, targetRay);

                if (lineOfSight) break;
                
                if (!clear) // Latch clear once true.
                {
                    // We don't have line of sight with the target yet, but are we clear of the ship?

                    sideways = vessel.transform.forward;

                    for (int i = 0; i < 4; i++)
                    {
                        targetRay.origin = vessel.ReferenceTransform.position;
                        sideways = Vector3.Cross(sideways, vessel.ReferenceTransform.up);
                        targetRay.direction = sideways;
                        clear = !RayIntersectsVessel(firer, targetRay);

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
            }

            fc.alignmentToleranceforBurn = previousTolerance;

            // Remove end cap. todo: will need to change to support cluster missiles.
            List<ModuleDecouple> decouplers = vessel.FindPartModulesImplementing<ModuleDecouple>();
            decouplers.ForEach(d => d.Decouple());

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);
            interceptLine = KCSDebug.CreateLine(Color.cyan);
            rcsLine = KCSDebug.CreateLine(Color.white);

            // enable autopilot
            engageAutopilot = true;
            maxAcceleration = GetMaxAcceleration(vessel);

            shutoffDistance = isInterceptor ? 3 : 10;

            string oldName = vessel.vesselName;
            string missileName = controller.weaponCode == "" ? "Missile" : controller.weaponCode;
            string firerName = ShorternName(firer.vesselName);
            vessel.vesselName = !isInterceptor ? $"{missileName} ({firerName} >> {ShorternName(target.vesselName)})" : $"Interceptor ({firerName})";
            GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel,string>(vessel, oldName, vessel.vesselName));

            controller.launched = true;

            yield break;
        }

        /*private void UpdateGuidanceByAngle()
        {
            if (target == null || (isInterceptor && (targetWeapon == null || targetWeapon.missed))) StopGuidance();

            float maxAcceleration = GetMaxAcceleration(vessel);

            targetVector = target.transform.position - vessel.ReferenceTransform.position;
            relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
            relVelNrm = relVel.normalized;
            relVelmag = relVel.magnitude;

            // Predict aim point based on missile acceleration and relative velocity.
            timeToHit = SolveTime(targetVector.magnitude, maxAcceleration, relVel.magnitude);
            lead = Vector3.ProjectOnPlane(relVel * -1, targetVector.normalized) * timeToHit;

            // Read target acceleration. Significantly higher hit rate but possibly unfair as it defeats dodging.
            //lead = target.acceleration.normalized * SolveDistance(timeToHit, (float)target.acceleration.magnitude, 0);

            targetVector = targetVector + lead;
            targetVectorNormal = targetVector.normalized;

            if (relVelmag > 100)
            {
                //correctionAmount = Mathf.Max((relVelmag / GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
                //correctionAmount = Mathf.Max((relVelmag / maxAcceleration), 0.1f);
                correctionAmount = Mathf.Max(relVelmag / maxAcceleration);
                correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionAmount);
                fc.attitude = correction;
            }
            else
            {
                fc.attitude = targetVectorNormal;
            }

            drift = Vector3.Dot(targetVectorNormal, relVelNrm) > 0.999999
                && (Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity || isInterceptor);

            //fc.alignmentToleranceforBurn = relVelmag > 50 ? 5 : 20;
            fc.throttle = drift ? 0 : 1;

            if (targetVector.magnitude < 10 || !engines.First().isOperational)
                StopGuidance();

            fc.Drive();

            // Update debug lines.
            Vector3 origin = vessel.ReferenceTransform.position;
            KCSDebug.PlotLine(new[] { origin, origin + targetVector }, targetLine);
            KCSDebug.PlotLine(new[] { origin, origin + (relVelNrm * 50) }, rvLine);
            KCSDebug.PlotLine(new[] { origin, origin + lead }, leadLine);
        }*/

        private void UpdateGuidance()
        {
            if (target == null || (isInterceptor && (targetWeapon == null || targetWeapon.missed))) 
            {
                StopGuidance();
                return;
            }

            targetVector = target.transform.position - vessel.ReferenceTransform.position;
            relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
            relVelNrm = relVel.normalized;
            relVelmag = relVel.magnitude;

            if (!isInterceptor)
            {
                timeToHit = SolveTime(targetVector.magnitude, maxAcceleration, Vector3.Dot(relVel, targetVector.normalized));
                lead = (relVelNrm * -1) * timeToHit * relVelmag;
                interceptVector = (target.transform.position + lead) - vessel.ReferenceTransform.position;

                controller.timeToHit = timeToHit;
            }
            else
            {
                timeToHit = ClosestTimeToCPA(targetVector, target.obt_velocity - vessel.obt_velocity, target.acceleration - (vessel.ReferenceTransform.up * maxAcceleration), 30);
                predictedPos = PredictPosition(target.transform.position, target.rootPart.Rigidbody.velocity - vessel.rootPart.Rigidbody.velocity, target.acceleration, timeToHit);
                interceptVector = predictedPos - vessel.ReferenceTransform.position;

                if (Vector3.Dot(interceptVector.normalized, targetVector.normalized) < 0)
                    interceptVector = interceptVector * -1;
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
            Vector3 origin = vessel.ReferenceTransform.position;
            //KCSDebug.PlotLine(new[] { origin, origin + (relVelNrm * 15) }, rvLine);
            KCSDebug.PlotLine(new[] { origin, origin + relVel }, rvLine);
            KCSDebug.PlotLine(new[] { origin, origin + rcs}, rcsLine);

            if (isInterceptor)
                KCSDebug.PlotLine(new[] { origin, origin + targetVector }, interceptLine);
            else
                KCSDebug.PlotLine(new[] { origin, origin + targetVector }, targetLine);
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
        }

        public override void Fire()
        {
            StartCoroutine(Launch());
        }

        public void FixedUpdate()
        {
            if (engageAutopilot) UpdateGuidance();
        }

        public void OnDestroy()
        {
            KCSDebug.DestroyLine(rvLine);
            KCSDebug.DestroyLine(targetLine);
            KCSDebug.DestroyLine(interceptLine);
            KCSDebug.DestroyLine(rcsLine);
            Destroy(fc);
        }

        public void StopGuidance()
        {
            engageAutopilot = false;
            controller.missed = true;
            OnDestroy();
        }
    }
}
