using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.Infrastructure.Input
{
    /// <summary>
    /// Копит нажатия и отпускания между тиками. Колбэки Input System приходят в Update,
    /// а симуляция сэмплит в FixedUpdate, поэтому без накопления быстрый тап может пропасть.
    /// </summary>
    public struct InputAccumulator
    {
        private PlayerButtons _pressed;
        private PlayerButtons _released;

        public void RegisterPress(PlayerButtons button)
        {
            _pressed |= button;
        }

        public void RegisterRelease(PlayerButtons button)
        {
            _released |= button;
        }

        /// <summary>Отдаёт накопленное вместе с текущим состоянием и сбрасывает накопители.</summary>
        public PlayerInputFrame Consume(Vector2 move, PlayerButtons held)
        {
            var frame = new PlayerInputFrame(Vector2.ClampMagnitude(move, 1f), _pressed, _released, held);
            _pressed = PlayerButtons.None;
            _released = PlayerButtons.None;
            return frame;
        }
    }
}
