using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Match;
using TinyTumble.Networking.Input;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Кубик-заглушка персонажа: ходит, прыгает и делает рывок от <see cref="IPlayerInput"/>.
    /// Симулирует только хост; клиенты видят результат через NetworkTransform. Игрок, удалённый игрок
    /// и бот управляют им одинаково. Заменится настоящим персонажем на этапе 3.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class StubCharacter : NetworkBehaviour
    {
        public const int BotSlot = -1;

        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_colorId = Shader.PropertyToID("_Color");

        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Renderer _renderer;
        [SerializeField] private NetworkPlayerInput _networkInput;

        // Слот определяет цвет у всех; локальный индекс нужен клиенту-владельцу, чтобы найти свой ввод.
        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(BotSlot);
        private readonly NetworkVariable<byte> _localIndex = new NetworkVariable<byte>();

        private CharacterConfig _characterConfig;
        private MatchConfig _matchConfig;
        private ILocalPlayers _localPlayers;
        private MatchCharacters _characters;
        private MaterialPropertyBlock _propertyBlock;

        // Только на хосте.
        private IPlayerInput _input;
        private MatchRoster _roster;
        private int _participantId = -1;
        private int _pendingSlot = BotSlot;
        private byte _pendingLocalIndex;
        private bool _eliminated;
        private bool _removedByMatch;
        private float _dashReadyTime;
        private float _dashEndTime;
        private Vector3 _dashDirection;
        private Vector3 _facing = Vector3.forward;

        public int ParticipantId => _participantId;

        /// <summary>Слот игрока 0..3 или <see cref="BotSlot"/>.</summary>
        public int Slot => _slot.Value;

        public bool IsBot => _slot.Value == BotSlot;

        public NetworkPlayerInput NetworkInput => _networkInput;

        [Inject]
        public void Construct(CharacterConfig characterConfig, MatchConfig matchConfig, ILocalPlayers localPlayers, MatchCharacters characters)
        {
            _characterConfig = characterConfig;
            _matchConfig = matchConfig;
            _localPlayers = localPlayers;
            _characters = characters;
        }

        /// <summary>Хост, до Spawn: кто управляет персонажем и в каком он слоте.</summary>
        public void ConfigureServer(int participantId, int slot, int localIndex, IPlayerInput input, MatchRoster roster)
        {
            _participantId = participantId;
            _pendingSlot = slot;
            _pendingLocalIndex = (byte)localIndex;
            _input = input;
            _roster = roster;
        }

        /// <summary>Хост: матч убирает персонажа сам (конец матча), выбывание засчитывать не нужно.</summary>
        public void MarkRemovedByMatch()
        {
            _removedByMatch = true;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Переменные пишем здесь, а не до Spawn: до спавна NGO ещё не связал их с объектом.
                _slot.Value = _pendingSlot;
                _localIndex.Value = _pendingLocalIndex;
            }

            // Физику считает хост. У клиентов NetworkRigidbody делает тело кинематическим,
            // и оно только следует за NetworkTransform.

            _slot.OnValueChanged += OnSlotChanged;
            _localIndex.OnValueChanged += OnLocalIndexChanged;
            ApplyColor();
            BindLocalInput();

            if (_characters != null)
            {
                _characters.Add(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            _slot.OnValueChanged -= OnSlotChanged;
            _localIndex.OnValueChanged -= OnLocalIndexChanged;

            if (_characters != null)
            {
                _characters.Remove(this);
            }

            // Владелец отключился посреди матча: NGO убрал его персонажа, для правил это выбывание.
            // При закрытии сессии выбывания не считаем, иначе матч «закончится» по пути в меню.
            if (IsServer && !_eliminated && !_removedByMatch && !NetworkManager.ShutdownInProgress && _roster != null)
            {
                _eliminated = true;
                _roster.Eliminate(_participantId);
            }
        }

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _renderer = GetComponentInChildren<Renderer>();
            _networkInput = GetComponent<NetworkPlayerInput>();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _input == null || _eliminated)
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
            if (_roster != null)
            {
                _roster.Eliminate(_participantId);
            }

            // Despawn убирает персонажа и у клиентов; сам объект уничтожится в конце кадра.
            if (IsSpawned)
            {
                NetworkObject.Despawn();
            }
        }

        private void OnSlotChanged(int previous, int current)
        {
            ApplyColor();
        }

        private void OnLocalIndexChanged(byte previous, byte current)
        {
            BindLocalInput();
        }

        private void BindLocalInput()
        {
            // Хост подаёт ввод своих игроков напрямую (ConfigureServer); отправляет только клиент-владелец.
            if (IsServer || !IsOwner || IsBot || _networkInput == null || _localPlayers == null)
            {
                return;
            }

            int localIndex = _localIndex.Value;
            var players = _localPlayers.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Index == localIndex)
                {
                    _networkInput.SetLocalSource(players[i].Input);
                    return;
                }
            }

            Debug.LogWarning($"Character in slot P{Slot + 1} is ours, but local player {localIndex} is missing.", this);
        }

        private void ApplyColor()
        {
            if (_renderer == null || _matchConfig == null)
            {
                return;
            }

            Color color = IsBot ? _matchConfig.BotColor : _matchConfig.GetPlayerColor(_slot.Value);
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
