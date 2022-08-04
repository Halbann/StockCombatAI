using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;

namespace KerbalCombatSystems
{
    public class ModuleMissileGuidance : PartModule
    {
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

        public bool engageAutopilot = false;
        //bool sas_orientation_set = false;

        Vessel target;

        LineRenderer targetLine;
        LineRenderer rvLine;
        LineRenderer correctionLine;
        LineRenderer SASLine;

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

            // find decoupler

            ModuleDecouple decoupler = FindDecoupler(part);
            if (decoupler == null)
            {
                Debug.Log("Missile launch failed. Couldn't find decoupler.");
                return;
            };

            // pop decoupler

            decoupler.Decouple();

            List<ModuleEngines> engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            foreach (ModuleEngines engine in engines)
            {
                engine.Activate();
            }


            // throttle up

            vessel.ctrlState.mainThrottle = 1;

            // Start line renderer

            //lineRenderer = new LineRenderer();
            //var myLineRenderer = Instantiate(lineRenderer);

            float th = 0.5f;

            targetLine = new GameObject().AddComponent<LineRenderer>();
            targetLine.useWorldSpace = true;
            Material lineMaterial = new Material(Shader.Find("Standard"));
            lineMaterial.color = Color.magenta;
            targetLine.material = lineMaterial;
            targetLine.SetWidth(th, th);

            rvLine =  new GameObject().AddComponent<LineRenderer>();
            rvLine.useWorldSpace = true;
            Material lineMaterial2 = new Material(Shader.Find("Standard"));
            lineMaterial2.color = Color.green;
            rvLine.material = lineMaterial2;
            rvLine.SetWidth(th, th);

            correctionLine =  new GameObject().AddComponent<LineRenderer>();
            correctionLine.useWorldSpace = true;
            Material lineMaterial3 = new Material(Shader.Find("Standard"));
            lineMaterial3.color = Color.white;
            correctionLine.material = lineMaterial3;
            correctionLine.SetWidth(th, th);

            SASLine =  new GameObject().AddComponent<LineRenderer>();
            SASLine.useWorldSpace = true;
            Material lineMaterial4 = new Material(Shader.Find("Standard"));
            lineMaterial4.color = Color.blue;
            SASLine.material = lineMaterial4;
            SASLine.SetWidth(th, th);


            // enable autopilot

            engageAutopilot = true;
        }

        public void FixedUpdate()
        {
            if (engageAutopilot)
            {
                Vector3 targetVector = (target.transform.position - vessel.transform.position).normalized;

                    Vector3[] linePositions1 = { target.transform.position, vessel.transform.position };
                    targetLine.SetPositions(linePositions1);

                Vector3 up = Vector3.Cross(targetVector, vessel.orbit.GetOrbitNormal());

                bool sasWasOn = vessel.ActionGroups[KSPActionGroup.SAS];
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                if (vessel.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist || !vessel.Autopilot.Enabled || !sasWasOn)
                {
                    vessel.Autopilot.Enable(VesselAutopilot.AutopilotMode.StabilityAssist);
                }

                Vector3 relVel = vessel.GetObtVelocity() - target.GetObtVelocity();
                Vector3 relVelNrm = relVel.normalized;

                    Vector3[] linePositions3 = { vessel.transform.position, vessel.transform.position + (relVelNrm * 50) };
                    rvLine.SetPositions(linePositions3);

                float correctionRatio = (relVel.magnitude / GetCurrentAcceleration(vessel)) * 1.33f;

                Vector3 correction = Vector3.LerpUnclamped(relVelNrm, targetVector, 1 + correctionRatio);

                    Vector3[] linePositions2 = { vessel.transform.position, vessel.transform.position + (correction * 50) };
                    correctionLine.SetPositions(linePositions2);

                Quaternion quat = Quaternion.LookRotation(correction, up) * Quaternion.Euler(90, 0, 0);
                Quaternion cquat = vessel.Autopilot.SAS.lockedRotation;
                Quaternion progressiveRotation = Quaternion.Slerp(cquat, quat, 0.1f);
                vessel.Autopilot.SAS.LockRotation(progressiveRotation);

                    Vector3[] linePositions4 = { vessel.transform.position, vessel.transform.position + ((progressiveRotation * Vector3.up) * 50) };
                    SASLine.SetPositions(linePositions4);
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

        float GetCurrentAcceleration(Vessel v)
        {
            List<ModuleEngines> engines = v.FindPartModulesImplementing<ModuleEngines>();
            float thrust = engines.Sum(e => e.GetCurrentThrust());
            return thrust / vessel.GetTotalMass();
        }
    }
}
