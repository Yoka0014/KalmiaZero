using System;
using System.Collections.Generic;
using System.Text.Json;

using KalmiaZero.Engines;
using KalmiaZero.GameFormats;
using KalmiaZero.NTuple;
using KalmiaZero.Protocols;
using KalmiaZero.Reversi;
using KalmiaZero.Learning;
using KalmiaZero.Evaluation;
using System.Linq;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var nTuples = (from _ in Enumerable.Range(0, 100) select new NTupleInfo(7)).ToArray();
            var valueFunc = new ValueFunction<double>(new NTuples(nTuples));
            var options = new ValueFuncOptimizerOptions<double>
            {
                NumEpoch = 30,
                LearningRate = 0.01,
                Epsilon = 1.0e-6,
                TrainDataPath = "../../../../../TrainData/train_data.bin",
                TestDataPath = "../../../../../TrainData/test_data.bin"
            };
            var optimizer = new ValueFuncOptimizer<double>("../../../../../TrainWorkDir", valueFunc, options);
            optimizer.StartOptimization();
        }
    }
}