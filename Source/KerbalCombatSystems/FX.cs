using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KCSFX : MonoBehaviour
    {
        public static FXMonger fxMonger;
        public static bool replaceExplosions = false;
        //public static string[] whitelist = new string[] { "metal bits", "fireball", "solid fuel chunks" };
        //public static string[] whitelist = new string[] { "metal bits", "psys_flash", "psys_sparks", "psys_flash_01", "psys_flash_02" };
        public static string[] whitelist = new string[] { "" };
        
        public static int lightCount = 6;
        public static float lightSpread = 5f;
        public static Color lightColor = new Color(252f / 255, 145f / 255, 50f / 255, 1);
        public static float lightIntensity = 0.4f;
        public static float lightRange = 50f;
        //public static float lightSpeed = 1.2f;
        public static float lightSpeed = 2f;
        public static AnimationCurve explosionBrightness;

        // Manage the total number of explosions in a given area for performance and visuals.
        private static List<Explosion> explosionQueue = new List<Explosion>();
        private static List<Explosion> explosions = new List<Explosion>();
        private static int explosionRateLimit = 4; // How many allowed within limitRadius per limitTime.
        private static float explosionLimitRadius = 1f;
        private static float explosionLimitTime = 0.6f;
        private static float explosionMergeMaxSpeedDiff = 100f;

        public struct Explosion
        {
            public Vector3 startVelocity;
            public Vector3 position;
            public float time;
        }

        void Start()
        {
            fxMonger = FindObjectOfType<FXMonger>();
            CreateExplosionBrightnessCurve();

            GameEvents.onPartWillDie.Add(OnPartWillDie);

            //KCSAssets.explosion.GetChild("Debris").GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Mesh;

            //if (!replaceExplosions)
            //    HideExplosions(true);
        }

        void LateUpdate()
        {
            // Don't consider explosions that are older than the limit time.
            if (explosions.Count > 0)
            {
                explosions.RemoveAll(e => Time.time - e.time > explosionLimitTime);
            }

            if (explosionQueue.Count > 0)
            {
                var existingNearbyExplosions = new List<Explosion>();
                float limitRadiusSqr = explosionLimitRadius * explosionLimitRadius;

                foreach (Explosion explosion in explosionQueue)
                {
                    // Skip this explosion if there have been more than explosionRateLimit explosions
                    // within explosionLimitRadius metres in the last explosionLimitTime seconds.

                    foreach (Explosion existingExplosion in explosions)
                    {
                        if (Vector3.SqrMagnitude(explosion.position - existingExplosion.position) < limitRadiusSqr)
                            existingNearbyExplosions.Add(existingExplosion);
                    }

                    if (existingNearbyExplosions.Count > explosionRateLimit)
                    {
                        Debug.Log($"[KCS]: Discarding explosion (speed {explosion.startVelocity.magnitude:0.0})");
                        continue;
                    }

                    // Add explosion effects.
                    var holder = new GameObject("KCS Explosion Host");
                    holder.transform.position = explosion.position;

                    var fx = holder.AddComponent<KCSExplosionFX>();
                    fx.startVelocity = explosion.startVelocity;

                    explosions.Add(explosion);


                    if (existingNearbyExplosions.Count > 0)
                    {
                        // Disable gas and debris when there are multiple explosions.
                        fx.useVapour = false;
                        fx.useDebris = false;
                    }
                }

                explosionQueue.Clear();
            }
        }

        private void OnOverheat(EventReport data)
        {
            Part part = data.origin;
            float explosionPotential = data.param;

            Debug.Log($"OnOverheat: {part.partInfo.name} with {explosionPotential}");
        }

        private void CreateExplosionBrightnessCurve()
        {
            // Create the animation curve.
            var curve = new AnimationCurve();
            curve.AddKey(0.0f, 0.0f);
            curve.AddKey(0.04f, lightIntensity);
            curve.AddKey(0.3f, 0.0f);
            curve.AddKey(1f, 0f);

            explosionBrightness = curve;
        }

        public static void HideExplosions(bool hide)
        {
            if (fxMonger == null)
            {
                fxMonger = FindObjectOfType<FXMonger>();

                if (fxMonger == null)
                {
                    Debug.Log("KCSFX: Could not find FXMonger!");
                    return;
                }
            }

            if (hide)
                Debug.Log("KCSFX: Hiding explosions.");
            else
                Debug.Log("KCSFX: Showing explosions.");

            replaceExplosions = hide;

            //GameObject[][] explosionLists = new GameObject[][] { fxMonger.explosions, fxMonger.debrisExplosion, fxMonger.thuds };
            GameObject[][] explosionLists = new GameObject[][] { fxMonger.explosions };

            foreach (GameObject[] explosionList in explosionLists)
            {
                foreach (GameObject explosion in explosionList)
                {
                    HideExplosion(explosion, hide);
                }
            }

            //GameEvents.onPartExplode.Add(OnPartExplode);
            //GameEvents.onCollision.Add(OnCollision);
            //fxMonger

            // rigidbody (velocity). 
            // explosive force (size/type)
        }

        public void OnPartWillDie(Part data)
        {
            if (!replaceExplosions)
                return;

            Debug.Log($"On part will die: {data.partInfo.name}");

            // Add explosion effects.
            //var holder = new GameObject("KCS Explosion");
            //holder.transform.position = data.transform.position;

            //var fx = holder.AddComponent<KCSExplosionFX>();
            //fx.startVelocity = data.RigidBodyPart.rb.velocity;

            Explosion queuedExplosion = new Explosion();
            queuedExplosion.position = data.transform.position;
            //queuedExplosion.startVelocity = data.RigidBodyPart.rb.velocity;
            queuedExplosion.startVelocity = data.vessel.rb_velocity;

            var explosionsByDistance = new List<Explosion>();
            float limitRadiusSqr = explosionLimitRadius * explosionLimitRadius;

            // Find the explosion under the limit that we are closest to.
            float closestDistance = float.MaxValue;
            Explosion mergeInto = new Explosion();

            foreach (var explosion in explosionQueue)
            {
                float distanceSqr = Vector3.SqrMagnitude(explosion.position - queuedExplosion.position);

                // If closer than the limit, insert the explosion into the list in order of distance.
                if (distanceSqr < limitRadiusSqr && distanceSqr < closestDistance)
                {
                    // Do not merge if there's a significant difference in speed (thus appearance).
                    Vector3 speedDiff = explosion.startVelocity - queuedExplosion.startVelocity;
                    if (speedDiff.magnitude < explosionMergeMaxSpeedDiff)
                    {
                        closestDistance = distanceSqr;
                        mergeInto = explosion;
                    }
                }
            }

            if (closestDistance < limitRadiusSqr)
            {
                // Merge the explosion into the closest one.
                mergeInto.startVelocity = (mergeInto.startVelocity + queuedExplosion.startVelocity) / 2;
                mergeInto.position = (mergeInto.position + queuedExplosion.position) / 2;
            }
            else
            {
                queuedExplosion.time = Time.time;
                explosionQueue.Add(queuedExplosion);
            }
        }

        private static void HideExplosion(GameObject explosion, bool hide)
        {
            explosion.SetActive(!hide);
            var explosionElements = explosion.GetComponentsInChildren<Transform>();

            foreach (Transform element in explosionElements)
            {
                if (!whitelist.Contains(element.name.ToLower()))
                {
                    if (element.childCount == 0)
                    {
                        // Disable the element.
                        element.gameObject.SetActive(!hide);
                    }
                    else
                    {
                        // Hide the particle system.
                        var renderer = element.GetComponent<ParticleSystemRenderer>();

                        if (renderer != null)
                            renderer.enabled = !hide;
                    }

                    // Disable audio source if it exists.
                    var audioSource = element.gameObject.GetComponent<AudioSource>();
                    if (audioSource != null)
                        audioSource.enabled = !hide;
                }
            }

            //var kcsExplosionFX = explosion.GetComponent<KCSExplosionFX>();
            //if (kcsExplosionFX != null)
            //{
            //    Destroy(kcsExplosionFX);
            //}

            //if (hide)
            //    explosion.AddComponent<KCSExplosionFX>();
        }
    }

    // stretch engine exhaust along up vector according to delta time, 24 fps, and 180 degree exposure.
    // lights stick around as glow? some kind of heat glow from high velocity kinetic impacts.

    public class KCSExplosionFX : MonoBehaviour
    {
        public bool useVapour = true;
        public bool useDebris = true;

        public Light[] lights;
        public Vector3 startVelocity;
        public GameObject explosion;
        public List<ParticleSystem> particleSystems = new List<ParticleSystem>();

        private Coroutine animateLights;

        // Start is called before the first frame update
        internal void Start()
        {
            // Create a separate game object to keep our static components on.

            //fxContainer = new GameObject("KCSExplosionFX");
            ////fxContainer.transform.parent = transform;
            ////fxContainer.transform.localPosition = Vector3.zero;
            //fxContainer.transform.localRotation = Quaternion.identity;
            //fxContainer.transform.position = transform.position;

            CreateLights();

            // Start the animation.
            KCSFX.explosionBrightness.keys[1].value = KCSFX.lightIntensity;
            animateLights = StartCoroutine(AnimateCurve());

            // Spawn the explosion prefab.
            explosion = Instantiate(KCSAssets.explosion, transform.position, Quaternion.identity);
            explosion.name = "KCS Explosion Particles";
            Destroy(explosion, 15f);

            // Optional removals.
            if (!useVapour)
            {
                var vapour = explosion.transform.Find("Vapour");
                vapour?.gameObject.SetActive(false);
            }

            if (!useDebris)
            {
                var debris = explosion.transform.Find("Debris");
                debris?.gameObject.SetActive(false);
            }

            // Make the explosion part of the floating origin system, we can give any part as an argument.
            //Part part = FlightGlobals.VesselsLoaded[0].parts[0];
            Part part;
            try
            {
                part = FlightGlobals.VesselsLoaded.Find(v => v.parts.Count > 0 && v.parts[0] != null).parts[0];
            }
            catch
            {
                Destroy(gameObject);
                return;
            }

            //var physicalExplosion = physicalObject.ConvertToPhysicalObject(part, explosion);
            Rigidbody erb = explosion.AddComponent<Rigidbody>();

            if (startVelocity == Vector3.zero)
            {
                Vessel nearestVessel = FlightGlobals.VesselsLoaded.OrderBy(v => Vector3.SqrMagnitude(v.transform.position - transform.position)).First();
                erb.velocity = nearestVessel.rb_velocity;
            }
            else
            {
                erb.velocity = startVelocity;
            }

            foreach (var ps in explosion.GetComponentsInChildren<ParticleSystem>())
            {
                // We can't use FloatingOrigin.RegisterParticleSystem because it uses offsetnonkrakensbane.
                // offsetnonkrakensbane is by how much we're moving the solar system (non-frame space).
                // offset is how much the stuff within the velocity frame is being moved by. (changing the centre of the frame to keep up with rb)
                // FO particle systems are part of non-frame space, which is wrong.

                particleSystems.Add(ps);
            }

            // Make the fx container part of the floating origin system.
            var physicalContainer = physicalObject.ConvertToPhysicalObject(part, gameObject);
            physicalContainer.maxDistance = float.MaxValue;
            physicalContainer.origDrag = 0;
            physicalContainer.StopAllCoroutines();
            physicalContainer.CancelInvoke();

            // Set colliderDelay to 2 via reflection.
            // We need to do this because physicalContainer enables and disables the GameObject,
            // which breaks the lighting coroutine. And I don't want to put the lights on another GameObject.
            var colliderDelay = physicalContainer.GetType().GetField("colliderDelay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            colliderDelay.SetValue(physicalContainer, 2f);

            Destroy(gameObject, 15);
        }

        internal void FixedUpdate()
        {
            IntegrateParticleSystems();
        }

        private void CreateLights()
        {
            GameObject lightHolder;
            Light light;
            lights = new Light[KCSFX.lightCount];
            Vector3[] positions = PointsOnSphere(KCSFX.lightCount);

            for (int i = 0; i < KCSFX.lightCount; i++)
            {
                lightHolder = new GameObject("KCS Explosion Light");
                //lightHolder.transform.parent = fxContainer.transform;
                lightHolder.transform.parent = transform;
                lightHolder.transform.localPosition = positions[i] * KCSFX.lightSpread;

                light = lightHolder.AddComponent<Light>();
                light.color = KCSFX.lightColor;
                light.range = KCSFX.lightRange;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.cullingMask = 1;
                light.intensity = 0;

                lights[i] = light;
            }

            if (lights.Any(l => l == null))
                Debug.Log("KCSFX: Lights are null.");
        }

        private void IntegrateParticleSystems()
        {
            if (FlightGlobals.ActiveVessel.packed)
                return;

            // Move all the particles by the within frame offset each fixed update.

            Vector3d offset = FloatingOrigin.fetch.offset;
            Vector3 excessV = FlightGlobals.ActiveVessel.rb_velocityD;

            particleSystems = particleSystems.Where(ps => ps != null).ToList();

            foreach (var particleSystem in particleSystems)
            {
                if (particleSystem.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    continue;

                int count = particleSystem.particleCount;
                ParticleSystem.Particle[] particleBuffer = particleSystem.GetParticleBuffer();

                if (count > 0)
                {
                    int num = count;
                    while (num-- > 0)
                    {
                        particleBuffer[num].position -= offset;
                        particleBuffer[num].velocity -= excessV;
                    }

                    particleSystem.SetParticles(particleBuffer, count);
                }
            }
        }

        private IEnumerator AnimateCurve()
        {
            float startTime = Time.time;
            float duration = KCSFX.explosionBrightness.keys.Last().time;

            float time;
            float timestepEnd;

            // The stock particle effects start on the next frame.
            yield return null;

            while (true)
            {
                if (Time.deltaTime == 0)
                {
                    yield return new WaitForSeconds(0);
                    continue;
                }

                time = (Time.time - startTime) * KCSFX.lightSpeed;
                timestepEnd = time + (Time.deltaTime * KCSFX.lightSpeed);

                float currentAmplitude;
                float previousAmplitude = float.MinValue;

                while (time <= timestepEnd)
                {
                    currentAmplitude = KCSFX.explosionBrightness.Evaluate(time);

                    if (currentAmplitude < previousAmplitude)
                    {
                        break;
                    }

                    previousAmplitude = currentAmplitude;
                    time += 0.005f;
                }

                foreach (var light in lights)
                {
                    light.intensity = previousAmplitude;
                }

                //float amplitude = KCSFX.explosionBrightness.Evaluate(time);
                //foreach (var light in lights)
                //{
                //    light.intensity = amplitude;
                //}

                if (time > duration || (time > 0 && previousAmplitude <= 0))
                    break;
                //if (time > duration || (time > 0 && amplitude <= 0))
                //    break;

                yield return new WaitForSeconds(0);
            }

            Debug.Log("[KCS]: Finished animating lights.");

            foreach (var light in lights)
                Destroy(light.gameObject);
        }

        public static Vector3[] PointsOnSphere(int n)
        {
            List<Vector3> upts = new List<Vector3>();
            float inc = Mathf.PI * (3 - Mathf.Sqrt(5));
            float off = 2.0f / n;
            float x = 0;
            float y = 0;
            float z = 0;
            float r = 0;
            float phi = 0;

            for (var k = 0; k < n; k++)
            {
                y = k * off - 1 + (off / 2);
                r = Mathf.Sqrt(1 - y * y);
                phi = k * inc;
                x = Mathf.Cos(phi) * r;
                z = Mathf.Sin(phi) * r;

                upts.Add(new Vector3(x, y, z));
            }
            Vector3[] pts = upts.ToArray();
            return pts;
        }
    }
}
