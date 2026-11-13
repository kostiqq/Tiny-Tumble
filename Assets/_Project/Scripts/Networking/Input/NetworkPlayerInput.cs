using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using Unity.Netcode;
using VContainer;

namespace TinyTumble.Networking.Input
{
    /// <summary>
    /// Ввод персонажа, которым управляет удалённый клиент. Клиент-владелец каждый тик отдаёт сюда свой кадр
    /// (<see cref="Submit"/>) и сам же по нему предсказывает движение; кадр уходит хосту по ненадёжному каналу
    /// вместе с двумя предыдущими. На хосте компонент — обычный <see cref="IPlayerInput"/>: персонаж
    /// не отличает такой ввод от геймпада или бота.
    /// </summary>
    public sealed class NetworkPlayerInput : NetworkBehaviour, IPlayerInput
    {
        private const int DefaultMaxBuffered = 4;

        private RemoteInputQueue _queue = new RemoteInputQueue(DefaultMaxBuffered);
        private InputBundle _outgoing;
        private uint _nextSequence;

        /// <summary>Хост: номер последнего применённого кадра. Уходит владельцу в снимке состояния.</summary>
        public uint LastConsumedSequence => _queue.LastConsumed;

        /// <summary>Хост: сколько кадров ждёт применения (диагностика).</summary>
        public int BufferedCount => _queue.Count;

        [Inject]
        public void Construct(SessionConfig config)
        {
            _queue = new RemoteInputQueue(config.MaxBufferedInputs);
        }

        public PlayerInputFrame Sample()
        {
            return _queue.Consume();
        }

        /// <summary>
        /// Клиент-владелец, ровно раз за тик: отправляет кадр хосту. Возвращает его номер и кадр в том виде,
        /// в каком его применит хост, — по нему и нужно предсказывать.
        /// </summary>
        public uint Submit(in PlayerInputFrame frame, out PlayerInputFrame sent)
        {
            var packet = new InputPacket(++_nextSequence, frame);
            sent = packet.ToFrame();
            if (!IsSpawned || !IsOwner || IsServer)
            {
                return packet.Sequence;
            }

            _outgoing.PushNewest(packet);
            SubmitInputRpc(_outgoing);
            return packet.Sequence;
        }

        public override void OnNetworkDespawn()
        {
            ResetStreams();
        }

        public override void OnGainedOwnership()
        {
            ResetStreams();
        }

        public override void OnLostOwnership()
        {
            ResetStreams();
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(InputBundle bundle)
        {
            _queue.Receive(bundle);
        }

        private void ResetStreams()
        {
            // Клиент отвалился или персонаж сменил владельца: не держим кнопки и стик навечно.
            _queue.Clear();
            _outgoing = default;
            _nextSequence = 0;
        }
    }
}
