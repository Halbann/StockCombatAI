using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class KCSController : MonoBehaviour
    {
        // GUI variables.
        private static ApplicationLauncherButton appLauncherButton;
        private static bool addedAppLauncherButton = false;
        private bool guiEnabled = false;
        private int windowWidth = 550;
        private int windowHeight = 700;
        private Rect windowRect;
        private GUIStyle boxStyle;

        List<ModuleShipController> shipControllerList;
        private int scrollViewHeight;
        private Vector2 scrollPosition;

        private void Start()
        {
            // Setup GUI. 

            windowRect = new Rect((Screen.width / 2) - (windowWidth / 2), (Screen.height / 2) - (windowHeight / 2), 0, 0);
            guiEnabled = false;

            AddToolbarButton();

            // Register vessel updates.

            UpdateList();
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
                UpdateList();
        }

        private void UpdateList()
        {
            var loadedVessels = FlightGlobals.VesselsLoaded;
            shipControllerList = new List<ModuleShipController>();

            foreach (Vessel v in loadedVessels)
            {
                var p = v.FindPartModuleImplementing<ModuleShipController>();
                if (p != null)
                    shipControllerList.Add(p);
            }
        }

        public void ToggleAIs()
        {
            foreach (var controller in shipControllerList)
            {
                controller.ToggleAI();
            }
        }

        // GUI functions.

        void OnGUI()
        {
            if (guiEnabled)
            {
                DrawGUI();
            }
        }
        private void DrawGUI() =>
            windowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, FillWindow, "Kerbal Combat Suite", GUILayout.Height(0), GUILayout.Width(windowWidth));

        private void FillWindow(int windowID)
        {
            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), ""))
                ToggleGui();

            boxStyle = GUI.skin.GetStyle("Box");
            GUILayout.BeginVertical();

            GUILayout.Label("This is a GUI for KCS.");

            GUIList();

            if (GUILayout.Button("Update List")) UpdateList();
            if (GUILayout.Button("Enable/Disable AIs")) ToggleAIs();
                

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 500));
        }

        private void GUIList()
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
                                FlightGlobals.ForceSetActiveVessel(v);
                            }
                        }
                    }
                GUILayout.EndScrollView();
            GUILayout.EndVertical();
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
