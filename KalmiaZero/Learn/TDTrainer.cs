using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Search;
using KalmiaZero.Utils;

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
        public int SaveWeightsInterval { get; init; } = 1000;
        public bool SaveOnlyLatestWeights { get; init; } = false;
    }

    public class TDTrainer<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        const double TCL_EPSILON = 1.0e-4;

        readonly TDTrainerConfig<WeightType> CONFIG;
        readonly double EXPLORATION_RATE_DELTA;
        readonly int PAST_STATES_BUFFER_CAPASITY;

        readonly ValueFunction<WeightType> valueFunc;
        readonly PastStatesBuffer pastStatesBuffer;
        double explorationRate;
        WeightType[][]? weightDeltaSum;
        WeightType[][]? weightDeltaAbsSum;
        WeightType[]? biasDeltaSum;
        WeightType[]? biasDeltaAbsSum;

        readonly Random rand;

        public TDTrainer(ValueFunction<WeightType> valueFunc, TDTrainerConfig<WeightType> config, int randSeed = -1)
        {
            this.CONFIG = config;
            this.EXPLORATION_RATE_DELTA = (config.FinalExplorationRate - config.InitialExplorationRate) / config.NumEpisodes;

            this.valueFunc = valueFunc;
            var capasity = int.CreateChecked(WeightType.Log(config.HorizonCutFactor, config.EligibilityTraceFactor)) + 1; 
            this.pastStatesBuffer = new PastStatesBuffer(capasity, valueFunc.NTuples);

            this.rand = (randSeed >= 0) ? new Random(randSeed) : new Random(Random.Shared.Next());
        }

        public void Train()
        {
            this.explorationRate = this.CONFIG.InitialExplorationRate;
            this.weightDeltaSum = new WeightType[this.valueFunc.NTuples.Length][];
            this.weightDeltaAbsSum = new WeightType[this.valueFunc.NTuples.Length][];
            this.biasDeltaSum = Enumerable.Repeat(WeightType.CreateChecked(TCL_EPSILON), this.valueFunc.NTuples.Length).ToArray();
            this.biasDeltaAbsSum = Enumerable.Repeat(WeightType.CreateChecked(TCL_EPSILON), this.valueFunc.NTuples.Length).ToArray();
            var numFeatures = this.valueFunc.NTuples.NumPossibleFeatures;
            for(var nTupleID = 0; nTupleID < this.valueFunc.NTuples.Length; nTupleID++)
            {
                var len = numFeatures[nTupleID];
                this.weightDeltaSum[nTupleID] = Enumerable.Repeat(WeightType.CreateChecked(TCL_EPSILON), len).ToArray();
                this.weightDeltaAbsSum[nTupleID] = Enumerable.Repeat(WeightType.CreateChecked(TCL_EPSILON), len).ToArray();
            }

            for (var episodeID = 0; episodeID < this.CONFIG.NumEpisodes; episodeID++)
            {
                this.pastStatesBuffer.Clear();
                RunEpisode();
                this.explorationRate -= EXPLORATION_RATE_DELTA;
            }
        }

        void RunEpisode()
        {
            var game = new GameInfo(valueFunc.NTuples);
            pastStatesBuffer.Clear();
            pastStatesBuffer.Add(game.FeatureVector);

            var moveCount = 0;
            var passed = false;
            while (true)
            {
                WeightType v = this.valueFunc.PredictWithBlackWeights(game.FeatureVector);
                WeightType nextV;

                if (game.Moves.Length == 0)  // pass
                {
                    game.Pass();
                    if (passed)
                        break;
                    passed = true;

                    nextV = WeightType.One - v;
                }
                if (moveCount < this.CONFIG.NumInitialRandomMoves || Random.Shared.NextDouble() < explorationRate)   // random move
                {
                    game.Update(ref game.Moves[this.rand.Next(game.Moves.Length)]);
                    nextV = this.valueFunc.PredictWithBlackWeights(game.FeatureVector);
                    passed = false;
                }
                else    // greedy
                {
                    var maxIdx = 0;
                    var maxVLogit = WeightType.NegativeInfinity;
                    for (var i = 0; i < game.Moves.Length; i++)
                    {
                        ref var move = ref game.Moves[i];
                        game.Update(ref move);
                        var vLogit = this.valueFunc.PredictLogitWithBlackWeights(game.FeatureVector);
                        if(vLogit > maxVLogit)
                        {
                            maxVLogit = vLogit;
                            maxIdx = i;
                        }
                        game.Undo(ref move);
                    }

                    game.Update(ref game.Moves[maxIdx]);
                    nextV = ValueFunction<WeightType>.StdSigmoid(maxVLogit);
                    passed = false;
                }

                Fit(this.CONFIG.DiscountRate * nextV - v);
                this.pastStatesBuffer.Add(game.FeatureVector);
            }
        }

        unsafe void Fit(WeightType tdError)
        {
            Debug.Assert(this.weightDeltaSum is not null);
            Debug.Assert(this.weightDeltaAbsSum is not null);
            Debug.Assert(this.biasDeltaSum is not null);
            Debug.Assert(this.biasDeltaAbsSum is not null);

            var alpha = this.CONFIG.LearningRate;
            var beta = this.CONFIG.TCLFactor;
            var eligibility = WeightType.One;
            WeightType g(WeightType x) => WeightType.Exp(beta * (x - WeightType.One));

            fixed (WeightType* weights = this.valueFunc.Weights)
            {
                foreach (var posFeatureVec in this.pastStatesBuffer)
                {
                    var delta = eligibility * tdError;
                    WeightType v = this.valueFunc.PredictWithBlackWeights(posFeatureVec);

                    for (var i = 0; i < posFeatureVec.Features.Length; i++)
                    {
                        var w = weights + this.valueFunc.NTupleOffset[i];
                        ref Feature feature = ref posFeatureVec.Features[i];
                        fixed (FeatureType* mirror = posFeatureVec.NTuples.GetMirroredFeatureRawTable(i))
                        fixed (WeightType* deltaSum = this.weightDeltaSum[i])
                        fixed (WeightType* deltaAbsSum = this.weightDeltaAbsSum[i])
                        {
                            WeightType lr;
                            for (var j = 0; j < feature.Length; j++)
                            {
                                var f = feature[j];
                                var mf = mirror[feature[j]];
                                lr = alpha * g(WeightType.Abs(deltaSum[i]) / deltaAbsSum[i]);
                                var dw = -lr * delta;

                                w[f] += dw;
                                w[mf] += dw;

                                deltaSum[f] += dw;
                                deltaSum[mf] += dw;

                                var absDW = WeightType.Abs(dw);
                                deltaAbsSum[f] += absDW;
                                deltaAbsSum[mf] += absDW;
                            }

                            lr = alpha * g(WeightType.Abs(this.biasDeltaSum[i]) / this.biasDeltaAbsSum[i]);
                            var db = -lr * delta;
                            this.valueFunc.Bias += db;

                            this.biasDeltaSum[i] += db;
                            this.biasDeltaAbsSum[i] += WeightType.Abs(db);
                        }
                    }

                    eligibility *= this.CONFIG.DiscountRate * this.CONFIG.EligibilityTraceFactor;
                }
            }
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
                pfVec.CopyTo(this.posFeatureVecs[this.loc++ % this.Capasity]);
                this.Count++;
            }

            public IEnumerable<PositionFeatureVector> EnumeratePastStates() 
            {
                if (this.Count == 0)
                    yield break;

                var idx = this.loc - 1;
                if (idx < 0)
                    idx = this.Capasity - 1;

                var yieldCount = 0;
                do
                {
                    yield return this.posFeatureVecs[idx];
                    if (--idx < 0)
                        idx = this.Capasity - 1;
                } while (idx != this.loc && ++yieldCount != this.Count);
            }

            public IEnumerator<PositionFeatureVector> GetEnumerator() => new Enumerator(this);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<PositionFeatureVector?>
            {
                public PositionFeatureVector? Current { get; private set; }

                readonly object? IEnumerator.Current => this.Current;

                readonly PastStatesBuffer pastStatesBuffer;
                int idx;
                int moveCount;

                public Enumerator(PastStatesBuffer pastStatesBuffer) => this.pastStatesBuffer = pastStatesBuffer;

                public readonly void Dispose() { }

                public bool MoveNext()
                {
                    if (this.pastStatesBuffer.Count == 0)
                        return false;

                    if (this.idx == this.pastStatesBuffer.loc && this.moveCount == this.pastStatesBuffer.Count)
                        return false;

                    var nextIdx = this.idx - 1;
                    if (nextIdx < 0)
                        nextIdx = this.pastStatesBuffer.Capasity - 1;
                    this.Current = this.pastStatesBuffer.posFeatureVecs[this.idx];
                    this.idx = nextIdx;
                    this.moveCount++;
                    return true;
                }

                public void Reset()
                {
                    this.Current = null;
                    this.idx = this.pastStatesBuffer.loc - 1;
                    if (idx < 0)
                        this.idx = this.pastStatesBuffer.Capasity - 1;
                    this.moveCount = 0;
                }
            }
        }
    }
}
