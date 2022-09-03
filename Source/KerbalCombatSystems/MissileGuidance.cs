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
        ModuleWeaponController controller;

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

            // turn on rcs thrusters
            List<ModuleRCSFX> Thrusters = vessel.FindPartModulesImplementing<ModuleRCSFX>();
            foreach (ModuleRCSFX Thruster in Thrusters)
            {
                Thruster.enabled = true;
            }

            //get an onboard probe core to control from
            FindCommand(vessel).MakeReference();

            // pulse to 5 m/s.
            var burnTime = 0.5f;
            var driftVelocity = 5;
            vessel.ctrlState.mainThrottle = driftVelocity / burnTime / GetMaxAcceleration(vessel);
            yield return new WaitForSeconds(burnTime);
            vessel.ctrlState.mainThrottle = 0;

            //enable RCS for translation
            if (!vessel.ActionGroups[KSPActionGroup.RCS])
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

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

            correctionRatio = Mathf.Max((relVelmag / GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
            correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionRatio);

            drift = Vector3.Dot(targetVectorNormal, relVelNrm) > 0.999999
                && Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity;

            fc.attitude = correction;
            fc.alignmentToleranceforBurn = 20;
            //fc.alignmentToleranceforBurn = relVelmag > 50 ? 5 : 20;
            fc.throttle = drift ? 0 : 1;

            fc.RCSVector = -relVelNrm;

            if (targetVector.magnitude < 10)
            {
                engageAutopilot = false;
                OnDestroy();
            }

            fc.Drive();

            // Update debug lines.
            KCSDebug.PlotLine(new[] { vessel.transform.position, target.transform.position }, targetLine);
            KCSDebug.PlotLine(new[] { vessel.transform.position, vessel.transform.position + (relVelNrm * 50) }, rvLine);
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
            Destroy(fc);
        }
    }
}
