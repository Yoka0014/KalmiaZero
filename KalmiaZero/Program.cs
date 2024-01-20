//#define VALUE_GREEDY_ENGINE
//#define PUCT_ENGINE
//#define ALPHA_BETA_ENGINE
//#define ENDGAME_TEST
#define PUCT_PERFT
//#define SL
//#define RL
//#define MULTI_RL
//#define TDL_RL
//#define SL_GA
//#define TD_GA
//#define OUT_GA_RES
//#define CREATE_VALUE_FUNC_FROM_INDIVIDUAL
//#define EDAX_NTUPLE
//#define DEV_TEST

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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using KalmiaZero.Search.AlphaBeta;
using System.Text.Json;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
#if DEV_TEST
            DevTest();
#endif

#if VALUE_GREEDY_ENGINE
            var engine = new ValueGreedyEngine();

            if (args.Length > 0)
                engine.SetOption("value_func_weights_path", args[0]);

            var nboard = new NBoard();
            nboard.Mainloop(engine);
#endif

#if PUCT_ENGINE
            var engine = new PUCTEngine();

            if (args.Length > 0)
                engine.SetOption("value_func_weights_path", args[0]);

            var nboard = new NBoard();
            nboard.Mainloop(engine);
#endif

#if ALPHA_BETA_ENGINE
            var engine = new AlphaBetaEngine();

            if (args.Length > 0)
                engine.SetOption("value_func_weights_path", args[0]);

            var nboard = new NBoard();
            nboard.Mainloop(engine);
#endif

#if ENDGAME_TEST
            var ttSize = long.Parse(args[1]) * 1024 * 1024;
            EndgameTest.SearchOneEndgame(new Searcher(ValueFunction<float>.LoadFromFile(args[0]), ttSize));
#endif

#if PUCT_PERFT
            PUCTPerft.Start(ValueFunction<float>.LoadFromFile(args[0]), 1, 10000, 100);
#endif

#if SL
            var sw = new Stopwatch();
            ValueFunction<float> valueFunc;
            if (args.Length > 0)
                valueFunc = ValueFunction<float>.LoadFromFile(args[0]);
            else
            {
                var nTupleInfos = Enumerable.Range(0, 12).Select(_ => new NTupleInfo(10)).ToArray();
                var nTuples = new NTupleGroup(nTupleInfos);
                valueFunc = new ValueFunction<float>(nTuples);
            }
            var slTrainer = new SupervisedTrainer<float>("AG01", valueFunc, new SupervisedTrainerConfig<float>());
            //(var trainData, var testData) = TrainData.CreateTrainDataFromWTHORFiles("../TrainData/", "WTHOR.JOU", "WTHOR.TRN");
            var data = TrainData.CreateTrainDataFormF5D6File("egaroucid_selfplay.txt");
            (var trainData, var testData) = TrainData.SplitIntoTrainAndTest(data, 0.01);
            sw.Start();
            slTrainer.Train(trainData, testData);
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if RL
            var sw = new Stopwatch();
            ValueFunction<float> valueFunc;

            if(args.Length > 0)
                valueFunc = ValueFunction<float>.LoadFromFile(args[0]);
            else
            {
                var nTupleInfos = Enumerable.Range(0, 12).Select(_ => new NTupleInfo(10)).ToArray();
                var nTuples = new NTupleGroup(nTupleInfos);
                valueFunc = new ValueFunction<float>(nTuples);
            }

            if (args.Length > 1 && args[1] == "zero")
                valueFunc.InitWeightsWithNormalRand(0.0f, 0.0f);

            var tdTrainer = new TDTrainer<float>("AG01", valueFunc, new TDTrainerConfig<float> { NumEpisodes = 5000000, SaveWeightsInterval = 10000, HorizonCutFactor =  0.1f, EligibilityTraceFactor = 0.9f });
            sw.Start();
            tdTrainer.Train();
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if MULTI_RL
            var sw = new Stopwatch();
            sw.Start();
            TDTrainer<float>.TrainMultipleAgents(Environment.CurrentDirectory,
                new TDTrainerConfig<float> { NumEpisodes = 5000000, SaveWeightsInterval = 1000000 }, 20, 10, 12);
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if TDL_RL
            var sw = new Stopwatch();
            ValueFunction<MiniMaxType> valueFunc;

            if (args.Length > 0)
                valueFunc = ValueFunction<MiniMaxType>.LoadFromFile(args[0]);
            else
            {
                var nTupleInfos = Enumerable.Range(0, 12).Select(_ => new NTupleInfo(10)).ToArray();
                var nTuples = new NTupleGroup(nTupleInfos);
                valueFunc = new ValueFunction<MiniMaxType>(nTuples);
            }

            if (args.Length > 1 && args[1] == "zero")
                valueFunc.InitWeightsWithNormalRand(0.0f, 0.0f);

            var tdTrainer = new TDLeafTrainer("AG01", valueFunc, new TDLeafTrainerConfig() { NumEpisodes = 5000000, EligibilityTraceFactor = 0.7f });
            sw.Start();
            tdTrainer.Train();
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
#endif

#if SL_GA
            var sw = new Stopwatch();
            (var trainData, var testData) = TrainData.CreateTrainDataFromWTHORFiles("../TrainData/", "WTHOR.JOU", "WTHOR.TRN", numData: 20000, 0.5);
            var ga = new SupervisedGA<float>(new SupervisedGAConfig<float>() { SLConfig = new SupervisedTrainerConfig<float>() {NumEpoch = 20} });
            sw.Start();
            if (args.Length > 0)
                ga.Train(args[0], trainData, testData, 1000);
            else
                ga.Train(trainData, testData, 1000);
            sw.Stop();
            Console.WriteLine($"{sw.ElapsedMilliseconds}[ms]");
#endif

#if TD_GA
            var sw = new Stopwatch();
            var ga = new TDGA(new TDGAConfig() { TDConfig = new TDTrainerConfig<float> { NumEpisodes = 5000000 }, NumTrainData = 10000, NumTestData = 10000 });
            sw.Start();
            if (args.Length > 0)
                ga.Train(args[0], 1000);
            else
                ga.Train(1000);
            sw.Stop();
            Console.WriteLine($"{sw.ElapsedMilliseconds}[ms]");
#endif

#if OUT_GA_RES
            var nTuplesGroups = GA<float>.DecodePool(args[0], 10, 12, 10);
            using var sw = new StreamWriter("ntuples.txt");
            var nTuples = nTuplesGroups[0].Tuples;
            foreach(var nTuple in nTuples)
            {
                sw.Write('[');
                for(var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
                {
                    if (nTuple.GetCoordinates(0).Contains(coord))
                        sw.Write(1);
                    else
                        sw.Write(0);

                    if(coord != BoardCoordinate.H8)
                        sw.Write(',');
                }
                sw.WriteLine(']');
            }
#endif

#if CREATE_VALUE_FUNC_FROM_INDIVIDUAL
            var nTuplesSet = GA<float>.DecodePool(args[0], 10, 12, 10);
            var idx = int.Parse(args[1]);
            var nTuples = nTuplesSet[idx];
            var path = (args.Length >= 3) ? args[2] : "value_func_ga.bin";
            new ValueFunction<float>(nTuples).SaveToFile(path);
#endif

#if EDAX_NTUPLE
            var edaxNTuples = new NTupleInfo[12]
            {
                // corner3x3 
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A1, BoardCoordinate.B1, BoardCoordinate.A2, BoardCoordinate.B2, BoardCoordinate.C1, BoardCoordinate.A3, BoardCoordinate.C2, BoardCoordinate.B3, BoardCoordinate.C3 }),
                // corner edge x 
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A5, BoardCoordinate.A4, BoardCoordinate.A3, BoardCoordinate.A2, BoardCoordinate.A1, BoardCoordinate.B2, BoardCoordinate.B1, BoardCoordinate.C1, BoardCoordinate.D1, BoardCoordinate.E1 }),
                // edge 2x 
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.B2, BoardCoordinate.A1, BoardCoordinate.B1, BoardCoordinate.C1, BoardCoordinate.D1, BoardCoordinate.E1, BoardCoordinate.F1, BoardCoordinate.G1, BoardCoordinate.H1, BoardCoordinate.G2 }),
                // edge4x2 2x 
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A1, BoardCoordinate.C1, BoardCoordinate.D1, BoardCoordinate.C2, BoardCoordinate.D2, BoardCoordinate.E2, BoardCoordinate.F2, BoardCoordinate.E1, BoardCoordinate.F1, BoardCoordinate.H1 }),
                // horizontal and vertical line (row = 2 or column = 2)
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A2, BoardCoordinate.B2, BoardCoordinate.C2, BoardCoordinate.D2, BoardCoordinate.E2, BoardCoordinate.F2, BoardCoordinate.G2, BoardCoordinate.H2 }),
                // horizontal and vertical line (row = 3 or column = 3)
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A3, BoardCoordinate.B3, BoardCoordinate.C3, BoardCoordinate.D3, BoardCoordinate.E3, BoardCoordinate.F3, BoardCoordinate.G3, BoardCoordinate.H3 }),
                // horizontal and vertical line (row = 4 or column = 4)
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A4, BoardCoordinate.B4, BoardCoordinate.C4, BoardCoordinate.D4, BoardCoordinate.E4, BoardCoordinate.F4, BoardCoordinate.G4, BoardCoordinate.H4 }),
                // diagonal line 8
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.A1, BoardCoordinate.B2, BoardCoordinate.C3, BoardCoordinate.D4, BoardCoordinate.E5, BoardCoordinate.F6, BoardCoordinate.G7, BoardCoordinate.H8 }),
                // diagonal line 7
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.B1, BoardCoordinate.C2, BoardCoordinate.D3, BoardCoordinate.E4, BoardCoordinate.F5, BoardCoordinate.G6, BoardCoordinate.H7 }),
                // diagonal line 6
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.C1, BoardCoordinate.D2, BoardCoordinate.E3, BoardCoordinate.F4, BoardCoordinate.G5, BoardCoordinate.H6 }),
                // diagonal line 5
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.D1, BoardCoordinate.E2, BoardCoordinate.F3, BoardCoordinate.G4, BoardCoordinate.H5 }),
                // diagonal line 4
                new NTupleInfo(new BoardCoordinate[] { BoardCoordinate.D1, BoardCoordinate.C2, BoardCoordinate.B3, BoardCoordinate.A4 }),
            };

            var nTuples = new NTupleGroup(edaxNTuples);
            var valueFunc = new ValueFunction<float>(nTuples, 4);
            valueFunc.SaveToFile("value_func_edax.bin");
#endif
        }

        static void DevTest()
        {
            var valueFunc = ValueFunction<float>.LoadFromFile("value_func_weights_sl_latest.bin");
            var pos = new Position();
            var pfv = new PositionFeatureVector(valueFunc.NTuples);
            pfv.Init(ref pos, Span<Move>.Empty);
            Console.WriteLine(valueFunc.Predict(pfv));

            pos.Update(BoardCoordinate.F5);
            pfv.Init(ref pos, Span<Move>.Empty);
            Console.WriteLine(valueFunc.Predict(pfv));
        }
    }
}