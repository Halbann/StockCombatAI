using UnityEngine;

using MechJebLib.Maneuvers;
using MechJebLib.Primitives;

namespace KerbalCombatSystems
{
    public static partial class Utils
    {
        // Static functions to hide MechJebLib complexity.

        public static Vector3d DeltaVToChangePeriapsis(Orbit o, double ut, double newPeR)
        {
            double radius = o.GetRadiusAtUT(ut);

            newPeR = Mathf.Clamp((float)newPeR, 0 + 1, (float)radius - 1);

            o.GetOrbitalStateVectorsAtUT(ut, out Vector3d pos, out Vector3d vel);
            V3 r = new V3(pos.x, pos.y, pos.z);
            V3 v = new V3(vel.x, vel.y, vel.z);

            V3 dv = ChangeOrbitalElement.ChangePeriapsis(o.referenceBody.gravParameter, r, v, newPeR);
            Vector3d dv3d = new Vector3d(dv.x, dv.y, dv.z);

            return dv3d.xzy;
        }
    }
}
