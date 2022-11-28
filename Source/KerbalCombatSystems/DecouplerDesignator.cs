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
            guiName = "Seperator Type",
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
                    part.GetComponent<ModuleDockingNode>().Undock();
                    break;
                default:
                    Debug.Log("[KCS]: Improper Decoupler Designation");
                    break;
            }
            seperated = true;
        }
    }
}
