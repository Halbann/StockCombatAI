using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using KSP.UI.Screens;
using KSP.UI;

using static KerbalCombatSystems.Utils;
using KerbalCombatSystems.Data;
using KerbalCombatSystems.Fireworks;

namespace KerbalCombatSystems
{
    // Overall flight controller for KCS.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class FlightManager : MonoBehaviour
    {
        #region Fields

        // GUI variables.

        private const string title = "KCS Beta v0.3.0 Preview 4";
        private ApplicationLauncherButton appLauncherButton;
        private bool guiEnabled = false;
        private bool guiHidden;

        public static int windowWidth = 400;
        public static int windowHeight = 700;
        public static int shipButtonWidth = 310;
        public static int shipButtonHeight = 71;
        public static int weaponButtonHeight = 41;
        public static int settingsScrollHeight = 340;
        public static int logScrollHeight = 350;

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
        private static bool scrollLock = false;
        private RectTransform clickBlocker;

        private readonly string[] modes = { "Ships", "Weapons", "Log", "Settings", "Debug" };
        private string mode = "Ships";


        // Flight manager variables.

        public static List<ModuleShipController> ships = new List<ModuleShipController>();
        public static List<ModuleWeaponController> weaponControllers = new List<ModuleWeaponController>();
        public static List<ModuleWeaponController> weaponsInFlight = new List<ModuleWeaponController>();
        public static List<ModuleWeaponController> interceptorsInFlight = new List<ModuleWeaponController>();
        private float lastUpdateTime;
        private float lastWeaponUpdateTime;


        // Weapon variables.

        private readonly List<ModuleWeaponController> weaponList = new List<ModuleWeaponController>();
        ModuleWeaponController selectedWeapon;
        private Vessel currentVessel;
        private static float launchFailureTime = float.NegativeInfinity;
        private static string launchFailureReason;


        // Battle log variables.

        private static readonly List<string> log = new List<string>();
        private static float lastLogged;


        // Dependency variables.

        static bool hasCC;
        static bool hasPRE;

        #endregion

        #region Main

        internal void Start()
        {
            // Clear log.
            log.Clear();

            // Setup GUI. 
            AddToolbarButton();
            CreateClickBlocker();

            UpdateWeaponsTab();
            UpdateMasterLists();

            // Register vessel updates.
            GameEvents.onVesselCreate.Add(VesselEventUpdate);
            GameEvents.onVesselDestroy.Add(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Add(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Add(VesselEventUpdate);

            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWasModified.Add(OnVesselModified);

            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);

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

        private void OnGameSceneLoadRequested(GameScenes data)
        {
            if (guiEnabled)
                DisableGui();

            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
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

            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWasModified.Remove(OnVesselModified);

            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);

            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);

            RemoveToolbarButton();
            Destroy(clickBlocker?.gameObject);

            ships.Clear();
            weaponsInFlight.Clear();
            interceptorsInFlight.Clear();
        }

        #endregion

        #region Ship Functions

        private void VesselEventUpdate(Vessel v)
        {
            if (Time.time - lastUpdateTime <= Time.fixedUnscaledDeltaTime)
                return;

            lastUpdateTime = Time.time;
            UpdateMasterLists();
        }

        internal static void Register(ModuleShipController module)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            ships.Add(module);
        }

        internal static void Unregister(ModuleShipController module)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            ships.Remove(module);
        }

        internal static void Register(ModuleWeaponController module)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            weaponControllers.Add(module);
        }

        internal static void Unregister(ModuleWeaponController module)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            weaponControllers.Remove(module);
        }

        private void UpdateMasterLists()
        {
            // Weapons.
            weaponsInFlight.Clear();
            interceptorsInFlight.Clear();
            weaponControllers.RemoveAll(w => w == null || w.vessel == null);

            foreach (ModuleWeaponController w in weaponControllers)
            {
                if (w.missed)
                    continue;

                if (!w.isInterceptor)
                    weaponsInFlight.Add(w);
                else
                    interceptorsInFlight.Add(w);
            }

            // Ships.
            var sort = ships.Where(s => s != null && s.vessel != null);

            sort = sort.OrderBy(s => s.side)
                .ThenBy(s => Math.Round(s.initialMass, 1))
                .ThenBy(s => s.vessel.vesselName);

            ships = sort.ToList(); // I hate linq.
        }

        private void UpdateWeaponsTab()
        {
            // Collect a sorted, collapsed list of usable weapons on the active vessel for display in the flight UI.

            weaponList.Clear();

            if (FlightGlobals.ActiveVessel == null) return;
            var controller = FindController(FlightGlobals.ActiveVessel);
            if (controller == null) return;

            controller.CheckWeapons();
            if (controller.weapons.Count < 1) return;

            // We want one weapon per weapon type (weaponisidentical), the one with the lowest number of child decouplers

            List<List<ModuleWeaponController>> weaponGroups = new List<List<ModuleWeaponController>>();
            foreach (var w in controller.weapons)
            {
                bool foundGroup = false;

                foreach (var group in weaponGroups)
                {
                    if (group.Count > 0 && w.Identical(group[0]))
                    {
                        group.Add(w);
                        foundGroup = true;
                    }
                }

                if (foundGroup)
                    continue;

                weaponGroups.Add(new List<ModuleWeaponController> { w });
            }

            var sortedList = weaponGroups.Select(g => g.OrderBy(w => w.childDecouplers).First())
                .OrderBy(w => w.weaponCode == "")
                .ThenBy(w => w.mass);

            weaponList.AddRange(sortedList);
        }

        private void OnVesselChange(Vessel data) =>
            UpdateWeaponsTab();

        private void OnVesselModified(Vessel vessel)
        {
            if (Time.fixedTime - lastWeaponUpdateTime > Time.fixedDeltaTime 
                && vessel.persistentId == FlightGlobals.ActiveVessel.persistentId)
            {
                UpdateWeaponsTab();
                lastWeaponUpdateTime = Time.fixedTime;
            }
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

        public static void UpdateTargeting()
        {
            foreach (var controller in ships)
                if (controller?.controllerRunning ?? false)
                    controller.targeting.Update();
        }

        public void FireSelectedWeapon()
        {
            if (selectedWeapon == null)
                return;

            selectedWeapon.Fire();
            UpdateWeaponsTab();
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
                colour = c.Colour;

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
                        colour = c.Colour;

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
                title,
                GUILayout.Height(0),
                GUILayout.Width(windowWidth)
            );

            if (clickBlocker != null)
            {
                // Update click blocker. This prevents clicking through the IMGUI (important for part action window opening).
                clickBlocker.sizeDelta = new Vector2(windowRect.width, windowRect.height);
                clickBlocker.anchoredPosition = new Vector2(windowRect.x - 0.5f * Screen.width, 0.5f * Screen.height - windowRect.y);
            }
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
                Overlay.Instance.DistanceToggle();

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            foreach (var m in modes)
            {
                if (GUILayout.Toggle(mode == m, m, buttonStyle))
                {
                    // Save settings when switching off settings page.
                    if (mode != m && mode == "Settings")
                        GlobalSettings.Save();

                    mode = m;
                }
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
            scrollViewHeight = (int)Mathf.Max(Mathf.Min(Screen.height * 0.5f, shipButtonHeight * ships.Count), 4 * shipButtonHeight);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false, GUILayout.Height(scrollViewHeight));
            
            if (ships.Count > 0)
            {
                Vessel vessel;

                foreach (ModuleShipController controller in ships)
                {
                    if (controller == null)
                        continue;

                    // Ship button.
                    vessel = controller.vessel;

                    string colour = "#ffffff";
                    var activeTarget = FlightGlobals.ActiveVessel.targetObject;

                    if (!controller.alive)
                        colour = "#808080";
                    else if (vessel == FlightGlobals.ActiveVessel)
                        colour = "#00f2ff";
                    else if (activeTarget != null && vessel == activeTarget.GetVessel())
                        colour = "#b4ff33";

                    string targetName = controller.Target == null ? "None" : ShortenName(controller.Target.vesselName);
                    ModuleWeaponController wep = controller.currentWeapon;
                    string weaponName = wep == null ? "None" : (wep.weaponCode == "" ? wep.weaponType : wep.weaponCode);
                    
                    string craftText = $"<color={colour}>{ShortenName(vessel.vesselName)}</color>";
                    craftText += $"<color=#808080ff>";
                    craftText += $"\nMass: {Math.Round(vessel.totalMass, 1)} t, IR: {Mathf.Round(controller.heatSignature)}";
                    craftText += $"\nTarget: {targetName}";
                    craftText += $", Weapon: {weaponName}";
                    craftText += $"\nState: {controller.state}";
                    craftText += $"</color>";

                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button(craftText, GUILayout.Width(shipButtonWidth)) && Event.current.button == 0)
                        FlightGlobals.ForceSetActiveVessel(vessel);

                    OpenPartWindow(controller.part);

                    // Side and AI buttons.
                    GUILayout.BeginVertical();

                    var width = GUILayout.Width(10);
                    string AI = $"<color={(controller.controllerRunning ? "#07D207" : "#FFFFFF")}>AI</color>";
                    if (GUILayout.Button(AI))
                        controller.ToggleAI();

                    string sideText = $"<color={controller.Colour}>{controller.side}</color>";
                    if (GUILayout.Button(sideText))
                    {
                        controller.ToggleSide();
                        UpdateMasterLists();
                    }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUILayout.Button("Enable/Disable AIs"))
                ToggleAIs();
        }

        private void WeaponsGUI()
        {
            GUILayout.BeginVertical(boxStyle);

            float listHeight = Mathf.Min(Screen.height * 0.5f, weaponButtonHeight * weaponList.Count);
            scrollViewHeight = (int)Mathf.Max(listHeight, 4 * weaponButtonHeight);
            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition, false, false, GUILayout.Height(scrollViewHeight));
            string weaponName;
            bool selected;

            foreach (var w in weaponList)
            {
                weaponName = w.weaponCode == "" ? w.weaponType : w.weaponCode;
                weaponName += $"\n<color=#808080ff>";
                weaponName += $"Type: {w.weaponType}";

                if (ModuleWeaponController.massTypes.Contains(w.weaponType))
                    weaponName += $", Mass: {w.mass:N1} t";
                else if (w.weaponType == "Firework" && w.setup)
                    weaponName += $", Ammo: {w.Firework.AmmoCount}";

                weaponName += $"</color>";

                selected = w == selectedWeapon || w.Identical(selectedWeapon);
                if (GUILayout.Toggle(selected, weaponName, GUI.skin.button) && Event.current.button == 0)
                    selectedWeapon = w;

                OpenPartWindow(w.part);
            }

            // Remove the selected weapon after it has been fired and we have selected its sibling.
            if (selectedWeapon != null 
                && selectedWeapon.vessel.persistentId != FlightGlobals.ActiveVessel.persistentId)
                selectedWeapon = null;

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUILayout.Button("Fire"))
                FireSelectedWeapon();

            if (Time.time - launchFailureTime < 3)
            {
                GUI.color = Color.red;
                GUILayout.Label("Launch failed: " + launchFailureReason);
                GUI.color = Color.white;
            }
        }

        private void OpenPartWindow(Part part)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (Event.current.button == 1)
            {
                Rect rect = GUILayoutUtility.GetLastRect();
                if (rect.Contains(Event.current.mousePosition))
                    UIPartActionController.Instance.SpawnPartActionWindow(part);
            }

            return;
        }

        private void LogGUI()
        {
            GUILayout.BeginVertical(boxStyle);
            logScrollPosition = GUILayout.BeginScrollView(
                logScrollPosition, false, false, GUILayout.Height(logScrollHeight));

            foreach (var text in log)
                GUILayout.Label(text);

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void SettingsGUI()
        {
            GUILayout.BeginVertical(boxStyle);
            settingsScrollPosition = GUILayout.BeginScrollView(settingsScrollPosition, false, false, GUILayout.Height(settingsScrollHeight));


            // ----- Overlay -----
            GUILayout.Label("Overlay", titleStyle);

            Overlay.useElevationArcs = GUILayout.Toggle(Overlay.useElevationArcs, "Use Elevation Arcs");
            Overlay.hideWithUI = GUILayout.Toggle(Overlay.hideWithUI, "Hide With UI");
            Overlay.hideWhenOffline = GUILayout.Toggle(Overlay.hideWhenOffline, "Hide When Offline");

            bool updateOpacity = false;

            SliderSetting(ref Overlay.globalBrightness, "Overall Brightness", 0, 2, ref updateOpacity);
            SliderSetting(ref Overlay.rangeBrightness, "Range Brightness", 0, 2, ref updateOpacity);
            SliderSetting(ref Overlay.targetBrightness, "Target Brightness", 0, 2, ref updateOpacity);
            SliderSetting(ref Overlay.elevationBrightness, "Elevation Brightness", 0, 2, ref updateOpacity);
            SliderSetting(ref Overlay.iconBrightness, "Icon Brightness", 0, 2, ref updateOpacity);

            if (updateOpacity)
                Overlay.UpdateOpacity();


            // ----- Fireworks -----
            GUILayout.Space(20);
            GUILayout.Label("Fireworks", titleStyle);
            EnumSetting(ref LaunchShell.effectsOption, "Use Effects");


            // ----- Misc. -----
            GUILayout.Space(20);
            GUILayout.Label("Misc.", titleStyle);
            UI.TooltipController.showTooltips = GUILayout.Toggle(UI.TooltipController.showTooltips, "Show Tooltips");


            // Hidden until CC supports fireworks.
            //GUILayout.Label("Gameplay", titleStyle);
            //SliderSetting(ref ModuleFirework.fireworkSpeed, "Firework Speed", 100, 500);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset All"))
            {
                GlobalSettings.ResetAll();
                Overlay.UpdateOpacity();
            }
            GUILayout.FlexibleSpace();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
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

                GUILayout.Label($"- Detection Range: {c.maxLockRange:0} m");
                GUILayout.Label($"- Weapon Range: {c.maxWeaponRange:0} m");

                if (c.Target != null)
                {
                    GUILayout.Label($"- Target: <b>{ShortenName(c.Target.vesselName)}</b>");

                    var targetRange = VesselDistance(a, c.Target);
                    GUILayout.Label($"- Target Range: {targetRange:N0} m");
                    GUILayout.Label($"- Distance to CPA: {c.distanceToIntercept:N1} m");
                    GUILayout.Label($"- Stopping Distance: {c.interceptStoppingDistance:N1} m");
                    //GUILayout.Label($"- Stop Time: {Time.fixedTime + c.interceptStopTime:N1} s (T-{c.interceptStopTime:N1} s)");
                    GUILayout.Label($"- Time: {Time.fixedTime:N1} s");
                    //GUILayout.Label($"- Flip Iterations: {c.interceptFlipIterations} s");
                }

                GUILayout.Label($"- Incoming: {c.incomingWeapons?.Count ?? 0}");
                GUILayout.Label($"- Evading: {c.dodgeWeapons?.Count ?? 0}");
                GUILayout.Label($"- Intercepting: {c.weaponsToIntercept?.Count ?? 0}");
            }
        }

        private void SliderSetting(ref float setting, string text, int min, int max, ref bool update)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(text, GUILayout.Width(windowWidth * 0.25f));
            GUILayout.Space(10);

            float settingLast = setting;
            setting = GUILayout.HorizontalSlider((float)Math.Round(setting, 2), min, max);

            GUILayout.Label(setting.ToString(), centeredText, GUILayout.Width(windowWidth * 0.13f));

            if (setting != settingLast)
                update = true;

            GUILayout.EndHorizontal();
        }

        private void EnumSetting(ref EffectsOption effectsOption, string text)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(text, GUILayout.Width(windowWidth * 0.25f));
            GUILayout.Space(10);

            string[] options = Enum.GetNames(typeof(EffectsOption));
            int index = (int)effectsOption;

            index = GUILayout.SelectionGrid(index, options, options.Length);
            effectsOption = (EffectsOption)index;

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
            UpdateMasterLists();
            UpdateWeaponsTab();
            guiEnabled = true;
            clickBlocker.gameObject.SetActive(true);
        }

        public void DisableGui() 
        {
            InputLockManager.RemoveControlLock("KCSGUI");
            guiEnabled = false;
            GlobalSettings.Save();
            clickBlocker.gameObject.SetActive(false);
        }

        private void OnShowUI() =>
            OnToggleUI(false);

        private void OnHideUI() =>
            OnToggleUI(true);

        private void OnToggleUI(bool hide)
        {
            guiHidden = hide;
        }

        private void CreateClickBlocker()
        {
            var canvas = UIMasterController.Instance.mainCanvas;
            var blocker = new GameObject(nameof(FlightManager) + "ClickBlocker");
            blocker.transform.SetParent(canvas.transform);
            clickBlocker = blocker.AddComponent<RectTransform>();
            clickBlocker.pivot = new Vector2(0f, 1f);
            blocker.AddComponent<CanvasRenderer>();
            blocker.AddComponent<UnityEngine.UI.Text>();

            clickBlocker.gameObject.SetActive(false);
        }

        #endregion
    }
}