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

     
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Decoupler Type",
            groupName = "Decoupler Designation Scroll"),
            UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None,
            options = new string[] { "Default", "Missile", "Rocket", "Bomb", "Countermeasure", "Escape Pod"})]
        public string DecouplerType = "Default";

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Interception",
            groupName = DecouplerDesignationGroupName,
            groupDisplayName = DecouplerDesignationGroupName),
            UI_Toggle(enabledText = "Enabled", disabledText = "Disabled", scene = UI_Scene.All)]
        public bool useAsInterceptor = false;
    }
}
