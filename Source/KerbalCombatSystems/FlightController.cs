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
        public Vector3 attitude;
        private bool facingDesiredRotation;
        public float throttle;
        private float throttleLerped;
        public float throttleLerpRate = 0.05f;
        public float alignmentToleranceforBurn = 5;

        private Vessel controllingVessel;

        LineRenderer currentVectorLine, targetVectorLine;

        public void Start()
        {
            controllingVessel = gameObject.GetComponent<Part>().vessel;

            // initialise debug lines
            currentVectorLine = KCSDebug.CreateLine(Color.yellow);
            targetVectorLine = KCSDebug.CreateLine(Color.red);
        }

        private void OnDestroy()
        {
            KCSDebug.DestroyLine(currentVectorLine);
            KCSDebug.DestroyLine(targetVectorLine);
        }

        public void Drive()
        {
            UpdateSAS(controllingVessel);
            UpdateThrottle(controllingVessel);
            // todo: implement own PID as alternative.
        }

        private void UpdateThrottle(Vessel v)
        {
            var af = Vector3.Angle(v.ReferenceTransform.up, attitude); 

            facingDesiredRotation = af < alignmentToleranceforBurn;
            throttle = facingDesiredRotation ? throttle : 0;

            // Move actual throttle towards throttle target gradually.
            throttleLerped = Mathf.MoveTowards(throttleLerped, throttle, 1 * Time.fixedDeltaTime);

            v.ctrlState.mainThrottle = throttleLerped;
            if (FlightGlobals.ActiveVessel != null && v == FlightGlobals.ActiveVessel)
                FlightInputHandler.state.mainThrottle = throttleLerped; //so that the on-screen throttle gauge reflects the autopilot throttle
        }

        void UpdateSAS(Vessel v)
        {
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

            ap.SAS.SetTargetOrientation(attitude, false);

            // Update debug lines.
            Vector3 origin = v.ReferenceTransform.position;
            KCSDebug.PlotLine(new[]{ origin, origin + v.ReferenceTransform.up * 50}, currentVectorLine);
            KCSDebug.PlotLine(new[]{ origin, origin + attitude * 50}, targetVectorLine);
        }
    }
}
