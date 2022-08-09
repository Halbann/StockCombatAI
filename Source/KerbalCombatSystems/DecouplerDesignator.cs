using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;

namespace KerbalCombatSystems
{
    public class ModuleDecouplerDesignate : PartModule
    {
        const string DecouplerDesignationGroupName = "Decoupler Designation";

     
        [KSPField(
            isPersistant = true, 
            guiActive = true, 
            guiActiveEditor = true, 
            guiName = "Decoupler Type",
            groupName = DecouplerDesignationGroupName,
            groupDisplayName = DecouplerDesignationGroupName),
            UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None,
            options = new string[] { "Default", "Weapon", "Countermeasure", "Escape Pod", "Warhead" })]
        public string DecouplerType = "Default";
        
    }
}
