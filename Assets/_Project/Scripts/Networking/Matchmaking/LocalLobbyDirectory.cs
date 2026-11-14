using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Session;
using UnityEngine;
using VContainer.Unity;

namespace TinyTumble.Networking.Matchmaking
{
    /// <summary>
    /// Локальный каталог лобби для редактора и development-сборки: копии игры на одной машине объявляют себя
    /// файлами в общей временной папке, адрес — 127.0.0.1 и порт хоста. Нужен, чтобы проверять поиск игры
    /// автотестом: со Steam каждая проверка требовала бы двух аккаунтов на двух машинах. В релизной сборке выключен.
    /// Хост переписывает свой файл раз в секунду; файлы старше <see cref="StaleSeconds"/> считаются брошенными.
    /// </summary>
    public sealed class LocalLobbyDirectory : ILobbyDirectory, ITickable, IDisposable
    {
        private const string LogTag = "Matchmaking";
        private const string Extension = ".lobby";
        private const float HeartbeatSeconds = 1f;
        private const double StaleSeconds = 3.0;

        private readonly NetworkSession _session;
        private readonly PlayerIdentity _identity;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;
        private readonly string _folder;
        private readonly string _ownFile;

        private bool _published;
        private int _players;
        private int _freeSlots;
        private float _nextWrite;

        public LocalLobbyDirectory(NetworkSession session, PlayerIdentity identity, SessionConfig config, IGameLog log)
        {
            _session = session;
            _identity = identity;
            _config = config;
            _log = log;
            _folder = Path.Combine(Application.temporaryCachePath, "local-lobbies");
            _ownFile = Path.Combine(_folder, _identity.Key.ToString("X16", CultureInfo.InvariantCulture) + Extension);
        }

        public ulong OwnKey => _identity.Key;

        public bool Supports(EndpointKind kind)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return kind == EndpointKind.Ip;
#else
            return false;
#endif
        }

        public UniTask SearchAsync(LobbyQuery query, List<LobbyAd> results, CancellationToken cancellationToken)
        {
            results.Clear();
            if (!Directory.Exists(_folder))
            {
                return UniTask.CompletedTask;
            }

            DateTime now = DateTime.UtcNow;
            string[] files = Directory.GetFiles(_folder, "*" + Extension);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (string.Equals(file, _ownFile, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if ((now - File.GetLastWriteTimeUtc(file)).TotalSeconds > StaleSeconds)
                    {
                        continue;
                    }

                    if (TryParse(File.ReadAllText(file), out int version, out LobbyAd ad)
                        && version == query.ProtocolVersion && ad.FreeSlots >= query.MinFreeSlots)
                    {
                        results.Add(ad);
                    }
                }
                catch (IOException)
                {
                    // Хост как раз переписывает файл: увидим его в следующий раз.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return UniTask.CompletedTask;
        }

        public void Publish(int players, int freeSlots)
        {
            if (_published && _players == players && _freeSlots == freeSlots)
            {
                return;
            }

            _published = true;
            _players = players;
            _freeSlots = freeSlots;
            Write();
        }

        public void Unpublish()
        {
            if (!_published)
            {
                return;
            }

            _published = false;
            Delete();
        }

        public void Tick()
        {
            if (_published && Time.realtimeSinceStartup >= _nextWrite)
            {
                Write();
            }
        }

        public void Dispose()
        {
            Unpublish();
        }

        /// <summary>Строка объявления: версия;ключ;порт;игроки;места. Вынесено для тестов.</summary>
        public static string Format(int version, ulong key, ushort port, int players, int freeSlots)
        {
            return string.Join(";",
                version.ToString(CultureInfo.InvariantCulture),
                key.ToString(CultureInfo.InvariantCulture),
                port.ToString(CultureInfo.InvariantCulture),
                players.ToString(CultureInfo.InvariantCulture),
                freeSlots.ToString(CultureInfo.InvariantCulture));
        }

        public static bool TryParse(string text, out int version, out LobbyAd ad)
        {
            version = 0;
            ad = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Trim().Split(';');
            if (parts.Length != 5
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out version)
                || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong key)
                || !ushort.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ushort port)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int players)
                || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int free))
            {
                return false;
            }

            // Копии на одной машине: пинг ничтожный, считаем нулём.
            ad = new LobbyAd(SessionEndpoint.Ip(SessionEndpoint.LoopbackIpv4, port), key, players, free, 0);
            return true;
        }

        private void Write()
        {
            _nextWrite = Time.realtimeSinceStartup + HeartbeatSeconds;
            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllText(_ownFile, Format(_config.ProtocolVersion, _identity.Key, _session.HostPort, _players, _freeSlots));
            }
            catch (IOException exception)
            {
                _log.Warning(LogTag, $"Could not write the local lobby ad: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                _log.Warning(LogTag, $"Could not write the local lobby ad: {exception.Message}");
            }
        }

        private void Delete()
        {
            try
            {
                if (File.Exists(_ownFile))
                {
                    File.Delete(_ownFile);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
