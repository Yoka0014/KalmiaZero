//#define VALUE_GREEDY_ENGINE
//#define PUCT_ENGINE
//#define PUCT_PERFT
//#define SL
//#define RL
//#define MULTI_RL
//#define MT_RL
//#define SL_GA
//#define TD_GA
#define OUT_GA_RES
//#define CREATE_VALUE_FUNC_FROM_INDIVIDUAL
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

#if PUCT_PERFT
            PUCTPerft.Start(ValueFunction<float>.LoadFromFile(args[0]), Environment.ProcessorCount, 500000, 100);
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
            ValueFunction<float> valueFunc;

            if(args.Length > 0)
                valueFunc = ValueFunction<float>.LoadFromFile(args[0]);
            else
            {
                var nTupleInfos = Enumerable.Range(0, 12).Select(_ => new NTupleInfo(10)).ToArray();
                var nTuples = new NTuples(nTupleInfos);
                valueFunc = new ValueFunction<float>(nTuples);
            }
            var tdTrainer = new TDTrainer<float>("AG01", valueFunc, new TDTrainerConfig<float> { NumEpisodes = 100000000, SaveWeightsInterval = 10000000 });
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

#if MT_RL
            var sw = new Stopwatch();

            ValueFunction<float> valueFunc;

            if (args.Length > 0)
                valueFunc = ValueFunction<float>.LoadFromFile(args[0]);
            else
            {
                var nTupleInfos = Enumerable.Range(0, 12).Select(_ => new NTupleInfo(10)).ToArray();
                var nTuples = new NTuples(nTupleInfos);
                valueFunc = new ValueFunction<float>(nTuples);
            }

            var trainer = new MTTDTrainer<float>(valueFunc, new MTTDTrainerConfig<float>
            {
                NumEpisodes = 5000000,
                NumThreads = 1
            });

            sw.Start();
            trainer.Train();
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
            var ga = new TDGA<float>(new TDGAConfig<float>());
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
        }

        static void DevTest()
        {
            using var sw = new StreamWriter("games.txt");
            var rand = new Random();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            Span<Move> nextMoves = stackalloc Move[Constants.MAX_NUM_MOVES];
            Span<float> moveValues = stackalloc float[Constants.MAX_NUM_MOVES];
            Span<int> moveCandidates = stackalloc int[Constants.MAX_NUM_MOVES];

            var valueFunc = ValueFunction<float>.LoadFromFile("params/value_func_weights.bin");
            var oppValueFunc = ValueFunction<float>.LoadFromFile("params/value_func_weights.bin");
            for (var game = 0; game < 10000; game++)
            {
                var pos = new Position();
                var moveHistory = new List<Move>();
                var featureVec = new PositionFeatureVector(valueFunc.NTuples);
                var oppFeatureVec = new PositionFeatureVector(oppValueFunc.NTuples);
                var numCandidates = 0;

                var numMoves = pos.GetNextMoves(ref moves);
                featureVec.Init(ref pos, moves[..numMoves]);
                oppFeatureVec.Init(ref pos, moves[..numMoves]);

                var passCount = 0;
                while (passCount < 2)
                {
                    if (numMoves == 0)
                    {
                        pos.Pass();
                        numMoves = pos.GetNextMoves(ref moves);
                        featureVec.Pass(moves[..numMoves]);
                        oppFeatureVec.Pass(moves[..numMoves]);
                        moveHistory.Add(Move.Pass);
                        passCount++;
                        (featureVec, oppFeatureVec) = (oppFeatureVec, featureVec);
                        (valueFunc, oppValueFunc) = (oppValueFunc, valueFunc);
                        continue;
                    }

                    passCount = 0;
                    var maxValue = float.NegativeInfinity;
                    for (var i = 0; i < numMoves; i++)
                    {
                        ref var move = ref moves[i];
                        pos.GenerateMove(ref move);
                        pos.Update(ref move);
                        var numNextMoves = pos.GetNextMoves(ref nextMoves);
                        featureVec.Update(ref move, nextMoves[..numNextMoves]);

                        moveValues[i] = 1.0f - valueFunc.Predict(featureVec);
                        if (moveValues[i] > maxValue)
                            maxValue = moveValues[i];

                        pos.Undo(ref move);
                        featureVec.Undo(ref move, moves[..numMoves]);
                    }

                    numCandidates = 0;
                    for (var i = 0; i < numMoves; i++)
                        if (float.Abs(moveValues[i] - maxValue) <= 0.05f)
                            moveCandidates[numCandidates++] = i;

                    var moveChosen = moves[moveCandidates[rand.Next(numCandidates)]];
                    pos.GenerateMove(ref moveChosen);
                    pos.Update(ref moveChosen);
                    numMoves = pos.GetNextMoves(ref moves);
                    featureVec.Update(ref moveChosen, moves[..numMoves]);
                    oppFeatureVec.Update(ref moveChosen, moves[..numMoves]);
                    moveHistory.Add(moveChosen);

                    (featureVec, oppFeatureVec) = (oppFeatureVec, featureVec);
                    (valueFunc, oppValueFunc) = (oppValueFunc, valueFunc);
                }

                foreach (var move in moveHistory)
                    if (move.Coord != BoardCoordinate.Pass)
                        sw.Write(move.Coord);
                sw.WriteLine();
            }
        }
    }
}