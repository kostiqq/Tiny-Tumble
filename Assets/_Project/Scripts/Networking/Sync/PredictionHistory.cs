using UnityEngine;

namespace TinyTumble.Networking.Sync
{
    /// <summary>
    /// Что предсказал владелец после каждого своего кадра ввода. Хост присылает состояние «после кадра N»,
    /// владелец сравнивает его со своей записью для N и узнаёт, насколько ошибся.
    /// </summary>
    public sealed class PredictionHistory
    {
        private const int Capacity = 128;

        private struct Entry
        {
            public uint Sequence;
            public Vector3 Position;
            public Vector3 Velocity;
        }

        private readonly Entry[] _ring = new Entry[Capacity];

        public void Record(uint sequence, Vector3 position, Vector3 velocity)
        {
            if (sequence == 0)
            {
                return;
            }

            _ring[sequence % Capacity] = new Entry { Sequence = sequence, Position = position, Velocity = velocity };
        }

        public bool TryGet(uint sequence, out Vector3 position, out Vector3 velocity)
        {
            Entry entry = _ring[sequence % Capacity];
            if (sequence == 0 || entry.Sequence != sequence)
            {
                position = default;
                velocity = default;
                return false;
            }

            position = entry.Position;
            velocity = entry.Velocity;
            return true;
        }

        /// <summary>
        /// Тело сдвинули коррекцией: сдвигаем и записи, иначе следующий снимок хоста насчитает ту же ошибку ещё раз.
        /// </summary>
        public void Shift(Vector3 delta)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_ring[i].Sequence != 0)
                {
                    _ring[i].Position += delta;
                }
            }
        }

        public void Clear()
        {
            System.Array.Clear(_ring, 0, Capacity);
        }
    }

    public enum CorrectionKind
    {
        None = 0,

        /// <summary>Ошибка заметная, но небольшая: убирается за несколько тиков.</summary>
        Smooth = 1,

        /// <summary>Ошибка большая (удар, телепорт): исправляется сразу.</summary>
        Snap = 2,
    }

    /// <summary>
    /// Плавная коррекция предсказания без перепроигрывания физики: ошибка убирается по доле за тик.
    /// Для одинакового ввода хост и владелец считают одинаково, так что ошибки малы и появляются
    /// от столкновений и задержек ввода.
    /// </summary>
    public sealed class PredictionCorrector
    {
        private readonly float _tolerance;
        private readonly float _snapDistance;
        private readonly float _rate;

        public PredictionCorrector(float tolerance, float snapDistance, float rate)
        {
            _tolerance = tolerance;
            _snapDistance = Mathf.Max(snapDistance, tolerance);
            _rate = rate;
        }

        /// <summary>Сколько ещё осталось исправить плавной коррекцией.</summary>
        public Vector3 PendingError { get; private set; }

        public CorrectionKind Evaluate(Vector3 authoritative, Vector3 predicted, out Vector3 error)
        {
            error = authoritative - predicted;
            float distance = error.magnitude;
            if (distance >= _snapDistance)
            {
                PendingError = Vector3.zero;
                return CorrectionKind.Snap;
            }

            if (distance <= _tolerance)
            {
                PendingError = Vector3.zero;
                return CorrectionKind.None;
            }

            PendingError = error;
            return CorrectionKind.Smooth;
        }

        /// <summary>Часть ошибки, которую нужно убрать в этом тике.</summary>
        public Vector3 Step(float deltaTime)
        {
            if (PendingError == Vector3.zero)
            {
                return Vector3.zero;
            }

            Vector3 step = PendingError * Mathf.Min(1f, _rate * deltaTime);
            PendingError -= step;
            if (PendingError.sqrMagnitude < 1e-8f)
            {
                PendingError = Vector3.zero;
            }

            return step;
        }

        public void Reset()
        {
            PendingError = Vector3.zero;
        }
    }
}
