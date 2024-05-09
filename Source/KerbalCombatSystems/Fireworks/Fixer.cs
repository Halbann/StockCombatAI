using UnityEngine;

namespace KerbalCombatSystems.Fireworks
{
    internal class Fixer
    {
        // Correct the velocity of a shell created by LaunchShell.

        internal static void CorrectStockShell(GameObject shell, ModulePartFirework launcher)
        {
            Rigidbody rb = shell.GetComponent<Rigidbody>();
            physicalObject phys = shell.GetComponent<physicalObject>();

            Vector3 forceDirection = launcher.gameObject.GetChild(launcher.cannonName).transform.up;
            Vector3 counterForce = -1 * forceDirection * launcher.shellMass * (launcher.shellVelocity / Time.fixedDeltaTime) * launcher.shellRBMassScaleValue;

            rb.drag = 0f;
            rb.angularVelocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.angularDrag = 0;
            rb.AddForce(counterForce);
            rb.velocity = launcher.vessel.rb_velocity;

            launcher.part.RigidBodyPart.force.Zero();
            phys.origDrag = 0f;
            Object.Destroy(shell.GetComponent<CollisionEnhancer>());

            rb.velocity += forceDirection * launcher.shellVelocity;
        }
    }
}
