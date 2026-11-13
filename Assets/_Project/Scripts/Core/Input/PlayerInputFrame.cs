using UnityEngine;

namespace TinyTumble.Core.Input
{
    /// <summary>
    /// Снимок ввода за один тик симуляции.
    /// Pressed и Released накапливаются между тиками, поэтому короткий тап не теряется,
    /// даже если нажатие и отпускание произошли между двумя FixedUpdate.
    /// </summary>
    public readonly struct PlayerInputFrame
    {
        public readonly Vector2 Move;

        /// <summary>Кнопки, нажатые хотя бы раз с прошлого тика.</summary>
        public readonly PlayerButtons Pressed;

        /// <summary>Кнопки, отпущенные хотя бы раз с прошлого тика.</summary>
        public readonly PlayerButtons Released;

        /// <summary>Кнопки, удерживаемые в момент сэмпла.</summary>
        public readonly PlayerButtons Held;

        public PlayerInputFrame(Vector2 move, PlayerButtons pressed, PlayerButtons released, PlayerButtons held)
        {
            Move = move;
            Pressed = pressed;
            Released = released;
            Held = held;
        }

        public bool WasPressed(PlayerButtons button)
        {
            return (Pressed & button) != 0;
        }

        public bool WasReleased(PlayerButtons button)
        {
            return (Released & button) != 0;
        }

        public bool IsHeld(PlayerButtons button)
        {
            return (Held & button) != 0;
        }
    }
}
