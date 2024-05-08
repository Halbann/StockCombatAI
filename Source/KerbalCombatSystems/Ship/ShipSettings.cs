using System.Linq;

using UnityEngine;

namespace KerbalCombatSystems
{
    public partial class ModuleShipController : PartModule
    {
        const string shipControllerGroupName = "Ship AI";

        private const float manoeuvringSpeedMin = 10f;
        private const float manoeuvringSpeedMax = 500f;

        // todo: add more axis fields and action wrappers where appropriate.
        [UI.Tooltip(
            title = "Manoeuvring Speed",
            text = "The speed relative to the target at which the AI will attempt to manoeuvre while intercepting."
        )]
        [KSPAxisField(
            guiName = "Manoeuvring Speed",
            isPersistant = true,
            groupStartCollapsed = false,
            minValue = manoeuvringSpeedMin,
            maxValue = manoeuvringSpeedMax,
            groupDisplayName = shipControllerGroupName,
            groupName = shipControllerGroupName,
            guiActive = true,
            guiActiveEditor = true,
            guiUnits = " m/s"
        )]
        [UI_FloatRange(
            minValue = manoeuvringSpeedMin,
            maxValue = manoeuvringSpeedMax,
            stepIncrement = 10f,
            scene = UI_Scene.All
        )]
        public float manoeuvringSpeed = 100f;


        [UI.Tooltip(
            title = "Strafing Speed Limit",
            text = "The AI will need to slow down before aiming projectiles if the speed of the target exceeds this limit."
            + " The right value depends on the speed of your projectiles."
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Strafing Speed Limit",
            guiUnits = " m/s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_FloatRange(
            minValue = 2f,
            maxValue = 100f,
            stepIncrement = 1f,
            scene = UI_Scene.All
        )]
        public float firingSpeed = 50f;


        [UI.Tooltip(
            title = "Angular Speed Limit",
            text = "The AI will need to slow down before aiming projectiles if the angular speed of the target exceeds this limit."
            + " The right value depends on your torque and the accuracy of your weapons."
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Angular Speed Limit",
            guiUnits = " °/s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_FloatRange(
            minValue = 1f,
            maxValue = 45f,
            stepIncrement = 1f,
            scene = UI_Scene.All
        )]
        public float firingAngularVelocityLimit = 10f;


        [UI.Tooltip(
            title = "Max. Salvo Size",
            text = "The number of missiles launched per salvo is the <i>mass of the target</i> divided " +
            "by the <i>mass of the selected missile multiplied by its mass ratio</i>, <b>limited by the max salvo size.</b>"
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Max. Salvo Size",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_FloatRange(
            minValue = 1,
            maxValue = 20,
            stepIncrement = 1,
            scene = UI_Scene.All
        )]
        public float maxSalvoSize = 5;

        [UI.Tooltip(
            title = "Salvo Interval",
            text = "The minimum amount of time between new missile salvos."
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Salvo Interval",
            guiUnits = " s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_FloatRange(
            minValue = 1,
            maxValue = 30,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float firingInterval = 7.5f;


        internal const float priorityTargetMin = 1f;
        internal const float priorityTargetMax = 250f;

        [UI.Tooltip(
            title = "Priority Target Mass",
            text = "Enemies in this mass range will always be targeted before enemies outside of it."
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Priority Target Mass",
            guiUnits = " t",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_MinMaxRange(
            minValueX = priorityTargetMin,
            maxValueX = priorityTargetMax,
            minValueY = priorityTargetMin,
            maxValueY = priorityTargetMax,
            stepIncrement = 1f,
            scene = UI_Scene.All
        )]
        public Vector2 priorityTargetRange = new Vector2(priorityTargetMin, priorityTargetMax);


        [UI.Tooltip(
            title = "Withdrawing Enemies",
            text = "How should the AI treat withdrawing enemies?\n"
            + "\n<b>Relegate:</b> Target after other enemies."
            + "\n<b>Priority:</b> Target before other enemies."
            + "\n<b>Valid:</b> Target alongside other enemies."
            + "\n<b>Invalid:</b> Ignore them."
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Withdrawing Enemies",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_ChooseOption(
            controlEnabled = true,
            affectSymCounterparts = UI_Scene.None,
            options = new string[] { "Relegate", "Priority", "Valid", "Invalid" }
        )]
        public string withdrawingPriority = "Relegate";


        [UI.Tooltip(
            title = "Use Evasion",
            text = "Should the AI attempt to dodge incoming missiles using its engines?"
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use Evasion",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        [UI_Toggle(
            enabledText = "Enabled",
            disabledText = "Disabled",
            scene = UI_Scene.All
        )]
        public bool useEvasion = true;


        // Legacy field.
        [KSPField(isPersistant = true)]
        public float forwardLaunchThrottle = 0f;

        private void UpgradeSettings()
        {
            var withdrawOption = Fields[nameof(withdrawingPriority)].uiControlEditor as UI_ChooseOption;
            if (!withdrawOption.options.Contains(withdrawingPriority))
            {
                // Use of C# 8.0 language features. Sus?

                withdrawingPriority = withdrawingPriority switch
                {
                    "Chase" => "Priority",
                    "Ignore" => "Invalid",
                    _ => "Relegate"
                };
            }
        }
    }
}
