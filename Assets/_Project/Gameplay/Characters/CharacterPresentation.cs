using TinyTumble.Core.Configs;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Визуал персонажа на дочернем объекте: «желейный» наклон и сплющивание на пружинах, замах, эмоции,
    /// хит-стоп и плавное сглаживание сетевых коррекций. Считается на каждой машине по видимому движению тела,
    /// поэтому одинаково работает для своего, чужого и бота и не требует синхронизации.
    /// Когда появится риг, сюда же подключится Animator: параметры уже выставляются, если он назначен.
    /// </summary>
    public sealed class CharacterPresentation : MonoBehaviour
    {
        private const float MaxAcceleration = 60f;
        private const float VelocitySmoothing = 20f;
        private const float LandingSpeedThreshold = 2f;
        private const float SwingDuration = 0.18f;
        private const float SwingLean = 28f;
        private const float EmoteHopHeight = 0.35f;
        private const float HitStopJitter = 0.04f;

        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_colorId = Shader.PropertyToID("_Color");
        private static readonly int s_speedId = Animator.StringToHash("Speed");
        private static readonly int s_stateId = Animator.StringToHash("State");
        private static readonly int s_pushId = Animator.StringToHash("Push");
        private static readonly int s_emoteId = Animator.StringToHash("Emote");

        [SerializeField] private Renderer[] _renderers = new Renderer[0];
        [Tooltip("Необязательно: будущий риг. Параметры Speed, State, триггеры Push и Emote.")]
        [SerializeField] private Animator _animator;

        private CharacterConfig _config;
        private float _emoteDuration = 0.8f;
        private PlayerCharacter _character;
        private MaterialPropertyBlock _propertyBlock;

        private Vector3 _previousPosition;
        private Vector3 _velocity;
        private bool _hasPrevious;
        private Vector2 _lean;
        private Vector2 _leanVelocity;
        private float _squash;
        private float _squashVelocity;
        private Vector3 _correctionOffset;
        private float _hitStopUntil;
        private float _swingStart = float.NegativeInfinity;
        private float _emoteStart = float.NegativeInfinity;
        private int _emote = -1;

        [Inject]
        public void Construct(CharacterConfig config, CombatConfig combatConfig)
        {
            _config = config;
            _emoteDuration = combatConfig.EmoteDuration;
        }

        public void Bind(PlayerCharacter character)
        {
            _character = character;
            Teleported();
        }

        public void SetColor(Color color)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer target = _renderers[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(s_baseColorId, color);
                _propertyBlock.SetColor(s_colorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>Тело сдвинули сетевой коррекцией: визуал остаётся на месте и догоняет плавно.</summary>
        public void AbsorbCorrection(Vector3 worldDelta)
        {
            _correctionOffset -= worldDelta;
            // Пружины не должны принять скачок за огромное ускорение.
            _previousPosition += worldDelta;
        }

        /// <summary>Телепорт: никаких хвостов от старого положения.</summary>
        public void Teleported()
        {
            _correctionOffset = Vector3.zero;
            _hasPrevious = false;
            _velocity = Vector3.zero;
            _lean = Vector2.zero;
            _leanVelocity = Vector2.zero;
            _squash = 0f;
            _squashVelocity = 0f;
        }

        public void HitStop(float duration)
        {
            _hitStopUntil = Mathf.Max(_hitStopUntil, Time.unscaledTime + duration);
        }

        public void PlaySwing()
        {
            _swingStart = Time.time;
            if (_animator != null)
            {
                _animator.SetTrigger(s_pushId);
            }
        }

        public void PlayEmote(int index)
        {
            _emote = index;
            _emoteStart = Time.time;
            if (_animator != null)
            {
                _animator.SetInteger(s_emoteId, index);
            }
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (_character == null || _config == null || deltaTime <= 0f)
            {
                return;
            }

            Transform root = _character.transform;
            Vector3 position = root.position;
            if (!_hasPrevious)
            {
                _previousPosition = position;
                _hasPrevious = true;
            }

            Vector3 rawVelocity = (position - _previousPosition) / deltaTime;
            _previousPosition = position;
            Vector3 previousVelocity = _velocity;
            _velocity = Vector3.Lerp(_velocity, rawVelocity, 1f - Mathf.Exp(-VelocitySmoothing * deltaTime));
            Vector3 acceleration = Vector3.ClampMagnitude((_velocity - previousVelocity) / deltaTime, MaxAcceleration);

            _correctionOffset = Vector3.Lerp(_correctionOffset, Vector3.zero, 1f - Mathf.Exp(-deltaTime / _config.VisualCorrectionTime));

            if (Time.unscaledTime < _hitStopUntil)
            {
                // Хит-стоп: пружины замирают, визуал мелко дрожит.
                transform.localPosition = root.InverseTransformVector(_correctionOffset) + Random.insideUnitSphere * HitStopJitter;
                return;
            }

            bool upright = _character.State != CharacterState.Ragdoll && _character.State != CharacterState.Grabbed;
            StepSprings(acceleration, previousVelocity, upright, deltaTime);
            Apply(root, upright);
            UpdateAnimator();
        }

        private void StepSprings(Vector3 acceleration, Vector3 previousVelocity, bool upright, float deltaTime)
        {
            // Наклон назад при разгоне и вперёд при торможении — в осях персонажа.
            Vector3 local = _character.transform.InverseTransformDirection(acceleration);
            Vector2 target = upright
                ? Vector2.ClampMagnitude(new Vector2(-local.z, local.x) * _config.LeanPerAcceleration, _config.MaxLean)
                : Vector2.zero;
            float stiffness = _config.WobbleStiffness;
            float damping = _config.WobbleDamping;

            _leanVelocity += (stiffness * (target - _lean) - damping * _leanVelocity) * deltaTime;
            _lean += _leanVelocity * deltaTime;

            // Приземление: вертикальная скорость резко погасла — сплющиваемся пропорционально удару.
            float landingSpeed = -previousVelocity.y;
            if (upright && landingSpeed > LandingSpeedThreshold && _velocity.y > previousVelocity.y + LandingSpeedThreshold * 0.5f)
            {
                _squashVelocity -= landingSpeed * _config.SquashPerLandingSpeed * stiffness * deltaTime * 4f;
            }

            _squashVelocity += (stiffness * (0f - _squash) - damping * _squashVelocity) * deltaTime;
            _squash = Mathf.Clamp(_squash + _squashVelocity * deltaTime, -_config.MaxSquash, _config.MaxSquash);
        }

        private void Apply(Transform root, bool upright)
        {
            float time = Time.time;
            float swing = 0f;
            float swingT = (time - _swingStart) / SwingDuration;
            if (swingT >= 0f && swingT < 1f)
            {
                swing = Mathf.Sin(swingT * Mathf.PI) * SwingLean;
            }

            Vector3 hop = Vector3.zero;
            float spin = 0f;
            float bow = 0f;
            float emoteT = (time - _emoteStart) / _emoteDuration;
            if (_emote >= 0 && emoteT >= 0f && emoteT < 1f && upright)
            {
                EvaluateEmote(_emote, emoteT, out hop, out spin, out bow);
            }
            else
            {
                _emote = -1;
            }

            transform.localPosition = root.InverseTransformVector(_correctionOffset) + hop;
            transform.localRotation = Quaternion.Euler(_lean.x + swing + bow, spin, _lean.y);

            // Сплющивание с сохранением объёма: ниже — шире.
            float squash = _squash;
            float widen = 1f - squash * 0.5f;
            transform.localScale = new Vector3(widen, 1f + squash, widen);
        }

        /// <summary>Четыре эмоции — четыре читаемых движения до появления анимаций.</summary>
        private static void EvaluateEmote(int index, float t, out Vector3 hop, out float spin, out float bow)
        {
            float bump = Mathf.Sin(t * Mathf.PI);
            hop = Vector3.zero;
            spin = 0f;
            bow = 0f;
            switch (index)
            {
                case 0:
                    // Радость: два прыжка.
                    hop = Vector3.up * (Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * EmoteHopHeight);
                    break;
                case 1:
                    // Пируэт.
                    spin = t * 360f;
                    hop = Vector3.up * (bump * EmoteHopHeight * 0.4f);
                    break;
                case 2:
                    // Дразнилка: покачивание из стороны в сторону.
                    hop = Vector3.right * (Mathf.Sin(t * Mathf.PI * 4f) * 0.1f);
                    spin = Mathf.Sin(t * Mathf.PI * 4f) * 25f;
                    break;
                default:
                    // Поклон.
                    bow = bump * 45f;
                    break;
            }
        }

        private void UpdateAnimator()
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetFloat(s_speedId, new Vector2(_velocity.x, _velocity.z).magnitude);
            _animator.SetInteger(s_stateId, (int)_character.State);
        }
    }
}
