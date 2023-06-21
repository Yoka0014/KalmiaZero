using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace KalmiaZero.Utils
{
    public static class BitManipulations
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ByteSwap(ulong bits)
        {
            var ret = bits << 56;
            ret |= (bits & 0x000000000000ff00) << 40;
            ret |= (bits & 0x0000000000ff0000) << 24;
            ret |= (bits & 0x00000000ff000000) << 8;
            ret |= (bits & 0x000000ff00000000) >> 8;
            ret |= (bits & 0x0000ff0000000000) >> 24;
            ret |= (bits & 0x00ff000000000000) >> 40;
            return ret | (bits >> 56);
        }

        public static int FindFirstSet(ulong bits) => BitOperations.TrailingZeroCount(bits);
        public static int FindNextSet(ulong bits) => FindFirstSet(bits &= (bits - 1));

        public static IEnumerable<int> EnumerateSets(ulong bits) 
        {
            for (var i = FindFirstSet(bits); bits != 0; i = FindNextSet(bits))
                yield return i;
        }
    }
}
