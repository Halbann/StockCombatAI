using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    public class ModuleShipController : PartModule
    {
        const string shipControllerGroupName = "Ship AI";

        // User parameters changed via UI.

        public bool controllerRunning = false;
        public float updateInterval;
        public float emergencyUpdateInterval = 0.5f;
        public float combatUpdateInterval = 2.5f;
        private bool allowRetreat;
        public float firingAngularVelocityLimit = 1; // degrees per second
        public float firingInterval = 7.5f;
        public float controlTimeout = 10;

        // Robotics tracking variables

        List<ModuleCombatRobotics> WeaponRoboticControllers = new List<ModuleCombatRobotics>();
        List<ModuleCombatRobotics> FlightRoboticControllers = new List<ModuleCombatRobotics>();

        // Ship AI variables.

        private KCSFlightController fc;

        public Vessel target;
        private ModuleShipController targetController;

        private Coroutine shipControllerCoroutine;
        private Coroutine behaviourCoroutine;
        private Coroutine missileCoroutine;
        private Coroutine interceptorCoroutine;
        public string state = "Init";
        private float lastUpdate;

        public List<ModuleWeaponController> weapons;
        private ModuleWeaponController currentProjectile;
        public List<ModuleWeaponController> incomingWeapons;
        private List<Tuple<ModuleWeaponController, float>> dodgeWeapons;
        private List<ModuleWeaponController> interceptors;
        private List<ModuleWeaponController> weaponsToIntercept;
        private float lastFired = 0;

        internal float maxDetectionRange;
        internal float maxWeaponRange;
        public float initialMass;
        private bool hasPropulsion;
        private bool hasWeapons;
        private bool hasControl;
        private float maxAcceleration;
        private bool roboticsDeployed;
        private bool WeaponRoboticsDeployed;
        private float shipLength;
        private Vector3 maxAngularAcceleration;
        private double minSafeAltitude;
        public float heatSignature;
        public float averagedSize;
        private float interceptorAcceleration = -1;
        private Part editorChild;
        private float lastInControl;
        private Part originalReferenceTransform;

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

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Firing Speed",
            guiUnits = "m/s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_FloatRange(
                minValue = 0f,
                maxValue = 100f,
                stepIncrement = 10f,
                scene = UI_Scene.All
            )]
        public float firingSpeed = 20f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Max. Salvo Size",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_FloatRange(
                minValue = 1,
                maxValue = 20,
                stepIncrement = 1,
                scene = UI_Scene.All
            )]
        public float maxSalvoSize = 5;

        [KSPField(
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Withdrawing Enemies",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None,
            options = new string[] { "Default", "Chase", "Ignore" })]
        public string withdrawingPriority = "Default";

        // Debugging
        internal float nearInterceptBurnTime;
        internal float nearInterceptApproachTime;
        internal float lateralVelocity;

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
            CheckWeapons();
            shipControllerCoroutine = StartCoroutine(ShipController());
            controllerRunning = true;

            //persist based settings for ship allowances
            allowRetreat = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().VeryDishonourable;
            //combatUpdateInterval = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().RefreshRate;
            //emergencyUpdateInterval = combatUpdateInterval / 4f;
        }

        public void StopAI()
        {
            fc.throttle = 0;
            fc.Drive();
            controllerRunning = false;

            if (shipControllerCoroutine != null)
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
            UpdateAttachment();

            if (HighLogic.LoadedSceneIsFlight)
            {
                fc = part.gameObject.AddComponent<KCSFlightController>();
                fc.alignmentToleranceforBurn = 7.5f;
                fc.throttleLerpRate = 3;

                Vector3 size = vessel.vesselSize;
                shipLength = (new[] { size.x, size.y, size.z }).ToList().Max();
                averagedSize = AveragedSize(vessel);
                initialMass = vessel.GetTotalMass();
                StartCoroutine(CalculateMaxAcceleration());

                weaponsToIntercept = new List<ModuleWeaponController>();
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartEvent.Add(UpdateAttachment);
            }
        }

        private IEnumerator CalculateMaxAcceleration()
        {
            while (vessel.MOI == Vector3.zero)
            {
                yield return new WaitForSeconds(1);
            }

            Vector3 availableTorque = Vector3.zero;
            var reactionWheels = vessel.FindPartModulesImplementing<ModuleReactionWheel>();
            foreach (var wheel in reactionWheels)
            {
                wheel.GetPotentialTorque(out Vector3 pos, out pos);
                availableTorque += pos;
            }

            maxAngularAcceleration = AngularAcceleration(availableTorque, vessel.MOI);
        }

        private void FixedUpdate()
        {
            if (controllerRunning) fc.Drive();
        }

        public void OnDestroy()
        {
            GameEvents.onEditorPartEvent.Remove(UpdateAttachment);

            if (HighLogic.LoadedSceneIsFlight && !vessel.packed && alive)
            {
                alive = false;
                DeathMessage(true);
            }
        }

        #endregion

        #region Main Functions/Loops

        private IEnumerator StatusChecker()
        {
            while (true)
            {
                CheckStatus();
                if (!alive)
                {
                    DeathMessage();
                    StopAI();
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Abort, true);
                    yield break;
                }

                CalculateHeatSignature();
                UpdateIncoming();

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

                CheckWeapons();

                if (hasWeapons || target != null)
                {
                    FindTarget();
                    UpdateDetectionRange();
                    FindInterceptTarget();

                    interceptorCoroutine = StartCoroutine(InterceptorFireControl());
                    yield return interceptorCoroutine;

                    missileCoroutine = StartCoroutine(MissileFireControl());
                    yield return missileCoroutine;
                }

                // Update behaviour tree for movement and projectile weapons.

                behaviourCoroutine = StartCoroutine(UpdateBehaviour());
                yield return behaviourCoroutine;
            }
        }

        private IEnumerator UpdateBehaviour()
        {
            maxAcceleration = GetMaxAcceleration(vessel);
            fc.RCSVector = Vector3.zero;

            // Movement.
            if (hasPropulsion && !hasWeapons && CheckWithdraw() && allowRetreat)
            {
                if (state != "Withdrawing")
                    KCSController.Log("%1 started to withdraw (out of weapons)", vessel);

                state = "Withdrawing";

                // Switch to passive robotics while withdrawing.
                UpdateFlightRobotics(false);

                // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.

                Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                bool facingNorth = Vector3.Angle(vessel.ReferenceTransform.up, orbitNormal) < 90;
                Vector3 deltav = orbitNormal * (facingNorth ? 1 : -1) * 200;
                fc.throttle = 1;

                while (deltav.magnitude > 10)
                {
                    if (!hasPropulsion) break;

                    deltav -= Vector3.Project(vessel.acceleration, deltav) * TimeWarp.fixedDeltaTime;
                    fc.attitude = deltav.normalized;

                    yield return new WaitForFixedUpdate();
                }

                fc.throttle = 0;
            }
            else if (CheckIncoming()) // Needs to start evading an incoming missile.
            {
                state = "Dodging";

                float previousTolerance = fc.alignmentToleranceforBurn;
                fc.alignmentToleranceforBurn = 45;
                fc.throttle = 1;

                ModuleWeaponController incoming = dodgeWeapons.First().Item1;
                Vector3 incomingVector;
                Vector3 dodgeVector;
                bool complete = false;

                while (UnderTimeLimit() && incoming != null && !complete)
                {
                    incomingVector = FromTo(vessel, incoming.vessel);
                    dodgeVector = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, incomingVector.normalized);
                    fc.attitude = dodgeVector;
                    fc.RCSVector = dodgeVector * 2;

                    yield return new WaitForFixedUpdate();
                    complete = Vector3.Dot(RelVel(vessel, incoming.vessel), incomingVector) < 0;
                }

                fc.throttle = 0;
                fc.alignmentToleranceforBurn = previousTolerance;
            }
            //else if (target != null && HasLock() && CanFireProjectile(target) && AngularVelocity(vessel, target) < firingAngularVelocityLimit)
            else if (target != null && HasLock() && CanFireProjectile(target))
            {
                // Aim at target using current projectile weapon.
                // The weapon handles firing.

                state = "Firing Projectile";
                fc.throttle = 0;
                currentProjectile.target = target;
                currentProjectile.side = side;
                fc.lerpAttitude = false;

                if (!currentProjectile.setup)
                    currentProjectile.Setup();

                if (!currentProjectile.fireSymmetry)
                {
                    //originalReferenceTransform = vessel.GetReferenceTransformPart();
                    vessel.SetReferenceTransform(currentProjectile.aimPart);
                }

                currentProjectile.targetSize = targetController.averagedSize;

                while (UnderTimeLimit() && target != null && currentProjectile.canFire)
                {
                    fc.attitude = currentProjectile.Aim();
                    fc.RCSVector = Vector3.ProjectOnPlane(RelVel(vessel, target), FromTo(vessel, target)) * -1;

                    // todo: correct for relative and angular velocity while firing if firing at an accelerating target

                    yield return new WaitForFixedUpdate();
                }

                RestoreReferenceTransform();
                fc.lerpAttitude = true;
            }
            else if (CheckOrbitUnsafe())
            {
                Orbit o = vessel.orbit;
                double UT;

                if (o.ApA < minSafeAltitude)
                {
                    // Entirety of orbit is inside atmosphere, burn up until apoapsis is outside atmosphere by a 10% margin.

                    state = "Correcting Orbit (Apoapsis too low)";
                    fc.throttle = 1;

                    while (UnderTimeLimit() && o.ApA < minSafeAltitude * 1.1)
                    {
                        UT = Planetarium.GetUniversalTime();
                        fc.attitude = o.Radial(UT);
                        yield return new WaitForFixedUpdate();
                    }
                }
                else if (o.altitude < minSafeAltitude)
                {
                    // Our apoapsis is outside the atmosphere but we are inside the atmosphere and descending.
                    // Burn up until we are ascending and our apoapsis is outside the atmosphere by a 10% margin.

                    state = "Correcting Orbit (Falling inside atmo)";
                    fc.throttle = 1;

                    while (UnderTimeLimit() && (o.ApA < minSafeAltitude * 1.1 || o.timeToPe < o.timeToAp))
                    {
                        UT = Planetarium.GetUniversalTime();
                        fc.attitude = o.Radial(UT);
                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    // We are outside the atmosphere but our periapsis is inside the atmosphere.
                    // Execute a burn to circularize our orbit at the current altitude.

                    state = "Correcting Orbit (Circularizing)";

                    Vector3d fvel, deltaV = Vector3d.up * 100;
                    fc.throttle = 1;

                    while (UnderTimeLimit() && deltaV.magnitude > 2)
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
            else if (target != null && weapons.Count > 0 && hasWeapons)
            {
                // todo: implement for longer range movement.
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebModuleRendezvousAutopilot.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/OrbitalManeuverCalculator.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebLib/Maths/Gooding.cs

                // Deploy combat robotics.
                UpdateFlightRobotics(true);

                ModuleWeaponController currentWeapon = GetPreferredWeapon(target, weapons);
                float minRange = currentWeapon.MinMaxRange.x;
                float maxRange = currentWeapon.MinMaxRange.y;
                float currentRange = VesselDistance(vessel, target);
                bool complete = false;
                bool nearInt = false;
                Vector3 relVel = RelVel(vessel, target);

                if (currentRange < minRange && AwayCheck(minRange))
                {
                    state = "Manoeuvring (Away)";
                    fc.throttle = 1;
                    float oldAlignment = fc.alignmentToleranceforBurn;
                    fc.alignmentToleranceforBurn = 135;

                    while (UnderTimeLimit() && target != null && !complete)
                    {
                        fc.attitude = FromTo(vessel, target).normalized * -1;
                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;
                        complete = FromTo(vessel, target).magnitude > minRange;

                        yield return new WaitForFixedUpdate();
                    }

                    fc.alignmentToleranceforBurn = oldAlignment;
                }
                // Reduce near intercept time by accounting for target acceleration
                // It should be such that "near intercept" is so close that you would go past them after you stop burning despite their acceleration
                // Also a chase timeout after which both parties should just use their weapons regardless of range.
                else if (currentRange > maxRange
                    && !(nearInt = NearIntercept(relVel, minRange, maxRange))
                    && CanInterceptShip(targetController))
                {
                    state = "Manoeuvring (Intercept Target)";

                    while (UnderTimeLimit() && target != null && !complete)
                    {
                        Vector3 toTarget = FromTo(vessel, target);
                        relVel = target.GetObtVelocity() - vessel.GetObtVelocity();

                        toTarget = ToClosestApproach(toTarget, relVel * -1, minRange * 1.2f);

                        // Burn the difference between the target and current velocities.
                        Vector3 desiredVel = toTarget.normalized * 100;
                        Vector3 burn = desiredVel - (relVel * -1);

                        // Bias towards eliminating lateral velocity early on.
                        Vector3 lateral = Vector3.ProjectOnPlane(burn, toTarget.normalized);
                        burn = Vector3.Slerp(burn.normalized, lateral.normalized,
                            Mathf.Clamp01(lateral.magnitude / (maxAcceleration * 10))) * burn.magnitude;

                        lateralVelocity = lateral.magnitude;

                        float throttle = Vector3.Dot(RelVel(vessel, target), toTarget.normalized) < manoeuvringSpeed ? 1 : 0;
                        fc.throttle = throttle * Mathf.Clamp(burn.magnitude / maxAcceleration, 0.2f, 1);

                        if (fc.throttle > 0)
                            fc.attitude = burn.normalized;
                        else
                            fc.attitude = toTarget.normalized;

                        complete = FromTo(vessel, target).magnitude < maxRange || NearIntercept(relVel, minRange, maxRange);

                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    if (relVel.magnitude > firingSpeed || nearInt)
                    {
                        state = "Manoeuvring (Kill Velocity)";

                        while (UnderTimeLimit() && target != null && !complete)
                        {
                            relVel = RelVel(vessel, target);
                            fc.attitude = relVel.normalized * -1;
                            complete = relVel.magnitude < firingSpeed / 3;
                            fc.throttle = !complete ? 1 : 0;

                            yield return new WaitForFixedUpdate();
                        }
                    }
                    else if (target != null && currentProjectile != null && AngularVelocity(vessel, target) > firingAngularVelocityLimit)
                    {
                        state = "Manoeuvring (Kill Angular Velocity)";

                        while (UnderTimeLimit() && target != null && !complete)
                        {
                            complete = AngularVelocity(vessel, target) < firingAngularVelocityLimit / 2;
                            fc.attitude = Vector3.ProjectOnPlane(RelVel(vessel, target), FromTo(vessel, target)).normalized * -1;
                            fc.throttle = !complete ? 1 : 0;

                            yield return new WaitForFixedUpdate();
                        }
                    }
                    else
                    {
                        if (hasPropulsion)
                            state = "Manoeuvring (Drift)";
                        else
                            state = "Stranded";

                        fc.throttle = 0;
                        fc.attitude = Vector3.zero;

                        yield return new WaitForSeconds(updateInterval);
                    }
                }
            }
            else
            {
                // Idle

                if (hasWeapons)
                    state = "Idle";
                else
                    state = "Idle (Unarmed)";

                fc.throttle = 0;
                fc.attitude = Vector3.zero;

                // Switch to passive robotics.
                UpdateFlightRobotics(false);

                yield return new WaitForSeconds(updateInterval);
            }
        }

        public IEnumerator MissileFireControl()
        {
            if (target != null && weapons.Count > 0 && Time.time - lastFired > firingInterval && HasLock())
            {
                List<ModuleWeaponController> missiles = GetAvailableMissiles(target);
                var preferred = GetPreferredWeapon(target, missiles);
                if (preferred == null) yield break;

                lastFired = Time.time;
                bool checkWeapons = false;
                float targetMass = (float)target.totalMass;

                if (targetController.incomingWeapons.Count > 0)
                {
                    targetMass = (float)target.totalMass - targetController.incomingWeapons.Sum(w => w.mass * w.targetMassRatio);
                    if (targetMass < ((preferred.mass * 1.2f) * preferred.targetMassRatio)) yield break;
                }

                int salvoCount = (int)Mathf.Max(Mathf.Floor(targetMass / (preferred.mass * preferred.targetMassRatio)), 1);
                salvoCount = Mathf.Min(salvoCount, missiles.Count);
                salvoCount = Mathf.Min(salvoCount, (int)maxSalvoSize);

                List<ModuleWeaponController> salvo = GetPreferredWeapon(target, missiles, salvoCount);
                ModuleWeaponController last = salvo.Last();

                // Make a log entry.

                bool single = salvo.Count == 1;
                string generic = single ? "missile" : "missiles";
                string missileName = preferred.weaponCode == "" ? generic : preferred.weaponCode + " " + generic;

                if (!single)
                    KCSController.Log($"%1 fired a salvo of {salvo.Count} {missileName} at %2", vessel, target);
                else
                    KCSController.Log($"%1 fired a {missileName} at %2", vessel, target);

                // Fire each missile.

                foreach (ModuleWeaponController weapon in salvo)
                {
                    if (weapon == null || weapon.vessel != vessel) continue;

                    checkWeapons = true;

                    weapon.target = target;
                    weapon.side = side;
                    weapon.Fire();

                    KCSController.weaponsInFlight.Add(weapon);
                    targetController.AddIncoming(weapon);

                    if (weapon.frontLaunch)
                    {
                        Coroutine waitForLaunch = StartCoroutine(WaitForLaunch(weapon));
                        yield return waitForLaunch;
                    }
                    else if (weapon != last)
                        yield return new WaitForSeconds(0.8f);
                }

                if (checkWeapons)
                    CheckWeapons();
            }
        }

        private IEnumerator InterceptorFireControl()
        {
            if (weaponsToIntercept.Count > 0 && interceptors.Count > 0)
            {
                bool checkWeapons = false;
                ModuleWeaponController interceptor;

                // Make a log entry.

                int count = Mathf.Min(weaponsToIntercept.Count, interceptors.Count);
                string interceptorString = count > 1 ? $"{count} interceptors" : "an interceptor";
                KCSController.Log($"%1 launched {interceptorString}", vessel);

                // Fire each interceptor.

                foreach (var interceptTarget in weaponsToIntercept)
                {
                    if (interceptors.Count < 1) break;
                    checkWeapons = true;

                    Vector3 interceptVector = FromTo(vessel, interceptTarget.vessel).normalized;

                    interceptor = GetPreferredWeapon(interceptTarget.vessel, interceptors, 1).First();
                    interceptors.Remove(interceptor);

                    interceptor.isInterceptor = true;
                    interceptor.targetWeapon = interceptTarget;
                    interceptor.target = interceptTarget.vessel;
                    interceptor.side = side;
                    interceptor.Fire();

                    interceptTarget.interceptedBy.Add(interceptor);
                    KCSController.interceptorsInFlight.Add(interceptor);

                    if (interceptor.frontLaunch)
                    {
                        Coroutine waitForLaunch = StartCoroutine(WaitForLaunch(interceptor));
                        yield return waitForLaunch;
                    }
                    else
                        yield return new WaitForSeconds(0.25f);
                }

                if (checkWeapons)
                    CheckWeapons();
            }
        }

        #endregion

        #region Utility Functions

        public void CheckWeapons()
        {
            weapons = vessel.FindPartModulesImplementing<ModuleWeaponController>();

            // Store the max weapon range for the overlay.
            if (weapons.Count > 0)
                maxWeaponRange = weapons.Max(w => w.MinMaxRange.y);

            interceptors = weapons.FindAll(w => w.useAsInterceptor);

            // Store the expected interceptor acceleration for CanIntercept calculations.
            if (interceptors.Count > 0 && interceptorAcceleration < 0)
            {
                var firstInterceptor = interceptors.OrderBy(i => i.childDecouplers).First();
                interceptorAcceleration = firstInterceptor.CalculateAcceleration();
            }
        }

        public IEnumerator CheckWeaponsDelayed()
        {
            yield return new WaitForFixedUpdate();
            CheckWeapons();
        }

        public ModuleShipController GetNearestEnemy()
        {
            var enemiesByDistance = KCSController.ships.FindAll(s => s != null && s.alive && s.side != side);
            if (enemiesByDistance.Count < 1) return null;
            return enemiesByDistance.OrderBy(s => VesselDistance(s.vessel, vessel)).First();
        }

        private ModuleWeaponController GetPreferredWeapon(Vessel target, List<ModuleWeaponController> weapons)
        {
            if (weapons.Count < 1) return null;
            return GetPreferredWeapon(target, weapons, -1).First();
        }

        private List<ModuleWeaponController> GetPreferredWeapon(Vessel target, List<ModuleWeaponController> weapons, int count = 1)
        {
            if (weapons.Count < 1) return null;

            float targetMass = (float)target.totalMass;

            // Order the available weapons based on the suitability of the their mass compared to the target. 
            var weaponsRanked = weapons.OrderBy(w => Mathf.Abs(targetMass - (w.mass * w.targetMassRatio))).ToList();

            // Special case for when we just want to know the preffered missile type.
            if (count < 0)
                return weaponsRanked.Take(1).ToList();

            // Select the most suitable weapons.
            weaponsRanked = weaponsRanked.Take(count).ToList();

            // For each weapon that has already been selected, add any identical weapons to the list.
            var identicalWeapons = new List<ModuleWeaponController>();

            foreach (var selectedWeapon in weaponsRanked)
                identicalWeapons.AddRange(weapons.FindAll(w => Mathf.Approximately(selectedWeapon.mass, w.mass) && !weaponsRanked.Contains(w) && !identicalWeapons.Contains(w)));

            // Make doubly sure there aren't any duplicate entries.
            weaponsRanked.AddRange(identicalWeapons);
            weaponsRanked = weaponsRanked.Distinct().ToList();

            // Group and order the identical missiles by their orientation to the target. More lined-up is better. 
            Vector3 targetVector = FromTo(vessel, target).normalized;
            var bays = weaponsRanked.GroupBy(w => Math.Round(Vector3.Dot(w.part.parent.transform.up, targetVector), 1)).ToList();
            bays = bays.OrderByDescending(i => i.Key).ToList();

            // Order the groups internally by their position in the stack. No bumper torpedos.
            weaponsRanked.Clear();

            foreach (var bay in bays)
            {
                var orderedBay = bay.OrderBy(w => w.childDecouplers).ToList();
                weaponsRanked.AddRange(orderedBay);
            }

            // Return a list of weapons with a suitable mass for the target, prioritised by alignment to the target, and sorted to fire stacks in the correct sequence.
            return weaponsRanked.Take(count).ToList();
        }

        private List<ModuleWeaponController> GetAvailableMissiles(Vessel target)
        {
            float targetRange = FromTo(vessel, target).magnitude;
            return weapons.FindAll(w => w.weaponType == "Missile" && targetRange > w.MinMaxRange.x && targetRange < w.MinMaxRange.y);
        }

        private bool CanFireProjectile(Vessel target)
        {
            if (RelVel(vessel, target).magnitude > firingSpeed) return false;

            float targetRange = FromTo(vessel, target).magnitude;
            currentProjectile = null;

            List<ModuleWeaponController> available = weapons.FindAll(w => ModuleWeaponController.projectileTypes.Contains(w.weaponType));
            available = available.FindAll(w => targetRange > w.MinMaxRange.x && targetRange < w.MinMaxRange.y);
            available = available.FindAll(w => w.canFire);

            if (available.Count < 1) return false;

            currentProjectile = available.First();
            return true;
        }

        public bool CheckStatus()
        {
            hasPropulsion = vessel.FindPartModulesImplementing<ModuleEngines>().FindAll(e => e.EngineIgnited && e.isOperational).Count > 0;
            hasWeapons = vessel.FindPartModulesImplementing<ModuleWeaponController>().FindAll(w => w.canFire).Count > 0;

            bool spunOut = false;
            if (vessel.angularVelocity.magnitude > 50)
            {
                if (Time.time - lastInControl > controlTimeout)
                    spunOut = true;
            }
            else
                lastInControl = Time.time;

            hasControl = vessel.isCommandable && !spunOut;

            bool dead = (!hasPropulsion && !hasWeapons) || !hasControl;
            alive = !dead;

            return alive;
        }

        private bool CheckWithdraw()
        {
            var nearest = GetNearestEnemy();
            if (nearest == null) return false;

            return Mathf.Abs(RelVel(vessel, nearest.vessel).magnitude) < 200;
        }

        private void UpdateDetectionRange()
        {
            var sensors = vessel.FindPartModulesImplementing<ModuleObjectTracking>();

            if (sensors.Count < 1)
                maxDetectionRange = 1000;
            else
                maxDetectionRange = sensors.Max(s => s.detectionRange);

            //if the sensors aren't deployed and the AI is running
            if (!DeployedSensors && controllerRunning)
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    var anim = Sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                    if (anim == null) continue;
                    TryToggle(true, anim);
                }
                DeployedSensors = true;
            }

            //if the sensors are deployed and the AI isn't runnning
            if (DeployedSensors && !controllerRunning)
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try retract animations, not all scanners will have them 
                    var anim = Sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                    if (anim == null) continue;
                    TryToggle(false, anim);
                }
                DeployedSensors = false;
            }
        }

        private void FindTarget()
        {
            List<ModuleShipController> validEnemies = KCSController.ships.FindAll(
                s =>
                s != null
                && s.vessel != null
                && s.side != side
                && s.alive);

            if (!hasWeapons || validEnemies.Count < 1)
            {
                target = null;
                targetController = null;
                return;
            }

            // Seperate out withdrawing enemies and enemies with disabled AI and de-prioritise them.

            List<ModuleShipController> withdrawingEnemies = validEnemies.FindAll(s => s.state == "Withdrawing" || s.state == "Idle (Unarmed)");
            validEnemies = validEnemies.Except(withdrawingEnemies).ToList();

            // Remove all possibility of targeting withdrawing enemies who are out of range and can't be intercepted.
            withdrawingEnemies = withdrawingEnemies.Except(withdrawingEnemies.FindAll(s => FromTo(vessel, s.vessel).magnitude > maxWeaponRange && !CanInterceptShip(s))).ToList();

            List<ModuleShipController> offlineEnemies = validEnemies.FindAll(s => !s.controllerRunning);
            validEnemies = validEnemies.Except(offlineEnemies).ToList();

            validEnemies = validEnemies.OrderBy(s => WeighTarget(s)).ToList();

            if (withdrawingPriority != "Ignore")
            {
                withdrawingEnemies = withdrawingEnemies.OrderBy(s => WeighTarget(s)).ToList();

                if (withdrawingPriority == "Chase")
                {
                    withdrawingEnemies.AddRange(validEnemies);
                    validEnemies = withdrawingEnemies;
                }
                else
                {
                    validEnemies.AddRange(withdrawingEnemies);
                }
            }

            offlineEnemies = offlineEnemies.OrderBy(s => WeighTarget(s)).ToList();
            validEnemies.AddRange(offlineEnemies);

            // Check again in case any withdrawing ships were removed.
            if (validEnemies.Count < 1)
            {
                target = null;
                targetController = null;
                return;
            }

            // Pick the highest priority target.
            targetController = validEnemies.First();
            target = targetController.vessel;

            // Debugging
            List<Tuple<string, float, float>> targetsWeighted = validEnemies.Select(s => Tuple.Create(s.vessel.GetDisplayName(), WeighTarget(s), VesselDistance(vessel, s.vessel))).ToList();

            // Update the stock target to reflect the KCS target.
            if (vessel.targetObject == null || vessel.targetObject.GetVessel() != target)
            {
                vessel.targetObject = target;

                if (vessel == FlightGlobals.ActiveVessel)
                    FlightGlobals.fetch.SetVesselTarget(target, true);
            }
        }

        private float WeighTarget(ModuleShipController target)
        {
            float distance = VesselDistance(target.vessel, vessel);
            float massComparison = Mathf.Min(initialMass, target.initialMass) / Mathf.Max(initialMass, target.initialMass);

            return distance * (1 - massComparison);
        }

        private bool HasLock()
        {
            return FromTo(vessel, target).magnitude < maxDetectionRange * Mathf.Clamp((targetController.heatSignature / 1500), 0.5f, 3.0f);
        }

        private void FindInterceptTarget()
        {
            if (interceptors.Count < 1 || KCSController.weaponsInFlight.Count < 1) return;

            weaponsToIntercept = KCSController.weaponsInFlight.FindAll(
                w =>
                w != null
                && w.vessel != null
                && w.launched
                && !w.missed
                && w.side != side
                && w.interceptedBy.Count < 1
                && VesselDistance(w.vessel, vessel) < maxDetectionRange
                && CanIntercept(w));

            weaponsToIntercept = weaponsToIntercept.OrderBy(w => VesselDistance(w.vessel, vessel)).ToList();

            var priorityIntercept = weaponsToIntercept.FindAll(w => w.target == vessel);
            if (priorityIntercept.Count > 0)
            {
                weaponsToIntercept = weaponsToIntercept.Except(priorityIntercept).ToList();
                weaponsToIntercept = priorityIntercept.Concat(weaponsToIntercept).ToList();
            }

            if (weaponsToIntercept.Count > 0 && updateInterval != emergencyUpdateInterval)
                updateInterval = emergencyUpdateInterval;
            else if (weaponsToIntercept.Count < 1 && updateInterval == emergencyUpdateInterval)
                updateInterval = combatUpdateInterval;
        }

        private bool CanIntercept(ModuleWeaponController weaponModule)
        {
            if (interceptorAcceleration < 1)
            {
                // We can't know if our interceptors are fast enough.
                // Say that we can intercept anyway, but only if it's heading for us.
                if (weaponModule.target == vessel)
                    return true;
                else
                    return false;
            }

            Vessel weapon = weaponModule.vessel;
            Vessel target = weaponModule.target;

            Vector3 weaponToTarget = target.CoM - weapon.CoM;
            Vector3 weaponAccVector = weaponToTarget.normalized * weaponModule.missile.maxAcceleration;
            Vector3 weaponRelVel = target.GetObtVelocity() - weapon.GetObtVelocity();

            // Exit if the missile is not actually going towards the target.
            if (Vector3.Dot(weaponToTarget, weaponRelVel * -1) < 0)
                return false;

            Vector3 intToTarget = target.CoM - vessel.CoM;
            Vector3 intAccVector = intToTarget.normalized * interceptorAcceleration;

            float timeToIntercept = ClosestTimeToCPA(intToTarget, target.GetObtVelocity() - vessel.GetObtVelocity(), target.acceleration - intAccVector, 99);

            // We can't use weaponModule.timeToHit because it uses a less accurate method than ClosestTimeToCPA.
            float weaponTime = ClosestTimeToCPA(weaponToTarget, weaponRelVel, target.acceleration - weaponAccVector, 99);

            // Can the interceptor get to the target before the missile?
            // This is the minimum requirement for an interception,
            // Anything slower than this will fail and anything faster can be expected
            // to intercept the missile at some time before it hits the target.
            return timeToIntercept + 0.5f < weaponTime && weaponTime > 3;
        }

        public void ToggleSide()
        {
            if (side == Side.A)
                side = Side.B;
            else
                side = Side.A;
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            CelestialBody body = o.referenceBody;
            PQS pqs = body.pqsController;
            double maxTerrainHeight = pqs.radiusMax - pqs.radius;
            minSafeAltitude = Math.Max(maxTerrainHeight, body.atmosphereDepth);

            return (o.PeA < minSafeAltitude && o.timeToPe < o.timeToAp) || o.ApA < minSafeAltitude;
        }

        private bool UnderTimeLimit(float timeLimit = 0)
        {
            if (timeLimit == 0)
                timeLimit = updateInterval;

            return Time.time - lastUpdate < timeLimit;
        }

        private bool NearIntercept(Vector3 relVel, float minRange, float maxRange)
        {
            float timeToKillVelocity = relVel.magnitude / maxAcceleration;

            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, relVel.normalized * -1) * Mathf.Deg2Rad;
            float timeToRotate = SolveTime(rotDistance * 0.75f, maxAngularAcceleration.magnitude) / 0.75f;

            Vector3 toClosestApproach = ToClosestApproach(relVel, minRange);

            // Return false if we aren't headed towards the target.
            float velToClosestApproach = Vector3.Dot(relVel, toClosestApproach.normalized);
            if (velToClosestApproach < 1) return false;

            float timeToClosestApproach = ClosestTimeToCPA(toClosestApproach, target.GetObtVelocity() - vessel.GetObtVelocity(), Vector3.zero, 9999);
            Vector3 closestApproach = PredictPosition(FromTo(vessel, target), target.GetObtVelocity() - vessel.GetObtVelocity(), Vector3.zero, 999);
            bool hasIntercept = closestApproach.magnitude < maxRange;

            if (!hasIntercept)
                return false;

            nearInterceptBurnTime = timeToKillVelocity + timeToRotate;
            nearInterceptApproachTime = timeToClosestApproach;

            return timeToClosestApproach < (timeToKillVelocity + timeToRotate);
        }

        private bool CanInterceptShip(ModuleShipController target)
        {
            // Is it worth us chasing a withdrawing ship?

            Vector3 toTarget = target.vessel.CoM - vessel.CoM;
            bool escaping = target.state.Contains("Withdraw") || target.state.Contains("Idle (Unarmed)");

            bool canIntercept = !escaping || // It is not trying to escape.
                toTarget.magnitude < maxWeaponRange || // It is already in range.
                maxAcceleration > target.maxAcceleration || // We are faster.
                Vector3.Dot(target.vessel.GetObtVelocity() - vessel.GetObtVelocity(), toTarget) < 0; // It is getting closer.

            return canIntercept;
        }

        private bool AwayCheck(float minRange)
        {
            // Check if we need to manually burn away from an enemy that's too close or
            // if it would be better to drift away.

            Vector3 toTarget = FromTo(vessel, target);
            Vector3 toEscape = toTarget.normalized * -1;
            Vector3 relVel = target.GetObtVelocity() - vessel.GetObtVelocity();

            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, toEscape) * Mathf.Deg2Rad;
            float timeToRotate = SolveTime(rotDistance / 2, maxAngularAcceleration.magnitude) * 2;
            float timeToDisplace = SolveTime(minRange - toTarget.magnitude, maxAcceleration, Vector3.Dot(relVel * -1, toEscape));
            float timeToEscape = timeToRotate * 2 + timeToDisplace;

            Vector3 drift = PredictPosition(toTarget, relVel, Vector3.zero, timeToEscape);
            bool manualEscape = drift.magnitude < minRange;

            return manualEscape;
        }

        private Vector3 ToClosestApproach(Vector3 toTarget, Vector3 relVel, float minRange)
        {
            Vector3 relVelInverse = target.GetObtVelocity() - vessel.GetObtVelocity();
            float timeToIntercept = ClosestTimeToCPA(toTarget, relVelInverse, Vector3.zero, 9999);

            // Minimising the target closest approach to the current closest approach prevents
            // ships that are targeting each other from fighting over the closest approach based on their min ranges.
            // todo: allow for trajectory fighting if fuel is high.
            Vector3 actualClosestApproach = toTarget + Displacement(relVelInverse, Vector3.zero, timeToIntercept);
            float actualClosestApproachDistance = actualClosestApproach.magnitude;

            // Get a position that is laterally offset from the target by our desired closest approach distance.
            Vector3 rotatedVector = Vector3.ProjectOnPlane(relVel, toTarget.normalized).normalized;

            // Lead if the target is accelerating away from us.
            if (Vector3.Dot(target.acceleration.normalized, toTarget.normalized) > 0)
                toTarget += Displacement(Vector3.zero, toTarget.normalized * Vector3.Dot(target.acceleration, toTarget.normalized), Mathf.Min(timeToIntercept, 999));

            Vector3 toClosestApproach = toTarget + (rotatedVector * Mathf.Min(minRange, toTarget.magnitude, actualClosestApproachDistance));

            // Need a maximum angle so that we don't end up going further away at close range.
            toClosestApproach = Vector3.RotateTowards(toTarget, toClosestApproach, 22.5f, float.MaxValue);

            return toClosestApproach;
        }

        private Vector3 ToClosestApproach(Vector3 relVel, float minRange)
        {
            Vector3 toTarget = FromTo(vessel, target);
            return ToClosestApproach(toTarget, relVel, minRange);
        }

        public void UpdateFlightRobotics(bool deploy)
        {
            if (deploy == roboticsDeployed) return;
            //generate list of KAL500 parts, could change in flight
            List<ModuleCombatRobotics> RoboticControllers = vessel.FindPartModulesImplementing<ModuleCombatRobotics>();
            foreach (ModuleCombatRobotics KAL in RoboticControllers)
            {
                if (KAL.RoboticsType == "Ship") FlightRoboticControllers.Add(KAL);
            }

            if (deploy)
                FlightRoboticControllers.ForEach(rc => rc.KALTrigger(true));
            else
                FlightRoboticControllers.ForEach(rc => rc.KALTrigger(false));

            //clear list of all modules once fired
            FlightRoboticControllers.Clear();

            roboticsDeployed = deploy;
        }

        public float UpdateWeaponsRobotics(bool Deploy, string WeaponTag)
        {
            if (Deploy == WeaponRoboticsDeployed) return 0f;
            //generate list of KAL250 parts, could change in flight
            List<ModuleCombatRobotics> RoboticControllers = vessel.FindPartModulesImplementing<ModuleCombatRobotics>();
            if (RoboticControllers.Count() == 0) return 0f;

            foreach (ModuleCombatRobotics KAL in RoboticControllers)
            {
                Debug.Log(KAL.GetModuleDisplayName());
                if (KAL.RoboticsType != "Weapon") continue;
                if (KAL.GetModuleDisplayName() != WeaponTag && KAL.GetModuleDisplayName() != "KAL Series Robotics Controller") continue;
                WeaponRoboticControllers.Add(KAL);
            }

            //get longest sequence length to pass as a wait time before firing
            float MaxSeqLength = WeaponRoboticControllers.Max(t => t.SequenceLength);
            Debug.Log("max length" + MaxSeqLength);

            if (Deploy)
                WeaponRoboticControllers.ForEach(rc => rc.KALTrigger(true));
            else
                WeaponRoboticControllers.ForEach(rc => rc.KALTrigger(false));

            //clear list of all modules once fired
            WeaponRoboticControllers.Clear();
            WeaponRoboticsDeployed = Deploy;

            //return a wait time to avoid premature firing unless retracting
            if (Deploy == false)
            {
                return 0f;
            }
            else
            {
                return MaxSeqLength;
            }
        }

        private bool CheckIncoming()
        {
            if (incomingWeapons == null || incomingWeapons.Count < 1) return false;

            Vector3 attitude = vessel.transform.up;

            if (float.IsInfinity(maxAcceleration)) return false;
            float timeToDisplace = SolveTime(shipLength, maxAcceleration);
            if (float.IsInfinity(timeToDisplace)) return false;

            dodgeWeapons = new List<Tuple<ModuleWeaponController, float>>();
            UpdateIncoming();

            Vessel iv;
            Vector3 incomingVector;
            Vector3 relVel;
            bool onCollisionCourse;

            foreach (var incoming in incomingWeapons)
            {
                iv = incoming.vessel;
                incomingVector = FromTo(vessel, iv);
                relVel = RelVel(vessel, iv);

                onCollisionCourse = Vector3.Dot(incomingVector.normalized, relVel.normalized) > 0.95;
                if (!onCollisionCourse) continue;

                Vector3 perpendicular = Vector3.ProjectOnPlane(attitude, incomingVector.normalized);
                float rotDistance = Vector3.Angle(attitude, perpendicular) * Mathf.Deg2Rad;
                float timeToRotate = SolveTime(rotDistance / 2, maxAngularAcceleration.magnitude) * 2;
                float timeToDodge = timeToRotate + timeToDisplace;

                float timeToHit = SolveTime(incomingVector.magnitude, (float)iv.acceleration.magnitude, Vector3.Dot(relVel, incomingVector.normalized));

                if (timeToHit > Mathf.Max(timeToDodge * 1.25f, updateInterval * 2)) continue;
                dodgeWeapons.Add(new Tuple<ModuleWeaponController, float>(incoming, timeToHit));
            }

            dodgeWeapons = dodgeWeapons.OrderBy(i => i.Item2).ToList();
            return dodgeWeapons.Count > 0;
        }

        public void AddIncoming(ModuleWeaponController wep)
        {
            if (incomingWeapons == null)
                incomingWeapons = new List<ModuleWeaponController>();

            incomingWeapons.Add(wep);
        }

        private void UpdateIncoming()
        {
            incomingWeapons.RemoveAll(w => w == null || w.missed);
        }

        public float CalculateHeatSignature()
        {
            float hottestPartTemp = (float)vessel.parts.Max(p => (p.skinTemperature + p.temperature) / 2);
            heatSignature = hottestPartTemp * averagedSize;
            return heatSignature;
        }

        private IEnumerator WaitForLaunch(ModuleWeaponController weapon)
        {
            state = "Firing Missile";
            fc.throttle = 0;
            fc.Drive();
            fc.Stability(true);

            while (!weapon.launched && UnderTimeLimit(5))
            {
                fc.Drive();
                yield return new WaitForFixedUpdate();
            }

            fc.Stability(false);
        }

        public string SideColour()
        {
            return side == Side.A ? "#0AACE3" : "#E30A0A";
        }

        public static string SideColour(Side side)
        {
            return side == Side.A ? "#0AACE3" : "#E30A0A";
        }

        private void DeathMessage(bool noController = false)
        {
            var reasons = new List<string>();
            if (!hasWeapons) reasons.Add("no weapons");
            if (!hasPropulsion) reasons.Add("no propulsion");
            if (!hasControl) reasons.Add("no control");
            if (noController) reasons.Add("AI controller destroyed");
            string reason = string.Join(", ", reasons);

            //KCSController.Log(string.Format("<b><color={0}>{1}</color> was disabled ({2})</b>", SideColour(), ShortenName(vessel.GetDisplayName()), reason));
            KCSController.Log(string.Format("<b>%1 was disabled ({0})</b>", reason), vessel);
        }

        internal void RestoreReferenceTransform()
        {
            vessel.SetReferenceTransform(originalReferenceTransform);
        }

        #endregion

        #region Part Appearance

        private void UpdateAttachment()
        {
            Transform mediumCapTop = part.FindModelTransform("MediumCapTop");
            Transform mediumBoltsTop = part.FindModelTransform("MediumBoltsTop");
            Transform mediumCapBottom = part.FindModelTransform("MediumCapBottom");
            Transform mediumBoltsBottom = part.FindModelTransform("MediumBoltsBottom");

            if (mediumCapTop == null) return;

            bool topAttached = part.attachNodes[1].attachedPart != null;
            bool bottomAttached = part.attachNodes[0].attachedPart != null || !topAttached;

            mediumCapTop.gameObject.SetActive(!topAttached);
            mediumBoltsTop.gameObject.SetActive(topAttached);

            mediumCapBottom.gameObject.SetActive(!bottomAttached);
            mediumBoltsBottom.gameObject.SetActive(bottomAttached);
        }

        private void UpdateAttachment(ConstructionEventType data0, Part data1)
        {
            if (part != data1 && data1.parent != part && data1 != editorChild) return;
            if (data1.parent == part)
            {
                editorChild = data1;
            }
            UpdateAttachment();
        }

        #endregion
    }
}