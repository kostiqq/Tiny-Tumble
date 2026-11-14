using System;
using System.Collections.Generic;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Party;
using UnityEngine;
using VContainer.Unity;

namespace TinyTumble.Networking.Matchmaking
{
    /// <summary>
    /// Хост: проверка пинга после подключения. Оценка до подключения (ping location Steam) бывает неточной, поэтому
    /// первые <see cref="SessionConfig.PingProbeSeconds"/> секунд хост меряет RTT до каждого, кто пришёл по поиску, и если
    /// медиана выше <see cref="SessionConfig.MaxMeasuredRttMs"/>, отправляет его пати обратно к лидеру — тот ищет дальше.
    /// Друзей (вступивших в пати хоста или пришедших переездом без поиска) не проверяем: с ними играют и с пингом.
    /// RTT меряется парой RPC, поэтому в него входят и кадры обработки на обеих сторонах (до ~2 кадров).
    /// </summary>
    public sealed class MatchmadePingGuard : IInitializable, ITickable, IDisposable
    {
        private const string LogTag = "Matchmaking";
        private const float PingInterval = 0.3f;
        private const int MaxSamples = 16;

        private readonly INetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;
        private readonly List<Probe> _probes = new List<Probe>(4);

        public MatchmadePingGuard(INetworkSession session, SessionLobby lobby, SessionConfig config, IGameLog log)
        {
            _session = session;
            _lobby = lobby;
            _config = config;
            _log = log;
        }

        public void Initialize()
        {
            _session.ClientConnected += OnClientConnected;
            _session.ClientDisconnected += OnClientDisconnected;
            _lobby.PongReceived += OnPong;
        }

        public void Dispose()
        {
            _session.ClientConnected -= OnClientConnected;
            _session.ClientDisconnected -= OnClientDisconnected;
            _lobby.PongReceived -= OnPong;
        }

        public void Tick()
        {
            if (_probes.Count == 0)
            {
                return;
            }

            if (!_session.IsHost)
            {
                _probes.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            for (int i = _probes.Count - 1; i >= 0; i--)
            {
                Probe probe = _probes[i];
                if (!probe.Resolved)
                {
                    // Пати клиента лобби записывает в своём обработчике подключения, а он может идти после нашего.
                    if (!_lobby.TryGetPeerParty(probe.ClientId, out ulong partyId, out bool matchmade))
                    {
                        continue;
                    }

                    if (!matchmade)
                    {
                        _probes.RemoveAt(i);
                        continue;
                    }

                    probe.PartyId = partyId;
                    probe.Resolved = true;
                }

                if (now - probe.StartedAt >= _config.PingProbeSeconds)
                {
                    _probes.RemoveAt(i);
                    Judge(probe);
                    continue;
                }

                if (now >= probe.NextPingAt)
                {
                    probe.NextPingAt = now + PingInterval;
                    _lobby.SendPing(probe.ClientId);
                }
            }
        }

        private void Judge(Probe probe)
        {
            float maxRtt = _config.MaxMeasuredRttMs / 1000f;
            if (!MatchmakingRules.IsPingTooHigh(probe.Samples, probe.Count, maxRtt))
            {
                return;
            }

            _log.Info(LogTag, $"Client {probe.ClientId} has a ping above {_config.MaxMeasuredRttMs} ms: sending party {probe.PartyId:X} back.");
            _lobby.SendPartyBack(probe.PartyId, PartyMoveReason.HighPing);
            for (int i = _probes.Count - 1; i >= 0; i--)
            {
                if (_probes[i].PartyId == probe.PartyId)
                {
                    _probes.RemoveAt(i);
                }
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (_session.IsHost)
            {
                _probes.Add(new Probe(clientId, Time.realtimeSinceStartup));
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            for (int i = _probes.Count - 1; i >= 0; i--)
            {
                if (_probes[i].ClientId == clientId)
                {
                    _probes.RemoveAt(i);
                }
            }
        }

        private void OnPong(ulong clientId, float rtt)
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                Probe probe = _probes[i];
                if (probe.ClientId == clientId && probe.Resolved && probe.Count < MaxSamples)
                {
                    probe.Samples[probe.Count++] = rtt;
                }
            }
        }

        /// <summary>Замеры одного клиента. Класс, а не структура: замеры дописываются по ссылке из списка.</summary>
        private sealed class Probe
        {
            public readonly ulong ClientId;
            public readonly float StartedAt;
            public readonly float[] Samples = new float[MaxSamples];
            public ulong PartyId;
            public bool Resolved;
            public int Count;
            public float NextPingAt;

            public Probe(ulong clientId, float startedAt)
            {
                ClientId = clientId;
                StartedAt = startedAt;
                NextPingAt = startedAt;
            }
        }
    }
}
