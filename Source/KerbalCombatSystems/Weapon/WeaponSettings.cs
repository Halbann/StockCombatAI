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

        const string distance = " m";
        const string time = " s";
        const string speed = " m/s";
        const string percent = "%";
        const string ratio = "x";
        const string radius = " target rad.";
        const string rpm = " RPM";


        #region Generic weapon fields
        const string weaponGroupName = "Weapon Settings";

        [UI.Tooltip(
            text = "The range to the target within which the AI should use this weapon.",
            title = "Range"
        )]
        [WeaponField("Range", weaponGroupName, distance)]
        [UI_MinMaxPow(
            minValueX = 50f,
            maxValueX = 15000f,
            minValueY = 50f,
            maxValueY = 15000f,
            stepIncrement = 50f
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
        [WeaponField("Mass Ratio", weaponGroupName, ratio)]
        [UI_FloatRange(
            minValue = 1f,
            maxValue = 10f,
            stepIncrement = 1f
        )]
        public float targetMassRatio = 5f;

        #endregion


        #region Missile fields
        const string missileGroup = "Missile Settings";

        private static readonly string[] missileFields = {
            "terminalVelocity", "useAsInterceptor", "igniteDelay", "pulseThrottle",
            "salvoSpacing", "pulseDuration" 
        };

        [WeaponField("Speed Limit", missileGroup, speed)]
        [UI_FloatRange(
            minValue = 50f,
            maxValue = 2000f,
            stepIncrement = 50f
        )]
        public float terminalVelocity = 2000f;

        [UI.Tooltip(
            text = "How long should the missile wait after" +
                " decoupling before performing a kick to push" +
                " itself away from the firer?",
            title = "Kick Delay"
        )]
        [WeaponField("Kick Delay", missileGroup, time)]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 2f,
            stepIncrement = 0.1f
        )]
        public float igniteDelay = 0.2f;

        [WeaponField("Kick Duration", missileGroup, time)]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 5f,
            stepIncrement = 0.1f
        )]
        public float pulseDuration = 0.5f;

        [WeaponField("Kick Throttle", missileGroup, percent)]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 100f,
            stepIncrement = 5f
        )]
        public float pulseThrottle = 50f;


        [UI.Tooltip(
            text = "How long does the AI need to wait after launching" +
                " this missile before launching the next missile in a salvo?",
            title = "Salvo Spacing"
        )]
        [WeaponField("Salvo Spacing", missileGroup, time)]
        [UI_FloatRange(
            minValue = 0.2f,
            maxValue = 5f,
            stepIncrement = 0.1f
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

        [WeaponField("Use for Interception", missileGroup)]
        [UI_Toggle(
            enabledText = "Enabled",
            disabledText = "Disabled"
        )]
        public bool useAsInterceptor = false;

        #endregion


        #region Firework fields
        const string fireworkGroup = "Firework Settings";

        private static readonly string[] fireworkFields = {
            "FWRoundBurst", 
            "FWBurstSpacing", 
            "FWaccuracyTolerance", 
            "FWBurstInterval",
            "volleyFire"
        };

        // Max should be part specific.
        [WeaponField("Fire Rate", fireworkGroup, rpm)]
        [UI_FloatRange(
            minValue = 60f,
            maxValue = 1200,
            stepIncrement = 10f
        )]
        public float FWBurstSpacing = 300;

        [WeaponField("Burst Size", fireworkGroup, " rounds")]
        [UI_FloatRange(
            minValue = 1f,
            maxValue = 8f,
            stepIncrement = 1f
        )]
        public float FWRoundBurst = 3f;

        [WeaponField("Time Between Bursts", fireworkGroup, time)]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 3f,
            stepIncrement = 0.05f
        )]
        public float FWBurstInterval = 0.5f;

        [WeaponField("Aim Tolerance", fireworkGroup, radius)]
        [UI_FloatRange(
            minValue = 0.1f,
            maxValue = 2f,
            stepIncrement = 0.1f
        )]
        public float FWaccuracyTolerance = 0.5f;

        [UI.Tooltip(
            text = "Fire all launchers in the cluster at once rather " +
            "than firing one until it runs out of shells.",
            title = "Volley Fire"
        )]
        [WeaponField("Volley Fire", fireworkGroup)]
        [UI_Toggle(
            enabledText = "Enabled",
            disabledText = "Disabled"
        )]
        public bool volleyFire = false;

        #endregion


        #region Rocket fields
        const string rocketGroupName = "Rocket Settings";

        private static readonly string[] rocketFields = {
            "firingInterval", "fireCountdown", "accuracyTolerance",
            "fireSymmetry"
        };

        [WeaponField("Firing Interval", rocketGroupName, time)]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 10f,
            stepIncrement = 0.1f
        )]
        public float firingInterval = 1f;

        [WeaponField("Firing Countdown", rocketGroupName, time)]
        [UI_FloatRange(
            minValue = 0f,
            maxValue = 10f,
            stepIncrement = 0.1f
        )]
        public float fireCountdown = 0.5f;

        [WeaponField("Aim Tolerance", rocketGroupName, radius)]
        [UI_FloatRange(
            minValue = 0.1f,
            maxValue = 2f,
            stepIncrement = 0.1f
        )]
        public float accuracyTolerance = 0.5f;

        [WeaponField("Fire Symmetrical Rockets", rocketGroupName)]
        [UI_Toggle(
            enabledText = "Enabled",
            disabledText = "Disabled"
        )]
        public bool fireSymmetry = false;

        #endregion


        public void UpgradeSettings()
        {
            // 0.3.0 - convert time to RPM.
            if (FWBurstSpacing <= 1f)
                FWBurstSpacing = 60f / FWBurstSpacing;
        }
    }

    class WeaponField : KSPField
    {
        public WeaponField(string name, string group, string unit = "") : base()
        {
            groupDisplayName = name;
            groupName = group;
            guiName = name;
            guiUnits = unit;
            isPersistant = true;
            guiActive = true;
            guiActiveEditor = true;
        }
    }
}
