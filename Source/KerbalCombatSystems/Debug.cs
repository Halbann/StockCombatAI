using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Debug : MonoBehaviour
    {
        private static bool _debugVisible;
        public static bool Visible
        {
            get => _debugVisible;
            set
            {
                if (_debugVisible == value)
                    return;

                _debugVisible = value;

                //removes inactive lines not caught fast enough by the generic line clearer
                if (!Visible)
                    HideLines(0);

                Log("Lines " + (Visible ? "enabled." : "disabled."));
            }
        }

        private static GUIStyle textStyle;

        private static List<LineRenderer> lines;
        private static List<float> times;

        public static List<DebugLabelData> debugLabels;

        public struct DebugLabelData
        {
            public string text;
            public Vector3 position;
            internal float time;

            public DebugLabelData(string text, Vector3 position)
            {
                this.text = text;
                this.position = position;
                time = Time.fixedTime;
            }
        }

        internal void Start()
        {
            Visible = false;
            lines = new List<LineRenderer>();
            times = new List<float>();
            debugLabels = new List<DebugLabelData>();

            StartCoroutine(LineCleaner());
        }

        internal void Update()
        {
            //on press f12 toggle missile lines
            /*if (Input.GetKeyDown(KeyCode.F12) && !Input.GetKey(KeyCode.LeftAlt))
            {
                //switch bool return
                drawDebugInfo = !drawDebugInfo;

                //removes inactive lines not caught fast enough by the generic line clearer
                if (!drawDebugInfo)
                    HideLines(0);


                Log("Lines " + (drawDebugInfo ? "enabled." : "disabled."));
            }*/
        }

        void OnGUI()
        {
            if (!Visible || Camera.main == null)
                return;

            InitStyles();
            DrawDebugText();
        }

        private void InitStyles()
        {
            // Initialise GUI styles.

            if (textStyle != null)
                return;

            textStyle = new GUIStyle(GUI.skin.label);

            Font calibriliFont = Resources.FindObjectsOfTypeAll<Font>().ToList().Find(f => f.name == "calibrili");
            if (calibriliFont != null)
                textStyle.font = calibriliFont;
        }

        #region Lines

        public static LineRenderer CreateLine(Color LineColour, float width = 0.5f)
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
            //Line.startWidth = width * 0.4f;
            Line.startWidth = 0.1f;
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
            if (Visible)
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
            if (line == null || line.gameObject == null)
                return;

            Destroy(line.gameObject);
        }

        private IEnumerator LineCleaner()
        {
            // Hide rogue lines that haven't been plotted in a while.

            while (true)
            {
                HideLines(5);

                yield return new WaitForSeconds(5f);
            }
        }

        private static void HideLines(float timeLimit)
        {
            LineRenderer currentLine;

            for (int i = 0; i < lines.Count; i++)
            {
                if (Time.time - times[i] < timeLimit)
                    continue;

                currentLine = lines[i];

                if (currentLine == null)
                    continue;

                // Hide inactive line.
                lines[i].positionCount = 0;
            }
        }

        #endregion

        #region Labels

        // todo: draw construction lines and timetointecept text for nearintercept variables
        private void ActiveVesselDebug()
        {

        }

        public static void DrawDebugLabel(string text, Vector3 position) =>
            debugLabels.Add(new DebugLabelData(text, position));

        private void DrawDebugText()
        {
            var allMissiles = FlightManager.weaponsInFlight.Concat(FlightManager.interceptorsInFlight);

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

            foreach (ModuleShipController ship in FlightManager.ships)
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

            foreach (DebugLabelData data in debugLabels)
            {
                if (data.time >= Time.fixedTime - 0.01f)
                    DebugLabel(data.text, data.position);
            }

            debugLabels.RemoveAll(d => Time.fixedTime - d.time > 0.1f);
        }

        private static void VesselLabel(string text, Vessel vessel)
        {
            DebugLabel(text, vessel.CoM);
        }

        public static void DebugLabel(string text, Vector3 position)
        {
            if (MapView.MapIsEnabled) return;

            Vector2 textSize = textStyle.CalcSize(new GUIContent(text));
            Rect textRect = new Rect(0, 0, textSize.x, textSize.y);
            Vector3 screenPos;

            screenPos = Camera.main.WorldToScreenPoint(position);

            textRect.x = screenPos.x + 18;
            textRect.y = (Screen.height - screenPos.y) - (textSize.y / 2);

            if (textRect.x > Screen.width || textRect.y > Screen.height || screenPos.z < 0) return;

            string labelText = text + "";
            GUI.Label(textRect, labelText, textStyle);
        }

        #endregion

        #region Logging

        public static void Log(string message)
        {
            UnityEngine.Debug.Log("[KCS]: " + message);
        }

        public static void LogError(string message)
        {
            UnityEngine.Debug.LogError("[KCS]: " + message);
        }

        #endregion
    }

    #region Transforms

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
            if (Debug.Visible)
            {
                UpdateLine(xLine, transform.right);
                UpdateLine(yLine, transform.up);
                UpdateLine(zLine, transform.forward);
            }

            if (Debug.Visible == drawEnabled)
                return;

            if (Debug.Visible)
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

            drawEnabled = Debug.Visible;
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

    #endregion
}
