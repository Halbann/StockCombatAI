using UnityEngine;

namespace KerbalCombatSystems
{
    public static partial class Utils
    {
        public static Vessel raycastTarget;
        public static int raycastLayer = 17;
        public static int ignoreLayer = 17;
        public const float maxDistance = 10;

        public static void PrepareForRaycast(Vessel vessel)
        {
            if (vessel == null)
                return;

            raycastTarget = vessel;
            SetVesselLayer(vessel, raycastLayer);
        }

        public static bool RaycastMulti(Ray r, float maxDistance = maxDistance)
        {
            int layerMask = 1 << raycastLayer;
            return Physics.Raycast(r, maxDistance, layerMask);
        }

        public static void FinishRaycast()
        {
            SetVesselLayer(raycastTarget, 0);
        }

        private static void SetVesselLayer(Vessel vessel, int layer)
        {
            // Set all parts on the vessel to the layer. 

            foreach (Part p in vessel.parts)
                foreach (Collider c in p.GetPartColliders())
                    c.gameObject.layer = layer;
        }

        //public static bool RayIntersectsVessel(Vessel v, Ray r)
        //{
        //    foreach (Part p in v.parts)
        //    {
        //        foreach (Collider c in p.GetPartColliders())
        //        {
        //            if (c.Raycast(r, out _, 50f))
        //                return true;
        //        }
        //    }

        //    return false;
        //}

        public static bool RayIntersectsVessel(Vessel _, Ray r, float maxDistance = maxDistance)
        {
            // Surprisingly, this is always faster than RaycastMulti and the above method, even with >2000 parts across three vessels.
            // I shouldn't try to optimise a black box.

            return Physics.Raycast(r, maxDistance);
        }

        public static bool RayIntersectsAny(Vessel ignoreVessel, Ray r, float maxDistance = maxDistance)
        {
            SetVesselLayer(ignoreVessel, ignoreLayer);
            bool hit = RayIntersectsAnyMulti(r, maxDistance);
            SetVesselLayer(ignoreVessel, 0);

            return hit;
        }

        public static bool RayIntersectsAnyMulti(Ray r, float maxDistance = maxDistance)
        {
            var layerMask = 1 << 0;
            bool hit = Physics.Raycast(r, maxDistance, layerMask);

            return hit;
        }

        // todo: make prepare/finish compatible and use capsule.
        public static bool CylinderIntersectsAny(Vessel ignore, Ray r, float radius, int sides = 4)
        {
            Ray edgeRay = new Ray(r.origin, r.direction);
            Vector3 cylinderEdge = Vector3.ProjectOnPlane(Vector3.up, r.direction).normalized * radius;

            PrepareForRaycast(ignore);
            bool hit = false;

            for (int i = 0; i < sides; i++)
            {
                edgeRay.origin = r.origin + (Quaternion.AngleAxis(360f * (i / (float)sides), r.direction) * cylinderEdge);
                if (RayIntersectsAnyMulti(edgeRay))
                {
                    hit = true;
                    break;
                }
            }

            FinishRaycast();

            return hit;
        }

        public static bool RayIntersectSphere(Ray ray, Vector3 centre, float radius)
        {
            // Check if a ray intersects a sphere.

            Vector3 toSphere = centre - ray.origin;

            // Check inside.
            if (toSphere.magnitude < radius)
                return true;

            ray.direction = ray.direction.normalized;

            // Check dot.
            if (Vector3.Dot(toSphere.normalized, ray.direction) < 0)
                return false;

            float a = Vector3.Dot(ray.direction, ray.direction);
            float b = 2.0f * Vector3.Dot(ray.origin, ray.direction);
            float c = Vector3.Dot(ray.origin, ray.origin) - radius * radius;

            float discriminant = b * b - 4.0f * a * c;

            return discriminant >= 0;
        }
    }
}