namespace KerbalCombatSystems
{
    public class ModuleDecouplerDesignate : PartModule
    {
        const string DecouplerDesignationGroupName = "Seperator Designation";

        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Seperator Type",
            groupName = DecouplerDesignationGroupName,
            groupDisplayName = DecouplerDesignationGroupName),
            UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None,
            options = new string[] { "Default", "Weapon", "Countermeasure", "Sub-Vessel", "Escape Pod", "Warhead" })]
        public string DecouplerType = "Default";
    }
}
