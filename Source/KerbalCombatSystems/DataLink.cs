using System;
using System.Text;

namespace KerbalCombatSystems
{
    public class ModuleDataLink : PartModule
    {
        public ModuleWeapon typeModule;
        public int scalingFactor = 5;
    }

    public class ModuleDataLinkRelay : ModuleDataLink
    {
        //relay parts only are capable of sending the target lists

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
        //all parts with antenna modules(commands pods and antennas afaik) are capable of receiving datalinked target lists

        const string dataLinkGroupName = "Target Receiver";

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Receiver Power",
              guiUnits = " ",
              groupName = dataLinkGroupName,
              groupDisplayName = dataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float receiverPower = 0f;

        [KSPField(isPersistant = true)]
        public float baseReceiverPower = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append($"Receiver Power: {receiverPower}");

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {
            receiverPower = baseReceiverPower * scalingFactor;
        }
    }
}
