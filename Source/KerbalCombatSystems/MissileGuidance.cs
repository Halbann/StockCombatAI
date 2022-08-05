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
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_MinMaxRange(minValueX = 100f, maxValueX = 5000f, minValueY = 200f, maxValueY = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
    
        public Vector2 MinMaxRange = new Vector2(500f, 1000f);

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Terminal Velocity",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_FloatRange(minValue = 10f, maxValue = 2000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float terminalVelocity = 300f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Interception",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_Toggle(enabledText = "Enabled", disabledText = "Disabled", scene = UI_Scene.All)]
        public bool useAsInterceptor = false;

        // Missile guidance variables.

        public bool engageAutopilot = false;
        Vessel target;
        Vessel firer;

        // Debugging line variables.

        LineRenderer targetLine;
        LineRenderer rvLine;
        LineRenderer correctionLine;
        LineRenderer SASLine;
        public bool drawDebugLines = true;

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
            Debug.Log($"Firing missile, let 'em have it! Max range: {maxRange}, Min range: {minRange}, Interceptor: {useAsInterceptor}");

            // set target

            target = vessel.targetObject.GetVessel();

            // electric charge check
            // fuel check
            // propulsion check

            StartCoroutine(Launch());
        }

        public IEnumerator Launch()
        {
            // find decoupler

            ModuleDecouple decoupler = FindDecoupler(part);
            if (decoupler == null)
            {
                Debug.Log("Missile launch failed. Couldn't find decoupler.");
                yield break;
            };

            // set firer
            firer = vessel;

            // pop decoupler
            decoupler.Decouple();

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

            // Create debug lines.

            if (drawDebugLines)
            {
                float th = 0.5f;

                targetLine = CreateDebugLine(Color.magenta, th);
                rvLine = CreateDebugLine(Color.green, th);
                correctionLine = CreateDebugLine(Color.white, th);
                SASLine = CreateDebugLine(Color.blue, th);
            }

            // enable autopilot

            engageAutopilot = true;

            yield break;
        }

        public void FixedUpdate()
        {
            if (engageAutopilot)
            {
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

                float correctionRatio = Mathf.Max((relVel.magnitude / GetMaxAcceleration(vessel)) * 1.33f, 0.1f);
                Vector3 correction = Vector3.LerpUnclamped(relVelNrm, targetVectorNormal, 1 + correctionRatio);

                Quaternion quat = Quaternion.LookRotation(correction, up) * Quaternion.Euler(90, 0, 0);
                Quaternion cquat = vessel.Autopilot.SAS.lockedRotation;
                Quaternion progressiveRotation = Quaternion.Slerp(cquat, quat, 0.1f);
                vessel.Autopilot.SAS.LockRotation(progressiveRotation);

                float angle = Quaternion.Angle(vessel.ReferenceTransform.rotation, quat);
                vessel.ctrlState.mainThrottle = angle < 5 ? 1 : 0;

                if (targetVector.magnitude < 10)
                {
                    engageAutopilot = false;
                }

                if (drawDebugLines)
                {
                    Vector3[] linePositions = { target.transform.position, vessel.transform.position };
                    targetLine.SetPositions(linePositions);

                    linePositions = new[]{ vessel.transform.position, vessel.transform.position + (correction * (50 * correctionRatio)) };
                    correctionLine.SetPositions(linePositions);

                    linePositions = new[]{ vessel.transform.position, vessel.transform.position + (relVelNrm * 50) };
                    rvLine.SetPositions(linePositions);

                    linePositions = new[]{ vessel.transform.position, vessel.transform.position + ((progressiveRotation * Vector3.up) * 50) };
                    SASLine.SetPositions(linePositions);
                }
            }
        }
        public override void OnStart(StartState state)
        {
        }
        public override void OnUpdate()
        {
        }

        ModuleDecouple FindDecoupler(Part origin)
        {
            Part currentPart = origin.parent;
            ModuleDecouple decoupler;

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

        LineRenderer CreateDebugLine(Color color, float thickness)
        {
            LineRenderer line = new GameObject().AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = thickness;
            line.endWidth = thickness;
            Material lineMaterial = new Material(Shader.Find("Standard"));
            lineMaterial.color = color;
            line.material = lineMaterial;
            return line;
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
