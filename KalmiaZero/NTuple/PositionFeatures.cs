using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.NTuple
{
    using static PositionFeaturesConstantConfig;

    public static class PositionFeaturesConstantConfig 
    {
        public const int NUM_SQUARE_STATES = 4;     // BLACK, WHITE, REACHEABLE_EMPTY and UNREACHABLE_EMPTY
        //public const int NUM_SQUARE_STATES = 3;   // BLACK, WHITE and EMPTY

        public const int BLACK = 0;
        public const int WHITE = 1;
        public const int REACHABLE_EMPTY = 2;
        public const int UNREACHABLE_EMPTY = 3;
    }

    /// <summary>
    /// Represents features of n-tuples in a position.
    /// </summary>
    /// <typeparam name="FeatureType">An integer type to represent a feature.</typeparam>
    public class PositionFeatures
    {
        
    }
}
