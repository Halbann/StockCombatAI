using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    // Overall controller for KCS. Setup battles, 
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class KCSController : MonoBehaviour
    {
        // GUI variables.
        private static ApplicationLauncherButton appLauncherButton;
        private static bool addedAppLauncherButton = false;
        private static bool guiEnabled = false;

        private int windowWidth = 350;
        private int windowHeight = 700;
        private Rect windowRect;
        private GUIStyle boxStyle;
        private int scrollViewHeight;
        private Vector2 scrollPosition;
        GUIStyle buttonStyle;

        private string[] modes = { "Ships", "Weapons" };
        private string mode = "Ships";

        public List<ModuleShipController> ships;
        public List<ModuleWeaponController> weaponsInFlight;
        private float lastUpdateTime;

        List<ModuleWeaponController> weaponList;
        ModuleWeaponController selectedWeapon;
        //public List<KCSShip> ships;

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
        }

        void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(VesselEventUpdate);
            GameEvents.onVesselDestroy.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Remove(VesselEventUpdate);
        }

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
            //ships = new List<KCSShip>();

            foreach (Vessel v in loadedVessels)
            {
                var p = v.FindPartModuleImplementing<ModuleShipController>();
                if (p != null)
                {
                    ships.Add(p);   
                    continue;
                }

                var w = v.FindPartModuleImplementing<ModuleWeaponController>();
                if (w != null && w.launched && !w.missed)
                    weaponsInFlight.Add(w);

                //ships.Add(new KCSShip(v, v.GetTotalMass())); // todo: Should update mass instead
            }
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

            // need to sort by distance from root part so that we don't fire bumper torpedos.
            //weaponList.OrderBy(m => m.vessel.parts.FindIndex 

            // todo: replace this.
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




        // GUI functions.

        void OnGUI()
        {
            // todo: don't draw when UI is hidden
            if (guiEnabled) DrawGUI();
        }

        private void DrawGUI() =>
            windowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, FillWindow, "Kerbal Combat Suite", GUILayout.Height(0), GUILayout.Width(windowWidth));

        private void FillWindow(int windowID)
        {
            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), ""))
                ToggleGui();

            buttonStyle = GUI.skin.button;
            boxStyle = GUI.skin.GetStyle("Box");

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();

            foreach (var m in modes)
            {
                if (GUILayout.Toggle(mode == m, m, buttonStyle))
                {
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
                default:
                    GUILayout.Label("Something went wrong...");
                    break;
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 500));
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

                    string colour = "#FFFFFF";
                    var activeTarget = FlightGlobals.ActiveVessel.targetObject;

                    if (!c.alive)
                        colour = "#808080ff";
                    else if (v == FlightGlobals.ActiveVessel)
                        colour = "#00b5bf";
                    else if (activeTarget != null && v == activeTarget.GetVessel())
                        colour = "#b4ff33";

                    string targetName = c.target == null ? "None" : c.target.vesselName;
                    string craftName = String.Format("<color={6}>{0}</color>\n<color=#808080ff>Part Count: {1}, Mass: {2} t\nAlive: {3}, Target: {4}\nState: {5}</color>",
                        v.GetDisplayName(), v.parts.Count, Math.Round(v.GetTotalMass(), 1), c.alive, targetName, c.state, colour);

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

                    if (GUILayout.Button(String.Format("<color={1}>{0}</color>", c.side, c.side == Side.A ? "#0AACE3" : "#E30A0A")))
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
                    string weaponName = String.Format("{0}\n<color=#808080ff>Type: {1}, Mass: {2} t</color>",
                        code, w.weaponType, w.mass);

                    if (GUILayout.Toggle(w == selectedWeapon, weaponName, GUI.skin.button))
                    {
                        //selectedWeapon = selectedWeapon != w ? w : null;
                        selectedWeapon = w;
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (GUILayout.Button("Update List")) UpdateWeaponList();
            if (GUILayout.Button("Fire")) FireSelectedWeapon();
        }

        // Application launcher/Toolbar setup.

        private void AddToolbarButton()
        {
            if (!addedAppLauncherButton)
            {
                Texture buttonTexture = GameDatabase.Instance.GetTexture("KCS/Icons/button", false);
                appLauncherButton = ApplicationLauncher.Instance.AddModApplication(ToggleGui, ToggleGui, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, buttonTexture);
                addedAppLauncherButton = true;
            }

            if (appLauncherButton.isActiveAndEnabled)
            {
                appLauncherButton.SetFalse(false);
            }
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
    }
}