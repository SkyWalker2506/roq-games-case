using UnityEngine;

namespace Shared.Tweening
{
    /// <summary>Easing curve identifiers used by <see cref="Tweener"/> and the juice services.</summary>
    public enum EaseType
    {
        /// <summary>Constant rate, no acceleration.</summary>
        Linear,
        /// <summary>Quadratic accelerate from zero.</summary>
        InQuad,
        /// <summary>Quadratic decelerate to zero.</summary>
        OutQuad,
        /// <summary>Quadratic accelerate then decelerate.</summary>
        InOutQuad,
        /// <summary>Cubic accelerate from zero.</summary>
        InCubic,
        /// <summary>Cubic decelerate to zero.</summary>
        OutCubic,
        /// <summary>Cubic accelerate then decelerate.</summary>
        InOutCubic,
        /// <summary>Quartic accelerate from zero.</summary>
        InQuart,
        /// <summary>Quartic decelerate to zero.</summary>
        OutQuart,
        /// <summary>Quintic accelerate from zero.</summary>
        InQuint,
        /// <summary>Quintic decelerate to zero.</summary>
        OutQuint,
        /// <summary>Sine accelerate from zero.</summary>
        InSine,
        /// <summary>Sine decelerate to zero.</summary>
        OutSine,
        /// <summary>Sine accelerate then decelerate.</summary>
        InOutSine,
        /// <summary>Exponential accelerate from zero.</summary>
        InExpo,
        /// <summary>Exponential decelerate to zero.</summary>
        OutExpo,
        /// <summary>Backs up before moving forward (anticipation).</summary>
        InBack,
        /// <summary>Overshoots the target then settles back (impact snap).</summary>
        OutBack,
        /// <summary>Anticipates, then overshoots and settles.</summary>
        InOutBack,
        /// <summary>Overshoots and oscillates into place (springy landing).</summary>
        OutElastic,
        /// <summary>Bounces on arrival, losing energy each hit.</summary>
        OutBounce
    }

    /// <summary>Stateless easing functions (Penner set) plus a critically damped spring helper.</summary>
    public static class Ease
    {
        /// <summary>Default OutBack/InBack overshoot constant (Penner's 1.70158).</summary>
        public const float DefaultBackOvershoot = 1.70158f;

        /// <summary>Evaluates <paramref name="type"/> at normalized time <paramref name="t"/> (clamped to 0..1).</summary>
        public static float Evaluate(EaseType type, float t)
        {
            return Evaluate(type, t, DefaultBackOvershoot);
        }

        /// <summary>Evaluates <paramref name="type"/> at normalized time <paramref name="t"/>, with <paramref name="overshoot"/> feeding the Back/Elastic curves.</summary>
        public static float Evaluate(EaseType type, float t, float overshoot)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            switch (type)
            {
                case EaseType.Linear: return t;

                case EaseType.InQuad: return t * t;
                case EaseType.OutQuad: return 1f - (1f - t) * (1f - t);
                case EaseType.InOutQuad:
                    return t < 0.5f ? 2f * t * t : 1f - Pow2(-2f * t + 2f) * 0.5f;

                case EaseType.InCubic: return t * t * t;
                case EaseType.OutCubic: return 1f - Pow3(1f - t);
                case EaseType.InOutCubic:
                    return t < 0.5f ? 4f * t * t * t : 1f - Pow3(-2f * t + 2f) * 0.5f;

                case EaseType.InQuart: return t * t * t * t;
                case EaseType.OutQuart: return 1f - Pow4(1f - t);

                case EaseType.InQuint: return t * t * t * t * t;
                case EaseType.OutQuint: return 1f - Pow5(1f - t);

                case EaseType.InSine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case EaseType.OutSine: return Mathf.Sin(t * Mathf.PI * 0.5f);
                case EaseType.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

                case EaseType.InExpo: return Mathf.Pow(2f, 10f * t - 10f);
                case EaseType.OutExpo: return 1f - Mathf.Pow(2f, -10f * t);

                case EaseType.InBack:
                {
                    float s = overshoot;
                    return t * t * ((s + 1f) * t - s);
                }
                case EaseType.OutBack:
                {
                    float s = overshoot;
                    float u = t - 1f;
                    return u * u * ((s + 1f) * u + s) + 1f;
                }
                case EaseType.InOutBack:
                {
                    float s = overshoot * 1.525f;
                    if (t < 0.5f)
                    {
                        float u = 2f * t;
                        return 0.5f * (u * u * ((s + 1f) * u - s));
                    }
                    else
                    {
                        float u = 2f * t - 2f;
                        return 0.5f * (u * u * ((s + 1f) * u + s) + 2f);
                    }
                }

                case EaseType.OutElastic:
                {
                    // period 0.3 -> ~3 visible oscillations before settling
                    const float period = 0.3f;
                    float c = (2f * Mathf.PI) / period;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - period * 0.25f) * c) + 1f;
                }

                case EaseType.OutBounce: return OutBounce(t);

                default: return t;
            }
        }

        /// <summary>Bounce-on-arrival curve (Penner), exposed for direct use.</summary>
        public static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        /// <summary>Advances a critically damped spring one step; no overshoot, frame-rate independent. <paramref name="smoothTime"/> is the approximate time to converge.</summary>
        public static float Spring(float current, float target, ref float velocity, float smoothTime, float deltaTime)
        {
            // Semi-implicit critically damped spring (Game Programming Gems 4, "Critically Damped Ease-In/Ease-Out").
            smoothTime = Mathf.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;
            return target + (change + temp) * exp;
        }

        /// <summary>Vector3 overload of the critically damped spring.</summary>
        public static Vector3 Spring(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime, float deltaTime)
        {
            float vx = velocity.x, vy = velocity.y, vz = velocity.z;
            float x = Spring(current.x, target.x, ref vx, smoothTime, deltaTime);
            float y = Spring(current.y, target.y, ref vy, smoothTime, deltaTime);
            float z = Spring(current.z, target.z, ref vz, smoothTime, deltaTime);
            velocity = new Vector3(vx, vy, vz);
            return new Vector3(x, y, z);
        }

        static float Pow2(float v) { return v * v; }
        static float Pow3(float v) { return v * v * v; }
        static float Pow4(float v) { return v * v * v * v; }
        static float Pow5(float v) { return v * v * v * v * v; }
    }
}
