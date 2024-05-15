using UnityEngine;

namespace KerbalCombatSystems.Fireworks
{
    public class ModuleStockLauncherAnimation : PartModule
    {
        // Animate the barrels section of the stock launchers.

        public Transform barrel;
        private ModulePartFirework launcher;
        private float lastFired = 0;
        private bool animating = false;

        [KSPField]
        public float spinTime = 0.1f;

        [KSPField]
        public float spinDegrees = -36f;

        internal void Start()
        {
            launcher = GetComponent<ModulePartFirework>();

            if (launcher == null || !HighLogic.LoadedSceneIsFlight)
            {
                enabled = false;
                return;
            }

            barrel = part.gameObject.GetChild("ridges").transform;
        }

        internal void Update()
        {
            if (!animating)
                return;

            float t = Mathf.Clamp(Time.time - lastFired, 0, spinTime);
            if (t >= spinTime)
                animating = false;

            t /= spinTime;
            t = Mathf.SmoothStep(0f, 1f, t);

            barrel.localRotation = Quaternion.Euler(0f, t * spinDegrees, 0f);
        }

        public void Spin()
        {
            lastFired = Time.time;
            animating = true;
        }
    }
}
