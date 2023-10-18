using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Search;

namespace KalmiaZero.Learn
{
    public class TDTrainerConfig<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
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

        public string WeightsFileName { get; init; } = "value_func_weights_td";
        public int SaveWeightsInterval { get; init; } = 1000;
        public bool SaveOnlyLatestWeights { get; init; } = false;
    }

    public class TDTrainer<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        const double TCL_EPSILON = 1.0e-4;

        readonly TDTrainerConfig<WeightType> CONFIG;
        readonly double EXPLORATION_RATE_DELTA;
        readonly string WEIGHTS_FILE_PATH; 

        readonly ValueFunction<WeightType> valueFunc;
        readonly PastStatesBuffer pastStatesBuffer;
        WeightType[][]? weightDeltaSum;
        WeightType[][]? weightDeltaAbsSum;
        WeightType biasDeltaSum;
        WeightType biasDeltaAbsSum;

        readonly Random rand;

        public TDTrainer(ValueFunction<WeightType> valueFunc, TDTrainerConfig<WeightType> config, int randSeed = -1)
        {
            this.CONFIG = config;
            this.EXPLORATION_RATE_DELTA = (config.FinalExplorationRate - config.InitialExplorationRate) / config.NumEpisodes;
            this.WEIGHTS_FILE_PATH = $"{config.WeightsFileName}_{"{0}"}.bin";

            this.valueFunc = valueFunc;
            var capasity = int.CreateChecked(WeightType.Log(config.HorizonCutFactor, config.EligibilityTraceFactor)) + 1; 
            this.pastStatesBuffer = new PastStatesBuffer(capasity, valueFunc.NTuples);

            this.rand = (randSeed >= 0) ? new Random(randSeed) : new Random(Random.Shared.Next());
        }

        public void Train()
        {
            var explorationRate = this.CONFIG.InitialExplorationRate;

            var tclEpsilon = WeightType.CreateChecked(TCL_EPSILON);
            this.weightDeltaSum = new WeightType[this.valueFunc.NTuples.Length][];
            this.weightDeltaAbsSum = new WeightType[this.valueFunc.NTuples.Length][];
            var numFeatures = this.valueFunc.NTuples.NumPossibleFeatures;
            for(var nTupleID = 0; nTupleID < this.valueFunc.NTuples.Length; nTupleID++)
            {
                var len = numFeatures[nTupleID];
                this.weightDeltaSum[nTupleID] = Enumerable.Repeat(tclEpsilon, len).ToArray();
                this.weightDeltaAbsSum[nTupleID] = Enumerable.Repeat(tclEpsilon, len).ToArray();
            }

            this.biasDeltaSum = this.biasDeltaAbsSum = tclEpsilon;

            for (var episodeID = 0; episodeID < this.CONFIG.NumEpisodes; episodeID++)
            {
                Console.WriteLine($"episode: {episodeID}");
                RunEpisode(explorationRate);
                explorationRate -= EXPLORATION_RATE_DELTA;

                if ((episodeID + 1) % this.CONFIG.SaveWeightsInterval == 0)
                {
                    var path = string.Format(this.WEIGHTS_FILE_PATH, episodeID);
                    this.valueFunc.SaveToFile(path);
                    Console.WriteLine($"Info: saved weights at \"{path}\"");
                }
            }
        }

        [SkipLocalsInit]
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
                    ref Move move = ref game.Moves[this.rand.Next(game.Moves.Length)];
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
            WeightType g(WeightType x) => WeightType.Exp(beta * (x - WeightType.One));

            fixed (WeightType* weights = this.valueFunc.Weights)
            {
                foreach (var posFeatureVec in this.pastStatesBuffer)
                {
                    var delta = eligibility * tdError;
                    if (posFeatureVec.SideToMove == DiscColor.Black)
                        ApplyGradients<Black>(posFeatureVec, weights, alpha, beta, delta);
                    else
                        ApplyGradients<White>(posFeatureVec, weights, alpha, beta, delta);

                    var lr = alpha * g(WeightType.Abs(this.biasDeltaSum) / this.biasDeltaAbsSum);
                    var db = -lr * delta;
                    this.valueFunc.Bias += db;
                    this.biasDeltaSum += db;
                    this.biasDeltaAbsSum += WeightType.Abs(db);

                    eligibility *= this.CONFIG.DiscountRate * this.CONFIG.EligibilityTraceFactor;
                    tdError = this.CONFIG.DiscountRate - WeightType.One - tdError;  // inverse tdError: DiscountRate * (1.0 - nextV) - (1.0 - v)
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void ApplyGradients<DiscColor>(PositionFeatureVector posFeatureVec, WeightType* weights, WeightType alpha, WeightType beta, WeightType delta) where DiscColor : IDiscColor
        {
            Debug.Assert(this.weightDeltaSum is not null);
            Debug.Assert(this.weightDeltaAbsSum is not null);

            WeightType v = this.valueFunc.PredictWithBlackWeights(posFeatureVec);
            WeightType g(WeightType x) => WeightType.Exp(beta * (x - WeightType.One));

            for (var i = 0; i < posFeatureVec.Features.Length; i++)
            {
                var w = weights + this.valueFunc.NTupleOffset[i];
                ref Feature feature = ref posFeatureVec.Features[i];
                fixed(FeatureType* opp = posFeatureVec.NTuples.GetOpponentFeatureRawTable(i))
                fixed (FeatureType* mirror = posFeatureVec.NTuples.GetMirroredFeatureRawTable(i))
                fixed (WeightType* dwSum = this.weightDeltaSum[i])
                fixed (WeightType* dwAbsSum = this.weightDeltaAbsSum[i])
                {
                    for (var j = 0; j < feature.Length; j++)
                    {
                        FeatureType f, mf;

                        if (typeof(DiscColor) == typeof(Black))
                        {
                            f = feature[j];
                            mf = mirror[feature[j]];
                        }
                        else
                        {
                            f = opp[feature[j]];
                            mf = opp[mirror[feature[j]]];
                        }

                        var lr = alpha * g(WeightType.Abs(dwSum[i]) / dwAbsSum[i]);
                        var dw = -lr * delta;

                        w[f] += dw;
                        w[mf] += dw;

                        dwSum[f] += dw;
                        dwSum[mf] += dw;

                        var absDW = WeightType.Abs(dw);
                        dwAbsSum[f] += absDW;
                        dwAbsSum[mf] += absDW;
                    }
                }
            }
        }

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

            public PastStatesBuffer(int capasity, NTuples nTuples) 
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
