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

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var valueFunc = ValueFunction<float>.LoadFromFile("params/value_func_weights.bin");
            valueFunc.InitWeightsWithNormalRand(0.0f, 0.0001f);
            var tdTrainer = new TDTrainer<float>(valueFunc, new TDTrainerConfig<float>());
            tdTrainer.Train();
        }
    }
}