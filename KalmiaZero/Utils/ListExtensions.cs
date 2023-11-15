using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KalmiaZero.Utils
{
    internal static class ListExtensions
    {
        public static void AddRange<T>(this List<T> list, ReadOnlySpan<T> span)
        {
            foreach(var n in span)
                list.Add(n);
        }
    }
}
