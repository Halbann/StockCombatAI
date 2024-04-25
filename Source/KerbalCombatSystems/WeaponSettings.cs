using System.Collections.Generic;

using UnityEngine;

using KerbalCombatSystems.UI;

namespace KerbalCombatSystems
{
    public partial class ModuleWeaponController : PartModule
    {
        public readonly static HashSet<string> projectileTypes = new HashSet<string> { "Rocket", "Firework" };
        public readonly static HashSet<string> types = new HashSet<string> { "Missile", "Rocket", "Firework", "Bomb", "MassCannon" };
        public readonly static HashSet<string> massTypes = new HashSet<string> { "Missile", "Rocket", "Bomb" };

        const string weaponGroupName = "Weapon Settings";
        const string missileGroupName = "Missile Settings";
        const string rocketGroupName = "Rocket Settings";
        const string fireworkGroupName = "Firework Settings";
        //const string MCGroupName = "Mass Cannon Settings";
        //const string BombGroupName = "Bomb Settings";


        #region Generic weapon fields

        [UI.Tooltip(
            text = "The range to the target within which the AI should use this weapon.",
            title = "Range"
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Range",
            guiUnits = " m",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName
        )]
        [UI_MinMaxPow(
            minValueX = 50f,
            maxValueX = 15000f,
            minValueY = 50f,
            maxValueY = 15000f,
            stepIncrement = 50f,
            scene = UI_Scene.All
        )]
        public Vector2 MinMaxRange = new Vector2(500f, 1500f);

        // todo: this isn't applicable to some weapon types.
        // there are other mass/decoupler based fields that aren't applicable either.

        [UI.Tooltip(
            text = "Use this to influence <b><color=white>weapon selection</color></b> and <b><color=white>salvo size</color></b>." +
                //" A higher value will influence the AI to use less ordnance to deal with a given target and vice versa." + 
                " Weapons are matched to appropriate targets and salvo sizes are determined based on the mass of the weapon multiplied by the mass ratio." +
                "\n\n<i>It's how many tonnes of 'target' can be destroyed for every tonne of 'weapon'</i>.",
            title = "Mass Ratio"
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Mass Ratio",
            guiUnits = "x",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName
        )]
        [UI_FloatRange(
            minValue = 1f,
            maxValue = 10f,
            stepIncrement = 1f,
            scene = UI_Scene.All
        )]
        public float targetMassRatio = 5f;

        #endregion

        #region Missile fields

        private static readonly string[] missileFields = {
                "terminalVelocity", "useAsInterceptor", "igniteDelay", "pulseThrottle",
                "salvoSpacing", "pulseDuration" };

        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Speed Limit",
            guiUnits = " m/s",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_FloatRange(
            minValue = 50f,
            maxValue = 2000f,
            stepIncrement = 50f,
            scene = UI_Scene.All
        )]
        public float terminalVelocity = 2000f;


        [UI.Tooltip(
            text = "How long should the missile wait after" +
                " decoupling before performing a kick to push" +
                " itself away from the firer?",
            title = "Kick Delay"
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Kick Delay",
            guiUnits = " s",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 2f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float igniteDelay = 0.2f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Kick Duration",
            guiUnits = " s",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 5f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float pulseDuration = 0.5f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Kick Throttle",
            guiUnits = "%",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 100f,
            stepIncrement = 5f,
            scene = UI_Scene.All
        )]
        public float pulseThrottle = 50f;


        [UI.Tooltip(
            text = "How long does the AI need to wait after launching" +
                " this missile before launching the next missile in a salvo?",
            title = "Salvo Spacing"
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Salvo Spacing",
            guiUnits = " s",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_FloatRange(
            minValue = 0.2f,
            maxValue = 5f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float salvoSpacing = 0.8f;


        // Missile staging isn't ready yet.

        /*[KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Terminal Time",
              guiUnits = " s",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
              UI_FloatRange(
                  minValue = 1f,
                  maxValue = 15f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float terminalTime = 5f;*/

        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Interception",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_Toggle(
            enabledText = "Enabled",
            disabledText = "Disabled",
            scene = UI_Scene.All
        )]
        public bool useAsInterceptor = false;


        #endregion

        #region Firework fields

        private static readonly string[] fireworkFields = {
                "FWRoundBurst", "FWBurstSpacing", "FWaccuracyTolerance", "FWBurstInterval" };

        // Not using turrets yet.

        /*[KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Type",
            groupName = fireworkGroupName,
            groupDisplayName = fireworkGroupName
        )]
        [UI_Toggle(
            enabledText = "Turret",
            disabledText = "Static",
            scene = UI_Scene.All
        )]
        public bool isTurret = false;*/

        public bool isTurret = false;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Firework Shot Burst",
            guiUnits = " Rounds",
            groupName = fireworkGroupName,
            groupDisplayName = fireworkGroupName
        )]
        [UI_FloatRange(
            minValue = 1f,
            maxValue = 8f,
            stepIncrement = 1f,
            scene = UI_Scene.All
        )]
        public float FWRoundBurst = 2f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Burst Round Spacing",
            guiUnits = " s",
            groupName = fireworkGroupName,
            groupDisplayName = fireworkGroupName
        )]
        [UI_FloatRange(
            minValue = 0.05f,
            maxValue = 1f,
            stepIncrement = 0.05f,
            scene = UI_Scene.All
        )]
        public float FWBurstSpacing = 0.25f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Burst Interval",
            guiUnits = " s",
            groupName = fireworkGroupName,
            groupDisplayName = fireworkGroupName
        )]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 5f,
            stepIncrement = 0.05f,
            scene = UI_Scene.All
        )]
        public float FWBurstInterval = 0.5f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Aim Tolerance",
            guiUnits = " Target Rad.",
            groupName = fireworkGroupName,
            groupDisplayName = fireworkGroupName
        )]
        [UI_FloatRange(
            minValue = 0.1f,
            maxValue = 2f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float FWaccuracyTolerance = 1f;


        #endregion

        #region Rocket fields

        private static readonly string[] rocketFields = {
                "firingInterval", "fireCountdown", "accuracyTolerance", "fireSymmetry" };

        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Firing Interval",
            guiUnits = " Seconds",
            groupName = rocketGroupName,
            groupDisplayName = rocketGroupName
        )]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 10f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float firingInterval = 1f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Firing Countdown",
            guiUnits = " Seconds",
            groupName = rocketGroupName,
            groupDisplayName = rocketGroupName
        )]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 10f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float fireCountdown = 0.5f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Aim Tolerance",
            guiUnits = " Target Rad.",
            groupName = rocketGroupName,
            groupDisplayName = rocketGroupName
        )]
        [UI_FloatRange(
            minValue = 0.1f,
            maxValue = 2f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float accuracyTolerance = 0.5f;


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Fire Symmetrical Rockets",
            groupName = rocketGroupName,
            groupDisplayName = rocketGroupName
        )]
        [UI_Toggle(
            enabledText = "Enabled",
            disabledText = "Disabled",
            scene = UI_Scene.All
        )]
        public bool fireSymmetry = false;


        #endregion

        #region Unused fields.

        // Mass cannon fields.

        /*[KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Round Muzzle Velocity",
              guiUnits = "m/s",
              groupName = MCGroupName,
              groupDisplayName = MCGroupName),
              UI_FloatRange(
                  minValue = 1f,
                  maxValue = 10000f,
                  stepIncrement = 50f,
                  scene = UI_Scene.All
              )]
        public float MCMuzzleVelocity = 250f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Mass Cannon Firing Length",
              guiUnits = " Seconds",
              groupName = MCGroupName,
              groupDisplayName = MCGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 10f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float MCFireTime = 1f;*/

        // Bomb fields.

        /*[KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Release Velocity",
              guiUnits = "m/s",
              groupName = BombGroupName,
              groupDisplayName = BombGroupName),
              UI_FloatRange(
                  minValue = 30f,
                  maxValue = 500f,
                  stepIncrement = 10f,
                  scene = UI_Scene.All
              )]
        public float BombReleaseVelocity = 300f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Minimum Safe Distance",
              guiUnits = "m",
              groupName = BombGroupName,
              groupDisplayName = BombGroupName),
              UI_FloatRange(
                  minValue = 50f,
                  maxValue = 1000f,
                  stepIncrement = 50f,
                  scene = UI_Scene.All
              )]
        public float BombSafeDistance = 200f;*/

        #endregion
    }
}
