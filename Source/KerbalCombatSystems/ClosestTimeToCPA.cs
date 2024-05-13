using System;
using System.Linq;

// This file is the only use of System.Numerics in the mod.
// It's used for its Complex number type to solve a quartic for ClosestTimeToCPAJerk.
// It's why the numerics dll is bundled in the release.
using System.Numerics;

using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace KerbalCombatSystems
{
    static partial class Utils
    {

        // Originally written by Brett Ryland and josuenos. Copied from https://github.com/BrettRyland/BDArmory
        // This code is distributed under CC-BY-SA 2.0: https://creativecommons.org/licenses/by-sa/2.0/

        // Predict the next time to the closest point of approach within the next maxTime seconds for two accelerating rigidbodies.
        public static float ClosestTimeToCPA(Vector3 relPosition, Vector3 relVelocity, Vector3 relAcceleration = default, float maxTime = 9999)
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

        // Similar to ClosestTimeToCPA, but with one more derivative (jerk) to consider.
        public static float ClosestTimeToCPAJerk(Vector3 relPosition, Vector3 relVelocity, Vector3 relAcceleration, Vector3 relJerk, float maxTime)
        {
            float A = Vector3.Dot(relJerk, relAcceleration) / 6f;
            float B = Vector3.Dot(relAcceleration, relAcceleration) / 2f + Vector3.Dot(relVelocity, relJerk);
            float C = Vector3.Dot(relVelocity, relAcceleration) * 3f / 2f;
            float D = Vector3.Dot(relVelocity, relVelocity) + Vector3.Dot(relPosition, relAcceleration);
            float E = Vector3.Dot(relPosition, relVelocity);

            Complex[] roots = Quartic(A, B, C, D, E);

            float[] realNumbers = roots.ToList().FindAll(n => n.Imaginary == 0.0).Select(n => (float)n.Real).ToArray();
            float[] positiveNumbers = Array.FindAll(realNumbers, x => x > 0).Select(n => (float)n).ToArray();

            float time = Mathf.Min(positiveNumbers);

            return Mathf.Clamp(time, 0f, maxTime);
        }

        // Quartic solver and child functions.
        // https://github.com/Kuuuube/Quartic_Cubic_Quadratic_Solver/blob/main/FQS_Quartic.cs#

        // code translated to c# almost entirely from https://github.com/NKrvavica/fqs/blob/master/fqs.py
        // MIT License.

        public static Complex[] Quadratic(Complex a0, Complex b0, Complex c0)
        {
            Complex a = b0 / a0;
            Complex b = c0 / a0;

            a0 = -0.5 * a;
            Complex delta = a0 * a0 - b;
            Complex sqrt_delta = Complex.Sqrt(delta);

            Complex r1 = a0 - sqrt_delta;
            Complex r2 = a0 + sqrt_delta;

            return new Complex[] { r1, r2 };
        }

        //this only returns the first solution of the cubic
        public static Complex Cubic(Complex a0, Complex b0, Complex c0, Complex d0)
        {
            Complex a = b0 / a0;
            Complex b = c0 / a0;
            Complex c = d0 / a0;

            Complex third = 0.333333333333333333333333333333333333333333333333333333333333;
            Complex a13 = a * third;
            Complex a2 = a13 * a13;

            Complex f = third * b - a2;
            Complex g = a13 * (2 * a2 - b) + c;
            Complex h = 0.25 * g * g + f * f * f;

            static Complex cubic_root(Complex x)
            {
                if (x.Real >= 0)
                {
                    return Math.Pow(x.Real, 1f / 3f);
                }
                else
                {
                    return -Math.Pow(-x.Real, 1f / 3f);
                }
            }

            if (f == 0 && g == 0 && h == 0)
            {
                return -cubic_root(c);
            }

            else if (h.Real <= 0)
            {
                Complex j = Complex.Sqrt(-f);
                Complex k = Complex.Acos(-0.5 * g / (j * j * j));
                Complex m = Complex.Cos(third * k);
                return 2 * j * m - a13;
            }

            else
            {
                Complex sqrt_h = Complex.Sqrt(h);
                Complex S = cubic_root(-0.5 * g + sqrt_h);
                Complex U = cubic_root(-0.5 * g - sqrt_h);
                Complex S_plus_U = S + U;
                return S_plus_U - a13;
            }
        }

        public static Complex[] Quartic(Complex a0, Complex b0, Complex c0, Complex d0, Complex e0)
        {
            Complex a = b0 / a0;
            Complex b = c0 / a0;
            Complex c = d0 / a0;
            Complex d = e0 / a0;

            a0 = 0.25 * a;
            Complex a02 = a0 * a0;

            Complex p = 3 * a02 - 0.5 * b;
            Complex q = a * a02 - b * a0 + 0.5 * c;
            Complex r = 3 * a02 * a02 - b * a02 + c * a0 - d;

            Complex z0 = Cubic(1, p, r, p * r - 0.5 * q * q);

            Complex s = Complex.Sqrt(2 * p + 2 * z0.Real);

            Complex t;

            if (s == 0)
            {
                t = z0 * z0 + r;
            }
            else
            {
                t = -q / s;
            }

            Complex[] r0r1 = Quadratic(1, s, z0 + t);
            Complex[] r2r3 = Quadratic(1, -s, z0 - t);

            return new Complex[] { (r0r1[0] - a0), (r0r1[1] - a0), (r2r3[0] - a0), (r2r3[1] - a0) };
        }


        // Time integration of CPA, unused.

        public static float TimeToCPAIntegrate(Vector3 relPosition, Vector3 relVelocity, Vector3 relAcceleration, Vector3 relJerk, float maxTime, float timeStep)
        {
            float distanceSqrDelta;
            Vector3 relPositionLast;

            int timeStepMultiplier = 256;
            timeStep *= timeStepMultiplier;
            int steps = Mathf.CeilToInt(maxTime / timeStep);
            float time = 0;

            for (int i = 0; i < steps; i++)
            {
                time += timeStep;

                relPositionLast = relPosition;
                relPosition += relVelocity * timeStep;

                distanceSqrDelta = relPosition.sqrMagnitude - relPositionLast.sqrMagnitude;
                if (distanceSqrDelta > 0)
                {
                    time -= timeStep;
                    relPosition -= relVelocity * timeStep;

                    float correction = ClosestTimeToCPA(relPosition, relVelocity, relAcceleration, timeStep);   
                    time += correction;

                    Debug.Log($"Finished CPA time integration. Steps: {i}");

                    break;
                }

                relAcceleration += relJerk * timeStep;
                relVelocity += relAcceleration * timeStep;
            }

            return time;
        }
    }
}
