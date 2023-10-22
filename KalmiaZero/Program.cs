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
            //var engine = new PUCTEngine();
            //var nboard = new NBoard();
            //nboard.Mainloop(engine);

            //var valueFunc = ValueFunction<float>.LoadFromFile("value_func_weights_td_249999.bin");
            //var pos = new Position();
            //var pfv = new PositionFeatureVector(valueFunc.NTuples);
            //Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            //var num = pos.GetNextMoves(ref moves);
            //pfv.Init(ref pos, moves[..num]);
            //Console.WriteLine(valueFunc.Predict(pfv));

            var sw = new Stopwatch();
            var valueFunc = ValueFunction<float>.LoadFromFile("params/value_func_weights.bin");
            valueFunc.InitWeightsWithNormalRand(0.0f, 0.0001f);
            var tdTrainer = new TDTrainer<float>("AG01", valueFunc, new TDTrainerConfig<float> { NumEpisodes = 250_000, SaveWeightsInterval = 10000 });
            sw.Start();
            tdTrainer.Train();
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);

            //var sw = new Stopwatch();
            //TDTrainer<float>.TrainMultipleAgents(Environment.CurrentDirectory,
            //    new TDTrainerConfig<float> { NumEpisodes = 250_000, SaveWeightsInterval = 10000 }, 20, 7, 100);
            //Console.WriteLine(sw.ElapsedMilliseconds);
        }
    }
}