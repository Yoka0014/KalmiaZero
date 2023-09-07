using System;
using System.Collections.Generic;

namespace KalmiaZero.Utils
{
    internal static class RandomExtensions
    {
        public static void Shuffle<T>(this Random rand, Span<T> array)
        {
            for(var i = array.Length - 1; i > 0; i--)
            {
                var j = rand.Next(0, i + 1);
                (array[i], array[j]) = (array[j], array[i]);
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
    }
}
