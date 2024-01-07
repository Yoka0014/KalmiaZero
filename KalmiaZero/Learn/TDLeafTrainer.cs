using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Search.AlphaBeta;

namespace KalmiaZero.Learn
{
    public record class TDLeafTrainerConfig
    {
        public int NumEpisodes { get; init; } = 250_000;
        public int NumInitialRandomMoves { get; init; } = 1;
        public AlphaBetaEvalType LearningRate { get; init; } = (AlphaBetaEvalType)0.2;
        public AlphaBetaEvalType DiscountRate { get; init; } = 1;
        public double InitialExplorationRate { get; init; } = 0.2;
        public double FinalExplorationRate { get; init; } = 0.1;
        public AlphaBetaEvalType EligibilityTraceFactor { get; init; } = (AlphaBetaEvalType)0.7;
        public AlphaBetaEvalType HorizonCutFactor { get; init; } = (AlphaBetaEvalType)0.1;
        public AlphaBetaEvalType TCLFactor { get; init; } = (AlphaBetaEvalType)2.7;
        public int SearchDepth { get; init; } = 3;
        public long TTSize { get; init; } = 4096L * 1024L * 1024L * 1024L;

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string WeightsFileName { get; init; } = "value_func_weights_tdl";
        public int SaveWeightsInterval { get; init; } = 10000;
        public bool SaveOnlyLatestWeights { get; init; } = false;
    }

    public class TDLeafTrainer
    {
        const double TCL_EPSILON = 1.0e-4;

        public string Label { get; }
        readonly TDLeafTrainerConfig CONFIG;
        readonly double EXPLORATION_RATE_DELTA;
        readonly string WEIGHTS_FILE_PATH;
        readonly StreamWriter logger;

        readonly Searcher searcher;
        readonly ValueFunction<AlphaBetaEvalType> valueFunc;
        readonly PastStatesBuffer pastStatesBuffer;
        readonly AlphaBetaEvalType[] weightDeltaSum;
        readonly AlphaBetaEvalType[] weightDeltaAbsSum;
        AlphaBetaEvalType biasDeltaSum;
        AlphaBetaEvalType biasDeltaAbsSum;
        AlphaBetaEvalType meanWeightDelta;

        readonly Random rand;

        public TDLeafTrainer(ValueFunction<AlphaBetaEvalType> valueFunc, TDLeafTrainerConfig config, int randSeed = -1)
        : this(valueFunc, config, Console.OpenStandardOutput(), randSeed) { }

        public TDLeafTrainer(ValueFunction<AlphaBetaEvalType> valueFunc, TDLeafTrainerConfig config, Stream logStream, int randSeed = -1)
        : this(string.Empty, valueFunc, config, logStream, randSeed) { }

        public TDLeafTrainer(string label, ValueFunction<AlphaBetaEvalType> valueFunc, TDLeafTrainerConfig config)
        : this(label, valueFunc, config, Console.OpenStandardOutput()) { }

        public TDLeafTrainer(string label, ValueFunction<AlphaBetaEvalType> valueFunc, TDLeafTrainerConfig config, Stream logStream, int randSeed = -1)
        {
            this.Label = label;
            this.CONFIG = config;
            this.EXPLORATION_RATE_DELTA = (config.InitialExplorationRate - config.FinalExplorationRate) / config.NumEpisodes;
            this.WEIGHTS_FILE_PATH = Path.Combine(this.CONFIG.WorkDir, $"{config.WeightsFileName}_{"{0}"}.bin");

            this.valueFunc = valueFunc;
            var numWeights = valueFunc.Weights.Length;
            this.weightDeltaSum = new AlphaBetaEvalType[numWeights];
            this.weightDeltaAbsSum = new AlphaBetaEvalType[numWeights];
            var capasity = int.CreateChecked(AlphaBetaEvalType.Log(config.HorizonCutFactor, config.EligibilityTraceFactor)) + 1;
            this.pastStatesBuffer = new PastStatesBuffer(capasity, valueFunc.NTuples);

            this.searcher = new Searcher(this.valueFunc, this.CONFIG.TTSize);
            this.searcher.PVNotificationIntervalMs = int.MaxValue;

            this.rand = (randSeed >= 0) ? new Random(randSeed) : new Random(Random.Shared.Next());

            this.logger = new StreamWriter(logStream);
            this.logger.AutoFlush = false;
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
