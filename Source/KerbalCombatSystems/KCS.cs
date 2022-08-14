using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    public static class KCS
    {
        public static ModuleDecouple FindDecoupler(Part origin, string type, bool ignoreTypeRequirement)
        {
            Part currentPart;
            Part nextPart = origin.parent;
            ModuleDecouple Decoupler;
            ModuleDecouplerDesignate module;

            if (nextPart == null) return null;

            for (int i = 0; i < 99; i++)
            {
                currentPart = nextPart;
                nextPart = currentPart.parent;

                if (nextPart == null) break;
                if (!currentPart.isDecoupler(out Decoupler)) continue;
                if (currentPart.GetComponent<ModuleDecouple>().isDecoupled == true) continue;

                module = currentPart.GetComponent<ModuleDecouplerDesignate>();
                if (module == null) continue;

                if (module.DecouplerType != type && !ignoreTypeRequirement) continue;

                return Decoupler;
            }

            return null;
        }

        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
        }

        public static float GetMaxAcceleration(Vessel v)
        {
            List<ModuleEngines> engines = v.FindPartModulesImplementing<ModuleEngines>();
            float thrust = engines.Sum(e => e.MaxThrustOutputVac(true));

            return thrust / v.GetTotalMass();
        }

        public static bool RayIntersectsVessel(Vessel v, Ray r)
        {
            foreach (Part p in v.parts)
            {
                foreach (Bounds b in p.GetColliderBounds())
                {
                    if (b.IntersectRay(r)) return true;
                }
            }

            return false;
        }

        public static Vector3 TargetLead(Vessel Target, Part Firer, float TravelVelocity)
        {
            Vector3 RelPos = Target.transform.position - Firer.transform.position;
            Vector3 RelVel = Target.GetObtVelocity() - Firer.vessel.GetObtVelocity();


            // Quadratic equation coefficients a*t^2 + b*t + c = 0
            float a = Vector3.Dot(RelVel, RelVel) - TravelVelocity * TravelVelocity;
            float b = 2f * Vector3.Dot(RelVel, RelPos);
            float c = Vector3.Dot(RelPos, RelPos);

            float desc = b * b - 4f * a * c;

            float ForwardDelta = 2f * c / (Mathf.Sqrt(desc) - b);

            //return the firing solution
            return Target.transform.position + Target.GetObtVelocity() * ForwardDelta;
            
        }

        public static void TryToggle(bool Direction, ModuleAnimationGroup Animation)
        {
            if (Direction && Animation.isDeployed == false)
            {
                //try deploy if not already
                Animation.DeployModule();
            }
            else if(!Direction && Animation.isDeployed == true)
            {
                //try retract if not already
                Animation.RetractModule();
            }

            //do nothing otherwise
        }

        public static List<ModuleDecouple> FindDecouplerChildren(Part Root, string type, bool ignoreTypeRequirement)
        {   
            //run through all child parts of the controllers parent for decoupler modules
            List<Part> ChildParts = Root.FindChildParts<Part>(true).ToList();
            //check the parent itself
            ChildParts.Add(Root);
            //spawn empty modules list to add to
            List<ModuleDecouple> DecouplerList = new List<ModuleDecouple>();


            foreach (Part CurrentPart in ChildParts)
            {
                ModuleDecouplerDesignate Module = CurrentPart.GetComponent<ModuleDecouplerDesignate>();

                //check for decoupler modules on the part of the correct type and add to a list
                if (CurrentPart.GetComponent<ModuleDecouple>() == null)     continue;
                if (CurrentPart.GetComponent<ModuleDecouple>().isDecoupled == true) continue;
                if (Module == null)                                         continue;
                if (Module.DecouplerType != type && !ignoreTypeRequirement) continue;

                DecouplerList.Add(CurrentPart.GetComponent<ModuleDecouple>());
            }
            
            return DecouplerList;
        }
    }

    /*public class KCSShip
    {
        public Vessel v;
        public float initialMass;

        public KCSShip(Vessel ship, float mass)
        {
            v = ship;
            initialMass = mass;
        }
    }*/

    public enum Side
    {
        A,
        B
    }
}
