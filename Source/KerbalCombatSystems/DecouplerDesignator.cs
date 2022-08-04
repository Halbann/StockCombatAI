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
        const string DecouplerDesignationGroupName = "Missile Guidance";

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Max Range",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_FloatRange(minValue = 200f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float maxRange = 1000f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Min Range",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_FloatRange(minValue = 100f, maxValue = 4000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float minRange = 500f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Use for Interception",
            groupName = missileGuidanceGroupName,
            groupDisplayName = missileGuidanceGroupName),
            UI_Toggle(enabledText = "Enabled", disabledText = "Disabled", scene = UI_Scene.All)]
        public bool useAsInterceptor = false;
    }
}
