using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Чувствительность стика. Геймпад — основной контроллер (раздел 11 GDD); клавиатура даёт только полные отклонения.
    /// </summary>
    [CreateAssetMenu(fileName = "InputConfig", menuName = "Tiny Tumble/Configs/Input")]
    public sealed class InputConfig : ScriptableObject
    {
        [Tooltip("Отклонение стика меньше этого считается нулём: изношенные стики дрейфуют.")]
        [SerializeField, Range(0f, 0.5f)] private float _stickDeadZone = 0.15f;

        [Tooltip("От этого отклонения стик считается выжатым до конца.")]
        [SerializeField, Range(0.5f, 1f)] private float _stickOuterZone = 0.95f;

        [Tooltip("Кривая отклика: 1 — линейно, больше — точнее на малых отклонениях.")]
        [SerializeField, Range(0.5f, 3f)] private float _stickResponseExponent = 1.4f;

        public float StickDeadZone => _stickDeadZone;
        public float StickOuterZone => _stickOuterZone;
        public float StickResponseExponent => _stickResponseExponent;
    }
}
