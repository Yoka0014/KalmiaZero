using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        const int SHOW_LOG_INTERVAL_MS = 1000;

        readonly SelfPlayTrainerConfig CONFIG;
        readonly ConcurrentQueue<TrainData> trainDataSet = new();
        readonly Random[] RANDS;
        readonly StreamWriter logger;

        public SelfPlayTrainer(SelfPlayTrainerConfig config) : this(config, Stream.Null) { }

        public SelfPlayTrainer(SelfPlayTrainerConfig config, Stream logStream)
        {
            this.CONFIG = config;
            this.RANDS = Enumerable.Range(0, config.NumActors).Select(_ => new Random(Random.Shared.Next())).ToArray();
            this.logger = new StreamWriter(logStream) { AutoFlush = false };
        }

        ~SelfPlayTrainer() => this.logger.Dispose();

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
                this.trainDataSet.Enqueue(GenerateRandomGame(new Position()));
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
            this.logger.WriteLine($"The number of actors: {this.CONFIG.NumActors}");
            this.logger.WriteLine($"the number of MCTS simulations: {this.CONFIG.NumSimulations}");
            this.logger.Flush();

            var logTask = Task.Run(() =>
            {
                while (true)
                {
                    var count = this.trainDataSet.Count;
                    this.logger.WriteLine($"[{count}/{this.CONFIG.NumGamesInBatch}]");
                    this.logger.Flush();

                    if (count == this.CONFIG.NumGamesInBatch)
                        break;

                    Thread.Sleep(SHOW_LOG_INTERVAL_MS);
                }
            }).ConfigureAwait(false);

            Parallel.For(0, numActors, actorID => genData(actorID, numGamesPerActor));

            var numGamesLeft = this.CONFIG.NumGamesInBatch % numActors;
            genData(0, numGamesLeft);

            void genData(int actorID, int numGames)
            {
                var tree = trees[actorID];
                var rand = this.RANDS[actorID];
                for (var i = 0; i < numGames; i++)
                {
                    var data = GenerateTrainDataWithMCTS(new Position(), tree, rand);
                    this.trainDataSet.Enqueue(data);
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
            var evalScores = new List<Half>();
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
                    evalScores.Add((Half)1 - evalScores[^1]);
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
                evalScores.Add((Half)tree.RootValue);
                moveCount++;

                tree.UpdateRootState(ref move);
            }

            moveHistory.RemoveRange(moveHistory.Count - 2, 2);  // removes last two passes.
            evalScores.RemoveRange(evalScores.Count - 2, 2);

            return new TrainData(rootPos, moveHistory, evalScores, (sbyte)pos.GetScore(DiscColor.Black));
        }
    }
}
