using System.Collections.Generic;
using MessagePipe;
using R3;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Messages;
using TinyTumble.Gameplay.Combat;
using TinyTumble.Gameplay.Match;
using TinyTumble.Networking.Input;
using TinyTumble.Networking.Sync;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Персонаж-капсула: движение, ragdoll, бой. Игрок, удалённый игрок и бот управляют им одинаково,
    /// через <see cref="IPlayerInput"/>.
    /// Хост авторитетен: он симулирует всех и решает попадания. Клиент-владелец предсказывает своё движение
    /// в обычном состоянии и плавно исправляет расхождение с хостом; в остальных состояниях (удар, ragdoll,
    /// его держат) и для чужих персонажей тело кинематическое и интерполируется по снимкам хоста.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerCharacter : NetworkBehaviour, IGrabbable, ICharacterInfo, ICombatant
    {
        public const int BotSlot = -1;

        private const float RagdollSpin = 9f;
        private const float BreakFreeSpeed = 3f;
        private const float GroundSearchDistance = 2f;

        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private CapsuleCollider _collider;
        [SerializeField] private NetworkPlayerInput _networkInput;
        [SerializeField] private CharacterNetSync _sync;
        [SerializeField] private CharacterPresentation _presentation;

        // Слот определяет цвет у всех; локальный индекс нужен клиенту-владельцу, чтобы найти свой ввод.
        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(BotSlot);
        private readonly NetworkVariable<byte> _localIndex = new NetworkVariable<byte>();
        private readonly NetworkVariable<float> _shake = new NetworkVariable<float>();
        private readonly NetworkVariable<GrabLinks> _holders = new NetworkVariable<GrabLinks>();
        private readonly ReactiveProperty<float> _shakeView = new ReactiveProperty<float>(0f);

        private CharacterConfig _characterConfig;
        private CombatConfig _combatConfig;
        private MatchConfig _matchConfig;
        private MatchSetup _matchSetup;
        private ILocalPlayers _localPlayers;
        private MatchCharacters _characters;
        private IPublisher<CharacterHit> _hitPublisher;

        private CharacterMotor _motor;
        private CharacterCombat _combat;
        private CollisionIgnores _ignores;
        private CharacterState _state;
        private CharacterFlags _flags;
        private float _stateSince;

        // Только на хосте.
        private IPlayerInput _input;
        private MatchRoster _roster;
        private int _participantId = -1;
        private int _pendingSlot = BotSlot;
        private byte _pendingLocalIndex;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation = Quaternion.identity;
        private bool _eliminated;
        private bool _removedByMatch;
        private float _grabImmuneUntil;
        private float _grabStart;
        private readonly List<PlayerCharacter> _holderRefs = new List<PlayerCharacter>(GrabLinks.Capacity);
        private PlayerCharacter _thrownBy;
        private float _thrownUntil;
        private Quaternion _getUpFrom;
        private Vector3 _getUpCenterFrom;
        private Vector3 _getUpCenterTo;

        // Только у клиента-владельца.
        private IPlayerInput _localSource;
        private bool _predicting;
        private uint _lastSubmitted;
        private byte _seenTeleport;
        private float _localPushReady;
        private float _localEmoteReady;

        public int ParticipantId => _participantId;

        public ulong Id => NetworkObjectId;

        public Vector3 Position => _rigidbody.position;

        public bool IsSwinging => _combat != null && _combat.IsSwinging;

        /// <summary>Слот игрока 0..3 или <see cref="BotSlot"/>.</summary>
        public int Slot => _slot.Value;

        public bool IsBot => _slot.Value == BotSlot;

        /// <summary>Хост: состояние по правилам. Клиент: состояние из последнего показанного снимка.</summary>
        public CharacterState State => _state;

        public CharacterFlags Flags => _flags;

        /// <summary>Встряск, %. Для HUD; меняет только хост.</summary>
        public ReadOnlyReactiveProperty<float> Shake => _shakeView;

        public NetworkPlayerInput NetworkInput => _networkInput;

        public CharacterPresentation Presentation => _presentation;

        public Rigidbody Body => _rigidbody;

        public float Height => _characterConfig != null ? _characterConfig.CharacterHeight : 1f;

        public Vector3 Facing => _motor != null ? _motor.Facing : transform.forward;

        /// <summary>Хост: боевые действия персонажа.</summary>
        public CharacterCombat Combat => _combat;

        /// <summary>Хост: по персонажу можно попасть (он на столе и не выбыл).</summary>
        public bool IsTargetable => IsSpawned && !_eliminated;

        public bool IsEliminated => _eliminated;

        /// <summary>Клиент-владелец сейчас предсказывает движение (диагностика, тесты).</summary>
        public bool IsPredicting => _predicting;

        public bool IsHeavy => true;

        public float HoldBottomOffset => 0f;

        private Vector3 CenterOffset => Vector3.up * (Height * 0.5f);

        [Inject]
        public void Construct(
            CharacterConfig characterConfig,
            CombatConfig combatConfig,
            MatchConfig matchConfig,
            MatchSetup matchSetup,
            ILocalPlayers localPlayers,
            MatchCharacters characters,
            IPublisher<CharacterHit> hitPublisher)
        {
            _characterConfig = characterConfig;
            _combatConfig = combatConfig;
            _matchConfig = matchConfig;
            _matchSetup = matchSetup;
            _localPlayers = localPlayers;
            _characters = characters;
            _hitPublisher = hitPublisher;
            _motor = new CharacterMotor(_rigidbody, characterConfig);
            _combat = new CharacterCombat(this, combatConfig);
            ApplyBodyDimensions();
        }

        /// <summary>Хост, до Spawn: кто управляет персонажем, в каком он слоте и где возрождается.</summary>
        public void ConfigureServer(int participantId, int slot, int localIndex, IPlayerInput input, MatchRoster roster)
        {
            _participantId = participantId;
            _pendingSlot = slot;
            _pendingLocalIndex = (byte)localIndex;
            _input = input;
            _roster = roster;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        /// <summary>Хост: матч убирает персонажа сам (конец матча), выбывание засчитывать не нужно.</summary>
        public void MarkRemovedByMatch()
        {
            _removedByMatch = true;
        }

        public override void OnNetworkSpawn()
        {
            _ignores = new CollisionIgnores(_collider);
            _motor.SetFacing(transform.forward);
            _state = CharacterState.Normal;
            _stateSince = Time.fixedTime;

            if (IsServer)
            {
                // Переменные пишем здесь, а не до Spawn: до спавна NGO ещё не связал их с объектом.
                _slot.Value = _pendingSlot;
                _localIndex.Value = _pendingLocalIndex;
                ApplyStatePhysics();
            }
            else
            {
                // Чужое тело ведут снимки хоста; своё станет динамическим, когда начнётся предсказание.
                _rigidbody.isKinematic = true;
            }

            _slot.OnValueChanged += OnSlotChanged;
            _localIndex.OnValueChanged += OnLocalIndexChanged;
            _shake.OnValueChanged += OnShakeChanged;
            _holders.OnValueChanged += OnHoldersChanged;
            _shakeView.Value = _shake.Value;
            _presentation.Bind(this);
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
            _shake.OnValueChanged -= OnShakeChanged;
            _holders.OnValueChanged -= OnHoldersChanged;

            if (IsServer)
            {
                DetachAllGrabs(Time.fixedTime);
            }

            if (_ignores != null)
            {
                _ignores.RestoreAll();
            }

            if (_characters != null)
            {
                _characters.Remove(this);
            }

            _localSource = null;
            _predicting = false;

            // Владелец отключился посреди матча: NGO убрал его персонажа, для правил это выбывание.
            // При закрытии сессии выбывания не считаем, иначе матч «закончится» по пути в меню.
            if (IsServer && !_eliminated && !_removedByMatch && !NetworkManager.ShutdownInProgress && _roster != null)
            {
                _eliminated = true;
                _roster.Eliminate(_participantId);
            }
        }

        public override void OnDestroy()
        {
            _shakeView.Dispose();
            base.OnDestroy();
        }

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _collider = GetComponent<CapsuleCollider>();
            _networkInput = GetComponent<NetworkPlayerInput>();
            _sync = GetComponent<CharacterNetSync>();
            _presentation = GetComponentInChildren<CharacterPresentation>();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || _motor == null)
            {
                return;
            }

            float now = Time.fixedTime;
            float deltaTime = Time.fixedDeltaTime;
            _ignores.Tick(now);

            if (IsServer)
            {
                ServerTick(now, deltaTime);
            }
            else if (IsOwner && _localSource != null)
            {
                OwnerTick(now, deltaTime);
            }
            else
            {
                FollowHost(deltaTime);
            }
        }

        // ---------- Хост: симуляция ----------

        private void ServerTick(float now, float deltaTime)
        {
            if (_eliminated)
            {
                return;
            }

            // Сначала снимок — это результат прошлого тика, вместе с номером последнего применённого кадра ввода.
            _flags = CollectFlags();
            _sync.ServerTick(CaptureSnapshot());

            // Ровно один сэмпл за тик, даже без управления: на нём держится накопление нажатий и очередь сетевого ввода.
            PlayerInputFrame frame = _input != null ? _input.Sample() : default;

            switch (_state)
            {
                case CharacterState.Normal:
                    _motor.Step(frame, now, deltaTime, _combat.SpeedMultiplier, allowDash: true);
                    break;

                case CharacterState.Stagger:
                    StepUncontrolled(deltaTime);
                    if (now - _stateSince >= _combatConfig.StaggerTime)
                    {
                        EnterState(CharacterState.Normal, now);
                    }

                    break;

                case CharacterState.Ragdoll:
                    StepUncontrolled(deltaTime);
                    _motor.ProbeGround(now);
                    if (CombatRules.ShouldGetUp(now - _stateSince, _motor.IsGrounded, _rigidbody.linearVelocity.magnitude, _characterConfig))
                    {
                        BeginGetUp(now);
                    }

                    break;

                case CharacterState.GettingUp:
                    StepGetUp(now);
                    break;

                case CharacterState.Grabbed:
                    StepGrabbed(now);
                    break;
            }

            _combat.Step(frame, now, _state == CharacterState.Normal);

            if (_thrownBy != null && now > _thrownUntil)
            {
                _thrownBy = null;
            }

            if (_rigidbody.position.y < _matchConfig.EliminationHeight)
            {
                Fall();
            }
        }

        private void StepUncontrolled(float deltaTime)
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            _motor.ApplyFallGravity(ref velocity, deltaTime);
            _rigidbody.linearVelocity = velocity;
        }

        private void BeginGetUp(float now)
        {
            _getUpFrom = _rigidbody.rotation;
            _getUpCenterFrom = _rigidbody.position + _rigidbody.rotation * CenterOffset;
            _getUpCenterTo = _getUpCenterFrom;

            // Встаём над опорой: центр поднимается на половину роста, иначе ноги ушли бы в пол.
            if (Physics.Raycast(_getUpCenterFrom, Vector3.down, out RaycastHit hit, GroundSearchDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                _getUpCenterTo.y = Mathf.Max(_getUpCenterFrom.y, hit.point.y + Height * 0.5f);
            }

            // Смотреть будем туда, куда смотрела «голова» лёжа.
            Vector3 forward = Vector3.ProjectOnPlane(_rigidbody.rotation * Vector3.forward, Vector3.up);
            Vector3 up = Vector3.ProjectOnPlane(_rigidbody.rotation * Vector3.up, Vector3.up);
            _motor.SetFacing(forward.sqrMagnitude > up.sqrMagnitude ? forward : up);
            EnterState(CharacterState.GettingUp, now);
        }

        private void StepGetUp(float now)
        {
            float t = Mathf.Clamp01((now - _stateSince) / _characterConfig.GetUpDuration);
            float eased = t * t * (3f - 2f * t);
            Quaternion rotation = Quaternion.Slerp(_getUpFrom, Quaternion.LookRotation(Facing, Vector3.up), eased);
            Vector3 center = Vector3.Lerp(_getUpCenterFrom, _getUpCenterTo, eased);
            _rigidbody.MoveRotation(rotation);
            _rigidbody.MovePosition(center - rotation * CenterOffset);

            if (t >= 1f)
            {
                EnterState(CharacterState.Normal, now);
                _rigidbody.linearVelocity = Vector3.zero;
                _grabImmuneUntil = now + _characterConfig.GrabImmunityAfterRagdoll;
                _motor.ResetTransient();
            }
        }

        private void EnterState(CharacterState state, float now)
        {
            _state = state;
            _stateSince = now;
            ApplyStatePhysics();
        }

        private void ApplyStatePhysics()
        {
            switch (_state)
            {
                case CharacterState.Ragdoll:
                    _rigidbody.isKinematic = false;
                    _rigidbody.constraints = RigidbodyConstraints.None;
                    break;

                case CharacterState.GettingUp:
                case CharacterState.Grabbed:
                    // Положение задаём сами (подъём, руки держащих): физика не должна с этим спорить.
                    _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                    _rigidbody.isKinematic = true;
                    break;

                default:
                    _rigidbody.isKinematic = false;
                    _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                    break;
            }
        }

        private CharacterFlags CollectFlags()
        {
            CharacterFlags flags = CharacterFlags.None;
            if (_combat.IsHolding)
            {
                flags |= CharacterFlags.Holding;
            }

            if (_combat.IsCarryingHeavy)
            {
                flags |= CharacterFlags.CarryingHeavy;
            }

            if (_combat.IsChargingThrow)
            {
                flags |= CharacterFlags.ChargingThrow;
            }

            return flags;
        }

        private BodySnapshot CaptureSnapshot()
        {
            return new BodySnapshot
            {
                Time = Time.fixedTimeAsDouble,
                Ack = ReferenceEquals(_input, _networkInput) ? _networkInput.LastConsumedSequence : 0u,
                Position = _rigidbody.position,
                Rotation = _rigidbody.rotation,
                Velocity = _rigidbody.isKinematic ? Vector3.zero : _rigidbody.linearVelocity,
                State = (byte)_state,
                Flags = (byte)_flags,
                Teleport = _sync.TeleportCounter,
            };
        }

        // ---------- Хост: попадания ----------

        /// <summary>
        /// Хост: по персонажу попали. Встряск растёт, отлёт сильнее с каждым процентом; сильный удар валит в ragdoll.
        /// </summary>
        public void ReceiveHit(float shakeAdded, Vector3 direction, PlayerCharacter attacker, HitKind kind, bool forceRagdoll, Vector3 point)
        {
            if (!IsServer || _eliminated)
            {
                return;
            }

            float now = Time.fixedTime;
            float shakeAfter = CombatRules.AddShake(_shake.Value, shakeAdded, _combatConfig);
            _shake.Value = shakeAfter;
            float speed = CombatRules.KnockbackSpeed(shakeAfter, _combatConfig);
            Vector3 velocity = CombatRules.KnockbackVelocity(direction, speed, _combatConfig);

            if (_state == CharacterState.Grabbed)
            {
                ReleaseFromHolders();
            }

            _combat.CancelActions(now);
            bool ragdoll = forceRagdoll || CombatRules.ShouldRagdoll(shakeAfter, _combatConfig);
            EnterState(ragdoll ? CharacterState.Ragdoll : CharacterState.Stagger, now);
            _rigidbody.linearVelocity = velocity;
            if (ragdoll)
            {
                // Кувырок через голову по направлению удара.
                Vector3 axis = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(velocity, Vector3.up).normalized);
                _rigidbody.angularVelocity = axis * RagdollSpin;
            }

            BroadcastHit(attacker, kind, point, speed, shakeAfter);
        }

        public void OnEnteredKillZone()
        {
            if (IsServer)
            {
                Fall();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Брошенный игрок сбивает того, в кого врезался (бросок игроком, раздел 4 GDD).
            if (!IsServer || _thrownBy == null || Time.fixedTime > _thrownUntil)
            {
                return;
            }

            if (collision.relativeVelocity.magnitude < _combatConfig.ThrownHitMinSpeed)
            {
                return;
            }

            Rigidbody other = collision.rigidbody;
            if (other == null || !other.TryGetComponent(out PlayerCharacter target) || target == this || target == _thrownBy || !target.IsTargetable)
            {
                return;
            }

            PlayerCharacter thrower = _thrownBy;
            _thrownBy = null;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : target.Body.position;
            target.ReceiveHit(_combatConfig.PlayerThrowShake, _rigidbody.linearVelocity, thrower, HitKind.ThrownPlayer, forceRagdoll: false, point);
        }

        private void Fall()
        {
            if (_eliminated)
            {
                return;
            }

            if (_matchSetup.RespawnOnFall)
            {
                Respawn();
                return;
            }

            Eliminate();
        }

        private void Respawn()
        {
            float now = Time.fixedTime;
            DetachAllGrabs(now);
            _thrownBy = null;
            EnterState(CharacterState.Normal, now);
            _rigidbody.position = _spawnPosition;
            _rigidbody.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _motor.SetFacing(_spawnRotation * Vector3.forward);
            _motor.ResetTransient();
            _shake.Value = 0f;
            _sync.MarkTeleported();
            _presentation.Teleported();
        }

        private void Eliminate()
        {
            _eliminated = true;
            DetachAllGrabs(Time.fixedTime);
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

        // ---------- Хост: захват (этот персонаж — цель) ----------

        public bool CanBeGrabbedBy(PlayerCharacter grabber, float now)
        {
            return IsServer && !_eliminated && grabber != null && grabber != this
                && now >= _grabImmuneUntil
                && _holderRefs.Count < GrabLinks.Capacity
                && !_holderRefs.Contains(grabber)
                && !ReferenceEquals(_combat.Held, grabber)
                && _state != CharacterState.GettingUp;
        }

        public void OnGrabbed(PlayerCharacter grabber, float now)
        {
            if (_holderRefs.Count == 0)
            {
                _grabStart = now;
                _combat.CancelActions(now);
                _thrownBy = null;
                EnterState(CharacterState.Grabbed, now);
            }

            _holderRefs.Add(grabber);
            GrabLinks links = _holders.Value;
            links.TryAdd(grabber.NetworkObjectId);
            _holders.Value = links;
        }

        public void OnReleased(PlayerCharacter grabber, Vector3 velocity, bool thrown, float now)
        {
            RemoveHolder(grabber);
            if (_holderRefs.Count > 0)
            {
                return;
            }

            if (thrown)
            {
                float shakeAfter = CombatRules.AddShake(_shake.Value, _combatConfig.PlayerThrowShake, _combatConfig);
                _shake.Value = shakeAfter;
                EnterState(CharacterState.Ragdoll, now);
                _rigidbody.linearVelocity = velocity;
                Vector3 axis = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(velocity, Vector3.up).normalized);
                _rigidbody.angularVelocity = axis * RagdollSpin;
                _thrownBy = grabber;
                _thrownUntil = now + _combatConfig.ThrownLifetime;
                BroadcastHit(grabber, HitKind.ThrownPlayer, _rigidbody.position, velocity.magnitude, shakeAfter);
            }
            else
            {
                EnterState(CharacterState.Stagger, now);
                _rigidbody.linearVelocity = velocity;
            }
        }

        private void StepGrabbed(float now)
        {
            for (int i = _holderRefs.Count - 1; i >= 0; i--)
            {
                PlayerCharacter holder = _holderRefs[i];
                if (holder == null || !holder.IsTargetable || !ReferenceEquals(holder.Combat.Held, this))
                {
                    RemoveHolder(holder);
                }
            }

            if (_holderRefs.Count == 0)
            {
                EnterState(CharacterState.Stagger, now);
                return;
            }

            if (now - _grabStart >= CombatRules.HoldLimit(_holderRefs.Count, _combatConfig))
            {
                BreakFree(now);
                return;
            }

            Vector3 position = Vector3.zero;
            for (int i = 0; i < _holderRefs.Count; i++)
            {
                position += _holderRefs[i].Combat.HoldPosition(HoldBottomOffset);
            }

            position /= _holderRefs.Count;
            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(Quaternion.LookRotation(_holderRefs[0].Facing, Vector3.up));
        }

        /// <summary>Вырвался сам: держащие теряют его, он отскакивает и недолго неуязвим для захвата.</summary>
        private void BreakFree(float now)
        {
            Vector3 away = _holderRefs.Count > 0 ? -_holderRefs[0].Facing : -Facing;
            ReleaseFromHolders();
            EnterState(CharacterState.Stagger, now);
            _rigidbody.linearVelocity = (away + Vector3.up).normalized * BreakFreeSpeed;
            _grabImmuneUntil = now + _characterConfig.GrabImmunityAfterRagdoll;
        }

        private void ReleaseFromHolders()
        {
            for (int i = _holderRefs.Count - 1; i >= 0; i--)
            {
                PlayerCharacter holder = _holderRefs[i];
                if (holder != null && holder.Combat != null)
                {
                    holder.Combat.ForgetHeld(this);
                }
            }

            _holderRefs.Clear();
            _holders.Value = default;
        }

        private void RemoveHolder(PlayerCharacter holder)
        {
            _holderRefs.Remove(holder);
            GrabLinks links = _holders.Value;
            if (holder != null && links.Remove(holder.NetworkObjectId))
            {
                _holders.Value = links;
            }
            else if (_holderRefs.Count == 0 && links.Count > 0)
            {
                // Держащий уже уничтожен и id не узнать: держащих не осталось — чистим целиком.
                _holders.Value = default;
            }
        }

        private void DetachAllGrabs(float now)
        {
            if (_combat != null && _combat.IsHolding)
            {
                _combat.Drop(now);
            }

            if (_holderRefs.Count > 0)
            {
                ReleaseFromHolders();
            }
        }

        // ---------- Клиент-владелец: предсказание ----------

        private void OwnerTick(float now, float deltaTime)
        {
            // Запись «после кадра N» делаем в начале следующего тика: физика уже применила скорость из прошлого шага.
            if (_predicting && _lastSubmitted != 0)
            {
                _sync.History.Record(_lastSubmitted, _rigidbody.position, _rigidbody.linearVelocity);
            }

            uint sequence = _networkInput.Submit(_localSource.Sample(), out PlayerInputFrame frame);
            _lastSubmitted = sequence;
            PlayLocalFeedback(frame, now);

            if (!_sync.HasState)
            {
                return;
            }

            BodySnapshot latest = _sync.Latest;
            _flags = (CharacterFlags)latest.Flags;
            bool hostAllows = (CharacterState)latest.State == CharacterState.Normal && latest.Teleport == _seenTeleport;

            if (_predicting && !hostAllows)
            {
                StopPredicting();
            }

            if (_predicting)
            {
                Reconcile(deltaTime);
                float speed = (_flags & CharacterFlags.CarryingHeavy) != 0 ? 1f - _combatConfig.HeavyCarrySlowdown : 1f;
                _motor.Step(frame, now, deltaTime, speed, allowDash: true);
                _state = CharacterState.Normal;
                return;
            }

            FollowHost(deltaTime);
            if (hostAllows && _state == CharacterState.Normal)
            {
                StartPredicting(latest);
            }
        }

        private void StartPredicting(in BodySnapshot latest)
        {
            // Показанное отстаёт от хоста на задержку интерполяции: прыгаем к свежему снимку, визуал догонит плавно.
            Vector3 delta = latest.Position - _rigidbody.position;
            _rigidbody.isKinematic = false;
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            _rigidbody.position = latest.Position;
            _rigidbody.linearVelocity = latest.Velocity;
            _rigidbody.angularVelocity = Vector3.zero;
            _motor.SetFacing(latest.Rotation * Vector3.forward);
            _motor.ResetTransient();
            _sync.History.Clear();
            _sync.Corrector.Reset();
            _presentation.AbsorbCorrection(delta);
            _predicting = true;
        }

        private void StopPredicting()
        {
            _predicting = false;
            _rigidbody.isKinematic = true;
            _sync.History.Clear();
            _sync.Corrector.Reset();
        }

        private void Reconcile(float deltaTime)
        {
            PredictionHistory history = _sync.History;
            PredictionCorrector corrector = _sync.Corrector;

            if (_sync.TryTakeUnread(out BodySnapshot snapshot) && snapshot.Ack != 0
                && history.TryGet(snapshot.Ack, out Vector3 predicted, out Vector3 predictedVelocity))
            {
                if (corrector.Evaluate(snapshot.Position, predicted, out Vector3 error) == CorrectionKind.Snap)
                {
                    ShiftBody(error);
                    _rigidbody.linearVelocity += snapshot.Velocity - predictedVelocity;
                }
            }

            Vector3 step = corrector.Step(deltaTime);
            if (step != Vector3.zero)
            {
                ShiftBody(step);
            }
        }

        private void ShiftBody(Vector3 delta)
        {
            _rigidbody.position += delta;
            _sync.History.Shift(delta);
            _presentation.AbsorbCorrection(delta);
        }

        private void PlayLocalFeedback(in PlayerInputFrame frame, float now)
        {
            // Мгновенный отклик у владельца: замах и эмоцию видно сразу, не дожидаясь хоста.
            if (frame.WasPressed(PlayerButtons.Push) && (_flags & CharacterFlags.Holding) == 0 && now >= _localPushReady)
            {
                _localPushReady = now + _combatConfig.PushCooldown;
                _presentation.PlaySwing();
            }

            int emote = CharacterCombat.PressedEmote(frame);
            if (emote >= 0 && now >= _localEmoteReady)
            {
                _localEmoteReady = now + _combatConfig.EmoteCooldown;
                _presentation.PlayEmote(emote);
            }
        }

        // ---------- Клиент: интерполяция ----------

        private void FollowHost(float deltaTime)
        {
            if (!_sync.TrySample(deltaTime, out BodySnapshot snapshot))
            {
                return;
            }

            if (snapshot.Teleport != _seenTeleport)
            {
                _seenTeleport = snapshot.Teleport;
                _rigidbody.position = snapshot.Position;
                _rigidbody.rotation = snapshot.Rotation;
                transform.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
                _presentation.Teleported();
            }
            else
            {
                _rigidbody.MovePosition(snapshot.Position);
                _rigidbody.MoveRotation(snapshot.Rotation);
            }

            _state = (CharacterState)snapshot.State;
            _flags = (CharacterFlags)snapshot.Flags;
            _motor.SetFacing(snapshot.Rotation * Vector3.forward);
        }

        // ---------- События для всех ----------

        /// <summary>Хост: начался замах (толчок или бросок).</summary>
        public void BroadcastSwing()
        {
            SwingRpc();
        }

        public void BroadcastEmote(int index)
        {
            EmoteRpc((byte)index);
        }

        private void BroadcastHit(PlayerCharacter attacker, HitKind kind, Vector3 point, float knockback, float shakeAfter)
        {
            ulong attackerId = attacker != null ? attacker.NetworkObjectId : CharacterHit.NoAttacker;
            HitRpc(attackerId, (byte)kind, point, knockback, shakeAfter);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingRpc()
        {
            // Клиент-владелец уже показал замах сам.
            if (IsOwner && !IsServer)
            {
                return;
            }

            _presentation.PlaySwing();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EmoteRpc(byte index)
        {
            if (IsOwner && !IsServer)
            {
                return;
            }

            _presentation.PlayEmote(index);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HitRpc(ulong attackerId, byte kind, Vector3 point, float knockback, float shakeAfter)
        {
            if (_hitPublisher != null)
            {
                _hitPublisher.Publish(new CharacterHit(NetworkObjectId, attackerId, (HitKind)kind, point, knockback, shakeAfter));
            }
        }

        // ---------- Слот, цвет, ввод ----------

        private void OnSlotChanged(int previous, int current)
        {
            ApplyColor();
        }

        private void OnLocalIndexChanged(byte previous, byte current)
        {
            BindLocalInput();
        }

        private void OnShakeChanged(float previous, float current)
        {
            _shakeView.Value = current;
        }

        private void OnHoldersChanged(GrabLinks previous, GrabLinks current)
        {
            _ignores.Apply(previous, current, NetworkManager, Time.fixedTime, _combatConfig.ThrowIgnoreTime);
        }

        private void BindLocalInput()
        {
            // Хост подаёт ввод своих игроков напрямую (ConfigureServer); отправляет только клиент-владелец.
            if (IsServer || !IsOwner || IsBot || _localPlayers == null)
            {
                return;
            }

            int localIndex = _localIndex.Value;
            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Index == localIndex)
                {
                    _localSource = players[i].Input;
                    return;
                }
            }

            Debug.LogWarning($"Character in slot P{Slot + 1} is ours, but local player {localIndex} is missing.", this);
        }

        private void ApplyColor()
        {
            if (_matchConfig == null)
            {
                return;
            }

            _presentation.SetColor(IsBot ? _matchConfig.BotColor : _matchConfig.GetPlayerColor(_slot.Value));
        }

        private void ApplyBodyDimensions()
        {
            _rigidbody.mass = _characterConfig.Mass;
            if (_collider != null)
            {
                _collider.height = _characterConfig.CharacterHeight;
                _collider.radius = _characterConfig.BodyRadius;
                _collider.center = CenterOffset;
            }
        }
    }
}
