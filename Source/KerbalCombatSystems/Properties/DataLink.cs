using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalCombatSystems
{
    public class ModuleDataLink : PartModule
    {
        public ModuleWeapon typeModule;

        public void Setup()
        {
            /*if (setup) return;

            string moduleName;
            switch (weaponType)
            {
                case "Missile":
                    moduleName = "ModuleDataLinkRelay";
                    break;
                case "Rocket":
                    moduleName = "ModuleDataLinkAntenna";
                    break;
                default:
                    Debug.Log($"[KCS]: Couldn't find a module for {linkType}.");
                    return;
            }
            
            if (part.GetComponent(moduleName) == null)
                typeModule = (ModuleWeapon)part.AddModule(moduleName);

            typeModule.Setup();
            setup = true;*/
        }
    }

    public class ModuleDataLinkRelay : ModuleDataLink
    {
        //relay parts only are capable of sending the target lists

        const string DataLinkGroupName = "Target Broadcaster";
        private int scalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = DataLinkGroupName,
              groupDisplayName = DataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float transmitterPower = 0f;

        [KSPField(isPersistant = true)]
        public float baseTransmitterPower = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Broadcast Power: {0} m", transmitterPower));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            scalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().DataLinkFactor;
            transmitterPower = baseTransmitterPower * scalingFactor;
        }
    }

    public class ModuleDataLinkAntenna : ModuleDataLink
    {
        //all parts with antenna modules(commands pods and antennas afaik) are capable of receiving datalinked target lists

        const string DataLinkGroupName = "Target Receiver";
        private int scalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = DataLinkGroupName,
              groupDisplayName = DataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float recieverPower = 0f;

        [KSPField(isPersistant = true)]
        public float baseReceiverPower = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", recieverPower));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {

            scalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().DataLinkFactor;
            recieverPower = baseReceiverPower * scalingFactor;
        }
    }
}
