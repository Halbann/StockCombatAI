using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    public class ModuleWeaponController : PartModule
    {
        const string weaponGroupName = "Weapon Settings";
        const string missileGroupName = "Missile Settings";
        const string rocketGroupName = "Rocket Settings";
        const string FireworkGroupName = "Firework Settings";
        const string MCGroupName = "Mass Cannon Settings";
        const string BombGroupName = "Bomb Settings";

        // Generic weapon variables.

        public Vessel target;
        public float mass = -1;
        public Side side;
        public int childDecouplers;

        [KSPField(isPersistant = true)]
        public bool canFire = true;

        [KSPField(isPersistant = true)]
        public string weaponType;

        [KSPField(isPersistant = true)]
        public bool launched = false;

        public static string[] types = { "Missile", "Rocket", "Firework", "Bomb", "MassCannon" };
        public static string[] massTypes = { "Missile", "Rocket", "Bomb" };
        public static string[] projectileTypes = { "Rocket", "Firework" };

        public ModuleWeapon typeModule;
        public Part aimPart;
        public bool setup = false;
        public float targetSize;

        #region Generic weapon fields

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Targeting Range",
            guiUnits = "m",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName),
            UI_MinMaxRange(
                minValueX = 50f,
                maxValueX = 5000f,
                minValueY = 100f,
                maxValueY = 5000f,
                stepIncrement = 50f,
                scene = UI_Scene.All
            )]
        public Vector2 MinMaxRange = new Vector2(500f, 1000f);

        [KSPField(isPersistant = true,
               guiActive = true,
               guiActiveEditor = true,
               guiName = "Mass Ratio",
               guiUnits = "x",
               groupName = weaponGroupName,
               groupDisplayName = weaponGroupName),
               UI_FloatRange(
                   minValue = 1f,
                   maxValue = 10f,
                   stepIncrement = 0.1f,
                   scene = UI_Scene.All
               )]
        public float targetMassRatio = 5f;

        #endregion

        #region Missile fields

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Speed Limit",
              guiUnits = " m/s",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
              UI_FloatRange(
                  minValue = 50f,
                  maxValue = 2000f,
                  stepIncrement = 50f,
                  scene = UI_Scene.All
              )]
        public float terminalVelocity = 2000f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Launch Delay",
              guiUnits = " s",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 2f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float igniteDelay = 0.2f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Launch Duration",
              guiUnits = " s",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 5f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float pulseDuration = 0.5f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Launch Throttle",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 1f,
                  stepIncrement = 0.01f,
                  scene = UI_Scene.All
              )]
        public float pulseThrottle = 0.5f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Salvo Spacing",
              guiUnits = " s",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
              UI_FloatRange(
                  minValue = 0.2f,
                  maxValue = 5f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float salvoSpacing = 0.8f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Interception",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool useAsInterceptor = false;

        /*[KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Velocity Match",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName),
        UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
        )]
        public bool MatchTargetVelocity = true;*/

        public bool frontLaunch = false;
        public bool missed = false;
        public bool hit = false;
        public bool isInterceptor = false;
        public ModuleWeaponController targetWeapon;
        public List<ModuleWeaponController> interceptedBy = new List<ModuleWeaponController>();
        public float timeToHit = -1;

        public ModuleMissile missile
        {
            get => (ModuleMissile)typeModule;
        }

        #endregion

        #region Bomb fields

        [KSPField(isPersistant = true,
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
        public float BombSafeDistance = 200f;

        #endregion

        #region Firework fields

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Firework Shot Burst",
              guiUnits = " Rounds",
              groupName = FireworkGroupName,
              groupDisplayName = FireworkGroupName),
              UI_FloatRange(
                  minValue = 1f,
                  maxValue = 8f,
                  stepIncrement = 1f,
                  scene = UI_Scene.All
              )]
        public float FWRoundBurst = 2f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Burst Round Spacing",
              guiUnits = " s",
              groupName = FireworkGroupName,
              groupDisplayName = FireworkGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 1f,
                  stepIncrement = 0.05f,
                  scene = UI_Scene.All
              )]
        public float FWBurstSpacing = 0.25f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Burst Interval",
              guiUnits = " s",
              groupName = FireworkGroupName,
              groupDisplayName = FireworkGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 5f,
                  stepIncrement = 0.05f,
                  scene = UI_Scene.All
              )]
        public float FWBurstInterval = 0.5f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Aim Tolerance",
              guiUnits = " Target Rad.",
              groupName = FireworkGroupName,
              groupDisplayName = FireworkGroupName),
              UI_FloatRange(
                  minValue = 0.1f,
                  maxValue = 2f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float FWaccuracyTolerance = 1f;

        // Hidden until implementation.
        /*[KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Flak",
            groupName = FireworkGroupName,
            groupDisplayName = FireworkGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool FWUseAsCIWS = false;*/

        #endregion

        #region Rocket fields

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Firing Interval",
              guiUnits = " Seconds",
              groupName = rocketGroupName,
              groupDisplayName = rocketGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 10f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float firingInterval = 1f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Firing Countdown",
              guiUnits = " Seconds",
              groupName = rocketGroupName,
              groupDisplayName = rocketGroupName),
              UI_FloatRange(
                  minValue = 0f,
                  maxValue = 10f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float fireCountdown = 0.5f;

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Aim Tolerance",
              guiUnits = " Target Rad.",
              groupName = rocketGroupName,
              groupDisplayName = rocketGroupName),
              UI_FloatRange(
                  minValue = 0.1f,
                  maxValue = 2f,
                  stepIncrement = 0.1f,
                  scene = UI_Scene.All
              )]
        public float accuracyTolerance = 0.5f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Fire Symmetrical Rockets",
            groupName = rocketGroupName,
            groupDisplayName = rocketGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool fireSymmetry = false;


        #endregion

        #region Mass Cannon fields

        // Mass cannon fields.

        [KSPField(isPersistant = true,
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
        public float MCFireTime = 1f;

        #endregion

        #region Weapon Code

        // Set persistent weapon code in editor and flight.

        [KSPField(isPersistant = true)]
        public string weaponCode = "";

        [KSPEvent(guiActive = true,
                  guiActiveEditor = true,
                  guiName = "Weapon Code",
                  groupName = weaponGroupName,
                  groupDisplayName = weaponGroupName,
                  name = "weaponCodeEvent")]
        public void SetWeaponCode()
        {
            if (part.vesselNaming == null)
                part.vesselNaming = new VesselNaming();

            part.vesselNaming.vesselName = weaponCode;
            part.partInfo.showVesselNaming = false;

            VesselRenameDialog.SpawnNameFromPart(part, SetWeaponCodeCallback, Dismiss, Remove, false, VesselType.Probe);
        }

        public void SetWeaponCodeCallback(String code, VesselType t, int i)
        {
            weaponCode = code.ToUpper();
            UpdateWeaponCodeUI();
        }

        private void UpdateWeaponCodeUI()
        {
            part.vesselNaming = null;

            var e = Events["SetWeaponCode"];
            var name = weaponCode == "" ? "None" : weaponCode;
            e.guiName = "Weapon Code:                            " + name;
        }

        // This needs to exist for the dialog to work.
        public void Dismiss() {
            part.vesselNaming = null;
        }

        public void Remove()
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
            // Missile fields.
            Fields["terminalVelocity"].guiActive = weaponType == "Missile";
            Fields["terminalVelocity"].guiActiveEditor = weaponType == "Missile";
            Fields["useAsInterceptor"].guiActive = weaponType == "Missile";
            Fields["useAsInterceptor"].guiActiveEditor = weaponType == "Missile";
            Fields["igniteDelay"].guiActive = weaponType == "Missile";
            Fields["igniteDelay"].guiActiveEditor = weaponType == "Missile";
            Fields["pulseThrottle"].guiActive = weaponType == "Missile";
            Fields["pulseThrottle"].guiActiveEditor = weaponType == "Missile"; 
            Fields["salvoSpacing"].guiActive = weaponType == "Missile";
            Fields["salvoSpacing"].guiActiveEditor = weaponType == "Missile"; 
            Fields["pulseDuration"].guiActive = weaponType == "Missile";
            Fields["pulseDuration"].guiActiveEditor = weaponType == "Missile"; 
            Fields["igniteDelay"].guiActive = weaponType == "Missile";
            Fields["igniteDelay"].guiActiveEditor = weaponType == "Missile"; 

            // Firework fields.
            Fields["FWRoundBurst"].guiActive = weaponType == "Firework";
            Fields["FWRoundBurst"].guiActiveEditor = weaponType == "Firework";
            Fields["FWBurstSpacing"].guiActive = weaponType == "Firework";
            Fields["FWBurstSpacing"].guiActiveEditor = weaponType == "Firework";
            //Fields["FWUseAsCIWS"].guiActive = weaponType == "Firework";
            //Fields["FWUseAsCIWS"].guiActiveEditor = weaponType == "Firework";
            Fields["FWaccuracyTolerance"].guiActive = weaponType == "Firework";
            Fields["FWaccuracyTolerance"].guiActiveEditor = weaponType == "Firework";
            Fields["FWBurstInterval"].guiActive = weaponType == "Firework";
            Fields["FWBurstInterval"].guiActiveEditor = weaponType == "Firework";
            
            // Rocket fields.
            Fields["firingInterval"].guiActive = weaponType == "Rocket";
            Fields["firingInterval"].guiActiveEditor = weaponType == "Rocket";
            Fields["fireCountdown"].guiActive = weaponType == "Rocket";
            Fields["fireCountdown"].guiActiveEditor = weaponType == "Rocket";
            Fields["accuracyTolerance"].guiActive = weaponType == "Rocket";
            Fields["accuracyTolerance"].guiActiveEditor = weaponType == "Rocket";
            Fields["fireSymmetry"].guiActive = weaponType == "Rocket";
            Fields["fireSymmetry"].guiActiveEditor = weaponType == "Rocket";

            //Mass Cannon Fields
            Fields["MCMuzzleVelocity"].guiActive = weaponType == "Mass Cannon";
            Fields["MCMuzzleVelocity"].guiActiveEditor = weaponType == "Mass Cannon";
            Fields["MCFireTime"].guiActive = weaponType == "Mass Cannon";
            Fields["MCFireTime"].guiActiveEditor = weaponType == "Mass Cannon";

            //Bomb Fields
            Fields["BombSafeDistance"].guiActive = weaponType == "Bomb";
            Fields["BombSafeDistance"].guiActiveEditor = weaponType == "Bomb";
            Fields["BombReleaseVelocity"].guiActive = weaponType == "Bomb";
            Fields["BombReleaseVelocity"].guiActiveEditor = weaponType == "Bomb";

            RefreshAssociatedWindows(part);
        }

        public static void RefreshAssociatedWindows(Part part)
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

        private void OnDestroy()
        {
            GameEvents.onEditorVariantApplied.Remove(OnVariantApplied);
        }

        #endregion

        public override void OnStart(StartState state)
        {
            weaponCode = weaponCode.ToUpper();
            UpdateWeaponCodeUI();

            if (types.IndexOf(weaponType) == -1)
                weaponType = part.variants.SelectedVariant.Name;

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            UpdateUI();

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (massTypes.Contains(weaponType))
                    CalculateMass();

                CountChildDecouplers();
            }
        }

        internal float CalculateMass(Part decoupler = null, bool useLast = true)
        {
            if (mass > 0 && useLast) return mass;

            if (decoupler == null)
            {
                var module = KCS.FindDecoupler(part, "Weapon", true); // todo: set to false later
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

            mass = totalMass;
            return totalMass;
        }

        private void CountChildDecouplers()
        {
            Part parent;
            var decoupler = FindDecoupler(part, "Weapon", true); // todo: set to false later

            if (decoupler != null)
                parent = decoupler.part;
            else
                parent = part.parent;

            childDecouplers = FindDecouplerChildren(parent, "Default", true).Count;
        }

        public float CalculateAcceleration(Part decoupler = null)
        {
            if (decoupler == null)
                decoupler = FindDecoupler(part, "Weapon", true).part;

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

        public void Setup()
        {
            if (setup) return;

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
                    Debug.Log($"[KCS]: Couldn't find a module for {weaponType}.");
                    return;
            }

            if (part.GetComponent(moduleName) == null)
                typeModule = (ModuleWeapon)part.AddModule(moduleName);

            typeModule.Setup();
            setup = true;
        }

        public Vector3 Aim()
        {
            if (typeModule == null)
                Setup();

            return typeModule.Aim();
        }

        public void UpdateSettings()
        {
            typeModule.UpdateSettings();
        }

        // 'Fire' button.

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Fire",
                  groupName = weaponGroupName,
                  groupDisplayName = weaponGroupName)]
        public void Fire()
        {
            if (typeModule == null)
                Setup();

            typeModule.Fire();
        }
    }

    public class ModuleWeapon : PartModule
    {
        virtual public void Fire() { }

        virtual public Vector3 Aim()
        {
            return Vector3.zero;
        }

        virtual public void Setup() { }

        virtual public void UpdateSettings() { }
    }
}
