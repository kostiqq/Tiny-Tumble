using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Создаёт ввод бота для персонажа. Реализация в AI: Gameplay не знает о ботах больше этого интерфейса.
    /// </summary>
    public interface IBotInputFactory
    {
        IPlayerInput Create(Transform body);
    }
}
