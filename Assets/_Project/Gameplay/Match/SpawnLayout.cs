using UnityEngine;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Расстановка по окружности вокруг центра стола. Точки спавна арены появятся с ArenaDefinition (этап 5).
    /// </summary>
    public static class SpawnLayout
    {
        public static Vector3 OnRing(int index, int total, float radius, float height, float angleOffset = 0f)
        {
            float angle = (total > 0 ? index * Mathf.PI * 2f / total : 0f) + angleOffset;
            return new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
        }

        /// <summary>Лицом к центру стола.</summary>
        public static Quaternion FacingCenter(Vector3 position)
        {
            Vector3 toCenter = new Vector3(-position.x, 0f, -position.z);
            return toCenter.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toCenter, Vector3.up) : Quaternion.identity;
        }
    }
}
