using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalCombatSystems
{
    public static partial class KCS
    {
        #region GetProperty
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

        public static float GetMaxAcceleration(Vessel v)
        {
            return GetMaxThrust(v) / v.GetTotalMass();
        }

        public static float GetMaxThrust(Vessel v)
        {
            List<ModuleEngines> engines = v.FindPartModulesImplementing<ModuleEngines>();
            //The basic ModuleRCS is depreciated and doesn't work properly with multiple nozzle rcs parts
            List<ModuleRCSFX> RCS = v.FindPartModulesImplementing<ModuleRCSFX>();

            return GetFireVector(engines, RCS, v.transform.position).magnitude;
        }

        public static Vector3 GetFireVector(List<ModuleEngines> engines, List<ModuleRCSFX> RCS, Vector3 origin)
        {
            //method to get the mean thrust vector of a list of engines and throttle enabled RCS

            //start the expected movement vector at the first child of the decoupler
            Vector3 thrustVector = origin;

            engines.RemoveAll(e => !e.EngineIgnited || !e.isOperational);
            RCS.RemoveAll(r => !r.useThrottle || !r.isEnabled);
            //place linears first to establish a direction
            RCS.Sort((a, b) => a.thrusterTransforms.Count().CompareTo(b.thrusterTransforms.Count()));

            foreach (ModuleEngines engine in engines)
            {
                if (engines.First() == engine)
                    thrustVector = GetMeanVector(engine);
                thrustVector += GetMeanVector(engine);
            }

            foreach (ModuleRCSFX thruster in RCS)
            {
                thrustVector = GetRCSVector(thruster, thrustVector);
            }

            return thrustVector;
        }

        public static Vector3 GetRCSVector(ModuleRCSFX thruster, Vector3 thrustVector)
        {
            //method to get the thrust vector of a specified rcs thruster
            Vector3 meanVector = Vector3.zero;
            List<Transform> positions = thruster.thrusterTransforms;

            foreach (Transform thrusterTransform in positions)
            {
                Vector3 pos = thrusterTransform.forward;
                float angle = Vector3.Angle(thrustVector, pos);
                // rcs will fire if facing in any degree forwards so include them only
                if (angle > 90)
                    thrustVector += (pos.normalized * thruster.thrusterPower);
            }

            return thrustVector;
        }

        public static Vector3 GetMeanVector(ModuleEngines thruster)
        {
            //method to get the thrust vector of a specific part, 
            //which in some cases is not the part vector
            Vector3 meanVector = Vector3.zero;
            List<Transform> positions = thruster.thrustTransforms;

            foreach (Transform thrusterTransform in positions)
            {
                Vector3 pos = thrusterTransform.forward;
                if (positions.First() == thrusterTransform)
                    meanVector = pos;
                meanVector += pos;
            }

            //get vector and set length to the thruster power
            meanVector = (meanVector.normalized * thruster.MaxThrustOutputVac(true));
            return meanVector;
        }

        #endregion

        #region DoAction
        public static string ShortenName(string name)
        {
            name = name.Split('(').First();
            name = name.Split('[').First();
            name = name.Replace(" - ", " ");
            name = name.Replace("-class", "");
            name = name.Replace("Heavy ", "");
            name = name.Replace("Light ", "");
            name = name.Replace("Frigate", "");
            name = name.Replace("Destroyer", "");
            name = name.Replace("Cruiser", "");
            name = name.Replace("Dreadnought", "");
            name = name.Replace("Corvette", "");
            name = name.Replace("Carrier", "");
            name = name.Replace("Battleship", "");
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

        public static void TryToggle(bool Direction, ModuleAnimationGroup Animation)
        {
            if (Direction && Animation.isDeployed == false)
            {
                //try deploy if not already
                Animation.DeployModule();
            }
            else if (!Direction && Animation.isDeployed == true)
            {
                //try retract if not already
                Animation.RetractModule();
            }

            //do nothing otherwise
        }

        // Create and set a new control point for a command module (commander) pointing along a world space vector (direction).
        // Uses: controlling missiles from the the average engine direction to allow for mistaken/unconventional probe core orientation.
        public static void AlignReference(ModuleCommand commander, Vector3 direction)
        {
            // Create a new transform named dynamic.
            GameObject tc = new GameObject("dynamic");
            Transform transform = tc.transform;
            transform.SetParent(commander.transform);

            // Create a new control point with the transform.
            ControlPoint dynamic = new ControlPoint("dynamic", "Dynamic", transform, Vector3.zero);

            // Orient the control point towards direction (finger) with perpendicular as the up vector (thumb).
            Vector3 perpendicular = Vector3.ProjectOnPlane(commander.transform.forward, direction);
            dynamic.transform.rotation = Quaternion.LookRotation(perpendicular, direction); // VAB orientation.

            // Add the control point to the command module and set it as active.
            commander.controlPoints.Add("dynamic", dynamic);
            commander.SetControlPoint("dynamic");
        }

        #endregion

        #region Finders
        public static ModuleCommand FindCommand(Vessel craft)
        {
            //get a list of onboard command pods and return one
            List<ModuleCommand> commandPoints = craft.FindPartModulesImplementing<ModuleCommand>();
            if (commandPoints.Count != 0)
            {
                return commandPoints.First();
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

        public static ModuleDecouplerDesignate FindDecoupler(Part origin, string type)
        {
            // method to search up the part tree to find a single decoupler
            bool defaultCoupler = (type != "" || type != null || type != "Default");

            Part currentPart;
            Part nextPart = origin.parent;
            ModuleDecouplerDesignate module;

            while (nextPart != null)
            {
                currentPart = nextPart;
                nextPart = currentPart.parent;

                // make sure the decoupler designator exists and is the specified type
                module = currentPart.GetComponent<ModuleDecouplerDesignate>();
                if (module == null) continue;
                if (module.decouplerDesignation != type && !defaultCoupler) continue;
                //strike any decouplers without any child parts
                if (!currentPart.FindChildParts<Part>(true).ToList().Any()) continue;

                return module;
            }

            return null;
        }

        public static List<ModuleDecouplerDesignate> FindDecouplerChildren(Part root, string type)
        {
            // method to search the children of a specified part for decoupler modules
            bool defaultCoupler = (type != "" || type != null || type != "Default");

            List<Part> childParts = root.FindChildParts<Part>(true).ToList();
            //check the parent itself
            childParts.Insert(0, root);
            //spawn empty modules list to add to
            List<ModuleDecouplerDesignate> seperatorList = new List<ModuleDecouplerDesignate>();
            ModuleDecouplerDesignate module;

            foreach (Part currentPart in childParts)
            {
                module = currentPart.GetComponent<ModuleDecouplerDesignate>();

                // make sure the decoupler designator exists and is the specified type
                if (module == null) continue;
                if (module.decouplerDesignation != type && !defaultCoupler) continue;
                //strike any decouplers without any child parts
                if (!currentPart.FindChildParts<Part>(true).ToList().Any()) continue;

                seperatorList.Add(module);
            }

            return seperatorList;
        }
        #endregion

        #region Physics Calculations
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

        public static float AngularVelocity(Vessel v, Vessel t)
        {
            Vector3 tv1 = FromTo(v, t);
            Vector3 tv2 = tv1 + RelVel(v, t);
            return Vector3.Angle(tv1.normalized, tv2.normalized);
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

        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
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

        #endregion
    }

    #region Objects

    //todo: update with naming convention basis
    public enum Side
    {
        A,
        B
    }

    /*public static class VesselExtensions
    {
        public static Vector3 Velocity(this Vessel v)
        {
            return v.rootPart.Rigidbody.velocity;
        }
    }*/

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
    #endregion
}
