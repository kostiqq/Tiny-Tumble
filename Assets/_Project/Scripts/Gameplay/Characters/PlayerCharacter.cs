using System.Collections.Generic;
using MessagePipe;
using R3;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Messages;
using TinyTumble.Gameplay.Cameras;
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
    /// Физический персонаж (active ragdoll): движение, равновесие, позы, бой. Игрок, удалённый игрок и бот
    /// управляют им одинаково, через <see cref="IPlayerInput"/>.
    /// Физику всех персонажей считает только хост: он применяет ввод, решает попадания и рассылает снимки
    /// (таз и повороты костей). Клиенты, включая владельца, показывают снимки с интерполяцией; предсказания нет,
    /// поэтому все машины видят одно и то же. Владелец сразу показывает свой замах и эмоцию поверх снимков.
    /// </summary>
    public sealed class PlayerCharacter : NetworkBehaviour, IGrabbable, ICharacterInfo, ICombatant, ICollisionBody
    {
        public const int BotSlot = -1;

        private const float RagdollSpin = 9f;
        private const float BreakFreeSpeed = 3f;
        private const float UprightDot = 0.9f;
        private const float OverlayHold = 0.2f;
        private const float OverlayFade = 0.2f;

        [SerializeField] private RagdollRig _rig;
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
        private CameraMoveFrame _moveFrame;
        private IPublisher<CharacterHit> _hitPublisher;

        private CharacterMotor _motor;
        private ProceduralPose _pose;
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
        private int _emote = -1;
        private float _emoteStart;

        // Только у клиента-владельца.
        private IPlayerInput _localSource;
        private float _localPushReady;
        private float _localEmoteReady;
        private float _localSwingStart = float.NegativeInfinity;
        private float _localEmoteStart = float.NegativeInfinity;
        private int _localEmote = -1;

        public int ParticipantId => _participantId;

        public ulong Id => NetworkObjectId;

        /// <summary>Опора под персонажем (уровень подошв стоя), по физике хоста. Для правил и ботов.</summary>
        public Vector3 Position => _rig.FeetPosition;

        /// <summary>Середина роста над видимым тазом: для камеры, без дрожания между тиками физики.</summary>
        public Vector3 VisualCenter => _rig.Hips.transform.position + Vector3.up * (_rig.Height * 0.5f - _rig.RideHeight);

        /// <summary>Что видно на экране и движется вместе с телом: для группы камеры.</summary>
        public Transform TrackingTarget => _rig.Hips.transform;

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

        public RagdollRig Rig => _rig;

        /// <summary>Таз: корень физического скелета.</summary>
        public Rigidbody Body => _rig.Hips;

        public Collider[] Colliders => _rig.Colliders;

        public float Height => _rig.Height;

        /// <summary>«Вверх» персонажа по тазу: у стоящего совпадает с мировым.</summary>
        public Vector3 Up => _rig.Up;

        public Vector3 Facing => _motor != null ? _motor.Facing : transform.forward;

        /// <summary>Хост: боевые действия персонажа.</summary>
        public CharacterCombat Combat => _combat;

        /// <summary>Хост: по персонажу можно попасть (он на столе и не выбыл).</summary>
        public bool IsTargetable => IsSpawned && !_eliminated;

        public bool IsEliminated => _eliminated;

        public bool IsHeavy => true;

        /// <summary>Держат за таз; низ — подошвы.</summary>
        public float HoldBottomOffset => _rig.RideHeight;

        [Inject]
        public void Construct(
            CharacterConfig characterConfig,
            CombatConfig combatConfig,
            MatchConfig matchConfig,
            MatchSetup matchSetup,
            ILocalPlayers localPlayers,
            MatchCharacters characters,
            CameraMoveFrame moveFrame,
            IPublisher<CharacterHit> hitPublisher)
        {
            _characterConfig = characterConfig;
            _combatConfig = combatConfig;
            _matchConfig = matchConfig;
            _matchSetup = matchSetup;
            _localPlayers = localPlayers;
            _characters = characters;
            _moveFrame = moveFrame;
            _hitPublisher = hitPublisher;
            _rig.Configure(characterConfig.Mass, characterConfig.PoseFrequency, characterConfig.PoseDamping);
            _motor = new CharacterMotor(_rig, characterConfig);
            _pose = new ProceduralPose(_rig, characterConfig);
            _combat = new CharacterCombat(this, combatConfig);
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
            _ignores = new CollisionIgnores(_rig.Colliders);
            _motor.SetFacing(transform.forward);
            _state = CharacterState.Normal;
            _stateSince = Time.fixedTime;

            if (IsServer)
            {
                // Переменные пишем здесь, а не до Spawn: до спавна NGO ещё не связал их с объектом.
                _slot.Value = _pendingSlot;
                _localIndex.Value = _pendingLocalIndex;
                _rig.SetSimulated(true);
                ApplyStatePhysics();
            }
            else
            {
                // Физику считает только хост: у клиента скелет ставится по снимкам.
                _rig.SetSimulated(false);
            }

            _slot.OnValueChanged += OnSlotChanged;
            _localIndex.OnValueChanged += OnLocalIndexChanged;
            _shake.OnValueChanged += OnShakeChanged;
            _holders.OnValueChanged += OnHoldersChanged;
            _shakeView.Value = _shake.Value;
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
            _rig = GetComponent<RagdollRig>();
            _networkInput = GetComponent<NetworkPlayerInput>();
            _sync = GetComponent<CharacterNetSync>();
            _presentation = GetComponent<CharacterPresentation>();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || _motor == null)
            {
                return;
            }

            float now = Time.fixedTime;
            _ignores.Tick(now);

            if (IsServer)
            {
                ServerTick(now, Time.fixedDeltaTime);
            }
            else if (IsOwner && _localSource != null)
            {
                OwnerTick(now);
            }
        }

        private void Update()
        {
            // Клиент ставит скелет каждый кадр отрисовки, а не тик физики: движение без ступенек.
            if (!IsSpawned || IsServer || _motor == null)
            {
                return;
            }

            FollowHost(Time.deltaTime);
            if (IsOwner)
            {
                ApplyLocalOverlay(Time.time);
            }
        }

        // ---------- Хост: симуляция ----------

        private void ServerTick(float now, float deltaTime)
        {
            if (_eliminated)
            {
                return;
            }

            // Сначала снимок — это результат прошлого тика.
            _flags = CollectFlags();
            _sync.ServerTick(CaptureSnapshot());

            // Ровно один сэмпл за тик, даже без управления: на нём держится накопление нажатий и очередь сетевого ввода.
            PlayerInputFrame frame = _input != null ? _input.Sample() : default;

            EvaluatePose(now, deltaTime);
            Quaternion[] desired = _pose.Desired;
            float yaw = _pose.YawOffset;

            switch (_state)
            {
                case CharacterState.Normal:
                    _motor.Step(frame, now, deltaTime, _combat.SpeedMultiplier, allowDash: true);
                    _motor.Support(1f, 1f, desired, yaw);
                    _rig.SetDriveStrength(1f);
                    break;

                case CharacterState.Stagger:
                    _motor.ProbeGround(now);
                    _motor.Coast();
                    _motor.Support(_characterConfig.StaggerBalance, 1f, desired, yaw);
                    _rig.SetDriveStrength(1f);
                    if (now - _stateSince >= _combatConfig.StaggerTime)
                    {
                        EnterState(CharacterState.Normal, now);
                    }

                    break;

                case CharacterState.Ragdoll:
                    _motor.ProbeGround(now);
                    _motor.Coast();
                    _rig.SetDriveStrength(_characterConfig.LimpPose);
                    if (CombatRules.ShouldGetUp(now - _stateSince, IsNearGround(), _rig.Hips.linearVelocity.magnitude, _characterConfig))
                    {
                        BeginGetUp(now);
                    }

                    break;

                case CharacterState.GettingUp:
                    StepGetUp(now, desired, yaw);
                    break;

                case CharacterState.Grabbed:
                    _rig.SetDriveStrength(_characterConfig.GrabbedPose);
                    StepGrabbed(now);
                    break;
            }

            _rig.DriveTowards(desired);
            _combat.Step(frame, now, _state == CharacterState.Normal);

            if (_thrownBy != null && now > _thrownUntil)
            {
                _thrownBy = null;
            }

            if (_rig.Hips.position.y < _matchConfig.EliminationHeight)
            {
                Fall();
            }
        }

        private void EvaluatePose(float now, float deltaTime)
        {
            Vector3 planar = _motor.PlanarVelocity;
            float speed = planar.magnitude;
            _pose.Advance(speed * deltaTime, deltaTime);

            if (_emote >= 0 && now - _emoteStart >= _combatConfig.EmoteDuration)
            {
                _emote = -1;
            }

            var input = new PoseInput
            {
                Gait = _characterConfig.MoveSpeed > 0f ? speed / _characterConfig.MoveSpeed : 0f,
                Grounded = _motor.IsGrounded,
                State = _state,
                Flags = _flags,
                Swing = _combat.SwingWeight(now),
                Emote = _state == CharacterState.Normal ? _emote : -1,
                EmoteProgress = (now - _emoteStart) / _combatConfig.EmoteDuration,
            };
            _pose.Evaluate(input);
        }

        /// <summary>Лежит на опоре: таз не дальше обычной высоты над ней.</summary>
        private bool IsNearGround()
        {
            return _motor.GroundDistance <= _rig.RideHeight + _characterConfig.GroundProbeDistance;
        }

        private void BeginGetUp(float now)
        {
            // Смотреть будем туда, куда смотрела «голова» лёжа.
            Vector3 forward = Vector3.ProjectOnPlane(_rig.Forward, Vector3.up);
            Vector3 up = Vector3.ProjectOnPlane(_rig.Up, Vector3.up);
            _motor.SetFacing(forward.sqrMagnitude > up.sqrMagnitude ? forward : up);
            EnterState(CharacterState.GettingUp, now);
        }

        private void StepGetUp(float now, Quaternion[] desired, float yaw)
        {
            float elapsed = now - _stateSince;
            float t = Mathf.Clamp01(elapsed / _characterConfig.GetUpDuration);
            float eased = t * t * (3f - 2f * t);

            _motor.ProbeGround(now);
            _motor.Coast();
            _motor.Support(eased, eased, desired, yaw);
            _rig.SetDriveStrength(Mathf.Lerp(_characterConfig.LimpPose, 1f, eased));

            bool upright = Vector3.Dot(_rig.Up, Vector3.up) >= UprightDot;
            if ((t >= 1f && upright) || elapsed >= _characterConfig.MaxGetUpTime)
            {
                EnterState(CharacterState.Normal, now);
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
            // Таз держащегося ведут руки держащих; в остальных состояниях тело целиком физическое.
            _rig.SetHipsKinematic(_state == CharacterState.Grabbed);
            // Ноги сталкиваются с миром только у лежащего: на ногах вес держит пружина таза.
            _rig.SetLegCollisions(_state == CharacterState.Ragdoll);
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
            Rigidbody hips = _rig.Hips;
            var snapshot = new BodySnapshot
            {
                Time = Time.fixedTimeAsDouble,
                Position = hips.position,
                Rotation = QuaternionPacking.Pack(hips.rotation),
                Velocity = hips.isKinematic ? Vector3.zero : hips.linearVelocity,
                Yaw = BodySnapshot.PackYaw(Facing),
                State = (byte)_state,
                Flags = (byte)_flags,
                Teleport = _sync.TeleportCounter,
            };
            _rig.CapturePose(ref snapshot.Pose);
            return snapshot;
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
            _emote = -1;
            bool ragdoll = forceRagdoll || CombatRules.ShouldRagdoll(shakeAfter, _combatConfig);
            EnterState(ragdoll ? CharacterState.Ragdoll : CharacterState.Stagger, now);
            _rig.SetVelocity(velocity);
            if (ragdoll)
            {
                Spin(velocity);
            }

            BroadcastHit(attacker, kind, point, speed, shakeAfter);
        }

        /// <summary>Хост: рывок тела вперёд в начале активного окна толчка, чтобы рука дотянулась.</summary>
        public void Lunge(Vector3 velocity)
        {
            if (IsServer && _state == CharacterState.Normal)
            {
                _rig.AddVelocity(velocity);
            }
        }

        /// <summary>Хост: эмоция началась (позу ведёт <see cref="ProceduralPose"/>, клиенты видят её в снимках).</summary>
        public void StartEmote(int index)
        {
            _emote = index;
            _emoteStart = Time.fixedTime;
        }

        public void OnEnteredKillZone()
        {
            if (IsServer)
            {
                Fall();
            }
        }

        /// <summary>Кость столкнулась с чем-то: брошенный игрок сбивает того, в кого врезался (раздел 4 GDD).</summary>
        public void OnPartCollision(Collision collision)
        {
            if (!IsServer || _thrownBy == null || Time.fixedTime > _thrownUntil)
            {
                return;
            }

            if (collision.relativeVelocity.magnitude < _combatConfig.ThrownHitMinSpeed)
            {
                return;
            }

            PlayerCharacter target = CharacterBodyPart.OwnerOf(collision.rigidbody);
            if (target == null || target == this || target == _thrownBy || !target.IsTargetable)
            {
                return;
            }

            PlayerCharacter thrower = _thrownBy;
            _thrownBy = null;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : target.Body.position;
            target.ReceiveHit(_combatConfig.PlayerThrowShake, _rig.Hips.linearVelocity, thrower, HitKind.ThrownPlayer, forceRagdoll: false, point);
        }

        /// <summary>Кисть коснулась чего-то: в активном окне толчка это попадание.</summary>
        public void OnHandContact(Collision collision)
        {
            if (!IsServer || _state != CharacterState.Normal || _eliminated || !_combat.IsSwinging)
            {
                return;
            }

            Rigidbody body = collision.rigidbody;
            if (body == null || _rig.Owns(body))
            {
                return;
            }

            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
            _combat.OnHandContact(body, point, Time.fixedTime);
        }

        private void Spin(Vector3 velocity)
        {
            // Кувырок через голову по направлению удара.
            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
            {
                _rig.Hips.angularVelocity = Vector3.Cross(Vector3.up, flat.normalized) * RagdollSpin;
            }
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
            _emote = -1;
            EnterState(CharacterState.Normal, now);
            _rig.ResetPose(_spawnPosition, _spawnRotation);
            _motor.SetFacing(_spawnRotation * Vector3.forward);
            _motor.ResetTransient();
            _shake.Value = 0f;
            _sync.MarkTeleported();
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
                _emote = -1;
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
                _rig.SetVelocity(velocity);
                Spin(velocity);
                _thrownBy = grabber;
                _thrownUntil = now + _combatConfig.ThrownLifetime;
                BroadcastHit(grabber, HitKind.ThrownPlayer, _rig.Hips.position, velocity.magnitude, shakeAfter);
            }
            else
            {
                EnterState(CharacterState.Stagger, now);
                _rig.SetVelocity(velocity);
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
            Rigidbody hips = _rig.Hips;
            hips.MovePosition(position);
            hips.MoveRotation(Quaternion.LookRotation(_holderRefs[0].Facing, Vector3.up) * _rig.RestRotation(RagdollBones.Hips));
        }

        /// <summary>Вырвался сам: держащие теряют его, он отскакивает и недолго неуязвим для захвата.</summary>
        private void BreakFree(float now)
        {
            Vector3 away = _holderRefs.Count > 0 ? -_holderRefs[0].Facing : -Facing;
            ReleaseFromHolders();
            EnterState(CharacterState.Stagger, now);
            _rig.SetVelocity((away + Vector3.up).normalized * BreakFreeSpeed);
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

        // ---------- Клиент-владелец: ввод и мгновенный отклик ----------

        private void OwnerTick(float now)
        {
            _networkInput.Submit(_localSource.Sample(), out PlayerInputFrame frame);

            // Мгновенный отклик у владельца: замах и эмоцию видно сразу, не дожидаясь снимков хоста.
            if (frame.WasPressed(PlayerButtons.Push) && (_flags & CharacterFlags.Holding) == 0 && now >= _localPushReady)
            {
                _localPushReady = now + _combatConfig.PushCooldown;
                _localSwingStart = now;
            }

            int emote = CharacterCombat.PressedEmote(frame);
            if (emote >= 0 && now >= _localEmoteReady)
            {
                _localEmoteReady = now + _combatConfig.EmoteCooldown;
                _localEmote = emote;
                _localEmoteStart = now;
            }
        }

        /// <summary>
        /// Поверх снимков хоста руки сразу принимают позу замаха или эмоции; когда хост подтвердит её
        /// в снимках (через пинг плюс задержку интерполяции), подмешивание гаснет.
        /// </summary>
        private void ApplyLocalOverlay(float now)
        {
            float swing = OverlayWeight(now - _localSwingStart);
            float emote = OverlayWeight(now - _localEmoteStart);
            float weight = Mathf.Max(swing, emote);
            if (weight <= 0f || _state != CharacterState.Normal)
            {
                return;
            }

            var input = new PoseInput
            {
                Grounded = true,
                State = CharacterState.Normal,
                Flags = _flags,
                Swing = swing > 0f ? 1f : 0f,
                Emote = emote > 0f ? _localEmote : -1,
                EmoteProgress = (now - _localEmoteStart) / _combatConfig.EmoteDuration,
            };
            _pose.Evaluate(input);
            Quaternion[] desired = _pose.Desired;
            _rig.BlendTowards(desired, RagdollBones.LeftUpperArm, weight);
            _rig.BlendTowards(desired, RagdollBones.LeftLowerArm, weight);
            _rig.BlendTowards(desired, RagdollBones.RightUpperArm, weight);
            _rig.BlendTowards(desired, RagdollBones.RightLowerArm, weight);
        }

        private static float OverlayWeight(float age)
        {
            if (age < 0f || age > OverlayHold + OverlayFade)
            {
                return 0f;
            }

            return age <= OverlayHold ? 1f : 1f - (age - OverlayHold) / OverlayFade;
        }

        // ---------- Клиент: интерполяция ----------

        private void FollowHost(float deltaTime)
        {
            if (!_sync.TrySample(deltaTime, out BodySnapshot from, out BodySnapshot to, out float t))
            {
                return;
            }

            Vector3 position = Vector3.LerpUnclamped(from.Position, to.Position, t);
            Quaternion rotation = Quaternion.Slerp(QuaternionPacking.Unpack(from.Rotation), QuaternionPacking.Unpack(to.Rotation), t);
            _rig.ApplyPose(position, rotation, from.Pose, to.Pose, t);

            _state = (CharacterState)from.State;
            _flags = (CharacterFlags)from.Flags;
            _motor.SetFacing(Vector3.Slerp(BodySnapshot.UnpackYaw(from.Yaw), BodySnapshot.UnpackYaw(to.Yaw), t));
        }

        // ---------- События для всех ----------

        private void BroadcastHit(PlayerCharacter attacker, HitKind kind, Vector3 point, float knockback, float shakeAfter)
        {
            ulong attackerId = attacker != null ? attacker.NetworkObjectId : CharacterHit.NoAttacker;
            HitRpc(attackerId, (byte)kind, point, knockback, shakeAfter);
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
                    // Стик поворачиваем на угол камеры до отправки: хост получает направление в осях стола.
                    _localSource = CameraRelativeInput.Wrap(players[i].Input, _moveFrame);
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
    }
}
