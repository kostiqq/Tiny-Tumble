using TinyTumble.Core.Configs;
using TinyTumble.Core.Messages;
using TinyTumble.Gameplay.Characters;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// Физический предмет на арене: его можно толкнуть, схватить, нести и бросить в соперника (+15% встряска).
    /// Общий компонент для всех наборов предметов по комнатам (масло, горошины, пешки…), отличаются только
    /// модель и параметры. Симулирует хост; клиенты видят его через NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GrabbableItem : NetworkBehaviour, IGrabbable, ICollisionBody
    {
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Collider _collider;
        [SerializeField] private NetworkTransform _networkTransform;
        [Tooltip("Тяжёлый предмет замедляет несущего.")]
        [SerializeField] private bool _isHeavy;
        [Tooltip("Расстояние от pivot до низа предмета, м.")]
        [SerializeField, Min(0f)] private float _holdBottomOffset = 0.2f;

        private readonly NetworkVariable<GrabLinks> _holder = new NetworkVariable<GrabLinks>();

        private CombatConfig _combatConfig;
        private MatchConfig _matchConfig;
        private CollisionIgnores _ignores;
        private Collider[] _colliders;

        // Только на хосте.
        private PlayerCharacter _heldBy;
        private PlayerCharacter _thrownBy;
        private float _thrownUntil;
        private Vector3 _respawnCenter;

        public Rigidbody Body => _rigidbody;

        public Collider[] Colliders => _colliders ??= new[] { _collider };

        public bool IsHeavy => _isHeavy;

        public float HoldBottomOffset => _holdBottomOffset;

        public bool IsHeld => _holder.Value.Count > 0;

        [Inject]
        public void Construct(CombatConfig combatConfig, MatchConfig matchConfig)
        {
            _combatConfig = combatConfig;
            _matchConfig = matchConfig;
        }

        public override void OnNetworkSpawn()
        {
            _ignores = new CollisionIgnores(Colliders);
            _respawnCenter = new Vector3(0f, transform.position.y, 0f);
            _holder.OnValueChanged += OnHolderChanged;
        }

        public override void OnNetworkDespawn()
        {
            _holder.OnValueChanged -= OnHolderChanged;
            if (IsServer && _heldBy != null)
            {
                _heldBy.Combat.ForgetHeld(this);
                _heldBy = null;
            }

            if (_ignores != null)
            {
                _ignores.RestoreAll();
            }
        }

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        public bool CanBeGrabbedBy(PlayerCharacter grabber, float now)
        {
            return IsServer && IsSpawned && _heldBy == null && grabber != null;
        }

        public void OnGrabbed(PlayerCharacter grabber, float now)
        {
            _heldBy = grabber;
            _thrownBy = null;
            _rigidbody.isKinematic = true;
            GrabLinks links = default;
            links.TryAdd(grabber.NetworkObjectId);
            _holder.Value = links;
        }

        public void OnReleased(PlayerCharacter grabber, Vector3 velocity, bool thrown, float now)
        {
            _heldBy = null;
            _holder.Value = default;
            _rigidbody.isKinematic = false;
            _rigidbody.linearVelocity = velocity;
            if (thrown)
            {
                _thrownBy = grabber;
                _thrownUntil = now + _combatConfig.ThrownLifetime;
                _rigidbody.angularVelocity = Random.insideUnitSphere * 6f;
            }
        }

        /// <summary>Хост: предмет попал под толчок.</summary>
        public void Push(Vector3 velocity)
        {
            if (!IsServer || _heldBy != null)
            {
                return;
            }

            _rigidbody.linearVelocity = velocity;
        }

        public void OnEnteredKillZone()
        {
            if (IsServer)
            {
                Respawn();
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }

            float now = Time.fixedTime;
            _ignores.Tick(now);
            if (!IsServer)
            {
                return;
            }

            if (_heldBy != null)
            {
                if (!_heldBy.IsTargetable || !ReferenceEquals(_heldBy.Combat.Held, this))
                {
                    OnReleased(_heldBy, Vector3.zero, thrown: false, now);
                }
                else
                {
                    _rigidbody.MovePosition(_heldBy.Combat.HoldPosition(_holdBottomOffset));
                    _rigidbody.MoveRotation(Quaternion.LookRotation(_heldBy.Facing, Vector3.up));
                }
            }

            if (_thrownBy != null && now > _thrownUntil)
            {
                _thrownBy = null;
            }

            if (_rigidbody.position.y < _matchConfig.EliminationHeight)
            {
                Respawn();
            }
        }

        private void OnCollisionEnter(Collision collision)
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
            if (target == null || target == _thrownBy || !target.IsTargetable)
            {
                return;
            }

            PlayerCharacter thrower = _thrownBy;
            _thrownBy = null;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : target.Body.position;
            target.ReceiveHit(_combatConfig.ItemThrowShake, _rigidbody.linearVelocity, thrower, HitKind.ThrownItem, forceRagdoll: false, point);
        }

        private void Respawn()
        {
            if (_heldBy != null)
            {
                _heldBy.Combat.ForgetHeld(this);
                _heldBy = null;
                _holder.Value = default;
            }

            _thrownBy = null;
            Vector2 offset = Random.insideUnitCircle * _matchConfig.ItemSpawnRadius;
            Vector3 position = _respawnCenter + new Vector3(offset.x, _matchConfig.SpawnHeight + 1f, offset.y);
            _rigidbody.isKinematic = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.position = position;
            transform.position = position;
            if (_networkTransform != null)
            {
                // Без телепорта клиенты интерполировали бы предмет из-под стола через всю арену.
                _networkTransform.Teleport(position, transform.rotation, transform.localScale);
            }
        }

        private void OnHolderChanged(GrabLinks previous, GrabLinks current)
        {
            _ignores.Apply(previous, current, NetworkManager, Time.fixedTime, _combatConfig.ThrowIgnoreTime);
        }
    }
}
