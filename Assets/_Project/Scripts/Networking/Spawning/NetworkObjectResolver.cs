using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Networking.Spawning
{
    /// <summary>
    /// Через какой DI-контейнер инстанцировать сетевые объекты, которые создаёт сам NGO (на клиентах).
    /// Каждый скоуп кладёт свой контейнер на вершину на время жизни, поэтому персонаж матча получает
    /// зависимости MatchLifetimeScope, а объект сессии — корневые.
    /// </summary>
    public sealed class NetworkObjectResolver
    {
        private readonly List<IObjectResolver> _stack = new List<IObjectResolver>();

        public IObjectResolver Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public IDisposable Push(IObjectResolver resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            _stack.Add(resolver);
            return new Entry(this, resolver);
        }

        public GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            IObjectResolver resolver = Current;
            if (resolver == null)
            {
                throw new InvalidOperationException($"No DI scope to instantiate network prefab '{prefab.name}'.");
            }

            return resolver.Instantiate(prefab, position, rotation);
        }

        private void Remove(IObjectResolver resolver)
        {
            // Скоупы не обязаны умирать строго в обратном порядке: снимаем именно свой.
            int index = _stack.LastIndexOf(resolver);
            if (index >= 0)
            {
                _stack.RemoveAt(index);
            }
        }

        private sealed class Entry : IDisposable
        {
            private NetworkObjectResolver _owner;
            private readonly IObjectResolver _resolver;

            public Entry(NetworkObjectResolver owner, IObjectResolver resolver)
            {
                _owner = owner;
                _resolver = resolver;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner.Remove(_resolver);
                _owner = null;
            }
        }
    }

    /// <summary>
    /// Регистрируется в скоупе как entry point: пока скоуп жив, сетевые объекты инстанцируются его контейнером.
    /// </summary>
    public sealed class NetworkResolverScope : IInitializable, IDisposable
    {
        private readonly NetworkObjectResolver _networkResolver;
        private readonly IObjectResolver _scopeResolver;
        private IDisposable _entry;

        public NetworkResolverScope(NetworkObjectResolver networkResolver, IObjectResolver scopeResolver)
        {
            _networkResolver = networkResolver;
            _scopeResolver = scopeResolver;
        }

        public void Initialize()
        {
            _entry = _networkResolver.Push(_scopeResolver);
        }

        public void Dispose()
        {
            if (_entry != null)
            {
                _entry.Dispose();
                _entry = null;
            }
        }
    }
}
