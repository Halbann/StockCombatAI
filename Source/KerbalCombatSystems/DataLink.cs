using System;
using System.Text;

namespace KerbalCombatSystems
{
    public class ModuleDataLink : PartModule
    {
        public ModuleWeapon typeModule;
        public int scalingFactor = 5;

        public void Setup()
        {
            /*if (setup) return;

            string moduleName;
            switch (weaponType)
            {
                case "transmitter":
                    moduleName = "ModuleDataLinkRelay";
                    break;
                case "reciever":
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
        // Relay parts only are capable of sending target lists to vessels with receivers

        const string dataLinkGroupName = "Target Broadcaster";

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Transmitter Power",
              guiUnits = " ",
              groupName = dataLinkGroupName,
              groupDisplayName = dataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float transmitterPower = 0f;

        [KSPField(isPersistant = true)]
        public float baseTransmitterPower = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Transmitter Power: {0}", transmitterPower));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            transmitterPower = baseTransmitterPower * scalingFactor;
        }
    }

    public class ModuleDataLinkAntenna : ModuleDataLink
    {
        // All parts with antenna modules(pods and antennas) are capable of receiving target lists

        const string dataLinkGroupName = "Target Receiver";

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Reciever Power",
              guiUnits = " ",
              groupName = dataLinkGroupName,
              groupDisplayName = dataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float recieverPower = 0f;

        [KSPField(isPersistant = true)]
        public float baseReceiverPower = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Reciever Power: {0}", recieverPower));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            recieverPower = baseReceiverPower * scalingFactor;
        }
    }
}
