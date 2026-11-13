using UnityEngine;

namespace TinyTumble.Networking.Sync
{
    /// <summary>
    /// Показывает тело, которым управляет хост, с задержкой <c>delay</c>: между двумя известными снимками
    /// всегда есть что интерполировать, даже при джиттере. Свои часы воспроизведения плавно подстраиваются
    /// под приход снимков (±10% скорости), а при большом расхождении прыгают сразу.
    /// Снимки, пришедшие не по порядку (канал ненадёжный), отбрасываются.
    /// </summary>
    public sealed class SnapshotInterpolator
    {
        private const int Capacity = 32;
        private const double MaxRateAdjust = 0.1;
        private const double RateGain = 2.0;
        private const double SnapDrift = 0.5;
        private const double MaxExtrapolation = 0.1;

        private readonly BodySnapshot[] _buffer = new BodySnapshot[Capacity];
        private int _count;
        private double _renderTime;
        private bool _clockStarted;

        public bool HasData => _count > 0;

        public int Count => _count;

        public BodySnapshot Latest => _buffer[_count - 1];

        /// <summary>Момент времени хоста, который сейчас показывается.</summary>
        public double RenderTime => _renderTime;

        /// <returns>false, если снимок устарел (пришёл позже более нового).</returns>
        public bool Add(in BodySnapshot snapshot)
        {
            if (_count > 0)
            {
                if (snapshot.Teleport != Latest.Teleport)
                {
                    // Телепорт: прошлые снимки к новому положению не ведут, интерполировать между ними нельзя.
                    _count = 0;
                }
                else if (snapshot.Time <= Latest.Time)
                {
                    return false;
                }
            }

            if (_count == Capacity)
            {
                Discard(1);
            }

            _buffer[_count++] = snapshot;
            return true;
        }

        public bool TrySample(float deltaTime, float delay, out BodySnapshot result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }

            AdvanceClock(deltaTime, delay);

            if (_renderTime <= _buffer[0].Time)
            {
                result = _buffer[0];
                return true;
            }

            int from = _count - 1;
            while (from > 0 && _buffer[from].Time > _renderTime)
            {
                from--;
            }

            if (from == _count - 1)
            {
                // Новых снимков нет: недолго продолжаем движение по скорости, дальше стоим.
                result = _buffer[from];
                double ahead = System.Math.Min(_renderTime - result.Time, MaxExtrapolation);
                result.Position += result.Velocity * (float)ahead;
            }
            else
            {
                BodySnapshot a = _buffer[from];
                BodySnapshot b = _buffer[from + 1];
                float t = (float)((_renderTime - a.Time) / (b.Time - a.Time));
                result = BodySnapshot.Lerp(a, b, Mathf.Clamp01(t));
            }

            // Всё, что старше текущей пары, уже не понадобится.
            Discard(from);
            return true;
        }

        public void Clear()
        {
            _count = 0;
            _clockStarted = false;
        }

        private void AdvanceClock(float deltaTime, float delay)
        {
            double target = Latest.Time - delay;
            if (!_clockStarted)
            {
                _clockStarted = true;
                _renderTime = target;
                return;
            }

            double drift = target - _renderTime;
            if (System.Math.Abs(drift) > SnapDrift)
            {
                _renderTime = target;
                return;
            }

            double rate = 1.0 + System.Math.Max(-MaxRateAdjust, System.Math.Min(MaxRateAdjust, drift * RateGain));
            _renderTime += deltaTime * rate;
        }

        private void Discard(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            for (int i = amount; i < _count; i++)
            {
                _buffer[i - amount] = _buffer[i];
            }

            _count -= amount;
        }
    }
}
