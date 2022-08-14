using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;

namespace KerbalCombatSystems
{
    public class ModuleMissile : PartModule
    {
        // Missile guidance variables.

        public bool engageAutopilot = false;
        KCSFlightController fc;
        private Vector3 targetVector;
        private Vector3 targetVectorNormal;
        private Vector3 relVel;
        private Vector3 relVelNrm;
        private float relVelmag;
        private float correctionRatio;
        private Vector3 correction;
        private bool drift;
        Vessel target;
        Vessel firer;
        ModuleDecouple decoupler;

        // Debugging line variables.

        LineRenderer targetLine, rvLine;
        private float terminalVelocity;

        private IEnumerator Launch()
        {
            // find decoupler
            decoupler = KCS.FindDecoupler(part, "Weapon", true);

            // todo:
            // electric charge check
            // fuel check
            // propulsion check

            // try to pop decoupler
            try
            {
                decoupler.Decouple();
            }
            catch
            {
                //notify of error but launch anyway for freefloating missiles
                Debug.Log("[KCS]: Couldn't find decoupler.");
            }

            // wait to try to prevent destruction of decoupler.
            // todo: could increase heat tolerance temporarily or calculate a lower throttle.
            yield return new WaitForSeconds(0.2f);

            // turn on engines
            List<ModuleEngines> engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            foreach (ModuleEngines engine in engines)
            {
                engine.Activate();
            }

            // pulse to 5 m/s.
            var burnTime = 0.5f;
            var driftVelocity = 5;
            vessel.ctrlState.mainThrottle = driftVelocity / burnTime / KCS.GetMaxAcceleration(vessel);
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
                lineOfSight = !KCS.RayIntersectsVessel(firer, targetRay);
            }

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);

            // enable autopilot
            fc = part.gameObject.AddComponent<KCSFlightController>();
            engageAutopilot = true;

            yield break;
        }

        private void UpdateGuidance()
        {
            if (target == null)
            {
                engageAutopilot = false;
                return;
            }

            targetVector = target.transform.position - vessel.transform.position;
            targetVectorNormal = targetVector.normalized;
            relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
            relVelNrm = relVel.normalized;
            relVelmag = relVel.magnitude;

            correctionRatio = Mathf.Max((relVelmag / KCS.GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
            correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionRatio);

            drift = Vector3.Dot(targetVectorNormal, relVelNrm) > 0.999999
                && Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity;

            fc.attitude = correction;
            fc.alignmentToleranceforBurn = 20;
            //fc.alignmentToleranceforBurn = relVelmag > 50 ? 5 : 20;
            fc.throttle = drift ? 0 : 1;

            if (targetVector.magnitude < 10)
                engageAutopilot = false;

            fc.Drive();

            // Update debug lines.
            KCSDebug.PlotLine(new[]{ vessel.transform.position, target.transform.position }, targetLine);
            KCSDebug.PlotLine(new[]{ vessel.transform.position, vessel.transform.position + (relVelNrm * 50) }, rvLine);
        }

        public void Start()
        {
            target = part.FindModuleImplementing<ModuleWeaponController>().target;
            terminalVelocity = part.FindModuleImplementing<ModuleWeaponController>().terminalVelocity;
            firer = vessel;

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
        }

    }
}
