using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Перевод желаемого поворота кости в <see cref="ConfigurableJoint.targetRotation"/>.
    /// Цель сустава задаётся в его собственных осях (axis, secondaryAxis) и относительно поворота,
    /// с которым сустав создан, поэтому прямое присваивание поворота кости не работает.
    /// </summary>
    public static class JointTargets
    {
        /// <summary>Поворот из осей кости в оси сустава. Считается один раз: оси сустава не меняются.</summary>
        public static Quaternion JointFrame(Vector3 axis, Vector3 secondaryAxis)
        {
            Vector3 right = axis;
            Vector3 forward = Vector3.Cross(axis, secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        /// <param name="jointFrame">Результат <see cref="JointFrame"/> для этого сустава.</param>
        /// <param name="targetRelative">Желаемый поворот кости относительно родительской кости.</param>
        /// <param name="startRelative">Тот же поворот в момент создания сустава (поза покоя).</param>
        public static Quaternion ToJointSpace(Quaternion jointFrame, Quaternion targetRelative, Quaternion startRelative)
        {
            // Пространство сустава обратно пространству кости, поэтому цель берётся обратной.
            return Quaternion.Inverse(jointFrame) * Quaternion.Inverse(targetRelative) * startRelative * jointFrame;
        }
    }
}
