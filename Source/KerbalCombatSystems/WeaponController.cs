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
    public partial class ModuleWeaponController : PartModule
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
        public LaunchType launchType = LaunchType.Radial;
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

            if (!types.Contains(weaponType))
                weaponType = part.variants.SelectedVariant.Name;

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            // todo: why did I add this? it breaks missiles. Presumably to do with something else.
            //else if (HighLogic.LoadedSceneIsFlight)
            //    Setup();

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
