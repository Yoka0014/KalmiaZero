using System.Runtime.CompilerServices;

namespace KalmiaZero.Utils
{
    internal static class FastMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float Exp2(float x)
        {
            int exp;
            if (x < 0)
            {
                if (x < -126)
                    return 0.0f;
                exp = (int)(x - 1);
            }
            else
                exp = (int)x;

            float output = x - exp;
            output = 1.0f + output * (0.6602339f + 0.33976606f * output);
            var tmp = *(int*)(&output);
            tmp += (int)((uint)exp << 23);
            return *(float*)(&tmp);
        }

        public static unsafe float Exp(float x) => Exp2(1.442695040f * x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float Log2(float x)
        {
            var tmp = *(uint*)&x;
            var expb = tmp >> 23;
            tmp = (tmp & 0x7fffff) | (0x7f << 23);
            var output = *(float*)&tmp;
            output -= 1.0f;
            return output * (1.3465552f - 0.34655523f * output) - 127 + expb;
        }

        public static float Log(float x) => 0.6931471805599453f * Log2(x);
    }
}
