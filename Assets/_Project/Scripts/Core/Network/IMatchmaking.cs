using System;

namespace TinyTumble.Core.Network
{
    public enum MatchmakingState
    {
        /// <summary>Поиска нет (в том числе пати уже нашла игру и гостит у чужого хоста).</summary>
        Idle = 0,

        /// <summary>Ищем открытые лобби.</summary>
        Searching = 1,

        /// <summary>Пати переезжает к найденному хосту.</summary>
        Joining = 2,

        /// <summary>Подходящих лобби нет: наше лобби открыто для всех, ждём игроков и время от времени ищем лобби полнее.</summary>
        WaitingAsHost = 3,
    }

    /// <summary>
    /// «Найти игру»: лидер пати ищет открытое лобби с местами на всю пати и подходящим пингом и уводит туда пати.
    /// Не нашёл — открывает своё лобби и ждёт. Время вышло — матч начинается, свободные места занимают боты.
    /// Поиск ведёт лидер; остальные видят только <see cref="SecondsLeft"/> и <see cref="IsMatchmadeSession"/>.
    /// </summary>
    public interface IMatchmaking
    {
        MatchmakingState State { get; }

        /// <summary>Есть где искать (Steam или, в редакторе и development-сборке, локальный матчмейкер).</summary>
        bool IsAvailable { get; }

        /// <summary>Можно начать поиск: из меню или лидером своей пати в своём лобби, пока в нём только своя пати.</summary>
        bool CanStartSearch { get; }

        /// <summary>Сколько осталось до старта с ботами, для всех в сессии; меньше 0 — поиск не идёт.</summary>
        float SecondsLeft { get; }

        /// <summary>Сессия собрана поиском: свободные места в матче занимают боты.</summary>
        bool IsMatchmadeSession { get; }

        event Action<MatchmakingState> StateChanged;

        bool StartSearch();

        void Cancel();
    }
}
