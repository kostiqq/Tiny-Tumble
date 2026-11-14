using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Core.Scenes;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Networking.Lobby;
using Unity.Netcode;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Старт матча: держит экран загрузки, пока грузится арена. Хост ждёт, пока арену загрузят все клиенты,
    /// расставляет игроков по слотам и ботов и раскладывает предметы для броска; клиент сообщает хосту,
    /// что готов принять объекты матча.
    /// </summary>
    public sealed class MatchEntryPoint : IInitializable, IAsyncStartable, IDisposable
    {
        private const string LogTag = "Match";

        private readonly MatchConfig _config;
        private readonly MatchSetup _setup;
        private readonly MatchArena _arena;
        private readonly MatchRoster _roster;
        private readonly MatchCharacters _characters;
        private readonly MatchNetworkObjects _objects;
        private readonly IAssetProvider _assets;
        private readonly IObjectResolver _resolver;
        private readonly ILoadingScreen _loadingScreen;
        private readonly ILocalPlayers _localPlayers;
        private readonly IBotInputFactory _botInputFactory;
        private readonly INetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly IGameLog _log;
        private readonly NetworkObject.VisibilityDelegate _visibility;
        private IDisposable _loadingHold;
        private bool _characterLoaded;
        private bool _itemLoaded;
        private bool _subscribedToLoads;

        public MatchEntryPoint(
            MatchConfig config,
            MatchSetup setup,
            MatchArena arena,
            MatchRoster roster,
            MatchCharacters characters,
            MatchNetworkObjects objects,
            IAssetProvider assets,
            IObjectResolver resolver,
            ILoadingScreen loadingScreen,
            ILocalPlayers localPlayers,
            IBotInputFactory botInputFactory,
            INetworkSession session,
            SessionLobby lobby,
            IGameLog log)
        {
            _config = config;
            _setup = setup;
            _arena = arena;
            _roster = roster;
            _characters = characters;
            _objects = objects;
            _assets = assets;
            _resolver = resolver;
            _loadingScreen = loadingScreen;
            _localPlayers = localPlayers;
            _botInputFactory = botInputFactory;
            _session = session;
            _lobby = lobby;
            _log = log;
            _visibility = _lobby.HasLoadedCurrentState;
        }

        public IReadOnlyList<PlayerCharacter> Characters => _characters.All;

        public bool IsReady { get; private set; }

        public void Initialize()
        {
            // Initialize вызывается при сборке скоупа, то есть ещё во время загрузки сцены Match:
            // холд берётся до того, как загрузчик сцен отпустит свой, и экран не мигает.
            _loadingHold = _loadingScreen.Hold();
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                await _arena.LoadAsync(_config.ArenaKey, cancellation);

                if (_session.Role.CurrentValue == SessionRole.None)
                {
                    // Матч запущен прямо из сцены Match: играем соло, но тоже через хост.
                    _localPlayers.TryJoinDefault();
                    SessionStartResult result = await _session.StartHostAsync(cancellation);
                    if (!result.Success)
                    {
                        _log.Error(LogTag, $"Cannot start a solo session: {result.Reason}.");
                        return;
                    }
                }

                if (_session.IsHost)
                {
                    await StartAsHostAsync(cancellation);
                }
                else
                {
                    _lobby.ReportLoaded();
                    _log.Info(LogTag, "Arena loaded, waiting for the host to spawn players.");
                }

                IsReady = true;
            }
            catch (OperationCanceledException)
            {
                // Из матча ушли до конца загрузки: скоуп уже уничтожается, ничего не делаем.
            }
            finally
            {
                ReleaseLoadingHold();
            }
        }

        public void Dispose()
        {
            if (_subscribedToLoads)
            {
                _subscribedToLoads = false;
                _lobby.ClientLoadedCurrentState -= OnClientLoaded;
            }

            ReleaseLoadingHold();
            if (_characterLoaded)
            {
                _characterLoaded = false;
                _assets.Release(_config.CharacterKey);
            }

            if (_itemLoaded)
            {
                _itemLoaded = false;
                _assets.Release(_config.ItemKey);
            }
        }

        private async UniTask StartAsHostAsync(CancellationToken cancellation)
        {
            // Хост без игроков (зашёл в матч мышью) играет с клавиатуры.
            _localPlayers.TryJoinDefault();

            var prefab = await _assets.LoadAsync<GameObject>(_config.CharacterKey, cancellation);
            _characterLoaded = true;

            GameObject itemPrefab = null;
            if (_config.ItemCount > 0 && !string.IsNullOrEmpty(_config.ItemKey))
            {
                itemPrefab = await _assets.LoadAsync<GameObject>(_config.ItemKey, cancellation);
                _itemLoaded = true;
            }

            // Вернувшийся после обрыва игрок досматривает матч: показываем ему персонажей, когда он загрузит арену.
            _lobby.ClientLoadedCurrentState += OnClientLoaded;
            _subscribedToLoads = true;

            await _lobby.WaitForClientsLoadedAsync(cancellation);

            SpawnParticipants(prefab);
            SpawnItems(itemPrefab);
            _log.Info(LogTag, $"Match started: {_roster.ParticipantCount} participants, {_objects.Count - _roster.ParticipantCount} items.");
        }

        private void SpawnParticipants(GameObject prefab)
        {
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            int connected = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsConnected)
                {
                    connected++;
                }
            }

            // Сессия собрана поиском игры: пустые места занимают боты. Иначе ботов столько, сколько выбрали в лобби.
            int bots = _lobby.Matchmaking.Matchmade ? Mathf.Max(0, _lobby.Capacity - connected) : _setup.BotCount;
            int total = connected + bots;
            int spawnIndex = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                PlayerSlot slot = slots[i];
                if (!slot.IsConnected)
                {
                    // Игрок отвалился до старта: слот за ним держится, но в этом матче он не участвует.
                    continue;
                }

                PlayerCharacter character = InstantiateCharacter(prefab, spawnIndex++, total);
                IPlayerInput input = ResolveInput(slot, character);
                int id = _roster.Add(isBot: false);
                character.ConfigureServer(id, slot.Index, slot.LocalIndex, input, _roster);
                character.NetworkObject.SpawnWithOwnership(slot.ClientId);
                _objects.Track(character.NetworkObject);
            }

            for (int i = 0; i < bots; i++)
            {
                PlayerCharacter character = InstantiateCharacter(prefab, spawnIndex++, total);
                int id = _roster.Add(isBot: true);
                character.ConfigureServer(id, PlayerCharacter.BotSlot, 0, _botInputFactory.Create(character), _roster);
                character.NetworkObject.Spawn();
                _objects.Track(character.NetworkObject);
            }
        }

        private void SpawnItems(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            // Между игроками: со смещением на пол-шага, чтобы предметы не падали им на головы.
            float offset = Mathf.PI / Mathf.Max(1, _config.ItemCount);
            for (int i = 0; i < _config.ItemCount; i++)
            {
                Vector3 position = SpawnLayout.OnRing(i, _config.ItemCount, _config.ItemSpawnRadius, _config.SpawnHeight, offset);
                GameObject instance = _resolver.Instantiate(prefab, position, Quaternion.identity);
                if (!instance.TryGetComponent(out NetworkObject networkObject))
                {
                    UnityEngine.Object.Destroy(instance);
                    throw new InvalidOperationException($"Prefab '{_config.ItemKey}' has no {nameof(NetworkObject)}.");
                }

                networkObject.CheckObjectVisibility = _visibility;
                networkObject.Spawn();
                _objects.Track(networkObject);
            }
        }

        private IPlayerInput ResolveInput(PlayerSlot slot, PlayerCharacter character)
        {
            if (slot.ClientId != NetworkManager.ServerClientId)
            {
                // Ввод удалённого игрока приходит по сети в компонент самого персонажа.
                return character.NetworkInput;
            }

            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Index == slot.LocalIndex)
                {
                    return players[i].Input;
                }
            }

            _log.Warning(LogTag, $"Host slot P{slot.Index + 1} has no local player {slot.LocalIndex}; it will stand still.");
            return null;
        }

        private PlayerCharacter InstantiateCharacter(GameObject prefab, int spawnIndex, int total)
        {
            Vector3 position = SpawnLayout.OnRing(spawnIndex, total, _config.SpawnRadius, _config.SpawnHeight);
            Quaternion rotation = SpawnLayout.FacingCenter(position);

            GameObject instance = _resolver.Instantiate(prefab, position, rotation);
            if (!instance.TryGetComponent(out PlayerCharacter character))
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException($"Prefab '{_config.CharacterKey}' has no {nameof(PlayerCharacter)}.");
            }

            // Объекты матча получают только клиенты, загрузившие арену: иначе NGO создал бы их в чужой сцене,
            // а при её выгрузке клиент уничтожил бы сетевые объекты сам, что NGO считает ошибкой.
            character.NetworkObject.CheckObjectVisibility = _visibility;
            return character;
        }

        private void OnClientLoaded(ulong clientId)
        {
            _objects.ShowTo(clientId);
        }

        private void ReleaseLoadingHold()
        {
            if (_loadingHold != null)
            {
                _loadingHold.Dispose();
                _loadingHold = null;
            }
        }
    }
}
