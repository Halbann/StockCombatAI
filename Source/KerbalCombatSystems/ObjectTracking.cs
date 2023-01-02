using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleObjectTracking : PartModule
    {
        //all ore scanning parts plus the sentinal at superlong range
        const string objectTrackingGroupName = "Object Tracking";
        private int scalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Lock Range",
              guiUnits = " m",
              groupName = objectTrackingGroupName,
              groupDisplayName = objectTrackingGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float detectionRange = 0f;

        [KSPField(isPersistant = true)]
        public float baseDetectionRange = 0f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Deploy Automatically",
            groupName = objectTrackingGroupName,
            groupDisplayName = objectTrackingGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool animate = true;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            // GetInfo runs once at game start when CurrentGame is null so we need to use the default value.
            float scalingFactor = 5f;

            output.Append(Environment.NewLine);
            output.Append(string.Format("Detection Range: {0} m", baseDetectionRange * scalingFactor));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            scalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().scalingFactor;
            detectionRange = baseDetectionRange * scalingFactor;
        }
    }
}
