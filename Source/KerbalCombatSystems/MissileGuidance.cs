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
    public class ModuleMissileGuidance : PartModule
    {
        // User parameters changed via UI.

        const string missileGuidanceGroupName = "Missile Guidance";

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Targetting Range",
            guiUnits = "m",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_MinMaxRange(
                minValueX = 100f,
                maxValueX = 5000f,
                minValueY = 200f,
                maxValueY = 5000f,
                stepIncrement = 50f,
                scene = UI_Scene.All
            )]
        public Vector2 MinMaxRange = new Vector2(500f, 1000f);

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Terminal Velocity",
              guiUnits = "m/s",
              groupName = missileGuidanceGroupName,
              groupDisplayName = missileGuidanceGroupName),
              UI_FloatRange(
                  minValue = 50f,
                  maxValue = 2000f,
                  stepIncrement = 50f,
                  scene = UI_Scene.All
              )]
        public float terminalVelocity = 300f;

        [KSPField(isPersistant = true,
               guiActive = true,
               guiActiveEditor = true,
               guiName = "Preferred Target Mass",
               guiUnits = "t",
               groupName = missileGuidanceGroupName,
               groupDisplayName = missileGuidanceGroupName),
               UI_FloatRange(
                   minValue = 0f,
                   maxValue = 1000f,
                   stepIncrement = 0.01f,
                   scene = UI_Scene.All
               )]
        public float TMassPreference = 50f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Interception",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool useAsInterceptor = false;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Velocity Match",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool MatchTargetVelocity = true;

        // Missile guidance variables.

        public bool engageAutopilot = false;
        Vessel target;
        Vessel firer;
        ModuleDecouple decoupler;
        
        KCSFlightController fc;

        // Debugging line variables.

        LineRenderer targetLine, rvLine;

        // 'Fire' button.

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Fire",
                  groupName = missileGuidanceGroupName,
                  groupDisplayName = missileGuidanceGroupName)]
        public void FireMissile()
        {
            float maxRange = MinMaxRange.x;
            float minRange = MinMaxRange.y;

            target = vessel.targetObject.GetVessel();
            firer = vessel;

            // find decoupler
            decoupler = FindDecoupler(part);

            // electric charge check
            // fuel check
            // propulsion check

            Debug.Log($"Firing missile, let 'em have it! Max range: {maxRange}, Min range: {minRange}, Interceptor: {useAsInterceptor}");
            
            StartCoroutine(Launch());
        }

        private IEnumerator Launch()
        {
            // try to pop decoupler
            try
            {
                decoupler.Decouple();
            }
            catch
            {
                //notify of error but launch anyway for freefloating missiles
                Debug.Log("Couldn't find decoupler.");
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
            vessel.ctrlState.mainThrottle = driftVelocity / burnTime / GetMaxAcceleration(vessel);
            yield return new WaitForSeconds(burnTime);
            vessel.ctrlState.mainThrottle = 0;

            // wait until clear of firer
            bool lineOfSight = false;
            Ray targetRay = new Ray();

            while (!lineOfSight)
            {
                yield return new WaitForSeconds(0.5f);
                targetRay.origin = vessel.ReferenceTransform.position;
                targetRay.direction = target.transform.position - vessel.transform.position;
                lineOfSight = !RayIntersectsVessel(firer, targetRay);
            }

            // initialise debug line renderer
            targetLine = KCSDebug.CreateLine(Color.magenta);
            rvLine = KCSDebug.CreateLine(Color.green);

            // enable autopilot
            fc = new KCSFlightController(vessel);
            engageAutopilot = true;

            yield break;
        }

        /*public void FixedUpdate()
        {
            if (engageAutopilot)
            {
                if (target == null)
                {
                    engageAutopilot = false;
                    return;
                }
                Vector3 targetVector = target.transform.position - vessel.transform.position;
                Vector3 targetVectorNormal = targetVector.normalized;
                Vector3 up = Vector3.Cross(targetVectorNormal, vessel.orbit.GetOrbitNormal());

                bool sasWasOn = vessel.ActionGroups[KSPActionGroup.SAS];
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                if (vessel.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist || !vessel.Autopilot.Enabled || !sasWasOn)
                {
                    vessel.Autopilot.Enable(VesselAutopilot.AutopilotMode.StabilityAssist);
                }

                Vector3 relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
                Vector3 relVelNrm = relVel.normalized;
                float relVelmag = relVel.magnitude;

                float correctionRatio = Mathf.Max((relVelmag / GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
                Vector3 correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionRatio);

                Quaternion quat = Quaternion.LookRotation(correction, up) * Quaternion.Euler(90, 0, 0);
                Quaternion cquat = vessel.Autopilot.SAS.lockedRotation;
                Quaternion progressiveRotation = Quaternion.Slerp(cquat, quat, 0.1f);
                vessel.Autopilot.SAS.LockRotation(progressiveRotation);

                float angle = Quaternion.Angle(vessel.ReferenceTransform.rotation, quat);
                float tolerance = relVelmag > 50 ? 5 : 20;
                vessel.ctrlState.mainThrottle = angle < tolerance ? 1 : 0;

                if (Vector3.Dot(targetVectorNormal, relVelNrm) > 0.999999
                    && Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity)
                {
                    vessel.ctrlState.mainThrottle = 0;
                }

                if (targetVector.magnitude < 10)
                    engageAutopilot = false;

                //debugging linedraws
                Vector3[] linePositions = { target.transform.position, vessel.transform.position };
                PlotLine(linePositions, targetLine);
                linePositions = new Vector3[] { vessel.transform.position, vessel.transform.position + (correction * 50) };
                PlotLine(linePositions, correctionLine);
                linePositions = new Vector3[] { vessel.transform.position, vessel.transform.position + (relVelNrm * 50) };
                PlotLine(linePositions, rvLine);
                linePositions = new Vector3[] { vessel.transform.position, vessel.transform.position + ((progressiveRotation * Vector3.up) * 50) };
                PlotLine(linePositions, SASLine);
            }
        }*/

        private void UpdateGuidance()
        {
            if (target == null)
            {
                engageAutopilot = false;
                return;
            }

            Vector3 targetVector = target.transform.position - vessel.transform.position;
            Vector3 targetVectorNormal = targetVector.normalized;
            Vector3 relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
            Vector3 relVelNrm = relVel.normalized;
            float relVelmag = relVel.magnitude;

            float correctionRatio = Mathf.Max((relVelmag / GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
            Vector3 correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionRatio);

            bool drift = Vector3.Dot(targetVectorNormal, relVelNrm) > 0.999999
                && Vector3.Dot(relVel, targetVectorNormal) > terminalVelocity;

            fc.attitude = correction;
            fc.alignmentToleranceforBurn = 20;
            //fc.alignmentToleranceforBurn = relVelmag > 50 ? 5 : 20;
            fc.throttle = drift ? 0 : 1;

            if (targetVector.magnitude < 10)
                engageAutopilot = false;

            fc.Update();

            // Update debug lines.
            KCSDebug.PlotLine(new[]{ target.transform.position, vessel.transform.position }, targetLine);
            KCSDebug.PlotLine(new[]{ vessel.transform.position, vessel.transform.position + (relVelNrm * 50) }, rvLine);
        }

        public void FixedUpdate()
        {
            if (engageAutopilot) UpdateGuidance();
        }        
        
        ModuleDecouple FindDecoupler(Part origin)
        {
            Part currentPart = origin.parent;
            ModuleDecouple decoupler;

            //ModuleDecouplerDesignate DecouplerType;	

            for (int i = 0; i < 99; i++)
            {
                if (currentPart.isDecoupler(out decoupler)) return decoupler;
                currentPart = currentPart.parent;
            }

            return null;
        }

        float GetMaxAcceleration(Vessel v)
        {
            List<ModuleEngines> engines = v.FindPartModulesImplementing<ModuleEngines>();
            float thrust = engines.Sum(e => e.MaxThrustOutputVac(true));

            return thrust / vessel.GetTotalMass();
        }

        private bool RayIntersectsVessel(Vessel v, Ray r)
        {
            foreach (Part p in v.parts)
            {
                foreach (Bounds b in p.GetColliderBounds())
                {
                    if (b.IntersectRay(r)) return true;
                }
            }

            return false;
        }
    }
}
