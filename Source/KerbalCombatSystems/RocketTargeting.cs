using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;


namespace KerbalCombatSystems
{
    public class ModuleRocket : ModuleWeapon
    {
        Vessel target;
        ModuleWeaponController controller;
        LineRenderer leadLine;
        private List<ModuleDecouplerDesignate> decouplers;
        internal ModuleDecouplerDesignate decoupler;
        Vector3 aimVector;

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
        static float maxSimTime = 20f;

        float fireCountdown;
        float firingInterval;
        float accuracyTolerance;
        bool fireSymmetry;

        GameObject prediction;

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();

            if (controller.target == null && vessel.targetObject == null) return;
            target = controller.target ?? vessel.targetObject.GetVessel();
            firingInterval = controller.firingInterval;
            fireCountdown = controller.fireCountdown;
            fireSymmetry = controller.fireSymmetry;
            accuracyTolerance = controller.accuracyTolerance;

            Debug.Log("[KCS]: decoupler is valid");
            NextRocket();

            leadLine = KCSDebug.CreateLine(Color.green);
            if (KCSDebug.showLines)
                prediction = CreateSphere();
        }

        public override Vector3 Aim()
        {
            if (decouplers.Count < 1 || decoupler == null || decoupler.part.vessel != vessel || target == null)
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
            Vector3 relativeAcceleration = (target.acceleration - FlightGlobals.getGeeForceAtPosition(target.CoM)) - (vessel.acceleration - FlightGlobals.getGeeForceAtPosition(vessel.CoM));
            leadVector = target.CoM + Displacement(target.obt_velocity - vessel.obt_velocity, relativeAcceleration, timeToHit);
            leadVector -= origin;

            KCSDebug.PlotLine(new Vector3[] { origin, origin + leadVector }, leadLine);

            // Scale the accuracy requirement (in degrees) based on the distance and size of the target.
            Vector3 targetRadius = Vector3.ProjectOnPlane(Vector3.up, targetVector.normalized).normalized * (controller.targetSize / 2) * accuracyTolerance;
            float aimTolerance = Vector3.Angle(targetVector, targetVector + targetRadius);

            bool onTarget = Vector3.Angle(leadVector.normalized, decoupler.transform.up) < aimTolerance;
            if (onTarget)
            {
                // We must remain on target for fireCountdown seconds before we can fire.
                // This ensures that angular velocity remains low, which otherwise leads to curved trajectories.

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

                    // Create a static pink ball where the hit is predicted to happen.
                    if (KCSDebug.showLines)
                    {
                        GameObject prediction = CreateSphere();
                        prediction.transform.position = origin + leadVector;
                    }
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
                if (eng == null)
                    continue;

                engines.Add(eng);

                // Measure total fuel consumption in tons per second.
                consumptionRate += Mathf.Lerp(eng.minFuelFlow, eng.maxFuelFlow, 1 * 0.01f * eng.thrustPercentage) * eng.flowMultiplier; // throttle = 1
            }

            if (engines.Count < 1)
                return -1;

            Vector3 thrustVector = GetFireVector(engines) * -1;
            aimVector = thrustVector.normalized;

            float thrust = Vector3.Dot(thrustVector, decoupler.transform.up);
            if (thrust < 1)
                return -1;

            float fuelMass = FuelMass(rocketParts);
            float dryMass = DryMass(rocketParts);
            float time = 0;
            float mass = 0;

            Vector3 pos = decoupler.transform.position;
            Vector3 velocity = vessel.GetObtVelocity();

            Vector3 firerPos = vessel.CoM;
            Vector3 firerVel = vessel.GetObtVelocity();
            Vector3 firerAcc = vessel.acceleration - FlightGlobals.getGeeForceAtPosition(target.CoM);

            Vector3 targetPos = target.CoM;
            Vector3 targetAcc = target.acceleration - FlightGlobals.getGeeForceAtPosition(target.CoM);
            Vector3 targetVel = target.GetObtVelocity();

            // Stop the simulation when the rocket is past the target.
            while (Vector3.Distance(firerPos, pos) < Vector3.Distance(firerPos, targetPos) && time < maxSimTime)
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

                // Update rocket, target and firer separately.
                // Target and rocket could be tracked by relative values but it's harder to conceptualise.

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

            return time;
        }

        public override void Fire()
        {
            //bit of a haphazard solution but this implementation of rocket firing is fairly constructed around only the AI being able to fire
            NextRocket();

            firing = true;
            StartCoroutine(FireRocket());
        }

        private IEnumerator FireRocket()
        {
            //run through all child parts of the controllers parent for engine modules
            List<Part> decouplerChildParts = decoupler.part.FindChildParts<Part>(true).ToList();

            foreach (Part currentPart in decouplerChildParts)
            {
                ModuleEngines module = currentPart.GetComponent<ModuleEngines>();

                //check for engine modules on the part and stop if not found
                if (module == null) continue;

                //activate the engine and force it to full capped thrust incase of ship throttle
                module.Activate();
                module.throttleLocked = true;
            }

            if (vessel.GetReferenceTransformPart() == controller.aimPart)
                FindController(vessel).RestoreReferenceTransform();

            decoupler.Separate();
            yield return new WaitForSeconds(firingInterval);

            firing = false;
        }

        private void NextRocket()
        {
            decouplers = FindDecouplerChildren(part.parent);
            if (decouplers.Count < 1)
            {
                controller.canFire = false;
                return;
            }

            decoupler = decouplers.Last();
            controller.aimPart = decoupler.part;
        }

        public void OnDestroy() =>
            KCSDebug.DestroyLine(leadLine);

        private GameObject CreateSphere()
        {
            GameObject prediction = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mr = prediction.GetComponent<MeshRenderer>();

            Material sphereMat = new Material(Shader.Find("Unlit/Color"));
            sphereMat.color = new Color(1f, 0f, 1f, 0.4f);
            mr.material = sphereMat;

            //todo sphere doesn't destroy
            prediction.transform.localScale = prediction.transform.localScale * 2;
            Destroy(prediction.GetComponent<SphereCollider>());

            return prediction;
        }
    }
}