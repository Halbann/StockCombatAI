using UnityEngine;
using Random = UnityEngine.Random;
using KSP.FX.Fireworks;

using static KerbalCombatSystems.Utils;
using KerbalCombatSystems.Effects;
using KerbalCombatSystems.Data;

namespace KerbalCombatSystems.Fireworks
{
    public class ShellEffects : MonoBehaviour
    {
        // General settings.
        public static bool showFlare = true;
        public static bool hideShell = true;
        public static LightRenderMode lightMode = LightRenderMode.Auto;
        public static float shellLifetime = 30f; // from fixer.

        // Tracer settings.
        public static float tracerToFlareTransition = 0.005f;
        public static float tracerWidth = 0.3f;
        public static float tracerOffset = 0f;
        public static float shutterAngle = 180;
        public static float framerate = 24;

        // Flare settings.
        public static float flareLightRange = 20f;
        public static float flareLightIntensity = 1.5f;
        public static float flareSize = 0.15f;
        public static float flareShimmer = 0.05f;

        // State.
        private Color colour;
        private bool setupComplete = false;
        private float launchTime; // from fixer.

        // Flare variables.
        private static Material flareMaterial;
        private Transform pivot;
        private Camera cam;
        private GameObject flare;
        private MaterialPropertyBlock flareMpb;
        private float seed;
        private float flareVisibility = 0;

        // Other variables.
        private static Material shellMat;
        private Tracer tracer;


        #region Main

        internal static ShellEffects AddEffects(GameObject shell, ModulePartFirework launcher)
        {
            ShellEffects effects = shell.AddComponent<ShellEffects>();

            Color colour = launcher.GetCurrentColor("primaryTrailColorChanger");
            effects.colour = Desaturate(colour, 0.66f);
            effects.Init();

            return effects;
        }

        internal static void RemoveStockEffects(GameObject shell)
        {
            // Delete the FX controller.
            Destroy(shell.GetComponent<FireworkFX>());

            // Delete the gameobject that holds the particle system.
            Destroy(shell.transform.GetChild(1).gameObject);
        }

        private void Init()
        {
            launchTime = Time.fixedTime;
            setupComplete = true;

            if (showFlare)
                CreateFlare();

            CreateLight();

            CreateTracer();

            ModifyShell();

            // Prevent the shell from casting shadows.
            GetComponentInChildren<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal void LateUpdate()
        {
            if (!setupComplete)
                return;

            UpdateFlare();
        }

        #endregion

        #region Creation

        private void ModifyShell()
        {
            if (!hideShell)
            {
                if (shellMat == null)
                {
                    shellMat = new Material(Shader.Find("Particles/Standard Unlit"));
                    shellMat.color = Color.white;
                }

                GetComponentInChildren<MeshRenderer>().motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            GameObject shell = gameObject.GetChild("fireworksShell");

            if (shell == null)
                return;

            var meshes = shell.GetComponentsInChildren<MeshRenderer>();
            foreach (var mesh in meshes)
            {
                if (hideShell)
                    mesh.enabled = false;
                else
                {
                    mesh.material = shellMat;
                }
            }
        }

        private void CreateFlare()
        {
            if (flareMaterial == null)
            {
                flareMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                flareMaterial.mainTexture = GameDatabase.Instance.GetTexture($"{Meta.name}/Icons/flare", false);
            }

            // Create a pivot for the flare to rotate on.
            pivot = new GameObject("FlarePivot").transform;
            pivot.transform.SetParent(transform);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.forward = Random.insideUnitSphere.normalized;

            // Create the flare.
            flare = GameObject.CreatePrimitive(PrimitiveType.Plane);
            flare.transform.SetParent(pivot);

            if (!hideShell)
                flare.transform.localPosition = Vector3.up * 0.25f;

            flare.transform.localPosition = Vector3.zero;
            flare.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);

            var flareRenderer = flare.GetComponent<MeshRenderer>();
            flareRenderer.material = flareMaterial;

            // The flare should be hidden for the first frame.
            flareMpb = new MaterialPropertyBlock();
            flareMpb.SetColor("_TintColor", new Color(0, 0, 0, 0));
            flareRenderer.SetPropertyBlock(flareMpb);

            // Remove the collider.
            DestroyImmediate(flare.GetComponent<MeshCollider>());

            // Set a seed for the random shimmer.
            seed = Random.Range(0f, 1000f);
        }

        private void CreateLight()
        {
            var light = new GameObject("ShellLight").AddComponent<Light>();
            light.transform.SetParent(transform);
            light.transform.localPosition = Vector3.zero;

            light.range = flareLightRange;
            light.intensity = flareLightIntensity;
            light.color = colour;
            light.shadows = LightShadows.Hard;
            light.renderMode = lightMode;

            // The light should only affect local space.
            light.cullingMask = 1 << 0;
        }

        private void CreateTracer()
        {
            tracer = gameObject.AddComponent<Tracer>();
            tracer.Material = AssetBundles.Get<Material>("BulletTrailMat");
            tracer.Colour = colour;
            tracer.Width = tracerWidth;
            tracer.offset = tracerOffset; // 0.05f

            tracer.exposureTime = 1f / (framerate / (shutterAngle / 360f));

            tracer.rb = GetComponent<Rigidbody>();
        }

        #endregion

        #region Update

        private void UpdateFlare()
        {
            if (!showFlare || pivot == null)
                return;

            if ((cam = FlightCamera.fetch.mainCamera) == null)
                return;

            // Point the flare towards the camera.
            pivot.up = Vector3.Normalize(cam.transform.position - pivot.transform.position);

            // Random shimmer.
            float randomScale = flareSize + flareShimmer * (Mathf.PerlinNoise(seed + Time.time * 10f, 0f) * 2 - 1);
            flare.transform.localScale = new Vector3(randomScale, 1f, randomScale);

            // Convert start and end positions to screen space.
            Vector2 screenStart = cam.WorldToScreenPoint(tracer.startPosition);
            Vector2 screenEnd = cam.WorldToScreenPoint(tracer.endPosition);

            screenStart.x /= cam.pixelWidth;
            screenStart.y /= cam.pixelHeight;

            screenEnd.x /= cam.pixelWidth;
            screenEnd.y /= cam.pixelHeight;

            float tracerScreenLength = Vector2.Distance(screenStart, screenEnd);
            flareVisibility = 1 - Mathf.Clamp01(tracerScreenLength / tracerToFlareTransition);
            flareVisibility *= 1 - Mathf.Clamp01((Time.time - launchTime) / shellLifetime);

            // Set the flare's visibility and overall size based on alignment of the tracer with the screen and lifetime.
            // The purpose of the flare is to give the tracer dimension when far away or viewed from the front.
            flareMpb.SetColor("_TintColor", colour * flareVisibility);
            flare.GetComponent<MeshRenderer>().SetPropertyBlock(flareMpb);
            flare.transform.localScale *= flareVisibility;
        }

        #endregion
    }
}