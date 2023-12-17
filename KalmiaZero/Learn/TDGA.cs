using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Search.MCTS;
using KalmiaZero.Utils;

namespace KalmiaZero.Learn
{
    public record class TDGAConfig 
    {
        public TDTrainerConfig<PUCTValueType> TDConfig { get; init; } = new();
        public GAConfig<PUCTValueType> GAConfig { get; init; } = new();
        public SupervisedTrainerConfig<PUCTValueType> SLConfig { get; init; } = new() { NumEpoch = 20 };

        public int NumTrainData { get; init; } = 10000;
        public int NumTestData { get; init; } = 10000;
        public PUCTValueType TrainDataVariationFactor = (PUCTValueType)0.05;
        public uint NumPlayouts { get; init; } = 3200;
        public int TrainDataUpdateInterval { get; init; } = 100;
        public int NumIterations { get; init; } = 3;
        public int NumThreads { get; init; } = Environment.ProcessorCount;
        public Random Random { get; init; } = new(Random.Shared.Next());

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string PoolFileName { get; init; } = "pool";
        public string FitnessHistoryFileName { get; init; } = "fitness_history";
    }

    public class TDGA
    {
        readonly TDGAConfig CONFIG;
        readonly string WORK_DIR;
        readonly string POOL_FILE_NAME;
        readonly string FITNESS_HISTROY_FILE_NAME;
        readonly int NTUPLE_SIZE;
        readonly int NUM_NTUPLES;
        readonly int NUM_TRAIN_DATA;
        readonly int NUM_TEST_DATA;
        readonly Random[] RANDS;
        readonly ParallelOptions PARALLEL_OPTIONS;

        public TDGA(TDGAConfig config)
        {
            this.CONFIG = config;
            this.NTUPLE_SIZE = config.GAConfig.NTupleSize;
            this.NUM_NTUPLES = config.GAConfig.NumNTuples;
            this.NUM_TRAIN_DATA = config.NumTrainData;
            this.NUM_TEST_DATA = config.NumTestData;

            this.WORK_DIR = config.WorkDir;
            this.POOL_FILE_NAME = $"{config.PoolFileName}_{"{0}"}";
            this.FITNESS_HISTROY_FILE_NAME = $"{config.FitnessHistoryFileName}_{"{0}"}";
            this.RANDS = Enumerable.Range(0, config.NumThreads).Select(_ => new Random(config.Random.Next())).ToArray();
            this.PARALLEL_OPTIONS = new ParallelOptions { MaxDegreeOfParallelism = config.NumThreads };
        }

        public void Train(int numGenerations)
        {
            var gaConfig = this.CONFIG.GAConfig with
            {
                WorkDir = this.WORK_DIR,
                PoolFileName = string.Format(this.POOL_FILE_NAME, 0),
                FitnessHistroyFileName = string.Format(this.FITNESS_HISTROY_FILE_NAME, 0)
            };
            var ga = new GA<PUCTValueType>(gaConfig);

            Console.WriteLine("Generate random play games.");

            var trainData = GenerateTrainDataFromRandomGame(this.NUM_TRAIN_DATA);
            var testData = GenerateTrainDataFromRandomGame(this.NUM_TEST_DATA);

            Console.WriteLine("Start n-tuple optimization with random play games.");

            var numGens = Math.Min(this.CONFIG.TrainDataUpdateInterval, numGenerations);
            ga.Train(trainData, testData, numGens);
            StartTrainLoop(numGenerations - numGens, ga.GetCurrentPool());
        }

        public void Train(string poolPath, int numGenerations)
        {
            var pool = Individual.LoadPoolFromFile(poolPath);
            StartTrainLoop(numGenerations, pool);
        }

        void StartTrainLoop(int numGenerations, Individual[] initialPool)
        {
            var pool = initialPool;
            var nTupleSize = this.CONFIG.GAConfig.NTupleSize;
            var numNTuples = this.CONFIG.GAConfig.NumNTuples;
            var numElites = (int)(this.CONFIG.GAConfig.EliteRate * this.CONFIG.GAConfig.PopulationSize);
            var genLeft = numGenerations;

            var id = 1;
            while (genLeft > 0)
            {
                Console.WriteLine($"\nGenerations left: {genLeft}");

                var gaConfig = this.CONFIG.GAConfig with
                {
                    WorkDir = this.WORK_DIR,
                    PoolFileName = string.Format(this.POOL_FILE_NAME, id),
                    FitnessHistroyFileName = string.Format(this.FITNESS_HISTROY_FILE_NAME, id)
                };
                var ga = new GA<PUCTValueType>(gaConfig);

                var nTupleGroups = GA<PUCTValueType>.DecodePool(pool, nTupleSize, numNTuples)[..numElites];

                Console.WriteLine("Start RL.");
                var valueFuncs = TrainAgents(nTupleGroups);

                Console.WriteLine("Generate train data with MCTS.");
                var trainData = GenerateTrainDataWithMCTS(this.NUM_TRAIN_DATA, valueFuncs);

                Console.WriteLine("Generate test data with MCTS.");
                var testData = GenerateTrainDataWithMCTS(this.NUM_TEST_DATA, valueFuncs);

                // debug
                using (var sw = new StreamWriter("train_data.txt"))
                {
                    foreach (var data in trainData)
                    {
                        foreach (var move in data.Moves)
                            if (move.Coord != BoardCoordinate.Pass)
                                sw.Write(move.Coord);
                        sw.WriteLine();
                    }
                }

                Console.WriteLine("Start n-tuple optimization.");
                var numGens = Math.Min(this.CONFIG.TrainDataUpdateInterval, genLeft);
                ga.Train(pool, trainData, testData, numGens);
                pool = ga.GetCurrentPool();
                genLeft -= numGens;
                id++;
            }
        }

        ValueFunction<PUCTValueType>[] TrainAgents(NTupleGroup[] nTupleGroups)
        {
            var config = this.CONFIG.TDConfig with { SaveWeightsInterval = int.MaxValue };
            var valueFuncs = nTupleGroups.Select(nt => new ValueFunction<PUCTValueType>(nt)).ToArray();

            Parallel.For(0, valueFuncs.Length, this.PARALLEL_OPTIONS, agentID =>
            {
                var trainer = new TDTrainer<PUCTValueType>($"AG-{agentID}", valueFuncs[agentID], config);
                trainer.Train();
            });

            return valueFuncs;
        }

        TrainData[] GenerateTrainDataWithMCTS(int numData, ValueFunction<PUCTValueType>[] valueFuncs)
        {
            var trainData = new TrainData[numData];
            var numThreads = this.CONFIG.NumThreads;
            var numGamesPerThread = numData / numThreads;
            var count = 0;
            Parallel.For(0, numThreads, this.PARALLEL_OPTIONS, 
                threadID => generate(threadID, trainData.AsSpan(numGamesPerThread * threadID, numGamesPerThread)));

            generate(0, trainData.AsSpan(numGamesPerThread * numThreads, numData % numThreads));

            return trainData;

            void generate(int threadID, Span<TrainData> data)
            {
                var rand = this.RANDS[threadID];
                var ag0 = rand.Next(valueFuncs.Length);
                int ag1;
                do
                    ag1 = rand.Next(valueFuncs.Length);
                while (ag0 == ag1);

                for (var i = 0; i < data.Length; i++)
                {
                    data[i] = GenerateGameWithMCTS(threadID, new Position(), valueFuncs[ag0], valueFuncs[ag1]);
                    Interlocked.Increment(ref count);
                    Console.WriteLine($"[{count} / {numData}]");
                }
            }
        }

        TrainData[] GenerateTrainDataFromRandomGame(int numData)
            => Enumerable.Range(0, numData).Select(_ => GenerateRandomGame(new Position())).ToArray();

        TrainData GenerateGameWithMCTS(int threadID, Position rootPos, ValueFunction<PUCTValueType> valueFuncForBlack, ValueFunction<PUCTValueType> valueFuncForWhite)
        {
            var rand = this.RANDS[threadID];
            var pos = rootPos;
            var moveHistory = new List<Move>();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var moveEvals = new MoveEvaluation[Constants.MAX_NUM_MOVES];

            var puct = new PUCT(valueFuncForBlack);
            var oppPUCT = new PUCT(valueFuncForWhite);
            puct.SetRootState(ref pos);
            oppPUCT.SetRootState(ref pos);
            var numMoves = pos.GetNextMoves(ref moves);

            var passCount = 0;
            while(passCount < 2)
            {
                if (numMoves == 0)
                {
                    pos.Pass();
                    numMoves = pos.GetNextMoves(ref moves);
                    moveHistory.Add(Move.Pass);
                    passCount++;

                    updatePUCT(puct, ref pos, BoardCoordinate.Pass);
                    updatePUCT(oppPUCT, ref pos, BoardCoordinate.Pass);
                    (puct, oppPUCT) = (oppPUCT, puct);
                    continue;
                }

                passCount = 0;
                Move move;
                if (numMoves == 1)
                    move = moves[0];
                else
                {
                    puct.SearchOnSingleThread(this.CONFIG.NumPlayouts);
                    var searchInfo = puct.CollectSearchInfo();

                    Debug.Assert(searchInfo is not null);

                    searchInfo.ChildEvals.CopyTo(moveEvals);
                    var bestValue = moveEvals[0].ExpectedReward;
                    var numCandidates = 0;
                    var playoutCount = 0u;
                    for(var i = 0; i < numMoves; i++) 
                    {
                        if (bestValue - moveEvals[i].ExpectedReward <= this.CONFIG.TrainDataVariationFactor)
                        {
                            (moveEvals[numCandidates], moveEvals[i]) = (moveEvals[i], moveEvals[numCandidates]);
                            playoutCount += moveEvals[numCandidates].PlayoutCount;
                            numCandidates++;
                        }
                    }
                    rand.Shuffle(moveEvals.AsSpan(0, numCandidates));

                    var arrow = playoutCount * rand.NextDouble();
                    var sum = 0u;
                    var idx = -1;
                    do
                        sum += moveEvals[++idx].PlayoutCount;
                    while (sum < arrow);
                    move = new Move(moveEvals[idx].Move);
                }

                pos.GenerateMove(ref move);
                pos.Update(ref move);
                numMoves = pos.GetNextMoves(ref moves);

                updatePUCT(puct, ref pos, move.Coord);
                updatePUCT(oppPUCT, ref pos, move.Coord);
                (puct, oppPUCT) = (oppPUCT, puct);
                moveHistory.Add(move);
            }

            moveHistory.RemoveRange(moveHistory.Count - 2, 2);  // removes last two passes.

            return new TrainData(rootPos, moveHistory, (sbyte)pos.GetScore(DiscColor.Black));

            static void updatePUCT(PUCT puct, ref Position pos, BoardCoordinate move)
            {
                if (!puct.TransitionRootStateToChildState(move))
                    puct.SetRootState(ref pos);
            }
        }

        TrainData GenerateRandomGame(Position rootPos)
        {
            var rand = this.RANDS[0];
            var pos = new Position(rootPos.GetBitboard(), rootPos.SideToMove);
            var moveHistory = new List<Move>();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];

            var passCount = 0;
            while(passCount < 2)
            {
                var numMoves = pos.GetNextMoves(ref moves);

                if(numMoves == 0)
                {
                    pos.Pass();
                    moveHistory.Add(Move.Pass);
                    passCount++;
                    continue;
                }

                passCount = 0;
                ref var move = ref moves[rand.Next(numMoves)];
                pos.GenerateMove(ref move);
                pos.Update(ref move);
                moveHistory.Add(move);
            }

            moveHistory.RemoveRange(moveHistory.Count - 2, 2);  // removes last two passes.

            return new TrainData(rootPos, moveHistory, (sbyte)pos.GetScore(DiscColor.Black));
        }
    }
}
