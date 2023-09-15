using System;

using KalmiaZero.Protocols;
using KalmiaZero.Engines;
using KalmiaZero.Utils;
using KalmiaZero.Evaluation;
using KalmiaZero.Search.MCTS;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            PUCTPerft.Start(ValueFunctionForTrain<PUCTValueType>.LoadFromFile("params/value_func_weights.bin"), 10000, 10);
            //var engine = new PUCTEngine();
            //var nboard = new NBoard();
            //nboard.Mainloop(engine);
        }
    }
}