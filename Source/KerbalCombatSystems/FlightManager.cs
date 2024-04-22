using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using KSP.UI.Screens;

using static KerbalCombatSystems.Utils;

namespace KerbalCombatSystems
{
    // Overall flight controller for KCS.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class FlightManager : MonoBehaviour
    {
        #region Fields

        // Instance variable.

        private static FlightManager instance;
        public static FlightManager Instance => instance;

        // GUI variables.

        private ApplicationLauncherButton appLauncherButton;
        private bool guiEnabled = false;
        private bool guiHidden;

        private const int windowWidth = 350;
        private const int windowHeight = 700;
        private static Rect windowRect = new Rect((Screen.width * 0.85f) - (windowWidth / 2), (Screen.height / 2) - (windowHeight / 2), 0, 0);
        private GUIStyle boxStyle;
        private GUIStyle smallTextButtonStyle;
        private GUIStyle buttonStyle;
        private GUIStyle titleStyle;
        private GUIStyle centeredText;
        private int scrollViewHeight;
        private Vector2 scrollPosition;
        private Vector2 settingsScrollPosition;
        private static Vector2 logScrollPosition;
        private const int logScrollHeight = 350;
        private static bool scrollLock = false;

        private readonly string[] modes = { "Ships", "Weapons", "Log", "Settings", "Debug" };
        private string mode = "Ships";


        // Flight manager variables.

        public static List<ModuleShipController> ships;
        public static List<ModuleWeaponController> weaponsInFlight;
        public static List<ModuleWeaponController> interceptorsInFlight;
        private float lastUpdateTime;


        // Weapon variables.

        List<ModuleWeaponController> weaponList;
        ModuleWeaponController selectedWeapon;
        private Vessel currentVessel;
        private static float launchFailureTime = float.NegativeInfinity;
        private static string launchFailureReason;


        // Battle log variables.

        private static readonly List<string> log = new List<string>();
        private static float lastLogged;
        private bool updateOverlayOpacity;


        // Dependency variables.

        static bool hasCC;
        static bool hasPRE;

        #endregion

        #region Main

        internal void Awake()
        {
            instance = this;
        }

        internal void Start()
        {
            // Clear log.

            log.Clear();

            // Setup GUI. 

            AddToolbarButton();

            // Register vessel updates.

            UpdateWeaponList();
            UpdateShipList();

            GameEvents.onVesselCreate.Add(VesselEventUpdate);
            GameEvents.onVesselDestroy.Add(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Add(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Add(VesselEventUpdate);

            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            foreach (var a in AssemblyLoader.loadedAssemblies)
            {
                if (!hasPRE && a.assembly.FullName.Contains("PhysicsRangeExtender"))
                {
                    hasPRE = true;
                    continue;
                }

                if (!hasCC && a.assembly.FullName.Contains("ContinuousCollision"))
                {
                    hasCC = true;
                    continue;
                }

                if (hasCC && hasPRE)
                    break;
            }

            // Change game settings.

            HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().EnableFullSASInSandbox = true;   
        }

        internal void Update()
        {
            ManageThrottle();
        }

        private void ManageThrottle()
        {
            Vessel a = FlightGlobals.ActiveVessel;
            if (a == null) return;

            bool fcRunning = !a.ActionGroups[KSPActionGroup.SAS] && a.Autopilot.Mode == VesselAutopilot.AutopilotMode.Normal;
            if (fcRunning || currentVessel != a)
            {
                FlightInputHandler.state.mainThrottle = a.ctrlState.mainThrottle;
            }

            currentVessel = a;
        }

        internal void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(VesselEventUpdate);
            GameEvents.onVesselDestroy.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Remove(VesselEventUpdate);

            RemoveToolbarButton();
        }

        #endregion

        #region Ship Functions

        private void VesselEventUpdate(Vessel v)
        {
            if (Time.time - lastUpdateTime < 2)
            {
                lastUpdateTime = Time.time;
                return;
            }

            lastUpdateTime = Time.time;
            StartCoroutine(UpdateShipListCountdown());
        }

        private IEnumerator UpdateShipListCountdown()
        {
            while (Time.time - lastUpdateTime < 2)
            {
                yield return new WaitForSecondsRealtime(2);
            }

            UpdateShipList();
        }

        private void UpdateShipList()
        {
            Debug.Log("Updated ship list.");

            var loadedVessels = FlightGlobals.VesselsLoaded;
            ships = new List<ModuleShipController>();
            weaponsInFlight = new List<ModuleWeaponController>();
            interceptorsInFlight = new List<ModuleWeaponController>();

            foreach (Vessel v in loadedVessels)
            {
                var p = v.FindPartModuleImplementing<ModuleShipController>();
                if (p != null)
                {
                    ships.Add(p);
                    continue;
                }

                var w = v.FindPartModuleImplementing<ModuleWeaponController>();
                if (w != null && !w.missed)
                {
                    if (!w.isInterceptor)
                        weaponsInFlight.Add(w);
                    else
                        interceptorsInFlight.Add(w);
                }
            }

            ships = ships.OrderBy(s => s.side.ToString()).ToList();
        }

        private void UpdateWeaponList()
        {
            // Collect a sorted, collapsed list of usable
            // weapons on the active vessel for display in the flight UI.

            var c = FlightGlobals.ActiveVessel.FindPartModuleImplementing<ModuleShipController>();
            if (c == null)
            {
                weaponList = new List<ModuleWeaponController>();
                return;
            };

            c.CheckWeapons();
            weaponList = c.weapons;

            // Separate out the uncoded weapons.
            var ungroupedMissiles = weaponList.FindAll(m => m.weaponCode == "");
            weaponList = weaponList.Except(ungroupedMissiles).ToList();

            // Get one weapon per weapon code, the one with the fewest child decouplers.
            weaponList = weaponList
                .GroupBy(m => m.weaponCode)
                .Select(g => g.OrderBy(m => m.childDecouplers).First())
                .ToList();
            
            weaponList = weaponList.OrderByDescending(m => m.weaponCode).ToList();

            // Get one weapon per weapon of a certain mass, the one with the fewest child decouplers.
            ungroupedMissiles = ungroupedMissiles
                .GroupBy(m => Math.Round(m.mass, 1))
                .Select(g => g.OrderBy(m => m.childDecouplers).First())
                .ToList();
            
            ungroupedMissiles = ungroupedMissiles.OrderByDescending(m => m.mass).ToList();
            weaponList = weaponList.Concat(ungroupedMissiles).ToList();
        }

        public void ToggleAIs()
        {
            bool running = ships.FindIndex(c => c.controllerRunning) > -1;

            foreach (var controller in ships)
            {
                if (running)
                    controller.StopAI();
                else
                    controller.StartAI();
            }
        }

        public void FireSelectedWeapon()
        {
            if (selectedWeapon == null)
                return;

            selectedWeapon.Fire();
            UpdateWeaponList();
        }

        public static void OnWeaponFailed(string reason)
        {
            launchFailureTime = Time.time;
            launchFailureReason = reason;
        }

        #endregion

        #region Logging

        public static void Log(string text)
        {
            if (Time.time - lastLogged > 3 && log.Count > 0)
                log.Add(string.Format("<color=#808080>-</color>"));

            log.Add(text);

            lastLogged = Time.time;
            logScrollPosition.y = int.MaxValue;
        }

        public static void Log(string text, Vessel v1, Vessel v2)
        {
            if (v1 == null) return;

            var c = FindController(v1);

            string colour = "#808080";
            if (c != null && c.alive)
                colour = c.SideColour();

            text = text.Replace("%1", string.Format("<color={1}>{0}</color>", ShortenName(v1.GetDisplayName()), colour));

            if (text.Contains("%2"))
            {
                if (v2 == null)
                {
                    text = text.Replace("%2", "unknown");
                }
                else
                {
                    c = FindController(v2);

                    colour = "#808080";
                    if (c != null && c.alive)
                        colour = c.SideColour();

                    text = text.Replace("%2", string.Format("<color={1}>{0}</color>", ShortenName(v2.GetDisplayName()), colour));
                }
            }

            Log(text);
        }

        public static void Log(string text, Vessel v1) =>
            Log(text, v1, null);

        #endregion

        #region GUI

        // GUI functions.

        internal void OnGUI()
        {
            if (guiEnabled && !guiHidden)
                DrawGUI();
        }

        private void DrawGUI()
        {
            windowRect = GUILayout.Window(
                GUIUtility.GetControlID(FocusType.Passive),
                windowRect,
                FillWindow,
                "KCS Beta v0.3.0",
                GUILayout.Height(0),
                GUILayout.Width(mode != "Log" ? windowWidth : windowWidth * 1.25f)
            );
        }

        private void FillWindow(int windowID)
        {
            if (boxStyle == null)
            {
                buttonStyle = GUI.skin.button;
                boxStyle = GUI.skin.GetStyle("Box");

                smallTextButtonStyle = new GUIStyle(buttonStyle)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter
                };

                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };

                centeredText = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            // Lock scroll zoom when mousing over the UI.

            bool lockedScroll = false;
            if (windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                lockedScroll = true;
                scrollLock = true;
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "KCSGUI");
            }

            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), ""))
                ToggleGui();

            if (GUI.Button(new Rect(windowRect.width - (18 * 2 + 8), 2, 24, 16), "O", smallTextButtonStyle))
                Overlay.DistanceToggle();

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            foreach (var m in modes)
            {
                if (GUILayout.Toggle(mode == m, m, buttonStyle))
                    mode = m;
            }

            GUILayout.EndHorizontal();

            switch (mode)
            {
                case "Ships":
                    ShipsGUI();
                    break;
                case "Weapons":
                    WeaponsGUI();
                    break;
                case "Log":
                    LogGUI();
                    break;
                case "Settings":
                    SettingsGUI();
                    break;
                case "Debug":
                    DebugGUI();
                    break;
                default:
                    GUILayout.Label("Something went wrong...");
                    break;
            }

            if (!hasCC)
                WarningMessage("Missing Continuous Collisions! Without continuous collisions, " +
                    "high speed missiles will phase through small targets.");

            if (!hasPRE)
                WarningMessage("Missing Physics Range Extender! Without PRE, " +
                    "KCS can't control vessels further than 200 metres away.");

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 500));

            if (!lockedScroll && scrollLock)
                InputLockManager.RemoveControlLock("KCSGUI");
        }

        private void WarningMessage(string message)
        {
            GUI.color = Color.red;
            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label(message);
            GUILayout.EndVertical();
            GUI.color = Color.white;
        }

        private void ShipsGUI()
        {
            GUILayout.BeginVertical(boxStyle);
            scrollViewHeight = Mathf.Max(Mathf.Min(5 * 45, 45 * ships.Count), 5 * 45);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(scrollViewHeight), GUILayout.Width(windowWidth));
            if (ships.Count > 0)
            {
                Vessel v;
                ModuleShipController c;

                foreach (var controller in ships)
                {
                    c = controller;
                    v = c.vessel;

                    string colour = "#ffffff";
                    var activeTarget = FlightGlobals.ActiveVessel.targetObject;

                    if (!c.alive)
                        colour = "#808080";
                    else if (v == FlightGlobals.ActiveVessel)
                        colour = "#00f2ff";
                    else if (activeTarget != null && v == activeTarget.GetVessel())
                        colour = "#b4ff33";

                    string targetName = c.target == null ? "None" : c.target.vesselName;
                    string craftName = String.Format("<color={6}>{0}</color>\n<color=#808080ff>Part Count: {1}, Mass: {2} t, IR: {7}\nTarget: {4}\nState: {5}</color>",
                        v.GetDisplayName(), v.parts.Count, Math.Round(v.GetTotalMass(), 1), null, targetName, c.state, colour, Math.Round(c.heatSignature));

                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button(craftName, GUILayout.Width(windowWidth * 0.8f)))
                    {
                        FlightGlobals.ForceSetActiveVessel(v);
                        UpdateWeaponList();
                    }

                    GUILayout.BeginVertical();

                    string AI = String.Format("<color={0}>AI</color>", c.controllerRunning ? "#07D207" : "#FFFFFF");
                    if (GUILayout.Button(AI))
                        c.ToggleAI();

                    if (GUILayout.Button(String.Format("<color={1}>{0}</color>", c.side, c.SideColour())))
                        c.ToggleSide();

                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUILayout.Button("Update List")) UpdateShipList();
            if (GUILayout.Button("Enable/Disable AIs")) ToggleAIs();
        }

        private void WeaponsGUI()
        {
            GUILayout.BeginVertical(boxStyle);

            scrollViewHeight = Mathf.Max(Mathf.Min(15 * 30, 30 * weaponList.Count), 5 * 30);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(scrollViewHeight), GUILayout.Width(windowWidth));
            
            if (weaponList.Count > 0)
            {
                foreach (var w in weaponList)
                {
                    string code = w.weaponCode == "" ? w.weaponType : w.weaponCode;
                    string weaponName = string.Format("{0}\n<color=#808080ff>Type: {1}, Mass: {2} t</color>",
                        code, w.weaponType, w.mass.ToString("0.0"));

                    bool sameAsSelected = false;
                    if (selectedWeapon != null)
                    {
                        if (w.weaponCode != "")
                        {
                            if (w.weaponCode == selectedWeapon.weaponCode)
                                sameAsSelected = true;
                        }
                        else if (Mathf.Approximately((float)Math.Round(w.mass, 1), (float)Math.Round(selectedWeapon.mass, 1)))
                            sameAsSelected = true;
                    }

                    if (GUILayout.Toggle(w == selectedWeapon || sameAsSelected, weaponName, GUI.skin.button))
                        selectedWeapon = w;
                }
            }

            // Remove the selected weapon after it has been fired and we have selected its sibling.
            if (selectedWeapon != null && selectedWeapon.vessel.persistentId != FlightGlobals.ActiveVessel.persistentId)
                selectedWeapon = null;

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUILayout.Button("Update List"))
                UpdateWeaponList();

            if (GUILayout.Button("Fire"))
                FireSelectedWeapon();

            if (Time.time - launchFailureTime < 3)
            {
                GUI.color = Color.red;
                GUILayout.Label("Launch failed: " + launchFailureReason);
                GUI.color = Color.white;
            }
        }

        private void LogGUI()
        {
            GUILayout.BeginVertical(boxStyle);
            logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, false, true, GUILayout.Height(logScrollHeight), GUILayout.Width(windowWidth * 1.25f));
            foreach (var text in log)
            {
                GUILayout.Label(text);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void SettingsGUI()
        {
            GUILayout.BeginVertical(boxStyle);
            settingsScrollPosition = GUILayout.BeginScrollView(settingsScrollPosition, false, false, GUILayout.Height(scrollViewHeight), GUILayout.Width(windowWidth));

            GUILayout.Label("Overlay", titleStyle);

            Overlay.useElevationArcs = GUILayout.Toggle(Overlay.useElevationArcs, "Use Elevation Arcs");
            Overlay.hideWithUI = GUILayout.Toggle(Overlay.hideWithUI, "Hide with UI");

            SliderSetting(ref Overlay.globalOpacity, "Global Opacity", 0, 4);
            SliderSetting(ref Overlay.rangeRingsOpacity, "Range Rings Opacity", 0, 1);
            SliderSetting(ref Overlay.rangeLinesOpacity, "Range Lines Opacity", 0, 1);
            SliderSetting(ref Overlay.secondaryRangeLinesOpacity, "Secondary Lines Opacity", 0, 1);
            SliderSetting(ref Overlay.dashedLinesOpacity, "Target Lines Opacity", 0, 1);
            SliderSetting(ref Overlay.elevationLinesOpacity, "Elevation Lines Opacity", 0, 1);
            SliderSetting(ref Overlay.markerOpacity, "Marker Opacity", 0, 1);

            if (updateOverlayOpacity)
            {
                updateOverlayOpacity = false;
                Overlay.UpdateOpacity();
            }

            // Hidden until CC supports fireworks.
            //GUILayout.Label("Gameplay", titleStyle);
            //SliderSetting(ref ModuleFirework.fireworkSpeed, "Firework Speed", 100, 500);

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Label("This menu is a placeholder. Settings changes are not permanent.");
        }

        private void DebugGUI()
        {
            bool debugVisible = GUILayout.Toggle(Debug.Visible, "Draw Debug Info");

            if (debugVisible != Debug.Visible)
                Debug.Visible = debugVisible;

            Debug.DrawVesselsBounds = GUILayout.Toggle(Debug.DrawVesselsBounds, "Draw Vessel Sizes");
            Debug.drawText = GUILayout.Toggle(Debug.drawText, "Draw Text");

            var a = FlightGlobals.ActiveVessel;
            var c = FindController(a);
            if (a != null && c != null)
            {
                GUILayout.Label($"Active Vessel: <b>{ShortenName(a.vesselName)}</b>");
                GUILayout.Label($"- State: {c.state}");

                string[] state = new string[]
                {
                    c.hasWeapons ? "WEP" : "",
                    c.hasPropulsion ? "PROP" : "",
                    c.hasControl ? "CTRL" : ""
                };

                string statusString = string.Join(", ", state.Where(s => s != ""));

                GUILayout.Label($"- Alive: {c.alive} ({statusString})");
                GUILayout.Label($"- Timer: {c.timeRemaining:0.00}");

                GUILayout.Label($"- Throttle: {c.fc.throttleActual * 100f:0} %");
                GUILayout.Label($"- Perturbation: {a.perturbation.magnitude:0.0} m/s");

                GUILayout.Label($"- Detection Range: {c.maxDetectionRange:0} m");
                GUILayout.Label($"- Weapon Range: {c.maxWeaponRange:0} m");

                if (c.target != null)
                {
                    GUILayout.Label($"- Target: <b>{ShortenName(c.target.vesselName)}</b>");

                    var targetRange = FromTo(a, c.target).magnitude;
                    GUILayout.Label($"- Target Range: {targetRange:N0} m");
                    GUILayout.Label($"- Approach Time: {c.nearInterceptApproachTime:N1} s");
                    GUILayout.Label($"- Burn Time: {c.nearInterceptBurnTime:N1} s");
                }

                GUILayout.Label($"- Incoming: {c.incomingWeapons?.Count ?? 0}");
                GUILayout.Label($"- Evading: {c.dodgeWeapons?.Count ?? 0}");
                GUILayout.Label($"- Intercepting: {c.weaponsToIntercept?.Count ?? 0}");
            }
        }

        private void SliderSetting(ref float setting, string text, int min, int max)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(text, GUILayout.Width(windowWidth * 0.25f));

            float settingLast = setting;
            setting = GUILayout.HorizontalSlider((float)Math.Round(setting, 2), min, max);

            GUILayout.Label(setting.ToString(), centeredText, GUILayout.Width(windowWidth * 0.25f));

            if (setting != settingLast && text.Contains("opacity"))
                updateOverlayOpacity = true;

            GUILayout.EndHorizontal();
        }

        // Application launcher/Toolbar setup.

        public void AddToolbarButton()
        {
            if (appLauncherButton != null)
                return;

            var scenes = ApplicationLauncher.AppScenes.FLIGHT;
            Texture buttonTexture = GameDatabase.Instance.GetTexture("KCS/Icons/Button", false);
            appLauncherButton = ApplicationLauncher.Instance.AddModApplication(EnableGui, DisableGui, null, null, null, null, scenes, buttonTexture);
        }

        public void RemoveToolbarButton()
        {
            if (appLauncherButton == null)
                return;

            ApplicationLauncher.Instance.RemoveModApplication(appLauncherButton);
            appLauncherButton = null;
        }

        public void ToggleGui()
        {
            if (guiEnabled)
                DisableGui();
            else
                EnableGui();
        }

        public void EnableGui() 
        {
            UpdateShipList();
            UpdateWeaponList();
            guiEnabled = true;
        }

        public void DisableGui() 
        {
            InputLockManager.RemoveControlLock("KCSGUI");
            guiEnabled = false;
        }

        private void OnShowUI() =>
            OnToggleUI(false);

        private void OnHideUI() =>
            OnToggleUI(true);

        private void OnToggleUI(bool hide)
        {
            guiHidden = hide;

            if ((Overlay.hideWithUI || !hide) && HighLogic.LoadedSceneIsFlight && !Overlay.overlayUnavailable)
                Overlay.SetVisibility(!hide);
        }

        #endregion
    }
}