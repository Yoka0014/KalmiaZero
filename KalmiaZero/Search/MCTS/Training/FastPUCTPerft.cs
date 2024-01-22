using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;

namespace KalmiaZero.Search.MCTS.Training
{
    internal static class FastPUCTPerft
    {
        public static void Start(ValueFunction<PUCTValueType> valueFunc, int numPlayouts, int numSamples)
        {
            Console.WriteLine($"NumPlayouts: {numPlayouts}");
            Console.WriteLine($"NumSamples: {numSamples}");

            var rootState = new Position();
            var tree = new FastPUCT(valueFunc, numPlayouts);
            var sw = new Stopwatch();
            sw.Start();
            for (var i = 0; i < numSamples; i++)
            {
                tree.SetRootState(ref rootState);
                tree.Search();
            }
            sw.Stop();
            Console.WriteLine($"[Result]\nMeanEllapsed: {(double)sw.ElapsedMilliseconds / numSamples}[ms]\nSearchSpeed: {numPlayouts * numSamples / (sw.ElapsedMilliseconds * 1.0e-3)}[pps]");
        }
    }
}