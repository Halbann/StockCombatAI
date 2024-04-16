using Steamworks;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Assets : MonoBehaviour
    {
        private static bool loaded = false;

        private static readonly string[] bundleNames = new string[]
        {
            "shaders",
            "ui",
            "effects"
        };

        private static Dictionary<string, Object> assets = new Dictionary<string, Object>();

        internal void Awake()
        {
            if (loaded)
                return;

            string path = Path.Combine(
                KSPUtil.ApplicationRootPath, "GameData", "KCS", "AssetBundles");

            // Load all KCS asset bundles.

            foreach (string bundleName in bundleNames)
            {
                LoadAssetBundle(Path.Combine(path, bundleName));
            }

            loaded = true;
        }

        private void LoadAssetBundle(string path)
        {
            // Check if the asset bundle exists

            if (!File.Exists(path))
            {
                Debug.LogError("Missing asset bundle at " + path);
                return;
            }

            // Try to load.

            AssetBundle bundle = AssetBundle.LoadFromFile(path);

            if (bundle == null)
            {
                Debug.LogError("Failed to load asset bundle at " + path);
                return;
            }

            // Load all assets in the bundle and add them to the dictionary.

            Object[] bundleAssets = bundle.LoadAllAssets();

            foreach (Object asset in bundleAssets)
            {
                assets.Add(asset.name, asset);
            }

            // Unload the bundle.

            bundle.Unload(false);
        }

        public static bool TryGetAsset<T>(string name, out T asset) where T : Object
        {
            if (assets.TryGetValue(name, out Object obj) && obj is T instance)
            {
                asset = instance;
                return true;
            }
            else
            {
                asset = default;
                return false;
            }
        }

        public static T GetAsset<T>(string name) where T : Object
        {
            if (assets.TryGetValue(name, out Object obj) && obj is T instance)
            {
                return instance;
            }
            else
            {
                throw new KeyNotFoundException($"Asset {name} of type {typeof(T).Name} not found.");
            }
        }
    }
}