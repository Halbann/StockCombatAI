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
            guiName = "Max Range",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_FloatRange(minValue = 200f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float maxRange = 1000f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Min Range",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_FloatRange(minValue = 100f, maxValue = 4000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float minRange = 500f;

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

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Fire",
                  groupName = missileGuidanceGroupName,
                  groupDisplayName = missileGuidanceGroupName)]
        public void FireMissile()
        {
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


            // enable autopilot

            engageAutopilot = true;
        }

        public override void OnFixedUpdate()
        {
        }

        public override void OnStart(StartState state)
        {
        }
        public override void OnUpdate()
        {
            if (engageAutopilot)
            {
                var fwd = (target.transform.position - vessel.transform.position).normalized;
                var up = Vector3.Cross(fwd, vessel.orbit.GetOrbitNormal());

                bool sasWasOn = vessel.ActionGroups[KSPActionGroup.SAS];
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                if (vessel.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist || !vessel.Autopilot.Enabled || !sasWasOn)
                {
                    vessel.Autopilot.Enable(VesselAutopilot.AutopilotMode.StabilityAssist);
                }

                Quaternion quat = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(90, 0, 0);
                Quaternion cquat = vessel.Autopilot.SAS.lockedRotation;
                Quaternion progressiveRotation = Quaternion.Slerp(cquat, quat, 0.1f);
                vessel.Autopilot.SAS.LockRotation(progressiveRotation);
            }
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
    }
}
