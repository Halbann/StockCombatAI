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

        // Components

        private KCSFlightController fc;
        private ModuleDecouple decoupler;
        private ModuleWeaponController controller;
        private List<ModuleEngines> engines;
        private ModuleEngines mainEngine;

        // Debugging line variables.

        LineRenderer targetLine, rvLine, leadLine;

        private IEnumerator Launch()
        {
            // find decoupler
            decoupler = FindDecoupler(part, "Weapon", true);

            // todo:
            // electric charge check
            // fuel check
            // propulsion check

            // try to pop decoupler
            if (decoupler != null)
                decoupler.Decouple();
            else
                //notify of error but launch anyway for freefloating missiles
                Debug.Log("[KCS]: Couldn't find decoupler.");

            // wait to try to prevent destruction of decoupler.
            // todo: could increase heat tolerance temporarily or calculate a lower throttle.
            yield return new WaitForSeconds(0.2f);

            // turn on engines
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            engines.ForEach(e => e.Activate());
            mainEngine = engines.First();
            
            if (!isInterceptor)
            {
                // pulse to 5 m/s.
                float burnTime = 0.5f;
                float driftVelocity = 5;
                vessel.ctrlState.mainThrottle = driftVelocity / burnTime / GetMaxAcceleration(vessel);

                yield return new WaitForSeconds(burnTime);

                vessel.ctrlState.mainThrottle = 0;
            }
            else
            {
                vessel.ctrlState.mainThrottle = 1;
            }

            // wait until clear of firer
            bool lineOfSight = false;
            Ray targetRay = new Ray();

            while (!lineOfSight)
            {
                yield return new WaitForSeconds(isInterceptor ? 0.1f : 0.5f);
                if (target == null) yield break;
                targetRay.origin = vessel.ReferenceTransform.position;
                targetRay.direction = target.transform.position - vessel.transform.position;
                lineOfSight = !RayIntersectsVessel(firer, targetRay);
            }

            // Remove end cap. todo: will need to change to support cluster missiles.
            List<ModuleDecouple> decouplers = vessel.FindPartModulesImplementing<ModuleDecouple>();
            decouplers.ForEach(d => d.Decouple());

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);
            leadLine = KCSDebug.CreateLine(Color.cyan);

            // enable autopilot
            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.alignmentToleranceforBurn = 20;
            fc.lerpAttitude = false;
            engageAutopilot = true;
            controller.launched = true;
            maxAcceleration = GetMaxAcceleration(vessel);

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
            if (targetVector.magnitude < 10 || ((mainEngine == null || !mainEngine.isOperational) && accuracy < 0.99))
            {
                StopGuidance();
                return;
            }

            drift = accuracy > 0.999999
                && (Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity || isInterceptor);

            fc.throttle = drift ? 0 : 1;
            fc.attitude = targetVectorNormal;
            fc.Drive();

            // Update debug lines.
            Vector3 origin = vessel.ReferenceTransform.position;
            KCSDebug.PlotLine(new[] { origin, origin + interceptVector }, targetLine);
            KCSDebug.PlotLine(new[] { origin, origin + (relVelNrm * 15) }, rvLine);

            if (!isInterceptor)
            {
                Vector3 targetOrigin = target.transform.position;
                KCSDebug.PlotLine(new[] { targetOrigin, targetOrigin + lead }, leadLine);
            }
        }

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();
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
            KCSDebug.DestroyLine(leadLine);
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
