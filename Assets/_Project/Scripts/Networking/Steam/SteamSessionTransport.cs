using System;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Session;
using TinyTumble.Networking.Transport;
using UnityEngine;

namespace TinyTumble.Networking.Steam
{
    /// <summary>
    /// Сессия через Steam: адрес хоста — его SteamID, связь через ретрансляцию Steam. Компонент
    /// <see cref="SteamNetworkTransport"/> добавляется на объект NetworkManager при первой настройке.
    /// </summary>
    public sealed class SteamSessionTransport : ISessionTransport
    {
        private readonly NetworkRuntime _runtime;
        private readonly SteamService _steam;
        private readonly Func<bool> _isSteamReady;
        private SteamNetworkTransport _component;

        public SteamSessionTransport(NetworkRuntime runtime, SteamService steam)
        {
            _runtime = runtime;
            _steam = steam;
            _isSteamReady = IsSteamReady;
        }

        public EndpointKind PreferredHostKind => EndpointKind.Steam;

        public bool Supports(EndpointKind kind)
        {
            return kind == EndpointKind.Steam && _steam.IsReady;
        }

        public bool TryConfigureHost(EndpointKind kind, ushort hostPort)
        {
            if (!Supports(kind))
            {
                return false;
            }

            Activate();
            return true;
        }

        public bool TryConfigureClient(in SessionEndpoint host)
        {
            // К самому себе через Steam не подключиться.
            if (!Supports(host.Kind) || host.Address == _steam.LocalSteamId)
            {
                return false;
            }

            Activate();
            _component.HostSteamId = host.Address;
            return true;
        }

        public void ConfigureTimeouts(float connectTimeout, float disconnectTimeout)
        {
            if (_component == null)
            {
                return;
            }

            _component.ConnectTimeoutMs = Mathf.RoundToInt(connectTimeout * 1000f);
            _component.DisconnectTimeoutMs = Mathf.RoundToInt(disconnectTimeout * 1000f);
        }

        public SessionEndpoint GetLocalSuccessor(ushort hostPort)
        {
            return SessionEndpoint.Steam(_steam.LocalSteamId);
        }

        /// <summary>
        /// SteamID клиент сообщает сам; верим ему, только если с этого SteamID к нам действительно подключены,
        /// иначе чужой клиент мог бы увести миграцию на посторонний адрес.
        /// </summary>
        public SessionEndpoint GetSuccessorEndpoint(ulong clientId, in SessionEndpoint announced)
        {
            if (announced.Kind != EndpointKind.Steam || _component == null || !_component.HasPeer(announced.Address))
            {
                return SessionEndpoint.None;
            }

            return announced;
        }

        private void Activate()
        {
            if (_component == null)
            {
                _component = _runtime.Manager.gameObject.AddComponent<SteamNetworkTransport>();
                _component.SetSteamReadyCheck(_isSteamReady);
            }

            _runtime.Manager.NetworkConfig.NetworkTransport = _component;
        }

        private bool IsSteamReady()
        {
            return _steam.IsReady;
        }
    }
}
