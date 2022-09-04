using System;
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
        public float updateInterval = 2f;
        public float firingAngularVelocityLimit = 1; // degrees per second

        // Ship AI variables.

        private KCSController controller;
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
        private const float firingInterval = 7.5f;

        private float maxDetectionRange;
        public float initialMass;
        private bool hasPropulsion;
        private bool hasWeapons;
        private float maxAcceleration;
        private bool roboticsDeployed;
        private float shipLength;
        private Vector3 maxAngularAcceleration;
        private double minSafeAltitude;
        private int updateCount;
        public float heatSignature;
        private float averagedSize;
        private float interceptorAcceleration = -1;

        Part editorChild;

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
                minValue = 1f,
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

        // todo: find option without green bar
        [KSPField(
            isPersistant = true, 
            guiActive = true, 
            guiActiveEditor = true, 
            guiName = "Withdrawing Enemies",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_ChooseOption(controlEnabled = true, affectSymCounterparts = UI_Scene.None, 
            options = new string[] { "De-prioritised", "Chase", "Ignore" })]
        public string withdrawingPriority = "De-prioritised";

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
                controller = FindObjectOfType<KCSController>();

                Vector3 size = vessel.vesselSize;
                shipLength = (new[] { size.x, size.y, size.z}).ToList().Max();
                averagedSize = (size.x + size.y + size.z) / 3;
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
            foreach(var wheel in reactionWheels)
            {
                Vector3 pos;
                wheel.GetPotentialTorque(out pos, out pos);
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
                    StopAI();
                    FireEscapePods();
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

                UpdateDetectionRange();
                FindTarget();
                FindInterceptTarget();

                // Missiles.

                CheckWeapons();

                interceptorCoroutine = StartCoroutine(InterceptorFireControl());
                yield return interceptorCoroutine;

                missileCoroutine = StartCoroutine(MissileFireControl());
                yield return missileCoroutine;

                // Update behaviour tree for movement and projectile weapons.

                behaviourCoroutine = StartCoroutine(UpdateBehaviour());
                yield return behaviourCoroutine;
            }
        }

        private IEnumerator UpdateBehaviour()
        {
            maxAcceleration = GetMaxAcceleration(vessel);

            updateCount++;
            //Debug.Log($"[KCS]: Update {updateCount} for {vessel.GetDisplayName()}.");

            // Movement.
            if (hasPropulsion && !hasWeapons && CheckWithdraw())
            {
                state = "Withdrawing";

                // Switch to passive robotics while withdrawing.
                UpdateRobotics(false);

                // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.

                Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                bool facingNorth = Vector3.Angle(vessel.ReferenceTransform.up, orbitNormal) < 90;
                Vector3 deltav = orbitNormal * (facingNorth ? 1 : -1) * 200;
                fc.throttle = 1;

                while (deltav.magnitude > 10)
                {
                    if (!hasPropulsion) break;

                    deltav = deltav - (Vector3.Project(vessel.acceleration, deltav) * TimeWarp.fixedDeltaTime);
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
                bool complete = false;

                while (UnderTimeLimit() && incoming != null && !complete)
                {
                    incomingVector = FromTo(vessel, incoming.vessel);
                    fc.attitude = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, incomingVector.normalized);

                    yield return new WaitForFixedUpdate();
                    complete = Vector3.Dot(RelVel(vessel, incoming.vessel), incomingVector) < 0;
                }

                fc.alignmentToleranceforBurn = previousTolerance;
            }
            else if (target != null && HasLock() && CanFireProjectile(target) && AngularVelocity(vessel, target) < firingAngularVelocityLimit)
            {
                // Aim at target using current projectile weapon.
                // The weapon handles firing.

                state = "Firing Projectile";
                fc.throttle = 0;
                currentProjectile.target = target;
                currentProjectile.side = side;

                // Temporarily disabled.
                if (currentProjectile.weaponType == "Rocket")
                {
                    yield return new WaitForSeconds(updateInterval);
                    yield break;
                }

                while (UnderTimeLimit() && target != null && currentProjectile.canFire)
                {
                    fc.attitude = currentProjectile.Aim();
                    // todo: correct for relative and angular velocity while firing if firing at an accelerating target

                    yield return new WaitForFixedUpdate();
                }
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
            else if (target != null && weapons.Count > 0 && hasWeapons)
            {
                // todo: implement for longer range movement.
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebModuleRendezvousAutopilot.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/OrbitalManeuverCalculator.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebLib/Maths/Gooding.cs

                // Deploy combat robotics.
                UpdateRobotics(true);

                ModuleWeaponController currentWeapon = GetPreferredWeapon(target, weapons);
                float minRange = currentWeapon.MinMaxRange.x;
                float maxRange = currentWeapon.MinMaxRange.y;
                float currentRange = VesselDistance(vessel, target);
                bool complete = false;
                Vector3 relVel = RelVel(vessel, target);

                if (currentRange < minRange)
                {
                    state = "Manoeuvring (Away)";
                    fc.throttle = 1;
                    float oldAlignment = fc.alignmentToleranceforBurn;
                    fc.alignmentToleranceforBurn = 45;

                    while (UnderTimeLimit() && target != null && !complete)
                    {
                        fc.attitude = FromTo(vessel, target).normalized * -1;
                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;
                        complete = FromTo(vessel, target).magnitude > minRange;

                        yield return new WaitForFixedUpdate();
                    }

                    fc.alignmentToleranceforBurn = oldAlignment;
                }
                else if (currentRange > maxRange && !NearIntercept(relVel, minRange))
                {
                    float angle;

                    while (UnderTimeLimit() && target != null && !complete)
                    {
                        relVel = RelVel(vessel, target);
                        Vector3 targetVec = ToClosestApproach(relVel, minRange * 1.2f).normalized;
                        angle = Vector3.Angle(relVel.normalized, targetVec);

                        if (angle > 45 && relVel.magnitude > maxAcceleration * 2)
                        {
                            state = "Manoeuvring (Match Velocity)";
                            fc.attitude = relVel.normalized * -1;
                        }
                        else
                        {
                            state = "Manoeuvring (Prograde to Target)";
                            fc.attitude = Vector3.LerpUnclamped(relVel.normalized, targetVec, 1 + 1 * (relVel.magnitude / maxAcceleration));
                        }

                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;
                        complete = FromTo(vessel, target).magnitude < maxRange;

                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    if (relVel.magnitude > firingSpeed)
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

                        /*while (UnderTimeLimit() && target != null)
                        {
                            fc.attitude = Vector3.ProjectOnPlane(FromTo(vessel, target).normalized, RelVel(vessel, target));

                            yield return new WaitForFixedUpdate();
                        }*/
                    }
                }
            }
            else
            {
                // Idle

                state = "Idling";

                fc.throttle = 0;
                fc.attitude = Vector3.zero;

                // Switch to passive robotics.
                UpdateRobotics(false);

                yield return new WaitForSeconds(updateInterval);
            }
        }

        public IEnumerator MissileFireControl()
        {
            if (target != null && weapons.Count > 0 && Time.time - lastFired > firingInterval && HasLock())
            {
                //float targetMass = targetController.initialMass;

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

                Debug.Log($"[KCS]: SALVO of {salvo.Count} from {vessel.GetDisplayName()} to {target.GetDisplayName()}. " +
                    $"(TM: {target.totalMass} / PM: {preferred.mass * preferred.targetMassRatio} / S: {targetMass / (preferred.mass * preferred.targetMassRatio)})");

                foreach (ModuleWeaponController weapon in salvo)
                {
                    if (weapon == null || weapon.vessel != vessel) continue;

                    checkWeapons = true;

                    weapon.target = target;
                    weapon.side = side;
                    weapon.Fire();

                    Debug.Log($"[KCS]: MISSILE from {vessel.GetDisplayName()} to {target.GetDisplayName()} using {weapon.weaponCode}");

                    controller.weaponsInFlight.Add(weapon);
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
            // todo: could we actually intercept it before it reaches its target? don't want to waste an interceptor.
            // need to know missile's time to hit and the ideal interceptors time to intercept

            if (weaponsToIntercept.Count > 0 && interceptors.Count > 0)
            {
                bool checkWeapons = false;
                ModuleWeaponController interceptor;
                //List<ModuleWeaponController> interceptorsReady;

                //var interceptorsReady = interceptors.OrderByDescending()

                foreach (var interceptTarget in weaponsToIntercept)
                {
                    if (interceptors.Count < 1) break;
                    checkWeapons = true;

                    Vector3 interceptVector = FromTo(vessel, interceptTarget.vessel).normalized;

                    // Group and order interceptors by 
                    //var interceptorGroups = interceptors.GroupBy(i => i.childDecouplers).ToList();
                    //var interceptorsReady = interceptorGroups.OrderBy(i => i.Key).First().ToList();
                    //interceptorsReady = interceptorsReady.OrderByDescending(w => Vector3.Dot(w.part.parent.transform.up, interceptVector)).ToList();

                    //var interceptorsReady = GetPreferredWeapon(interceptTarget.vessel, interceptors);

                    //interceptor = interceptorsReady.First();
                    interceptor = GetPreferredWeapon(interceptTarget.vessel, interceptors, 1).First();
                    interceptors.Remove(interceptor);

                    interceptor.isInterceptor = true;
                    interceptor.targetWeapon = interceptTarget;
                    interceptor.target = interceptTarget.vessel;
                    interceptor.side = side;
                    interceptor.Fire();

                    Debug.Log($"[KCS]: INTERCEPTOR from {vessel.GetDisplayName()} to {weaponsToIntercept.First().weaponCode}");

                    interceptTarget.interceptedBy.Add(interceptor);

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

            if (weapons.Count > 0)
            {
                //weaponsMinRange = weapons.Min(w => w.MinMaxRange.x);
                //weaponsMaxRange = weapons.Max(w => w.MinMaxRange.y);
            }

            interceptors = weapons.FindAll(w => w.useAsInterceptor);

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
            var enemiesByDistance = controller.ships.FindAll(s => s != null && s.alive && s.side != side);
            if (enemiesByDistance.Count < 1) return null;
            return enemiesByDistance.OrderBy(s => KCS.VesselDistance(s.vessel, vessel)).First();
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
            hasWeapons = vessel.FindPartModulesImplementing<ModuleWeaponController>().FindAll(w => w.canFire).Count > 0;
            //bool control = vessel.maxControlLevel != Vessel.ControlLevel.NONE && vessel.angularVelocity.magnitude < 20;
            bool control = vessel.isCommandable && vessel.angularVelocity.magnitude < 99;
            bool dead = (!hasPropulsion && !hasWeapons) || !control;

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
                    var anim = Sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                    if (anim == null) continue;
                    TryToggle(true, Sensor.part.FindModuleImplementing<ModuleAnimationGroup>());
                }
                DeployedSensors = true;
            }
        }

        private void FindTarget()
        {
            List<ModuleShipController> validEnemies = controller.ships.FindAll(
                s => 
                s != null
                && s.vessel != null
                && s.side != side
                && s.alive);

            if (validEnemies.Count < 1) { 
                target = null;
                return;
            }

            List<ModuleShipController> withdrawingEnemies = validEnemies.FindAll(s => s.state == "Withdrawing");
            validEnemies = validEnemies.Except(withdrawingEnemies).ToList();
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

            targetController = validEnemies.First();
            target = targetController.vessel;
            
            // Debugging
            List<Tuple<string, float, float>> targetsWeighted = validEnemies.Select(s => Tuple.Create(s.vessel.GetDisplayName(), WeighTarget(s), VesselDistance(vessel, s.vessel))).ToList();
            
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
            float massDifference = Mathf.Abs(initialMass - target.initialMass);

            float massComparison = Mathf.Min(initialMass, target.initialMass) / Mathf.Max(initialMass, target.initialMass);
            //float massComparison2 = 2 * Mathf.Min(initialMass, target.initialMass) / (initialMass + target.initialMass);

            return distance * (1 - massComparison);
        }

        private bool HasLock()
        {
           return FromTo(vessel, target).magnitude < maxDetectionRange * Mathf.Clamp((targetController.heatSignature / 1500), 0.5f, 3.0f);
        }

        private void FindInterceptTarget()
        {
            if (interceptors.Count < 1 || controller.weaponsInFlight.Count < 1) return;

            weaponsToIntercept = controller.weaponsInFlight.FindAll(
                w => 
                w != null
                && w.vessel != null
                && !w.missed
                && w.side != side
                && VesselDistance(w.vessel, vessel) < maxDetectionRange
                && w.interceptedBy.Count < 1
                && CanIntercept(w));

            weaponsToIntercept = weaponsToIntercept.OrderBy(w => VesselDistance(w.vessel, vessel)).ToList();
        }

        private bool CanIntercept(ModuleWeaponController weapon)
        {
            if (interceptorAcceleration < 1) return false;

            Vessel intTarget = weapon.vessel;
            Vector3 targetVector = FromTo(vessel, intTarget);
            Vector3 intAccVector = targetVector.normalized * interceptorAcceleration;
            float timeToIntercept = ClosestTimeToCPA(targetVector, intTarget.obt_velocity - vessel.obt_velocity, intTarget.acceleration - intAccVector, 30);
            float distance = VesselDistance(vessel, intTarget);

            return timeToIntercept + 0.5f < weapon.timeToHit && weapon.timeToHit > 0 && distance > 500;
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
            return o.PeA < minSafeAltitude;
        }

        private bool UnderTimeLimit(float timeLimit = 0)
        {
            if (timeLimit == 0) 
                timeLimit = updateInterval;

            return Time.time - lastUpdate < timeLimit;
        }

        private bool NearIntercept(Vector3 relVel, float minRange)
        {
            float timeToKillVelocity = (relVel.magnitude - firingSpeed) / maxAcceleration;

            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, relVel.normalized * -1) * Mathf.Deg2Rad;
            //float timeToRotate = SolveTime(rotDistance / 2, maxAngularAcceleration.magnitude) * 2;
            float timeToRotate = SolveTime(rotDistance * 0.75f, maxAngularAcceleration.magnitude) / 0.75f;

            Vector3 toClosestApproach = ToClosestApproach(relVel, minRange);
            float velToClosestApproach = Vector3.Dot(relVel, toClosestApproach.normalized);
            if (velToClosestApproach < 1) return false;
            float timeToClosestApproach = Mathf.Abs(toClosestApproach.magnitude / velToClosestApproach);

            return timeToClosestApproach < (timeToKillVelocity + timeToRotate);
        }

        private Vector3 ToClosestApproach(Vector3 relVel, float minRange)
        {
            Vector3 rotatedVector = Vector3.ProjectOnPlane(relVel, FromTo(vessel, target).normalized).normalized;
            Vector3 toClosestApproach = (target.transform.position + (rotatedVector * minRange)) - vessel.transform.position;
            return toClosestApproach;
        }

        public void UpdateRobotics(bool deploy)
        {
            if (deploy == roboticsDeployed) return;

            //generate list of KAL500 parts, could change in flight
            List<ModuleCombatRobotics> RoboticControllers = vessel.FindPartModulesImplementing<ModuleCombatRobotics>();
            
            if (deploy)
                RoboticControllers.ForEach(rc => rc.CombatTrigger());
            else
                RoboticControllers.ForEach(rc => rc.PassiveTrigger());

            roboticsDeployed = deploy;
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

            foreach(var incoming in incomingWeapons)
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