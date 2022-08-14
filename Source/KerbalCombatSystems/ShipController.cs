﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    public class ModuleShipController : PartModule
    {
        // User parameters changed via UI.

        const string shipControllerGroupName = "Ship AI";
        public bool controllerRunning = false;
        public float updateInterval = 0.1f;

        // Ship AI variables.

        private Coroutine shipControllerCoroutine;
        private KCSFlightController fc;
        private KCSController controller;
        public Vessel target;
        public List<ModuleWeaponController> weapons;
        float lastFired;
        float fireInterval;
        float maxDetectionRange;
        public float initialMass;
        public Side side;
        private bool DeployedSensors;

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
        }

        public void CheckWeapons()
        {
            weapons = vessel.FindPartModulesImplementing<ModuleWeaponController>();
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                fc = part.gameObject.AddComponent<KCSFlightController>();
                controller = FindObjectOfType<KCSController>();
            }
        }

        private IEnumerator ShipController()
        {
            while (true)
            {
                // Find target.

                UpdateDetectionRange();
                FindTarget();

                // Engage. todo: Need to separate update of target position from main coroutine. 

                if (target != null)
                {
                    fc.attitude = (target.ReferenceTransform.position - vessel.ReferenceTransform.position).normalized;
                    fc.throttle = 1;

                    if (Time.time - lastFired > fireInterval)
                    {
                        lastFired = Time.time;
                        fireInterval = UnityEngine.Random.Range(5, 15);

                        if (weapons.Count > 0)
                        {
                            //this line is what should be causing the bumper weapons deployment
                            //just getting from end would avoid
                            var missileIndex = UnityEngine.Random.Range(0, weapons.Count - 1);
                            weapons[missileIndex].target = target;
                            weapons[missileIndex].Fire();

                            yield return null;
                            CheckWeapons();
                        }
                    }  
                } 
                else
                {
                    fc.throttle = 0;
                }

                yield return new WaitForSeconds(updateInterval);
            } 
        }

        void UpdateDetectionRange()
        {
            var sensors = vessel.FindPartModulesImplementing<ModuleObjectTracking>();

            maxDetectionRange = sensors.Max(s => s.detectionRange);
            

            if(!DeployedSensors) //&& AI is enabled
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    try { KCS.TryToggle(true, Sensor.part.FindModuleImplementing<ModuleAnimationGroup>()); } catch { }
                }
                DeployedSensors = true;
            }

            /*if (DeployedSensors) //&& AI is disabled
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    try { KCS.TryToggle(true, Sensor.part.FindModuleImplementing<ModuleAnimationGroup>()); } catch { }
                }
                DeployedSensors = true;
            }*/
        }

        void FindTarget()
        {
            var ships = controller.ships.FindAll(
                s => KCS.VesselDistance(s.vessel, vessel) < maxDetectionRange
                && s.side != side);
            //ships.Remove(ships.Find(s => s.vessel == vessel));

            if (ships.Count < 1) return;

            target = ships.OrderBy(s => KCS.VesselDistance(s.vessel, vessel)).First().vessel;
        }


        public void FixedUpdate()
        {
            if (controllerRunning) fc.Drive();
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
    }
}


