using System;
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
            if (sensors.Count < 1)
            {
                maxDetectionRange = 1000; // visual range perhaps?
                return;
            }

            maxDetectionRange = sensors.Max(s => s.detectionRange);
            // todo: deploy any sensors that aren't deployed
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


//legacy bomber code

/*function BombingRun {
    Set ColAvoidSafety to False.
    lock SteerTo to calculateCorrection().

    if true {
        set drawSteeringTarget to vecDraw( { return ship:controlPart:position. }, { return (steering:vector)*100. }, red, "", 1.0, true, 0.2, true, true).

        set drawTargetPositionTarget to v(0,0,0).
        set drawTargetPosition to vecDraw( { return ship:controlPart:position. }, { return drawTargetPositionTarget. }, magenta, "", 1.0, true, 0.2, true, true).

        set drawTargetRelVelTarget to v(0,0,0).
        set drawTargetRelVel to vecDraw( { return ship:controlPart:position. },  { return drawTargetRelVelTarget. }, green, "", 1.0, true, 0.2, true, true).
    }

    set ShipThrottle to 0.

    until (enemy:position - ship:position):mag < BombReleaseRange {
        set adjustThrottle to 0.
        

        IF vectorAngle(ship:facing:vector, steering:vector) < 5 {
            local targetRelVel to ship:velocity:orbit - enemy:velocity:orbit.
            local targetVec to enemy:direction:vector.

            set adjustThrottle to 0.5.
            IF (vDot(targetRelVel:normalized, targetVec:normalized) > 0.999) {
                IF (vDot(targetRelVel, targetVec) > BombVelocity) {
                    set adjustThrottle to 0.
                }
                IF (targetRelVel:MAG > BombVelocity+5) {
                    Translate(-targetRelVel).
                }
            }
        }
        
        set ShipThrottle to adjustThrottle.
    }
    
    set ShipThrottle to 0.
}

function BomberAvoid {
    Translate(-(ship:velocity:orbit - enemy:velocity:orbit)).
    wait 2.
    Translate(v(0,0,0)).

    local pitchUp to angleAxis(-30, ship:facing:starVector).
    local overshoot to pitchUp * ship:facing.
    set SteerTo to overshoot:vector.

    until vang((ship:velocity:orbit - enemy:velocity:orbit), enemy:direction:vector) > 15 {
        if vectorAngle(ship:facing:vector, SteerTo) < 5 { set ShipThrottle to 1. }
        else {set ShipThrottle to 0.}
    }

    set ShipThrottle to 0.
    Set ColAvoidSafety to True.
}*/ 
