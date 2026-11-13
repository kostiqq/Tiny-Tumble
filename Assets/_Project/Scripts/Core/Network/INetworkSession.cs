using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace TinyTumble.Core.Network
{
    /// <summary>
    /// Сетевая сессия. Соло и локальный кооп тоже идут через неё как хост без подключений,
    /// поэтому отдельной офлайн-ветки в игровой логике нет.
    /// </summary>
    public interface INetworkSession
    {
        ReadOnlyReactiveProperty<SessionRole> Role { get; }

        bool IsHost { get; }

        /// <summary>Подключены к чужому хосту.</summary>
        bool IsRemoteClient { get; }

        /// <summary>Сетевой id этой машины. У хоста всегда 0.</summary>
        ulong LocalClientId { get; }

        /// <summary>Код причины последнего отключения (см. <see cref="DisconnectReasons"/>), или пустая строка.</summary>
        string LastDisconnectReason { get; }

        /// <summary>Хост: подключился удалённый клиент.</summary>
        event Action<ulong> ClientConnected;

        /// <summary>Хост: удалённый клиент отключился.</summary>
        event Action<ulong> ClientDisconnected;

        /// <summary>
        /// Сессия закончилась окончательно не по нашей команде: хост нас не пустил или выгнал,
        /// либо связь пропала и ни вернуться к хосту, ни перейти к преемнику не удалось.
        /// </summary>
        event Action<string> ConnectionLost;

        /// <summary>Связь с хостом пропала, идёт переподключение или миграция хоста.</summary>
        bool IsRecovering { get; }

        UniTask<SessionStartResult> StartHostAsync(CancellationToken cancellationToken);

        /// <param name="host">Адрес хоста. IP без порта подключается к порту сессии по умолчанию.</param>
        UniTask<SessionStartResult> StartClientAsync(SessionEndpoint host, CancellationToken cancellationToken);

        /// <summary>Завершает сессию. Повторный вызов безопасен.</summary>
        void Shutdown();
    }

    public enum SessionRole
    {
        None = 0,
        Host = 1,
        Client = 2,
    }

    public readonly struct SessionStartResult
    {
        public readonly bool Success;

        /// <summary>Код из <see cref="DisconnectReasons"/>, если не удалось.</summary>
        public readonly string Reason;

        private SessionStartResult(bool success, string reason)
        {
            Success = success;
            Reason = reason;
        }

        public static SessionStartResult Ok => new SessionStartResult(true, string.Empty);

        public static SessionStartResult Fail(string reason)
        {
            return new SessionStartResult(false, reason);
        }
    }
}
