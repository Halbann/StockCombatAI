using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace KerbalCombatSystems
{
    public static partial class Utils
    {
        #region GetProperty
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

        public static float GetMaxThrust(List<ModuleEngines> engines)
        {
            float thrust = 0;

            foreach (ModuleEngines engine in engines)
            {
                if (!StatusChecker.Healthy(engine))
                    continue;

                thrust += engine.MaxThrustOutputVac(true);
            }

            return thrust;
        }

        public static Vector3 GetFireVector(List<ModuleEngines> engines, List<ModuleRCSFX> RCS = null, Vector3 thrustVector = default)
        {
            // Place linears first to establish a direction, not currently needed
            //RCS.Sort((a, b) => a.thrusterTransforms.Count().CompareTo(b.thrusterTransforms.Count()));

            if (engines?.Any() == true)
            {
                // If there are engines we can override any potential provided vector
                thrustVector = GetMeanVector(engines.First());
                foreach (ModuleEngines engine in engines.Skip(1))
                {
                    thrustVector += GetMeanVector(engine);
                }
                if (RCS?.Any() == true)
                {
                    // If there are engines we can add RCS on top
                    foreach (ModuleRCSFX thruster in RCS)
                    {
                        thrustVector += GetRCSVector(thruster, thrustVector);
                    }
                }
            }
            else if (RCS?.Any() == true)
            {
                // If there are no engines we have to plot the RCS along the provided vector
                Vector3 rcsVector = GetRCSVector(RCS.First(), thrustVector);
                foreach (ModuleRCSFX thruster in RCS.Skip(1))
                {
                    rcsVector += GetRCSVector(thruster, thrustVector);
                }
                //replace thrustVector with RCS Vector
                thrustVector = rcsVector;
            }

            return thrustVector;
        }

        public static Vector3 GetRCSVector(ModuleRCSFX thruster, Vector3 thrustVector)
        {
            //method to get the thrust vector of a specified rcs thruster
            List<Transform> positions = thruster.thrusterTransforms;
            Vector3 meanVector = Vector3.zero;

            foreach (Transform thrusterTransform in positions)
            {
                Vector3 pos = thrusterTransform.up * thruster.thrusterPower;
                // rcs will fire if thrust goes in the forward direction by any degree, this is reduced with angle offset
                if ( Vector3.Dot(thrustVector.normalized, pos.normalized) > 0)
                    meanVector += pos * Vector3.Dot(thrustVector.normalized, pos.normalized);
            }

            return meanVector;
        }

        public static Vector3 GetMeanVector(ModuleEngines thruster)
        {
            //method to get the thrust vector of a specified engine
            Vector3 meanVector = Vector3.zero;
            List<Transform> positions = thruster.thrustTransforms;

            foreach (Transform thrusterTransform in positions)
            {
                Vector3 pos = thrusterTransform.forward;
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

        // Create and set a new control point for a command module (commander) pointing along a world space vector (direction).
        // Uses: controlling missiles from the the average engine direction to allow for mistaken/unconventional probe core orientation.
        public static void AlignReference(ModuleCommand commander, Vector3 direction)
        {
            ControlPoint dynamic = commander.GetControlPoint("dynamic");
            // Check for an already existing dynamic control point
            if (dynamic == null)
            {
                // Create a new transform named dynamic.
                GameObject tc = new GameObject("dynamic");
                Transform transform = tc.transform;
                transform.SetParent(commander.transform);
                transform.position = commander.transform.position;

                // Create a new control point with the transform.
                dynamic = new ControlPoint("dynamic", "Dynamic", transform, Vector3.zero);

                // Add the control point to the command module and set it as active.
                commander.controlPoints.Add("dynamic", dynamic);
            }

            commander.SetControlPoint("dynamic");

            Vector3 referenceRoll = commander.part.GetReferenceTransform().forward;
            Vector3 roll = referenceRoll != direction.normalized ? referenceRoll : commander.transform.forward;

            // Orient the control point towards direction (finger) with perpendicular as the up vector (thumb).
            Vector3 perpendicular = Vector3.ProjectOnPlane(roll, direction.normalized);
            dynamic.transform.rotation = Quaternion.LookRotation(perpendicular, direction.normalized); // VAB orientation.
        }

        #endregion

        #region Finders
        public static ModuleCommand FindCommand(Vessel craft)
        {
            //get a list of onboard command pods and return one
            List<ModuleCommand> commandPoints = craft.FindPartModulesImplementing<ModuleCommand>();

            return commandPoints.FirstOrDefault();
        }

        public static ModuleShipController FindController(Vessel v)
        {
            var ship = FlightManager.ships.Find(m => m.vessel == v);

            if (ship == null)
                return v.FindPartModuleImplementing<ModuleShipController>();

            return ship;
        }

        public static ModuleDecouplerDesignate FindDecoupler(Part origin, string type = "Default")
        {
            // Search up the part tree to find a separator.

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

                //"" is shorthand for ignoring the type requirement and firing any decoupler
                if (type != "" && module.decouplerDesignation != type) continue;

                //strike any decouplers without any child parts
                if (!currentPart.FindChildParts<Part>(false).Any()) continue;

                return module;
            }

            return null;
        }

        // Search the children of a specified part for separators.
        public static List<ModuleDecouplerDesignate> FindDecouplerChildren(Part root, string type = "Default")
        {
            List<Part> childParts = root.FindChildParts<Part>(true).ToList();
            childParts.Insert(0, root); //check the parent itself

            List<ModuleDecouplerDesignate> seperatorList = new List<ModuleDecouplerDesignate>();
            ModuleDecouplerDesignate module;

            foreach (Part currentPart in childParts)
            {
                module = currentPart.GetComponent<ModuleDecouplerDesignate>();

                // make sure the decoupler designator exists and is the specified type
                if (module == null) continue;

                //"" is shorthand for ignoring the type requirement and firing any decoupler
                if (type != "" && module.decouplerDesignation != type) continue;

                //strike any decouplers without any child parts
                if (!currentPart.FindChildParts<Part>(false).ToList().Any()) continue;

                seperatorList.Add(module);
            }

            return seperatorList;
        }

        #endregion

        #region Graphics

        public static Color Desaturate(Color color, float sat)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);

            return Color.HSVToRGB(h, s * sat, v);
        }

        #endregion

        #region Physics
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

        public static float GetTolerance(Vector3 relativePosition, float targetSize, float tolerance)
        {
            // Determine the accuracy requirement (in degrees) based on the distance and size of the target.
            Vector3 targetRadius = Vector3.ProjectOnPlane(Vector3.up, relativePosition.normalized).normalized * (targetSize / 2) * tolerance;
            float aimTolerance = Vector3.Angle(relativePosition, relativePosition + targetRadius);

            return aimTolerance;
        }

        public static bool OnTarget(Vector3 targetAim, Vector3 currentAim, Vector3 relativePosition, float targetSize, float tolerance)
        {
            float requirement = GetTolerance(relativePosition, targetSize, tolerance);
            float current = Vector3.Angle(targetAim.normalized, currentAim);

            return current < requirement;
        }

        public static float AngularVelocity(Vessel v, Vessel t, float window) =>
            Vector3.Angle(v.Pos(t), v.Pos(t) + v.Vel(t) * window) / window;

        /// <summary>
        /// Get the relative position of a target vessel in the reference frame of the vessel.
        /// </summary>
        public static Vector3 Pos(this Vessel vessel, Vessel target) =>
            target.Pos() - vessel.Pos();

        /// <summary>
        /// Get the relative velocity of a target vessel in the reference frame of the vessel.
        /// </summary>
        public static Vector3 Vel(this Vessel vessel, Vessel target) =>
            target.Vel() - vessel.Vel();

        public static Vector3 Pos(this Vessel vessel) =>
            vessel.CoM;

        public static Vector3 Vel(this Vessel vessel) =>
            vessel.obt_velocity;

        public static float SolveTime(float distance, float acceleration, float vel = 0)
        {
            float a = 0.5f * acceleration;
            float b = vel;
            float c = Mathf.Abs(distance) * -1;

            float x = (-b + Mathf.Sqrt(b * b - 4 * a * c)) / (2 * a);

            return x;
        }

        public static Vector3 PredictPosition(Vector3 position, Vector3 velocity, Vector3 acceleration, float time)
        {
            return position + Displacement(velocity, acceleration, time);
        }

        public static Vector3 PredictPosition(Vector3 position, Vector3 velocity, float time)
        {
            return position + velocity * time;
        }

        public static Vector3 Displacement(Vector3 velocity, Vector3 acceleration, float time)
        {
            return velocity * time + 0.5f * acceleration * Mathf.Pow(time, 2);
        }

        public static Vector3 Displacement(Vector3 velocity, Vector3 acceleration, Vector3 jerk, float time)
        {
            return velocity * time
                + 0.5f * acceleration * Mathf.Pow(time, 2)
                + (1f / 6f) * jerk * Mathf.Pow(time, 3);
        }

        public static float Displacement(float speed, float acceleration, float jerk, float time)
        {
            return Displacement(speed, acceleration, time)
                + (1f / 6f) * jerk * Mathf.Pow(time, 3);
        }

        public static float Displacement(float speed, float acceleration, float time)
        {
            return speed * time
                + 0.5f * acceleration * Mathf.Pow(time, 2);
        }

        public static float Displacement(float speed, float acceleration)
        {
            return Mathf.Pow(speed, 2) / (2 * acceleration);
        }

        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
        }

        public static float ToAngle(this float angle)
        {
            angle = (angle + 180) % 360;
            return angle > 0 ? angle - 180 : angle + 180;
        }

        public static float AngleDifference(float a, float b)
        {
            return (a - b + 540) % 360 - 180;
        }

        public static Vector3 ClosestApproach(Vector3 pos, Vector3 vel)
        {
            return pos + vel * ClosestTimeToCPA(pos, vel);
        }

        #endregion

        #region Maths

        public static bool Approximately(float a, float b, float margin)
        {
            return Mathf.Abs(a - b) < a * margin;
        }

        public static float Map(float value, float a, float b, float min, float max)
        {
            float position = Mathf.InverseLerp(a, b, value);
            return Mathf.Lerp(min, max, position);
        }

        #endregion

        #region Projectile Leading

        public static Vector3 TargetLead(Vessel target, Part firer, float travelVelocity)
        {
            Vector3 relPos = target.CoM - firer.transform.position;
            Vector3 relVel = target.GetObtVelocity() - firer.vessel.GetObtVelocity();
            Vector3 relAcc = target.acceleration - firer.vessel.acceleration;

            float timeToHit = ClosestTimeToCPA(relPos, relVel + (relPos.normalized * travelVelocity * -1), relAcc, 60);
            Vector3 leadPosition = PredictPosition(relPos, relVel, relAcc, timeToHit);

            return leadPosition;
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

    #endregion
}
