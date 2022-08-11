using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;

namespace KerbalCombatSystems
{
    public class ModuleFirework : PartModule
    {
        
// Debugging line variables.

        LineRenderer TargetLine, LeadLine;
        private int RoundBurst;

private IENumerator FireworkAim()
{
 
            //until aimed
//recalculate target vector

}


public void Start()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;
            RoundBurst = part.FindModuleImplementing<ModuleWeaponController>().FWRoundBurst;
            Firer = vessel;

//get list of fireworks


StartCoroutine(FireworkAim());
        }

    }

public void OnDestroy()
        {
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(TargetLine);
        }
}
