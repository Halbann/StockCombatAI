using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    static partial class KCS
    {

        // Originally written by Brett Ryland and josuenos. Copied from https://github.com/BrettRyland/BDArmory
        // This code is distributed under CC-BY-SA 2.0: https://creativecommons.org/licenses/by-sa/2.0/

        // Predict the next time to the closest point of approach within the next maxTime seconds for two accelerating rigidbodies.
        public static float ClosestTimeToCPA(Vector3 relPosition, Vector3 relVelocity, Vector3 relAcceleration, float maxTime)
        {
            float A = Vector3.Dot(relAcceleration, relAcceleration) / 2f;
            float B = Vector3.Dot(relVelocity, relAcceleration) * 3f / 2f;
            float C = Vector3.Dot(relVelocity, relVelocity) + Vector3.Dot(relPosition, relAcceleration);
            float D = Vector3.Dot(relPosition, relVelocity);
            if (A == 0) // Not actually a cubic. Relative acceleration is zero, so return the much simpler linear timeToCPA.
            {
                return Mathf.Clamp(-Vector3.Dot(relPosition, relVelocity) / relVelocity.sqrMagnitude, 0f, maxTime);
            }
            float D0 = B * B - 3f * A * C;
            float D1 = 2 * B * B * B - 9f * A * B * C + 27f * A * A * D;
            float E = D1 * D1 - 4f * D0 * D0 * D0; // = -27*A^2*discriminant
            // float discriminant = 18f * A * B * C * D - 4f * Mathf.Pow(B, 3f) * D + Mathf.Pow(B, 2f) * Mathf.Pow(C, 2f) - 4f * A * Mathf.Pow(C, 3f) - 27f * Mathf.Pow(A, 2f) * Mathf.Pow(D, 2f);
            if (E > 0)
            { // Single solution (E is positive)
                float F = (D1 + Mathf.Sign(D1) * Mathf.Sqrt(E)) / 2f;
                float G = Mathf.Sign(F) * Mathf.Pow(Mathf.Abs(F), 1f / 3f);
                float time = -1f / 3f / A * (B + G + D0 / G);
                return Mathf.Clamp(time, 0f, maxTime);
            }
            else if (E < 0)
            { // Triple solution (E is negative)
                float F_real = D1 / 2f;
                float F_imag = Mathf.Sign(D1) * Mathf.Sqrt(-E) / 2f;
                float F_abs = Mathf.Sqrt(F_real * F_real + F_imag * F_imag);
                float F_ang = Mathf.Atan2(F_imag, F_real);
                float G_abs = Mathf.Pow(F_abs, 1f / 3f);
                float G_ang = F_ang / 3f;
                float time = -1f;
                for (int i = 0; i < 3; ++i)
                {
                    float G = G_abs * Mathf.Cos(G_ang + 2f * (float)i * Mathf.PI / 3f);
                    float t = -1f / 3f / A * (B + G + D0 * G / G_abs / G_abs);
                    if (t > 0f && Mathf.Sign(Vector3.Dot(relVelocity, relVelocity) + Vector3.Dot(relPosition, relAcceleration) + 3f * t * Vector3.Dot(relVelocity, relAcceleration) + 3f / 2f * t * t * Vector3.Dot(relAcceleration, relAcceleration)) > 0)
                    { // It's a minimum and in the future.
                        if (time < 0f || t < time) // Update the closest time.
                            time = t;
                    }
                }
                return Mathf.Clamp(time, 0f, maxTime);
            }
            else
            { // Repeated root
                if (Mathf.Abs(B * B - 2f * A * C) < 1e-7)
                { // A triple-root.
                    return Mathf.Clamp(-B / 3f / A, 0f, maxTime);
                }
                else
                { // Double root and simple root.
                    return Mathf.Clamp(Mathf.Max((9f * A * D - B * C) / 2 / (B * B - 3f * A * C), (4f * A * B * C - 9f * A * A * D - B * B * B) / A / (B * B - 3f * A * C)), 0f, maxTime);
                }
            }
        }

        public static Vector3 PredictPosition(Vector3 position, Vector3 velocity, Vector3 acceleration, float time)
        {
            return position + velocity * time + 0.5f * acceleration * Mathf.Pow(time, 2);
        }

        public static Vector3 Displacement(Vector3 velocity, Vector3 acceleration, float time)
        {
            return velocity * time + 0.5f * acceleration * Mathf.Pow(time, 2);
        }
    }
}
