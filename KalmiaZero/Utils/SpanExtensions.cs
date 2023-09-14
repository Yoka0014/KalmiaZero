using System;
using System.Numerics;

namespace KalmiaZero.Utils
{
    internal static class SpanExtensions
    {
        public static bool Contains<T>(this Span<T> span, T value) where T : IComparable
        {
            foreach(var n in span)
                if(n.CompareTo(value) == 0)
                    return true;
            return false;
        }

        public static T Max<T>(this ReadOnlySpan<T> span) where T : IComparable<T> => span.Max(x => x);

        public static TResult Max<TSource, TResult>(this ReadOnlySpan<TSource> span, Func<TSource, TResult> selector) where TResult : IComparable<TResult>
        {
            var max = selector(span[0]);
            foreach (var n in span[1..])
            {
                var key = selector(n);
                if (max.CompareTo(key) <= 0)
                    max = key;
            }
            return max;
        }

        public static int IndexOf<T>(this ReadOnlySpan<T> span, T value) where T : Enum
        {
            for(var i = 0; i < span.Length; i++)
                if (span[i].Equals(value)) 
                    return i;
            return -1;
        }

        public static T Sum<T>(this Span<T> span) where T : INumber<T> => Sum((ReadOnlySpan<T>)span);

        public static T Sum<T>(this ReadOnlySpan<T> span) where T : INumber<T>
        {
            var sum = T.Zero;
            foreach(var n in span)
                sum += n;
            return sum;
        }

        public static T Sum<T>(this T[] array) where T : INumber<T> => array.AsSpan().Sum();
    }
}
