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

    public class KCSDebug : MonoBehaviour
    {
        public bool ShowLines;

        private void Start()
        {
            ShowLines = false;
        }

        private void Update()
        {
            bool key = Input.GetKeyDown(KeyCode.F12);
            //on press f12 toggle missile lines
            if (Input.GetKeyDown(KeyCode.F12) && !Input.GetKey(KeyCode.LeftAlt))
            {
                //switch bool return
                ShowLines = !ShowLines;
                if (ShowLines)
                {
                    Debug.Log("Line Enabled");
                }
                else
                {
                    Debug.Log("Line disabled");
                }
            }
        }
    }
}
