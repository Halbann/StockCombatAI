using System.Linq;

namespace KerbalCombatSystems
{
    public class ModuleDecouplerDesignate : PartModule
    {
        [KSPField(isPersistant = true)]
        public string seperatorType = "";

        [KSPField(isPersistant = true)]
        public bool seperated = false;

        private const string groupName = "KCS Designation";
        public readonly static string[] types = new string[] { "Default", "Warhead", "EscapePod" };
        public readonly static string[] typeNames = new string[] { "Default", "Warhead", "Escape Pod" };

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

        public override void OnAwake()
        {
            SetupTypes();
        }

        public void Separate()
        {
            switch(seperatorType)
            {
                case "anchor":
                    part.GetComponent<ModuleAnchoredDecoupler>().Decouple();
                    break;
                case "stack":
                    part.GetComponent<ModuleDecouple>().Decouple();
                    break;
                case "port":
                    ModuleDockingNode node = part.GetComponent<ModuleDockingNode>();
                    if (node == null || node.state == "Ready")
                        break;

                    if (node.state == "Disengage" || node.state == "PreAttached")
                        node.Decouple();
                    else
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
