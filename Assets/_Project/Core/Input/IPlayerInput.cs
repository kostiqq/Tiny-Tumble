namespace TinyTumble.Core.Input
{
    /// <summary>
    /// Источник ввода персонажа: устройство игрока или бот. Gameplay знает только этот интерфейс.
    /// </summary>
    public interface IPlayerInput
    {
        /// <summary>
        /// Вызывается ровно один раз за тик симуляции (FixedUpdate). Возвращает накопленное
        /// с прошлого вызова и сбрасывает накопители.
        /// </summary>
        PlayerInputFrame Sample();
    }
}
