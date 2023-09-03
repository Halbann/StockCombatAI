using System;
using System.Text;

namespace KerbalCombatSystems
{
    class ModuleObjectTracking : PartModule
    {
        private const string groupName = "Object Tracking";
        private static readonly int scalingFactor = 1;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Lock Range",
              guiUnits = " m",
              groupName = groupName,
              groupDisplayName = groupName
            )]
            [UI_Label(scene = UI_Scene.All)]
        public float detectionRange = 0f;

        [KSPField(isPersistant = true)]
        public float baseDetectionRange = 0f;

        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Deploy Automatically",
            groupName = groupName,
            groupDisplayName = groupName)]
            [UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool animate = true;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(string.Format("Detection Range: {0} m", baseDetectionRange * scalingFactor));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            detectionRange = baseDetectionRange * scalingFactor;

            // Hide the animation option for the ship controller.
            if (part.partInfo.category == PartCategories.Control)
            {
                Fields["animate"].guiActiveEditor = false;
                Fields["animate"].guiActive = false;
            }
        }
    }
}
