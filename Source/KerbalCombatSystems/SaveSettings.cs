namespace KerbalCombatSystems
{
    public class KCSSaveSettings : GameParameters.CustomParameterNode
    {
        // Instance.

        private static KCSSaveSettings instance;
        public static KCSSaveSettings Instance
        {
            get
            {
                if (instance == null)
                    if (HighLogic.CurrentGame != null)
                        instance = HighLogic.CurrentGame.Parameters.CustomParams<KCSSaveSettings>();

                return instance;
            }
        }


        // Boilerplate.
        
        public override string Section => "KCS";
        public override string DisplaySection => "KCS";
        public override int SectionOrder => 1;
        public override string Title => "Combat";
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;


        // Custom parameters.

        public static bool AllowWithdrawal => Instance.allowWithdrawal;
        [GameParameters.CustomParameterUI("Allow Withdrawal",
            toolTip = "Are ships ever allowed to retreat from combat?")]
        public bool allowWithdrawal = true;
    }
}