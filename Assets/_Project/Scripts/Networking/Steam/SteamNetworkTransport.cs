using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Steam
{
    /// <summary>
    /// Транспорт NGO на SteamNetworkingSockets: P2P по SteamID через ретрансляцию Steam (SDR), без проброса
    /// портов. Опросный: события соединений приходят колбэком Steam и ждут в очереди, данные читаются в PollEvent.
    /// Транспортный id клиента на хосте — хэндл его соединения (никогда не 0), у клиента хост — <see cref="ServerClientId"/>.
    /// Компонент добавляется на объект NetworkManager в рантайме (<see cref="SteamSessionTransport"/>).
    /// </summary>
    public sealed class SteamNetworkTransport : NetworkTransport
    {
        private const int VirtualPort = 0;
        private const int MessageBatch = 64;
        private const int InitialBufferSize = 2048;

        // Смещения полей в нативном SteamNetworkingMessage_t: читаем их напрямую, чтобы не создавать мусор
        // через Marshal.PtrToStructure на каждое сообщение.
        private static readonly int DataOffset = Marshal.OffsetOf(typeof(SteamNetworkingMessage_t), nameof(SteamNetworkingMessage_t.m_pData)).ToInt32();
        private static readonly int SizeOffset = Marshal.OffsetOf(typeof(SteamNetworkingMessage_t), nameof(SteamNetworkingMessage_t.m_cbSize)).ToInt32();
        private static readonly int ConnectionOffset = Marshal.OffsetOf(typeof(SteamNetworkingMessage_t), nameof(SteamNetworkingMessage_t.m_conn)).ToInt32();

        private readonly Queue<PendingEvent> _events = new Queue<PendingEvent>(16);
        private readonly Dictionary<uint, ulong> _peers = new Dictionary<uint, ulong>(8);
        private readonly List<uint> _peerBuffer = new List<uint>(8);
        private readonly IntPtr[] _messages = new IntPtr[MessageBatch];
        private readonly SteamNetworkingConfigValue_t[] _options = new SteamNetworkingConfigValue_t[2];

        private Callback<SteamNetConnectionStatusChangedCallback_t> _statusChanged;
        private Func<bool> _isSteamReady;
        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetPollGroup _pollGroup = HSteamNetPollGroup.Invalid;
        private HSteamNetConnection _serverConnection = HSteamNetConnection.Invalid;
        private bool _isServer;
        private bool _serverConnectReported;
        private int _messageCount;
        private int _messageIndex;
        private byte[] _receiveBuffer = new byte[InitialBufferSize];
        private IntPtr _sendBuffer;
        private int _sendCapacity;
        private SteamNetConnectionRealTimeStatus_t _status;
        private SteamNetConnectionRealTimeLaneStatus_t _laneStatus;

        public override ulong ServerClientId => 0;

        /// <summary>К кому подключаться клиенту.</summary>
        public ulong HostSteamId { get; set; }

        public int ConnectTimeoutMs { get; set; } = 10000;

        public int DisconnectTimeoutMs { get; set; } = 10000;

        /// <summary>Хост: этот SteamID сейчас подключён к нам (проверка адреса преемника).</summary>
        public bool HasPeer(ulong steamId)
        {
            foreach (KeyValuePair<uint, ulong> pair in _peers)
            {
                if (pair.Value == steamId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Без готового Steam транспорт ничего не трогает: нативные вызовы после SteamAPI.Shutdown упадут.</summary>
        public void SetSteamReadyCheck(Func<bool> isSteamReady)
        {
            _isSteamReady = isSteamReady;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
        }

        public override bool StartServer()
        {
            if (!IsSteamReady())
            {
                return false;
            }

            ResetState();
            _isServer = true;
            Subscribe();
            int optionCount = FillOptions();
            _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, optionCount, _options);
            _pollGroup = SteamNetworkingSockets.CreatePollGroup();
            if (_listenSocket == HSteamListenSocket.Invalid || _pollGroup == HSteamNetPollGroup.Invalid)
            {
                Shutdown();
                return false;
            }

            return true;
        }

        public override bool StartClient()
        {
            if (!IsSteamReady() || HostSteamId == 0)
            {
                return false;
            }

            ResetState();
            _isServer = false;
            Subscribe();
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(HostSteamId));
            int optionCount = FillOptions();
            _serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, optionCount, _options);
            if (_serverConnection == HSteamNetConnection.Invalid)
            {
                Shutdown();
                return false;
            }

            return true;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (!IsSteamReady() || payload.Count == 0)
            {
                return;
            }

            HSteamNetConnection connection = ToConnection(clientId);
            if (connection == HSteamNetConnection.Invalid)
            {
                return;
            }

            EnsureSendCapacity(payload.Count);
            Marshal.Copy(payload.Array, payload.Offset, _sendBuffer, payload.Count);
            int flags = IsReliable(networkDelivery)
                ? Constants.k_nSteamNetworkingSend_ReliableNoNagle
                : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
            EResult result = SteamNetworkingSockets.SendMessageToConnection(connection, _sendBuffer, (uint)payload.Count, flags, out _);
            if (result != EResult.k_EResultOK && result != EResult.k_EResultNoConnection)
            {
                Debug.LogWarning($"[Steam] Send of {payload.Count} bytes to {clientId} failed: {result}.");
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            receiveTime = Time.realtimeSinceStartup;
            payload = default;

            if (_events.Count > 0)
            {
                PendingEvent pending = _events.Dequeue();
                clientId = pending.ClientId;
                return pending.Type;
            }

            if (_messageIndex >= _messageCount)
            {
                FetchMessages();
            }

            while (_messageIndex < _messageCount)
            {
                IntPtr message = _messages[_messageIndex];
                uint connection = (uint)Marshal.ReadInt32(message, ConnectionOffset);

                // Колбэк «соединено» мог ещё не прийти, а данные уже здесь: сначала сообщаем о подключении,
                // иначе NGO выбросит первое сообщение (запрос на подключение) от «неизвестного» клиента.
                if (TryReportConnectBeforeData(connection, out clientId))
                {
                    return NetworkEvent.Connect;
                }

                _messageIndex++;
                bool known = _isServer ? _peers.ContainsKey(connection) : connection == _serverConnection.m_HSteamNetConnection;
                int size = Marshal.ReadInt32(message, SizeOffset);
                if (!known || size <= 0)
                {
                    SteamNetworkingMessage_t.Release(message);
                    continue;
                }

                if (_receiveBuffer.Length < size)
                {
                    _receiveBuffer = new byte[Mathf.NextPowerOfTwo(size)];
                }

                Marshal.Copy(Marshal.ReadIntPtr(message, DataOffset), _receiveBuffer, 0, size);
                SteamNetworkingMessage_t.Release(message);

                // NGO разбирает данные сразу, до следующего PollEvent, поэтому один буфер на всех безопасен.
                clientId = _isServer ? connection : ServerClientId;
                payload = new ArraySegment<byte>(_receiveBuffer, 0, size);
                return NetworkEvent.Data;
            }

            clientId = 0;
            return NetworkEvent.Nothing;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            HSteamNetConnection connection = ToConnection(clientId);
            if (_isServer && connection != HSteamNetConnection.Invalid)
            {
                _peers.Remove(connection.m_HSteamNetConnection);
                // Linger: NGO только что отправил причину отключения, пусть она дойдёт.
                Close(connection, true);
            }
        }

        public override void DisconnectLocalClient()
        {
            if (!_isServer && _serverConnection != HSteamNetConnection.Invalid)
            {
                Close(_serverConnection, true);
                _serverConnection = HSteamNetConnection.Invalid;
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            HSteamNetConnection connection = ToConnection(clientId);
            if (!IsSteamReady() || connection == HSteamNetConnection.Invalid)
            {
                return 0;
            }

            EResult result = SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref _status, 0, ref _laneStatus);
            return result == EResult.k_EResultOK && _status.m_nPing > 0 ? (ulong)_status.m_nPing : 0;
        }

        public override void Shutdown()
        {
            if (IsSteamReady())
            {
                ReleasePendingMessages();
                _peerBuffer.Clear();
                foreach (KeyValuePair<uint, ulong> pair in _peers)
                {
                    _peerBuffer.Add(pair.Key);
                }

                for (int i = 0; i < _peerBuffer.Count; i++)
                {
                    Close(new HSteamNetConnection(_peerBuffer[i]), true);
                }

                if (_serverConnection != HSteamNetConnection.Invalid)
                {
                    Close(_serverConnection, true);
                }

                if (_listenSocket != HSteamListenSocket.Invalid)
                {
                    SteamNetworkingSockets.CloseListenSocket(_listenSocket);
                }

                if (_pollGroup != HSteamNetPollGroup.Invalid)
                {
                    SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
                }
            }

            Unsubscribe();
            ResetState();
        }

        protected override void OnEarlyUpdate()
        {
            // Колбэки соединений до опроса NGO: так подключение и обрыв видны в том же кадре.
            if (IsSteamReady())
            {
                SteamAPI.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            if (_sendBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sendBuffer);
                _sendBuffer = IntPtr.Zero;
                _sendCapacity = 0;
            }
        }

        private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            HSteamNetConnection connection = data.m_hConn;
            ESteamNetworkingConnectionState state = data.m_info.m_eState;

            if (_isServer)
            {
                if (data.m_info.m_hListenSocket != _listenSocket)
                {
                    return;
                }

                switch (state)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                        // Допуск решает NGO по запросу на подключение, здесь принимаем всех.
                        if (SteamNetworkingSockets.AcceptConnection(connection) != EResult.k_EResultOK
                            || !SteamNetworkingSockets.SetConnectionPollGroup(connection, _pollGroup))
                        {
                            Close(connection, false);
                        }

                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                        ReportServerConnect(connection.m_HSteamNetConnection, data.m_info.m_identityRemote.GetSteamID().m_SteamID);
                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                        if (_peers.Remove(connection.m_HSteamNetConnection))
                        {
                            SetEndReason(state, data.m_info.m_szEndDebug);
                            _events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, connection.m_HSteamNetConnection));
                        }

                        Close(connection, false);
                        break;
                }

                return;
            }

            if (connection != _serverConnection)
            {
                return;
            }

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    ReportClientConnect();
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    // И обрыв, и неудачное подключение для NGO-клиента — Disconnect от сервера.
                    SetEndReason(state, data.m_info.m_szEndDebug);
                    _events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, ServerClientId));
                    Close(connection, false);
                    _serverConnection = HSteamNetConnection.Invalid;
                    break;
            }
        }

        private bool TryReportConnectBeforeData(uint connection, out ulong clientId)
        {
            clientId = 0;
            if (_isServer)
            {
                if (_peers.ContainsKey(connection))
                {
                    return false;
                }

                var handle = new HSteamNetConnection(connection);
                if (!SteamNetworkingSockets.GetConnectionInfo(handle, out SteamNetConnectionInfo_t info)
                    || info.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    return false;
                }

                ReportServerConnect(connection, info.m_identityRemote.GetSteamID().m_SteamID);
            }
            else
            {
                if (_serverConnectReported || connection != _serverConnection.m_HSteamNetConnection)
                {
                    return false;
                }

                ReportClientConnect();
            }

            PendingEvent pending = _events.Dequeue();
            clientId = pending.ClientId;
            return true;
        }

        private void ReportServerConnect(uint connection, ulong steamId)
        {
            if (_peers.ContainsKey(connection))
            {
                return;
            }

            _peers[connection] = steamId;
            _events.Enqueue(new PendingEvent(NetworkEvent.Connect, connection));
        }

        private void ReportClientConnect()
        {
            if (_serverConnectReported)
            {
                return;
            }

            _serverConnectReported = true;
            _events.Enqueue(new PendingEvent(NetworkEvent.Connect, ServerClientId));
        }

        private void FetchMessages()
        {
            _messageIndex = 0;
            _messageCount = 0;
            if (!IsSteamReady())
            {
                return;
            }

            if (_isServer)
            {
                if (_pollGroup != HSteamNetPollGroup.Invalid)
                {
                    _messageCount = Mathf.Max(0, SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_pollGroup, _messages, MessageBatch));
                }
            }
            else if (_serverConnection != HSteamNetConnection.Invalid)
            {
                _messageCount = Mathf.Max(0, SteamNetworkingSockets.ReceiveMessagesOnConnection(_serverConnection, _messages, MessageBatch));
            }
        }

        private void ReleasePendingMessages()
        {
            for (int i = _messageIndex; i < _messageCount; i++)
            {
                SteamNetworkingMessage_t.Release(_messages[i]);
            }

            _messageIndex = 0;
            _messageCount = 0;
        }

        private HSteamNetConnection ToConnection(ulong clientId)
        {
            if (!_isServer)
            {
                return clientId == ServerClientId ? _serverConnection : HSteamNetConnection.Invalid;
            }

            return clientId != 0 && clientId <= uint.MaxValue ? new HSteamNetConnection((uint)clientId) : HSteamNetConnection.Invalid;
        }

        private int FillOptions()
        {
            _options[0].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutInitial;
            _options[0].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            _options[0].m_val.m_int32 = ConnectTimeoutMs;
            _options[1].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected;
            _options[1].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            _options[1].m_val.m_int32 = DisconnectTimeoutMs;
            return _options.Length;
        }

        private void EnsureSendCapacity(int size)
        {
            if (_sendCapacity >= size)
            {
                return;
            }

            if (_sendBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sendBuffer);
            }

            _sendCapacity = Mathf.Max(InitialBufferSize, Mathf.NextPowerOfTwo(size));
            _sendBuffer = Marshal.AllocHGlobal(_sendCapacity);
        }

        private void SetEndReason(ESteamNetworkingConnectionState state, string debug)
        {
            DisconnectEvents reason = state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                ? DisconnectEvents.ClosedByRemote
                : DisconnectEvents.ProtocolTimeout;
            SetDisconnectEvent(reason, debug);
        }

        private void Close(HSteamNetConnection connection, bool linger)
        {
            if (IsSteamReady())
            {
                SteamNetworkingSockets.CloseConnection(connection, 0, null, linger);
            }
        }

        private void Subscribe()
        {
            if (_statusChanged == null)
            {
                _statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
            }
        }

        private void Unsubscribe()
        {
            if (_statusChanged != null)
            {
                _statusChanged.Dispose();
                _statusChanged = null;
            }
        }

        private void ResetState()
        {
            _events.Clear();
            _peers.Clear();
            _listenSocket = HSteamListenSocket.Invalid;
            _pollGroup = HSteamNetPollGroup.Invalid;
            _serverConnection = HSteamNetConnection.Invalid;
            _serverConnectReported = false;
            _messageIndex = 0;
            _messageCount = 0;
        }

        private bool IsSteamReady()
        {
            return _isSteamReady != null && _isSteamReady();
        }

        private static bool IsReliable(NetworkDelivery delivery)
        {
            return delivery == NetworkDelivery.Reliable || delivery == NetworkDelivery.ReliableSequenced
                || delivery == NetworkDelivery.ReliableFragmentedSequenced;
        }

        private readonly struct PendingEvent
        {
            public readonly NetworkEvent Type;
            public readonly ulong ClientId;

            public PendingEvent(NetworkEvent type, ulong clientId)
            {
                Type = type;
                ClientId = clientId;
            }
        }
    }
}
