using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

using UnityEngine;

using KSP.UI.Screens;

using static KerbalCombatSystems.Utils;
using KerbalCombatSystems.UI;
using KerbalCombatSystems.Weapon;

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
        public Part AimPart =>
            typeModule?.AimPart ?? null;

        public float targetSize;

        [KSPField(isPersistant = true)]
        public bool canFire = true;

        // Specific to missiles.
        [KSPField(isPersistant = true)]
        public bool launched = false;

        public Side side;
        public float mass = -1f;
        public float dryMass = -1f;
        public int childDecouplers;
        public LaunchType launchType = LaunchType.Radial;
        public bool missed = false;
        public bool hit = false;
        public bool isInterceptor = false;
        public ModuleWeaponController targetWeapon;
        public List<ModuleWeaponController> interceptedBy = new List<ModuleWeaponController>();
        public float timeToHit = -1;

        public ModuleMissile Missile => (ModuleMissile)typeModule;
        public ModuleFirework Firework => (ModuleFirework)typeModule;

        public bool IsProjectile => projectileTypes.Contains(weaponType);

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
            weaponCode = code;
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

        public override void OnAwake()
        {
            base.OnAwake();
            FlightManager.Register(this);
        }

        public override void OnStartFinished(StartState state)
        {
            UpgradeSettings();
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
                    UpdateMass();
                    CountChildDecouplers();
                }

                if (weaponType == "Firework")
                    Setup();
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

            FlightManager.Unregister(this);
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
            {
                ScreenMessages.PostScreenMessage($"Can't synchronise a weapon without a weapon code.");
                return;
            }

            List<ModuleWeaponController> modules;

            if (HighLogic.LoadedSceneIsEditor)
                modules = EditorLogic.SortedShipList.SelectMany(p => p.FindModulesImplementing<ModuleWeaponController>()).ToList();
            else if (HighLogic.LoadedSceneIsFlight)
                modules = vessel.FindPartModulesImplementing<ModuleWeaponController>();
            else
                return;

            int count = 0;
            foreach (ModuleWeaponController module in modules)
            {
                if (module.weaponCode.ToLower() == weaponCode.ToLower() && module != this)
                {
                    foreach (BaseField field in module.Fields)
                        if (field.isPersistant && (field.guiActiveEditor || field.guiActive))
                            field.SetValue(field.GetValue(this), module);

                    if (HighLogic.LoadedSceneIsFlight && module.setup)
                        module.UpdateSettings();

                    count++;
                }
            }

            if (count > 0)
                ScreenMessages.PostScreenMessage($"Copied settings to {count} other {weaponCode} controller{(count > 1 ? "s" : "")}.");
            else
                ScreenMessages.PostScreenMessage($"No other {weaponCode} controllers found.");
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

        private void UpdateMass(Part decoupler = null)
        {
            if (decoupler == null)
            {
                decoupler = FindDecoupler(part)?.part;
                if (decoupler == null)
                    return;
            }

            mass = 0;
            dryMass = 0;
            var parts = decoupler.FindChildParts<Part>(true);

            foreach (Part part in parts)
            {
                if (CheckDecoupler(part, out _, "Default"))
                    break;

                mass += part.mass + part.GetResourceMass();
                dryMass += part.mass;
            }

            if (mass <= 0)
                throw new Exception($"[KCS]: Trying to calculate mass on {vessel.vesselName} {weaponCode} but mass is {mass}.");
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

            // todo: investigate if this happens. I don't think it should?
            if (mass == 0)
                throw new Exception($"[KCS]: Trying to calculate acceleration on {vessel.vesselName} {weaponCode} but mass is {mass}.");

            return thrust / mass;
        }

        internal bool Identical(ModuleWeaponController otherWeapon)
        {
            // Check that a weapon is functionally identical to another weapon.
            // For creating salvos.

            if (otherWeapon == null)
                return false;

            if (weaponCode != "" || otherWeapon.weaponCode != "")
                return weaponCode.ToLower() == otherWeapon.weaponCode.ToLower();
            else
                return Approximately(dryMass, otherWeapon.dryMass, 0.05f);
        }
    }
}
