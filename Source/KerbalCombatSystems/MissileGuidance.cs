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
        private Vector3 targetVector;
        private Vector3 targetVectorNormal;
        private Vector3 relVel;
        private Vector3 relVelNrm;
        private float relVelmag;
        private float correctionRatio;
        private Vector3 correction;
        private bool drift;

        // Debugging line variables.

        LineRenderer targetLine, rvLine;

        // Set persistent missile code in editor and flight.

        [KSPField(isPersistant = true)]
        public string missileCode = "";

        [KSPEvent(guiActive = true,
                  guiActiveEditor = true,
                  guiName = "Set Missile Code",
                  groupName = missileGuidanceGroupName,
                  groupDisplayName = missileGuidanceGroupName,
                  name = "missileCodeEvent")]
        public void SetMissileCode()
        {
            VesselRenameDialog.SpawnNameFromPart(part, SetMissileCodeCallback, Dismiss, Remove, false, VesselType.Probe);
        }

        public void SetMissileCodeCallback(String code, VesselType t, int i)
        {
            missileCode = code;
            UpdateMissileCodeUI();
        }

        private void UpdateMissileCodeUI()
        {
            var e = Events["SetMissileCode"];
            var name = missileCode == "" ? "None" : missileCode;
            e.guiName = "Set Missile Code:                                " + name;

            if (part.vesselNaming == null)
                part.vesselNaming = new VesselNaming();

            part.vesselNaming.vesselName = missileCode;
        }

        // This needs to exist for the dialog to work.
        public void Dismiss() {}
        public void Remove()
        {
            missileCode = "";
            UpdateMissileCodeUI();
        }

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

            if (vessel.targetObject == null) return;
            target = vessel.targetObject.GetVessel();
            firer = vessel;

            // find decoupler
            decoupler = KCS.FindDecoupler(part, "Missile", true);

            // todo:
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
            vessel.ctrlState.mainThrottle = driftVelocity / burnTime / KCS.GetMaxAcceleration(vessel);
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
            UpdateMissileCodeUI();
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
