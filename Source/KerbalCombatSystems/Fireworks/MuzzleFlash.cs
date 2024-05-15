using System.Collections;

using UnityEngine;

using KerbalCombatSystems.Data;
using static KerbalCombatSystems.Utils;

namespace KerbalCombatSystems.Fireworks
{
    public class ModuleMuzzleFlash : PartModule
    {
        public static float muzzleFlashLifetime = 0.07f;
        public static Vector2 muzzleFlashStretch = new Vector2(0.95f, 1.2f);
        public static Vector2 muzzleFlashScale = new Vector2(0.5f, 2f);
        public static Vector2 muzzleFlashOpacity = new Vector2(0.1f, 1f);

        private ModulePartFirework launcher;
        private GameObject flash;
        private MaterialPropertyBlock mpb;
        private MeshRenderer renderer;
        private bool visible = false;

        public void Start()
        {
            launcher = GetComponent<ModulePartFirework>();

            if (launcher == null || !HighLogic.LoadedSceneIsFlight)
            {
                enabled = false;
                return;
            }

            CreateFlash();
        }

        private void CreateFlash()
        {
            // Create the muzzle flash mesh.
            var muzzle = gameObject.GetChild(launcher.cannonName);
            flash = Instantiate(AssetBundles.Get<GameObject>("Muzzle Flash"));
            flash.transform.SetParent(muzzle.transform, false);
            flash.transform.localPosition = new Vector3(0, 0.01f, 0);
            flash.SetActive(false);
            mpb = new MaterialPropertyBlock();
            renderer = flash.GetComponent<MeshRenderer>();
        }

        private void ShowFlash()
        {
            flash.transform.localRotation = Quaternion.Euler(0, Random.value * 360, 0);
            flash.transform.localScale = new Vector3(1, Range(muzzleFlashStretch), 1f) * Range(muzzleFlashScale);
            mpb.SetColor("_Color", new Color(1, 1, 0.4f, Range(muzzleFlashOpacity)));
            renderer.SetPropertyBlock(mpb);

            flash.SetActive(true);
        }

        private IEnumerator HideFlash()
        {
            yield return new WaitForSeconds(muzzleFlashLifetime);
            flash.SetActive(false);
            visible = false;
        }

        public void MuzzleEffect()
        {
            if (launcher == null || visible)
                return;

            visible = true;
            ShowFlash();
            StartCoroutine(HideFlash());
        }
    }
}
