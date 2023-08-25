using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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
    }
}
