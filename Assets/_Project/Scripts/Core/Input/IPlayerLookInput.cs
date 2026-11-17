using UnityEngine;

namespace TinyTumble.Core.Input
{
    /// <summary>
    /// Управление камерой с устройств локального игрока. Камера локальна, по сети не передаётся,
    /// поэтому это отдельный от <see cref="IPlayerInput"/> интерфейс. Читается раз в кадр (LateUpdate).
    /// </summary>
    public interface IPlayerLookInput
    {
        /// <summary>Перемещение мыши за кадр, пиксели.</summary>
        Vector2 ReadMouseDelta();

        /// <summary>Правый стик, сырое значение -1..1 (мёртвую зону применяет камера).</summary>
        Vector2 ReadStick();

        /// <summary>Нажатия «следующий / предыдущий персонаж» в наблюдении с прошлого вызова; сбрасывает их.</summary>
        int ConsumeSpectateSteps();

        /// <summary>Нажат ли переключатель захвата курсора с прошлого вызова; сбрасывает нажатие.</summary>
        bool ConsumeCursorToggle();
    }
}
