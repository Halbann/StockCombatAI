using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    public static partial class KCS
    {
        public static Seperator FindDecoupler(Part origin, string type, bool ignoreTypeRequirement)
        {
            Part currentPart;
            Part nextPart = origin.parent;
            ModuleDecouple Decoupler;
            ModuleDockingNode port;
            ModuleDecouplerDesignate module;

            if (nextPart == null) return null;

            for (int i = 0; i < 99; i++)
            {
                currentPart = nextPart;
                nextPart = currentPart.parent;

                if (nextPart == null) break;

                Decoupler = currentPart.GetComponent<ModuleDecouple>();
                port = currentPart.GetComponent<ModuleDockingNode>();

                if (Decoupler == null && port == null) continue;
                Seperator sep = new Seperator();

                if (Decoupler != null)
                {
                    if (currentPart.GetComponent<ModuleDecouple>().isDecoupled == true) continue;

                    module = currentPart.GetComponent<ModuleDecouplerDesignate>();
                    if (module == null) continue;

                    if (module.DecouplerType != type && !ignoreTypeRequirement) continue;

                    sep.decoupler = Decoupler;
                }
                else
                {
                    sep.port = port;
                    sep.isDockingPort = true;
                }

                sep.part = currentPart;
                return sep;
            }

            return null;
        }

        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
        }

        public static float GetMaxAcceleration(Vessel v)
        {
            return GetMaxThrust(v) / v.GetTotalMass();
        }

        public static float GetMaxThrust(Vessel v)
        {
            List<ModuleEngines> engines = v.FindPartModulesImplementing<ModuleEngines>();
            engines.RemoveAll(e => !e.EngineIgnited || !e.isOperational);
            float thrust = engines.Sum(e => e.MaxThrustOutputVac(true));

            List<ModuleRCS> RCS = v.FindPartModulesImplementing<ModuleRCS>();
            foreach (ModuleRCS thruster in RCS)
            {
                if (thruster.useThrottle)
                    thrust += thruster.thrusterPower;
            }

            return engines.Sum(e => e.MaxThrustOutputVac(true));
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
            Vector3 RelPos = Target.CoM - Firer.transform.position;
            Vector3 RelVel = Target.GetObtVelocity() - Firer.vessel.GetObtVelocity();

            // Quadratic equation coefficients a*t^2 + b*t + c = 0
            float a = Vector3.Dot(RelVel, RelVel) - TravelVelocity * TravelVelocity;
            float b = 2f * Vector3.Dot(RelVel, RelPos);
            float c = Vector3.Dot(RelPos, RelPos);

            float desc = b * b - 4f * a * c;
            float ForwardDelta = 2f * c / (Mathf.Sqrt(desc) - b);

            Vector3 leadPosition = Target.CoM + RelVel * ForwardDelta;
            return leadPosition - Firer.transform.position;
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

        public static Vector3 FromTo(Vessel v1, Vessel v2)
        {
             return v2.transform.position - v1.transform.position;
        }

        public static Vector3 RelVel(Vessel v1, Vessel v2)
        {
            return v1.GetObtVelocity() - v2.GetObtVelocity();
        }

        public static Vector3 AngularAcceleration(Vector3 torque, Vector3 MoI)
        {
            return new Vector3(MoI.x.Equals(0) ? float.MaxValue : torque.x / MoI.x,
                MoI.y.Equals(0) ? float.MaxValue : torque.y / MoI.y,
                MoI.z.Equals(0) ? float.MaxValue : torque.z / MoI.z);
        }

        public static float Integrate(float d, float a, float i = 0.1f, float v = 0)
        {
            float t = 0;

            while (d > 0)
            {
                v = v + a * i;
                d = d - v * i;
                t = t + i;
            }

            return t;
        }

        public static float SolveTime(float distance, float acceleration, float vel = 0)
        {
            float a = 0.5f * acceleration;
            float b = vel;
            float c = Mathf.Abs(distance) * -1;

            float x = (-b + Mathf.Sqrt(b * b - 4 * a * c)) / (2 * a);

            return x;
        }

        public static float SolveDistance(float time, float acceleration, float vel = 0)
        {
            return (vel * time) + 0.5f * acceleration * Mathf.Pow(time, 2);
        }

        public static float AngularVelocity(Vessel v, Vessel t)
        {
            Vector3 tv1 = FromTo(v, t);
            Vector3 tv2 = tv1 + RelVel(v, t);
            return Vector3.Angle(tv1.normalized, tv2.normalized);
        }

        public static string ShorternName(string name)
        {
            name = name.Split('(').First();
            name = name.Split('[').First();
            name = name.Replace(" - ", " ");
            name = name.Replace("-class", "");
            name = name.Replace("Heavy ", "");
            name = name.Replace("Light ", "");
            name = name.Replace("Frigate", "");
            name = name.Replace("Destroyer", "");
            name = name.Replace("Fighter", "");
            name = name.Replace("Debris", "");
            name = name.Replace("Probe", "");
            name = name.Replace("Lander", "");
            name = name.Replace("Ship", "");
            name = name.Replace("Plane", "");
            name = name.Replace("  ", " ");
            name = name.Trim();

            return name;
        }

        public static ModuleCommand FindCommand(Vessel craft)
        {
            //get a list of onboard control points and return the first found
            List<ModuleCommand> CommandPoints = craft.FindPartModulesImplementing<ModuleCommand>();
            if (CommandPoints.Count != 0)
            {
                return CommandPoints.First();
            }
            //gotta have a command point somewhere so this is just for compiling
            return null;
        }

        public static ModuleShipController FindController(Vessel v)
        {
            var ship = KCSController.ships.Find(m => m.vessel == v);

            if (ship == null)
                return v.FindPartModuleImplementing<ModuleShipController>(); 

            return ship;
        }

        public static float AveragedSize(Vessel v)
        {
            Vector3 size = v.vesselSize;
            return (size.x + size.y + size.z) / 3;
        }

        public static float FuelMass(List<Part> parts)
        {
            float totalMass = 0;
            foreach (Part part in parts)
            {
                if (part.partInfo.category == PartCategories.Coupling) break;
                totalMass += part.GetResourceMass();
            }

            return totalMass;
        }

        public static float DryMass(List<Part> parts)
        {
            float totalMass = 0;
            foreach (Part part in parts)
            {
                if (part.partInfo.category == PartCategories.Coupling) break;
                totalMass += part.mass;
            }

            return totalMass;
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

    public class Seperator
    {
        public bool isDockingPort = false;
        public ModuleDockingNode port;
        public ModuleDecouple decoupler;
        public Part part;
        public Transform transform { get => part.transform; }

        public void Separate()
        {
            if (isDockingPort)
                port.Decouple();
            else
                decoupler.Decouple();
        }
    }

    //public static class VesselExtensions
    //{
    //    public static Vector3 Velocity(this Vessel v)
    //    {
    //        return v.rootPart.Rigidbody.velocity;
    //    }
    //}
}
