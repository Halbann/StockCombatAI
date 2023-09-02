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

                //removes inactive lines not caught fast enough by the generic line clearer
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

                VesselLabel("ETA: " 
                    + missile.timeToHit.ToString("0.00")
                    + "\n Launched: "
                    + missile.launched.ToString()
                    + "\n Missed: "
                    + missile.missed.ToString(),
                    missile.vessel);
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
                    + "\n Throttle: "
                    + ship.fc.throttleLerped.ToString("0.00")
                    + "\n Current Weapon: "
                    + ((ship.currentWeapon?.weaponCode) == "" ? (ship.currentWeapon?.weaponType) : (ship.currentWeapon?.weaponCode)),
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

    public class DrawTransform : MonoBehaviour
    {
        public static bool drawTransforms = true;
        private bool drawEnabled = false;

        LineRenderer xLine;
        LineRenderer yLine;
        LineRenderer zLine;

        void Start()
        {
            if (!drawTransforms)
            {
                Destroy(this);
                return;
            }

            xLine = new GameObject().AddComponent<LineRenderer>();
            yLine = new GameObject().AddComponent<LineRenderer>();
            zLine = new GameObject().AddComponent<LineRenderer>();

            SetupLine(xLine, Color.red);
            SetupLine(yLine, Color.green);
            SetupLine(zLine, Color.blue);

            xLine.enabled = drawEnabled;
            yLine.enabled = drawEnabled;
            zLine.enabled = drawEnabled;
        }

        // Update is called once per frame
        void Update()
        {
            if (KCSDebug.showLines)
            {
                UpdateLine(xLine, transform.right);
                UpdateLine(yLine, transform.up);
                UpdateLine(zLine, transform.forward);
            }

            if (KCSDebug.showLines == drawEnabled)
                return;

            if (KCSDebug.showLines)
            {
                xLine.enabled = true;
                yLine.enabled = true;
                zLine.enabled = true;
            }
            else
            {
                xLine.enabled = false;
                yLine.enabled = false;
                zLine.enabled = false;
            }

            drawEnabled = KCSDebug.showLines;
        }

        void SetupLine(LineRenderer line, Color color)
        {
            line.material = new Material(Shader.Find("Unlit/Color"));
            line.material.color = color;
            line.widthMultiplier = 0.03f;
        }

        void UpdateLine(LineRenderer line, Vector3 direction)
        {
            line.SetPositions(new Vector3[] { transform.position, transform.position + direction * 2 });
        }
    }
}
