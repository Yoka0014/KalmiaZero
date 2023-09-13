using System;

using KalmiaZero.Protocols;
using KalmiaZero.Engines;
using KalmiaZero.Utils;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var engine = new PUCTEngine();
            var nboard = new NBoard();
            nboard.Mainloop(engine);

            //var nTuples = (from _ in Enumerable.Range(0, 100) select new NTupleInfo(7)).ToArray();
            //var valueFunc = new ValueFunction<double>(new NTuples(nTuples));
            //var options = new ValueFuncOptimizerOptions<double>
            //{
            //    NumEpoch = 1000,
            //    LearningRate = 0.01,
            //    Epsilon = 1.0e-6,
            //    TrainDataPath = "../../../../../TrainData/train_data.bin",
            //    TestDataPath = "../../../../../TrainData/test_data.bin"
            //};
            //var optimizer = new ValueFuncOptimizer<double>("../../../../../TrainWorkDir", valueFunc, options);
            //optimizer.StartOptimization();
        }
    }
}