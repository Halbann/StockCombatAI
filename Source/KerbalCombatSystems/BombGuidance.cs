using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;

namespace KerbalCombatSystems
{
    public class ModuleBomb : PartModule
    {
        /*
        // Targetting variables.
        public bool FireStop = false;
        // KCSFlightController fc;
        Vessel Target;
        private Vector3 LeadVector;

        // Debugging line variables.
        LineRenderer TargetLine, RVLine;

        //rocket decoupler variables
        private List<ModuleDecouple> RocketBases;
        ModuleDecouple Decoupler;

        public void Start()
        {
            Target = part.FindModuleImplementing<ModuleWeaponController>().target;

            // initialise debug line renderer
            TargetLine = KCSDebug.CreateLine(Color.magenta);
            RVLine = KCSDebug.CreateLine(Color.blue);

            //find a decoupler associated with the weapon
            RocketBases = KCS.FindDecouplerChildren(part.parent, "Weapon", false);
            Decoupler = RocketBases[RocketBases.Count() - 1];

        }

        public void Update()
        {
            if (Target != null)
            {
                // Update debug lines.
                KCSDebug.PlotLine(new[] { part.transform.position, Target.transform.position }, TargetLine);
                KCSDebug.PlotLine(new[] { part.transform.position, LeadVector }, RVLine);
            }

            //todo: skip over the aiming part if not on autopilot
            //todo: add an appropriate aim deviation check
            //(Vector3.AngleBetween(LeadVector, part.parent.forward()) > 1) 
            if ((true || Target == null) && FireStop == false)
            {
                FireStop = true;
                //once the flight conditions are met drop the bombs
                Decoupler.Decouple();
                //delete the active module at the end.
                part.RemoveModule(part.GetComponent<ModuleRocket>());
            }
        }



        public void OnDestroy()
        {
            KCSDebug.DestroyLine(LeadLine);
            KCSDebug.DestroyLine(TargetLine);
        }
        */
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
