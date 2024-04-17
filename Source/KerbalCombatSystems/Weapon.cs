using UnityEngine;

namespace KerbalCombatSystems
{
    public class ModuleWeapon : PartModule
    {
        virtual public void Fire() { }

        virtual public Vector3 Aim()
        {
            return Vector3.zero;
        }

        virtual public void Setup() { }

        virtual public void UpdateSettings() { }
    }
}
