using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>Сетевой объект из нескольких коллайдеров (персонаж — по коллайдеру на кость).</summary>
    public interface ICollisionBody
    {
        Collider[] Colliders { get; }
    }

    /// <summary>
    /// Отключённые столкновения объекта с теми, кто его держит. После броска столкновения возвращаются
    /// не сразу, а через паузу: иначе брошенное зацепилось бы за бросившего.
    /// Ведётся на каждой машине по <see cref="GrabLinks"/>, поэтому одинаково у хоста и клиентов.
    /// </summary>
    public sealed class CollisionIgnores
    {
        private struct Entry
        {
            public Collider[] Other;
            public float RestoreAt;
        }

        private readonly Collider[] _self;
        private readonly List<Entry> _entries = new List<Entry>(GrabLinks.Capacity);

        public CollisionIgnores(Collider[] self)
        {
            _self = self;
        }

        /// <summary>Сверяет держащих: новых игнорирует, ушедшим возвращает столкновения через <paramref name="restoreDelay"/>.</summary>
        public void Apply(in GrabLinks previous, in GrabLinks current, NetworkManager manager, float now, float restoreDelay)
        {
            for (int i = 0; i < GrabLinks.Capacity; i++)
            {
                ulong id = previous[i];
                if (id != GrabLinks.None && !current.Contains(id) && TryGetColliders(manager, id, out Collider[] colliders))
                {
                    RestoreLater(colliders, now + restoreDelay);
                }
            }

            for (int i = 0; i < GrabLinks.Capacity; i++)
            {
                ulong id = current[i];
                if (id != GrabLinks.None && TryGetColliders(manager, id, out Collider[] colliders))
                {
                    Ignore(colliders);
                }
            }
        }

        public void Tick(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry.RestoreAt > now)
                {
                    continue;
                }

                SetIgnored(entry.Other, false);
                _entries.RemoveAt(i);
            }
        }

        public void RestoreAll()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                SetIgnored(_entries[i].Other, false);
            }

            _entries.Clear();
        }

        private void Ignore(Collider[] other)
        {
            if (other == _self || _self == null)
            {
                return;
            }

            SetIgnored(other, true);
            int index = IndexOf(other);
            var entry = new Entry { Other = other, RestoreAt = float.PositiveInfinity };
            if (index >= 0)
            {
                _entries[index] = entry;
            }
            else
            {
                _entries.Add(entry);
            }
        }

        private void RestoreLater(Collider[] other, float at)
        {
            int index = IndexOf(other);
            if (index >= 0)
            {
                _entries[index] = new Entry { Other = other, RestoreAt = at };
            }
        }

        private void SetIgnored(Collider[] other, bool ignored)
        {
            if (other == null || _self == null)
            {
                return;
            }

            for (int a = 0; a < _self.Length; a++)
            {
                Collider self = _self[a];
                if (self == null)
                {
                    continue;
                }

                for (int b = 0; b < other.Length; b++)
                {
                    if (other[b] != null)
                    {
                        Physics.IgnoreCollision(self, other[b], ignored);
                    }
                }
            }
        }

        private int IndexOf(Collider[] other)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Other == other)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryGetColliders(NetworkManager manager, ulong networkObjectId, out Collider[] colliders)
        {
            colliders = null;
            if (manager == null || manager.SpawnManager == null
                || !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject)
                || networkObject == null
                || !networkObject.TryGetComponent(out ICollisionBody body))
            {
                return false;
            }

            colliders = body.Colliders;
            return colliders != null;
        }
    }
}
