using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;

namespace KalmiaZero.Learn
{
    public record class TDGAConfig<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public TDTrainerConfig<WeightType> TDConfig { get; init; } = new();
        public GAConfig<WeightType> GAConfig { get; init; } = new();
        public SupervisedTrainerConfig<WeightType> SLConfig { get; init; } = new() { NumEpoch = 20 };

        public int NumTrainData { get; init; } = 10000;
        public int NumTestData { get; init; } = 10000;
        public WeightType TrainDataVariationFactor = WeightType.CreateChecked(0.05);
        public int TestDataMaxRandomMove { get; init; } = 10;
        public int TrainDataUpdateInterval { get; init; } = 100;
        public int NumIterations { get; init; } = 3;
        public int NumThreads { get; init; } = Environment.ProcessorCount;
        public Random Random { get; init; } = new(Random.Shared.Next());

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string PoolFileName { get; init; } = "pool";
        public string FitnessHistoryFileName { get; init; } = "fitness_history";
    }

    public class TDGA<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        readonly TDGAConfig<WeightType> CONFIG;
        readonly string WORK_DIR;
        readonly string POOL_FILE_NAME;
        readonly string FITNESS_HISTROY_FILE_NAME;
        readonly int NTUPLE_SIZE;
        readonly int NUM_NTUPLES;
        readonly int NUM_TRAIN_DATA;
        readonly int NUM_TEST_DATA;
        readonly Random[] RANDS;
        readonly ParallelOptions PARALLEL_OPTIONS;

        public TDGA(TDGAConfig<WeightType> config)
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
            var ga = new GA<WeightType>(gaConfig);

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
                var ga = new GA<WeightType>(gaConfig);

                var nTupleGroups = GA<WeightType>.DecodePool(pool, nTupleSize, numNTuples)[..numElites];

                Console.WriteLine($"Start RL.");
                var valueFuncs = TrainAgents(nTupleGroups);

                Console.WriteLine("Generate games by RL agents.");
                var trainData = GenerateTrainDataByAgents(this.NUM_TRAIN_DATA, valueFuncs);
                var testData = GenerateTrainDataByAgents(this.NUM_TEST_DATA, valueFuncs);

                Console.WriteLine("Start n-tuple optimization.");
                var numGens = Math.Min(this.CONFIG.TrainDataUpdateInterval, genLeft);
                ga.Train(pool, trainData, testData, numGens);
                pool = ga.GetCurrentPool();
                genLeft -= numGens;
                id++;
            }
        }

        ValueFunction<WeightType>[] TrainAgents(NTupleGroup[] nTupleGroups)
        {
            var config = this.CONFIG.TDConfig with { SaveWeightsInterval = int.MaxValue };
            var valueFuncs = nTupleGroups.Select(nt => new ValueFunction<WeightType>(nt)).ToArray();

            Parallel.For(0, valueFuncs.Length, this.PARALLEL_OPTIONS, agentID =>
            {
                var trainer = new TDTrainer<WeightType>($"AG-{agentID}", valueFuncs[agentID], config);
                trainer.Train();
            });

            return valueFuncs;
        }

        TrainData[] GenerateTrainDataByAgents(int numData, ValueFunction<WeightType>[] valueFuncs)
        {
            var trainData = new TrainData[numData];
            var numThreads = this.CONFIG.NumThreads;
            var numGamesPerThread = numData / numThreads;
            Parallel.For(0, numThreads, this.PARALLEL_OPTIONS, threadID =>
                generate(threadID, trainData.AsSpan(numGamesPerThread * threadID, numGamesPerThread)));

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
                    data[i] = GenerateGameByAgent(threadID, new Position(), valueFuncs[ag0], valueFuncs[ag1]);
            }
        }

        TrainData[] GenerateTrainDataFromRandomGame(int numData)
            => Enumerable.Range(0, numData).Select(_ => GenerateRandomGame(new Position())).ToArray();

        TrainData GenerateGameByAgent(int threadID, Position rootPos, ValueFunction<WeightType> valueFuncForBlack, ValueFunction<WeightType> valueFuncForWhite)
        {
            var rand = this.RANDS[threadID];
            var pos = rootPos;
            var moveHistory = new List<Move>();
            var featureVec = new PositionFeatureVector(valueFuncForBlack.NTuples);
            var oppFeatureVec = new PositionFeatureVector(valueFuncForWhite.NTuples);
            var valueFunc = valueFuncForBlack;
            var oppValueFunc = valueFuncForWhite;
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            Span<Move> nextMoves = stackalloc Move[Constants.MAX_NUM_MOVES];
            Span<WeightType> moveValues = stackalloc WeightType[Constants.MAX_NUM_MOVES];
            Span<int> moveCandidates = stackalloc int[Constants.MAX_NUM_MOVES];
            var numCandidates = 0;

            var numMoves = pos.GetNextMoves(ref moves);
            featureVec.Init(ref pos, moves[..numMoves]);
            oppFeatureVec.Init(ref pos, moves[..numMoves]);

            var passCount = 0;
            while(passCount < 2)
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
                var maxValue = WeightType.NegativeInfinity;
                for(var i = 0; i < numMoves; i++)
                {
                    ref var move = ref moves[i];
                    pos.GenerateMove(ref move);
                    pos.Update(ref move);
                    var numNextMoves = pos.GetNextMoves(ref nextMoves);
                    featureVec.Update(ref move, nextMoves[..numNextMoves]);

                    moveValues[i] = WeightType.One - valueFunc.Predict(featureVec);
                    if (moveValues[i] > maxValue)
                        maxValue = moveValues[i];

                    pos.Undo(ref move);
                    featureVec.Undo(ref move, moves[..numMoves]);
                }

                numCandidates = 0;
                for (var i = 0; i < numMoves; i++)
                    if (WeightType.Abs(moveValues[i] - maxValue) <= this.CONFIG.TrainDataVariationFactor)
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

            moveHistory.RemoveRange(moveHistory.Count - 2, 2);  // removes last two passes.

            return new TrainData(rootPos, moveHistory, (sbyte)pos.GetScore(DiscColor.Black));
        }

        TrainData GenerateRandomGame(Position rootPos)
        {
            var rand = this.RANDS[0];
            var pos = new Position(rootPos.GetBitboard(), rootPos.SideToMove);
            var moves = new List<Move>();
            Span<Move> nextMoves = stackalloc Move[Constants.MAX_NUM_MOVES];

            var passCount = 0;
            while(passCount < 2)
            {
                var numMoves = pos.GetNextMoves(ref nextMoves);

                if(numMoves == 0)
                {
                    pos.Pass();
                    moves.Add(Move.Pass);
                    passCount++;
                    continue;
                }

                passCount = 0;
                ref var move = ref nextMoves[rand.Next(numMoves)];
                pos.GenerateMove(ref move);
                pos.Update(ref move);
                moves.Add(move);
            }

            moves.RemoveRange(moves.Count - 2, 2);  // removes last two passes.

            return new TrainData(rootPos, moves, (sbyte)pos.GetScore(DiscColor.Black));
        }
    }
}
