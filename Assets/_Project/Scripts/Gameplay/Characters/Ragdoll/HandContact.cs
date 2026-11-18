using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Кисть: контакты передаются персонажу, толчок засчитывается по касанию кисти в активном окне.
    /// Нужны и касания, начавшиеся до окна (рука уже упёрлась в соперника), поэтому есть и Stay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandContact : MonoBehaviour
    {
        [SerializeField] private PlayerCharacter _owner;

        private void OnCollisionEnter(Collision collision)
        {
            _owner.OnHandContact(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            _owner.OnHandContact(collision);
        }

#if UNITY_EDITOR
        /// <summary>Только для генератора префаба.</summary>
        public void EditorSetup(PlayerCharacter owner)
        {
            _owner = owner;
        }
#endif
    }
}
