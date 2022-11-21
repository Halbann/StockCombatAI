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
        private Vector3 attitudeLerped;
        private float error;
        private float angleLerp;
        public bool lerpAttitude = true;
        private float lerpRate;
        private bool lockAttitude = false;

        private bool facingDesiredRotation;
        public float throttle;
        public float throttleActual;
        private float throttleLerped;
        public float throttleLerpRate = 1;
        public float alignmentToleranceforBurn = 5;

        public Vector3 RCSVector;
        public float RCSPower = 3f;
        private Vessel controllingVessel;
        private Vector3 RCSThrust;
        private Vector3 up, right, forward;
        private float RCSThrottle;
        private Vector3 RCSVectorLerped = Vector3.zero;

        LineRenderer currentVectorLine, targetVectorLine;
        LineRenderer rcsLine;
        //LineRenderer rup, rright, rforward;

        public void Awake()
        {
            controllingVessel = gameObject.GetComponent<Part>().vessel;

            // initialise debug lines
            currentVectorLine = KCSDebug.CreateLine(Color.yellow);
            targetVectorLine = KCSDebug.CreateLine(Color.red);
            rcsLine = KCSDebug.CreateLine(Color.white);
            //rright = KCSDebug.CreateLine(Color.red);
            //rup = KCSDebug.CreateLine(Color.green);
            //rforward = KCSDebug.CreateLine(Color.blue);
        }

        internal void OnDestroy()
        {
            KCSDebug.DestroyLine(currentVectorLine);
            KCSDebug.DestroyLine(targetVectorLine);
            KCSDebug.DestroyLine(rcsLine);
            //KCSDebug.DestroyLine(rup);
            //KCSDebug.DestroyLine(rright);
            //KCSDebug.DestroyLine(rforward);
        }

        public void Drive()
        {
            if (controllingVessel == null)
            {
                Destroy(this);
                return;
            }

            error = Vector3.Angle(controllingVessel.ReferenceTransform.up, attitude); 

            UpdateSAS(controllingVessel);
            UpdateThrottle(controllingVessel);
            UpdateRCS(controllingVessel);
            // todo: implement own PID as alternative.
        }

        private void UpdateThrottle(Vessel v)
        {
            //if (throttle == 0 && throttleLerped == 0) return;
            //if (v == null) return;

            facingDesiredRotation = error < alignmentToleranceforBurn;
            throttleActual = facingDesiredRotation ? throttle : 0;

            // Move actual throttle towards throttle target gradually.
            throttleLerped = Mathf.MoveTowards(throttleLerped, throttleActual, throttleLerpRate * Time.fixedDeltaTime);

            v.ctrlState.mainThrottle = throttleLerped;
            //if (FlightGlobals.ActiveVessel != null && v == FlightGlobals.ActiveVessel)
            //    FlightInputHandler.state.mainThrottle = throttleLerped; //so that the on-screen throttle gauge reflects the autopilot throttle
        }

        void UpdateRCS (Vessel v)
        {
            if (RCSVector == Vector3.zero) return;

            if (RCSVectorLerped == Vector3.zero)
                RCSVectorLerped = RCSVector;

            // This system works for now but it's convuluted and isn't very stable.
            RCSVectorLerped = Vector3.Lerp(RCSVectorLerped, RCSVector, 5f * Time.fixedDeltaTime * Mathf.Clamp01(RCSVectorLerped.magnitude / RCSPower));
            RCSThrottle = Mathf.Lerp(0, 1.732f, Mathf.InverseLerp(0, RCSPower, RCSVectorLerped.magnitude));
            RCSThrust = RCSVectorLerped.normalized * RCSThrottle;
            
            up = v.ReferenceTransform.forward * -1;
            forward = v.ReferenceTransform.up * -1;
            right = Vector3.Cross(up, forward);

            v.ctrlState.X = Mathf.Clamp(Vector3.Dot(RCSThrust, right), -1, 1);
            v.ctrlState.Y = Mathf.Clamp(Vector3.Dot(RCSThrust, up), -1, 1);
            v.ctrlState.Z = Mathf.Clamp(Vector3.Dot(RCSThrust, forward), -1, 1);

            //Vector3 origin = v.ReferenceTransform.position;
            //KCSDebug.PlotLine(new[] { origin, origin + right * 10 * v.ctrlState.X }, rright);
            //KCSDebug.PlotLine(new[] { origin, origin + up * 10 *  v.ctrlState.Y}, rup);
            //KCSDebug.PlotLine(new[] { origin, origin + forward * 10 * v.ctrlState.Z}, rforward);

            Vector3 origin = v.ReferenceTransform.position;
            KCSDebug.PlotLine(new[]{ origin, origin + RCSThrust.normalized * Mathf.Clamp(RCSThrust.magnitude, 0, 15) }, rcsLine);
        }
        
        void UpdateSAS(Vessel v)
        {
            if (attitude == Vector3.zero || lockAttitude) return;
            //if (v == null) return;

            // SAS must be turned off. Don't know why.
            if (v.ActionGroups[KSPActionGroup.SAS])
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);

            var ap = v.Autopilot;
            if (ap == null) return;

            // The offline SAS must not be on stability assist. Normal seems to work on most probes.
            if (ap.Mode != VesselAutopilot.AutopilotMode.Normal)
                ap.SetMode(VesselAutopilot.AutopilotMode.Normal);

            // Lerp attitude while burning to reduce instability.
            if (lerpAttitude) {
                angleLerp = Mathf.InverseLerp(0, 10, error);
                lerpRate = Mathf.Lerp(1, 10, angleLerp);
                attitudeLerped = Vector3.Lerp(attitudeLerped, attitude, lerpRate * Time.deltaTime);
            }

            ap.SAS.SetTargetOrientation(throttleLerped > 0 && lerpAttitude ? attitudeLerped : attitude, false);

            // Update debug lines.
            Vector3 origin = v.ReferenceTransform.position;
            KCSDebug.PlotLine(new[] { origin, origin + v.ReferenceTransform.up * 50 }, currentVectorLine);
            KCSDebug.PlotLine(new[] { origin, origin + attitude * 50 }, targetVectorLine);
        }

        public void Stability(bool enable)
        {
            lockAttitude = enable;

            var ap = controllingVessel.Autopilot;
            if (ap == null) return;

            controllingVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, enable);
            ap.SetMode(enable ? VesselAutopilot.AutopilotMode.StabilityAssist : VesselAutopilot.AutopilotMode.Normal);
        }
    }
}
