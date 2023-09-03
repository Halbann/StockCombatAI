﻿using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using static KerbalCombatSystems.KCS;

namespace KerbalCombatSystems
{
    class ModuleEscapePodGuidance : PartModule
    {
        #region Fields

        // Current status retained during craft loading
        [KSPField(isPersistant = true)]
        private bool escaped = false;

        private List<Part> shipControllerParts;
        private List<ModuleEngines> engines;
        private KCSFlightController fc;
        private ModuleDecouplerDesignate seperator;
        private Vessel parent;

        // Relavent Game Setting
        private int refreshRate;

        #endregion

        #region Buttons/Actions

        const string groupName = "Escape Pod Guidance";

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
            seperator = FindDecoupler(part, "Escape Pod");
            StartCoroutine(EscapeSequence());
        }

        [KSPAction("Launch", KSPActionGroup.Abort)]
        public void LaunchAction(KSPActionParam param)
        {
            Launch();
        }

        #endregion

        #region Main

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            shipControllerParts = new List<Part>();

            // Store a reference to the parent ship.
            parent = vessel;

            // Set the refresh rate
            refreshRate = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().refreshRate;

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

                yield return new WaitForSeconds(refreshRate);
            }
        }

        private IEnumerator EscapeSequence()
        {
            // Stop checking for connection.
            //if (statusRoutine != null)
            //    StopCoroutine(statusRoutine);

            // try to pop decoupler
            if (seperator != null)
            {
                seperator.Separate();
            }
            else
            {
                // Notify of error but try to launch anyway
                Debug.Log("[KCS]: Couldn't find decoupler on " + vessel.GetName() + " (Escape Pod)");
            }

            if (vessel == parent)
            {
                // We didn't find a decoupler and we're still attached to the parent.

                Debug.Log("[KCS]: Failed to launch escape pod on " + vessel.vesselName);

                // Abort the escape.
                yield break;
            }

            KCSController.Log("Escape pod launching from %1", parent);
            escaped = true;

            yield return new WaitForFixedUpdate(); // Wait for our new vessel to be created.

            // Transfer as many crew as possible from the parent ship.
            TransferCrew();

            // Try find command part
            ModuleCommand command = FindCommand(vessel);
            if (command)
                command.MakeReference();

            // Activate engines.
            engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            engines.ForEach(e => e.Activate());

            // Add flight controller.
            fc = part.gameObject.AddComponent<KCSFlightController>();
            fc.throttleLerpRate = 100;
            fc.throttle = 1;
            fc.alignmentToleranceforBurn = 10;
            fc.attitude = vessel.ReferenceTransform.up;
            fc.Drive();

            // Deploy parachutes.
            List<ModuleParachute> parachutes = vessel.FindPartModulesImplementing<ModuleParachute>();
            parachutes.ForEach(p => p.Deploy());


            // If there are no command points then end the escape after the inital hard burn
            if (command)
            {
                yield return new WaitForSeconds(1f); // Exiting the ship.
                StartCoroutine(FlightRoutine());
            }
            else
            {
                yield return new WaitForSeconds(10f); // Exiting the ship and burning for a few seconds
                Debug.Log("[KCS]: Escape sequence ending on " + vessel.GetName() + " (Escape Pod)");

                // Remove the flight controller and allow the guidance to cease
                Destroy(fc);
            }
        }

        IEnumerator FlightRoutine()
        {
            while (true)
            {
                if (engines.FindAll(e => e.EngineIgnited && e.isOperational).Count < 1)
                    break;

                if (CheckOrbitUnsafe())
                {
                    Orbit o = vessel.orbit;
                    double UT = Planetarium.GetUniversalTime();

                    // Execute a burn to circularize our orbit at the current altitude.
                    Vector3d fvel, deltaV = Vector3d.up * 100;

                    while (deltaV.magnitude > 2)
                    {
                        yield return new WaitForFixedUpdate();

                        UT = Planetarium.GetUniversalTime();
                        fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                        deltaV = fvel - vessel.GetObtVelocity();

                        fc.attitude = deltaV.normalized;
                        fc.throttle = Mathf.Lerp(0, 1, (float)(deltaV.magnitude / 10));
                        fc.Drive();
                    }
                }
                else
                {
                    // Burn either normal or anti-normal until we run out of fuel.
                    Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                    bool facingNorth = Vector3.Angle(vessel.ReferenceTransform.up, orbitNormal) < 90;
                    fc.attitude = orbitNormal * (facingNorth ? 1 : -1);
                    fc.throttle = 1;
                    fc.Drive();
                }

                yield return new WaitForSeconds(refreshRate);
            }

            Debug.Log("[KCS]: Escape sequence ending on " + vessel.GetName() + " (Escape Pod)");

            //remove the flight controller and allow the guidance to cease
            Destroy(fc);
        }

        #endregion

        #region Functions

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

            // todo: fix kerbal portraits not updating on parent.

            vessel.DespawnCrew();
            parent.DespawnCrew();

            StartCoroutine(FinaliseCrewTransfer(parent, vessel));
        }

        public static void MoveCrewMember(ProtoCrewMember crew, Part part)
        {
            Part source = crew.seat.part;

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

            target.SpawnCrew();
            source.SpawnCrew();
        }

        #endregion
    }
}
