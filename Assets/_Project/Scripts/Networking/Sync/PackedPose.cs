using System;
using Unity.Netcode;

namespace TinyTumble.Networking.Sync
{
    /// <summary>
    /// Повороты костей персонажа относительно родительских костей, каждый упакован в 32 бита
    /// (<see cref="QuaternionPacking"/>). Число костей фиксировано: формат снимка одинаков для всех моделей.
    /// Что за кость под каким индексом, знает геймплей; сетевому слою это не нужно.
    /// Поля, а не массив: структура копируется без аллокаций.
    /// </summary>
    public struct PackedPose : INetworkSerializable
    {
        public const int Count = 10;

        private uint _r0;
        private uint _r1;
        private uint _r2;
        private uint _r3;
        private uint _r4;
        private uint _r5;
        private uint _r6;
        private uint _r7;
        private uint _r8;
        private uint _r9;

        public uint this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return _r0;
                    case 1: return _r1;
                    case 2: return _r2;
                    case 3: return _r3;
                    case 4: return _r4;
                    case 5: return _r5;
                    case 6: return _r6;
                    case 7: return _r7;
                    case 8: return _r8;
                    case 9: return _r9;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: _r0 = value; break;
                    case 1: _r1 = value; break;
                    case 2: _r2 = value; break;
                    case 3: _r3 = value; break;
                    case 4: _r4 = value; break;
                    case 5: _r5 = value; break;
                    case 6: _r6 = value; break;
                    case 7: _r7 = value; break;
                    case 8: _r8 = value; break;
                    case 9: _r9 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _r0);
            serializer.SerializeValue(ref _r1);
            serializer.SerializeValue(ref _r2);
            serializer.SerializeValue(ref _r3);
            serializer.SerializeValue(ref _r4);
            serializer.SerializeValue(ref _r5);
            serializer.SerializeValue(ref _r6);
            serializer.SerializeValue(ref _r7);
            serializer.SerializeValue(ref _r8);
            serializer.SerializeValue(ref _r9);
        }
    }
}
