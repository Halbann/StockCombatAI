using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleObjectTracking : PartModule
    {
        //all ore scanning parts plus the sentinal at superlong range

        const string objectTrackingGroupName = "Situational Awareness";

        [KSPField(
              guiActive = true,
              guiActiveEditor = true, 
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = objectTrackingGroupName,
              groupDisplayName = objectTrackingGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float detectionRange = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", detectionRange));

            return output.ToString();
        }
    }
}
