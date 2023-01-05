using UnityEngine;

namespace KerbalCombatSystems
{
    public class ModuleDecouplerDesignate : PartModule
    {
        //denote seperator type
        [KSPField(isPersistant = true)]
            public string seperatorType = "seperatorType";


        [KSPField(isPersistant = true)]
            public bool seperated = false;

        const string DecouplerDesignationGroupName = "KCS Designation";
        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Separator Type",
            groupName = DecouplerDesignationGroupName,
            groupDisplayName = DecouplerDesignationGroupName),
            UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None,
            options = new string[] { "Default", "Warhead", "Escape Pod" })] //re-add "Countermeasure" at later date
            public string decouplerDesignation = "Default";

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

                    if (node.state == "Disengage")
                        Debug.Log("hi");

                    if (node.state == "Disengage" || node.state == "PreAttached")
                        node.Decouple();
                    else
                        node.Undock();

                    break;
                default:
                    Debug.Log("[KCS]: Improper Decoupler Designation");
                    break;
            }
            seperated = true;
        }
    }
}
