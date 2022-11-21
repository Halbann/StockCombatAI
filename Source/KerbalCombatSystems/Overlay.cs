using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Overlay : MonoBehaviour
    {
        #region Fields

        public static int circleSteps = 120;
        public static int numberOfRangeLines = 4;
        public static float size = 100.0f;
        public static bool useElevationArcs = true;
        public static bool hideWithUI = true;

        public static float rangeRingsOpacity = 0.1f;
        public static float rangeLinesOpacity = 0.1f;
        public static float secondaryRangeLinesOpacity = 0.05f;
        public static float dashedLinesOpacity = 0.1f;
        public static float elevationLinesOpacity = 0.15f;
        public static float weaponRangeOpacity = 0.3f;
        public static float markerOpacity = 0.3f;
        public static float rangeNumberOpacity = 0.2f;
        public static float shipnameOpacity = 0.6f;
        public static float globalOpacity = 1.2f;

        public static float markerScale = 10;
        public static float iconSize = 25;
        public static float reticleStackSize = 10;
        public static int shipFontSize = 14;
        public static int maxTargetCircles = 3;

        public static float dashSpacing = 0.15f;
        public static float dashLength = 0.1f;
        public static float dashScaling = 1;

        public static KeyCode quickToggleZoomKey = KeyCode.O;

        // Final range is multiplied by size.
        public float[] ranges = { 5, 10, 20, 30, 40, 50, 75, 100, 150, 200, 300, 400, 500 };

        private List<ModuleShipController> ships = new List<ModuleShipController>();
        private ModuleShipController activeController;
        private static Transform centre;
        internal static FlightCamera mainCamera;
        private static float closeCamDistance = 50;
        private static float farCamDistance = 2000;
        private Vessel activeVessel;
        private float detectionRange;
        private float weaponRange;

        private static bool runOnce = true;
        internal static bool hideOverlay = false;

        private static LineMesh rangeLinesMesh;
        private static LineMesh secondaryRangeLinesMesh;
        private static LineMesh rangeRingsMesh;
        private static LineMesh elevationLinesMesh;
        private static LineMesh dashedLinesMesh;
        private static LineMesh detectionRangeMesh;
        private static LineMesh weaponRangeMesh;

        private List<List<Vector3>> rangeRingLines = new List<List<Vector3>>();
        private List<List<Vector3>> rangeLines = new List<List<Vector3>>();
        private List<List<Vector3>> secondaryRangeLines = new List<List<Vector3>>();
        private List<List<Vector3>> elevationLines = new List<List<Vector3>>();
        private List<List<Vector3>> dashedLines = new List<List<Vector3>>();
        private List<List<Vector3>> detectionRangeLines = new List<List<Vector3>>();
        private List<List<Vector3>> weaponRangeLines = new List<List<Vector3>>();

        private static Material transparentLineMat;
        private static Material rangeLineMat;
        private static Material secondaryRangeLineMat;
        private static Material dashedLineMat;
        private static Material detectionRangeMat;
        private static Material weaponRangeMat;
        private static Material elevationLineMat;

        private static float linesOpacity;
        private float linesOpacityLast = -1;

        private static List<Marker> markers = new List<Marker>();
        private static Material markerMaterial;

        private Color currentIconColour;
        private static Material iconMat;
        private static Texture2D iconTexture;
        private static List<Texture2D> targetCircles;
        private Color activeTargetColour = new Color(0.7f, 1, 0.2f, 1);
        private Rect drawIconRect = new Rect();

        private GUIStyle rangeNumberStyle;
        private static Color rangeNumberColour = new Color(1, 1, 1, rangeNumberOpacity);

        private GUIStyle shipNameStyle;
        private Color shipNameColour = new Color(1, 1, 1, shipnameOpacity);

        //public int gridX = 41;
        //public int gridY = 41;
        //public float crossSize = 0.5f;
        //public float gridSpacing = 2.5f;
        //private LineMesh grid;
        //private List<List<Vector3>> gridLines;

        #endregion


        #region Start

        private void Start()
        {
            if (FlightGlobals.ActiveVessel.situation != Vessel.Situations.ORBITING)
            {
                enabled = false;
                return;
            }

            if (runOnce)
            {
                runOnce = false;

                // Create the materials for the different lines, markers and icons.
                CreateMaterials();

                // Generate textures of different sized rings to use as target markers.

                targetCircles = new List<Texture2D>();
                int circleSize = (int)(32 * 1.1f);
                Color circleColor = new Color(1, 1, 1, 0.8f);
                int growCircle;

                for (int i = 0; i < maxTargetCircles; i++)
                {
                    growCircle = i * 10;
                    targetCircles.Add(CreateCircleTexture(circleSize + growCircle, (circleSize + growCircle) / 2, 1, circleColor));
                    circleColor.a *= 0.75f;
                }

                // Load the ship and missile icon texture.
                iconTexture = GameDatabase.Instance.GetTexture("KCS/Icons/OverlayIcon", false);
            }

            // Give the transform a unique identifier and place it at the origin.
            // The origin in KSP moves with the active vessel.
            // Alternatively we could use the active vessel's COM.
            centre = transform;
            centre.position = new Vector3(0, 0, 0);

            // Create a game object and set up a GoodLines LineMesh for each type of line.
            rangeRingsMesh = CreateLine(transparentLineMat, true);
            rangeLinesMesh = CreateLine(rangeLineMat, true);
            secondaryRangeLinesMesh = CreateLine(secondaryRangeLineMat, true);
            detectionRangeMesh = CreateLine(detectionRangeMat, true);
            weaponRangeMesh = CreateLine(weaponRangeMat, true);
            elevationLinesMesh = CreateLine(elevationLineMat);
            dashedLinesMesh = CreateLine(dashedLineMat);

            // Generate the points for the range rings and range lines and send them to their respective LineMesh components.
            // We only need to do this once because the lines are parented to the centre and they don't need to change shape.
            CreateFixedLines();

            // Add the overlay layer to the main camera.
            // This is necessary to make the overlay invisible to additional cameras by default (multi-cam, hull-cam, docking-cam, etc).
            // Layer 8 is only used by the game in the editor I believe.
            FlightCamera.fetch.mainCamera.cullingMask |= (1 << 8);
        }

        private void CreateFixedLines()
        {
            // Range rings. Concentric circles at set ranges around the origin.

            circleSteps = Mathf.Max(circleSteps, 3);
            rangeRingLines.Clear();

            // Generate the points that form the rings.
            foreach (float range in ranges)
                rangeRingLines.Add(Circle(circleSteps, range * size));

            // Send the points to the range rings mesh.
            rangeRingsMesh.SetLinesFromPoints(rangeRingLines);


            // Range lines. Straight lines extending from closest range to the furthest, with range numbers at each range.
            // The markings themselves are drawn in the GUI.

            rangeLines.Clear();
            Vector3 north = centre.right;
            Vector3 direction;

            // Generate points for segmented lines going in each direction.
            for (int i = 0; i < numberOfRangeLines; i++)
            {
                direction = Quaternion.AngleAxis(360 * i / numberOfRangeLines, centre.up) * north;
                rangeLines.Add(RangeLine(direction));
            }

            // Send the points to the range lines mesh.
            rangeLinesMesh.SetLinesFromPoints(rangeLines);

            // Offset by 45 degrees.
            //north = Quaternion.AngleAxis(360f / (numberOfRangeLines * 4) / 2, centre.up) * north;

            // Generate points for faded lines inbetween each range line.
            for (int i = 0; i < numberOfRangeLines * 4; i++)
            {
                direction = Quaternion.AngleAxis(360 * i / (numberOfRangeLines * 4), centre.up) * north;
                secondaryRangeLines.Add(RangeLine(direction));
            }

            // Send the points to the faded range lines mesh.
            secondaryRangeLinesMesh.SetLinesFromPoints(secondaryRangeLines);
        }

        // Create all necessary materials here. Only gets called once.
        private void CreateMaterials()
        {
            transparentLineMat = new Material(KCSAssets.LineShader);
            transparentLineMat.SetColor("_Color", new Color(1, 1, 1, rangeRingsOpacity));
            transparentLineMat.SetFloat("_Thickness", 1.3f);

            rangeLineMat = new Material(KCSAssets.LineShader);
            rangeLineMat.SetColor("_Color", new Color(1, 1, 1, rangeLinesOpacity));
            rangeLineMat.SetFloat("_Thickness", 1.3f);

            secondaryRangeLineMat = new Material(KCSAssets.LineShader);
            secondaryRangeLineMat.SetColor("_Color", new Color(1, 1, 1, secondaryRangeLinesOpacity));
            secondaryRangeLineMat.SetFloat("_Thickness", 1.3f);

            dashedLineMat = new Material(KCSAssets.LineShader);
            dashedLineMat.SetColor("_Color", new Color(1, 1, 1, dashedLinesOpacity));
            dashedLineMat.SetFloat("_Thickness", 1.7f);

            detectionRangeMat = new Material(KCSAssets.LineShader);
            detectionRangeMat.SetColor("_Color", new Color(1f, 0.6f, 0.3f, weaponRangeOpacity));
            detectionRangeMat.SetFloat("_Thickness", 1.7f);

            weaponRangeMat = new Material(KCSAssets.LineShader);
            weaponRangeMat.SetColor("_Color", new Color(1f, 0.3f, 0.3f, weaponRangeOpacity));
            weaponRangeMat.SetFloat("_Thickness", 1.7f);

            elevationLineMat = new Material(KCSAssets.LineShader);
            elevationLineMat.SetColor("_Color", new Color(1f, 1f, 1f, elevationLinesOpacity));
            elevationLineMat.SetFloat("_Thickness", 1.3f);

            markerMaterial = new Material(Shader.Find("Sprites/Default"));
            markerMaterial.color = Color.white;

            iconMat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        }

        #endregion


        #region Update

        private void FixedUpdate()
        {
            if (hideOverlay) return;
            if (FlightGlobals.ActiveVessel == null) return;

            if (activeVessel != FlightGlobals.ActiveVessel)
            {
                activeVessel = FlightGlobals.ActiveVessel;
                activeController = FindController(activeVessel);
            }

            if (activeController == null)
                return;

            if (mainCamera == null)
                mainCamera = FlightCamera.fetch;

            if (mainCamera.mode == FlightCamera.Modes.LOCKED)
                centre.up = FlightGlobals.ActiveVessel.ReferenceTransform.forward;
            else
                centre.up = mainCamera.getReferenceFrame() * Vector3.up;
        }

        private void Update()
        {
            if (hideOverlay)
                return;

            if (activeController == null)
                return;

            if (MapView.MapIsEnabled || mainCamera == null || FlightGlobals.ActiveVessel == null)
                return;

            WatchShipList();

            if (Input.GetKeyDown(quickToggleZoomKey))
                DistanceToggle();

            // Show the overlay lines only when the camera is zoomed out.

            float cameraDistance = Vector3.Distance(mainCamera.transform.position, centre.position);
            linesOpacity = Mathf.MoveTowards(linesOpacity, cameraDistance > 650 ? 1 : 0, 6 * Time.unscaledDeltaTime);

            if (linesOpacity != linesOpacityLast)
            {
                UpdateOpacity();
                linesOpacityLast = linesOpacity;
            }

            if (linesOpacity <= 0) return;

            if (ships.Count > 0)
            {
                // Arcs that visually anchor the ships to the plane of the overlay.

                elevationLines.Clear();

                foreach (var ship in ships)
                {
                    if (ship == null || ship == FlightGlobals.ActiveVessel) continue;

                    var shipPos = ship.vessel.CoM;

                    if (useElevationArcs)
                        elevationLines.Add(ElevationArc(shipPos));
                    else
                        elevationLines.Add(SegmentedLine(shipPos, centre.position + Vector3.ProjectOnPlane(shipPos - centre.position, centre.up), 10));
                }

                if (elevationLines.Count > 0 || dashedLinesMesh.Positions.Count > 0)
                    elevationLinesMesh.SetLinesFromPoints(elevationLines);

                // Dashed lines between each ship and its current target. 

                dashedLines.Clear();
                foreach (var ship in ships)
                {
                    if (ship == null
                        || !ship.controllerRunning
                        || !ship.alive
                        || ship.state == "Withdrawing"
                        || ship.target == null)
                        continue;

                    dashedLines.AddRange(DashedLine(ship.vessel.CoM, ship.target.CoM, dashLength, dashSpacing));
                }

                if (dashedLines.Count > 0 || dashedLinesMesh.Positions.Count > 0)
                    dashedLinesMesh.SetLinesFromPoints(dashedLines);

                // Display max weapon range (red) and max detection range (blue) as rings on the overlay.
                // If the end point of a ship's elevation arc is within one of these circles, then the player
                // can see that the ship is in range.

                if (!ReferenceEquals(activeController, null))
                {
                    if (activeController.maxDetectionRange > 0 &&
                        detectionRange != activeController.maxDetectionRange)
                    {
                        detectionRange = activeController.maxDetectionRange;

                        detectionRangeLines.Clear();
                        detectionRangeLines.AddRange(DashedCircle(2, 1, AvoidRangeOverlap(detectionRange)));
                        detectionRangeMesh.SetLinesFromPoints(detectionRangeLines);
                    }

                    if (activeController.maxWeaponRange > 0 &&
                        weaponRange != activeController.maxWeaponRange)
                    {
                        weaponRange = activeController.maxWeaponRange;

                        weaponRangeLines.Clear();
                        weaponRangeLines.AddRange(DashedCircle(2, 1, AvoidRangeOverlap(weaponRange)));
                        weaponRangeMesh.SetLinesFromPoints(weaponRangeLines);
                    }
                }
            }
        }

        private float AvoidRangeOverlap(float distance)
        {
            float margin = 0.05f;
            float range, marginDistance;

            foreach (float rangeUnscaled in ranges)
            {
                range = rangeUnscaled * size;
                marginDistance = range * margin;

                if (Mathf.Abs(range - distance) < marginDistance)
                {
                    if (distance >= range)
                        distance = range + marginDistance;
                    else
                        distance = range - marginDistance;

                    break;
                }
            }

            return distance;
        }

        private void WatchShipList()
        {
            if (ships.Count == KCSController.ships.Count) return;

            ships = KCSController.ships;
            markers.ForEach(m => m.DeleteMarker());
            markers.Clear();
            CreateMarkers();
        }

        private void CreateMarkers()
        {
            foreach (var ship in ships)
            {
                if (ship == null) continue;

                var markerObject = Instantiate(KCSAssets.markerPrefab);
                markerObject.layer = 8;

                // Need to manually correct the shader as it comes out of the prefab as null.
                markerObject.GetComponent<SpriteRenderer>().material = markerMaterial;

                var marker = markerObject.AddComponent<Marker>();
                marker.centre = centre;
                marker.target = ship;
                markers.Add(marker);
            }
        }

        // Fade in/out the entire overlay by updating the alpha values of the individual materials.
        // Every material that needs to fade with the overlay must be updated here.
        // TODO: store materials in a list and update automatically.
        internal static void UpdateOpacity()
        {
            Color rangeRingsColour = transparentLineMat.GetColor("_Color");
            rangeRingsColour.a = rangeRingsOpacity * linesOpacity * globalOpacity;
            transparentLineMat.SetColor("_Color", rangeRingsColour);

            Color rangeLinesColour = rangeLineMat.GetColor("_Color");
            rangeLinesColour.a = rangeLinesOpacity * linesOpacity * globalOpacity;
            rangeLineMat.SetColor("_Color", rangeLinesColour);

            Color secondaryRangeLinesColour = secondaryRangeLineMat.GetColor("_Color");
            secondaryRangeLinesColour.a = secondaryRangeLinesOpacity * linesOpacity * globalOpacity;
            secondaryRangeLineMat.SetColor("_Color", secondaryRangeLinesColour);

            Color dashedLinesColour = dashedLineMat.GetColor("_Color");
            dashedLinesColour.a = dashedLinesOpacity * linesOpacity * globalOpacity;
            dashedLineMat.SetColor("_Color", dashedLinesColour);

            Color detectionRangeColour = detectionRangeMat.GetColor("_Color");
            detectionRangeColour.a = weaponRangeOpacity * linesOpacity * globalOpacity;
            detectionRangeMat.SetColor("_Color", detectionRangeColour);

            Color weaponRangeColour = weaponRangeMat.GetColor("_Color");
            weaponRangeColour.a = weaponRangeOpacity * linesOpacity * globalOpacity;
            weaponRangeMat.SetColor("_Color", weaponRangeColour);

            Color elevationLinesColour = elevationLineMat.GetColor("_Color");
            elevationLinesColour.a = elevationLinesOpacity * linesOpacity * globalOpacity;
            elevationLineMat.SetColor("_Color", elevationLinesColour);

            Color markerColour = markerMaterial.GetColor("_Color");
            markerColour.a = markerOpacity * linesOpacity * globalOpacity;
            markerMaterial.color = markerColour;

            rangeNumberColour.a = rangeNumberOpacity * linesOpacity * globalOpacity;
        }

        // Quickly switch to the camera range needed for the overlay to display.
        internal static void DistanceToggle()
        {
            if (mainCamera.Distance > 650)
            {
                farCamDistance = Mathf.Max(mainCamera.Distance, 2000);
                mainCamera.SetDistance(closeCamDistance);
            }
            else
            {
                closeCamDistance = Mathf.Min(mainCamera.Distance, 100);
                mainCamera.SetDistance(farCamDistance);
            }
        }

        #endregion


        #region Line Generation

        // All positions are relative to the transform of the LineMesh gameobject.
        // Use world positions by keeping the transform at the origin with the default scale and rotation.

        // Create a gameobject with a properly set up GoodLines LineMesh.
        private LineMesh CreateLine(Material mat, bool makeChild = false)
        {
            GameObject obj = new GameObject("Line Container");
            obj.layer = 8;
            obj.AddComponent<MeshFilter>();
            LineMesh line = obj.AddComponent<LineMesh>();
            MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
            meshRenderer.material = mat;

            if (makeChild)
            {
                obj.transform.SetPositionAndRotation(centre.position, centre.rotation);
                obj.transform.SetParent(centre);
            }

            return line;
        }

        // We need to use segmented straight lines because the camera culls whole lines 
        // that have either end outside of the camera frustum.
        private List<Vector3> SegmentedLine(Vector3 start, Vector3 end, int segments)
        {
            var line = new List<Vector3>();

            for (int i = 0; i <= segments; i++)
                line.Add(Vector3.Lerp(start, end, (float)i / segments));

            return line;
        }

        // Create a dashed line, each dash is 'length' long in model-space.
        private List<List<Vector3>> DashedLine(Vector3 start, Vector3 end, float length, float spacing)
        {
            var line = new List<List<Vector3>>();

            if (length <= 0 || spacing <= 0)
                return line;

            // Cap the number of dashes at 200 for performance. Stretch the line if 200 dashes isn't enough.
            float distanceScaling = Mathf.Max(Vector3.Distance(start, end) / ((length + spacing) * size * dashScaling * 200), 1);

            Vector3 current = start;
            bool space = true;

            while (current != end)
            {
                if (space)
                {
                    current = Vector3.MoveTowards(current, end, spacing * size * dashScaling * distanceScaling);
                }
                else
                {
                    var dash = new List<Vector3>();
                    dash.Add(current);
                    current = Vector3.MoveTowards(current, end, length * size * dashScaling * distanceScaling);
                    dash.Add(current);
                    line.Add(dash);
                }

                space = !space;
            }

            return line;
        }

        // Generate points for a segmented line extending in the given direction from the the first range to the last range.
        private List<Vector3> RangeLine(Vector3 direction)
        {
            return SegmentedLine(direction * ranges.First() * size, direction * ranges.Last() * size, 25);
        }

        // Generate points for an upwards-facing circle made of STEPS number of straight lines with the given radius.
        private List<Vector3> Circle(int steps, float radius)
        {
            var line = new List<Vector3>();

            for (int currentStep = 0; currentStep <= steps; currentStep++)
            {
                float circProgress = (float)currentStep / steps;
                float currentRadian = circProgress * 2 * Mathf.PI;

                line.Add(CircleXY(currentRadian, radius));
            }

            return line;
        }

        private List<Vector3> ElevationArc(Vector3 shipPos)
        {
            var line = new List<Vector3>();
            int degreesPerSegment = 2;

            // Calculate the ship's position on the overlay plane.
            // Push it outwards so that the position is the same distance from the centre
            // as the distance from the centre to the ship.

            Vector3 centreToShip = shipPos - centre.position;
            Vector3 shipOnPlane = centre.transform.position + Vector3.ProjectOnPlane(centreToShip, centre.up);
            shipOnPlane = (shipOnPlane - centre.transform.position).normalized * centreToShip.magnitude;
            Vector3 currentPoint = shipOnPlane;

            if (centreToShip.magnitude == 0 || centreToShip == shipOnPlane)
            {
                //return new List<Vector3>() { shipPos, shipOnPlane };
                return new List<Vector3>() { shipPos, shipPos };
            }

            // Add the first point manually.
            line.Add(centre.transform.position + shipOnPlane);

            // Add new points to the line in an arc between the ship's range marker on the plane and the ship's actual position.
            for (int i = 0; i < (float)90 / degreesPerSegment; i++)
            {
                currentPoint = Vector3.RotateTowards(currentPoint, centreToShip, degreesPerSegment * Mathf.Deg2Rad, 0);
                line.Add(centre.transform.position + currentPoint);
            }

            return line;
        }

        private List<List<Vector3>> DashedCircle(float dashDegress, float spaceDegrees, float radius)
        {
            var line = new List<List<Vector3>>();
            float progressDeg = 0;

            while (progressDeg < 360)
            {
                var dash = new List<Vector3>();

                dash.Add(CircleXY(progressDeg * Mathf.Deg2Rad, radius));
                progressDeg += dashDegress;
                dash.Add(CircleXY(progressDeg * Mathf.Deg2Rad, radius));
                line.Add(dash);

                progressDeg += spaceDegrees;
            }

            return line;
        }

        private Vector3 CircleXY(float currentRadian, float radius)
        {
            float xScaled = Mathf.Cos(currentRadian);
            float yScaled = Mathf.Sin(currentRadian);

            float x = xScaled * radius;
            float y = yScaled * radius;

            return new Vector3(x, 0, y);
        }

        /*private void CreateGrid()
        {
            gridLines = new List<List<Vector3>>();
            Vector3 start = new Vector3(0,0,0);
            start.x =- (gridX * gridSpacing) / 2;
            start.y =- (gridY * gridSpacing) / 2;

            Vector3 xOffset = new Vector3(crossSize / 2, 0, 0);
            Vector3 yOffset = new Vector3(0, 0, crossSize / 2);

            Vector3 x1, x2, y1, y2;
            Vector3 crossCentre = new Vector3();
            float mag, xpos, ypos;

            for (int x = 0; x < gridX; x++)
            {
                xpos = start.x + x * gridSpacing;

                for (int y = 0; y < gridY; y++)
                {
                    ypos = start.y + y * gridSpacing;
                    //crossCentre = new Vector3(xpos, 0, ypos);
                    crossCentre.x = xpos;
                    crossCentre.z = ypos;

                    mag = crossCentre.magnitude;
                    if (mag > 50 * size || mag < 5 * size) continue;

                    x1 = centre.TransformPoint(crossCentre - xOffset);
                    x2 = centre.TransformPoint(crossCentre + xOffset);
                    y1 = centre.TransformPoint(crossCentre - yOffset);
                    y2 = centre.TransformPoint(crossCentre + yOffset);

                    gridLines.Add(new List<Vector3>() { x1, x2 });
                    gridLines.Add(new List<Vector3>() { y1, y2 });
                }
            }

            grid.SetLinesFromPoints(gridLines);
        }*/

        #endregion


        #region GUI

        private void OnGUI()
        {
            if (MapView.MapIsEnabled || PauseMenu.isOpen || hideOverlay)
                return;

            if (Event.current.type.Equals(EventType.Repaint))
            {
                DrawShipIcons();
                DrawWeaponIcons();
                DrawShipText();

                if (linesOpacity > 0)
                    DrawRangeText();
            }
        }

        private void DrawShipIcons()
        {
            var shipsTargeting = ships.FindAll(ship => ship.controllerRunning
                                                        && ship.state != "Withdrawing"
                                                        && ship.alive
                                                        && ship.target != null);

            var shipTargets = shipsTargeting.Select(ship => ship.target).ToList();

            foreach (var ship in ships)
            {
                if (ship == null || ship.vessel == null) continue;

                currentIconColour = Color.grey;

                if (ship.alive)
                    ColorUtility.TryParseHtmlString(ship.SideColour(), out currentIconColour);

                // Draw a diamond icon with the ship's team colour.
                // TODO: use different regular polygons for each team for colour-blindness.  
                DrawIcon(ship.vessel.CoM, iconTexture, Vector2.one * iconSize, currentIconColour);

                // Draw a ring around the ship's target to represent target lock.
                // Increase the size of the ring to make concentric circles with other ships that have the same target.
                if (shipsTargeting.Contains(ship))
                {
                    shipTargets.Remove(ship.target);
                    int shipTargetCount = shipTargets.FindAll(t => t.persistentId == ship.target.persistentId).Count;
                    shipTargetCount = Mathf.Min(shipTargetCount, maxTargetCircles - 1);

                    if (ship.vessel.persistentId == FlightGlobals.ActiveVessel.persistentId)
                        currentIconColour = activeTargetColour;

                    DrawIcon(ship.target.CoM, targetCircles[shipTargetCount], Vector2.one * targetCircles[shipTargetCount].width, currentIconColour);
                }
            }
        }

        private void DrawWeaponIcons()
        {
            var allMissiles = KCSController.weaponsInFlight.Concat(KCSController.interceptorsInFlight);

            foreach (var wep in allMissiles)
            {
                if (wep == null || wep.missed || !wep.launched) continue;

                currentIconColour = Color.white;

                if (!wep.isInterceptor)
                    ColorUtility.TryParseHtmlString(ModuleShipController.SideColour(wep.side), out currentIconColour);

                // Draw a small diamond for each missile with the missile's team colour.
                // Draw a white diamond for interceptors.
                // TODO: special missile icon.
                DrawIcon(wep.vessel.CoM, iconTexture, Vector2.one * iconSize * 0.25f, currentIconColour);
            }
        }

        private void DrawRangeText()
        {
            // Setup the font the first time the function is called. This needs to be done inside OnGUI().
            if (rangeNumberStyle == null)
            {
                rangeNumberStyle = new GUIStyle(GUI.skin.label);

                Font dottyFont = Resources.FindObjectsOfTypeAll<Font>().ToList().Find(f => f.name == "dotty");
                if (dottyFont != null)
                    rangeNumberStyle.font = dottyFont;
            }

            Vector3 north = centre.right;
            Vector2 textSize = rangeNumberStyle.CalcSize(new GUIContent("99k"));
            Rect textRect = new Rect(0, 0, textSize.x, textSize.y);
            Vector3 pos, direction, screenPos;
            string text;

            GUI.color = rangeNumberColour;

            for (int i = 0; i < numberOfRangeLines; i++)
            {
                direction = Quaternion.AngleAxis(360 * i / numberOfRangeLines, centre.up) * north;

                foreach (float range in ranges)
                {
                    pos = centre.position + (direction * range * size);
                    screenPos = Camera.main.WorldToScreenPoint(pos);

                    text = Math.Round((range * size) / 1000, 1).ToString();

                    textRect.x = screenPos.x - textSize.x / 2;
                    textRect.y = (Screen.height - screenPos.y) - textSize.y;

                    if (textRect.x > Screen.width || textRect.y > Screen.height || screenPos.z < 0) continue;

                    // Draw a column of range numbers (5, 7.5, 10, etc) along each cardinal direction.
                    GUI.Label(textRect, text, rangeNumberStyle);
                }
            }
        }

        private void DrawShipText()
        {
            // Setup the font the first time the function is called. This needs to be done inside OnGUI().
            if (shipNameStyle == null)
            {
                shipNameStyle = new GUIStyle(GUI.skin.label);

                Font calibriliFont = Resources.FindObjectsOfTypeAll<Font>().ToList().Find(f => f.name == "calibrili");
                if (calibriliFont != null)
                    shipNameStyle.font = calibriliFont;
            }

            if (shipNameStyle.fontSize != shipFontSize)
                shipNameStyle.fontSize = shipFontSize;

            Vector2 textSize = shipNameStyle.CalcSize(new GUIContent("DD-02 PAS Rigel Kentaurus Class Battlecruiser"));
            Rect textRect = new Rect(0, 0, textSize.x, textSize.y);
            Vector3 screenPos;

            foreach (var ship in ships)
            {
                if (ship == null || ship.vessel == null)
                    continue;

                // Fade the ship name as we get closer. Don't draw anything if it's faded out entirely.

                float alpha = Mathf.Clamp01((Vector3.Distance(Camera.main.transform.position, ship.vessel.CoM) - 150) / 100);
                if (alpha <= 0)
                    continue;

                // Calculate the screen position to display the ship name.

                screenPos = Camera.main.WorldToScreenPoint(ship.vessel.CoM);

                textRect.x = screenPos.x + 18;
                textRect.y = (Screen.height - screenPos.y) - (textSize.y / 2);

                if (textRect.x > Screen.width || textRect.y > Screen.height || screenPos.z < 0) continue;

                // Create a shorterned ship name with a bold prefix.

                string name = ShorternName(ship.vessel.GetDisplayName());
                string[] nameWords = name.Split(' ');
                bool endPrefix = false;
                bool allCaps;

                for (int i = 0; i < nameWords.Length; i++)
                {
                    string w = nameWords[i];

                    allCaps = w == w.ToUpper();
                    if (!allCaps)
                        endPrefix = true;

                    nameWords[i] = allCaps && !endPrefix ? $"<b>{w}</b>" : $"<i>{w}</i>";
                }

                name = string.Join(" ", nameWords);

                // Draw the ship name.

                shipNameColour.a = shipnameOpacity * alpha * globalOpacity;
                GUI.color = shipNameColour;
                GUI.Label(textRect, name, shipNameStyle);
            }
        }

        private Texture2D CreateCircleTexture(int size, int radius, int lineThickness, Color colour)
        {
            // Create new texture and clear it.

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Trilinear;

            Color[] clearColours = new Color[size * size];
            for (int i = 0; i < clearColours.Length; i++)
                clearColours[i] = Color.clear;

            texture.SetPixels(clearColours);

            // Draw circle.

            float rSquared = radius * radius;
            int x = size / 2;
            int y = size / 2;

            for (int u = x - radius; u < x + radius + 1; u++)
                for (int v = y - radius; v < y + radius + 1; v++)
                    if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared)
                        texture.SetPixel(u, v, colour);

            // Remove interior.

            radius -= lineThickness;
            rSquared = radius * radius;

            for (int u = x - radius; u < x + radius + 1; u++)
                for (int v = y - radius; v < y + radius + 1; v++)
                    if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared)
                        texture.SetPixel(u, v, Color.clear);

            // Update texture.
            texture.Apply();

            return texture;
        }

        private void DrawIcon(Vector3 pos, Texture texture, Vector2 size, Color colour)
        {
            float alpha = Mathf.Clamp01((Vector3.Distance(Camera.main.transform.position, pos) - 150) / 100);
            if (alpha <= 0) return;

            Vector3 screenPos = Camera.main.WorldToViewportPoint(pos);
            float xPos = (screenPos.x * Screen.width) - (0.5f * size.x);
            float yPos = ((1 - screenPos.y) * Screen.height) - (0.5f * size.y);

            if (xPos > Screen.width || yPos > Screen.height || screenPos.z < 0)
                return;

            colour.a = 0.6f * alpha * globalOpacity;
            iconMat.SetColor("_TintColor", colour);
            drawIconRect.x = xPos;
            drawIconRect.y = yPos;
            drawIconRect.width = size.x;
            drawIconRect.height = size.y;

            Graphics.DrawTexture(drawIconRect, texture, iconMat);
        }

        #endregion

        // When the UI is toggled with F2.
        internal static void SetVisibility(bool hidden)
        {
            hideOverlay = hidden;

            if (rangeLinesMesh == null)
                return;

            var linemeshes = new List<LineMesh>() {
                rangeLinesMesh,
                secondaryRangeLinesMesh,
                rangeRingsMesh,
                elevationLinesMesh,
                dashedLinesMesh,
                detectionRangeMesh,
                weaponRangeMesh
            };

            foreach (var linemesh in linemeshes)
            {
                linemesh.gameObject.GetComponent<MeshRenderer>().enabled = !hidden;
            }

            markers.ForEach(m => m.gameObject.GetComponent<SpriteRenderer>().enabled = !hidden);
        }
    }

    // 3D circle sprites that visually connect the elevation lines to the plane defined by the range rings.
    public class Marker : MonoBehaviour
    {
        public Transform centre;
        public ModuleShipController target;

        private Transform TargetTransform => target.vessel.transform;
        private bool deleted = false;
        private bool visible = true;
        private Vector3 onPlane;
        private Vector3 centreToTarget;

        void Update()
        {
            if (centre == null || target == null)
            {
                DeleteMarker();
                return;
            }

            // Hide the marker if it belongs to the active vessel.
            bool isActive = target.vessel == FlightGlobals.ActiveVessel;
            if (visible == isActive && !Overlay.hideOverlay)
            {
                visible = !isActive;
                GetComponent<SpriteRenderer>().enabled = visible;
            }

            if (Overlay.useElevationArcs)
            {
                // Place the marker on the overlay plane at the correct range.
                centreToTarget = target.transform.position - centre.position;
                onPlane = Vector3.ProjectOnPlane(centreToTarget, centre.up);
                transform.position = centre.transform.position + (onPlane.normalized * centreToTarget.magnitude);
            }
            else
            {
                // Place the marker on the overlay plane underneath/above the target vessel.
                transform.position = centre.transform.position + Vector3.ProjectOnPlane(TargetTransform.position - centre.position, centre.up);
            }

            transform.forward = centre.up;

            // Maintain a fixed screen size.
            if (Overlay.mainCamera != null)
                transform.localScale = Vector3.one * Overlay.markerScale * (Vector3.Distance(transform.position, Overlay.mainCamera.transform.position) / 1000);
        }

        internal void DeleteMarker()
        {
            if (deleted) return;
            deleted = true;
            Destroy(gameObject);
        }
    }
}
