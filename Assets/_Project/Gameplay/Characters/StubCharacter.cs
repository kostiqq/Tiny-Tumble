using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Match;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Кубик-заглушка персонажа: ходит, прыгает и делает рывок от <see cref="IPlayerInput"/>.
    /// Игрок и бот управляют им одинаково. Заменится настоящим персонажем на этапе боя.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class StubCharacter : MonoBehaviour
    {
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_colorId = Shader.PropertyToID("_Color");

        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Renderer _renderer;

        private CharacterConfig _characterConfig;
        private MatchConfig _matchConfig;
        private IPlayerInput _input;
        private MatchRoster _roster;
        private MaterialPropertyBlock _propertyBlock;
        private int _participantId = -1;
        private float _dashReadyTime;
        private float _dashEndTime;
        private Vector3 _dashDirection;
        private Vector3 _facing = Vector3.forward;
        private bool _eliminated;

        public int ParticipantId => _participantId;

        [Inject]
        public void Construct(CharacterConfig characterConfig, MatchConfig matchConfig)
        {
            _characterConfig = characterConfig;
            _matchConfig = matchConfig;
        }

        public void Bind(int participantId, IPlayerInput input, MatchRoster roster, Color color)
        {
            _participantId = participantId;
            _input = input;
            _roster = roster;
            SetColor(color);
        }

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _renderer = GetComponentInChildren<Renderer>();
        }

        private void FixedUpdate()
        {
            if (_input == null || _eliminated)
            {
                return;
            }

            // Ровно один сэмпл за тик: на нём держится накопление нажатий в IPlayerInput.
            PlayerInputFrame frame = _input.Sample();
            float time = Time.fixedTime;

            var move = new Vector3(frame.Move.x, 0f, frame.Move.y);
            if (move.sqrMagnitude > 0.0001f)
            {
                _facing = move.normalized;
            }

            Vector3 velocity = _rigidbody.linearVelocity;
            if (time < _dashEndTime)
            {
                velocity.x = _dashDirection.x * _characterConfig.DashSpeed;
                velocity.z = _dashDirection.z * _characterConfig.DashSpeed;
            }
            else
            {
                Vector3 target = move * _characterConfig.MoveSpeed;
                float blend = 1f - Mathf.Exp(-_characterConfig.Acceleration * Time.fixedDeltaTime);
                velocity.x = Mathf.Lerp(velocity.x, target.x, blend);
                velocity.z = Mathf.Lerp(velocity.z, target.z, blend);

                if (frame.WasPressed(PlayerButtons.Dash) && time >= _dashReadyTime)
                {
                    _dashDirection = _facing;
                    _dashEndTime = time + _characterConfig.DashDuration;
                    _dashReadyTime = time + _characterConfig.DashCooldown;
                }
            }

            if (frame.WasPressed(PlayerButtons.Jump) && IsGrounded())
            {
                velocity.y = _characterConfig.JumpVelocity;
            }

            _rigidbody.linearVelocity = velocity;

            if (_rigidbody.position.y < _matchConfig.EliminationHeight)
            {
                Eliminate();
            }
        }

        private bool IsGrounded()
        {
            return Physics.Raycast(_rigidbody.position, Vector3.down, _characterConfig.GroundCheckDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void Eliminate()
        {
            _eliminated = true;
            gameObject.SetActive(false);
            if (_roster != null)
            {
                _roster.Eliminate(_participantId);
            }
        }

        private void SetColor(Color color)
        {
            if (_renderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(s_baseColorId, color);
            _propertyBlock.SetColor(s_colorId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
