using R3;
using TinyTumble.Core.Network;
using UnityEngine;
using VContainer;

namespace TinyTumble.UI.Session
{
    /// <summary>
    /// Показывает кнопки по роли: «Начать матч», «Завершить матч», «В лобби» нажимает только хост,
    /// «Готов» — только клиент. Вне сессии ведём себя как хост (соло).
    /// Компонент должен висеть на всегда активном объекте (корень UI сцены), а не на самих кнопках:
    /// Unity не вызывает OnDestroy у объекта, который ни разу не был активен, и скрытая с рождения кнопка
    /// не смогла бы отписаться.
    /// </summary>
    public sealed class SessionRoleVisibility : MonoBehaviour
    {
        [SerializeField] private GameObject[] _hostOnly = new GameObject[0];
        [SerializeField] private GameObject[] _clientOnly = new GameObject[0];

        [Inject]
        public void Construct(INetworkSession session)
        {
            session.Role.Subscribe(OnRoleChanged).AddTo(this);
        }

        private void OnRoleChanged(SessionRole role)
        {
            bool isClient = role == SessionRole.Client;
            SetActive(_hostOnly, !isClient);
            SetActive(_clientOnly, isClient);
        }

        private static void SetActive(GameObject[] targets, bool active)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].SetActive(active);
                }
            }
        }
    }
}
