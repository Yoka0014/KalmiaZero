using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace KalmiaZero.Utils
{
    internal static class RandomExtensions
    {
        public static void Shuffle<T>(this Random rand, T[] array) => rand.Shuffle(array.AsSpan());

        public static void Shuffle<T>(this Random rand, Span<T> span)
        {
            for(var i = span.Length - 1; i > 0; i--)
            {
                var j = rand.Next(0, i + 1);
                (span[i], span[j]) = (span[j], span[i]);
            }
        }

        public static void Shuffle<T>(this Random rand, List<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rand.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static T NextNormal<T>(this Random rand, T mu, T sigma) where T : struct, IFloatingPointIeee754<T>
        {
            T rand_0, rand_1;
            if(typeof(T) == typeof(Half) || typeof(T) == typeof(float))
            {
                while ((rand_0 = T.CreateChecked(rand.NextSingle())) <= T.Epsilon) ;
                rand_1 = T.CreateChecked(rand.NextSingle());
            }
            else
            {
                while ((rand_0 = T.CreateChecked(rand.NextDouble())) <= T.Epsilon) ;
                rand_1 = T.CreateChecked(rand.NextDouble());
            }

            var two = T.One + T.One;
            var normRand = T.Sqrt(-two * T.Log(rand_0)) * T.Sin(two * T.Pi * rand_1);
            return normRand * sigma + mu;
        }
    }
}
