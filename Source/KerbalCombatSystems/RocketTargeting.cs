using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using static KerbalCombatSystems.KCS;


namespace KerbalCombatSystems
{
    public class ModuleRocket : ModuleWeapon
    {
        Vessel target;
        ModuleWeaponController controller;
        LineRenderer leadLine;
        private List<ModuleDecouple> decouplers;
        internal ModuleDecouple decoupler;

        public bool firing = false;
        Vector3 targetVector;
        Vector3 leadVector;
        Vector3 rocketAcceleration;
        Vector3 origin;

        float lastOffTarget;
        float lastCalculated;
        float previousCalculation = 0;
        float latestCalculation = 0;
        static float calculateInterval = 0.5f;
        static float simTimestep = 0.02f;

        float fireCountdown;
        float firingInterval;
        bool fireSymmetry;

        GameObject prediction;

        //private float dfuelMass;
        //private float ddrymass;
        //private float dmass;
        //private float dacc;
        //private float drvel;
        //private float ddistance;
        //private float dthrust;

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();

            if (controller.target == null && vessel.targetObject == null) return;
            target = controller.target ?? vessel.targetObject.GetVessel();
            firingInterval = controller.firingInterval;
            fireCountdown = controller.fireCountdown;
            fireSymmetry = controller.fireSymmetry;

            NextRocket();

            leadLine = KCSDebug.CreateLine(Color.green);
            if (KCSDebug.showLines)
                prediction = CreateSphere();
        }

        public override Vector3 Aim()
        {
            if (decouplers.Count < 1 || decoupler == null || decoupler.vessel != vessel || target == null)
                return Vector3.zero;

            float timeSinceLastCalculated = Time.time - lastCalculated;
            if (timeSinceLastCalculated > calculateInterval)
            {
                if (latestCalculation == 0)
                    latestCalculation = Calculate();

                previousCalculation = latestCalculation;
                latestCalculation = Calculate();
                lastCalculated = Time.time;
                timeSinceLastCalculated = 0;
            }

            origin = decoupler.transform.position;

            float timeToHit = Mathf.LerpUnclamped(previousCalculation, latestCalculation, 1 + (timeSinceLastCalculated / calculateInterval));
            leadVector = target.CoM + Displacement(target.obt_velocity - vessel.obt_velocity, (target.acceleration - FlightGlobals.getGeeForceAtPosition(target.CoM)) - (vessel.acceleration - FlightGlobals.getGeeForceAtPosition(vessel.CoM)), timeToHit);
            leadVector -= origin;

            KCSDebug.PlotLine(new Vector3[] { origin, origin + leadVector }, leadLine);


            // Scale the accuracy requirement (in degrees) based on the distance and size of the target.
            //Vector3 targetVisualRadius = Vector3.ProjectOnPlane(Vector3.up, targetVector.normalized).normalized * controller.targetSize / 8;
            Vector3 targetVisualRadius = Vector3.ProjectOnPlane(Vector3.up, targetVector.normalized).normalized * controller.targetSize / 2;
            float aimTolerance = Vector3.Angle(targetVector, targetVector + targetVisualRadius);

            bool onTarget = Vector3.Angle(leadVector.normalized, decoupler.transform.up) < aimTolerance;
            if (onTarget)
            {
                // We must remain on target for fireCountdown seconds before we can fire.
                // This ensures that angular velocity is low, which otherwise leads to curved trajectories.

                if (Time.time - lastOffTarget > fireCountdown && !firing)
                {
                    Fire();

                    // Fire a rocket from any mirrored controllers.
                    if (fireSymmetry)
                    {
                        foreach (Part p in part.symmetryCounterparts)
                        {
                            var r = p.GetComponent<ModuleWeaponController>();
                            if (r != null && p != part)
                                r.Fire();
                        }
                    }

                    // Create a static pink ball where the hit it predicted to happen.
                    if (KCSDebug.showLines)
                    {
                        GameObject prediction = CreateSphere();
                        prediction.transform.position = origin + leadVector;
                    }

                    //Debug.Log($"dfuelMass: {dfuelMass}");
                    //Debug.Log($"ddrymass: {ddrymass}");
                    //Debug.Log($"dmass: {dmass}");
                    //Debug.Log($"dacc: {dacc}");
                    //Debug.Log($"drvel: {drvel}");
                    //Debug.Log($"ddistance: {ddistance}");
                    //Debug.Log($"dthrust: {dthrust}");

                    //float dfuelMass1 = dfuelMass;
                    //float ddrymass1 = ddrymass;
                    //float dmass1 = dmass;
                    //float dacc1 = dacc;
                    //float drvel1 = drvel;
                    //float ddistance1 = ddistance;
                    //float dthrust1 = dthrust;

                    //KCSController.rocketDebugTime = Time.time;
                }
            }
            else
            {
                lastOffTarget = Time.time;
            }

            return leadVector.normalized;
        }

        // Runs a simulation to find out how long it will take for a rocket to reach its target.
        public float Calculate()
        {
            targetVector = target.CoM - decoupler.transform.position;

            // Find out which parts of the vessel make up the rocket.
            var rocketParts = decoupler.part.FindChildParts<Part>(true).ToList();
            float consumptionRate = 0;

            if (rocketParts.Count < 1)
                return -1;

            // Find the engines.
            var engines = new List<ModuleEngines>();
            ModuleEngines eng;
            foreach (var p in rocketParts)
            {
                eng = p.FindModuleImplementing<ModuleEngines>();
                if (eng == null) continue;

                engines.Add(eng);
                
                // Sum fuel consumption in tons per second.
                consumptionRate += Mathf.Lerp(eng.minFuelFlow, eng.maxFuelFlow, 1 * 0.01f * eng.thrustPercentage) * eng.flowMultiplier; // throttle = 1
            }

            if (engines.Count < 1)
                return -1;

            // Calculate the thrust of the rocket, accounting for losses due to engines
            // pointing slightly off-axis (usually to induce spin, for accuracy).
            float thrust = 0;
            foreach (var e in engines)
            {
                // Not giving the correct value.
                //float cosineLosses = Vector3.Dot(-e.thrustTransforms[0].forward, decoupler.transform.up);
                
                float cosineLosses = 0.93333333333f;
                thrust += (e.MaxThrustOutputVac(true) * cosineLosses);
            }

            // Correct values
            //100.8 thrust
            //0.93 cl

            if (thrust < 1)
                return -1;
            
            float fuelMass = FuelMass(rocketParts);
            float dryMass = DryMass(rocketParts);
            float time = 0;
            float mass = dryMass + fuelMass;

            Vector3 pos = decoupler.transform.position;
            Vector3 velocity = vessel.GetObtVelocity();

            Vector3 firerPos = vessel.CoM;
            Vector3 firerVel = vessel.GetObtVelocity();
            Vector3 firerAcc = vessel.acceleration - FlightGlobals.getGeeForceAtPosition(target.CoM);

            Vector3 targetPos = target.CoM;
            Vector3 targetAcc = target.acceleration - FlightGlobals.getGeeForceAtPosition(target.CoM);
            Vector3 targetVel = target.GetObtVelocity();

            // Stop the simulation when the rocket is past the target.
            while (Vector3.Distance(firerPos, pos) < Vector3.Distance(firerPos, targetPos) && time < 20)
            {
                time += simTimestep;

                if (fuelMass > 0)
                {
                    // Drain fuel and update mass.
                    fuelMass -= consumptionRate * simTimestep;
                    mass = dryMass + fuelMass;
                }

                // Set values to exactly zero once when out of fuel.
                if (fuelMass < 0)
                {
                    fuelMass = 0;
                    thrust = 0;
                    mass = dryMass;
                }

                // Account for the curvature of the orbit by updating the gravitational part of the acceleration in each step.

                targetVel += (targetAcc + FlightGlobals.getGeeForceAtPosition(targetPos)) * simTimestep;
                targetPos += targetVel * simTimestep;

                firerVel += (firerAcc + FlightGlobals.getGeeForceAtPosition(firerPos)) * simTimestep;
                firerPos += firerVel * simTimestep;

                rocketAcceleration = targetVector.normalized * (thrust / mass);
                velocity += (FlightGlobals.getGeeForceAtPosition(pos) + rocketAcceleration) * simTimestep;
                pos += velocity * simTimestep;
            }

            // A floating pink ball predicts where the rocket will be when it passes the target.
            if (KCSDebug.showLines && prediction != null)
                prediction.transform.position = pos - (FlightGlobals.ActiveVessel.GetObtVelocity() * time);

            //dfuelMass = fuelMass;
            //ddrymass = dryMass;
            //dmass = mass;
            //dacc = ((Vector3)FlightGlobals.getGeeForceAtPosition(pos) + rocketAcceleration).magnitude;
            //drvel = (targetVel - velocity).magnitude;
            //ddistance = (targetPos - pos).magnitude;
            //dthrust = thrust;

            return time;
        }

        public override void Fire()
        {
            firing = true;
            StartCoroutine(FireRocket());
        }

        private IEnumerator FireRocket()
        {
            //run through all child parts of the controllers parent for engine modules
            List<Part> DecouplerChildParts = decoupler.part.FindChildParts<Part>(true).ToList();

            foreach (Part CurrentPart in DecouplerChildParts)
            {
                ModuleEngines Module = CurrentPart.GetComponent<ModuleEngines>();

                //check for engine modules on the part and stop if not found
                if (Module == null) continue;

                //activate the engine and force it to full capped thrust incase of ship throttle
                Module.Activate();
                Module.throttleLocked = true;
            }

            if (vessel.GetReferenceTransformPart() == controller.aimPart)
                FindController(vessel).RestoreReferenceTransform();

            //wait a frame before decoupling to ensure engine activation(may not be required)
            yield return null;
            decoupler.Decouple();

            NextRocket();
            yield return new WaitForSeconds(firingInterval);
            firing = false;
        }

        private void NextRocket()
        {
            decouplers = FindDecouplerChildren(part.parent, "Weapon", true);
            if (decouplers.Count < 1)
            {
                controller.canFire = false;
                return;
            }

            decoupler = decouplers.Last();
            controller.aimPart = decoupler.part;
            //acceleration = controller.CalculateAcceleration(decoupler.part);
        }

        public void OnDestroy() =>
            KCSDebug.DestroyLine(leadLine);

        private GameObject CreateSphere()
        {
            GameObject prediction = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mr = prediction.GetComponent<MeshRenderer>();

            Material sphereMat = new Material(Shader.Find("Unlit/Color"));
            sphereMat.color = Color.magenta;

            mr.material = sphereMat;

            prediction.transform.localScale = prediction.transform.localScale * 6;
            Destroy(prediction.GetComponent<SphereCollider>());

            return prediction;
        }
    }
}