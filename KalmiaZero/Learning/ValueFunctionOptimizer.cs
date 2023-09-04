using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;
using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Utils;
using System.Threading;

namespace KalmiaZero.Learning
{
    public struct ValueFuncOptimizerOptions<WeightType> where WeightType : struct, IFloatingPointIeee754<WeightType>
    {
        public int NumEpoch { get; set; }
        public WeightType LearningRate { get; set; } = WeightType.One;
        public int CheckpointInterval { get; set; } = 10;
        public WeightType Epsilon { get; set; } = WeightType.Epsilon;
        public int Patience { get; set; } = 5;
        public int NumThreads { get; set; } = Environment.ProcessorCount;
        public string TrainDataPath { get; set; } = "train_data.bin";
        public string TestDataPath { get; set; } = "test_data.bin";
        public string LogFilePath { get; set; } = "train.log";
        public string GradPow2SumsFileName { get; set; } = "grad2_sum.bin";
        public string WeightsFileName { get; set; } = "value_func_weights.bin";

        public ValueFuncOptimizerOptions() { }
    }

    struct BatchItem<FloatType> where FloatType : struct, IFloatingPointIeee754<FloatType>
    {
        public PositionFeatureVector Input;
        public FloatType Output;
    }

    class ValueFuncOptimizer<WeightType> where WeightType : struct, IFloatingPointIeee754<WeightType>
    {
        ValueFunction<WeightType> valueFunc;
        WeightType[][] gradsPow2Sums;
        WeightType biasGradPow2Sum;
        List<WeightType> trainLossHistory = new();
        List<WeightType> testLossHistory = new();
        ValueFuncOptimizerOptions<WeightType> options;
        ParallelOptions parallelOptions;

        public ValueFuncOptimizer(string workDirPath, ValueFunction<WeightType> valueFunc, ValueFuncOptimizerOptions<WeightType> options)
        {
            this.options = options;
            this.parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.NumThreads };
            this.valueFunc = valueFunc;
            this.gradsPow2Sums = new WeightType[valueFunc.NTuples.Length][];
            this.biasGradPow2Sum = WeightType.Zero;
            for (var nTupleID = 0; nTupleID < valueFunc.NTuples.Length; nTupleID++)
                gradsPow2Sums[nTupleID] = new WeightType[valueFunc.GetWeights(DiscColor.Black, nTupleID).Length];
        }

        WeightType CalculateGradients(BatchItem<WeightType>[] batch, WeightType[][][] grads, WeightType[] biasGrad)
        {
            var loss = new WeightType[this.options.NumThreads];

            foreach (var g in grads)
                foreach (var gPerThread in g)
                    Array.Clear(gPerThread);

            var nTuples = this.valueFunc.NTuples;
            var numItemsPerThread = batch.Length / this.options.NumThreads;
            var numRestItems = batch.Length % this.options.NumThreads;

            Parallel.For(0, this.options.NumThreads, 
                threadID => kernel(threadID, batch.AsSpan(threadID * numItemsPerThread, numItemsPerThread)));

            kernel(0, batch.AsSpan(this.options.NumThreads, numRestItems));

            var lossSum = loss.Sum();
            if (lossSum is Half hLoss)
            {
                var lossMean = hLoss / (Half)batch.Length;
                if (lossMean is WeightType ret)
                    return ret;
            }
            else if (lossSum is float fLoss)
            {
                var lossMean = fLoss / batch.Length;
                if (lossMean is WeightType ret)
                    return ret;
            }
            else if (lossSum is double dLoss)
            {
                var lossMean = dLoss / batch.Length;
                if (lossMean is WeightType ret)
                    return ret;
            }

            throw new InvalidCastException($"Cannot cast to Half, float and double from {typeof(WeightType)}.");

            void kernel(int threadID, Span<BatchItem<WeightType>> batch)
            {
                var gradPerThread = grads[threadID];
                for (var i = 0; i < batch.Length; i++)
                {
                    ref var batchItem = ref batch[i];
                    var y = this.valueFunc.Predict(batchItem.Input);
                    var delta = y - batch[i].Output;
                    loss[threadID] += BinaryCrossEntropy(y, batch[i].Output);

                    for (var nTupleID = 0; nTupleID < nTuples.Length; nTupleID++)
                    {
                        var g = gradPerThread[nTupleID];
                        ReadOnlySpan<int> features = batchItem.Input.GetFeatures(nTupleID);
                        ReadOnlySpan<int> mirror = nTuples.GetMirroredFeatureTable(nTupleID);
                        for (var j = 0; j < features.Length; j++)
                        {
                            var f = features[j];
                            g[f] += delta;

                            var mirrored = mirror[f];
                            if (mirrored != f)
                                g[mirrored] += delta;
                        }
                    }

                    biasGrad[threadID] += delta;
                }
            }
        }

        void AggregateGradients(WeightType[][][] grads, WeightType[][] aggregated)
        {
            Parallel.For(0, grads.Length, threadID =>
            {
                var gradsPerThread = grads[threadID];
                for(var nTupleID = 0; nTupleID <gradsPerThread.Length; nTupleID++)
                {
                    var ag = aggregated[nTupleID];
                    var g = gradsPerThread[nTupleID];
                    for (var feature = 0; feature < g.Length; feature++)
                        AtomicOperations.Add(ref ag[feature], g[feature]);
                }
            });
        }

        void ApplyGradients(WeightType[][] grads, WeightType biasGrad)
        {
            var eta = this.options.LearningRate;
            for(var nTupleID = 0; nTupleID < grads.Length; nTupleID++)
            {
                var w = this.valueFunc.GetWeights(DiscColor.Black, nTupleID);
                var gradsPerNTuple = grads[nTupleID];
                var g2 = this.gradsPow2Sums[nTupleID];
                Parallel.For(0, gradsPerNTuple.Length, feature =>
                {
                    var g = gradsPerNTuple[feature];
                    g2[feature] += g * g;
                    w[feature] += eta / WeightType.Sqrt(g2[feature]) * g;
                });
            }

            this.biasGradPow2Sum += biasGrad * biasGrad;
            this.valueFunc.Bias += eta / WeightType.Sqrt(this.biasGradPow2Sum) * biasGrad;
        }

        static WeightType BinaryCrossEntropy(WeightType y, WeightType t)
            => -(t * WeightType.Log(y + WeightType.Epsilon)
            + (WeightType.One - t) * WeightType.Log(WeightType.One - y + WeightType.Epsilon));
    }
}
