using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine.UI;
using KSP.UI.TooltipTypes;

using KerbalCombatSystems.Data;

namespace KerbalCombatSystems.UI
{
    // Attribute used to define a tooltip in association with a field on a part module.

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
    public class Tooltip : Attribute
    {
        public string title = ":(";
        public string text = "Someone forgot to write this tooltip.";
    }


    // This class is used inside each part module to manage the creation and destruction of tooltip objects.

    // todo: implement as monobehaviour for easier destruction.
    [Settings(category = "Misc", displayName = "Misc.", visible = true)]
    public class TooltipController : IDisposable
    {
        [Setting] public static bool showTooltips = true;

        public Part part;
        public PartModule module;

        private bool tooltipAdded = false;
        private readonly List<KSP.UI.TooltipController> tooltips = new List<KSP.UI.TooltipController>();

        public TooltipController(PartModule module)
        {
            this.module = module;
            this.part = module.part;

            Setup();
        }

        ~TooltipController()
        {
            Dispose();
        }

        private void Setup()
        {
            GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);
        }

        public void RefreshTooltips()
        {
            RemoveTooltips();

            if (showTooltips)
                AddTooltips();
        }

        private void OnPartActionUIShown(UIPartActionWindow _, Part windowPart)
        {
            if (part == null || module == null)
                return;

            if (windowPart != part)
                return;

            if (tooltipAdded == showTooltips)
                return;

            RefreshTooltips();
        }

        // todo: add support for events (buttons).

        private void AddTooltips()
        {
            tooltipAdded = true;

            int fieldCount = module.Fields.Count;
            BaseField field;

            for (int i = 0; i < fieldCount; i++)
            {
                field = module.Fields[i];

                Tooltip customTooltip = (Tooltip)field.FieldInfo.GetCustomAttributes(typeof(Tooltip), false).FirstOrDefault();
                if (customTooltip == null)
                    continue;

                // Exit if the field is not visible. We cannot access the control.
                if (!field.guiActive || !field.guiActiveEditor)
                    continue;

                UI_Control control = HighLogic.LoadedSceneIsFlight ? field.uiControlFlight : field.uiControlEditor;
                UIPartActionItem partAction = control.partActionItem;

                if (control == null || partAction == null)
                {
                    //Debug.LogError("KCS: Tooltip control not found.");
                    continue;
                }

                // Exit if the tooltip if a tooltip exists already.
                if (control.partActionItem.gameObject.GetComponent<TooltipController>() != null)
                    continue;

                var tooltipController = control.partActionItem.gameObject.AddComponent<TooltipController_TitleAndText>();
                tooltips.Add(tooltipController);

                var tooltipPrefab = AssetBase.GetPrefab("Tooltip_TitleAndText")?.GetComponent<Tooltip_TitleAndText>();
                if (tooltipPrefab == null)
                {
                    Debug.LogError("Tooltip prefab not found.");
                    continue;
                }

                tooltipController.textString = customTooltip.text;
                tooltipController.titleString = customTooltip.title;

                // There's only a small chance that the UIMaster will use our prefab,
                // because they are cached for each tooltip type. Needed nonetheless.

                tooltipController.prefab = tooltipPrefab;
                tooltipController.TooltipPrefabType = tooltipPrefab;

                var selectable = control.partActionItem.gameObject.GetComponentInChildren<Selectable>();
                if (selectable == null)
                {
                    Debug.LogError("Tooltip selectable not found.");
                    continue;
                }

                // Set selectableBase via reflection.
                var selectableBase = tooltipController.GetType().GetField("selectableBase", BindingFlags.NonPublic | BindingFlags.Instance);
                selectableBase.SetValue(tooltipController, selectable);
            }
        }

        private void RemoveTooltips()
        {
            tooltipAdded = false;

            foreach (var tooltip in tooltips)
            {
                if (tooltip == null)
                    continue;

                UnityEngine.Object.Destroy(tooltip);
            }

            tooltips.Clear();
        }

        public void Dispose()
        {
            GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);
        }
    }
}
