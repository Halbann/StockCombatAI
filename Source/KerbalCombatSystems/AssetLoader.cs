using System.IO;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KCSAssets : MonoBehaviour
    {
        private static bool loaded;
        private static string path;

        // Assets

        public static Shader LineShader;
        public static Shader LinePixelShader;
        public static Shader TestShader;
        public static GameObject markerPrefab;
        public static GameObject canvasPrefab;

        public string ShadersPath
        {
            get
            {
                // todo: shaders for mac and linux.
                switch (Application.platform)
                {
                    //case RuntimePlatform.OSXPlayer:
                    //    return _bundlePath + Path.DirectorySeparatorChar +
                    //           "kcsshaders_macosx";
                    //case RuntimePlatform.WindowsPlayer:
                    //    return _bundlePath + Path.DirectorySeparatorChar +
                    //           "kcsshaders_windows";
                    //case RuntimePlatform.LinuxPlayer:
                    //    return _bundlePath + Path.DirectorySeparatorChar +
                    //    "kcsshaders_linux";
                    default:
                        return path + Path.DirectorySeparatorChar +
                               "kcsshaders";
                }
            }
        }

        public string UIPath => path + Path.DirectorySeparatorChar + "kcsui";

        private void Awake()
        {
            if (loaded) return;

            path = KSPUtil.ApplicationRootPath + "GameData" + 
                Path.DirectorySeparatorChar + "KCS" + 
                Path.DirectorySeparatorChar + "AssetBundles";

            LoadShaderAssets();
            LoadUIAssets();
            loaded = true;
        }

        private void LoadUIAssets()
        {
            AssetBundle UIbundle = AssetBundle.LoadFromFile(UIPath);

            if (UIbundle == null)
            {
                Debug.Log("[KCS] Error: Missing UI asset bundle.");
            } 

            markerPrefab = UIbundle.LoadAsset<GameObject>("Assets/Prefabs/Marker.prefab");
            canvasPrefab = UIbundle.LoadAsset<GameObject>("Assets/Prefabs/KCSCanvas.prefab");
            UIbundle.Unload(false);
        }

        private void LoadShaderAssets()
        {
            AssetBundle shaderBundle = AssetBundle.LoadFromFile(ShadersPath);

            if (shaderBundle == null)
            {
                Debug.Log("[KCS] Error: Missing shaders asset bundle.");
                return;
            }

            Shader[] shaders = shaderBundle.LoadAllAssets<Shader>();
            foreach (Shader shader in shaders)
            {
                if (shader == null) continue;

                switch (shader.name)
                {
                    case "GoodLines/Line":
                        LineShader = shader;
                        break;

                    case "GoodLines/PixelPerfect":
                        LinePixelShader = shader;
                        break;

                    case "Unlit/WorldSpaceNormals":
                        TestShader = shader;
                        break;
                }
            }

            shaderBundle.Unload(false);
        }
    }
}