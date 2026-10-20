using TinyTumble.Core.Input;
using Unity.Netcode;

namespace TinyTumble.Networking.Input
{
    /// <summary>
    /// Ввод персонажа, которым управляет удалённый клиент. У клиента-владельца сэмплит его локальный
    /// <see cref="IPlayerInput"/> и шлёт хосту; на хосте сам является <see cref="IPlayerInput"/> для симуляции.
    /// Персонаж на хосте не отличает такой ввод от геймпада или бота.
    /// Отправка надёжная: потерянное нажатие прыжка хуже лишних байт; предсказание и ненадёжный канал — этап 3.
    /// </summary>
    public sealed class NetworkPlayerInput : NetworkBehaviour, IPlayerInput
    {
        private IPlayerInput _localSource;
        private RemoteInputBuffer _buffer;
        private InputPacket _lastSent;
        private bool _hasSent;

        /// <summary>Клиент-владелец: чей ввод отправлять.</summary>
        public void SetLocalSource(IPlayerInput source)
        {
            _localSource = source;
        }

        public PlayerInputFrame Sample()
        {
            return _buffer.Consume();
        }

        public override void OnNetworkDespawn()
        {
            _localSource = null;
            _buffer.Clear();
        }

        public override void OnLostOwnership()
        {
            _localSource = null;
        }

        private void FixedUpdate()
        {
            // Хост симулирует сам и ввод себе не шлёт; чужие клиенты ничего не отправляют.
            if (_localSource == null || !IsSpawned || !IsOwner || IsServer)
            {
                return;
            }

            // Ровно один сэмпл за тик, как и у персонажа на хосте.
            var packet = new InputPacket(_localSource.Sample());

            // Пока стик и кнопки не меняются, хосту хватает последнего пакета.
            if (_hasSent && !packet.HasEvents && packet.SameState(_lastSent))
            {
                return;
            }

            _lastSent = packet;
            _hasSent = true;
            SubmitInputRpc(packet);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(InputPacket packet)
        {
            _buffer.Push(packet.ToFrame());
        }
    }
}
