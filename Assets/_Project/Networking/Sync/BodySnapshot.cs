using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Sync
{
    /// <summary>
    /// Состояние физического тела на хосте в момент времени. Игровые поля (состояние персонажа, флаги)
    /// — непрозрачные байты: сетевой слой правил игры не знает.
    /// </summary>
    public struct BodySnapshot : INetworkSerializable
    {
        /// <summary>Время физики хоста, с.</summary>
        public double Time;

        /// <summary>Номер последнего кадра ввода владельца, применённого до этого состояния. 0 — не было.</summary>
        public uint Ack;

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;

        public byte State;
        public byte Flags;

        /// <summary>Растёт при каждой телепортации: между такими снимками интерполировать нельзя.</summary>
        public byte Teleport;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Time);
            serializer.SerializeValue(ref Ack);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref Teleport);
        }

        /// <summary>Промежуточное состояние. Дискретные поля — от более раннего снимка: переход виден в свой момент.</summary>
        public static BodySnapshot Lerp(in BodySnapshot from, in BodySnapshot to, float t)
        {
            return new BodySnapshot
            {
                Time = from.Time + (to.Time - from.Time) * t,
                Ack = from.Ack,
                Position = Vector3.LerpUnclamped(from.Position, to.Position, t),
                Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t),
                Velocity = Vector3.LerpUnclamped(from.Velocity, to.Velocity, t),
                State = from.State,
                Flags = from.Flags,
                Teleport = from.Teleport,
            };
        }
    }
}
