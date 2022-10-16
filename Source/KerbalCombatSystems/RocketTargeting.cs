using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;
using System.Collections;
using static KerbalCombatSystems.KCS;


namespace KerbalCombatSystems
{
    public class ModuleRocket : ModuleWeapon
    {
        Vessel target;
        ModuleWeaponController controller;
        LineRenderer leadLine;
        private List<ModuleDecouple> decouplers;
        ModuleDecouple decoupler;

        public bool firing = false;
        Vector3 targetVector;
        Vector3 leadVector;
        Vector3 rocketAcceleration;
        Vector3 origin;
        float timeToHit;
        float acceleration;

        public override void Setup()
        {
            controller = part.FindModuleImplementing<ModuleWeaponController>();

            if (controller.target == null && vessel.targetObject == null) return;
            target = controller.target ?? vessel.targetObject.GetVessel();

            leadLine = KCSDebug.CreateLine(Color.green);

            NextRocket();
        }

        public override Vector3 Aim()
        {
            if (decouplers.Count < 1 || decoupler == null || decoupler.vessel != vessel || target == null)
                return Vector3.zero;

            origin = decoupler.transform.position;
            targetVector = target.CoM - decoupler.transform.position;
            rocketAcceleration = targetVector.normalized * acceleration;
            timeToHit = ClosestTimeToCPA(targetVector, target.obt_velocity - vessel.obt_velocity, target.acceleration - rocketAcceleration, 99);
            leadVector = target.CoM + Displacement(target.obt_velocity - vessel.obt_velocity, (target.acceleration - rocketAcceleration) * 0.5, timeToHit);
            leadVector = leadVector - origin;
                
            //leadVector = leadVector.normalized;

            KCSDebug.PlotLine(new Vector3[] { origin, origin + leadVector }, leadLine);

            if (Vector3.Angle(leadVector.normalized, decoupler.transform.up) < 0.25 && !firing)
                Fire();

            return leadVector.normalized;
        }

        public override void Fire()
        {
            firing = true;
            StartCoroutine(FireRocket());
        }

        private IEnumerator FireRocket()
        {
            //run through all child parts of the controllers parent for engine modules
            List<Part> DecouplerChildParts = decoupler.part.FindChildParts<Part>(true).ToList();

            foreach (Part CurrentPart in DecouplerChildParts)
            {
                ModuleEngines Module = CurrentPart.GetComponent<ModuleEngines>();

                //check for engine modules on the part and stop if not found
                if (Module == null) continue;

                //activate the engine and force it to full capped thrust incase of ship throttle
                Module.Activate();
                Module.throttleLocked = true;
            }

            //wait a frame before decoupling to ensure engine activation(may not be required)
            yield return null;
            decoupler.Decouple();

            yield return new WaitForSeconds(0.5f);
            NextRocket();
            firing = false;
        }

        private void NextRocket()
        {
            decouplers = FindDecouplerChildren(part.parent, "Weapon", true);
            if (decouplers.Count < 1)
            {
                controller.canFire = false;
                return;
            }

            decoupler = decouplers.Last();
            acceleration = controller.CalculateAcceleration(decoupler.part);
        }

        public void OnDestroy() =>
            KCSDebug.DestroyLine(leadLine);
        
    }
}