using UnityEngine;

namespace KerbalCombatSystems
{
    public static partial class Utils
    {
        public static Vessel raycastTarget;
        public static int raycastLayer = 17;

        public static void PrepareRaycast(Vessel vessel)
        {
            if (vessel == null)
                return;

            raycastTarget = vessel;
            SetVesselLayer(vessel, raycastLayer);
        }

        public static bool Raycast(Ray r, float maxDistance = 50f)
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
            {
                foreach (Collider c in p.GetPartColliders())
                {
                    c.gameObject.layer = layer;
                }
            }
        }

        public static bool RayIntersectsVessel(Vessel v, Ray r)
        {
            foreach (Part p in v.parts)
            {
                foreach (Collider c in p.GetPartColliders())
                {
                    if (c.Raycast(r, out _, 50f))
                        return true;
                }
            }

            return false;
        }

        // todo: make prepare/finish compatible and use capsule.
        public static bool CylinderIntersectsVessel(Vessel v, Ray r, float radius, int sides = 4)
        {
            Ray edgeRay = new Ray(r.origin, r.direction);
            Vector3 cylinderEdge = Vector3.ProjectOnPlane(Vector3.up, r.direction).normalized * radius;

            for (int i = 0; i < sides; i++)
            {
                edgeRay.origin = r.origin + (Quaternion.AngleAxis(360f * (i / (float)sides), r.direction) * cylinderEdge);

                foreach (Part p in v.parts)
                {
                    foreach (Collider c in p.GetPartColliders())
                    {
                        if (c.Raycast(edgeRay, out _, 50f))
                            return true;
                    }
                }
            }

            return false;
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
