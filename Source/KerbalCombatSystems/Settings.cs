using System;
using System.Collections;
using System.Reflection;

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

        [GameParameters.CustomParameterUI("Allow Withdrawal",
        toolTip = "Whether ships are allowed to retreat from combat.")]
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