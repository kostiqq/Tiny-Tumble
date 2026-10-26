using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace TinyTumble.Gameplay.Cameras
{
    /// <summary>
    /// Одна общая камера на всех (раздел 11 GDD, без сплит-скрина): Cinemachine Target Group держит в кадре
    /// всех персонажей матча и центр стола, чтобы камера не наезжала вплотную, когда игроки сбились в кучу.
    /// Персонажи добавляются и убираются сами по мере спавна и выбывания.
    /// </summary>
    public sealed class MatchCamera : MonoBehaviour
    {
        [SerializeField] private CinemachineTargetGroup _group;
        [SerializeField, Min(0f)] private float _characterWeight = 1f;
        [Tooltip("Радиус вокруг персонажа, который должен попадать в кадр, м.")]
        [SerializeField, Min(0f)] private float _characterRadius = 1.2f;

        private MatchCharacters _characters;

        [Inject]
        public void Construct(MatchCharacters characters)
        {
            _characters = characters;
            _characters.Added += OnAdded;
            _characters.Removed += OnRemoved;
            for (int i = 0; i < _characters.All.Count; i++)
            {
                OnAdded(_characters.All[i]);
            }
        }

        private void OnDestroy()
        {
            if (_characters != null)
            {
                _characters.Added -= OnAdded;
                _characters.Removed -= OnRemoved;
            }
        }

        private void OnAdded(PlayerCharacter character)
        {
            if (_group != null && character != null && _group.FindMember(character.transform) < 0)
            {
                _group.AddMember(character.transform, _characterWeight, _characterRadius);
            }
        }

        private void OnRemoved(PlayerCharacter character)
        {
            if (_group != null && character != null)
            {
                _group.RemoveMember(character.transform);
            }
        }
    }
}
