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
        public static bool ShowLines;

        private void Start()
        {
            ShowLines = false;
        }

        private void Update()
        {
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

        public static LineRenderer CreateLine(Color LineColour)
        {
            //spawn new line
            LineRenderer Line = new GameObject().AddComponent<LineRenderer>();
            Line.useWorldSpace = true;
            //create a material for the line with its unique colour
            Material LineMaterial = new Material(Shader.Find("Standard"));
            LineMaterial.color = LineColour;
            Line.material = LineMaterial;
            //make it a point
            Line.startWidth = 0.5f;
            Line.endWidth = 0f;
            //pass the line back to be associated with a vector
            return Line;
        }

        public static void PlotLine(Vector3[] Positions, LineRenderer Line)
        {
            if (ShowLines)
            {
                Line.positionCount = 2;
                Line.SetPositions(Positions);
            }
            else
            {
                Line.positionCount = 0;
            }
        }

        public static void DestroyLine(LineRenderer line)
        {
            if (line == null) return;
            if (line.gameObject == null) return;
            line.gameObject.DestroyGameObject();
        }
    }
}
