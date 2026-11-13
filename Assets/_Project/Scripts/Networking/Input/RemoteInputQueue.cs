using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.Networking.Input
{
    /// <summary>
    /// Кадры ввода удалённого клиента на хосте: по одному на тик хоста, в порядке номеров.
    /// Пакеты приходят неравномерно и могут теряться или дублироваться (в каждом три последних кадра).
    /// Если кадров нет, хост повторяет последний стик и удержание без нажатий; если накопилось больше
    /// допустимого, лишние склеиваются (нажатия не теряются), и хост догоняет клиента.
    /// </summary>
    public sealed class RemoteInputQueue
    {
        private const int RingCapacity = 32;

        private readonly InputPacket[] _ring = new InputPacket[RingCapacity];
        private int _head;
        private int _count;
        private uint _lastReceived;
        private PlayerInputFrame _lastFrame;

        public RemoteInputQueue(int maxBuffered)
        {
            MaxBuffered = Mathf.Clamp(maxBuffered, 1, RingCapacity);
        }

        public int MaxBuffered { get; }

        public int Count => _count;

        /// <summary>Номер последнего кадра, применённого хостом. 0 — ещё ни одного.</summary>
        public uint LastConsumed { get; private set; }

        public void Receive(in InputBundle bundle)
        {
            // В пакете новый кадр первым; ставим в очередь от старого к новому, пропуская уже известные.
            for (int i = bundle.Count - 1; i >= 0; i--)
            {
                InputPacket packet = bundle[i];
                if (packet.Sequence == 0 || packet.Sequence <= _lastReceived)
                {
                    continue;
                }

                _lastReceived = packet.Sequence;
                Enqueue(packet);
            }
        }

        public PlayerInputFrame Consume()
        {
            if (_count == 0)
            {
                // Кадр задержался в сети: держим последнее состояние, но не повторяем нажатия.
                return new PlayerInputFrame(_lastFrame.Move, PlayerButtons.None, PlayerButtons.None, _lastFrame.Held);
            }

            InputPacket packet = Dequeue();
            PlayerInputFrame frame = packet.ToFrame();
            PlayerButtons pressed = frame.Pressed;
            PlayerButtons released = frame.Released;

            // Отстали от клиента: склеиваем лишние кадры в один, чтобы задержка ввода не росла.
            while (_count > MaxBuffered - 1 && _count > 0)
            {
                packet = Dequeue();
                frame = packet.ToFrame();
                pressed |= frame.Pressed;
                released |= frame.Released;
            }

            LastConsumed = packet.Sequence;
            _lastFrame = new PlayerInputFrame(frame.Move, pressed, released, frame.Held);
            return _lastFrame;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            _lastReceived = 0;
            LastConsumed = 0;
            _lastFrame = default;
        }

        private void Enqueue(in InputPacket packet)
        {
            if (_count == RingCapacity)
            {
                // Переполнение возможно, только если хост долго не сэмплил: старейший кадр уступает место.
                Dequeue();
            }

            _ring[(_head + _count) % RingCapacity] = packet;
            _count++;
        }

        private InputPacket Dequeue()
        {
            InputPacket packet = _ring[_head];
            _head = (_head + 1) % RingCapacity;
            _count--;
            return packet;
        }
    }
}
