using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

using UnityEngine;

using KSP.UI.Screens;

using static KerbalCombatSystems.Utils;
using KerbalCombatSystems.UI;

namespace KerbalCombatSystems
{
    public class ModuleWeaponController : PartModule
    {
        // Generic.
        public Vessel target;
        public ModuleWeapon typeModule;
        public bool setup = false;
        private TooltipController tooltips;

        [KSPField(isPersistant = true)]
        public string weaponType;

        // todo: none of these specific fields should exist.
        // the module part of the weapon controller should
        // be stripped to bare essentials, specifics should be
        // hidden at lower level.

        // Specific to rockets and fireworks.
        public Part aimPart;
        public float targetSize;

        [KSPField(isPersistant = true)]
        public bool canFire = true;

        // Specific to missiles.
        [KSPField(isPersistant = true)]
        public bool launched = false;

        public Side side;
        public float mass = -1;
        public int childDecouplers;
        public int frontLaunch = 0;
        public bool missed = false;
        public bool hit = false;
        public bool isInterceptor = false;
        public ModuleWeaponController targetWeapon;
        public List<ModuleWeaponController> interceptedBy = new List<ModuleWeaponController>();
        public float timeToHit = -1;

        public ModuleMissile Missile
        {
            get => (ModuleMissile)typeModule;
        }

        public static string[] types = { "Missile", "Rocket", "Firework", "Bomb", "MassCannon" };
        public static string[] massTypes = { "Missile", "Rocket", "Bomb" };
        public static string[] projectileTypes = { "Rocket", "Firework" };

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


        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Clearance Distance",
            guiUnits = " m",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName
        )]
        [UI_FloatRange(
            minValue = 0,
            maxValue = 20f,
            stepIncrement = 0.1f,
            scene = UI_Scene.All
        )]
        public float clearanceDistance = 0.5f;


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

        #region Weapon Code

        // Set persistent weapon code in editor and flight.

        [KSPField(isPersistant = true)]
        public string weaponCode = "";

        [KSPEvent(
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Weapon Code",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName,
            name = "weaponCodeEvent"
        )]
        public void OpenWeaponCodeGUI()
        {
            if (part.vesselNaming == null)
                part.vesselNaming = new VesselNaming();

            part.vesselNaming.vesselName = weaponCode;
            part.partInfo.showVesselNaming = false;

            VesselRenameDialog.SpawnNameFromPart(
                part, 
                SetWeaponCodeCallback, 
                WeaponCodeUIRemoveDismiss,
                WeaponCodeUIRemove, false, VesselType.Probe);
        }

        private void SetWeaponCodeCallback(string code, VesselType t, int i) =>
            SetWeaponCode(code);

        public void SetWeaponCode(string code)
        {
            weaponCode = code.ToUpper();
            UpdateWeaponCodeUI();
        }

        private void UpdateWeaponCodeUI()
        {
            part.vesselNaming = null;

            var e = Events["OpenWeaponCodeGUI"];
            var name = weaponCode == "" ? "None" : weaponCode;
            e.guiName = "Weapon Code:                            " + name;
        }

        // This needs to exist for the dialog to work.
        private void WeaponCodeUIRemoveDismiss() {
            part.vesselNaming = null;
        }

        private void WeaponCodeUIRemove()
        {
            weaponCode = "";
            UpdateWeaponCodeUI();
        }

        #endregion

        #region Type Switching

        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            weaponType = variant.Name;
            UpdateUI();
        }

        private void UpdateUI()
        {

            string[] missileFields = { 
                "terminalVelocity", "useAsInterceptor", "igniteDelay", "pulseThrottle", 
                "salvoSpacing", "pulseDuration", "clearanceDistance" };
            //"terminalTime"

            string[] fireworkFields = {
                "FWRoundBurst", "FWBurstSpacing", "FWaccuracyTolerance", "FWBurstInterval" };

            string[] rocketFields = {
                "firingInterval", "fireCountdown", "accuracyTolerance", "fireSymmetry" };

            // todo: this is still stupid. this stuff should be hidden at the module level.

            UpdateFieldGroupVisibility("Missile", missileFields);
            UpdateFieldGroupVisibility("Firework", fireworkFields);
            UpdateFieldGroupVisibility("Rocket", rocketFields);

            RefreshAssociatedWindows(part);
            StartCoroutine(RefreshTooltips());
        }

        private IEnumerator RefreshTooltips()
        {
            // We're refreshing tooltips right after marking the part window to be recreated,
            // so we need for that to actually happen, otherwise our new tooltips will be destroyed.

            if (part.PartActionWindow == null)
                yield break;

            float time = Time.time + 0.5f;
            while (part.PartActionWindow.displayDirty && Time.time < time)
                yield return null;

            tooltips?.RefreshTooltips();
        }

        private void UpdateFieldGroupVisibility(string groupName, string[] fieldNames)
        {
            bool visible = weaponType == groupName;
            BaseField field;

            foreach (var fieldName in fieldNames)
            {
                try
                {
                    field = Fields[fieldName];
                    field.guiActive = visible;
                    field.guiActiveEditor = visible;
                }
                catch
                {
                    Debug.LogError($"Couldn't find weapon controller field {fieldName}.");
                }
            }
        }

        private static void RefreshAssociatedWindows(Part part)
        {
            IEnumerator<UIPartActionWindow> window = FindObjectsOfType(typeof(UIPartActionWindow)).Cast<UIPartActionWindow>().GetEnumerator();
            while (window.MoveNext())
            {
                if (window.Current == null) continue;
                if (window.Current.part == part)
                {
                    window.Current.displayDirty = true;
                }
            }
            window.Dispose();
        }

        #endregion

        public override void OnStartFinished(StartState state)
        {
            weaponCode = weaponCode.ToUpper();
            UpdateWeaponCodeUI();

            if (types.IndexOf(weaponType) == -1)
                weaponType = part.variants.SelectedVariant.Name;

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);
            else if (HighLogic.LoadedSceneIsFlight)
                Setup();

            UpdateUI();

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (massTypes.Contains(weaponType))
                {
                    CalculateMass();
                    CountChildDecouplers();
                }
            }

            tooltips = new TooltipController(this);

            // Conversion of old mass ratio increment to new.
            targetMassRatio = Mathf.Round(targetMassRatio);

            GameEvents.onPartActionUIDismiss.Add(OnPartActionUIDismiss);
        }

        internal void OnDestroy()
        {
            GameEvents.onEditorVariantApplied.Remove(OnVariantApplied);
            GameEvents.onPartActionUIDismiss.Remove(OnPartActionUIDismiss);

            tooltips?.Dispose();
        }

        private void OnPartActionUIDismiss(Part data)
        {
            if (part.persistentId != data.persistentId)
                return;

            if (!HighLogic.LoadedSceneIsFlight)
                return;

            if (setup)
                UpdateSettings();
        }

        // todo: implement tooltips for KSPEvent.
        [UI.Tooltip(
            title = "Sync Settings",
            text = "Copy all settings to any other weapon controllers on this vessel with the same code."
        )]
        [KSPEvent(
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Sync Settings",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName
        )]
        public void ShareSettings()
        {
            if (weaponCode == "")
                return;

            List<ModuleWeaponController> modules;

            if (HighLogic.LoadedSceneIsEditor)
            {
                modules = EditorLogic.SortedShipList.SelectMany(p => p.FindModulesImplementing<ModuleWeaponController>()).ToList();
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                modules = vessel.FindPartModulesImplementing<ModuleWeaponController>();
            }
            else
            {
                return;
            }

            foreach (ModuleWeaponController module in modules)
            {
                if (module.weaponCode == weaponCode)
                {
                    foreach (BaseField field in module.Fields)
                    {
                        //if (field.isPersistant && (field.guiActiveEditor || field.guiActive))
                        //    field.SetValue(module, field.GetValue(this));
                        if (field.isPersistant && (field.guiActiveEditor || field.guiActive))
                            field.SetValue(field.GetValue(this), module);
                    }

                    if (HighLogic.LoadedSceneIsFlight && module.setup)
                        module.UpdateSettings();
                }
            }
        }

        public void Setup()
        {
            if (setup)
                return;

            string moduleName;
            switch (weaponType)
            {
                case "Missile":
                    moduleName = "ModuleMissile";
                    break;
                case "Rocket":
                    moduleName = "ModuleRocket";
                    break;
                case "Firework":
                    moduleName = "ModuleFirework";
                    break;
                case "MassCannon":
                    moduleName = "ModuleMassCannon";
                    break;
                case "Bomb":
                    moduleName = "ModuleBomb";
                    break;
                default:
                    Debug.LogError($"Couldn't find a module for {weaponType}.");
                    return;
            }

            if (part.GetComponent(moduleName) == null)
                typeModule = (ModuleWeapon)part.AddModule(moduleName);

            typeModule.Setup();
            setup = true;
        }

        public Vector3 Aim()
        {
            if (!setup)
                Setup();

            return typeModule.Aim();
        }

        public void UpdateSettings()
        {
            typeModule.UpdateSettings();
        }

        // 'Fire' button.

        [KSPEvent(
            guiActive = true,
            guiActiveEditor = false,
            guiName = "Fire",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName
        )]
        public void Fire()
        {
            if (!setup)
                Setup();

            typeModule.Fire();
        }

        [KSPAction("Fire")]
        public void FireAction(KSPActionParam _) =>
            Fire();


        // todo: These functions are only relevant to certain types.
        // Mostly missiles.

        private float CalculateMass(Part decoupler = null, bool useLast = true)
        {
            if (mass > 0 && useLast) return mass;

            if (decoupler == null)
            {
                var module = FindDecoupler(part);
                if (module == null) return 1.0f;
                decoupler = module.part;
            }

            float totalMass = 0;
            var parts = decoupler.FindChildParts<Part>(true);

            foreach (Part part in parts)
            {
                if (part.partInfo.category == PartCategories.Coupling) break;
                totalMass = totalMass + part.mass + part.GetResourceMass();
            }

            return mass = totalMass;
        }

        private void CountChildDecouplers()
        {
            Part parent;
            var decoupler = FindDecoupler(part);

            if (decoupler != null)
                parent = decoupler.part;
            else
                parent = part.parent;

            childDecouplers = FindDecouplerChildren(parent).Count;
        }

        public float CalculateAcceleration(Part decoupler = null)
        {
            if (decoupler == null)
                decoupler = FindDecoupler(part).part;

            var children = decoupler.FindChildParts<Part>(true).ToList();

            var engines = new List<ModuleEngines>();
            ModuleEngines engineModule;

            foreach (var p in children)
            {
                engineModule = p.FindModuleImplementing<ModuleEngines>();

                if (engineModule != null)
                    engines.Add(engineModule);
            }

            float thrust = engines.Sum(e => e.MaxThrustOutputVac(true));
            float mass = CalculateMass(decoupler, false);

            return thrust / mass;
        }
    }
}
