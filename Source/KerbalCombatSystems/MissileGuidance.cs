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
        // Missile guidance variables.

        public bool engageAutopilot = false;
        private KCSFlightController fc;
        private Vector3 targetVector;
        private Vector3 targetVectorNormal;
        private Vector3 relVel;
        private Vector3 relVelNrm;
        private float relVelmag;
        private float correctionRatio;
        private Vector3 correction;
        private bool drift;
        private Vessel target;
        private Vessel firer;
        private ModuleDecouple decoupler;
        private ModuleWeaponController controller;
        private List<ModuleEngines> engines;
        private float timeToHit;
        private Vector3 lead;
        private float terminalVelocity;

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
            foreach (ModuleEngines engine in engines)
            {
                engine.Activate();
            }

            // pulse to 5 m/s.
            float burnTime = 0.5f;
            float driftVelocity = 5;
            vessel.ctrlState.mainThrottle = driftVelocity / burnTime / GetMaxAcceleration(vessel);
            yield return new WaitForSeconds(burnTime);
            vessel.ctrlState.mainThrottle = 0;

            // wait until clear of firer
            bool lineOfSight = false;
            Ray targetRay = new Ray();

            while (!lineOfSight)
            {
                yield return new WaitForSeconds(0.5f);
                if (target == null) yield break;
                targetRay.origin = vessel.ReferenceTransform.position;
                targetRay.direction = target.transform.position - vessel.transform.position;
                lineOfSight = !RayIntersectsVessel(firer, targetRay);
            }

            // Remove end cap. todo: will have to change to support cluster missiles.
            List<ModuleDecouple> decouplers = vessel.FindPartModulesImplementing<ModuleDecouple>();
            foreach (ModuleDecouple d in decouplers)
            {
                d.Decouple();
            }

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);
            leadLine = KCSDebug.CreateLine(Color.cyan);

            // enable autopilot
            fc = part.gameObject.AddComponent<KCSFlightController>();
            engageAutopilot = true;

            yield break;
        }

        private void UpdateGuidance()
        {
            if (target == null) StopGuidance();

            targetVector = target.transform.position - vessel.ReferenceTransform.position;
            relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
            relVelNrm = relVel.normalized;
            relVelmag = relVel.magnitude;

            if (relVelmag > 100)
            {
                // Predict aim point based on missile acceleration and relative velocity.
                timeToHit = SolveTime(targetVector.magnitude, (float)vessel.acceleration.magnitude, relVel.magnitude);
                lead = Vector3.ProjectOnPlane(relVel * -1, targetVector.normalized) * timeToHit;

                // Read target acceleration. Significantly higher hit rate but possibly unfair as it defeats dodging.
                //lead = target.acceleration.normalized * SolveDistance(timeToHit, (float)target.acceleration.magnitude, 0);

                targetVector = targetVector + lead;
            }

            targetVectorNormal = targetVector.normalized;

            correctionRatio = Mathf.Max((relVelmag / GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
            correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionRatio);

            drift = Vector3.Dot(targetVectorNormal, relVelNrm) > 0.999999
                && Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity;

            fc.attitude = correction;
            fc.alignmentToleranceforBurn = 20;
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
        }

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();
            target = controller.target ?? vessel.targetObject.GetVessel();
            terminalVelocity = controller.terminalVelocity;
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
            OnDestroy();
            return;
        }
    }
}
