using UnityEngine;

namespace KerbalCombatSystems
{
    public class ControlChecker
    {
        public float controlTimeout = 10;
        public float spinoutThreshold = 50;

        private readonly PartModule _host;
        private float _lastInControl;

        public ControlChecker(PartModule host)
        {
            _host = host;
        }

        public bool CheckControl()
        {
            bool spunOut = false;
            if (_host.vessel.angularVelocity.magnitude > spinoutThreshold)
            {
                if (Time.time - _lastInControl > controlTimeout)
                    spunOut = true;
            }
            else
                _lastInControl = Time.time;

            return !spunOut && _host.vessel.IsControllable;
        }
    }
}
