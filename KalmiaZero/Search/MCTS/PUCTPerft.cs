using System;
using System.Collections.Generic;
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
        public static void Start(ValueFunctionForTrain<PUCTValueType> valueFunc, uint numPlayouts, int numSamples)
        {
            var rootState = new Position();
            var tree = new PUCT(valueFunc);
            var npsSum = 0.0;
            var ellpasedSum = 0;
            for(var i = 0; i < numSamples; i++)
            {
                tree.SetRootState(ref rootState);
                tree.EnableEarlyStopping = false;
                tree.Search(numPlayouts, int.MaxValue, 0);
                npsSum += tree.Nps;
                ellpasedSum += tree.SearchEllapsedMs;
            }
            Console.WriteLine($"[Result]\nMeanEllapsed: {(double)ellpasedSum / numSamples}[ms]\nSearchSpeed: {npsSum / numSamples}[nps]");
        }
    }
}
