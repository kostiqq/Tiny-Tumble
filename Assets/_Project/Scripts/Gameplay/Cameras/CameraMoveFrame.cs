using TinyTumble.Core.Configs;
using UnityEngine;

namespace TinyTumble.Gameplay.Cameras
{
    /// <summary>
    /// Поворот камеры по горизонтали, относительно которого локальные игроки двигают персонажей.
    /// Пишет камера матча раз в кадр, читает ввод персонажа в FixedUpdate. Стик поворачивается до отправки
    /// на хост, поэтому хост и сеть по-прежнему получают направление в осях стола.
    /// </summary>
    public sealed class CameraMoveFrame
    {
        private readonly CameraConfig _config;
        private float _yaw;
        private float _sin;
        private float _cos = 1f;

        public CameraMoveFrame(CameraConfig config)
        {
            _config = config;
        }

        /// <summary>Поворот камеры вокруг вертикали, градусы: 0 — смотрит вдоль +Z.</summary>
        public float Yaw => _yaw;

        public void SetYaw(float yaw)
        {
            if (Mathf.Approximately(yaw, _yaw))
            {
                return;
            }

            _yaw = yaw;
            float radians = yaw * Mathf.Deg2Rad;
            _sin = Mathf.Sin(radians);
            _cos = Mathf.Cos(radians);
        }

        /// <summary>Стик (x — вправо, y — вперёд от камеры) в направление по осям стола (x, z).</summary>
        public Vector2 ToWorld(Vector2 move)
        {
            if (!_config.CameraRelativeMovement || move == Vector2.zero)
            {
                return move;
            }

            // Поворот вокруг Y на yaw: вперёд камеры = (sin, cos), вправо = (cos, -sin).
            return new Vector2(move.x * _cos + move.y * _sin, move.y * _cos - move.x * _sin);
        }
    }
}
