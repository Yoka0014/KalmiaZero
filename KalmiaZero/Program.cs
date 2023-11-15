//#define ENGINE
//#define SL
//#define RL
//#define MULTI_RL
#define SL_GA
//#define CHECK_GA_RES

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
using System.Linq;

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
            var nTupleInfos = Enumerable.Range(0, 1).Select(_ => new NTupleInfo(7)).ToArray();

            foreach(var nTuple in nTupleInfos)
                Console.WriteLine(nTuple);

            var nTuples = new NTuples(nTupleInfos);
            var valueFunc = new ValueFunction<float>(nTuples);
            var slTrainer = new SupervisedTrainer<float>("AG01", valueFunc, new SupervisedTrainerConfig<float>());
            (var trainData, var testData) = TrainData.CreateTrainDataFromWTHORFiles("../TrainData/", "WTHOR.JOU", "WTHOR.TRN");
            sw.Start();
            slTrainer.Train(trainData, testData);
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if RL
            var sw = new Stopwatch();
            var nTupleInfos = Enumerable.Range(0, 1).Select(_ => new NTupleInfo(7)).ToArray();

            foreach (var nTuple in nTupleInfos)
                Console.WriteLine(nTuple);

            var nTuples = new NTuples(nTupleInfos);
            var valueFunc = new ValueFunction<float>(nTuples);
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

#if SL_GA
            var sw = new Stopwatch();
            (var trainData, _) = TrainData.CreateTrainDataFromWTHORFiles("../TrainData/", "WTHOR.JOU", "WTHOR.TRN", 0.0);
            var ga = new SupervisedGA<float>(new SupervisedGAConfig<float>());
            sw.Start();
            ga.Train(trainData, 1000);
            sw.Stop();
            Console.WriteLine($"{sw.ElapsedMilliseconds}[ms]");
#endif

#if CHECK_GA_RES
            var nTuplesSet = SupervisedGA<float>.DecodePool(args[0], 7, 1, 10);
            foreach (var nTuples in nTuplesSet)
                foreach (var nTuple in nTuples.Tuples)
                {
                    Console.WriteLine(nTuple);
                    Console.WriteLine();
                }
#endif
        }
    }
}