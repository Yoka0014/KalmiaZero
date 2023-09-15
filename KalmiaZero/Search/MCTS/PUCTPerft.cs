﻿using System;
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
        public static void Start(ValueFunction valueFunc, int numThreads, uint numPlayouts, int numSamples)
        {
            var rootState = new Position();
            var tree = new PUCT(valueFunc);
            tree.EnableEarlyStopping = false;
            tree.NumThreads = numThreads;

            var npsSum = 0.0;
            var ellpasedSum = 0;
            for(var i = 0; i < numSamples; i++)
            {
                tree.SetRootState(ref rootState);
                tree.Search(numPlayouts, int.MaxValue / 10, 0);
                npsSum += tree.Nps;
                ellpasedSum += tree.SearchEllapsedMs;
            }
            Console.WriteLine($"[Result]\nMeanEllapsed: {(double)ellpasedSum / numSamples}[ms]\nSearchSpeed: {npsSum / numSamples}[nps]");
        }
    }
}