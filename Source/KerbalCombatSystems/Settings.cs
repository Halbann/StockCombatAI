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


        [GameParameters.CustomIntParameterUI("Scanning Range", minValue = 1, maxValue = 10, stepSize = 1,
        toolTip = "Multiplier for scanner ranges, default 5km")]
        public int ScalingFactor { get { return ScalingFactorDefault; } set { ScalingFactorDefault = value; } }
        private int ScalingFactorDefault = 10;


        //todo: migrate referesh rate into custom mod settings
        [GameParameters.CustomIntParameterUI("Refresh Rate", minValue = 1, maxValue = 30, stepSize = 1,
        toolTip = "How many seconds between intensive but accuracy aiding AI functions")]
        public int RefreshRate { get { return RefreshRateDefault; } set { RefreshRateDefault = value; } }
        private int RefreshRateDefault = 5;

        [GameParameters.CustomParameterUI("Allow Withdrawl",
        toolTip = "Whether ships are allowed retreat from battle")]
        public bool VeryDishonourable = true;

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

    public class KCSOpponents : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Opponent Spawning"; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }
        public override string Section { get { return "KCS"; } }
        public override string DisplaySection { get { return "KCS"; } }
        public override int SectionOrder { get { return 2; } }
        public override bool HasPresets { get { return true; } }


        [GameParameters.CustomParameterUI("Enabled")]
        public bool SpawningEnabled = false;


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
}