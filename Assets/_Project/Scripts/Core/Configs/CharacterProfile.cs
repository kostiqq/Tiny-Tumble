using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Модель персонажа и всё, что генератору префаба нужно знать о ней: рост, толщина коллайдеров,
    /// доли массы и лимиты суставов. Модель — гуманоид (импорт Humanoid), кости находятся по аватару.
    /// Префаб перегенерируется целиком (Tiny Tumble → Setup → Generate Characters), поэтому всё,
    /// что нужно подкрутить для конкретной модели, правится здесь, а не в префабе.
    /// </summary>
    [CreateAssetMenu(fileName = "CP_Character", menuName = "Tiny Tumble/Characters/Character Profile")]
    public sealed class CharacterProfile : ScriptableObject
    {
        [Tooltip("FBX гуманоида. Генератор сам переключит импорт на Humanoid.")]
        [SerializeField] private GameObject _model;
        [Tooltip("Адрес префаба в Addressables.")]
        [SerializeField] private string _address = "Character";
        [Tooltip("Рост персонажа после масштабирования модели, м (от подошв до макушки).")]
        [SerializeField, Min(0.1f)] private float _height = 1f;

        [Header("Толщина коллайдеров, доля роста")]
        [SerializeField, Range(0.01f, 0.4f)] private float _hipsRadius = 0.11f;
        [SerializeField, Range(0.01f, 0.4f)] private float _chestRadius = 0.13f;
        [Tooltip("0 — по модели: от кости головы до макушки.")]
        [SerializeField, Range(0f, 0.4f)] private float _headRadius = 0f;
        [SerializeField, Range(0.01f, 0.2f)] private float _armRadius = 0.045f;
        [SerializeField, Range(0.01f, 0.2f)] private float _legRadius = 0.055f;

        [Header("Доли массы")]
        [SerializeField, Min(0.001f)] private float _hipsMass = 0.25f;
        [SerializeField, Min(0.001f)] private float _chestMass = 0.25f;
        [SerializeField, Min(0.001f)] private float _headMass = 0.12f;
        [SerializeField, Min(0.001f)] private float _upperArmMass = 0.04f;
        [SerializeField, Min(0.001f)] private float _lowerArmMass = 0.035f;
        [SerializeField, Min(0.001f)] private float _upperLegMass = 0.06f;
        [SerializeField, Min(0.001f)] private float _lowerLegMass = 0.05f;

        [Header("Лимиты суставов, град: x — скручивание вдоль кости, y — отклонение")]
        [SerializeField] private Vector2 _spineLimits = new Vector2(25f, 35f);
        [SerializeField] private Vector2 _headLimits = new Vector2(50f, 45f);
        [SerializeField] private Vector2 _upperArmLimits = new Vector2(60f, 160f);
        [SerializeField] private Vector2 _lowerArmLimits = new Vector2(60f, 130f);
        [SerializeField] private Vector2 _upperLegLimits = new Vector2(30f, 100f);
        [SerializeField] private Vector2 _lowerLegLimits = new Vector2(15f, 120f);

        public GameObject Model => _model;
        public string Address => _address;
        public float Height => _height;
        public float HipsRadius => _hipsRadius;
        public float ChestRadius => _chestRadius;
        public float HeadRadius => _headRadius;
        public float ArmRadius => _armRadius;
        public float LegRadius => _legRadius;
        public float HipsMass => _hipsMass;
        public float ChestMass => _chestMass;
        public float HeadMass => _headMass;
        public float UpperArmMass => _upperArmMass;
        public float LowerArmMass => _lowerArmMass;
        public float UpperLegMass => _upperLegMass;
        public float LowerLegMass => _lowerLegMass;
        public Vector2 SpineLimits => _spineLimits;
        public Vector2 HeadLimits => _headLimits;
        public Vector2 UpperArmLimits => _upperArmLimits;
        public Vector2 LowerArmLimits => _lowerArmLimits;
        public Vector2 UpperLegLimits => _upperLegLimits;
        public Vector2 LowerLegLimits => _lowerLegLimits;

#if UNITY_EDITOR
        /// <summary>Только для генератора проекта: профиль по умолчанию создаётся кодом.</summary>
        public void EditorInitialize(GameObject model, string address, float height)
        {
            _model = model;
            _address = address;
            _height = height;
        }
#endif
    }
}
