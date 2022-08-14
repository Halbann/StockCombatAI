using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KerbalCombatSystems
{
    public class ModuleWeaponController : PartModule
    {
        // User parameters changed via UI.

        const string weaponGroupName = "Weapon Settings";
        const string missileGroupName = "Missile Guidance";
        const string rocketGroupName = "Rocket Settings";
        const string FireworkGroupName = "Firework Settings";
        const string MCGroupName = "Mass Cannon Settings";
        const string BombGroupName = "Bomb Settings";


        // Generic weapon fields.

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Targeting Range",
            guiUnits = "m",
            groupName = weaponGroupName,
            groupDisplayName = weaponGroupName),
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
               guiName = "Preferred Target Mass",
               guiUnits = "t",
               groupName = weaponGroupName,
               groupDisplayName = weaponGroupName),
               UI_FloatRange(
                   minValue = 0f,
                   maxValue = 1000f,
                   stepIncrement = 0.01f,
                   scene = UI_Scene.All
               )]
        public float TMassPreference = 50f;


        // Missile fields.

        [KSPField(isPersistant = true,
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Terminal Velocity",
              guiUnits = "m/s",
              groupName = missileGroupName,
              groupDisplayName = missileGroupName),
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
            guiName = "Use for Interception",
            groupName = missileGroupName,
            groupDisplayName = missileGroupName),
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
            groupName = missileGroupName,
            groupDisplayName = missileGroupName),
        UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
        )]
        public bool MatchTargetVelocity = true;

        // Rocket fields.

        // Bomb fields.

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


        // Firework fields.

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
              guiUnits = " Seconds",
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
            guiName = "Use for Flak",
            groupName = FireworkGroupName,
            groupDisplayName = FireworkGroupName),
            UI_Toggle(
                enabledText = "Enabled",
                disabledText = "Disabled",
                scene = UI_Scene.All
            )]
        public bool FWUseAsCIWS = false;


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
        

        // Generic weapon variables.

        public Vessel target;
        public float mass;

        [KSPField(isPersistant = true)]
        public string weaponType;

        string[] types = { "Missile", "Rocket", "Firework", "Bomb", "Mass Cannon" }; 


        // Set persistent weapon code in editor and flight.

        [KSPField(isPersistant = true)]
        public string weaponCode = "";

        [KSPEvent(guiActive = true,
                  guiActiveEditor = true,
                  guiName = "Set Weapon Code",
                  groupName = weaponGroupName,
                  groupDisplayName = weaponGroupName,
                  name = "weaponCodeEvent")]
        public void SetWeaponCode()
        {
            VesselRenameDialog.SpawnNameFromPart(part, SetWeaponCodeCallback, Dismiss, Remove, false, VesselType.Probe);
        }

        public void SetWeaponCodeCallback(String code, VesselType t, int i)
        {
            weaponCode = code;
            UpdateWeaponCodeUI();
        }

        private void UpdateWeaponCodeUI()
        {
            var e = Events["SetWeaponCode"];
            var name = weaponCode == "" ? "None" : weaponCode;
            e.guiName = "Set Weapon Code:                                " + name;

            if (part.vesselNaming == null)
                part.vesselNaming = new VesselNaming();

            part.vesselNaming.vesselName = weaponCode;
            part.partInfo.showVesselNaming = false;
        }

        // This needs to exist for the dialog to work.
        public void Dismiss() {}
        public void Remove()
        {
            weaponCode = "";
            UpdateWeaponCodeUI();
        }

        // 'Fire' button.

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Fire",
                  groupName = weaponGroupName,
                  groupDisplayName = weaponGroupName)]
        public void Fire()
        {
            if (target == null)
            {
                if (vessel.targetObject == null) return;
                target = vessel.targetObject.GetVessel();
            }

            // Delete after above is uncommented.
            if (target == null && vessel.targetObject != null)
            {
                target = vessel.targetObject.GetVessel();
            }

            Debug.Log($"[KCS]: " + vessel.GetName() + " firing " + weaponType);

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
                case "Mass Cannon":
                    moduleName = "ModuleMassCannon";
                    break;
                case "Bomb":
                    moduleName = "ModuleBomb";
                    break;
                default:
                    Debug.Log($"[KCS]: Couldn't find a module for {weaponType}.");
                    return;
            }

            part.AddModule(moduleName);
        }

        public override void OnStart(StartState state)
        {
            UpdateWeaponCodeUI();

            if (types.IndexOf(weaponType) == -1)
            {
                weaponType = part.variants.SelectedVariant.Name;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);
            }

            UpdateUI();

            String[] massTypes = {"Missile", "Rocket", "Bomb"};
            if (HighLogic.LoadedSceneIsFlight && massTypes.Contains(weaponType)) 
                CalculateMass();
        }

        private float CalculateMass()
        {
            var decoupler = KCS.FindDecoupler(part, "Weapon", true); // todo: set to false later
            float totalMass = 0;
            var parts = decoupler.part.FindChildParts<Part>(true);

            foreach (Part part in parts)
            {
                if (part.partInfo.category == PartCategories.Coupling) break;
                totalMass = totalMass + part.mass + part.GetResourceMass();
            }

            mass = (float)Math.Round(totalMass, 2);
            return totalMass;
        }

        private void OnDestroy()
        {
            GameEvents.onEditorVariantApplied.Remove(OnVariantApplied);
        }

        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            weaponType = variant.Name;
            UpdateUI();
        }
        private void UpdateUI()
        {
            //Missile Fields
            Fields["terminalVelocity"].guiActive = weaponType == "Missile";
            Fields["terminalVelocity"].guiActiveEditor = weaponType == "Missile";
            Fields["useAsInterceptor"].guiActive = weaponType == "Missile";
            Fields["useAsInterceptor"].guiActiveEditor = weaponType == "Missile";
            Fields["MatchTargetVelocity"].guiActive = weaponType == "Missile";
            Fields["MatchTargetVelocity"].guiActiveEditor = weaponType == "Missile";
            //Firework fields
            Fields["FWRoundBurst"].guiActive = weaponType == "Firework";
            Fields["FWRoundBurst"].guiActiveEditor = weaponType == "Firework";
            Fields["FWBurstSpacing"].guiActive = weaponType == "Firework";
            Fields["FWBurstSpacing"].guiActiveEditor = weaponType == "Firework";
            Fields["FWUseAsCIWS"].guiActive = weaponType == "Firework";
            Fields["FWUseAsCIWS"].guiActiveEditor = weaponType == "Firework";
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
    }
}
