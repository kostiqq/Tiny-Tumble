using System;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Ключ игрока на время запуска игры: по нему хост узнаёт вернувшегося после обрыва и отдаёт ему прежний слот.
    /// Сетевой id для этого не годится — при каждом подключении NGO выдаёт новый.
    /// Ключ живёт до выхода из игры: переподключение идёт в том же запуске, а несколько копий игры на одной
    /// машине (тесты) не должны делить ключ через общий файл. Со Steam ключом станет SteamID.
    /// </summary>
    public sealed class PlayerIdentity
    {
        public PlayerIdentity()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            ulong key = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8);
            // 0 зарезервирован под «ключа нет» (первое подключение без резерва).
            Key = key != 0 ? key : 1;
        }

        public ulong Key { get; }
    }
}
