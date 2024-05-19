using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace KerbalCombatSystems
{
    public static class VesselBounds
    {
        private const float cacheTimeout = 10f;

        private struct Cache
        {
            internal int partCount;
            internal Vessel vessel;
            internal Bounds bounds;
            internal float timestamp;

            internal Cache(Vessel vessel)
            {
                partCount = vessel.Parts.Count;
                bounds = CalculateBoundsLocal(vessel);
                this.vessel = vessel;
                timestamp = Time.time;
            }
        }

        private static readonly Dictionary<uint, Cache> cache = new Dictionary<uint, Cache>();

        public static Bounds GetBoundsLocal(Vessel vessel)
        {
            var id = vessel.persistentId;

            if (cache.TryGetValue(id, out Cache result))
            {
                bool expired = Time.time - result.timestamp > cacheTimeout;

                if (!expired || result.partCount == vessel.Parts.Count)
                {
                    return result.bounds;
                }
                else
                {
                    cache.Remove(id);
                }
            }

            Cache entry = new Cache(vessel);
            cache.Add(id, entry);

            return entry.bounds;
        }

        public static Bounds CalculateBoundsLocal(Vessel vessel)
        {
            if (vessel.parts.Count == 0)
                return default;

            Transform local = vessel.transform;
            Vector3 comLocalspace = local.InverseTransformPoint(vessel.CoM);
            Bounds vesselBounds = new Bounds(comLocalspace, Vector3.zero);

            Bounds meshBounds, partBounds;
            Vector3 partPositionVessel, meshCentreWorldSpace, meshSizeWorldSpace;

            foreach (Part part in vessel.parts)
            {
                partPositionVessel = local.InverseTransformPoint(part.transform.position);
                partBounds = new Bounds(partPositionVessel, Vector3.zero); // Vector3.one

                foreach (var renderer in part.GetPartColliders().ToList())
                {
                    meshBounds = renderer switch
                    {
                        MeshCollider meshCollider =>      meshCollider.sharedMesh.bounds,
                        BoxCollider boxCollider =>        new Bounds(boxCollider.center, boxCollider.size),
                        SphereCollider sphereCollider =>  new Bounds(sphereCollider.center, Vector3.one * sphereCollider.radius * 2),
                        _ =>                              new Bounds(Vector3.zero, Vector3.zero),
                    };

                    meshCentreWorldSpace = renderer.transform.TransformPoint(meshBounds.center);
                    meshBounds.center = local.InverseTransformPoint(meshCentreWorldSpace);

                    meshSizeWorldSpace = renderer.transform.TransformVector(meshBounds.size);
                    meshBounds.size = local.InverseTransformVector(meshSizeWorldSpace);

                    partBounds.Encapsulate(meshBounds);
                }

                vesselBounds.Encapsulate(partBounds);
            }

            // The resulting bounds is a W,H,D vector in ReferenceTransform space, centered on the CoM.
            return vesselBounds;
        }


        /*public static Bounds CalculateCraftSize(List<Part> parts, Part rootPart)
        {
            // Returns the size (width, height, depth) of an AABB
            // centered on the root part of the vessel.

            if (parts.Count == 0 || rootPart == null)
                return default;

            Bounds vesselBounds = new Bounds(rootPart.transform.root.position, Vector3.zero);

            int count = parts.Count;
            Part part;
            Bounds partBounds;

            for (int i = 0; i < count; i++)
            {
                part = parts[i];
                partBounds = new Bounds(part.transform.position, Vector3.zero);

                foreach (var colliderBounds in part.GetColliderBounds())
                    partBounds.Encapsulate(colliderBounds);

                vesselBounds.Encapsulate(partBounds);
            }

            // Merge all bounds into one all encapsulating bounds.
            return vesselBounds;
        }*/
    }
}
