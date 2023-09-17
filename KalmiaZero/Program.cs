using System;

using KalmiaZero.Reversi;
using KalmiaZero.Protocols;
using KalmiaZero.Engines;
using KalmiaZero.Utils;
using KalmiaZero.Evaluation;
using KalmiaZero.Search.MCTS;
using System.IO;
using System.Text;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //PUCTPerft.Start(ValueFunction<PUCTValueType>.LoadFromFile("params/value_func_weights.bin"), 1, 10000, 10);
            var engine = new ValueGreedyEngine();
            var nboard = new NBoard();
            nboard.Mainloop(engine);
        }
    }
}