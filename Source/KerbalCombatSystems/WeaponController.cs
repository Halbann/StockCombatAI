using KSP.UI.Screens;
using System;
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


        // Generic weapon variables.

        public Vessel target;

        [KSPField(isPersistant = true)]
        string weaponType;

        string[] types = { "Missile", "Rocket", "Bomb" }; 


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

            Debug.Log($"[KCS]: Firing weapon, let 'em have it!");
            
            string moduleName;
            
            switch (weaponType)
            {
                case "Missile":
                    moduleName = "ModuleMissile";
                    break;
                case "Rocket":
                    moduleName = "ModuleRocket";
                    break;
                case "Bomb":
                    moduleName = "ModuleBomb";
                    break;
                default:
                    Debug.Log($"[KCS]: Couldn't find a module for {weaponType}.");
                    return;
            }

            part.AddModule(moduleName, true);
        }

        public void Start()
        {
            UpdateWeaponCodeUI();

            if (types.IndexOf(weaponType) == -1)
            {
                //weaponType = part.baseVariant.Name;
                weaponType = part.variants.SelectedVariant.Name;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);
            }

            UpdateUI();
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
            Fields["terminalVelocity"].guiActive = weaponType == "Missile";
            Fields["terminalVelocity"].guiActiveEditor = weaponType == "Missile";
            Fields["useAsInterceptor"].guiActive = weaponType == "Missile";
            Fields["useAsInterceptor"].guiActiveEditor = weaponType == "Missile";
            Fields["MatchTargetVelocity"].guiActive = weaponType == "Missile";
            Fields["MatchTargetVelocity"].guiActiveEditor = weaponType == "Missile";
            // Add other fields like this: 
            // Fields["BombSpeed"].guiActiveEditor = weaponType == "Bomb";
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
