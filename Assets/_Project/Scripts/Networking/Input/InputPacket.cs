using System;
using TinyTumble.Core.Input;
using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Input
{
    /// <summary>
    /// Кадр ввода одного тика в сетевом виде, с порядковым номером. По номеру хост подтверждает,
    /// какой кадр он уже применил, а клиент сверяет своё предсказание.
    /// Стик квантуется до байта на ось: клиент предсказывает по тому же квантованному значению,
    /// которое получит хост, поэтому округление не даёт расхождения.
    /// </summary>
    public struct InputPacket : INetworkSerializable, IEquatable<InputPacket>
    {
        private const float StickScale = 127f;

        private uint _sequence;
        private sbyte _moveX;
        private sbyte _moveY;
        private ushort _pressed;
        private ushort _released;
        private ushort _held;

        public InputPacket(uint sequence, in PlayerInputFrame frame)
        {
            _sequence = sequence;
            Vector2 move = Vector2.ClampMagnitude(frame.Move, 1f);
            _moveX = Quantize(move.x);
            _moveY = Quantize(move.y);
            _pressed = (ushort)frame.Pressed;
            _released = (ushort)frame.Released;
            _held = (ushort)frame.Held;
        }

        /// <summary>1, 2, 3… 0 — «кадра нет».</summary>
        public uint Sequence => _sequence;

        public PlayerInputFrame ToFrame()
        {
            // Ввод с сети не доверенный: стик длиннее единицы дал бы ускорение.
            var move = Vector2.ClampMagnitude(new Vector2(_moveX / StickScale, _moveY / StickScale), 1f);
            return new PlayerInputFrame(move, (PlayerButtons)_pressed, (PlayerButtons)_released, (PlayerButtons)_held);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _sequence);
            serializer.SerializeValue(ref _moveX);
            serializer.SerializeValue(ref _moveY);
            serializer.SerializeValue(ref _pressed);
            serializer.SerializeValue(ref _released);
            serializer.SerializeValue(ref _held);
        }

        public bool Equals(InputPacket other)
        {
            return _sequence == other._sequence && _moveX == other._moveX && _moveY == other._moveY
                && _pressed == other._pressed && _released == other._released && _held == other._held;
        }

        public override bool Equals(object obj)
        {
            return obj is InputPacket other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_sequence, _moveX, _moveY, _pressed, _released, _held);
        }

        private static sbyte Quantize(float value)
        {
            return (sbyte)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * StickScale);
        }
    }

    /// <summary>
    /// Последние <see cref="Capacity"/> кадров одним пакетом, новый первым. Канал ненадёжный: каждый кадр
    /// уходит трижды, и потеря двух пакетов подряд не теряет нажатий.
    /// </summary>
    public struct InputBundle : INetworkSerializable
    {
        public const int Capacity = 3;

        private byte _count;
        private InputPacket _frame0;
        private InputPacket _frame1;
        private InputPacket _frame2;

        public int Count => _count;

        /// <summary>0 — самый новый кадр.</summary>
        public InputPacket this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                switch (index)
                {
                    case 0:
                        return _frame0;
                    case 1:
                        return _frame1;
                    default:
                        return _frame2;
                }
            }
        }

        /// <summary>Добавляет новый кадр в начало; самый старый выпадает.</summary>
        public void PushNewest(in InputPacket packet)
        {
            _frame2 = _frame1;
            _frame1 = _frame0;
            _frame0 = packet;
            if (_count < Capacity)
            {
                _count++;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _count);
            if (serializer.IsReader && _count > Capacity)
            {
                _count = Capacity;
            }

            // Пишем только заполненные кадры: первые тики после спавна короче.
            if (_count > 0)
            {
                _frame0.NetworkSerialize(serializer);
            }

            if (_count > 1)
            {
                _frame1.NetworkSerialize(serializer);
            }

            if (_count > 2)
            {
                _frame2.NetworkSerialize(serializer);
            }
        }
    }
}
