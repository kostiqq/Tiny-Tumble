using System;
using Steamworks;
using TinyTumble.Core.Logging;
using VContainer.Unity;

namespace TinyTumble.Networking.Steam
{
    /// <summary>
    /// Жизненный цикл Steam API: запуск, колбэки каждый кадр, завершение. Если Steam не запущен или игра
    /// стартовала не из Steam (нет steam_appid.txt рядом с exe), сервис просто не готов, и сессия работает по IP.
    /// App ID пока 480 (Spacewar) из steam_appid.txt в корне проекта.
    /// </summary>
    public sealed class SteamService : IInitializable, ITickable, IDisposable
    {
        private const string LogTag = "Steam";

        private readonly IGameLog _log;

        public SteamService(IGameLog log)
        {
            _log = log;
        }

        public bool IsReady { get; private set; }

        /// <summary>SteamID текущего пользователя, 0 — Steam не готов.</summary>
        public ulong LocalSteamId { get; private set; }

        public void Initialize()
        {
            try
            {
                if (!Packsize.Test())
                {
                    _log.Error(LogTag, "Steamworks.NET was built for another platform (Packsize test failed).");
                    return;
                }

                ESteamAPIInitResult result = SteamAPI.InitEx(out string error);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    _log.Info(LogTag, $"Steam is not available ({result}: {error}). Online play goes over IP.");
                    return;
                }
            }
            catch (DllNotFoundException exception)
            {
                _log.Error(LogTag, $"steam_api64.dll was not found: {exception.Message}");
                return;
            }

            IsReady = true;
            LocalSteamId = SteamUser.GetSteamID().m_SteamID;
            // Ретрансляция Steam (SDR) готовится несколько секунд: начинаем сразу, а не при первом подключении.
            SteamNetworkingUtils.InitRelayNetworkAccess();
            _log.Info(LogTag, $"Steam is ready: {SteamFriends.GetPersonaName()} ({LocalSteamId}).");
        }

        public void Tick()
        {
            if (IsReady)
            {
                SteamAPI.RunCallbacks();
            }
        }

        public void Dispose()
        {
            if (!IsReady)
            {
                return;
            }

            IsReady = false;
            LocalSteamId = 0;
            SteamAPI.Shutdown();
        }
    }
}
