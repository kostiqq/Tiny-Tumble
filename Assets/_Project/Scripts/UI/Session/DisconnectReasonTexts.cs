using TinyTumble.Core.Network;

namespace TinyTumble.UI.Session
{
    /// <summary>
    /// Текст для кода причины из <see cref="DisconnectReasons"/>. Перейдёт в таблицы Unity Localization вместе с остальным UI.
    /// </summary>
    public static class DisconnectReasonTexts
    {
        public static string Get(string reason)
        {
            switch (reason)
            {
                case DisconnectReasons.None:
                case null:
                    return string.Empty;
                case DisconnectReasons.VersionMismatch:
                    return "У хоста другая версия игры.";
                case DisconnectReasons.LobbyFull:
                    return "В лобби нет свободных мест.";
                case DisconnectReasons.MatchInProgress:
                    return "Матч уже идёт, подключитесь, когда хост вернётся в лобби.";
                case DisconnectReasons.LoadTimeout:
                    return "Арена загружалась слишком долго, хост вас отключил.";
                case DisconnectReasons.HostLeft:
                    return "Хост вышел, и продолжить игру с другим хостом не удалось.";
                case DisconnectReasons.ConnectFailed:
                    return "Не удалось подключиться. Проверьте адрес и что хост запущен.";
                case DisconnectReasons.InvalidAddress:
                    return "Неверный адрес. Пример: 192.168.0.10 или 192.168.0.10:7777.";
                case DisconnectReasons.AlreadyRunning:
                    return "Подключение уже идёт.";
                case DisconnectReasons.StartFailed:
                    return "Не удалось запустить сеть. Возможно, порт занят.";
                case DisconnectReasons.SessionMismatch:
                    return "По этому адресу уже идёт другая игра.";
                case DisconnectReasons.PartyTooBig:
                    return "В лобби не хватает мест на всю пати.";
                case DisconnectReasons.LobbyClosed:
                    return "Хост уже не ищет игроков.";
                case DisconnectReasons.HighPing:
                    return "Слишком большой пинг до хоста.";
                default:
                    return reason;
            }
        }
    }
}
