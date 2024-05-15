using UnityEngine;

namespace KerbalCombatSystems.Fireworks
{
    internal class Fixer : MonoBehaviour
    {
        // Correct the velocity of a shell created by LaunchShell.
        // Delete it if it's too old, too slow, too far away or breaks on impact.

        private float launchTime;
        private Rigidbody rb;
        private ModulePartFirework firer;
        private bool destroyed = false;

        public static float deleteSpeed = 20f;
        public static float breakingForce = 20f;
        public static float shellLifetime = 30f;

        public static Fixer AddFixer(GameObject shell, ModulePartFirework firer)
        {
            var fixer = shell.AddComponent<Fixer>();
            fixer.firer = firer;

            return fixer;
        }

        protected void Awake()
        {
            launchTime = Time.fixedTime;
            rb = GetComponent<Rigidbody>();
        }

        protected void FixedUpdate()
        {
            if (Time.fixedTime - launchTime > shellLifetime)
                Destroy(gameObject);

            // todo: this doesn't apply to firework fireworks.
            //if (Vector3.Magnitude(rb.velocity - firer.vessel.rb_velocity) < deleteSpeed)
            //    Destroy(gameObject);
        }

        internal void OnCollisionEnter(Collision col)
        {
            if (destroyed)
                return;

            if (col.impulse.magnitude > breakingForce)
            {
                destroyed = true;
                Destroy(gameObject);
                // trigger effects.
            }
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
