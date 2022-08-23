using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    class KCSFlightController : MonoBehaviour
    {
        public Vector3 attitude = Vector3.zero;
        private bool facingDesiredRotation;
        public float throttle;
        public float throttleActual;
        private float throttleLerped;
        public float throttleLerpRate = 1;
        public float alignmentToleranceforBurn = 5;
        private Vector3 attitudeLerped;
        private float error;
        private float angleLerp;
        private float lerpRate;

        private Vessel controllingVessel;

        LineRenderer currentVectorLine, targetVectorLine;

        public void Start()
        {
            controllingVessel = gameObject.GetComponent<Part>().vessel;

            // initialise debug lines
            currentVectorLine = KCSDebug.CreateLine(Color.yellow);
            targetVectorLine = KCSDebug.CreateLine(Color.red);
        }

        internal void OnDestroy()
        {
            KCSDebug.DestroyLine(currentVectorLine);
            KCSDebug.DestroyLine(targetVectorLine);
        }

        public void Drive()
        {
            error = Vector3.Angle(controllingVessel.ReferenceTransform.up, attitude); 

            UpdateSAS(controllingVessel);
            UpdateThrottle(controllingVessel);
            // todo: implement own PID as alternative.
        }

        private void UpdateThrottle(Vessel v)
        {
            //if (throttle == 0 && throttleLerped == 0) return;
            if (v == null) return;

            facingDesiredRotation = error < alignmentToleranceforBurn;
            throttleActual = facingDesiredRotation ? throttle : 0;

            // Move actual throttle towards throttle target gradually.
            throttleLerped = Mathf.MoveTowards(throttleLerped, throttleActual, throttleLerpRate * Time.fixedDeltaTime);

            v.ctrlState.mainThrottle = throttleLerped;
            if (FlightGlobals.ActiveVessel != null && v == FlightGlobals.ActiveVessel)
                FlightInputHandler.state.mainThrottle = throttleLerped; //so that the on-screen throttle gauge reflects the autopilot throttle
        }

        void UpdateSAS(Vessel v)
        {
            if (attitude == Vector3.zero) return;
            if (v == null) return;

            // SAS must be turned off. Don't know why.
            if (v.ActionGroups[KSPActionGroup.SAS])
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);

            var ap = v.Autopilot;
            if (ap == null) return;

            // The offline SAS must not be on stability assist. Normal seems to work on most probes.
            if (ap.Mode != VesselAutopilot.AutopilotMode.Normal)
            {
                ap.SetMode(VesselAutopilot.AutopilotMode.Normal);
            }

            // Lerp attitude while burning to reduce instability.
            angleLerp = Mathf.InverseLerp(0, 10, error);
            lerpRate = Mathf.Lerp(1, 10, angleLerp);
            attitudeLerped = Vector3.Lerp(attitudeLerped, attitude, lerpRate * Time.deltaTime);

            ap.SAS.SetTargetOrientation(throttleLerped > 0 ? attitudeLerped : attitude, false);

            // Update debug lines.
            Vector3 origin = v.ReferenceTransform.position;
            KCSDebug.PlotLine(new[]{ origin, origin + v.ReferenceTransform.up * 50}, currentVectorLine);
            KCSDebug.PlotLine(new[]{ origin, origin + attitudeLerped * 50}, targetVectorLine);
        }
    }
}
