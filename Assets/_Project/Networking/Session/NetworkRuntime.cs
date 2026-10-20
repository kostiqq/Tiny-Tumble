using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Владелец NetworkManager. NGO не разрешает вкладывать NetworkManager в другой объект,
    /// поэтому он не лежит в корневом префабе, а создаётся отдельным корневым объектом при первом обращении.
    /// </summary>
    public sealed class NetworkRuntime : IDisposable
    {
        private readonly NetworkManager _prefab;
        private NetworkManager _manager;
        private UnityTransport _transport;
        private bool _disposed;

        public NetworkRuntime(NetworkManager prefab)
        {
            _prefab = prefab;
        }

        public NetworkManager Manager
        {
            get
            {
                EnsureCreated();
                return _manager;
            }
        }

        public UnityTransport Transport
        {
            get
            {
                EnsureCreated();
                return _transport;
            }
        }

        /// <summary>Есть запущенная сессия (хост или подключённый/подключающийся клиент).</summary>
        public bool IsRunning => _manager != null && _manager.IsListening;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_manager == null)
            {
                return;
            }

            if (_manager.IsListening && !_manager.ShutdownInProgress)
            {
                _manager.Shutdown(discardMessageQueue: true);
            }

            Object.Destroy(_manager.gameObject);
            _manager = null;
        }

        private void EnsureCreated()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NetworkRuntime));
            }

            if (_manager != null)
            {
                return;
            }

            if (_prefab == null)
            {
                throw new InvalidOperationException("NetworkManager prefab is not assigned in RootLifetimeScope.");
            }

            _manager = Object.Instantiate(_prefab);
            _manager.name = _prefab.name;
            _transport = _manager.GetComponent<UnityTransport>();
            if (_transport == null)
            {
                throw new InvalidOperationException($"NetworkManager prefab '{_prefab.name}' has no {nameof(UnityTransport)}.");
            }

            _manager.NetworkConfig.NetworkTransport = _transport;
        }
    }
}
