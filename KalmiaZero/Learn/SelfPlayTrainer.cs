using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MathNet.Numerics.Distributions;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;
using KalmiaZero.Search.MCTS.Training;

namespace KalmiaZero.Learn
{
    public class SelfPlayTrainerConfig
    {
        public int NumActors { get; init; } = Environment.ProcessorCount;

        public int NumSamplingMoves { get; init; } = 30;
        public int NumSimulations { get; init; } = 800;
        public double RootDirichletAlpha { get; init; } = 0.3;
        public double RootExplorationFraction { get; init; } = 0.25;

        public int NumTrainingThreads = Environment.ProcessorCount;
        public int NumGamesInBatch { get; init; } = 500_000;
        public int NumEpoch { get; init; } = 200;
        public bool StartWithRandomTrainData { get; init; } = true;
        public PUCTValueType LearningRate { get; init; } = (PUCTValueType)0.001;
        public string WeightsFileName { get; init; } = "value_func_weights_sp";
    }

    public class SelfPlayTrainer
    {
        readonly SelfPlayTrainerConfig CONFIG;
        readonly List<TrainData> trainDataSet = new();
        readonly Random[] RANDS;
        readonly StreamWriter logger;

        public SelfPlayTrainer(SelfPlayTrainerConfig config) : this(config, Stream.Null) { }

        public SelfPlayTrainer(SelfPlayTrainerConfig config, Stream logStream)
        {
            this.CONFIG = config;
            this.RANDS = Enumerable.Range(0, config.NumActors).Select(_ => new Random(Random.Shared.Next())).ToArray();
            this.logger = new StreamWriter(logStream) { AutoFlush = true };
        }

        public void Train(ValueFunction<PUCTValueType> valueFunc, int numIterations)
        {
            for (var i = 0; i < numIterations; i++)
            {
                if (i == 0 && this.CONFIG.StartWithRandomTrainData)
                    GenerateTrainDataSetWithRandomPlay();
                else
                    GenerateTrainDataSetWithSelfPlay(valueFunc);

                new SupervisedTrainer<PUCTValueType>(valueFunc,
                    new SupervisedTrainerConfig<PUCTValueType>()
                    {
                        LearningRate = this.CONFIG.LearningRate,
                        NumEpoch = this.CONFIG.NumEpoch,
                        WeightsFileName = $"{this.CONFIG.WeightsFileName}_{i}"
                    }).Train(this.trainDataSet.ToArray(), Array.Empty<TrainData>());
            }
        }

        void GenerateTrainDataSetWithRandomPlay()
        {
            this.trainDataSet.Clear();
            for (var i = 0; i < this.CONFIG.NumGamesInBatch; i++)
                this.trainDataSet.Add(GenerateRandomGame(new Position()));
        }

        void GenerateTrainDataSetWithSelfPlay(ValueFunction<PUCTValueType> valueFunc)
        {
            this.trainDataSet.Clear();

            var numActors = this.CONFIG.NumActors;
            var trees = Enumerable.Range(0, numActors).Select(_ => new FastPUCT(valueFunc, this.CONFIG.NumSimulations)
            {
                RootDirichletAlpha = this.CONFIG.RootDirichletAlpha,
                RootExplorationFraction = this.CONFIG.RootExplorationFraction
            }).ToArray();
            var numGamesPerActor = this.CONFIG.NumGamesInBatch / numActors;

            this.logger.WriteLine("Start self-play.");
            Parallel.For(0, numActors, genData);

            var numGamesLeft = this.CONFIG.NumGamesInBatch % numActors;
            Parallel.For(0, numGamesLeft, genData);

            void genData(int actorID)
            {
                var tree = trees[actorID];
                var rand = this.RANDS[actorID];
                for (var i = 0; i < numGamesPerActor; i++)
                {
                    var data = GenerateTrainDataWithMCTS(new Position(), tree, rand);
                    lock (this.trainDataSet)
                        this.trainDataSet.Add(data);
                    this.logger.WriteLine($"[{this.trainDataSet.Count}/{this.CONFIG.NumGamesInBatch}]");
                }
            }
        }

        TrainData GenerateRandomGame(Position rootPos)
        {
            var rand = this.RANDS[0];
            var pos = new Position(rootPos.GetBitboard(), rootPos.SideToMove);
            var moveHistory = new List<Move>();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];

            var passCount = 0;
            while (passCount < 2)
            {
                var numMoves = pos.GetNextMoves(ref moves);

                if (numMoves == 0)
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

        TrainData GenerateTrainDataWithMCTS(Position rootPos, FastPUCT tree, Random rand)
        {
            var pos = rootPos;
            var moveHistory = new List<Move>();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var numSampleMoves = this.CONFIG.NumSamplingMoves;

            tree.SetRootState(ref pos);

            var passCount = 0;
            var moveCount = 0;
            while (passCount < 2)
            {
                var numMoves = pos.GetNextMoves(ref moves);

                if (numMoves == 0)
                {
                    pos.Pass();
                    moveHistory.Add(Move.Pass);
                    passCount++;

                    tree.PassRootState();
                    continue;
                }

                tree.Search();

                Move move;
                if (moveCount < numSampleMoves)
                    move = tree.SelectMoveWithVisitCountDist(rand);
                else
                    move = tree.SelectMaxVisitMove();

                passCount = 0;
                pos.GenerateMove(ref move);
                pos.Update(ref move);
                moveHistory.Add(move);
                moveCount++;

                tree.UpdateRootState(ref move);
            }

            moveHistory.RemoveRange(moveHistory.Count - 2, 2);  // removes last two passes.

            return new TrainData(rootPos, moveHistory, (sbyte)pos.GetScore(DiscColor.Black));
        }
    }
}
