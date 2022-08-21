﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static KerbalCombatSystems.KCS;
using Random = UnityEngine.Random;

namespace KerbalCombatSystems
{
    public class ModuleShipController : PartModule
    {
        // User parameters changed via UI.

        const string shipControllerGroupName = "Ship AI";
        public bool controllerRunning = false;
        public float updateInterval = 2.5f;

        // Ship AI variables.

        private Coroutine shipControllerCoroutine;
        private Coroutine behaviourCoroutine;
        private KCSFlightController fc;
        private KCSController controller;
        public Vessel target;
        public List<ModuleWeaponController> weapons;
        private float weaponsMinRange;
        private float weaponsMaxRange;
        private float lastFired;
        private float fireInterval;
        private float maxDetectionRange;
        public float initialMass;
        private bool hasPropulsion;
        private bool hasWeapons;
        private float lastUpdate;
        private double minSafeAltitude;
        public string state = "Init";

        [KSPField(isPersistant = true)]
        public Side side;

        [KSPField(isPersistant = true)]
        public bool alive = true;

        [KSPField(isPersistant = true)]
        private bool DeployedSensors;
        

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Manoeuvring Speed",
            guiUnits = "m/s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_FloatRange(
                minValue = 10f,
                maxValue = 500f,
                stepIncrement = 10f,
                scene = UI_Scene.All
            )]
        public float manoeuvringSpeed = 100f;


        #region Controller State & Start/Update

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Toggle AI",
                  groupName = shipControllerGroupName,
                  groupDisplayName = shipControllerGroupName)]
        public void ToggleAI()
        {
            if (!controllerRunning) StartAI();
            else StopAI();
        }

        public void StartAI()
        {
            initialMass = vessel.GetTotalMass();
            CheckWeapons();
            shipControllerCoroutine = StartCoroutine(ShipController());
            controllerRunning = true;
        }

        public void StopAI()
        {
            controllerRunning = false;
            StopCoroutine(shipControllerCoroutine);

            if (behaviourCoroutine != null)
                StopCoroutine(behaviourCoroutine);
        }

        private void Start()
        {
            if (HighLogic.LoadedSceneIsFlight)
                StartCoroutine(StatusChecker());
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                fc = part.gameObject.AddComponent<KCSFlightController>();
                controller = FindObjectOfType<KCSController>();
            }
        }
        private void FixedUpdate()
        {
            if (controllerRunning) fc.Drive();
        }

        #endregion

        #region Main Functions/Loops

        private IEnumerator StatusChecker()
        {
            while (true)
            {
                CheckStatus();
                if (!alive) {
                  StopAI();
                  FireEscapePods();
                }
                
                yield return new WaitForSeconds(updateInterval);
            }
        }

        private IEnumerator ShipController()
        {
            CheckStatus();
            if (!alive)
            {
                StopAI();
                yield break;
            } 

            while (true)
            {
                lastUpdate = Time.time;

                // Find target.

                UpdateDetectionRange();
                FindTarget();

                // Missiles.

                CheckWeapons();
                MissileFireControl();

                // Update behaviour tree for movement and projectile weapons.

                behaviourCoroutine = StartCoroutine(UpdateBehaviour());
                yield return behaviourCoroutine;
            } 
        }

        private IEnumerator UpdateBehaviour()
        {
            // Movement.

            if (hasPropulsion && !hasWeapons)
            {
                // Check if ship actually needs to and is able to withdraw.
                // todo: these checks should be part of the behaviour condition via a function.

                state = "Withdrawing";

                if (vessel.VesselDeltaV.TotalDeltaVActual < 1) yield break; 

                var nearest = GetNearestEnemy();
                if (nearest == null)
                {
                    fc.throttle = 0;
                    yield break;
                }

                if (Mathf.Abs((nearest.vessel.GetObtVelocity() - vessel.GetObtVelocity()).magnitude) < 200)
                {
                    // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.

                    double initDeltaV = vessel.VesselDeltaV.TotalDeltaVActual;
                    Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                    bool facingNorth = Vector3.Angle(vessel.ReferenceTransform.up, orbitNormal) < 90;

                    fc.throttle = 1;
                    fc.attitude = orbitNormal * (facingNorth ? 1 : -1);

                    while (vessel.VesselDeltaV.TotalDeltaVActual > (initDeltaV - 200))
                    {
                        if (vessel.VesselDeltaV.TotalDeltaVActual < 1) break;

                        orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                        fc.attitude = orbitNormal * (facingNorth ? 1 : -1);

                        yield return new WaitForSeconds(1.0f);
                    }

                    fc.throttle = 0;
                }
            }
            else if (false) // Able to use a projectile weapon (range, ammo)
            {
                // Aim at target using current projectile weapon.

            }
            else if (false) // Needs to start evading an incoming missile.
            {

            }
            else if (CheckOrbitUnsafe())
            {
                Orbit o = vessel.orbit;
                double UT = Planetarium.GetUniversalTime();

                if (o.ApA < minSafeAltitude) 
                {
                    // Entirety of orbit is inside atmosphere, burn up until apoapsis is outside atmosphere by a 10% margin.

                    state = "Correcting Orbit (Apoapsis too low)";
                    fc.throttle = 1;

                    while (o.ApA < minSafeAltitude * 1.1)
                    {
                        UT = Planetarium.GetUniversalTime();
                        fc.attitude = o.Radial(UT);
                        yield return new WaitForFixedUpdate();
                    }
                }
                else if (o.altitude < minSafeAltitude)
                {
                    if (o.timeToPe < o.timeToAp)
                    {
                        // Our apoapsis is outside the atmosphere but we are inside the atmosphere and descending.
                        // Burn up until we are ascending and our apoapsis is outside the atmosphere by a 10% margin.

                        state = "Correcting Orbit (Falling inside atmo)";
                        fc.throttle = 1;

                        while (o.ApA < minSafeAltitude * 1.1 || o.timeToPe < o.timeToAp)
                        {
                            UT = Planetarium.GetUniversalTime();
                            fc.attitude = o.Radial(UT);
                            yield return new WaitForFixedUpdate();
                        }
                    }

                    // We are inside the atmosphere but our apoapsis is outside the atmosphere and we are gaining altitude.
                    // The most efficient thing to do is idle.

                    state = "Correcting Orbit (Wait)";
                }
                else
                {
                    // We are outside the atmosphere but our periapsis is inside the atmosphere.
                    // Execute a burn to circularize our orbit at the current altitude.

                    state = "Correcting Orbit (Circularizing)";

                    Vector3d fvel, deltaV = Vector3d.up * 100;
                    fc.throttle = 1;

                    while (deltaV.magnitude > 2)
                    {
                        yield return new WaitForFixedUpdate();

                        UT = Planetarium.GetUniversalTime();
                        fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                        deltaV = fvel - vessel.GetObtVelocity();

                        fc.attitude = deltaV.normalized;
                        fc.throttle = Mathf.Lerp(0, 1, (float)(deltaV.magnitude / 10));
                    }
                }

                fc.throttle = 0;
            }
            else if (target != null && weapons.Count > 0)
            {
                // todo: implement for longer range movement.
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebModuleRendezvousAutopilot.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/OrbitalManeuverCalculator.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebLib/Maths/Gooding.cs

                /* 

                known: 

                mass of the target
                mass of each of my weapons
                min and max ranges of each of my weapons
                my position & velocity
                target position & velocity

                
                problem:

                maintain a good range for firing the best weapon for this target

                - best weapon: the weapon who's preferred target mass is closest to the target mass.
                - good range: a range between the min and max range of these weapons, preferrably nearer the max range.

                 */

                ModuleWeaponController currentWeapon = GetPreferredWeapon(target, weapons);
                float minRange = currentWeapon.MinMaxRange.x;
                float maxRange = currentWeapon.MinMaxRange.y;
                float currentRange = VesselDistance(vessel, target);

                if (currentRange < minRange)
                {
                    state = "Manoeuvring (Away)";

                    Vector3 prograde = vessel.GetObtVelocity().normalized;
                    bool targetIsPrograde = Vector3.Angle(FromTo(vessel, target).normalized, prograde) < 90;

                    fc.throttle = 1;

                    while ((Time.time - lastUpdate < updateInterval) && target != null)
                    {
                        prograde = vessel.GetObtVelocity().normalized;
                        fc.attitude = prograde * (targetIsPrograde ? -1 : 1);
                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;

                        yield return new WaitForFixedUpdate();
                    }
                }
                else if (currentRange > maxRange)
                {
                    Vector3 relVel, targetVec;
                    float angle;
                    float maxAcceleration = GetMaxAcceleration(vessel);

                    while ((Time.time - lastUpdate < updateInterval) && target != null)
                    {
                        relVel = RelVel(vessel, target);
                        targetVec = FromTo(vessel, target).normalized;
                        angle = Vector3.Angle(relVel.normalized, targetVec);

                        if (relVel.magnitude < maxAcceleration * 2)
                        {
                            state = "Manoeuvring (Target)";
                            fc.attitude = targetVec;
                        }
                        else
                        {
                            if (angle > 45)
                            {
                                state = "Manoeuvring (Match Velocity)";
                                fc.attitude = relVel.normalized * -1;
                            }
                            else
                            {
                                state = "Manoeuvring (Prograde to Target)";
                                fc.attitude = Vector3.LerpUnclamped(relVel.normalized, targetVec, 1 + 1 * (relVel.magnitude / maxAcceleration));
                            } 
                        }

                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;

                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    Vector3 relVel = RelVel(vessel, target);
                    float maxAcceleration = GetMaxAcceleration(vessel);

                    if (relVel.magnitude > 5 * GetMaxAcceleration(vessel))
                    {
                        state = "Manoeuvring (Kill Velocity)";

                        while ((Time.time - lastUpdate < updateInterval) && target != null)
                        {
                            relVel = RelVel(vessel, target);
                            fc.attitude = relVel.normalized * -1;
                            fc.throttle = relVel.magnitude > maxAcceleration ? 1 : 0;

                            yield return new WaitForFixedUpdate();
                        }
                    }
                    else
                    {
                        state = "Manoeuvring (Drift)";

                        fc.throttle = 0;
                        fc.attitude = FromTo(vessel, target).normalized;

                        while ((Time.time - lastUpdate < updateInterval) && target != null)
                        {
                            fc.attitude = FromTo(vessel, target).normalized;

                            yield return new WaitForFixedUpdate();
                        }
                    }
                }
            }
            else
            {
                // Idle

                state = "Idling";

                fc.throttle = 0;
                fc.attitude = Vector3.zero;
                yield return new WaitForSeconds(updateInterval);
            }
        }

        public void MissileFireControl()
        {
            if (target == null) return;

            if (Time.time - lastFired > fireInterval)
            {
                lastFired = Time.time;
                fireInterval = UnityEngine.Random.Range(5, 15);

                if (weapons.Count > 0)
                {
                    var weapon = GetPreferredWeapon(target, GetAvailableWeapons(target));

                    if (weapon != null)
                    {
                        weapon.target = target;
                        weapon.Fire();

                        StartCoroutine(CheckWeaponsDelayed());
                    }
                }
            }
        }

        #endregion

        #region Utility Functions

        public void CheckWeapons()
        {
            weapons = vessel.FindPartModulesImplementing<ModuleWeaponController>();

            if (weapons.Count > 0)
            {
                weaponsMinRange = weapons.Min(w => w.MinMaxRange.x);
                weaponsMaxRange = weapons.Max(w => w.MinMaxRange.y);
            }
        }

        public IEnumerator CheckWeaponsDelayed()
        {
            yield return new WaitForFixedUpdate();
            CheckWeapons();
        }

        public ModuleShipController GetNearestEnemy()
        {
            var enemiesByDistance = controller.ships.FindAll(s => s.alive && s.side != side);
            if (enemiesByDistance.Count < 1) return null;
            return enemiesByDistance.OrderBy(s => KCS.VesselDistance(s.vessel, vessel)).First();
        }

        private ModuleWeaponController GetPreferredWeapon(Vessel target, List<ModuleWeaponController> weapons)
        {
            float targetMass = (float)target.totalMass;
            if (weapons.Count < 1) return null;
            return weapons.OrderBy(w => Mathf.Abs(targetMass - (w.mass * w.targetMassRatio))).First();
        }

        private List<ModuleWeaponController> GetAvailableWeapons(Vessel target)
        {
            float targetRange = FromTo(vessel, target).magnitude;
            return weapons.FindAll(w => targetRange > w.MinMaxRange.x && targetRange < w.MinMaxRange.y);
        }

        private void FireEscapePods()
        {
            //function to fire escape pods when the ship is dead but still holds AI units
            List<ModuleEscapePodGuidance> PodList = vessel.FindPartModulesImplementing<ModuleEscapePodGuidance>();
            //trigger the escape start method in every found controller
            foreach (ModuleEscapePodGuidance EscapePod in PodList)
            {
                EscapePod.BeginEscape();
            }
        }

        public bool CheckStatus()
        {
            hasPropulsion = vessel.FindPartModulesImplementing<ModuleEngines>().FindAll(e => e.EngineIgnited && e.isOperational).Count > 0;
            hasWeapons = vessel.FindPartModulesImplementing<ModuleWeaponController>().Count > 0;
            //bool control = vessel.maxControlLevel != Vessel.ControlLevel.NONE && vessel.angularVelocity.magnitude < 20;
            bool control = vessel.isCommandable && vessel.angularVelocity.magnitude < 20;
            bool dead = (!hasPropulsion && !hasWeapons) || !control;

            alive = !dead;
            return alive;
        }

        void UpdateDetectionRange()
        {
            var sensors = vessel.FindPartModulesImplementing<ModuleObjectTracking>();

            if (sensors.Count < 1)
            {
                maxDetectionRange = 1000;
            }
            else
            {
                maxDetectionRange = sensors.Max(s => s.detectionRange);
            }

            //if the sensors aren't deployed and the AI is running
            if(!DeployedSensors && controllerRunning)
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    var anim = Sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                    if (anim == null) continue;
                    KCS.TryToggle(true, anim);
                }
                DeployedSensors = true;
            }

            //if the sensors are deployed and the AI isn't runnning
            if (DeployedSensors && !controllerRunning)
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    try { KCS.TryToggle(true, Sensor.part.FindModuleImplementing<ModuleAnimationGroup>()); } catch { }
                }
                DeployedSensors = true;
            }
        }

        void FindTarget()
        {
            var ships = controller.ships.FindAll(
                s => VesselDistance(s.vessel, vessel) < maxDetectionRange
                && s.side != side
                && s.alive);

            if (ships.Count < 1) { 
                target = null;
                return;
            }

            target = ships.OrderBy(s => VesselDistance(s.vessel, vessel)).First().vessel;

            if (vessel.targetObject == null || vessel.targetObject.GetVessel() != target)
            {
                vessel.targetObject = target;

                if (vessel == FlightGlobals.ActiveVessel)
                    FlightGlobals.fetch.SetVesselTarget(target, true);
            }
        }

        public void ToggleSide()
        {
            if (side == Side.A)
            {
                side = Side.B;
            }
            else
            {
                side = Side.A;
            }
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            CelestialBody body = o.referenceBody;
            PQS pqs = body.pqsController;
            double maxTerrainHeight = pqs.radiusMax - pqs.radius;
            minSafeAltitude = Math.Max(maxTerrainHeight, body.atmosphereDepth);
            return o.PeA < minSafeAltitude;
        }

        #endregion
    }
}


