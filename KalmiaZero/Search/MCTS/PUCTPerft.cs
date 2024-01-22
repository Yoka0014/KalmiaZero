using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;

namespace KalmiaZero.Search.MCTS
{
    internal static class PUCTPerft
    {
        public static void Start(ValueFunction<PUCTValueType> valueFunc, int numThreads, uint numPlayouts, int numSamples)
        {
            Console.WriteLine($"NumPlayouts: {numPlayouts}");
            Console.WriteLine($"NumThreads: {numThreads}");
            Console.WriteLine($"NumSamples: {numSamples}");

            var rootState = new Position();
            var tree = new PUCT(valueFunc);
            tree.NumThreads = numThreads;
            var sw = new Stopwatch();
            sw.Start();
            for (var i = 0; i < numSamples; i++)
            {
                tree.SetRootState(ref rootState);
                tree.EnableEarlyStopping = false;
                tree.Search(numPlayouts, int.MaxValue / 10, 0);
            }
            sw.Stop();
            Console.WriteLine($"[Result]\nMeanEllapsed: {(double)sw.ElapsedMilliseconds / numSamples}[ms]\nSearchSpeed: {numPlayouts * numSamples / (sw.ElapsedMilliseconds * 1.0e-3)}[pps]");
        }
    }
}