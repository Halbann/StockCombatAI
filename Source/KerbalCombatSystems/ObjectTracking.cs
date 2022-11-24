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
        const string objectTrackingGroupName = "Situational Awareness";
        private int scalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = objectTrackingGroupName,
              groupDisplayName = objectTrackingGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float detectionRange = 0f;

        [KSPField(isPersistant = true)]
        public float baseDetectionRange = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", detectionRange));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            scalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().scalingFactor;
            detectionRange = baseDetectionRange * scalingFactor;
        }
    }
}
