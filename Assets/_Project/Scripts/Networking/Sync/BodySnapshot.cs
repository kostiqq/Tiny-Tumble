using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Sync
{
    /// <summary>
    /// Состояние физического персонажа на хосте в момент времени: корень (таз), направление взгляда
    /// и повороты костей. Игровые поля (состояние персонажа, флаги) — непрозрачные байты:
    /// сетевой слой правил игры не знает.
    /// </summary>
    public struct BodySnapshot : INetworkSerializable
    {
        /// <summary>Время физики хоста, с.</summary>
        public double Time;

        public Vector3 Position;

        /// <summary>Поворот корня, упакованный <see cref="QuaternionPacking"/>.</summary>
        public uint Rotation;

        public Vector3 Velocity;

        /// <summary>Куда смотрит персонаж: угол вокруг вертикали, 0..65535 на полный круг.</summary>
        public ushort Yaw;

        public byte State;
        public byte Flags;

        /// <summary>Растёт при каждой телепортации: между такими снимками интерполировать нельзя.</summary>
        public byte Teleport;

        public PackedPose Pose;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Time);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref Teleport);
            serializer.SerializeNetworkSerializable(ref Pose);
        }

        public static ushort PackYaw(Vector3 facing)
        {
            float degrees = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            return (ushort)Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 360f * ushort.MaxValue);
        }

        public static Vector3 UnpackYaw(ushort yaw)
        {
            float radians = yaw / (float)ushort.MaxValue * 2f * Mathf.PI;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
