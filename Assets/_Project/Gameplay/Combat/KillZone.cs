using TinyTumble.Gameplay.Characters;
using UnityEngine;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// Kill-зона за краем арены (триггер). Персонаж в ней выпадает: выбывает или возрождается по правилам
    /// матча; предмет возвращается на стол. Решает хост, на клиентах триггер ничего не делает.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class KillZone : MonoBehaviour
    {
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null)
            {
                return;
            }

            if (body.TryGetComponent(out PlayerCharacter character))
            {
                character.OnEnteredKillZone();
            }
            else if (body.TryGetComponent(out GrabbableItem item))
            {
                item.OnEnteredKillZone();
            }
        }
    }
}
