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
