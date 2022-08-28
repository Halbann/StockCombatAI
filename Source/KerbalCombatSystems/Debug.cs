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
    [KSPAddon(KSPAddon.Startup.Flight, false)]

    public class KCSDebug : MonoBehaviour
    {
        public static bool ShowLines;
        private static List<LineRenderer> lines;
        private static List<float> times;

        private void Start()
        {
            ShowLines = false;
            lines = new List<LineRenderer>();
            times = new List<float>();
            StartCoroutine(LineCleaner());
        }

        private void Update()
        {
            //on press f12 toggle missile lines
            if (Input.GetKeyDown(KeyCode.F12) && !Input.GetKey(KeyCode.LeftAlt))
            {
                //switch bool return
                ShowLines = !ShowLines;
                Debug.Log("[KCS]: Lines " + (ShowLines ? "enabled." : "disabled."));
            }
        }

        public static LineRenderer CreateLine(Color LineColour)
        {
            //spawn new line
            LineRenderer Line = new GameObject().AddComponent<LineRenderer>();
            Line.useWorldSpace = true;

            // Create a material for the line with its unique colour.
            Material LineMaterial = new Material(Shader.Find("Standard"));
            LineMaterial.color = LineColour;
            LineMaterial.shader = Shader.Find("Unlit/Color");
            Line.material = LineMaterial;

            //make it come to a point
            Line.startWidth = 0.5f;
            Line.endWidth = 0.1f;

            // Don't draw until the line is first plotted.
            Line.positionCount = 0;

            lines.Add(Line);
            times.Add(Time.time);

            //pass the line back to be associated with a vector
            return Line;
        }

        public static void PlotLine(Vector3[] Positions, LineRenderer Line)
        {
            if (ShowLines)
            {
                Line.positionCount = 2;
                Line.SetPositions(Positions);

                int index = lines.FindIndex(l => l == Line);
                times[index] = Time.time;
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

        private IEnumerator LineCleaner()
        {
            // Hide rogue lines that haven't been plotted in a while.

            LineRenderer currentLine;

            while (true)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (Time.time - times[i] < 5) continue;
                    currentLine = lines[i];
                    if (currentLine == null) continue;
                    lines[i].positionCount = 0;
                }

                yield return new WaitForSeconds(5);
            }
        }
    }
}
