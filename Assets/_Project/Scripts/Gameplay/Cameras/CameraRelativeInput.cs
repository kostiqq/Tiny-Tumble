using TinyTumble.Core.Input;

namespace TinyTumble.Gameplay.Cameras
{
    /// <summary>
    /// Ввод локального игрока, у которого «вперёд» — это от камеры. Оборачивает устройство один раз при привязке
    /// персонажа; боты и удалённые игроки идут без обёртки.
    /// </summary>
    public sealed class CameraRelativeInput : IPlayerInput
    {
        private readonly IPlayerInput _source;
        private readonly CameraMoveFrame _frame;

        public CameraRelativeInput(IPlayerInput source, CameraMoveFrame frame)
        {
            _source = source;
            _frame = frame;
        }

        public PlayerInputFrame Sample()
        {
            PlayerInputFrame frame = _source.Sample();
            return new PlayerInputFrame(_frame.ToWorld(frame.Move), frame.Pressed, frame.Released, frame.Held);
        }

        /// <summary>Обёртывает, если есть чем; null остаётся null.</summary>
        public static IPlayerInput Wrap(IPlayerInput source, CameraMoveFrame frame)
        {
            return source == null || frame == null ? source : new CameraRelativeInput(source, frame);
        }
    }
}
