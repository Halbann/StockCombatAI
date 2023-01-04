using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    // Overall controller for KCS. Setup battles, 
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class KCSController : MonoBehaviour
    {
        #region Fields

        // GUI variables.
        private static ApplicationLauncherButton appLauncherButton;
        private static bool addedAppLauncherButton = false;
        private static bool guiEnabled = false;
        private static bool guiHidden;

        private int windowWidth = 350;
        private int windowHeight = 700;
        private Rect windowRect;
        private GUIStyle boxStyle;
        private GUIStyle smallTextButtonStyle;
        private GUIStyle buttonStyle;
        private GUIStyle titleStyle;
        private GUIStyle centeredText;
        private static int scrollViewHeight;
        private Vector2 scrollPosition;
        private Vector2 settingsScrollPosition;
        private static Vector2 logScrollPosition;
        private const int logScrollHeight = 350;

        private readonly string[] modes = { "Ships", "Weapons", "Log", "Settings" };
        private string mode = "Ships";

        public static List<ModuleShipController> ships;
        public static List<ModuleWeaponController> weaponsInFlight;
        public static List<ModuleWeaponController> interceptorsInFlight;
        private float lastUpdateTime;

        List<ModuleWeaponController> weaponList;
        ModuleWeaponController selectedWeapon;
        private Vessel currentVessel;

        private static List<string> log;
        private static float lastLogged;
        private bool updateOverlayOpacity;

        static bool hasCC;
        static bool hasPRE;

        #endregion

        #region Main

        private void Awake()
        {
            log = new List<string>();
        }

        private void Start()
        {
            // Setup GUI. 

            windowRect = new Rect((Screen.width * 0.85f) - (windowWidth / 2), (Screen.height / 2) - (windowHeight / 2), 0, 0);
            guiEnabled = false;

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

        private void Update()
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

        void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(VesselEventUpdate);
            GameEvents.onVesselDestroy.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Remove(VesselEventUpdate);
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
            Debug.Log("[KCS]: Updated ship list.");

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
            var c = FlightGlobals.ActiveVessel.FindPartModuleImplementing<ModuleShipController>();
            if (c == null)
            {
                weaponList = new List<ModuleWeaponController>();
                return;
            };

            c.CheckWeapons();
            weaponList = c.weapons;

            var ungroupedMissiles = weaponList.FindAll(m => m.weaponCode == "");
            weaponList = weaponList.Except(ungroupedMissiles).ToList();
            weaponList = weaponList.GroupBy(m => m.weaponCode).Select(g => g.First()).ToList();
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
            if (selectedWeapon == null) return;
            selectedWeapon.Fire();
            UpdateWeaponList();
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

        void OnGUI()
        {
            if (guiEnabled && !guiHidden) DrawGUI();
        }

        private void DrawGUI() =>
            windowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, FillWindow, "Kerbal Combat Systems", GUILayout.Height(0), GUILayout.Width(mode != "Log" ? windowWidth : windowWidth * 1.25f));

        private void FillWindow(int windowID)
        {
            if (boxStyle == null)
            {
                buttonStyle = GUI.skin.button;
                boxStyle = GUI.skin.GetStyle("Box");

                smallTextButtonStyle = new GUIStyle(buttonStyle);
                smallTextButtonStyle.fontSize = 10;
                smallTextButtonStyle.alignment = TextAnchor.MiddleCenter;

                titleStyle = new GUIStyle(GUI.skin.label);
                titleStyle.alignment = TextAnchor.MiddleCenter;
                titleStyle.fontStyle = FontStyle.Bold;

                centeredText = new GUIStyle(GUI.skin.label);
                centeredText.alignment = TextAnchor.MiddleCenter;
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

                    if (GUILayout.Toggle(w == selectedWeapon, weaponName, GUI.skin.button))
                        selectedWeapon = w;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUILayout.Button("Update List")) UpdateWeaponList();
            if (GUILayout.Button("Fire")) FireSelectedWeapon();
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

        private void AddToolbarButton()
        {
            if (!addedAppLauncherButton)
            {
                Texture buttonTexture = GameDatabase.Instance.GetTexture("KCS/Icons/Button", false);
                appLauncherButton = ApplicationLauncher.Instance.AddModApplication(ToggleGui, ToggleGui, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, buttonTexture);
                addedAppLauncherButton = true;
            }

            if (appLauncherButton.isActiveAndEnabled)
                appLauncherButton.SetFalse(false);
        }

        public void ToggleGui()
        {
            if (guiEnabled)
            {
                DisableGui();
                appLauncherButton.SetFalse(false);
            }
            else
            {
                UpdateShipList();
                UpdateWeaponList();
                EnableGui();
            }
        }
        public void EnableGui() { guiEnabled = true; }
        public void DisableGui() { guiEnabled = false; }

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