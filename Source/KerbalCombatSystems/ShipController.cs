using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using static KerbalCombatSystems.Utils;
using KerbalCombatSystems.UI;
using KerbalCombatSystems.Data;

namespace KerbalCombatSystems
{
    public partial class ModuleShipController : PartModule
    {
        #region Fields

        // Ship state variables.

        public bool hasPropulsion;
        public bool hasWeapons;
        public bool hasControl;

        [KSPField(isPersistant = true)]
        public bool alive = true;

        [KSPField(isPersistant = true)]
        public Side side;

        public string Colour
            => SideColour(side);

        public List<ModuleWeaponController> incomingWeapons = new List<ModuleWeaponController>();

        public float heatSignature;


        // Vessel state variables.

        public float initialMass;

        private List<ModuleEngines> engines;
        private double maxThrust;

        public float averagedSize;
        public float shipLength;

        private float maxAcceleration;
        private float maxAngularAccCacheTime = 0;
        private Vector3 _maxAngularAcceleration = Vector3.zero;

        public Vector3 MaxAngularAcceleration
        {
            get
            {
                if (Time.unscaledTime - maxAngularAccCacheTime > 10)
                {
                    _maxAngularAcceleration = CalculateAngularAcceleration();
                    maxAngularAccCacheTime = Time.unscaledTime;
                }

                return _maxAngularAcceleration;
            }
        }

        public double TotalDeltaV =>
            vessel.VesselDeltaV.TotalDeltaVActual;


        // AI variables.

        [KSPField(isPersistant = true)]
        public bool controllerActive = false;

        public bool controllerRunning = false;
        public string state = "Offline";
        public bool withdrawalEnabled = true;
        public bool firingEnabled = true;
        public bool manoeuvringEnabled = true;
        public bool orbitCorrectionEnabled = true;

        private float updateInterval = combatUpdateInterval;
        private const float emergencyUpdateInterval = 0.5f;
        internal const float combatUpdateInterval = 2.5f;
        private float lastUpdate;

        private TooltipController tooltips;
        internal KCSFlightController fc;
        internal StatusChecker statusChecker;
        internal Targeting targeting;

        private Coroutine shipControllerCoroutine;
        private Coroutine behaviourCoroutine;
        private Coroutine missileCoroutine;
        private Coroutine interceptorCoroutine;

        // Movement.
        public static float approachingInterceptMargin = 1.5f;

        // Target.
        public Vessel Target =>
            targeting.target;

        internal ModuleShipController TargetController =>
            targeting.targetController;

        // Sensors.
        private List<ModuleObjectTracking> sensors = new List<ModuleObjectTracking>();
        private bool deployedSensors = false;
        internal float maxLockRange;

        // Robotics.
        private bool roboticsDeployed;
        private List<ModuleCombatRobotics> combatRobotics = new List<ModuleCombatRobotics>();

        // Weapons.
        internal float maxWeaponRange;
        private float lastFired = 0;
        public List<ModuleWeaponController> weapons;
        public ModuleWeaponController currentWeapon;

        // Interceptors.
        private List<ModuleWeaponController> interceptors = new List<ModuleWeaponController>();
        internal List<ModuleWeaponController> weaponsToIntercept = new List<ModuleWeaponController>();
        private float interceptorAcceleration = -1;

        // Evasion.
        internal List<Tuple<ModuleWeaponController, float>> dodgeWeapons = new List<Tuple<ModuleWeaponController, float>>();

        // Projectiles.
        private Part originalReferenceTransform;
        private ModuleWeaponController currentProjectile;
        public static float firingOffsetStrength = 2f;

        // Debugging
        internal float interceptStoppingDistance;
        internal float distanceToIntercept;
        internal float timeRemaining;

        #endregion


        #region Controller State & Start/Update

        [KSPEvent(
            guiActive = true,
            guiActiveEditor = false,
            guiName = "Enable AI",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName
        )]
        public void ToggleAI()
        {
            if (!controllerRunning) 
                StartAI();
            else 
                StopAI();

            Events[nameof(ToggleAI)].guiName = controllerRunning ? "Disable AI" : "Enable AI";
        }

        [KSPAction("Toggle AI")]
        public void ToggleAIAction(KSPActionParam _)
            => ToggleAI();

        [KSPAction("Activate AI")]
        public void ActivateAIAction(KSPActionParam _)
        {
            if (!controllerRunning)
                StartAI();
        }

        [KSPAction("Deactivate AI")]
        public void DeactivateAIAction(KSPActionParam _)
        {
            if (controllerRunning)
                StopAI();
        }

        public void StartAI()
        {
            if (!alive)
            {
                controllerRunning = false;
                controllerActive = false;
                return;
            }

            updateInterval = combatUpdateInterval;

            CheckWeapons();
            UpdateLockRange();
            UpdateSensorAnimations(true);

            controllerRunning = true;
            controllerActive = true;
            fc.lerpThrottle = true;
            shipControllerCoroutine = StartCoroutine(ShipController());
        }

        public void StopAI()
        {
            fc.lerpThrottle = false;
            fc.throttle = 0;
            fc.Drive();
            controllerRunning = false;
            controllerActive = false;
            targeting.ClearTarget();

            StopAllCoroutines();

            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);

            UpdateSensorAnimations(false);

            state = "Offline";
        }

        public override void OnAwake()
        {
            base.OnAwake();

            tooltips = new TooltipController(this);
            FlightManager.Register(this);
        }

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);
            UpgradeSettings();

            StartCoroutine(Initialise());
        }

        // Called when the flight scene is loaded, after all vessel setup is definitely complete.
        private IEnumerator Initialise()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();

            if (HighLogic.LoadedSceneIsFlight)
                StartFlight();
        }

        private void StartFlight()
        {
            // Components.

            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.alignmentToleranceforBurn = 7.5f;
            fc.throttleLerpRate = 3;

            statusChecker = ShipComponent.Create<StatusChecker>(this);
            targeting = new Targeting(this);

            // Initial state variables.
            Vector3 size = VesselBounds.GetBoundsLocal(vessel).size;
            shipLength = Mathf.Max(size.x, size.y, size.z);
            averagedSize = (size.x + size.y + size.z) / 3;
            initialMass = vessel.GetTotalMass();

            if (controllerActive)
                StartAI();
        }

        internal void FixedUpdate()
        {
            if (controllerRunning)
                fc.Drive();
        }

        internal void OnDestroy()
        {
            // Cleanup in all scenes.
            tooltips.Dispose();
            tooltips = null;

            if (!HighLogic.LoadedSceneIsFlight)
                return;

            // Cleanup from flight.

            Destroy(fc);
            Destroy(statusChecker);

            // Dereference. This causes instant de-allocation of memory.
            // Idk if it's really important overall.
            // I'm pretty sure it's not.
            targeting = null;
            statusChecker = null;
            fc = null;

            // Death message in the event that the controller part is destroyed.
            if (vessel != null
                && !vessel.packed
                && vessel.isActiveAndEnabled
                && alive)
            {
                alive = false;
                DeathMessage(true);
            }

            FlightManager.Unregister(this);
        }

        #endregion


        #region Main Functions/Loops

        private IEnumerator ShipController()
        {
            statusChecker.CheckStatus();
            if (!alive)
            {
                StopAI();
                yield break;
            }

            while (true)
            {
                lastUpdate = Time.time;
                updateInterval = incomingWeapons.Count > 0 ? emergencyUpdateInterval : combatUpdateInterval;

                // Find target.

                CheckWeapons();

                if (hasWeapons || Target != null)
                {
                    targeting.Update();
                    UpdateLockRange();
                    FindInterceptTarget();

                    // todo: this structure is stupid. we should be able to run these in parallel.
                    // doing them in sequence causes unresponsiveness even at low update intervals.

                    // Manually drive coroutines to avoid frames being eaten by yield break.
                    var interceptor = InterceptorFireControl();
                    while (interceptor.MoveNext()) yield return interceptor.Current;

                    var missile = MissileFireControl();
                    while (missile.MoveNext()) yield return missile.Current;
                }

                // Update behaviour tree for movement and projectile weapons.

                behaviourCoroutine = StartCoroutine(UpdateBehaviour());
                yield return behaviourCoroutine;
            }
        }

        private IEnumerator UpdateBehaviour()
        {
            // Movement.

            if (hasPropulsion)
            {
                UpdatePropulsionInfo();

                var waitForFixedUpdate = new WaitForFixedUpdate();
                bool hasTarget = Target != null;

                if (Target != null)
                    currentWeapon = GetPreferredWeapon(Target, weapons);

                if (useEvasion && CheckEvasion()) 
                {
                    // Evade an incoming missile.
                    state = "Evading";
                    
                    float previousTolerance = fc.alignmentToleranceforBurn;
                    bool lerpThrottle = fc.lerpThrottle;
                    fc.lerpThrottle = false;
                    fc.alignmentToleranceforBurn = 70;
                    fc.throttle = 1;

                    ModuleWeaponController incoming = dodgeWeapons[0].Item1;
                    Vector3 incomingVector;
                    Vector3 dodgeVector;
                    bool complete = false;

                    while (UnderTimeLimit() && incoming != null && !complete)
                    {
                        incomingVector = FromTo(vessel, incoming.vessel);
                        dodgeVector = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, incomingVector.normalized);
                        fc.attitude = dodgeVector;
                        fc.RCSVector = dodgeVector * 2;

                        yield return waitForFixedUpdate;
                        complete = Vector3.Dot(RelVel(vessel, incoming.vessel), incomingVector) < 0;
                    }

                    fc.RCSVector = Vector3.zero;
                    fc.throttle = 0;
                    fc.lerpThrottle = lerpThrottle;
                    fc.alignmentToleranceforBurn = previousTolerance;
                }
                else if (orbitCorrectionEnabled && OrbitDangerous(vessel.orbit)) 
                {
                    // Fix an immediately dangerous orbit.

                    yield return StartCoroutine(OrbitCorrection());
                }
                else if (withdrawalEnabled && KCSSaveSettings.AllowWithdrawal && !hasWeapons && CheckWithdraw())
                {
                    // Withdraw from combat.

                    if (state != "Withdrawing")
                        FlightManager.Log("%1 started to withdraw (out of weapons)", vessel);

                    state = "Withdrawing";

                    // Switch to passive robotics while withdrawing.
                    SetShipRobotics(false);

                    // Determine the direction.

                    var enemies = targeting.Enemies;
                    Vector3 averagePos = Vector3.zero;
                    if (enemies.Count > 1)
                    {
                        foreach (var enemy in enemies)
                            averagePos += FromTo(vessel, enemy.vessel).normalized;

                        averagePos /= enemies.Count;
                    }

                    Vector3 direction = enemies.Count > 1 ? -averagePos.normalized : vessel.ReferenceTransform.up;
                    Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                    bool facingNorth = Vector3.Angle(direction, orbitNormal) < 90;

                    // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.

                    Vector3 deltav = orbitNormal * (facingNorth ? 1 : -1) * 200;
                    float previousTolerance = fc.alignmentToleranceforBurn;
                    fc.throttle = 1;
                    fc.alignmentToleranceforBurn = 70;

                    while (deltav.magnitude > 10)
                    {
                        if (!hasPropulsion) break;

                        deltav -= Vector3.Project(vessel.acceleration, deltav) * TimeWarp.fixedDeltaTime;
                        fc.attitude = deltav.normalized;

                        yield return waitForFixedUpdate;
                    }

                    fc.alignmentToleranceforBurn = previousTolerance;
                    fc.throttle = 0;
                }
                else if (firingEnabled && hasTarget && HasLock() && CanFireProjectile(Target, out currentProjectile) && currentWeapon == currentProjectile)
                {
                    // Fire a statically mounted projectile.

                    Vector3 relVel = RelVel(vessel, Target);

                    if (relVel.magnitude > firingSpeed)
                    {
                        yield return StartCoroutine(KillVelocity());
                    }
                    else if (AngularVelocity(vessel, Target, 5f) > firingAngularVelocityLimit)
                    {
                        // Ideally we don't want to use an angular velocity limit, write better targeting.

                        state = "Manoeuvring (Kill Angular Velocity)";
                        bool complete = false;
                        float attitudeTolerance = fc.alignmentToleranceforBurn;
                        fc.alignmentToleranceforBurn = 45;
                        Vector3 vel, pos;

                        while (UnderTimeLimit() && Target != null && !complete)
                        {
                            complete = AngularVelocity(vessel, Target, 5f) < firingAngularVelocityLimit / 2;

                            vel = vessel.Vel(Target);
                            pos = vessel.Pos(Target);

                            fc.attitude = Vector3.ProjectOnPlane(vel, ClosestApproach(pos, vel));
                            fc.throttle = !complete ? 1 : 0;

                            yield return waitForFixedUpdate;
                        }

                        fc.alignmentToleranceforBurn = attitudeTolerance;
                    }
                    else
                    {
                        // Aim at target using current projectile weapon.
                        // The weapon handles firing.

                        state = "Firing Projectile";
                        fc.throttle = 0;
                        currentProjectile.target = Target;
                        currentProjectile.side = side;
                        fc.lerpAttitude = false;

                        if (!currentProjectile.setup)
                            currentProjectile.Setup();

                        // I was doing a turret check here before because the way it works is that
                        // the weapon takes control of the turret. But is that a good idea?
                        // Turret weapons should work in parallel with ship movement, not as part of it.

                        float alignment = Vector3.Dot(currentProjectile.AimPart.transform.up, vessel.ReferenceTransform.up);
                        if (alignment < 0.99f && !currentProjectile.fireSymmetry /*&& !currentProjectile.isTurret*/)
                        {
                            originalReferenceTransform = vessel.GetReferenceTransformPart();
                            vessel.SetReferenceTransform(currentProjectile.AimPart);
                        }

                        // todo: box raycast.

                        currentProjectile.targetSize = TargetController.averagedSize;
                      
                        while (UnderTimeLimit() && Target != null && currentProjectile.canFire)
                        {
                            Vector3 aim = currentProjectile.Aim();
                            fc.attitude = aim == Vector3.zero ? vessel.ReferenceTransform.up : aim;
                            fc.RCSVector = Vector3.ProjectOnPlane(RelVel(vessel, Target), FromTo(vessel, Target)) * -1;

                            relVel = Target.GetObtVelocity() - vessel.GetObtVelocity();
                            fc.throttle = Mathf.Clamp01(Mathf.Max(Vector3.Dot(relVel, vessel.ReferenceTransform.up), 0) / (maxAcceleration / firingOffsetStrength));

                            yield return waitForFixedUpdate;
                        }

                        if (!currentProjectile.canFire)
                            statusChecker.CheckStatus();

                        //if (!currentProjectile.isTurret)
                        if (originalReferenceTransform != null)
                            RestoreReferenceTransform();

                        fc.lerpAttitude = true;
                        fc.RCSVector = Vector3.zero;
                    }
                }
                else if (manoeuvringEnabled && hasTarget && weapons.Count > 0 && hasWeapons)
                {
                    // Combat Manoeuvering

                    // Deploy combat robotics.
                    SetShipRobotics(true);

                    float minRange = currentWeapon.MinMaxRange.x;
                    float maxRange = Mathf.Min(currentWeapon.MinMaxRange.y, TargetLockRange());
                    float currentRange = VesselDistance(vessel, Target);
                    bool complete = false;

                    if (currentRange < minRange)
                    {
                        if (AwayCheck(minRange))
                        {
                            state = "Manoeuvring (Away)";
                            fc.throttle = 1;
                            float oldAlignment = fc.alignmentToleranceforBurn;
                            fc.alignmentToleranceforBurn = 135;

                            while (UnderTimeLimit() && Target != null && !complete)
                            {
                                fc.attitude = FromTo(vessel, Target).normalized * -1;
                                fc.throttle = Vector3.Dot(RelVel(vessel, Target), fc.attitude) < manoeuvringSpeed ? 1 : 0;
                                complete = FromTo(vessel, Target).magnitude > minRange || !AwayCheck(minRange);

                                yield return waitForFixedUpdate;
                            }

                            fc.alignmentToleranceforBurn = oldAlignment;
                        }
                        else
                        {
                            state = "Manoeuvring (Drift Away)";

                            Vector3 toTarget;
                            fc.throttle = 0;

                            while (UnderTimeLimit() && Target != null && !complete)
                            {
                                toTarget = FromTo(vessel, Target);
                                complete = toTarget.magnitude > minRange;
                                fc.attitude = toTarget.normalized;

                                yield return waitForFixedUpdate;
                            }
                        }
                    }
                    else if (ApproachingIntercept(state.Contains("Kill Velocity") ? approachingInterceptMargin : 0))
                    {
                        // todo: needs a rethink.
                        // Why do we ever want to kill velocity outside of max range?
                        // What if I want to arrive at a certain range at a certain speed?
                        // What if the target is accelerating? What if the target isn't?

                        yield return StartCoroutine(KillVelocity());
                    }
                    else if (currentRange > maxRange
                        && CanInterceptShip(TargetController)
                        && !OnIntercept(state == "Manoeuvring (Intercept Target)" ? 0.05f : 0.25f))
                    {
                        // Enterance: wide tolerance.
                        // Exit: tight tolerance.

                        yield return StartCoroutine(InterceptTarget());
                    }
                    else
                    {
                        state = "Manoeuvring (Drift)";
                        fc.throttle = 0;
                        fc.attitude = vessel.ReferenceTransform.up;

                        do
                        {
                            // We are either in range of the target, or on a course to intercept within tolerance.
                            yield return waitForFixedUpdate;
                        } while (UnderTimeLimit() && !ApproachingIntercept());
                    }
                }
                else if (OrbitUnsafe(vessel.orbit))
                {
                    // Correct a non-immediately dangerous orbit when we're not doing anything else.

                    yield return StartCoroutine(OrbitCorrection());
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
                    SetShipRobotics(false);

                    yield return new WaitForSeconds(updateInterval);
                }
            }
            else
            {
                // We can't do anything because we don't have any engines.
                // todo: technically we could fire projectiles.

                state = "Stranded";
                fc.throttle = 0;
                fc.attitude = Vector3.zero;

                yield return new WaitForSeconds(updateInterval);
            }
        }

        private IEnumerator InterceptTarget()
        {
            state = "Manoeuvring (Intercept Target)";
            var wait = new WaitForFixedUpdate();

            Vector3 pos, vel, target, burn, delta, intercept;
            float burnTime;

            do
            {
                // Goal is to get within the range of the current weapon.
                // We have a preferred end of our range bracket.
                // We would like to get there at our manoeuvring speed.

                // BIG TODO: Very unhappy with intercept behaviour.
                // Needs fast iteration in Unity to get it right.
                // Ships will be AWFUL in this commit.

                pos = vessel.Pos(Target);
                vel = vessel.Vel(Target);
                target = Intercept(pos, vel);
                intercept = -target.normalized * manoeuvringSpeed;
                delta = intercept - vel;
                burnTime = delta.magnitude / maxAcceleration;
                burn = -(delta / burnTime);

                fc.attitude = burn.normalized;
                fc.throttle = 1;

                yield return wait;
            } while (UnderTimeLimit() && Target != null && !(OnIntercept(0.05f) || ApproachingIntercept()));
        }

        private IEnumerator KillVelocity()
        {
            state = "Manoeuvring (Kill Velocity)";

            Vector3 relVel;
            bool complete = false;
            var waitForFixedUpdate = new WaitForFixedUpdate();
            float alignmenmt = fc.alignmentToleranceforBurn;
            fc.alignmentToleranceforBurn = 45;

            float minimumSpeed = Mathf.Min(firingSpeed, maxAcceleration * 0.15f);
            float speedTargetSqr = Mathf.Pow(Mathf.Max(firingSpeed / 5, minimumSpeed), 2);


            while (UnderTimeLimit() && Target != null && !complete)
            {
                relVel = Target.GetObtVelocity() - vessel.GetObtVelocity();
                fc.attitude = (relVel + Target.perturbation).normalized;
                complete = relVel.sqrMagnitude < speedTargetSqr;
                fc.throttle = !complete ? 1 : 0;

                yield return waitForFixedUpdate;

                ApproachingIntercept(); // debug
            }

            fc.alignmentToleranceforBurn = alignmenmt;
        }

        private IEnumerator OrbitCorrection()
        {
            double UT;
            Orbit o = vessel.orbit;
            var waitForFixedUpdate = new WaitForFixedUpdate();
            double minSafeAltitude = MinSafeAltitude(vessel.mainBody);

            if (o.ApA < minSafeAltitude)
            {
                // Entirety of orbit is inside atmosphere, burn up until apoapsis is outside atmosphere by a 10% margin.

                state = "Correcting Orbit (Apoapsis too low)";
                fc.throttle = 1;

                while (UnderTimeLimit() && o.ApA < minSafeAltitude * 1.1)
                {
                    UT = Planetarium.GetUniversalTime();
                    fc.attitude = o.Radial(UT);
                    yield return waitForFixedUpdate;
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
                    yield return waitForFixedUpdate;
                }
            }
            else
            {
                // We are outside the atmosphere but our periapsis is inside the atmosphere/terrain.
                // Execute a burn (using mechjeb!) to raise our periapsis above the minimum altitude, with some margin.

                state = "Correcting Orbit (Raising Periapsis)";

                UT = Planetarium.GetUniversalTime();
                Vector3d target = DeltaVToChangePeriapsis(vessel.orbit, UT, vessel.mainBody.Radius + MinSafeAltitude(vessel.mainBody) + 10000f);
                bool complete = false;
                Vector3d totalPert = Vector3d.zero;
                Vector3d delta;
                float timeLimit = Mathf.Max((float)target.magnitude / maxAcceleration * 1.1f, updateInterval);

                while (UnderTimeLimit(timeLimit) && !complete)
                {
                    //deltaV = target - vessel.GetObtVelocity();
                    totalPert += vessel.perturbation * Time.fixedDeltaTime;
                    delta = target - totalPert;

                    fc.attitude = delta.normalized;
                    fc.throttle = Mathf.Clamp01(0.05f + (float)delta.magnitude / (maxAcceleration * 0.1f));

                    yield return waitForFixedUpdate;

                    complete = delta.magnitude < maxAcceleration * 0.2f;
                }
            }

            fc.throttle = 0;
        }

        public IEnumerator MissileFireControl()
        {
            bool canFire =
                Target != null
                && weapons.Count > 0
                && Time.time - lastFired > firingInterval
                && HasLock();

            if (!canFire)
                yield break;

            List<ModuleWeaponController> missiles = GetAvailableMissiles(Target);
            var preferred = GetPreferredWeapon(Target, missiles);
            if (preferred == null)
                yield break;

            lastFired = Time.time;
            bool checkWeapons = false;
            float targetMass = (float)Target.totalMass;

            // Decide how many missiles to use based on the mass of the missile we want to use, the mass of the target,
            // and the mass of the weapons already on their way to the target.

            if (TargetController.incomingWeapons.Count > 0)
            {
                targetMass = (float)Target.totalMass - TargetController.incomingWeapons.Sum(w => w.mass * w.targetMassRatio);
                if (targetMass < ((preferred.mass * 1.2f) * preferred.targetMassRatio))
                    yield break;
            }

            int salvoCount = (int)Mathf.Max(Mathf.Floor(targetMass / (preferred.mass * preferred.targetMassRatio)), 1);
            salvoCount = Mathf.Min(salvoCount, missiles.Count);
            salvoCount = Mathf.Min(salvoCount, (int)maxSalvoSize);

            List<ModuleWeaponController> salvo = GetPreferredWeapon(Target, missiles, salvoCount);
            ModuleWeaponController last = salvo.Last();

            // Make a log entry.

            bool single = salvo.Count == 1;
            string missileName = preferred.weaponCode == "" ? "missile" : preferred.weaponCode;
            string pluraliser = missileName.ToLower().Last() == 's' ? "'" : "s";

            if (!single)
                FlightManager.Log($"%1 fired a salvo of {salvo.Count} {missileName}{pluraliser} at %2", vessel, Target);
            else
                FlightManager.Log($"%1 fired a {missileName} at %2", vessel, Target);

            // Trigger robotics.

            var roboticsCodes = salvo.Select(w => w.weaponCode).Distinct();
            float roboticsDuration = HandleWeaponRobotics(roboticsCodes, true);
            if (roboticsDuration > 0)
                yield return new WaitForSeconds(roboticsDuration);

            // Fire each missile.

            foreach (ModuleWeaponController weapon in salvo)
            {
                if (weapon == null || weapon.vessel != vessel)
                    continue;

                checkWeapons = true;

                weapon.target = Target;
                weapon.side = side;
                weapon.Fire();

                FlightManager.weaponsInFlight.Add(weapon);
                TargetController.AddIncoming(weapon);

                // If the missile is not radial (it's inside a bay or in front of the ship),
                // we need to keep the ship still until the missile is actually launched,
                // unless we're evading in which case we're better off moving.

                if (weapon.launchType != LaunchType.Radial && state != "Evading")
                {
                    float launchTime = Time.time;

                    state = "Launching Missile";
                    yield return StartCoroutine(WaitForLaunch(weapon, weapon.salvoSpacing * 2));
                    yield return new WaitForSeconds(Mathf.Max(weapon.salvoSpacing - (Time.time - launchTime), 0));
                }
                else if (weapon != last)
                    yield return new WaitForSeconds(weapon.salvoSpacing);
            }

            // Retract robotics.
            HandleWeaponRobotics(roboticsCodes, false);

            if (checkWeapons)
                CheckWeapons();
        }

        private IEnumerator InterceptorFireControl()
        {
            if (weaponsToIntercept.Count < 1 || interceptors.Count < 1)
                yield break;

            bool checkWeapons = false;
            ModuleWeaponController interceptor;

            // Make a log entry.

            int count = Mathf.Min(weaponsToIntercept.Count, interceptors.Count);
            string interceptorString = count > 1 ? $"{count} interceptors" : "an interceptor";
            FlightManager.Log($"%1 launched {interceptorString}", vessel);

            // Trigger robotics.

            var roboticsCodes = interceptors.Select(w => w.weaponCode).Distinct();
            float roboticsDuration = HandleWeaponRobotics(roboticsCodes, true);
            if (roboticsDuration > 0)
                yield return new WaitForSeconds(roboticsDuration);

            // Fire each interceptor.
            var lastTarget = weaponsToIntercept.Last();

            foreach (var interceptTarget in weaponsToIntercept)
            {
                if (interceptors.Count < 1)
                    break;

                interceptor = GetPreferredWeapon(interceptTarget.vessel, interceptors, 1).First();
                if (interceptor == null)
                    continue;

                checkWeapons = true;

                interceptors.Remove(interceptor);
                interceptor.isInterceptor = true;
                interceptor.targetWeapon = interceptTarget;
                interceptor.target = interceptTarget.vessel;
                interceptor.side = side;
                interceptor.Fire();

                interceptTarget.interceptedBy.Add(interceptor);
                FlightManager.interceptorsInFlight.Add(interceptor);

                if (interceptor.launchType != LaunchType.Radial && state != "Evading")
                {
                    state = "Launching Interceptor";
                    yield return StartCoroutine(WaitForLaunch(interceptor, interceptor.salvoSpacing * 2));
                }
                else if (interceptTarget != lastTarget)
                    yield return new WaitForSeconds(interceptor.salvoSpacing);
            }

            if (checkWeapons)
                CheckWeapons();

            // Retract robotics.
            HandleWeaponRobotics(roboticsCodes, false);
        }

        #endregion


        #region Utility Functions

        private bool UnderTimeLimit(float timeLimit = 0)
        {
            if (timeLimit == 0)
                timeLimit = updateInterval;

            timeRemaining = timeLimit - (Time.time - lastUpdate);

            return Time.time - lastUpdate < timeLimit;
        }

        private void UpdatePropulsionInfo()
        {
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            maxThrust = GetMaxThrust(engines);
            maxAcceleration = (float)(maxThrust / vessel.totalMass);
        }

        #region Weapons

        public void CheckWeapons()
        {
            // Find all on-board weapons.
            if (FlightManager.weaponControllers.Count < vessel.parts.Count)
                weapons = FlightManager.weaponControllers.FindAll(w => w.vessel == vessel);
            else
                weapons = vessel.FindPartModulesImplementing<ModuleWeaponController>();

            // Store the max weapon range for the overlay.
            if (weapons.Count < 1)
            {
                interceptors.Clear();
                maxWeaponRange = 0;
                return;
            }

            // Store the max weapon range for the overlay.
            maxWeaponRange = weapons.Max(w => w.MinMaxRange.y);

            // Find all on-board interceptors.
            interceptors = weapons.FindAll(w => w.useAsInterceptor);

            // Store the expected interceptor acceleration for CanIntercept calculations.
            if (interceptors.Count > 0 && interceptorAcceleration < 0)
            {
                var firstInterceptor = interceptors.OrderBy(i => i.childDecouplers).First();
                interceptorAcceleration = firstInterceptor.CalculateAcceleration();
            }
        }

        private static bool WeaponIsChild(ModuleWeaponController weapon, ModuleWeaponController otherWeapon)
        {
            // Check if a weapon is in a stack below another weapon.
            // If it is, it should always be fired before the parent weapon.

            // todo: solidify decoupler reference so that it only happens once.
            var dec = FindDecoupler(weapon.part);
            if (dec == null)
                return false;

            return dec.part.FindChildParts<Part>(true).Contains(otherWeapon.part);
        }

        private List<ModuleWeaponController> RankWeapons(Vessel target, List<ModuleWeaponController> weapons)
        {
            float targetMass = (float)target.totalMass;

            // Order the available weapons based on the suitability of the their mass compared to the target. 
            return weapons
                .OrderBy(w => Mathf.Abs(targetMass - (w.mass * w.targetMassRatio)))
                .Where(w => w.canFire)
                .ToList();
        }

        private ModuleWeaponController GetPreferredWeapon(Vessel target, List<ModuleWeaponController> weapons)
        {
            // Used for pre-emptively selecting a weapon for a given target.
            // So that we can determine range and salvo size before firing.

            if (weapons.Count < 1)
                return null;

            return RankWeapons(target, weapons).First();
        }

        private List<ModuleWeaponController> GetPreferredWeapon(Vessel target, List<ModuleWeaponController> weapons, int count)
        {
            // Build an ordered, ready to fire salvo list, based on the suitability of the weapon for the target.

            // todo: this probably works with different types of missile,
            // but it sort of assumes that the final salvo is built entirely out of one type.
            // todo: reduce allocation, reduce LINQ.

            if (weapons.Count < 1)
                return null;

            var weaponsRanked = RankWeapons(target, weapons);

            // Select the most suitable weapons.
            weaponsRanked = weaponsRanked.Take(count).ToList();

            // For each weapon that has already been selected, add any identical weapons to the list.
            var identicalWeapons = new List<ModuleWeaponController>();

            foreach (var selectedWeapon in weaponsRanked)
                identicalWeapons.AddRange(weapons.FindAll(w => (selectedWeapon.Identical(w) || WeaponIsChild(selectedWeapon, w)) && !weaponsRanked.Contains(w) && !identicalWeapons.Contains(w)));
            
            weaponsRanked.AddRange(identicalWeapons);

            // Make doubly sure there aren't any duplicate entries.
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

        private bool CanFireProjectile(Vessel target, out ModuleWeaponController weapon)
        {
            // Check if we can enter the firing behaviour.
            // And update the selected projectile weaponController to use in the process.

            float targetRange = FromTo(vessel, target).magnitude;

            var available = weapons.Where(w =>
                ModuleWeaponController.projectileTypes.Contains(w.weaponType)
                && (targetRange > w.MinMaxRange.x || w.weaponType == "Firework")
                && targetRange < w.MinMaxRange.y
                && w.canFire
            );

            weapon = available.FirstOrDefault();
            return weapon != null;
        }

        #endregion

        private Vector3 CalculateAngularAcceleration()
        {
            // todo: this is utterly brain dead. And how it's being used is brain dead too.
            // when this gets refactored I need to look at mechjeb, and move it into VesselStatus
            // could this be leading to problems in ApproachingIntercept()?

            Vector3 availableTorque = Vector3.zero;
            var reactionWheels = vessel.FindPartModulesImplementing<ModuleReactionWheel>();

            foreach (var wheel in reactionWheels)
            {
                wheel.GetPotentialTorque(out Vector3 pos, out pos);
                availableTorque += pos;
            }

            return AngularAcceleration(availableTorque, vessel.MOI);
        }

        private bool CheckWithdraw()
        {
            var nearest = targeting.GetNearestEnemy();
            if (nearest == null) return false;

            return Mathf.Abs(RelVel(vessel, nearest.vessel).magnitude) < 200;
        }

        #region Sensors

        internal void UpdateLockRange()
        {
            // todo: this includes a whole ship loop. check for sensors (and similar parts) from vessel/parts changed events.
            // have sensors self report to a vessel module ASAP.
            sensors.Clear();
            sensors.AddRange(vessel.FindPartModulesImplementing<ModuleObjectTracking>());

            if (sensors.Count < 1)
                maxLockRange = 1000;
            else
                maxLockRange = sensors.Max(s => s.detectionRange);
        }

        internal void UpdateSensorAnimations(bool state)
        {
            if (sensors.Count < 1)
                return;

            if (deployedSensors == state)
                return;

            ModuleAnimationGroup anim;
            foreach (ModuleObjectTracking sensor in sensors)
            {
                if (!sensor.animate) continue;

                anim = sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                if (anim == null) continue;

                if (state == anim.isDeployed) continue;
                if (state) anim.DeployModule(); else anim.RetractModule();
            }

            deployedSensors = state;
        }

        private float TargetLockRange()
        {
            // todo: This is voo-doo. I should pay attention to in-game results.
            return maxLockRange * Mathf.Clamp(TargetController.heatSignature / 1500, 0.5f, 3.0f);
        }

        private bool HasLock()
        {
            return VesselDistance(vessel, Target) < TargetLockRange();
        }

        public float CalculateHeatSignature()
        {
            float hottestPartTemp = (float)vessel.parts.Max(p => (p.skinTemperature + p.temperature) / 2);
            heatSignature = hottestPartTemp * averagedSize;
            return heatSignature;
        }

        #endregion

        private void FindInterceptTarget()
        {
            if (interceptors.Count < 1 || FlightManager.weaponsInFlight.Count < 1)
            {
                if (weaponsToIntercept.Count > 0)
                    weaponsToIntercept.Clear();

                return;
            }

            weaponsToIntercept = FlightManager.weaponsInFlight.FindAll(
                w =>
                w != null
                && w.vessel != null
                && w.launched
                && !w.missed
                && w.side != side
                && w.interceptedBy.Count < 1
                && VesselDistance(w.vessel, vessel) < maxLockRange
                && CanInterceptWeapon(w));

            weaponsToIntercept = weaponsToIntercept.OrderBy(w => VesselDistance(w.vessel, vessel)).ToList();

            var priorityIntercept = weaponsToIntercept.FindAll(w => w.target == vessel);
            if (priorityIntercept.Count > 0)
            {
                weaponsToIntercept = weaponsToIntercept.Except(priorityIntercept).ToList();
                weaponsToIntercept = priorityIntercept.Concat(weaponsToIntercept).ToList();
            }
        }

        private bool CanInterceptWeapon(ModuleWeaponController weaponModule)
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

            // We can be sure that we can perform an intercept if we're the target
            // and the missile is at least several seconds out.
            if (target == vessel && weaponModule.timeToHit > 3)
                return true;

            Vector3 weaponToTarget = target.CoM - weapon.CoM;
            Vector3 weaponAccVector = weaponToTarget.normalized * weaponModule.Missile.maxAcceleration;
            Vector3 weaponRelVel = target.GetObtVelocity() - weapon.GetObtVelocity();

            // Exit if the missile is not actually going towards the target.
            if (Vector3.Dot(weaponToTarget, weaponRelVel * -1) < 0)
                return false;

            Vector3 intToTarget = target.CoM - vessel.CoM;
            Vector3 intAccVector = intToTarget.normalized * interceptorAcceleration;

            float timeToIntercept = ClosestTimeToCPA(intToTarget, target.GetObtVelocity() - vessel.GetObtVelocity(), target.acceleration - intAccVector);

            // We can't use weaponModule.timeToHit because it uses a less accurate method than ClosestTimeToCPA.
            float weaponTime = ClosestTimeToCPA(weaponToTarget, weaponRelVel, target.acceleration - weaponAccVector);

            // Can the interceptor get to the missile's *target* before the missile does?
            // Anything slower than this will fail and anything faster can be expected
            // to intercept the missile at some time before it hits the target.
            return timeToIntercept + 0.5f < weaponTime && weaponTime > 3;
        }

        #region Orbit Correction

        internal static bool OrbitUnsafe(Orbit orbit)
        {
            // Is this orbit non-permanent?
            // Exception: the orbit is non-permanent but at current t it's ascending.

            // ! If this were to be used anywhere other than the ship controller it would need to include OrbitDangerous().
            // ! We can skip it currently because of the logic of the ship controller.

            Orbit o = orbit;
            double minSafeAltitude = MinSafeAltitude(o.referenceBody);
            bool orbitUnsafe = (o.PeA < minSafeAltitude && o.timeToPe < o.timeToAp) || o.ApA < minSafeAltitude;

            return orbitUnsafe;
        }

        private static double MinSafeAltitude(CelestialBody body)
        {
            // Thanks Josue.
            double maxTerrainHeight = 200;
            if (body.pqsController)
            {
                PQS pqs = body.pqsController;
                maxTerrainHeight = pqs.radiusMax - pqs.radius;
            }

            return Math.Max(maxTerrainHeight, body.atmosphereDepth);
        }

        internal bool OrbitDangerous(Orbit orbit)
        {
            // Question: Is this orbit unrecoverable for THIS vessel, or immediately dangerous?

            var body = orbit.referenceBody;

            // Is this orbit currently inside the atmosphere or below max terrain height?
            double minSafeAltitude = MinSafeAltitude(body);
            if (orbit.altitude < minSafeAltitude)
                return true;

            // Is the periapsis inside the atmosphere/terrain?
            if (orbit.PeA > minSafeAltitude)
                return false;

            // We would have no chance of recovering the orbit.
            if (Mathf.Approximately(maxAcceleration, 0))
                return true;

            // Orbit currently outside the atmosphere.
            // Orbit is unsafe (periapsis inside atmosphere/terrain).

            // Would we be able to correct this orbit NOW?
            double UT = Planetarium.GetUniversalTime();
            Vector3d deltaV = DeltaVToChangePeriapsis(orbit, UT, body.Radius + minSafeAltitude);

            // TotalDeltaV could be 10 seconds out of date, so subtract 10 seconds worth.
            // Also include a 5% margin.

            return deltaV.magnitude > (TotalDeltaV - maxAcceleration * 10) * 0.95f;
        }

        #endregion

        #region Combat Movement

        public float BurnTime(double deltaV, double totalConsumption)
        {
            // Copied from BetterBurnTime. Agrees with VesselDeltaV.

            double exhaustVelocity = maxThrust / totalConsumption; // meters/second
            double massRatio = Math.Exp(deltaV / exhaustVelocity);
            double currentTotalShipMass = vessel.totalMass;
            double fuelMass = currentTotalShipMass * (1.0 - 1.0 / massRatio);
            double burnTimeNeeded = fuelMass / totalConsumption;

            return (float)burnTimeNeeded;
        }

        private float StoppingDistance(float speed)
        {
            // Calculate stopping distance for a given speed,
            // accounting for the increase in acceleration as fuel is consumed.

            // Time.
            double consumptionRate = ModuleRocket.GetConsumptionRate(engines);
            float timeToKillVelocity = (float)BurnTime(speed, consumptionRate);

            // Acceleration.
            float acceleration = maxAcceleration;

            // Jerk.
            double accelerationNext = maxThrust / (vessel.totalMass - consumptionRate);
            float jerk = (float)accelerationNext - acceleration;

            return Displacement(speed, -acceleration, -jerk, timeToKillVelocity);
        }

        private bool ApproachingIntercept(float margin = 0)
        {
            // todo: this is not working as intended presumably because time to cpa is wrong under certain conditions.
            // because we're at our cpa and heading away?
            // something to fix in Unity.

            // Do we need to switch from intercepting to start killing velocity?
            Vector3 velocity = vessel.Vel(Target); 
            Vector3 position = vessel.Pos(Target);

            // Return false if we aren't headed towards the target.
            float speedToTarget = Vector3.Dot(velocity, -position.normalized);
            if (speedToTarget < 10)
                return false;

            // todo: Do we actually have an intercept? Is there a simple way to rule out false conditions here?
            // presumably OnIntercept, but does that cover all scenarios?

            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, velocity.normalized) * Mathf.Deg2Rad;
            float timeToRotate = SolveTime(rotDistance * 0.75f, MaxAngularAcceleration.magnitude) / 0.75f; // nonsense.
            float distanceToKillVelocity = StoppingDistance(velocity.magnitude);

            float totalDistance = distanceToKillVelocity + velocity.magnitude * (margin + timeToRotate * 3);
            float distanceToCPA = Intercept(position, velocity).magnitude;
            
            // Debug.
            interceptStoppingDistance = totalDistance;
            distanceToIntercept = distanceToCPA;

            return distanceToCPA < totalDistance;
        }

        internal bool CanInterceptShip(ModuleShipController target)
        {
            // Is it worth us chasing a withdrawing ship?

            Vector3 toTarget = vessel.Pos(target.vessel);
            bool escaping = target.state.Contains("Withdraw") || target.state.Contains("Idle (Unarmed)");

            if (!escaping) // It is not trying to escape.
                return true;

            if (toTarget.magnitude < maxWeaponRange) // It is already in range.
                return true;

            if (maxAcceleration > target.maxAcceleration) // We are faster.
                return true;

            if (Vector3.Dot(vessel.Vel(target.vessel), toTarget) < 0) // It is getting closer
                return true;

            return false;
        }

        private bool OnIntercept(float tolerance)
        {
            // Question: Are we already on our desired intercept course with the current target, within tolerance?

            if (Target == null)
                return false;

            Vector3 pos = vessel.Pos(Target);
            Vector3 vel = vessel.Vel(Target);

            // Are we getting closer or further away?
            bool approaching = Vector3.Dot(pos, vel) < 0;
            if (!approaching)
                return false;

            bool cpaInTolerance = ClosestApproach(pos, vel).magnitude < InterceptionRange() * (tolerance + 1);
            bool speedInTolerance = Mathf.Abs(vel.magnitude - manoeuvringSpeed) < manoeuvringSpeed * tolerance;

            return cpaInTolerance && speedInTolerance;
        }

        private bool AwayCheck(float minRange)
        {
            // Check if we need to manually burn away from an enemy that's too close or
            // if it would be better to drift away.

            Vector3 pos = vessel.Pos(Target);
            Vector3 vel = vessel.Vel(Target);
            Vector3 away = pos.normalized * -1;

            // another bs rotation calc.
            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, away) * Mathf.Deg2Rad;
            float timeToRotate = SolveTime(rotDistance / 2, MaxAngularAcceleration.magnitude) * 2;

            float timeToDisplace = SolveTime(minRange - pos.magnitude, maxAcceleration, Vector3.Dot(vel * -1, away));
            float timeToEscape = timeToRotate * 2 + timeToDisplace;

            // Do we need to take action or can we just drift in the same amount of time?
            return PredictPosition(pos, vel, timeToEscape).magnitude < minRange;
        }

        private float InterceptionRange()
        {
            float rangeCoef = currentWeapon.weaponType == "Missile" ? 0.75f : 0.25f;
            Vector2 rangeBracket = currentWeapon.MinMaxRange;
            float weaponRange = rangeBracket.x + (rangeBracket.y - rangeBracket.x) * rangeCoef;

            return Mathf.Min(weaponRange, TargetLockRange());
        }

        private Vector3 Intercept(Vector3 pos, Vector3 vel)
        {
            return pos + Vector3.ProjectOnPlane(-vel, pos).normalized * InterceptionRange();
        }

        #endregion

        #region Robotics

        public void SetShipRobotics(bool deploy)
        {
            if (deploy == roboticsDeployed)
                return;

            roboticsDeployed = deploy;
            var controllers = vessel.FindPartModulesImplementing<ModuleCombatRobotics>();

            foreach (var combatRobotic in controllers)
                if (combatRobotic.roboticsType == "Ship")
                    combatRobotic.Set(deploy);
        }

        public void SetWeaponRobotics(bool deploy, string weaponCode, out float duration)
        {
            string code = weaponCode.ToLower();
            duration = 0f;

            foreach (ModuleCombatRobotics combatRobotic in combatRobotics)
            {
                if (combatRobotic.roboticsType != "Weapon")
                    continue;

                if (combatRobotic.Tag.ToLower() != code) // could be made to include default robotics name.
                    continue;

                duration = Mathf.Max(duration, combatRobotic.Duration);
                combatRobotic.Set(deploy);
            }
        }

        private float HandleWeaponRobotics(IEnumerable<string> codes, bool deploy)
        {
            float roboticsDuration = 0;
            combatRobotics = vessel.FindPartModulesImplementing<ModuleCombatRobotics>(); // cringe.

            foreach (var code in codes)
            {
                if (code == "")
                    continue;

                SetWeaponRobotics(deploy, code, out float duration);
                roboticsDuration = Mathf.Max(roboticsDuration, duration);
            }

            return roboticsDuration;
        }

        #endregion

        private bool CheckEvasion()
        {
            if (incomingWeapons.Count < 1)
                return false;

            if (float.IsInfinity(maxAcceleration) || maxAcceleration == 0)
                return false;

            float timeToDisplace = SolveTime(shipLength, maxAcceleration);
            if (float.IsInfinity(timeToDisplace))
                return false;

            dodgeWeapons.Clear();
            RefreshIncoming();

            Vessel incoming;
            Vector3 attitude = vessel.transform.up;
            Vector3 pos, vel;
            bool onCollisionCourse;

            foreach (var controller in incomingWeapons)
            {
                incoming = controller.vessel;
                pos = vessel.Pos(incoming);
                vel = vessel.Vel(incoming);

                onCollisionCourse = Vector3.Dot(pos.normalized, -vel.normalized) > 0.7;
                if (!onCollisionCourse)
                    continue;

                // once again bs rotation calc.
                Vector3 perpendicular = Vector3.ProjectOnPlane(attitude, pos.normalized);
                float rotDistance = Vector3.Angle(attitude, perpendicular) * Mathf.Deg2Rad;
                float timeToRotate = SolveTime(rotDistance / 2, MaxAngularAcceleration.magnitude) * 2;
                float timeToDodge = timeToRotate + timeToDisplace;

                float timeToHit = SolveTime(pos.magnitude, 
                    Vector2.Dot(-(Vector3)incoming.perturbation, pos.normalized), 
                    Vector3.Dot(-vel, pos.normalized));

                // If the time to hit is greater than the time it would take us to dodge it with some margin, ignore it.
                if (timeToHit > Mathf.Max(timeToDodge * 1.25f, updateInterval * 2))
                    continue;

                dodgeWeapons.Add(new Tuple<ModuleWeaponController, float>(controller, timeToHit));
            }

            // Sort by time to hit, ascending.
            dodgeWeapons.Sort((x, y) => x.Item2.CompareTo(y.Item2));

            return dodgeWeapons.Count > 0;
        }

        public void AddIncoming(ModuleWeaponController wep)
        {
            incomingWeapons.Add(wep);
        }

        private IEnumerator WaitForLaunch(ModuleWeaponController weapon, float timeLimit)
        {
            bool lerpThrottle = fc.lerpThrottle;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.lerpThrottle = false;
            fc.throttle = 0;

            var wait = new WaitForFixedUpdate();
            while (!weapon.launched && UnderTimeLimit(timeLimit))
                yield return wait;

            fc.lerpThrottle = lerpThrottle;
        }

        public static string SideColour(Side side)
        {
            return side == Side.A ? "#0AACE3" : "#E30A0A";
        }

        public void ToggleSide()
        {
            if (side == Side.A)
                side = Side.B;
            else
                side = Side.A;

            FlightManager.UpdateTargeting();
        }

        internal void DeathMessage(bool noController = false)
        {
            var reasons = new List<string>();
            if (!hasWeapons) reasons.Add("no weapons");
            if (!hasPropulsion) reasons.Add("no propulsion");
            if (!hasControl) reasons.Add("no control");
            if (noController) reasons.Add("no AI");
            string reason = string.Join(", ", reasons);

            FlightManager.Log($"<b>%1 was disabled ({reason})</b>", vessel);
        }

        internal void RestoreReferenceTransform()
        {
            vessel.SetReferenceTransform(originalReferenceTransform);
            originalReferenceTransform = null;
        }

        internal void RefreshIncoming()
        {
            incomingWeapons.RemoveAll(w => w == null || w.missed);
        }

        #endregion
    }
}