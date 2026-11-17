using System;
using System.Collections.Generic;

namespace TinyTumble.Core.Input
{
    /// <summary>
    /// Локальные игроки, присоединившиеся со своих устройств. Живут дольше сцен: лобби → матч → итоги → лобби.
    /// </summary>
    public interface ILocalPlayers
    {
        IReadOnlyList<LocalPlayer> Players { get; }

        int MaxPlayers { get; }

        /// <summary>Пока true, нажатие кнопки присоединения на новом устройстве добавляет игрока.</summary>
        bool JoiningEnabled { get; set; }

        event Action<LocalPlayer> PlayerJoined;

        /// <summary>
        /// Присоединяет клавиатуру и мышь, если ещё нет ни одного игрока. Нужен для запуска матча
        /// напрямую из сцены Match. Возвращает false, если клавиатуры нет или игроки уже есть.
        /// </summary>
        bool TryJoinDefault();
    }

    public sealed class LocalPlayer
    {
        public LocalPlayer(int index, string deviceName, IPlayerInput input, IPlayerLookInput look = null)
        {
            Index = index;
            DeviceName = deviceName;
            Input = input;
            Look = look;
        }

        /// <summary>0..3, соответствует P1..P4.</summary>
        public int Index { get; }

        public string DeviceName { get; }

        public IPlayerInput Input { get; }

        /// <summary>Управление камерой; null — у игрока нет камеры (тесты).</summary>
        public IPlayerLookInput Look { get; }
    }
}
