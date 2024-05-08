using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using static KerbalCombatSystems.Utils;

namespace KerbalCombatSystems
{
    internal class Targeting
    {
        // Generic.

        private readonly ModuleShipController ship;

        private Vessel Vessel =>
            ship.vessel;

        public Targeting(ModuleShipController ship)
        {
            this.ship = ship;
        }

        // Targeting.

        public Vessel target;
        public ModuleShipController targetController;

        private struct TargetInfo
        {
            public ModuleShipController ship;
            public float weight;
            public bool withdrawing;
            public bool priority;
            public bool offline;
        }
        private readonly List<TargetInfo> targets = new List<TargetInfo>();

        public List<ModuleShipController> Enemies
            => FindEnemies();

        private List<ModuleShipController> FindEnemies()
        {
            List<ModuleShipController> validEnemies = FlightManager.ships.FindAll(s =>
                s != null
                && s.vessel != null
                && s.side != ship.side
                && s.alive);

            return validEnemies;
        }

        public ModuleShipController GetNearestEnemy()
        {
            var enemies = Enemies;
            if (enemies.Count < 1)
                return null;

            return enemies.OrderBy(s => Utils.VesselDistance(s.vessel, Vessel)).First();
        }

        public void Update()
        {
            if (!ship.hasWeapons)
            {
                ClearTarget();
                return;
            }

            var enemies = FindEnemies();
            if (enemies.Count < 1)
            {
                ClearTarget();
                return;
            }

            bool usingPriority = ship.priorityTargetRange.x != ModuleShipController.priorityTargetMin 
                || ship.priorityTargetRange.y != ModuleShipController.priorityTargetMax;
            bool noUpperBound = ship.priorityTargetRange.y == ModuleShipController.priorityTargetMax;
            bool withdrawingValid = ship.withdrawingPriority == "Valid";
            TargetInfo target;
            float distance;

            targets.Clear();

            // O1 time complexity categorisation of targets.
            foreach (var enemy in enemies)
            {
                Vector3 position = enemy.vessel.CoM - Vessel.CoM;
                distance = position.magnitude;

                bool isWithdrawing = enemy.state == "Withdrawing" || enemy.state == "Idle (Unarmed)";
                if (isWithdrawing)
                {
                    // We have no chance of catching this withdrawing enemy.
                    if (distance > ship.maxWeaponRange && !ship.CanInterceptShip(enemy))
                        continue;
                }

                // What will our orbit be like at the maximum distance that we can use our weapons?
                Vector3 interceptPosition = position.normalized * Mathf.Min(ship.maxWeaponRange, distance) + Vessel.CoM;
                Orbit orbit = Orbit.OrbitFromStateVectors(
                    interceptPosition, enemy.vessel.orbit.vel.xzy, Vessel.orbit.referenceBody, Planetarium.GetUniversalTime());

                // To be in range of this target is to be in an immediately dangerous or unrecoverable orbit, skip.
                if (ship.OrbitDangerous(orbit))
                    continue;

                target = new TargetInfo { ship = enemy, weight = WeighTarget(enemy, distance) };

                // Offline target.
                if (!enemy.controllerRunning)
                {
                    target.offline = true;
                    targets.Add(target);
                    continue;
                }

                // Withdrawing target.
                if (isWithdrawing && !withdrawingValid)
                {
                    target.withdrawing = true;
                    targets.Add(target);
                    continue;
                }

                // Priority target.
                if (usingPriority)
                {
                    bool isPriority =
                        distance < ship.maxWeaponRange * 2
                        && enemy.initialMass > ship.priorityTargetRange.x
                        && (noUpperBound || enemy.initialMass < ship.priorityTargetRange.y);

                    if (isPriority)
                    {
                        target.priority = true;
                        targets.Add(target);
                        continue;
                    }
                }

                // Normal target.
                targets.Add(target);
            }

            // Multisort.
            var sorted = targets
                .OrderBy(t => t.offline)
                .ThenBy(t => t.withdrawing)
                .ThenByDescending(t => t.priority)
                .ThenBy(t => t.weight);

            // Get the highest priority target.
            var targetShip = sorted.FirstOrDefault().ship;
            if (targetShip == null)
            {
                ClearTarget();
                return;
            }

            // Update the target.
            SetTarget(targetShip);
        }

        private void SetTarget(ModuleShipController target)
        {
            targetController = target;
            this.target = target.vessel;

            // Update the stock target to reflect the KCS target.
            if ((Vessel)Vessel.targetObject != target.vessel)
                UpdateActiveTarget(target.vessel);
        }

        internal void ClearTarget()
        {
            if (target == null)
                return;

            target = null;
            targetController = null;
            UpdateActiveTarget(null);
        }

        private void UpdateActiveTarget(Vessel target)
        {
            Vessel.targetObject = target;
            if (Vessel == FlightGlobals.ActiveVessel)
                FlightGlobals.fetch.SetVesselTarget(target, true);
        }

        private float WeighTarget(ModuleShipController target, float distance)
        {
            float massComparison = 1 - Mathf.Min(ship.initialMass, target.initialMass) / Mathf.Max(ship.initialMass, target.initialMass);

            // How close the target is to our own mass is used as a coefficient
            // (identical mass is 0, different mass tends towards 1)
            // to reduce the effective distance of the target.
            // TLDR: Lower number = closer mass and distance = better target. Papa John's.

            return massComparison * distance;
        }
    }
}