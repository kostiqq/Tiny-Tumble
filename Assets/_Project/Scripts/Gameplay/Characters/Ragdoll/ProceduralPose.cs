using TinyTumble.Core.Configs;
using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>Что нужно позе, чтобы выбрать положение костей.</summary>
    public struct PoseInput
    {
        /// <summary>Доля скорости шага 0..1: размах ног и рук.</summary>
        public float Gait;
        public bool Grounded;
        public CharacterState State;
        public CharacterFlags Flags;

        /// <summary>Вес позы толчка 0..1: руки вперёд.</summary>
        public float Swing;

        /// <summary>Индекс эмоции или -1.</summary>
        public int Emote;

        /// <summary>Прогресс эмоции 0..1.</summary>
        public float EmoteProgress;
    }

    /// <summary>
    /// Процедурные позы вместо анимационных клипов: шаг по фазе, руки при толчке, ношении и броске, эмоции.
    /// Конечности задаются направлением в осях персонажа (x вправо, y вверх, z вперёд), поэтому позы
    /// не зависят от того, как ориентированы кости конкретной модели и в какой позе (T или A) она сделана.
    /// </summary>
    public sealed class ProceduralPose
    {
        private const float TwoPi = Mathf.PI * 2f;
        private const float ElbowBend = 20f;
        private const float AirLegSwing = 20f;
        private const float AirKneeBend = 40f;

        private readonly RagdollRig _rig;
        private readonly CharacterConfig _config;
        private readonly Quaternion[] _desired = new Quaternion[RagdollBones.Count];
        private float _phase;

        public ProceduralPose(RagdollRig rig, CharacterConfig config)
        {
            _rig = rig;
            _config = config;
        }

        /// <summary>Повороты костей в осях персонажа после <see cref="Evaluate"/>.</summary>
        public Quaternion[] Desired => _desired;

        /// <summary>Доворот корпуса вокруг вертикали, град (пируэт).</summary>
        public float YawOffset { get; private set; }

        /// <summary>
        /// Шаги идут по пройденному пути, а не по времени: ноги не «буксуют». Частота ограничена
        /// <see cref="CharacterConfig.MaxStepRate"/>: приводы суставов должны успевать за шагом.
        /// </summary>
        public void Advance(float distance, float deltaTime)
        {
            float cycles = Mathf.Min(distance / _config.StrideLength, _config.MaxStepRate * deltaTime);
            _phase = Mathf.Repeat(_phase + cycles, 1f);
        }

        public void Evaluate(in PoseInput input)
        {
            YawOffset = 0f;
            float sine = Mathf.Sin(_phase * TwoPi);
            float cosine = Mathf.Cos(_phase * TwoPi);
            float gait = input.Grounded ? Mathf.Clamp01(input.Gait) : 0f;
            bool upright = input.State == CharacterState.Normal || input.State == CharacterState.GettingUp;

            _desired[RagdollBones.Hips] = _rig.RestRotation(RagdollBones.Hips);
            Rotate(RagdollBones.Chest, Quaternion.identity);
            Rotate(RagdollBones.Head, Quaternion.identity);

            // Ноги: левая выносится вперёд, когда фаза растёт; в выносе колено сгибается.
            if (!input.Grounded && input.State == CharacterState.Normal)
            {
                Leg(RagdollBones.LeftUpperLeg, RagdollBones.LeftLowerLeg, AirLegSwing, AirKneeBend);
                Leg(RagdollBones.RightUpperLeg, RagdollBones.RightLowerLeg, AirLegSwing * 0.5f, AirKneeBend);
            }
            else
            {
                float step = _config.StepAngle * gait;
                float knee = _config.KneeBend * gait;
                Leg(RagdollBones.LeftUpperLeg, RagdollBones.LeftLowerLeg, step * sine, knee * Mathf.Max(0f, cosine));
                Leg(RagdollBones.RightUpperLeg, RagdollBones.RightLowerLeg, -step * sine, knee * Mathf.Max(0f, -cosine));
            }

            // Руки: по умолчанию висят и качаются против ног.
            float armSwing = _config.ArmSwingAngle * gait * sine;
            Vector3 left = Quaternion.Euler(armSwing, 0f, 0f) * new Vector3(-0.2f, -1f, 0.05f);
            Vector3 right = Quaternion.Euler(-armSwing, 0f, 0f) * new Vector3(0.2f, -1f, 0.05f);

            if ((input.Flags & CharacterFlags.ChargingThrow) != 0)
            {
                left = new Vector3(-0.2f, 0.8f, -0.5f);
                right = new Vector3(0.2f, 0.8f, -0.5f);
            }
            else if ((input.Flags & CharacterFlags.Holding) != 0)
            {
                left = new Vector3(-0.2f, 1f, 0.25f);
                right = new Vector3(0.2f, 1f, 0.25f);
            }
            else if (input.State == CharacterState.Stagger)
            {
                left = new Vector3(-0.8f, 0.5f, -0.2f);
                right = new Vector3(0.8f, 0.5f, -0.2f);
            }

            if (input.Swing > 0f)
            {
                left = Vector3.Slerp(left.normalized, new Vector3(-0.1f, 0.05f, 1f).normalized, input.Swing);
                right = Vector3.Slerp(right.normalized, new Vector3(0.1f, 0.05f, 1f).normalized, input.Swing);
            }

            if (input.Emote >= 0 && upright)
            {
                Emote(input.Emote, Mathf.Clamp01(input.EmoteProgress), ref left, ref right);
            }

            Arm(RagdollBones.LeftUpperArm, RagdollBones.LeftLowerArm, left, input.Swing);
            Arm(RagdollBones.RightUpperArm, RagdollBones.RightLowerArm, right, input.Swing);
        }

        private void Emote(int index, float t, ref Vector3 left, ref Vector3 right)
        {
            float bump = Mathf.Sin(t * Mathf.PI);
            switch (index)
            {
                case 0:
                    // Радость: руки вверх и машут.
                    float wave = Mathf.Sin(t * Mathf.PI * 4f) * 0.35f;
                    left = new Vector3(-0.45f + wave, 1f, 0f);
                    right = new Vector3(0.45f + wave, 1f, 0f);
                    break;
                case 1:
                    // Пируэт: руки в стороны, корпус делает оборот.
                    left = new Vector3(-1f, 0.15f, 0f);
                    right = new Vector3(1f, 0.15f, 0f);
                    YawOffset = t * 360f;
                    break;
                case 2:
                    // Дразнилка: качается из стороны в сторону, руки болтаются.
                    float sway = Mathf.Sin(t * Mathf.PI * 4f);
                    Rotate(RagdollBones.Chest, Quaternion.Euler(0f, 0f, 15f * sway));
                    left = new Vector3(-0.6f, -0.5f, 0.3f * sway);
                    right = new Vector3(0.6f, -0.5f, -0.3f * sway);
                    break;
                default:
                    // Поклон.
                    Rotate(RagdollBones.Chest, Quaternion.Euler(45f * bump, 0f, 0f));
                    left = new Vector3(-0.1f, -1f, 0.3f);
                    right = new Vector3(0.1f, -1f, 0.3f);
                    break;
            }
        }

        /// <param name="swing">Угол вперёд, град (положительный — нога впереди).</param>
        /// <param name="knee">Сгиб колена назад, град.</param>
        private void Leg(int upper, int lower, float swing, float knee)
        {
            // От позы покоя, а не «строго вниз»: в покое ноги модели могут быть расставлены, и выпрямленная
            // вертикально нога оказалась бы длиннее высоты таза — упёрлась бы в стол и опрокинула тело.
            Point(upper, Quaternion.Euler(-swing, 0f, 0f) * _rig.RestDirection(upper));
            Point(lower, Quaternion.Euler(-swing + knee, 0f, 0f) * _rig.RestDirection(lower));
        }

        private void Arm(int upper, int lower, Vector3 direction, float straighten)
        {
            Vector3 upperDirection = direction.normalized;
            // Локоть чуть согнут вперёд; в толчке рука выпрямляется.
            float bend = ElbowBend * (1f - straighten);
            Vector3 side = Vector3.Cross(upperDirection, Vector3.forward);
            Vector3 lowerDirection = side.sqrMagnitude > 1e-4f
                ? Quaternion.AngleAxis(bend, side.normalized) * upperDirection
                : upperDirection;
            Point(upper, upperDirection);
            Point(lower, lowerDirection);
        }

        /// <summary>
        /// Кость смотрит в направлении (оси персонажа). Сначала она несётся вместе с родителем (как в покое),
        /// потом доворачивается кратчайшим поворотом: так между звеньями нет лишнего скручивания, которое
        /// упиралось бы в лимиты суставов. Родитель должен быть уже посчитан.
        /// </summary>
        private void Point(int bone, Vector3 direction)
        {
            int parent = RagdollBones.Parent(bone);
            Quaternion carried = _desired[parent] * Quaternion.Inverse(_rig.RestRotation(parent));
            Quaternion baseRotation = carried * _rig.RestRotation(bone);
            Vector3 baseDirection = carried * _rig.RestDirection(bone);
            _desired[bone] = Quaternion.FromToRotation(baseDirection, direction.normalized) * baseRotation;
        }

        /// <summary>Кость повёрнута относительно позы покоя (оси персонажа).</summary>
        private void Rotate(int bone, Quaternion offset)
        {
            _desired[bone] = offset * _rig.RestRotation(bone);
        }
    }
}
