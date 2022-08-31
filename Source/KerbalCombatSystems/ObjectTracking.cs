using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleObjectTracking : PartModule
    {
        List<ModuleAnimationGroup> SpinAnims;
        //all ore scanning parts plus the sentinal at superlong range

        const string objectTrackingGroupName = "Situational Awareness";

        [KSPField(isPersistant = true)]
        public string SensorSize;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true, 
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = objectTrackingGroupName,
              groupDisplayName = objectTrackingGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float detectionRange = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", detectionRange));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            SpinAnims = part.FindModulesImplementing<ModuleAnimationGroup>();
        }


        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            SensorSize = variant.Name;
            ModuleResourceScanner OreScanner = part.FindModuleImplementing<ModuleResourceScanner>();

            Debug.Log(SensorSize);

            switch (SensorSize)
            {
                case "Medium":
                    detectionRange = 10000f;
                    OreScanner.MaxAbundanceAltitude = 500000;//500km
                    break;
                case "Short":
                    detectionRange = 5000f;
                    OreScanner.MaxAbundanceAltitude = 100000;//100km
                    break;
                case "Dome":
                    detectionRange = 2500f;
                    OreScanner.MaxAbundanceAltitude = 30000;//30km
                    break;
                default:
                    Debug.Log("variant not found");
                    //it's a non-variant scanner, no need to modify ranges
                    break;
            }

            UpdateUI();
        }

        private void UpdateUI()
        {
            SpinAnims[0].moduleIsEnabled = SensorSize == "Medium";
            SpinAnims[1].moduleIsEnabled = SensorSize == "Short";

            SpinAnims[0].Fields["Deploy <<1>>"].guiActive = SensorSize == "Medium";
            SpinAnims[0].Fields["Retract <<1>>"].guiActive = SensorSize == "Medium";
            SpinAnims[0].Fields["Deploy <<1>>"].guiActiveEditor = SensorSize == "Medium";
            SpinAnims[0].Fields["Retract <<1>>"].guiActiveEditor = SensorSize == "Medium";

            Debug.Log(SpinAnims[0].deployActionName);//returns above values
            Debug.Log(SpinAnims[0].retractActionName);//returns above values

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
