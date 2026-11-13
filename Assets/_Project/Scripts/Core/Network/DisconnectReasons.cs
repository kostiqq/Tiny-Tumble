namespace TinyTumble.Core.Network
{
    /// <summary>
    /// Коды причин отказа и отключения. По сети идёт код, а не текст: текст на нужном языке подбирает UI.
    /// </summary>
    public static class DisconnectReasons
    {
        public const string None = "";
        public const string VersionMismatch = "version";
        public const string LobbyFull = "full";
        public const string MatchInProgress = "in_match";
        public const string LoadTimeout = "load_timeout";
        public const string HostLeft = "host_left";
        public const string ConnectFailed = "connect_failed";
        public const string InvalidAddress = "bad_address";
        public const string AlreadyRunning = "already_running";
        public const string StartFailed = "start_failed";

        /// <summary>По адресу отвечает чужая сессия (например, преемник уже начал новую игру).</summary>
        public const string SessionMismatch = "session_mismatch";

        /// <summary>Пати целиком не помещается в лобби: пускать её по частям нельзя.</summary>
        public const string PartyTooBig = "party_too_big";

        /// <summary>
        /// Наш ли это код. NGO при обрыве кладёт в причину свой текст на английском — его в UI не показываем.
        /// </summary>
        public static bool IsKnown(string reason)
        {
            switch (reason)
            {
                case VersionMismatch:
                case LobbyFull:
                case MatchInProgress:
                case LoadTimeout:
                case HostLeft:
                case ConnectFailed:
                case InvalidAddress:
                case AlreadyRunning:
                case StartFailed:
                case SessionMismatch:
                case PartyTooBig:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Хост сознательно не пускает нас: переподключаться и искать преемника бессмысленно.
        /// </summary>
        public static bool IsRejection(string reason)
        {
            switch (reason)
            {
                case VersionMismatch:
                case LobbyFull:
                case MatchInProgress:
                case LoadTimeout:
                case SessionMismatch:
                case PartyTooBig:
                    return true;
                default:
                    return false;
            }
        }
    }
}
