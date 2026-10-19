using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Scenes;
using TinyTumble.Gameplay.Characters;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Старт матча: держит экран загрузки, пока грузится арена, затем расставляет игроков и ботов.
    /// </summary>
    public sealed class MatchEntryPoint : IInitializable, IAsyncStartable, IDisposable
    {
        private const string LogTag = "Match";

        private readonly MatchConfig _config;
        private readonly MatchArena _arena;
        private readonly MatchRoster _roster;
        private readonly IAssetProvider _assets;
        private readonly IObjectResolver _resolver;
        private readonly ILoadingScreen _loadingScreen;
        private readonly ILocalPlayers _localPlayers;
        private readonly IBotInputFactory _botInputFactory;
        private readonly IGameLog _log;
        private readonly List<StubCharacter> _characters = new List<StubCharacter>();
        private IDisposable _loadingHold;
        private bool _characterLoaded;

        public MatchEntryPoint(
            MatchConfig config,
            MatchArena arena,
            MatchRoster roster,
            IAssetProvider assets,
            IObjectResolver resolver,
            ILoadingScreen loadingScreen,
            ILocalPlayers localPlayers,
            IBotInputFactory botInputFactory,
            IGameLog log)
        {
            _config = config;
            _arena = arena;
            _roster = roster;
            _assets = assets;
            _resolver = resolver;
            _loadingScreen = loadingScreen;
            _localPlayers = localPlayers;
            _botInputFactory = botInputFactory;
            _log = log;
        }

        public IReadOnlyList<StubCharacter> Characters => _characters;

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

                var prefab = await _assets.LoadAsync<GameObject>(_config.CharacterKey, cancellation);
                _characterLoaded = true;

                SpawnParticipants(prefab);
                IsReady = true;
                _log.Info(LogTag, $"Match started: {_roster.ParticipantCount} participants.");
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
            ReleaseLoadingHold();
            if (_characterLoaded)
            {
                _characterLoaded = false;
                _assets.Release(_config.CharacterKey);
            }
        }

        private void SpawnParticipants(GameObject prefab)
        {
            // Матч, запущенный прямо из сцены Match, иначе остался бы без игроков.
            _localPlayers.TryJoinDefault();

            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            int total = players.Count + _config.BotCount;
            int slot = 0;

            for (int i = 0; i < players.Count; i++)
            {
                StubCharacter character = Spawn(prefab, slot++, total);
                int id = _roster.Add(isBot: false);
                character.Bind(id, players[i].Input, _roster, _config.GetPlayerColor(players[i].Index));
            }

            for (int i = 0; i < _config.BotCount; i++)
            {
                StubCharacter character = Spawn(prefab, slot++, total);
                int id = _roster.Add(isBot: true);
                character.Bind(id, _botInputFactory.Create(character.transform), _roster, _config.BotColor);
            }
        }

        private StubCharacter Spawn(GameObject prefab, int slot, int total)
        {
            float angle = total > 0 ? slot * Mathf.PI * 2f / total : 0f;
            var position = new Vector3(Mathf.Sin(angle) * _config.SpawnRadius, _config.SpawnHeight, Mathf.Cos(angle) * _config.SpawnRadius);
            // Лицом к центру стола.
            Quaternion rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + 180f, 0f);

            GameObject instance = _resolver.Instantiate(prefab, position, rotation);
            var character = instance.GetComponent<StubCharacter>();
            if (character == null)
            {
                throw new InvalidOperationException($"Prefab '{_config.CharacterKey}' has no {nameof(StubCharacter)}.");
            }

            _characters.Add(character);
            return character;
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
