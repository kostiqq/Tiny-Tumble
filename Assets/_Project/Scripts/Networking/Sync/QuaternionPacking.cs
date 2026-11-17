using UnityEngine;

namespace TinyTumble.Networking.Sync
{
    /// <summary>
    /// Поворот в 32 бита по схеме «три наименьших»: 2 бита — индекс самой большой по модулю компоненты,
    /// по 10 бит — остальные три. Самая большая восстанавливается из единичной длины.
    /// Ошибка — около 0.2°, этого хватает для костей и корпуса персонажа.
    /// </summary>
    public static class QuaternionPacking
    {
        private const float Range = 0.70710678f;
        private const int Bits = 10;
        private const uint Mask = (1u << Bits) - 1u;
        private const float Steps = Mask;

        public static uint Pack(Quaternion rotation)
        {
            float x = rotation.x;
            float y = rotation.y;
            float z = rotation.z;
            float w = rotation.w;
            float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (length < 1e-6f)
            {
                return Pack(Quaternion.identity);
            }

            x /= length;
            y /= length;
            z /= length;
            w /= length;

            uint largest = 0;
            float largestAbs = Mathf.Abs(x);
            if (Mathf.Abs(y) > largestAbs)
            {
                largest = 1;
                largestAbs = Mathf.Abs(y);
            }

            if (Mathf.Abs(z) > largestAbs)
            {
                largest = 2;
                largestAbs = Mathf.Abs(z);
            }

            if (Mathf.Abs(w) > largestAbs)
            {
                largest = 3;
            }

            // q и -q — один поворот: делаем самую большую компоненту положительной, её знак не храним.
            float largestValue = largest == 0 ? x : largest == 1 ? y : largest == 2 ? z : w;
            if (largestValue < 0f)
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            float a;
            float b;
            float c;
            switch (largest)
            {
                case 0:
                    a = y;
                    b = z;
                    c = w;
                    break;
                case 1:
                    a = x;
                    b = z;
                    c = w;
                    break;
                case 2:
                    a = x;
                    b = y;
                    c = w;
                    break;
                default:
                    a = x;
                    b = y;
                    c = z;
                    break;
            }

            return (largest << 30) | (Quantize(a) << 20) | (Quantize(b) << 10) | Quantize(c);
        }

        public static Quaternion Unpack(uint packed)
        {
            uint largest = packed >> 30;
            float a = Dequantize((packed >> 20) & Mask);
            float b = Dequantize((packed >> 10) & Mask);
            float c = Dequantize(packed & Mask);
            float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            switch (largest)
            {
                case 0:
                    return new Quaternion(d, a, b, c);
                case 1:
                    return new Quaternion(a, d, b, c);
                case 2:
                    return new Quaternion(a, b, d, c);
                default:
                    return new Quaternion(a, b, c, d);
            }
        }

        private static uint Quantize(float value)
        {
            float normalized = (value + Range) / (2f * Range);
            return (uint)Mathf.Clamp(Mathf.RoundToInt(normalized * Steps), 0, (int)Mask);
        }

        private static float Dequantize(uint value)
        {
            return value / Steps * (2f * Range) - Range;
        }
    }
}
