using System;

namespace TinyTumble.Core.Input
{
    /// <summary>Кнопки действий из раздела 4 GDD. Флаги, чтобы кадр ввода оставался значимым типом без массивов.</summary>
    [Flags]
    public enum PlayerButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Push = 1 << 1,
        Grab = 1 << 2,
        Parry = 1 << 3,
        Dash = 1 << 4,
        Emote1 = 1 << 5,
        Emote2 = 1 << 6,
        Emote3 = 1 << 7,
        Emote4 = 1 << 8,
    }
}
