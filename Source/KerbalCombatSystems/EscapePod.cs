using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using KSP.UI.Screens.Flight;

using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    class ModuleEscapePodGuidance : PartModule
    {
        #region Fields

        [KSPField(isPersistant = true)]
        private bool escaped = false;

        private List<Part> shipControllerParts;
        private List<ModuleEngines> engines;
        private KCSFlightController fc;
        private ModuleDecouplerDesignate seperator;
        private Vessel parent;
        private int parentPartCount;

        public static float checkRate = 0.5f;

        public const string groupName = "Escape Pod";
        public const float escapeSpeedMin = 50f;
        public const float escapeSpeedMax = 500f;

        // Escape burn delta-V.

        [KSPAxisField(
            guiName = "Escape Δv",
            isPersistant = true,
            groupStartCollapsed = false,
            minValue = escapeSpeedMin,
            maxValue = escapeSpeedMax,
            groupDisplayName = groupName,
            groupName = groupName,
            guiActive = true,
            guiActiveEditor = true,
            guiUnits = " m/s"
        )]
        [UI_FloatRange(
            minValue = escapeSpeedMin,
            maxValue = escapeSpeedMax,
            stepIncrement = 10f,
            scene = UI_Scene.All
        )]
        public float escapeSpeed = 200f;


        // Escape orbital direction.

        [UI_Cycle(
            stateNames = new string[]
            {
                "Normal",
                "Anti-Normal",
                "Prograde",
                "Retrograde"
            }, 
            controlEnabled = true,
            scene = UI_Scene.All
        )]
        [KSPField(
            guiName = "Escape Direction",
            isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            groupName = groupName,
            groupDisplayName = groupName
        )]
        public int escapeDirection = 0;
        public EscapeDirection Direction =>
            (EscapeDirection)escapeDirection;


        #endregion

        #region Buttons/Actions


        // Escape is called from the button, from the action, when no controllers are found, or when the ship controller calls an abort.
        [KSPEvent(
            guiActive = true,
            guiActiveEditor = false,
            guiName = "Launch",
            groupName = groupName,
            groupDisplayName = groupName
        )]
        public void Launch()
        {
            //find decoupler
            seperator = FindDecoupler(part, "EscapePod");

            // We could be on the main command pod and the root, in which case the decoupler is a child.
            if (seperator == null)
                seperator = FindDecouplerChildren(vessel.rootPart, "EscapePod").FirstOrDefault();

            StartCoroutine(EscapeSequence());
        }

        [KSPAction("Launch", KSPActionGroup.Abort)]
        public void LaunchAction(KSPActionParam param)
        {
            Launch();
        }

        #endregion

        #region Main

        public override void OnStartFinished(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            shipControllerParts = new List<Part>();

            // Store a reference to the parent ship.
            parent = vessel;
            parentPartCount = parent.parts.Count;

            // Find ship controllers and add them to our list.
            var controllers = vessel.FindPartModulesImplementing<ModuleShipController>();
            shipControllerParts = controllers.Select(m => m.part).ToList();

            // Only start the status routine if we have a ship controller.
            if (shipControllerParts.Count > 0)
                StartCoroutine(StatusRoutine());
        }

        // Continuously check for a connection to the ship controller.
        IEnumerator StatusRoutine()
        {
            escaped = false;

            while (!escaped)
            {
                for (int i = shipControllerParts.Count - 1; i >= 0; i--)
                {
                    //if part does not exist / on the same ship
                    if (shipControllerParts[i] == null || shipControllerParts[i].vessel.id != part.vessel.id)
                        shipControllerParts.RemoveAt(i);
                }

                if (shipControllerParts.Count < 1)
                {
                    escaped = true;
                    Launch();
                }

                yield return new WaitForSeconds(checkRate);
            }
        }

        private IEnumerator EscapeSequence()
        {
            // try to pop decoupler
            if (seperator != null)
            {
                seperator.Separate();
            }

            yield return new WaitForFixedUpdate(); // Wait for our new vessel to be created.

            ModuleShipController shipController = vessel.FindPartModuleImplementing<ModuleShipController>();

            if (vessel.parts.Count == parentPartCount
                && shipController != null && shipController.alive)
            {
                // We didn't find a decoupler and we're still part of a functional parent warship.

                Debug.Log("[KCS]: Failed to launch escape pod on " + vessel.vesselName);

                // Abort the escape.
                yield break;
            }

            KCSController.Log("Escape pod launching from %1", parent);
            escaped = true;

            yield return new WaitForFixedUpdate(); // Wait for our new vessel to be created.

            // Transfer as many crew as possible from the parent ship.
            if (parent != vessel)
                TransferCrew();

            // Activate engines.
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            engines.ForEach(e => e.Activate());

            ModuleCommand command = FindCommand(vessel);
            if (command)
            {
                command.MakeReference();
                Vector3 propulsionVector = -GetFireVector(engines);
                AlignReference(command, propulsionVector.normalized);
            }

            // Add flight controller.
            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.lerpAttitude = false;
            fc.throttleLerpRate = 100;
            fc.throttle = 1;
            fc.alignmentToleranceforBurn = 90;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.Drive();

            fc.Stability(true);

            // Deploy parachutes.
            List<ModuleParachute> parachutes = vessel.FindPartModulesImplementing<ModuleParachute>();
            parachutes.ForEach(p => p.Deploy());

            yield return new WaitForSeconds(0.2f);

            float clearanceTime = Time.time;
            if (vessel != parent)
            {
                Ray ray = new Ray();
                bool blocked = true;

                Vector3 escapeDirection = GetEscapeDirection(Planetarium.GetUniversalTime());

                while (blocked && Time.time - clearanceTime < 2)
                {
                    yield return new WaitForSeconds(0.1f);

                    ray.origin = vessel.CoM;
                    ray.direction = escapeDirection;

                    blocked = RayIntersectsVessel(parent, ray);
                }
            }

            fc.Stability(false);

            StartCoroutine(FlightRoutine());
        }

        IEnumerator FlightRoutine()
        {
            float lastUpdate;

            while (true)
            {
                lastUpdate = Time.time;

                if (!InControl())
                    break;

                if (Direction != EscapeDirection.Retrograde && CheckOrbitUnsafe())
                {
                    Orbit o = vessel.orbit;
                    double UT;

                    // Execute a burn to circularize our orbit at the current altitude.
                    Vector3d fvel, deltaVd = Vector3d.up * 100;

                    while (deltaVd.magnitude > 2 && Time.time - lastUpdate < checkRate)
                    {
                        yield return new WaitForFixedUpdate();

                        UT = Planetarium.GetUniversalTime();
                        fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                        deltaVd = fvel - vessel.GetObtVelocity();

                        fc.attitude = deltaVd.normalized;
                        fc.throttle = Mathf.Lerp(0, 1, (float)(deltaVd.magnitude / 10));
                        fc.Drive();
                    }
                }
                else
                {
                    // Plane change. Burning either north or south until we reach the escape speed.

                    double UT = Planetarium.GetUniversalTime();
                    Vector3 deltaV = GetEscapeDirection(UT) * escapeSpeed;

                    CelestialBody SOI = vessel.orbit.referenceBody;
                    Vector3 previousDirection = deltaV.normalized;
                    float directionChange = 0;

                    fc.throttle = 1;

                    while (deltaV.magnitude > 10 && InControl() && directionChange < 90)
                    {

                        UT = Planetarium.GetUniversalTime();
                        deltaV = GetEscapeDirection(UT) * deltaV.magnitude;
                        deltaV -= Vector3.Project(vessel.acceleration, deltaV) * TimeWarp.fixedDeltaTime;

                        fc.attitude = deltaV.normalized;
                        fc.Drive();

                        if (vessel.orbit.referenceBody != SOI)
                        {
                            directionChange = 0;
                            previousDirection = deltaV.normalized;
                            SOI = vessel.orbit.referenceBody;
                        }

                        // Limit the change in direction so that we don't loop around in a very low gravity SOI.
                        directionChange += Vector3.Angle(previousDirection, deltaV.normalized);
                        previousDirection = deltaV.normalized;

                        yield return new WaitForFixedUpdate();
                    }

                    fc.throttle = 0;
                    fc.Drive();

                    break; 
                }

                yield return new WaitForSeconds(checkRate);
            }

            // Remove the flight controller and allow the guidance to cease.

            Destroy(fc);

            // Let the the aero and parachutes take over if we're de-orbiting.

            yield return new WaitUntil(() => fc == null);

            if (Direction == EscapeDirection.Retrograde)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            }
        }

        bool InControl()
        {
            engines.RemoveAll(e => !e.EngineIgnited || !e.isOperational);

            return vessel.IsControllable && engines.Count > 0;
        }

        #endregion

        #region Functions

        public enum EscapeDirection
        {
            Normal,
            AntiNormal,
            Prograde,
            Retrograde
        }

        private Vector3 GetEscapeDirection(double UT)
        {
            switch (Direction)
            {
                case EscapeDirection.Normal:
                    //return vessel.orbit.Normal(UT).normalized;
                    return (Vector3)vessel.orbit.h.xzy.normalized;
                case EscapeDirection.AntiNormal:
                    return (Vector3)vessel.orbit.h.xzy.normalized * -1;
                case EscapeDirection.Prograde:
                    return (Vector3)vessel.orbit.Prograde(UT).normalized;
                case EscapeDirection.Retrograde:
                    return (Vector3)vessel.orbit.Prograde(UT).normalized * -1;
                default:
                    return (Vector3)vessel.orbit.h.xzy.normalized;
            }
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            CelestialBody body = o.referenceBody;
            PQS pqs = body.pqsController;
            double maxTerrainHeight = pqs.radiusMax - pqs.radius;

            return o.PeA < maxTerrainHeight;
        }

        private void TransferCrew()
        {
            // Transfer as many crew as possible from parent to vessel.

            List<ProtoCrewMember> crew = parent.GetVesselCrew().ToList();
            var emptySeats = new List<Part>();

            foreach (var part in vessel.parts)
            {
                int seats = part.CrewCapacity - part.protoModuleCrew.Count;

                for (int i = 0; i < seats; i++)
                    emptySeats.Add(part);
            }

            if (emptySeats.Count < 1)
                return;

            Part seat;

            // todo: deprioritise external command seats.

            foreach (ProtoCrewMember crewMember in crew)
            {
                if (emptySeats.Count < 1)
                    break;

                seat = emptySeats.Last();
                emptySeats.RemoveAt(emptySeats.Count - 1);

                MoveCrewMember(crewMember, seat);
            }

            Vessel active = FlightGlobals.ActiveVessel;
            if (parent == active || vessel == active)
                active.DespawnCrew();

            StartCoroutine(FinaliseCrewTransfer(parent, vessel));
        }

        public static void MoveCrewMember(ProtoCrewMember crew, Part part)
        {
            Part source = crew?.seat?.part;

            if (crew == null || source == null || part == null)
                return;

            source.RemoveCrewmember(crew);
            part.AddCrewmember(crew);

            GameEvents.onCrewTransferred.Fire(new GameEvents.HostedFromToAction<ProtoCrewMember, Part>(crew, source, part));

            if (part.partInfo.name == "seatExternalCmd")
            {
                var seat = part.FindModuleImplementing<KerbalSeat>();

                if (seat.Occupant == null)
                    seat.OnStartFinished(StartState.Orbital);
            }
        }

        public static IEnumerator FinaliseCrewTransfer(Vessel source, Vessel target)
        {
            Vessel.CrewWasModified(source, target);

            yield return null;

            Vessel active = FlightGlobals.ActiveVessel;
            if (source == active || target == active)
            {
                active.SpawnCrew();
                KerbalPortraitGallery.Instance.StartReset(active);
            }
        }

        #endregion
    }
}
