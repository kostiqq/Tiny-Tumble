using System.Collections.Generic;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Барьер «все клиенты загрузили сцену». Эпоха отличает отчёты о текущем переходе от запоздавших
    /// отчётов о прошлом (например, второй матч подряд).
    /// </summary>
    public sealed class ClientLoadTracker
    {
        private readonly List<ulong> _pending = new List<ulong>();

        public int Epoch { get; private set; }

        public bool IsComplete => _pending.Count == 0;

        public IReadOnlyList<ulong> Pending => _pending;

        public void Begin(int epoch, IReadOnlyList<ulong> clients)
        {
            Epoch = epoch;
            _pending.Clear();
            for (int i = 0; i < clients.Count; i++)
            {
                if (!_pending.Contains(clients[i]))
                {
                    _pending.Add(clients[i]);
                }
            }
        }

        /// <returns>true, если отчёт принят (клиент ждался в этой эпохе).</returns>
        public bool Report(ulong clientId, int epoch)
        {
            return epoch == Epoch && _pending.Remove(clientId);
        }

        /// <summary>Отключившийся клиент больше не задерживает остальных.</summary>
        public void Remove(ulong clientId)
        {
            _pending.Remove(clientId);
        }
    }
}
