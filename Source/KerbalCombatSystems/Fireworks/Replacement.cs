using System;
using System.Reflection;

using UnityEngine;
using KSP.FX.Fireworks;
using Object = UnityEngine.Object;

namespace KerbalCombatSystems.Fireworks
{
    class Replacement
    {
        // Recreate a stock shell that would otherwise be created by LaunchShell.

        static bool fieldsAcquired = false;
        static MethodInfo configureSoundFX;
        static MethodInfo matchColorPickers;
        static MethodInfo getCurrentPSByType;
        static MethodInfo getCurrentFXByType;
        static FieldInfo fireworkColors;
        static FieldInfo variationOnShellDirMultiplier;

        static void AcquireFields()
        {
            fieldsAcquired = true;
            Type type = typeof(ModulePartFirework);

            configureSoundFX = GetMethodInfo(type, "configureSoundFX");
            matchColorPickers = GetMethodInfo(type, "matchColorPickers");
            getCurrentPSByType = GetMethodInfo(type, "getCurrentPSByType");
            getCurrentFXByType = GetMethodInfo(type, "getCurrentFXByType");

            fireworkColors = GetFieldInfo(type, "fireworkColors");
            variationOnShellDirMultiplier = GetFieldInfo(type, "variationOnShellDirMultiplier");
        }

        internal static void ReplaceStockShell(ModulePartFirework l)
        {
            // Replicate the process of creating a shell in LaunchShell.
            // Might be less prone to error. Keep as a fallback.

            if (l.shellPrefab == null)
                return;

            if (!CheatOptions.InfinitePropellant)
            {
                if (l.fireworkShots < 1)
                    return;

                l.fireworkShots -= 1f;
                l.fireworkShotsDisplay = l.fireworkShots.ToString("F0");
            }

            Transform transform = l.gameObject.GetChild(l.cannonName)?.transform;
            if (transform == null)
                return;

            if (!fieldsAcquired)
                AcquireFields();

            // Instantiate shell.
            GameObject shell = Object.Instantiate(l.shellPrefab, transform.position, transform.rotation);

            bool inAtmosphere = LaunchShell.InAtmosphere;

            // Convert to physical object.
            physicalObject obj = physicalObject.ConvertToPhysicalObject(l.part, shell);
            obj.maxDistance = 10000f;
            obj.origDrag = inAtmosphere ? l.shellDrag : 0f;

            // Setup rigidbody.
            Rigidbody rb = obj.rb;
            float massScale = l.shellRBMassScaleValue; // l.shellRBMassScaleValue is 0.05 but should be 0.001.
            rb.mass = l.shellMass * massScale;
            rb.maxAngularVelocity = PhysicsGlobals.MaxAngularVelocity;
            rb.drag = inAtmosphere ? l.shellDrag : 0f;
            rb.useGravity = false;
            rb.velocity = l.vessel.rb_velocity;

            // Launch.
            Vector3 shellForceDir = transform.up;
            if (l.variationOnShellDirection)
                shellForceDir = (shellForceDir + UnityEngine.Random.onUnitSphere * l.GetField<float>(variationOnShellDirMultiplier)).normalized;

            Vector3 force = shellForceDir * rb.mass * (l.shellVelocity / Time.fixedDeltaTime);
            rb.AddForce(force, ForceMode.Force); // Launch force.
            l.part.AddForce(-1f * force * 0.001f / massScale); // Recoil (fudged).

            // Stock appearance.
            Renderer componentInChildren = shell.GetComponentInChildren<Renderer>();
            if (componentInChildren != null && componentInChildren.material.mainTexture == null)
            {
                componentInChildren.material.SetTexture("_MainTex", l.part.GetPartRenderers()[0].material.mainTexture);
            }

            l.fxController = shell.GetComponent<FireworkFX>();
            if (inAtmosphere)
                configureSoundFX.Invoke(l, null);

            matchColorPickers.Invoke(l, null);
            var colours = l.GetField<Color[]>(fireworkColors);

            l.fxController.Setup(
                l.shellDuration,
                GetPrefab(l, FireworkEffectType.TRAIL),
                GetPrefab(l, FireworkEffectType.BURST),
                colours[0],
                colours[1],
                colours[3],
                colours[2],
                colours[4],
                l.shellVelocity,
                l.burstSpread,
                l.burstDuration,
                l.burstFlareSize,
                GetFX(l, FireworkEffectType.BURST).crackleSFX,
                GetFX(l, FireworkEffectType.BURST).randomizeBurstOrientation,
                GetFX(l, FireworkEffectType.TRAIL).minTrailLifetime,
                GetFX(l, FireworkEffectType.TRAIL).maxTrailLifetime);
        }

        static GameObject GetPrefab(ModulePartFirework launcher, FireworkEffectType type)
        {
            return getCurrentPSByType.Invoke(launcher, new object[] { type }) as GameObject;
        }

        static FireworkFXDefinition GetFX(ModulePartFirework launcher, FireworkEffectType type)
        {
            return getCurrentFXByType.Invoke(launcher, new object[] { type }) as FireworkFXDefinition;
        }

        internal static MethodInfo GetMethodInfo(Type type, string methodName)
        {
            // Invoke a private method using reflection,

            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            return method;
        }

        internal static FieldInfo GetFieldInfo(Type type, string fieldName)
        {
            // Get a private field using reflection.

            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field;
        }
    }

    static class Extensions
    {
        public static T GetField<T>(this object instance, FieldInfo field)
        {
            return (T)field.GetValue(instance);
        }
    }
}
