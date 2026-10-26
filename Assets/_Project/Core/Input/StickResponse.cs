using UnityEngine;

namespace TinyTumble.Core.Input
{
    /// <summary>
    /// Радиальная мёртвая зона и кривая отклика стика. Направление сохраняется, меняется только длина.
    /// </summary>
    public static class StickResponse
    {
        public static Vector2 Apply(Vector2 raw, float deadZone, float outerZone, float exponent)
        {
            float magnitude = raw.magnitude;
            if (magnitude <= deadZone || magnitude <= 0f)
            {
                return Vector2.zero;
            }

            float range = Mathf.Max(0.0001f, outerZone - deadZone);
            float normalized = Mathf.Clamp01((magnitude - deadZone) / range);
            float shaped = Mathf.Pow(normalized, Mathf.Max(0.01f, exponent));
            return raw / magnitude * shaped;
        }
    }
}
