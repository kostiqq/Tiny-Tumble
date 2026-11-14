using System;

namespace TinyTumble.Core.Network
{
    /// <summary>
    /// Пати — игроки, которые держатся вместе и переезжают между сессиями целиком. Пати не равна сессии:
    /// в одной сессии могут быть несколько пати (своя у хоста и пришедшие к нему), и пати переживает
    /// смену хоста. Друг, подключившийся по приглашению или адресу, входит в пати хоста.
    /// Состав виден в <see cref="ILobby.Slots"/> по <see cref="PlayerSlot.PartyId"/>.
    /// </summary>
    public interface IParty
    {
        ulong PartyId { get; }

        /// <summary>Мы лидер своей пати: можем увести её в другую сессию.</summary>
        bool IsLeader { get; }

        /// <summary>Пати переезжает: старая сессия закрыта, идёт подключение к новой.</summary>
        bool IsMoving { get; }

        /// <summary>Мы лидер, сессия идёт и хост в лобби (посреди матча не переезжают).</summary>
        bool CanMove { get; }

        /// <summary>Мы лидер пати, которая гостит у чужого хоста: можно увести её обратно к себе.</summary>
        bool CanRegroup { get; }

        /// <summary>Переезд начался (у каждого участника пати).</summary>
        event Action MoveStarted;

        /// <summary>Переезд закончился: удачно или нет, смотрите роль сессии.</summary>
        event Action MoveFinished;

        /// <summary>
        /// Увести всю пати к другому хосту. Если хост её не примет или недоступен, пати соберётся у лидера:
        /// он поднимет свою сессию, остальные подключатся к нему.
        /// </summary>
        bool MoveTo(SessionEndpoint host);

        /// <summary>Уйти всей пати из чужой сессии: лидер становится хостом, остальные подключаются к нему.</summary>
        bool Regroup();
    }
}
