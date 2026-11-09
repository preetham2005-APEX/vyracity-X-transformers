using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    public struct TyreForces
    {
        /// <summary>Longitudinal force along the wheel heading, N (+ = forward).</summary>
        public float Fx;
        /// <summary>Lateral force along the wheel's right axis, N.</summary>
        public float Fy;
        /// <summary>Combined normalised slip (1 = at the peak).</summary>
        public float CombinedSlip;
        /// <summary>Load-adjusted friction coefficient used.</summary>
        public float Mu;
    }

    /// <summary>
    /// Simplified magic-formula tyre with combined slip through a normalised slip vector
    /// (VEHICLE_HANDLING_SPEC §2). Pure functions: no Unity state, unit-testable.
    /// </summary>
    public static class TyreModel
    {
        /// <summary>Stiffness factor B that puts the peak of sin(C·atan(B·s)) at s = 1.</summary>
        public static float StiffnessFactor(float shapeC)
        {
            shapeC = Mathf.Clamp(shapeC, 1.05f, 2.5f);
            return Mathf.Tan(Mathf.PI / (2f * shapeC));
        }

        /// <summary>Normalised force curve: 0 at s = 0, 1 at s = 1, falls off beyond it.</summary>
        public static float Curve(float s, float shapeC)
        {
            shapeC = Mathf.Clamp(shapeC, 1.05f, 2.5f);
            return Mathf.Sin(shapeC * Mathf.Atan(StiffnessFactor(shapeC) * s));
        }

        public static float LoadedMu(in TyreSpec t, float load, float surfaceGrip)
        {
            float nominal = Mathf.Max(1f, t.NominalLoad);
            float mu = t.Mu * (1f - t.LoadSensitivity * (load / nominal - 1f));
            mu = Mathf.Clamp(mu, 0.5f * t.Mu, 1.3f * t.Mu);
            return mu * Mathf.Max(0f, surfaceGrip);
        }

        /// <param name="slipRatio">κ = (ωr − vx) / |vx| (+ = wheelspin).</param>
        /// <param name="slipAngleRad">α = atan(vy / |vx|), where vy is the contact-patch velocity along the wheel's right axis.</param>
        public static TyreForces Evaluate(in TyreSpec t, float slipRatio, float slipAngleRad, float load, float surfaceGrip)
        {
            if (load <= 0f) return default;
            float peakAngle = Mathf.Max(0.5f, t.LatPeakSlipDeg) * Mathf.Deg2Rad;
            float sx = slipRatio / Mathf.Max(0.01f, t.LongPeakSlip);
            float sy = slipAngleRad / peakAngle;
            float s = Mathf.Sqrt(sx * sx + sy * sy);
            float mu = LoadedMu(t, load, surfaceGrip);
            if (s < 1e-6f) return new TyreForces { Mu = mu };

            float fxMax = mu * load;
            float fyMax = mu * t.LateralGripScale * load;
            float fx = fxMax * Curve(s, t.LongShape) * (sx / s);
            float fy = -fyMax * Curve(s, t.LatShape) * (sy / s);
            return new TyreForces { Fx = fx, Fy = fy, CombinedSlip = s, Mu = mu };
        }

        /// <summary>dFx/dκ at zero slip (the maximum slope). Used for the implicit wheel-speed update.</summary>
        public static float LongitudinalStiffness(in TyreSpec t, float load, float surfaceGrip)
        {
            if (load <= 0f) return 0f;
            float c = Mathf.Clamp(t.LongShape, 1.05f, 2.5f);
            return LoadedMu(t, load, surfaceGrip) * load * c * StiffnessFactor(c) / Mathf.Max(0.01f, t.LongPeakSlip);
        }
    }
}
