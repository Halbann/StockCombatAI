using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;



namespace KerbalCombatSystems
{

    public class KCSCombat : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Combat"; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }
        public override string Section { get { return "KCS"; } }
        public override string DisplaySection { get { return "KCS"; } }
        public override int SectionOrder { get { return 1; } }
        public override bool HasPresets { get { return true; } }


        [GameParameters.CustomIntParameterUI("Scanning Range Multiplier", minValue = 1, maxValue = 10, stepSize = 1,
        toolTip = "Multiplier for scanner ranges, default 5")]
        public int scalingFactor { get { return scalingFactorDefault; } set { scalingFactorDefault = value; } }
        private int scalingFactorDefault = 5;

        [GameParameters.CustomIntParameterUI("Transmission Range Multiplier", minValue = 1, maxValue = 10, stepSize = 1,
        toolTip = "Multiplier for datalink transmittter and receiever power")]
        public int dataLinkFactor { get { return dataLinkFactorDefault; } set { dataLinkFactorDefault = value; } }
        private int dataLinkFactorDefault = 5;

        //todo: migrate referesh rate into mod config tied settings
        [GameParameters.CustomIntParameterUI("Refresh Rate", minValue = 1, maxValue = 10, stepSize = 1,
        toolTip = "Multiplier for the time space between intensive but accuracy aiding AI functions")]
        public int refreshRate { get { return refreshRateDefault; } set { refreshRateDefault = value; } }
        private int refreshRateDefault = 5;
        //change inbuilt code to use the minimum functional value

        [GameParameters.CustomParameterUI("Allow Withdrawal",
        toolTip = "Whether ships are allowed retreat from battle")]
        public bool allowWithdrawal = true;

        public override void SetDifficultyPreset(GameParameters.Preset preset)
        {
        }

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            if (member.Name == "EnabledForSave") //This Field must always be enabled.
                return true;

            return true; //otherwise return true
        }

        public override IList ValidValues(MemberInfo member)
        {
            return null;
        }
    }

    // Hidden until implementation.
    /*public class KCSOpponents : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Opponent Spawning"; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }
        public override string Section { get { return "KCS"; } }
        public override string DisplaySection { get { return "KCS"; } }
        public override int SectionOrder { get { return 2; } }
        public override bool HasPresets { get { return true; } }


        [GameParameters.CustomParameterUI("Enabled")]
        public bool spawningEnabled = false;


        public override void SetDifficultyPreset(GameParameters.Preset preset)
        {
        }

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            if (member.Name == "EnabledForSave") //This Field must always be enabled.
                return true;

            return true; //otherwise return true
        }

        public override IList ValidValues(MemberInfo member)
        {
            return null;
        }
    }*/
}