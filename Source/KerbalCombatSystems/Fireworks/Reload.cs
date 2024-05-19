using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

using KerbalCombatSystems.Data;

namespace KerbalCombatSystems.Fireworks
{
    public class ModuleLauncherReload : PartModule
    {
        // Settings.
        public static int searchLimit = 50;
        public static bool useCrossfeed = true;

        [KSPField]
        public string defaultMagazine = "KCSfireworkShells8";

        [KSPField(isPersistant = true)]
        private bool appliedDefaultShellVelocity = false;


        // Variables.
        private FieldInfo gridField;
        private ModulePartFirework launcher;
        private ModuleInventoryPart inventory;
        private bool uiOpened = false;

        public delegate void OnShotCountChangedHandler(ModulePartFirework launcher);
        public event OnShotCountChangedHandler OnShotCountChanged;

        // debug
        public static List<Part> searchedParts = new List<Part>();
        private static PartSet crossfeedParts;

        #region Main

        public override void OnStartFinished(StartState state)
        {
            launcher = part.FindModuleImplementing<ModulePartFirework>();
            inventory = part.FindModuleImplementing<ModuleInventoryPart>();

            if (launcher == null || inventory == null)
            {
                Debug.LogError($"{part.partInfo.name} is missing a ModulePartFirework or ModuleInventoryPart.");
                return;
            }

            // Make sure the inventory is visible (hidden by community fixes).
            inventory.Fields["InventorySlots"].group.startCollapsed = false;
       
            // Register events.
            GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);
            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);
            GameEvents.onModuleInventoryChanged.Add(OnModuleInventoryChanged);

            // Get field for updating shot count in inventory UI.
            gridField = typeof(ModuleInventoryPart).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic);

            // todo: this belongs on a different module.
            IncreaseDefaultLaunchVelocity();

            UpdateInventoryFromShots();
            UpdateUI();
        }

        internal void OnDestroy()
        {
            GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);
            GameEvents.onPartActionUICreate.Remove(OnPartActionUICreate);
            GameEvents.onModuleInventoryChanged.Remove(OnModuleInventoryChanged);
        }

        #endregion

        #region Actions/Buttons

        [KSPAction("Reload")]
        public void ReloadAction(KSPActionParam _) =>
            Reload();

        [KSPEvent(guiName = "Reload", guiActive = true)]
        public void ReloadEvent() =>
            Reload();

        public bool Reload()
        {
            // Pull a magazine from the nearest inventory and move it into the launcher inventory.

            if (inventory == null)
                return false;

            StoredPart magazine = GetLoadedMagazine();

            // If the existing magazine is empty, delete it.
            // If we already have a usable magazine, return.
            if (magazine != null)
            {
                if (GetShellResource(magazine).amount <= 0)
                    RemoveMagazine(inventory, magazine);
                else
                    return false;
            }

            // Search the part tree for a usable magazine.
            ModuleInventoryPart cargoInventory;
            (cargoInventory, magazine) = LocateMagazine();

            // No magazine was found.
            if (magazine == null)
                return false;

            TakeShells(cargoInventory, magazine);

            return true;
        }

        #endregion

        #region Functions

        public StoredPart GetLoadedMagazine() => GetMagazine(inventory);


        // Search the vessel for a usable shell magazine.
        // todo: this could be heavily optimised if necessary.
        private (ModuleInventoryPart, StoredPart) LocateMagazine()
        {
            ModuleInventoryPart cargoInventory = null;
            StoredPart magazine = null;
            Part currentPart = part;
            Part previousPart = null;
            searchedParts.Clear();

            int i = 0;
            crossfeedParts = part.crossfeedPartSet;

            // Climb the part tree, checking all branches we see for an inventory.
            while (i < searchLimit)
            {
                if (currentPart == null)
                    break;

                // Go down the available branches.
                if (LocateMagazineInBranch(currentPart, ref cargoInventory, ref magazine, ref i, previousPart))
                    break;

                // No luck? Go up the tree.
                previousPart = currentPart;
                currentPart = currentPart.parent;
            }

            return (cargoInventory, magazine);
        }


        // Search a part and all its children (recursively) for a magazine. This is the downwards part of the search.
        private bool LocateMagazineInBranch(Part part, ref ModuleInventoryPart cargoInventory, ref StoredPart magazine, ref int iterations, Part previousPart = null)
        {
            searchedParts.Add(part);

            if (useCrossfeed && !crossfeedParts.ContainsPart(part))
                return false;

            cargoInventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (cargoInventory != null)
            {
                if (part.FindModuleImplementing<ModulePartFirework>() == null)
                {
                    magazine = GetMagazine(cargoInventory);
                    if (magazine != null)
                    {
                        return true; // Success.
                    }
                }
            }

            // Failure. Continue the search.

            iterations++;

            if (iterations > searchLimit)
                return false;

            // Begin recursion on all branches apart from the one we just came from.

            var children = part.children.ToList();
            children.Remove(previousPart);

            foreach (Part child in children)
            {
                if (iterations > searchLimit)
                    return false;

                if (LocateMagazineInBranch(child, ref cargoInventory, ref magazine, ref iterations))
                    return true;
            }

            return false;
        }


        // Move shells (magazine or individual) to our own inventory.
        public void TakeShells(ModuleInventoryPart cargoInventory, StoredPart magazine)
        {
            float magVolume = magazine.snapshot.partPrefab.FindModuleImplementing<ModuleCargoPart>().packedVolume;

            if (inventory.volumeCapacity < magVolume)
            {
                // The magazine is too large to fit in the internal inventory, move the shells.

                if (!inventory.StoreCargoPartAtSlot(defaultMagazine, 0))
                {
                    Debug.LogError($"{launcher.part.partInfo.name} is missing a default magazine.");
                }

                // Add to our own shell resource.
                var shells = GetShellResource(GetLoadedMagazine());
                shells.amount = Mathf.Clamp((float)magazine.snapshot.resources[0].amount, 0, (float)shells.amount);

                // Remove from the cargo shell resource.
                var magShells = magazine.snapshot.resources[0];
                magShells.amount -= shells.amount;

                if (magShells.amount <= 0)
                    RemoveMagazine(cargoInventory, magazine);
            }
            else
            {
                // Move the whole magazine into the launcher inventory.

                ProtoPartSnapshot magData = new ProtoPartSnapshot(magazine.snapshot.partPrefab, vessel.protoVessel);

                magData.resources[0].amount = magazine.snapshot.resources[0].amount;
                magData.mass = magazine.snapshot.mass;
                magData.moduleMass = magazine.snapshot.moduleMass;

                inventory.StoreCargoPartAtSlot(magData, 0);

                RemoveMagazine(cargoInventory, magazine);
            }
        }

       
        // The cannon was just fired, remove a shell from the inventory and make sure the internal count is correct.
        public void RemoveShell()
        {
            if (inventory == null)
                return;

            var magazine = GetLoadedMagazine();
            var shells = GetShellResource(magazine);

            if (shells == null)
                return;

            shells.amount -= 1;

            launcher.fireworkShots = (int)shells.amount;
            launcher.fireworkShotsDisplay = shells.amount.ToString("0");

            UpdateShotsFromInventory(shells);

            if (shells.amount <= 0)
                RemoveMagazine(inventory, magazine);
        }


        #endregion

        #region Stock Interaction


        // todo: this belongs on a different module.
        private void IncreaseDefaultLaunchVelocity()
        {
            // Set the shell velocity to 100 if it's 50 and this is the first time we're seeing it.
            if (!appliedDefaultShellVelocity && launcher.shellVelocity == 50)
                launcher.shellVelocity = 100;

            appliedDefaultShellVelocity = true;
        }


        // Runs after firing or when the inventory state changes. Syncs the stock shot count with the inventory.
        private void UpdateShotsFromInventory(ProtoPartResourceSnapshot shells = null)
        {
            shells ??= GetShellResource(GetLoadedMagazine());

            int amount = 0;
            if (shells != null)
                amount = (int)shells.amount;

            if (launcher == null || launcher.fireworkShots == amount)
                return;

            launcher.fireworkShots = amount;
            launcher.fireworkShotsDisplay = amount.ToString("0");
        }


        // Runs once on start. Sync loaded shells with stock shot count.
        private void UpdateInventoryFromShots()
        {
            inventory.ClearPartAtSlot(0);

            if (!inventory.StoreCargoPartAtSlot(defaultMagazine, 0))
            {
                Debug.LogError($"{launcher.part.partInfo.name} is missing a default magazine.");
            }

            var shells = GetShellResource(GetLoadedMagazine());
            if (shells == null)
                return;

            shells.amount = launcher.fireworkShots;
        }


        #endregion

        #region Helpers

        // Delete the specified magazine from the specified inventory part.
        private static void RemoveMagazine(ModuleInventoryPart inventory, StoredPart magazine)
        {
            int keyIndex = inventory.storedParts.IndexOf(magazine);
            int key = inventory.storedParts.Keys.ElementAt(keyIndex);

            inventory.ClearPartAtSlot(key);
        }


        // Try to retreive a magazine part from a specified inventory.
        public static StoredPart GetMagazine(ModuleInventoryPart inventory)
        {
            if (inventory == null)
                return null;

            var storedParts = inventory.storedParts.Values;
            StoredPart magazine = null;

            foreach (var part in storedParts)
            {
                if (part.partName.Contains("KCSfireworkShells"))
                {
                    magazine = part;
                    break;
                }
            }

            return magazine;
        }


        public static ProtoPartResourceSnapshot GetShellResource(StoredPart magazine)
        {
            if (magazine == null)
                return null;

            return magazine.snapshot.resources.Find(r => r.resourceName == "FireworkShells");
        }


        #endregion

        #region UI

        // Runs every frame that the part window is open. Hides the stock slider in the editor,
        // updates text in the inventory UI, and collapses the effects groups.
        private void UpdateUI()
        {
            // Continuously hide the stock shot slider in the editor.

            if (launcher != null)
                launcher.Fields["fireworkShots"].guiActiveEditor = false;

            // Continuously update the inventory UI with the current number of shells.

            if (inventory == null)
                return;

            UI_Grid grid = (UI_Grid)gridField.GetValue(inventory);

            if (grid != null && grid.pawInventory != null)
            {
                grid.pawInventory.Window.displayDirty = false;

                grid.pawInventory.packedVolumeText.text = $"{launcher.fireworkShots}/{launcher.maxShots} Shells";
                grid.pawInventory.packedVolumeSlider.value = launcher.fireworkShots;
                grid.pawInventory.packedVolumeSlider.maxValue = launcher.maxShots;
            }

            // Collapse the effects groups the first time the part window is opened.

            if (!uiOpened && part.PartActionWindow != null)
            {
                uiOpened = true;

                part.PartActionWindow.parameterGroups.TryGetValue("trail", out UIPartActionGroup trailGroup);
                trailGroup.Collapse();

                part.PartActionWindow.parameterGroups.TryGetValue("burst", out UIPartActionGroup burstGroup);
                burstGroup.Collapse();
            }
        }

        private void OnPartActionUIShown(UIPartActionWindow data0, Part data1)
        {
            if (data1 != part)
                return;

            UpdateUI();
        }

        private void OnPartActionUICreate(Part data)
        {
            if (data != part)
                return;

            UpdateUI();
        }

        #endregion

        #region Events

        public void OnLauncherFired()
        {
            if (!CheatOptions.InfinitePropellant)
                RemoveShell();

            UpdateUI();
        }

        private void OnModuleInventoryChanged(ModuleInventoryPart data)
        {
            if (data != inventory)
                return;

            UpdateShotsFromInventory();
            StartCoroutine(Delay(new WaitForEndOfFrame(), UpdateUI));

            OnShotCountChanged?.Invoke(launcher);
        }

        private IEnumerator Delay(YieldInstruction delay, Action action)
        {
            yield return delay;
            action();
        }

        #endregion
    }
}
