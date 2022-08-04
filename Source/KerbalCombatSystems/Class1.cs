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
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MissileGuidance : MonoBehaviour
    {
        public void Update()
        {
            
        }
    }

    

   

    public class ModuleMissileGuidance : PartModule
    {
        float SafeDistance;
        float TerminalDistance;

      

        public override void OnStart(StartState state)
        {

            //check for a ship controller
            //if found wait

            //once decoupled copy the nearest ship(assume it's parent)'s target vessel

            //eject at slow speed
            //wait
            //match velocity

            //do hatbats neato Missile guidance
        }

        public void Fire()
        {
            StartCoroutine(SafeDistanceCoroutine());
        }

        IEnumerator SafeDistanceCoroutine()
        {
            yield return new WaitUntil(true);
        }

        public void Update()
        {

        }




    }
}
