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
        private int ScalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true, 
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = objectTrackingGroupName,
              groupDisplayName = objectTrackingGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float DetectionRange = 0f;

        [KSPField(isPersistant = true)]
        public float BaseDetectionRange = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", DetectionRange));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            SpinAnims = part.FindModulesImplementing<ModuleAnimationGroup>();

            ScalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().ScalingFactor;
            DetectionRange = BaseDetectionRange * ScalingFactor;
        }


        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            string SensorSize = variant.Name;
            ModuleResourceScanner OreScanner = part.FindModuleImplementing<ModuleResourceScanner>();

            Debug.Log(SensorSize);

            switch (SensorSize)
            {
                case "Medium":
                    BaseDetectionRange = 1000f;
                    DetectionRange = BaseDetectionRange * ScalingFactor;
                    OreScanner.MaxAbundanceAltitude = 500000;//500km
                    UpdateButtons(SpinAnims[0], SpinAnims[1]);
                    break;
                case "Short":
                    BaseDetectionRange = 500f;
                    DetectionRange = BaseDetectionRange * ScalingFactor;
                    OreScanner.MaxAbundanceAltitude = 100000;//100km
                    UpdateButtons(SpinAnims[1], SpinAnims[0]);
                    break;
                /*case "Dome":
                    detectionRange = 250f * ScalingFactor;
                    OreScanner.MaxAbundanceAltitude = 30000;//30km
                    break;*/
                default:
                    Debug.Log("variant not found");
                    //it's a non-variant scanner, no need to modify ranges
                    break;
            }
        }

        private void UpdateButtons(ModuleAnimationGroup EnabledAnim, ModuleAnimationGroup DisabledAnim)
        {
            // SpinAnims[0].moduleIsEnabled = SensorSize == "Medium";
            // SpinAnims[1].moduleIsEnabled = SensorSize == "Short";
            //SpinAnims[0].deployActionName

            EnabledAnim.enabled = true;

            EnabledAnim.Fields["Deploy <<1>>"].guiActive = true;
            EnabledAnim.Fields["Retract <<1>>"].guiActive = true;
            EnabledAnim.Fields["Deploy <<1>>"].guiActiveEditor = true;
            EnabledAnim.Fields["Retract <<1>>"].guiActiveEditor = true;


            /*SpinAnims[0].Events["Deploy <<1>>"].guiActive = SensorSize == "Medium";
            SpinAnims[0].Events["Retract <<1>>"].guiActive = SensorSize == "Medium";
            SpinAnims[0].Events["Deploy <<1>>"].guiActiveEditor = SensorSize == "Medium";
            SpinAnims[0].Events["Retract <<1>>"].guiActiveEditor = SensorSize == "Medium";*/

            //Debug.Log(SpinAnims[0].deployActionName);//returns above values
            //Debug.Log(SpinAnims[0].retractActionName);//returns above values

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
