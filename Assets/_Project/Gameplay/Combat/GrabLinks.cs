using System;
using Unity.Netcode;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// Кто держит объект: до двух NetworkObjectId (цепочка захвата — двое держат одного).
    /// Синхронизируется всем: каждая машина отключает столкновения держащего и удерживаемого,
    /// иначе предсказанное тело владельца врезалось бы в того, кого он несёт.
    /// Внутри хранится id + 1, чтобы значение по умолчанию означало «никто не держит».
    /// </summary>
    public struct GrabLinks : INetworkSerializable, IEquatable<GrabLinks>
    {
        public const int Capacity = 2;
        public const ulong None = ulong.MaxValue;

        private ulong _first;
        private ulong _second;

        public int Count => (_first != 0 ? 1 : 0) + (_second != 0 ? 1 : 0);

        public ulong this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return _first != 0 ? _first - 1 : None;
                    case 1:
                        return _second != 0 ? _second - 1 : None;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public bool Contains(ulong id)
        {
            return id != None && (_first == id + 1 || _second == id + 1);
        }

        public bool TryAdd(ulong id)
        {
            if (id == None || Contains(id))
            {
                return false;
            }

            if (_first == 0)
            {
                _first = id + 1;
                return true;
            }

            if (_second == 0)
            {
                _second = id + 1;
                return true;
            }

            return false;
        }

        public bool Remove(ulong id)
        {
            if (id == None)
            {
                return false;
            }

            if (_first == id + 1)
            {
                _first = _second;
                _second = 0;
                return true;
            }

            if (_second == id + 1)
            {
                _second = 0;
                return true;
            }

            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _first);
            serializer.SerializeValue(ref _second);
        }

        public bool Equals(GrabLinks other)
        {
            return _first == other._first && _second == other._second;
        }

        public override bool Equals(object obj)
        {
            return obj is GrabLinks other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_first, _second);
        }
    }
}
