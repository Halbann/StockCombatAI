using System.Collections.Generic;
using UnityEngine;

namespace KerbalCombatSystems.Fireworks
{
    internal class Fixer : MonoBehaviour
    {
        // Correct the velocity of a shell created by LaunchShell.
        // Delete it if it's too old, too slow, too far away or breaks on impact.

        private float launchTime;
        private Rigidbody rb;
        private readonly Dictionary<uint, Vessel> speedChecks = new Dictionary<uint, Vessel>();
        private bool destroyed = false;
        private bool collided = false;

        public static float deleteSpeed = 20f;
        public static float breakingImpulse = 20f;
        public static float shellLifetime = 30f;

        protected void Awake()
        {
            launchTime = Time.fixedTime;
            rb = GetComponent<Rigidbody>();
        }

        protected void FixedUpdate()
        {
            if (Time.fixedTime - launchTime > shellLifetime)
                Destroy(gameObject);

            if (collided)
                CheckSpeed();
        }

        private void CheckSpeed()
        {
            // It's visually unappealing when a shell, for whatever reason,
            // is left trapped inside a vessel or just floating beside it, and
            // bad for performance.

            // After at least one collision, delete the shell if it's slowed down a lot
            // relative to any of the vessels it's collided with.

            foreach (var vessel in speedChecks.Values)
            {
                if (Vector3.Magnitude(rb.velocity - vessel.rb_velocity) < deleteSpeed)
                {
                    Destroy(gameObject);
                    break;
                }
            }
        }

        internal void OnCollisionEnter(Collision col)
        {
            if (destroyed)
                return;

            collided = true;

            if (col.impulse.magnitude > breakingImpulse)
            {
                destroyed = true;
                Destroy(gameObject);
                // trigger effects.
                // apply decal
            }
            // Add the vessel to the speed check list.
            var part = FlightGlobals.GetPartUpwardsCached(col.gameObject);
            if (part == null)
                return;

            if (speedChecks.ContainsKey(part.vessel.persistentId))
                return;

            speedChecks.Add(part.vessel.persistentId, part.vessel);
        }

        internal static void CorrectStockShell(GameObject shell, ModulePartFirework launcher)
        {
            Rigidbody rb = shell.GetComponent<Rigidbody>();
            physicalObject phys = shell.GetComponent<physicalObject>();

            Vector3 forceDirection = launcher.gameObject.GetChild(launcher.cannonName).transform.up;
            Vector3 counterForce = -1 * forceDirection * launcher.shellMass * (launcher.shellVelocity / Time.fixedDeltaTime) * launcher.shellRBMassScaleValue;

            rb.drag = 0f;
            rb.angularVelocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.detectCollisions = false;
            rb.angularDrag = 0;
            rb.AddForce(counterForce);
            rb.velocity = launcher.vessel.rb_velocity;

            launcher.part.RigidBodyPart.force.Zero();
            phys.origDrag = 0f;
            Destroy(shell.GetComponent<CollisionEnhancer>());

            rb.velocity += forceDirection * launcher.shellVelocity; // How to make this shell specific eventually?
        }
    }
}
