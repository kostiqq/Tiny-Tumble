using System;
using TinyTumble.Core.Input;
using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Input
{
    /// <summary>
    /// Ввод, пришедший с клиента. Между тиками хоста может прийти несколько пакетов или ни одного:
    /// нажатия копятся до сэмпла, стик и удержание берутся из последнего пакета.
    /// </summary>
    public struct RemoteInputBuffer
    {
        private Vector2 _move;
        private PlayerButtons _held;
        private PlayerButtons _pressed;
        private PlayerButtons _released;

        public void Push(in PlayerInputFrame frame)
        {
            _move = frame.Move;
            _held = frame.Held;
            _pressed |= frame.Pressed;
            _released |= frame.Released;
        }

        public PlayerInputFrame Consume()
        {
            var frame = new PlayerInputFrame(_move, _pressed, _released, _held);
            _pressed = PlayerButtons.None;
            _released = PlayerButtons.None;
            return frame;
        }

        /// <summary>Клиент отвалился или персонаж сменил владельца: не держим кнопки и стик навечно.</summary>
        public void Clear()
        {
            this = default;
        }
    }

    /// <summary>Кадр ввода в сетевом виде.</summary>
    public struct InputPacket : INetworkSerializable, IEquatable<InputPacket>
    {
        private Vector2 _move;
        private ushort _pressed;
        private ushort _released;
        private ushort _held;

        public InputPacket(in PlayerInputFrame frame)
        {
            _move = frame.Move;
            _pressed = (ushort)frame.Pressed;
            _released = (ushort)frame.Released;
            _held = (ushort)frame.Held;
        }

        /// <summary>Кадр без событий: те же стик и удержание, без нажатий и отпусканий.</summary>
        public bool HasEvents => _pressed != 0 || _released != 0;

        public PlayerInputFrame ToFrame()
        {
            // Ввод с сети не доверенный: стик длиннее единицы дал бы ускорение.
            return new PlayerInputFrame(Vector2.ClampMagnitude(_move, 1f), (PlayerButtons)_pressed, (PlayerButtons)_released, (PlayerButtons)_held);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _move);
            serializer.SerializeValue(ref _pressed);
            serializer.SerializeValue(ref _released);
            serializer.SerializeValue(ref _held);
        }

        /// <summary>Совпадают стик и удержание. Нажатия не сравниваются: пакет с ними отправляется всегда.</summary>
        public bool SameState(in InputPacket other)
        {
            return _move == other._move && _held == other._held;
        }

        public bool Equals(InputPacket other)
        {
            return _move == other._move && _pressed == other._pressed && _released == other._released && _held == other._held;
        }

        public override bool Equals(object obj)
        {
            return obj is InputPacket other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_move, _pressed, _released, _held);
        }
    }
}
