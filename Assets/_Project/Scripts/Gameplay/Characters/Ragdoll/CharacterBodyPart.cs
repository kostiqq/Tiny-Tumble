using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Кость персонажа: по ней толчок, захват и брошенный предмет находят, в кого попали.
    /// Столкновения кости передаются персонажу (брошенный игрок сбивает того, в кого врезался).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterBodyPart : MonoBehaviour
    {
        [SerializeField] private PlayerCharacter _owner;
        [SerializeField] private int _bone;

        public PlayerCharacter Owner => _owner;

        public int Bone => _bone;

        /// <summary>Персонаж, которому принадлежит тело, или null.</summary>
        public static PlayerCharacter OwnerOf(Rigidbody body)
        {
            return body != null && body.TryGetComponent(out CharacterBodyPart part) ? part._owner : null;
        }

        private void OnCollisionEnter(Collision collision)
        {
            _owner.OnPartCollision(collision);
        }

#if UNITY_EDITOR
        /// <summary>Только для генератора префаба.</summary>
        public void EditorSetup(PlayerCharacter owner, int bone)
        {
            _owner = owner;
            _bone = bone;
        }
#endif
    }
}
