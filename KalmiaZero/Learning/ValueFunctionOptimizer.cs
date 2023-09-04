using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;
using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using System.Runtime.CompilerServices;

namespace KalmiaZero.Learning
{
    public struct ValueFuncOptimizerOptions<WeightType> where WeightType : IFloatingPointIeee754<WeightType>
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

    struct BatchItem<FloatType> where FloatType : IFloatingPointIeee754<FloatType>
    {
        public Bitboard Input;
        public FloatType Output;
    }

    class ValueFuncOptimizer<WeightType> where WeightType : IFloatingPointIeee754<WeightType>
    {
        ValueFunction<WeightType> valueFunc;
        WeightType[][] gradsPow2Sums;
        List<WeightType> trainLossHistory = new();
        List<WeightType> testLossHistory = new();
        ValueFuncOptimizerOptions<WeightType> options;
        ParallelOptions parallelOptions;

        public ValueFuncOptimizer(string workDirPath, ValueFunction<WeightType> valueFunc, ref ValueFuncOptimizerOptions<WeightType> options)
        {
            this.options = options;
            this.parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.NumThreads };
            this.valueFunc = valueFunc;
            this.gradsPow2Sums = new WeightType[valueFunc.NTuples.Length][];
            for (var nTupleID = 0; nTupleID < valueFunc.NTuples.Length; nTupleID++)
                gradsPow2Sums[nTupleID] = new WeightType[valueFunc.GetWeights(DiscColor.Black, nTupleID).Length];
        }

        WeightType CalculateGradients(BatchItem<WeightType>[] batch, WeightType[][][] grads)
        {
            throw new NotImplementedException();
            //var featureVector = (from _ in Enumerable.Range(0, options.NumThreads) 
            //                     select new PositionFeatureVector(this.valueFunc.NTuples.ToArray())).ToArray();
            //var loss = new WeightType[options.NumThreads];

            //foreach (var g in grads)
            //    foreach (var gPerThread in g)
            //        Array.Clear(gPerThread);

            //var numItemsPerThread = batch.Length / this.options.NumThreads;
            //var restItems = batch.Length % this.options.NumThreads;
            //Parallel.For(0, this.options.NumThreads, threadID =>
            //{
            //    var pfVec = featureVector[threadID];
            //    var offset = numItemsPerThread * threadID;
            //    var gradPerThread = grads[threadID];

            //    for (var i = 0; i < numItemsPerThread; i++)
            //    {
            //        // ここら辺は後でローカル関数化
            //        // あとbias項も必要では？
            //        var pos = new Position(ref batch[i].Input, DiscColor.Black);
            //        var y = Predict(pfVec, ref pos);
            //        var delta = y - batch[i].Output;
            //        loss[threadID] += BinaryCrossEntropy(y, batch[i].Output);

            //        for (var nTupleID = 0; nTupleID < pfVec.NumNTuples; nTupleID++)
            //        {
            //            var g = gradPerThread[nTupleID];
            //            ReadOnlySpan<int> features = pfVec.GetFeatures(nTupleID);
            //            for (var j = 0; j < features.Length; j++)
            //                g[features[j]] += delta;
            //        }
            //    }
            //});
        }

        [SkipLocalsInit]
        WeightType Predict(PositionFeatureVector pfVec, ref Position pos)
        {
            Span<Move> legalMoves = stackalloc Move[Constants.NUM_SQUARES];
            pfVec.Init(ref pos, legalMoves[..pos.GetNextMoves(ref legalMoves)]);
            return this.valueFunc.Predict(pfVec);
        }

        static WeightType BinaryCrossEntropy(WeightType y, WeightType t)
            => -(t * WeightType.Log(y + WeightType.Epsilon)
            + (WeightType.One - t) * WeightType.Log(WeightType.One - y + WeightType.Epsilon));
    }
}
