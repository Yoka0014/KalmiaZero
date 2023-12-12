using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Search;

namespace KalmiaZero.Learn
{
    public record class TDTrainerConfig<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public int NumEpisodes { get; init; } = 250_000;
        public int NumInitialRandomMoves { get; init; } = 1;
        public WeightType LearningRate { get; init; } = WeightType.CreateChecked(0.2);
        public WeightType DiscountRate { get; init; } = WeightType.One;
        public double InitialExplorationRate { get; init; } = 0.2;
        public double FinalExplorationRate { get; init; } = 0.1;
        public WeightType EligibilityTraceFactor { get; init; } = WeightType.CreateChecked(0.5);
        public WeightType HorizonCutFactor { get; init; } = WeightType.CreateChecked(0.1);
        public WeightType TCLFactor { get; init; } = WeightType.CreateChecked(2.7);

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string WeightsFileName { get; init; } = "value_func_weights_td";
        public int SaveWeightsInterval { get; init; } = 10000;
        public bool SaveOnlyLatestWeights { get; init; } = false;
    }

    public class TDTrainer<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        const double TCL_EPSILON = 1.0e-4;

        public string Label { get; }
        readonly TDTrainerConfig<WeightType> CONFIG;
        readonly double EXPLORATION_RATE_DELTA;
        readonly string WEIGHTS_FILE_PATH;
        readonly StreamWriter logger;

        readonly ValueFunction<WeightType> valueFunc;
        readonly PastStatesBuffer pastStatesBuffer;
        readonly WeightType[] weightDeltaSum;
        readonly WeightType[] weightDeltaAbsSum;
        WeightType biasDeltaSum;
        WeightType biasDeltaAbsSum;
        WeightType meanWeightDelta;

        readonly Random rand;

        public TDTrainer(ValueFunction<WeightType> valueFunc, TDTrainerConfig<WeightType> config, int randSeed = -1)
        : this(valueFunc, config, Console.OpenStandardOutput(), randSeed) { }

        public TDTrainer(ValueFunction<WeightType> valueFunc, TDTrainerConfig<WeightType> config, Stream logStream, int randSeed = -1)
        : this(string.Empty, valueFunc, config, logStream, randSeed) { }

        public TDTrainer(string label, ValueFunction<WeightType> valueFunc, TDTrainerConfig<WeightType> config)
        : this(label, valueFunc, config, Console.OpenStandardOutput()) { }

        public TDTrainer(string label, ValueFunction<WeightType> valueFunc, TDTrainerConfig<WeightType> config, Stream logStream, int randSeed = -1)
        {
            this.Label = label;
            this.CONFIG = config;
            this.EXPLORATION_RATE_DELTA = (config.InitialExplorationRate - config.FinalExplorationRate) / config.NumEpisodes;
            this.WEIGHTS_FILE_PATH = Path.Combine(this.CONFIG.WorkDir, $"{config.WeightsFileName}_{"{0}"}.bin");

            this.valueFunc = valueFunc;
            var numWeights = valueFunc.Weights.Length;
            this.weightDeltaSum = new WeightType[numWeights];
            this.weightDeltaAbsSum = new WeightType[numWeights];
            var capasity = int.CreateChecked(WeightType.Log(config.HorizonCutFactor, config.EligibilityTraceFactor)) + 1;
            this.pastStatesBuffer = new PastStatesBuffer(capasity, valueFunc.NTuples);

            this.rand = (randSeed >= 0) ? new Random(randSeed) : new Random(Random.Shared.Next());

            this.logger = new StreamWriter(logStream);
            this.logger.AutoFlush = false;
        }

        public static void TrainMultipleAgents(string workDir, TDTrainerConfig<WeightType> config, int numAgents, int tupleSize, int numTuples)
            => TrainMultipleAgents(workDir, config, numAgents, tupleSize, numTuples, Environment.ProcessorCount);

        public static void TrainMultipleAgents(string workDir, TDTrainerConfig<WeightType> config, int numAgents, int tupleSize, int numTuples, int numThreads)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = numThreads };
            Parallel.For(0, numAgents, options, agentID =>
            {
                var dir = $"AG-{agentID}";
                Path.Combine(workDir, dir);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var tuples = (from _ in Enumerable.Range(0, numTuples) select new NTupleInfo(tupleSize)).ToArray();
                var nTuples = new NTupleGroup(tuples);
                var valueFunc = new ValueFunction<WeightType>(nTuples);
                new TDTrainer<WeightType>($"AG-{agentID}", valueFunc, config with { WorkDir = dir }).Train();
            });
        }

        public void Train()
        {
            var explorationRate = this.CONFIG.InitialExplorationRate;
            var tclEpsilon = WeightType.CreateChecked(TCL_EPSILON);
            Array.Fill(this.weightDeltaSum, tclEpsilon);
            Array.Fill(this.weightDeltaAbsSum, tclEpsilon);
            this.biasDeltaSum = this.biasDeltaAbsSum = tclEpsilon;

            WriteLabel();
            this.logger.WriteLine("Start learning.\n");
            WriteParams(explorationRate);
            this.logger.Flush();

            for (var episodeID = 0; episodeID < this.CONFIG.NumEpisodes; episodeID++)
            {
                RunEpisode(explorationRate);
                explorationRate -= EXPLORATION_RATE_DELTA;

                if ((episodeID + 1) % this.CONFIG.SaveWeightsInterval == 0)
                {
                    WriteLabel();

                    var fromEpisodeID = episodeID - this.CONFIG.SaveWeightsInterval + 1;
                    this.logger.WriteLine($"Episodes {fromEpisodeID} to {episodeID} have done.");

                    var path = string.Format(this.WEIGHTS_FILE_PATH, episodeID);
                    this.valueFunc.SaveToFile(path);

                    this.logger.WriteLine($"Weights were saved at \"{path}\"\n");
                    WriteParams(explorationRate);
                    this.logger.WriteLine();
                    this.logger.Flush();
                }
            }

            this.valueFunc.CopyWeightsBlackToWhite();
        }

        void WriteLabel()
        {
            if (!string.IsNullOrEmpty(this.Label))
                this.logger.WriteLine($"[{this.Label}]");
        }

        void WriteParams(double explorationRate)
        {
            this.logger.WriteLine($"ExplorationRate: {explorationRate}");
            this.logger.WriteLine($"MeanLearningRate: {CalcMeanLearningRate()}");
            this.logger.WriteLine($"Bias: {this.valueFunc.Bias}");
        }

        WeightType CalcMeanLearningRate()
        {
            var sum = WeightType.Zero;
            foreach ((var n, var a) in this.weightDeltaSum.Zip(this.weightDeltaAbsSum))
                sum += Decay(WeightType.Abs(n / a));
            sum += Decay(this.biasDeltaSum / this.biasDeltaAbsSum);
            return this.CONFIG.LearningRate * sum / WeightType.CreateChecked(this.weightDeltaSum.Length + 1);
        }

        void RunEpisode(double explorationRate)
        {
            var game = new GameInfo(valueFunc.NTuples);
            pastStatesBuffer.Clear();
            pastStatesBuffer.Add(game.FeatureVector);
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            int numMoves;

            var moveCount = 0;
            while (true)
            {
                if (game.Moves.Length == 0)  // pass
                {
                    game.Pass();
                    this.pastStatesBuffer.Add(game.FeatureVector);
                    continue;
                }

                WeightType v = this.valueFunc.PredictWithBlackWeights(game.FeatureVector);
                WeightType nextV;
                int moveIdx;
                if (moveCount < this.CONFIG.NumInitialRandomMoves || Random.Shared.NextDouble() < explorationRate)   // random move
                {
                    Move move = game.Moves[this.rand.Next(game.Moves.Length)];
                    game.Position.GenerateMove(ref move);
                    game.Update(ref move);

                    if (game.Moves.Length == 0 && game.Position.IsGameOver)
                    {
                        Fit(GetReward(game.Position.DiscDiff) - v);
                        break;
                    }

                    nextV = WeightType.One - this.valueFunc.PredictWithBlackWeights(game.FeatureVector);
                }
                else    // greedy
                {
                    game.Moves.CopyTo(moves);
                    numMoves = game.Moves.Length;

                    moveIdx = 0;
                    var minVLogit = WeightType.PositiveInfinity;
                    for (var i = 0; i < numMoves; i++)
                    {
                        ref Move move = ref moves[i];
                        game.Position.GenerateMove(ref move);
                        game.Update(ref move);
                        WeightType vLogit = this.valueFunc.PredictLogitWithBlackWeights(game.FeatureVector);
                        if (vLogit < minVLogit)
                        {
                            minVLogit = vLogit;
                            moveIdx = i;
                        }
                        game.Undo(ref move, moves[..numMoves]);
                    }

                    game.Update(ref moves[moveIdx]);

                    if (game.Moves.Length == 0 && game.Position.IsGameOver) // terminal state
                    {
                        Fit(GetReward(game.Position.DiscDiff) - v);
                        break;
                    }

                    nextV = WeightType.One - ValueFunction<WeightType>.StdSigmoid(minVLogit);
                }

                Fit(this.CONFIG.DiscountRate * nextV - v);
                this.pastStatesBuffer.Add(game.FeatureVector);
                moveCount++;
            }
        }

        unsafe void Fit(WeightType tdError)
        {
            var alpha = this.CONFIG.LearningRate;
            var beta = this.CONFIG.TCLFactor;
            var eligibility = WeightType.One;

            fixed (WeightType* weights = this.valueFunc.Weights)
            fixed (WeightType* weightDeltaSum = this.weightDeltaSum)
            fixed (WeightType* weightDeltaAbsSum = this.weightDeltaAbsSum)
            {
                foreach (var posFeatureVec in this.pastStatesBuffer)
                {
                    var delta = eligibility * tdError;
                    if (posFeatureVec.SideToMove == DiscColor.Black)
                        ApplyGradients<Black>(posFeatureVec, weights, weightDeltaSum, weightDeltaAbsSum, alpha, beta, delta);
                    else
                        ApplyGradients<White>(posFeatureVec, weights, weightDeltaSum, weightDeltaAbsSum, alpha, beta, delta);

                    var reg = WeightType.One / WeightType.CreateChecked(posFeatureVec.NumNTuples + 1);
                    var lr = reg * alpha * Decay(WeightType.Abs(this.biasDeltaSum) / this.biasDeltaAbsSum);
                    var db = lr * delta;
                    this.valueFunc.Bias += db;
                    this.biasDeltaSum += db;
                    this.biasDeltaAbsSum += WeightType.Abs(db);

                    eligibility *= this.CONFIG.DiscountRate * this.CONFIG.EligibilityTraceFactor;
                    tdError = this.CONFIG.DiscountRate - WeightType.One - tdError;  // inverse tdError: DiscountRate * (1.0 - nextV) - (1.0 - v)
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void ApplyGradients<DiscColor>(PositionFeatureVector posFeatureVec, WeightType* weights, WeightType* weightDeltaSum, WeightType* weightDeltaAbsSum, WeightType alpha, WeightType beta, WeightType delta) where DiscColor : IDiscColor
        {
            for (var i = 0; i < posFeatureVec.Features.Length; i++)
            {
                var offset = this.valueFunc.NTupleOffset[i];
                var w = weights + offset;
                var dwSum = weightDeltaSum + offset;
                var dwAbsSum = weightDeltaAbsSum + offset;

                ref Feature feature = ref posFeatureVec.Features[i];
                fixed (FeatureType* opp = posFeatureVec.NTuples.GetOpponentFeatureRawTable(i))
                fixed (FeatureType* mirror = posFeatureVec.NTuples.GetMirroredFeatureRawTable(i))
                {
                    var reg = WeightType.One / WeightType.CreateChecked((posFeatureVec.NumNTuples + 1) * feature.Length);
                    for (var j = 0; j < feature.Length; j++)
                    {
                        var f = (typeof(DiscColor) == typeof(Black)) ? feature[j] : opp[feature[j]];
                        var mf = mirror[f];

                        var lr = reg * alpha * Decay(WeightType.Abs(dwSum[i]) / dwAbsSum[i]);
                        var dw = lr * delta;
                        var absDW = WeightType.Abs(dw);

                        if (mf != f)
                        {
                            dw *= WeightType.CreateChecked(0.5);
                            absDW *= WeightType.CreateChecked(0.5);
                            w[mf] += dw;
                            dwSum[mf] += dw;
                            dwAbsSum[mf] += absDW;
                        }

                        w[f] += dw;
                        dwSum[f] += dw;
                        dwAbsSum[f] += absDW;
                    }
                }
            }
        }

        WeightType Decay(WeightType x) => WeightType.Exp(this.CONFIG.TCLFactor * (x - WeightType.One));

        static WeightType GetReward(int discDiff)
        {
            if (discDiff == 0)
                return WeightType.One / (WeightType.One + WeightType.One);
            else
                return (discDiff > 0) ? WeightType.Zero : WeightType.One;
        }

        class PastStatesBuffer : IEnumerable<PositionFeatureVector>
        {
            public int Capasity => this.posFeatureVecs.Length;
            public int Count { get; private set; } = 0;

            readonly PositionFeatureVector[] posFeatureVecs;
            int loc = 0;

            public PastStatesBuffer(int capasity, NTupleGroup nTuples)
            {
                this.posFeatureVecs = Enumerable.Range(0, capasity).Select(_ => new PositionFeatureVector(nTuples)).ToArray();
            }

            public void Clear() => this.loc = 0;

            public void Add(PositionFeatureVector pfVec)
            {
                pfVec.CopyTo(this.posFeatureVecs[this.loc]);
                this.loc = (this.loc + 1) % this.Capasity;
                this.Count = Math.Min(this.Count + 1, this.Capasity);
            }

            public IEnumerator<PositionFeatureVector> GetEnumerator() => new Enumerator(this);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public class Enumerator : IEnumerator<PositionFeatureVector>
            {
                public PositionFeatureVector Current { get; private set; }

                object IEnumerator.Current => this.Current;

                readonly PastStatesBuffer pastStatesBuffer;
                int idx;
                int moveCount;

                public Enumerator(PastStatesBuffer pastStatesBuffer)
                {
                    this.pastStatesBuffer = pastStatesBuffer;
                    Reset();
                    Debug.Assert(this.Current is not null);
                }

                public void Dispose() { }

                public bool MoveNext()
                {
                    if (this.moveCount == this.pastStatesBuffer.Count)
                        return false;

                    var nextIdx = this.idx - 1;
                    if (nextIdx < 0)
                        nextIdx = this.pastStatesBuffer.Count - 1;
                    this.Current = this.pastStatesBuffer.posFeatureVecs[this.idx];
                    this.idx = nextIdx;
                    this.moveCount++;
                    return true;
                }

                public void Reset()
                {
                    this.Current = PositionFeatureVector.Empty;
                    this.idx = this.pastStatesBuffer.loc - 1;
                    if (idx < 0)
                        this.idx = this.pastStatesBuffer.Count - 1;
                    this.moveCount = 0;
                }
            }
        }
    }
}