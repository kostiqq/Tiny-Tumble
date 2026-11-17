using System.Collections.Generic;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Cameras
{
    /// <summary>
    /// Камера матча на этой машине.
    /// Один локальный игрок: камера за его персонажем, вращение мышью или правым стиком (<see cref="OrbitFollow"/>),
    /// после выбывания — наблюдение за оставшимися (Q/E, LB/RB).
    /// Несколько локальных игроков: общая камера — Cinemachine Target Group держит в кадре всех и центр стола.
    /// Между режимами переключает CinemachineBrain с плавным переходом. Все числа — в <see cref="CameraConfig"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)] // До LateUpdate у CinemachineBrain: он забирает позу камеры в том же кадре.
    public sealed class MatchCamera : MonoBehaviour
    {
        [Header("Общая камера")]
        [SerializeField] private CinemachineTargetGroup _group;
        [SerializeField] private Transform _tableAnchor;
        [SerializeField] private CinemachineCamera _sharedCamera;
        [SerializeField] private CinemachineFollow _sharedFollow;
        [SerializeField] private CinemachineGroupFraming _sharedFraming;

        [Header("Камера за игроком")]
        [Tooltip("CinemachineCamera без компонентов движения: позу ей задаёт этот скрипт.")]
        [SerializeField] private CinemachineCamera _followCamera;

        [Header("Основная камера")]
        [SerializeField] private CinemachineBrain _brain;

        private CameraConfig _config;
        private MatchCharacters _characters;
        private ILocalPlayers _localPlayers;
        private CameraMoveFrame _moveFrame;
        private OrbitFollow _orbit;

        private PlayerCharacter _target;
        private PlayerCharacter _spectated;
        private bool _ownSeen;
        private float _ownLostAt = -1f;
        private Vector3 _lastAnchor;
        private bool _hasAnchor;
        private bool _followMode;
        private bool _modeApplied;
        private bool _cursorReleased;
        private bool _cursorLockedByUs;

        [Inject]
        public void Construct(CameraConfig config, MatchCharacters characters, ILocalPlayers localPlayers, CameraMoveFrame moveFrame)
        {
            _config = config;
            _characters = characters;
            _localPlayers = localPlayers;
            _moveFrame = moveFrame;
            _orbit = new OrbitFollow(config);

            _characters.Added += OnAdded;
            _characters.Removed += OnRemoved;
            for (int i = 0; i < _characters.All.Count; i++)
            {
                OnAdded(_characters.All[i]);
            }

            ApplyConfig();
#if UNITY_EDITOR
            _config.Changed += ApplyConfig;
#endif
        }

        private void OnDestroy()
        {
            if (_characters != null)
            {
                _characters.Added -= OnAdded;
                _characters.Removed -= OnRemoved;
            }

#if UNITY_EDITOR
            if (_config != null)
            {
                _config.Changed -= ApplyConfig;
            }
#endif

            ReleaseCursor();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Вернувшись в окно, курсор захватываем заново (если игрок не отпускал его сам).
            if (!hasFocus)
            {
                ReleaseCursor();
            }
        }

        private void LateUpdate()
        {
            if (_config == null)
            {
                return;
            }

            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            bool followMode = _config.FollowLocalPlayer && players.Count <= 1;
            if (followMode != _followMode || !_modeApplied)
            {
                SetMode(followMode);
            }

            if (!followMode)
            {
                // Общая камера смотрит вдоль смещения: «вперёд» для стика — от камеры к столу.
                Vector3 offset = _config.SharedOffset;
                _moveFrame.SetYaw(Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg);
                return;
            }

            IPlayerLookInput look = players.Count == 1 ? players[0].Look : null;
            bool mouseEnabled = UpdateCursor(look);
            float deltaTime = Time.deltaTime;

            UpdateTarget(look);
            if (_target != null)
            {
                _lastAnchor = Anchor(_target);
                _hasAnchor = true;
            }

            if (!_hasAnchor)
            {
                return;
            }

            // Без цели (пауза перед наблюдением) камера стоит на месте падения, но крутить её можно.
            _orbit.ApplyLook(look, mouseEnabled, deltaTime);
            _orbit.Tick(_lastAnchor, _target != null ? _target.Facing : Vector3.forward, deltaTime);
            _followCamera.transform.SetPositionAndRotation(_orbit.Position, _orbit.Rotation);
            _moveFrame.SetYaw(_orbit.TargetYaw);
        }

        // ---------- Цель ----------

        private void UpdateTarget(IPlayerLookInput look)
        {
            int steps = look != null ? look.ConsumeSpectateSteps() : 0;
            PlayerCharacter own = FindOwn();
            if (own != null)
            {
                if (!_ownSeen || _target != own)
                {
                    // Свой персонаж появился (начало матча или вернулся после наблюдения): сразу за спину.
                    _orbit.Reset(Anchor(own), own.Facing);
                }

                _ownSeen = true;
                _ownLostAt = -1f;
                _spectated = null;
                _target = own;
                return;
            }

            // Своего персонажа нет: выбыл или ещё не появился (игрок досматривает матч после переподключения).
            if (_ownSeen)
            {
                if (_ownLostAt < 0f)
                {
                    _ownLostAt = Time.time;
                }

                if (Time.time - _ownLostAt < _config.SpectateDelay)
                {
                    _target = null;
                    return;
                }
            }

            if (!IsValidSpectateTarget(_spectated))
            {
                _spectated = Step(_spectated, 1);
            }
            else if (steps != 0)
            {
                _spectated = Step(_spectated, steps > 0 ? 1 : -1);
            }

            if (_spectated != _target && _spectated != null)
            {
                if (_hasAnchor)
                {
                    _orbit.Retarget(Anchor(_spectated));
                }
                else
                {
                    _orbit.Reset(Anchor(_spectated), _spectated.Facing);
                }
            }

            _target = _spectated;
        }

        /// <summary>Персонаж единственного локального игрока: наш, не бот.</summary>
        private PlayerCharacter FindOwn()
        {
            IReadOnlyList<PlayerCharacter> all = _characters.All;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerCharacter character = all[i];
                if (character != null && character.IsSpawned && character.IsOwner && !character.IsBot)
                {
                    return character;
                }
            }

            return null;
        }

        private bool IsValidSpectateTarget(PlayerCharacter character)
        {
            if (character == null || !character.IsSpawned)
            {
                return false;
            }

            IReadOnlyList<PlayerCharacter> all = _characters.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == character)
                {
                    return IsSpectateCandidate(character);
                }
            }

            return false;
        }

        private bool IsSpectateCandidate(PlayerCharacter character)
        {
            if (character == null || !character.IsSpawned)
            {
                return false;
            }

            return _config.SpectateBots || !character.IsBot || !HasHumanCandidates();
        }

        private bool HasHumanCandidates()
        {
            IReadOnlyList<PlayerCharacter> all = _characters.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].IsSpawned && !all[i].IsBot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Следующий подходящий персонаж по кругу от <paramref name="from"/>; null — наблюдать не за кем.</summary>
        private PlayerCharacter Step(PlayerCharacter from, int direction)
        {
            IReadOnlyList<PlayerCharacter> all = _characters.All;
            int count = all.Count;
            if (count == 0)
            {
                return null;
            }

            int start = -1;
            for (int i = 0; i < count; i++)
            {
                if (all[i] == from)
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                start = direction > 0 ? -1 : 0;
            }

            for (int offset = 1; offset <= count; offset++)
            {
                int index = ((start + direction * offset) % count + count) % count;
                if (IsSpectateCandidate(all[index]))
                {
                    return all[index];
                }
            }

            return null;
        }

        private static Vector3 Anchor(PlayerCharacter character)
        {
            // Центр тела, а не опора: в ragdoll персонаж крутится вокруг центра, и камеру не мотает.
            Transform root = character.transform;
            return root.position + root.rotation * (Vector3.up * (character.Height * 0.5f));
        }

        // ---------- Режим и курсор ----------

        private void SetMode(bool followMode)
        {
            _followMode = followMode;
            _modeApplied = true;
            if (_followCamera != null)
            {
                _followCamera.enabled = followMode;
            }

            if (_sharedCamera != null)
            {
                _sharedCamera.enabled = !followMode;
            }

            if (!followMode)
            {
                ReleaseCursor();
            }
        }

        /// <summary>Захват курсора в режиме за игроком. Возвращает, вращает ли сейчас камеру мышь.</summary>
        private bool UpdateCursor(IPlayerLookInput look)
        {
            if (look != null && look.ConsumeCursorToggle())
            {
                _cursorReleased = !_cursorReleased;
            }

            if (!_config.LockCursor)
            {
                ReleaseCursor();
                return true;
            }

            bool wantLocked = look != null && !_cursorReleased && Application.isFocused;
            if (wantLocked)
            {
                if (!_cursorLockedByUs || Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    _cursorLockedByUs = true;
                }

                return true;
            }

            ReleaseCursor();
            return false;
        }

        private void ReleaseCursor()
        {
            if (!_cursorLockedByUs)
            {
                return;
            }

            _cursorLockedByUs = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ---------- Конфиг и общая группа ----------

        private void ApplyConfig()
        {
            if (_config == null)
            {
                return;
            }

            if (_followCamera != null)
            {
                LensSettings lens = _followCamera.Lens;
                lens.FieldOfView = _config.FollowFieldOfView;
                lens.NearClipPlane = _config.NearClipPlane;
                _followCamera.Lens = lens;
            }

            if (_sharedCamera != null)
            {
                LensSettings lens = _sharedCamera.Lens;
                lens.FieldOfView = _config.SharedFieldOfView;
                lens.NearClipPlane = _config.NearClipPlane;
                _sharedCamera.Lens = lens;
            }

            if (_sharedFollow != null)
            {
                _sharedFollow.FollowOffset = _config.SharedOffset;
            }

            if (_sharedFraming != null)
            {
                _sharedFraming.FramingSize = _config.SharedFramingSize;
                _sharedFraming.DollyRange = _config.SharedDollyRange;
                _sharedFraming.Damping = _config.SharedDamping;
            }

            if (_brain != null)
            {
                _brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, _config.BlendTime);
            }

            if (_group != null)
            {
                List<CinemachineTargetGroup.Target> targets = _group.Targets;
                for (int i = 0; i < targets.Count; i++)
                {
                    CinemachineTargetGroup.Target target = targets[i];
                    bool isTable = _tableAnchor != null && target.Object == _tableAnchor;
                    target.Weight = isTable ? _config.SharedTableWeight : _config.SharedCharacterWeight;
                    target.Radius = isTable ? _config.SharedTableRadius : _config.SharedCharacterRadius;
                }
            }
        }

        private void OnAdded(PlayerCharacter character)
        {
            if (_group != null && character != null && _group.FindMember(character.transform) < 0)
            {
                _group.AddMember(character.transform, _config.SharedCharacterWeight, _config.SharedCharacterRadius);
            }
        }

        private void OnRemoved(PlayerCharacter character)
        {
            if (_group != null && character != null)
            {
                _group.RemoveMember(character.transform);
            }

            if (character == _target)
            {
                _target = null;
            }

            if (character == _spectated)
            {
                _spectated = null;
            }
        }
    }
}
