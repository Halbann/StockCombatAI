using System.Linq;

using KerbalCombatSystems.UI;

namespace KerbalCombatSystems
{
    // todo: this shoud be a module on the controller that provides
    // a button to click to set a decoupler. With no decoupler being "Auto".

    public class ModuleDecouplerDesignate : PartModule
    {
        [KSPField(isPersistant = true)]
        public string seperatorType = "";

        [KSPField(isPersistant = true)]
        public bool seperated = false;

        private const string groupName = "Designation";
        public readonly static string[] types = new string[] { "Default", "Warhead", "EscapePod" };
        public readonly static string[] typeNames = new string[] { "Default", "Warhead", "Escape Pod" };

        [Tooltip(
            title = "Designation",
            text = "How should the AI think of this separator?\n"
            + "\n<b>Default:</b> Holds a weapon."
            + "\n<b>Warhead:</b> Part of a weapon."
            + "\n<b>Escape Pod:</b> Holds an escape pod."
        )]
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Type",
            groupName = groupName,
            groupDisplayName = groupName
        )]
        [UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None)]
        public string decouplerDesignation = "Default";

        private TooltipController tooltipController;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            SetupTypes();
            tooltipController = new TooltipController(this);
        }

        internal void OnDestroy()
        {
            tooltipController?.Dispose();
        }

        public void Separate()
        {
            switch(seperatorType)
            {
                case "anchor":
                    part.GetComponent<ModuleAnchoredDecoupler>()?.Decouple();
                    break;
                case "stack":
                    part.GetComponent<ModuleDecouple>()?.Decouple();
                    break;
                case "port":
                    ModuleDockingNode node = part.GetComponent<ModuleDockingNode>();
                    if (node == null || node.state == "Ready")
                        break;

                    node.Undock();

                    break;
                default:
                    Debug.Log("Improper Decoupler Designation");
                    break;
            }

            seperated = true;
        }

        private void SetupTypes()
        {
            // Pre-0.3.0 support.
            if (decouplerDesignation == "Escape Pod")
                decouplerDesignation = "EscapePod";

            UI_ChooseOption optionsField;

            if (HighLogic.LoadedSceneIsEditor)
                optionsField = Fields[nameof(decouplerDesignation)].uiControlEditor as UI_ChooseOption;
            else
                optionsField = Fields[nameof(decouplerDesignation)].uiControlFlight as UI_ChooseOption;

            optionsField.options = types;
            optionsField.display = typeNames;

            if (!types.Contains(decouplerDesignation))
                decouplerDesignation = "Default";
        }
    }
}
