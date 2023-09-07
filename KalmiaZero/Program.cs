using System;

using KalmiaZero.Learning;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(TrainData.LoadFromFile(@"C:\Users\yu_ok\source\repos\KalmiaZero\TrainData\test_data.bin").Length);
            //var engine = new MCEngine();
            //engine.SetOption("NumPlayouts", "100");

            //var engine = new ValueGreedyEngine();
            //engine.SetOption("WeightsFilePath", @"C:\Users\yu_ok\source\repos\KalmiaZero\Weights\value_func_weights.bin");

            //var nboard = new NBoard();
            //nboard.Mainloop(engine);

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