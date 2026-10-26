using TinyTumble.Core.Input;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Создаёт ввод бота для персонажа. Реализация в AI: Gameplay не знает о ботах больше этого интерфейса.
    /// Бот работает только на хосте, где известно всё о персонажах.
    /// </summary>
    public interface IBotInputFactory
    {
        IPlayerInput Create(ICombatant self);
    }
}
