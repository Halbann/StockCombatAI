using KSP.UI.Screens;
using System;
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

        private int windowWidth = 300;
        private int windowHeight = 700;
        private Rect windowRect;
        private GUIStyle boxStyle;
        private int scrollViewHeight;
        private Vector2 scrollPosition;
        GUIStyle buttonStyle;

        private string[] modes = { "Weapons", "Ships" }; 
        private string mode = "Weapons";

        List<ModuleShipController> shipControllerList;
        List<ModuleMissileGuidance> weaponList;
        ModuleMissileGuidance selectedWeapon;
        public List<KCSShip> ships;

        private void Start()
        {
            // Setup GUI. 

            windowRect = new Rect((Screen.width / 2) - (windowWidth / 2), (Screen.height / 2) - (windowHeight / 2), 0, 0);
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
            if (guiEnabled)
                UpdateShipList();
        }

        private void UpdateShipList()
        {
            var loadedVessels = FlightGlobals.VesselsLoaded;
            shipControllerList = new List<ModuleShipController>();
            ships = new List<KCSShip>();

            foreach (Vessel v in loadedVessels)
            {
                var p = v.FindPartModuleImplementing<ModuleShipController>();
                if (p == null) continue;

                shipControllerList.Add(p);
                ships.Add(new KCSShip(v, v.GetTotalMass())); // todo: Should update mass instead
            }
        }
        
        private void UpdateWeaponList()
        {
            var c = FlightGlobals.ActiveVessel.FindPartModuleImplementing<ModuleShipController>();
            if (c == null) { 
                weaponList = new List<ModuleMissileGuidance>();
                return;
            };

            c.CheckWeapons();
            weaponList = c.missiles;

            // need to sort by distance from root part so that we don't fire bumper torpedos.
            //weaponList.OrderBy(m => m.vessel.parts.FindIndex 

            // todo: replace this.
            var ungroupedMissiles = weaponList.FindAll(m => m.missileCode == "");
            weaponList = weaponList.Except(ungroupedMissiles).ToList();
            weaponList = weaponList.GroupBy(m => m.missileCode).Select(g => g.First()).ToList();
            weaponList = weaponList.Concat(ungroupedMissiles).ToList();
        }

        public void ToggleAIs()
        {
            foreach (var controller in shipControllerList)
            {
                controller.ToggleAI();
            }
        }

        public void FireSelectedWeapon()
        {
            if (selectedWeapon == null) return;
            selectedWeapon.FireMissile();
            UpdateWeaponList();
        }




        // GUI functions.

        void OnGUI()
        {
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
                scrollViewHeight = Mathf.Max(Mathf.Min(15 * 30, 30 * shipControllerList.Count), 5 * 30);
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(scrollViewHeight), GUILayout.Width(windowWidth));
                    if (shipControllerList.Count > 0)
                    {
                        Vessel v;

                        foreach (var controller in shipControllerList)
                        {
                            v = controller.vessel;
                            string craftName = String.Format("{0}\n<color=#808080ff>Part Count: {1}, Mass: {2} t</color>", 
                                v.GetDisplayName(), v.parts.Count, Math.Round(v.GetTotalMass(), 1));

                            if (GUILayout.Button(craftName))
                            {
                                FlightGlobals.SetActiveVessel(v);
                                UpdateWeaponList();
                            }
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
                            string missileCode = w.missileCode == "" ? "Missile" : w.missileCode;
                            string weaponName = String.Format("{0}\n<color=#808080ff>Part Count: {1}, Mass: {2} t</color>", 
                                missileCode, "Unknown", "Unknown");

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
                EnableGui();
            }
        }
        public void EnableGui() { guiEnabled = true; }
        public void DisableGui() { guiEnabled = false; }
    }
}
