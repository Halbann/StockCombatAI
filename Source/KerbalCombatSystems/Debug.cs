using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]

    public class KCSDebug : MonoBehaviour
    {
        public static bool showLines;
        private static List<LineRenderer> lines;
        private static List<float> times;

        //relavent game settings
        private GUIStyle textStyle;

        private void Start()
        {
            showLines = false;
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
                showLines = !showLines;

                //this block strikes me as redundant to line 91 but not actively harmful
                if (!showLines)
                {
                    foreach (var line in lines)
                    {
                        if (line == null) continue;
                        line.positionCount = 0;
                    }
                }

                Debug.Log("[KCS]: Lines " + (showLines ? "enabled." : "disabled."));
            }
        }

        void OnGUI()
        {
            if (!showLines) return;

            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label);

                Font calibriliFont = Resources.FindObjectsOfTypeAll<Font>().ToList().Find(f => f.name == "calibrili");
                if (calibriliFont != null)
                    textStyle.font = calibriliFont;
            }

            DrawDebugText();
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
            if (showLines)
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

                yield return new WaitForSeconds(5f);
            }
        }

        // todo: draw construction lines and timetointecept text for nearintercept variables
        private void ActiveVesselDebug()
        {

        }

        private void DrawDebugText()
        {
            var allMissiles = KCSController.weaponsInFlight.Concat(KCSController.interceptorsInFlight);

            GUI.color = Color.white;

            foreach (ModuleWeaponController missile in allMissiles)
            {
                if (missile == null || missile.vessel == null)
                    continue;

                VesselLabel("ETA: " + missile.timeToHit.ToString("0.00"), missile.vessel);
            }

            foreach (ModuleShipController ship in KCSController.ships)
            {
                if (ship == null || ship.vessel == null)
                    continue;

                VesselLabel("State: "
                    + ship.state
                    + "\n Burn Time: "
                    + ship.nearInterceptBurnTime.ToString("0.00")
                    + "\n Intercept Time: "
                    + ship.nearInterceptApproachTime.ToString("0.00")
                    + "\n Lateral Vel: "
                    + ship.lateralVelocity.ToString("0.00"),
                    ship.vessel);
            }
        }

        private void VesselLabel(string text, Vessel vessel)
        {
            if (MapView.MapIsEnabled) return;

            Vector2 textSize = textStyle.CalcSize(new GUIContent(text));
            Rect textRect = new Rect(0, 0, textSize.x, textSize.y);
            Vector3 screenPos;

            screenPos = Camera.main.WorldToScreenPoint(vessel.CoM);

            textRect.x = screenPos.x + 18;
            textRect.y = (Screen.height - screenPos.y) - (textSize.y / 2);

            if (textRect.x > Screen.width || textRect.y > Screen.height || screenPos.z < 0) return;

            GUI.Label(textRect, text, textStyle);
        }
    }
}
