//#define ENGINE
#define SL
//#define RL
//#define MULTI_RL

using System;

using KalmiaZero.Reversi;
using KalmiaZero.Protocols;
using KalmiaZero.Engines;
using KalmiaZero.Utils;
using KalmiaZero.Evaluation;
using KalmiaZero.Search.MCTS;
using System.IO;
using System.Text;
using KalmiaZero.Learn;
using KalmiaZero.Search;
using KalmiaZero.NTuple;
using System.Diagnostics;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
#if ENGINE
            var engine = new PUCTEngine();
            var nboard = new NBoard();
            nboard.Mainloop(engine);
#endif

#if SL
            var sw = new Stopwatch();
            var valueFunc = ValueFunction<float>.LoadFromFile("params/value_func_weights.bin");
            valueFunc.InitWeightsWithNormalRand(0.0f, 0.0f);
            var slTrainer = new SupervisedTrainer<float>("AG01", valueFunc, new SupervisedTrainerConfig<float>());
            (var trainData, var testData) = TrainData.CreateTrainDataFromWTHORFiles("../TrainData/", "WTHOR.JOU", "WTHOR.TRN");
            sw.Start();
            slTrainer.Train(trainData, testData);
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if RL
            var sw = new Stopwatch();
            var valueFunc = ValueFunction<float>.LoadFromFile("params/value_func_weights.bin");
            valueFunc.InitWeightsWithNormalRand(0.0f, 0.0f);
            var tdTrainer = new TDTrainer<float>("AG01", valueFunc, new TDTrainerConfig<float> { NumEpisodes = 250_000, SaveWeightsInterval = 10000 });
            sw.Start();
            tdTrainer.Train();
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if MULTI_RL
            var sw = new Stopwatch();
            sw.Start();
            TDTrainer<float>.TrainMultipleAgents(Environment.CurrentDirectory,
                new TDTrainerConfig<float> { NumEpisodes = 250_000, SaveWeightsInterval = 10000 }, 24, 7, 100);
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif
        }
    }
}