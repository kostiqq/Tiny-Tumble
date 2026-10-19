namespace TinyTumble.Core.Utils
{
    /// <summary>
    /// Счётчик удержаний. Методы возвращают true, когда меняется итоговое состояние «удерживается».
    /// </summary>
    public sealed class HoldCounter
    {
        public int Count { get; private set; }

        public bool IsHeld => Count > 0;

        /// <returns>true, если это первое удержание.</returns>
        public bool Acquire()
        {
            Count++;
            return Count == 1;
        }

        /// <returns>true, если это было последнее удержание.</returns>
        public bool Release()
        {
            if (Count == 0)
            {
                return false;
            }

            Count--;
            return Count == 0;
        }
    }
}
