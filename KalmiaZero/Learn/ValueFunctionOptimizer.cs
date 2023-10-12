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
using System.IO;
using System.Runtime.CompilerServices;

namespace KalmiaZero.Learn
{
    public struct ValueFuncOptimizerOptions<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public int NumEpoch { get; set; }
        public WeightType LearningRate { get; set; } = WeightType.CreateChecked(0.01);
        public int CheckpointInterval { get; set; } = 10;
        public WeightType Epsilon { get; set; } = WeightType.CreateChecked(1.0e-6);
        public int Patience { get; set; } = 5;
        public int NumThreads { get; set; } = Environment.ProcessorCount;
        public string TrainDataPath { get; set; } = "train_data.bin";
        public string TestDataPath { get; set; } = "test_data.bin";
        public string WeightsFileName { get; set; } = "value_func_weights.bin";
        public string LossHistoryPath { get; set; } = "loss_history.txt";

        public ValueFuncOptimizerOptions() { }
    }

    struct BatchItem<FloatType> where FloatType : unmanaged, IFloatingPointIeee754<FloatType>
    {
        public Bitboard Input;
        public FloatType Output;
    }

    class ValueFuncOptimizer<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        readonly string WORK_DIR_PATH;
        ValueFunction<WeightType> valueFunc;
        WeightType[][] gradsPow2Sums;
        WeightType biasGradPow2Sum;
        ValueFuncOptimizerOptions<WeightType> options;
        ParallelOptions parallelOptions;
        List<(WeightType trainLoss, WeightType testLoss)> lossHistroy = new();

        public ValueFuncOptimizer(string workDirPath, ValueFunction<WeightType> valueFunc, ValueFuncOptimizerOptions<WeightType> options)
        {
            this.WORK_DIR_PATH = workDirPath;
            this.options = options;
            this.parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.NumThreads };
            this.valueFunc = valueFunc;
            this.gradsPow2Sums = new WeightType[valueFunc.NTuples.Length][];
            this.biasGradPow2Sum = WeightType.Zero;
            for (var nTupleID = 0; nTupleID < valueFunc.NTuples.Length; nTupleID++)
                gradsPow2Sums[nTupleID] = new WeightType[valueFunc.GetWeights(DiscColor.Black, nTupleID).Length];
        }

        public void StartOptimization()
        {
            Console.WriteLine("create batch...");
            var trainBatch = CreateBatch(this.options.TrainDataPath);
            var testBatch = CreateBatch(this.options.TestDataPath);
            Console.WriteLine("done.");

            NTuples nTuples = this.valueFunc.NTuples;

            var grads = (from threadID in Enumerable.Range(0, this.options.NumThreads)
                         select (from nTupleID in Enumerable.Range(0, nTuples.Length)
                                 select new WeightType[nTuples.NumPossibleFeatures[nTupleID]]).ToArray()).ToArray();

            var aggregatedGrads = (from nTupleID in Enumerable.Range(0, nTuples.Length) 
                                   select new WeightType[nTuples.NumPossibleFeatures[nTupleID]]).ToArray();

            var biasGrad = new WeightType[this.options.NumThreads];

            var overfittingCount = 0;
            var testLoss = WeightType.Zero;
            var bestTestLoss = WeightType.PositiveInfinity;
            var prevTrainLoss = WeightType.PositiveInfinity;
            var prevTestLoss = WeightType.PositiveInfinity;
            for(var epoch = 0; epoch < this.options.NumEpoch; epoch++)
            {
                Console.WriteLine($"epoch {epoch}:");

                testLoss = CalculateLoss(testBatch);
                Console.WriteLine($"test loss: {testLoss}");

                var testLossDiff = testLoss - prevTestLoss;
                if (testLossDiff > this.options.Epsilon && ++overfittingCount > this.options.Patience)
                {
                    Console.WriteLine("early stopping.");
                    break;
                }

                var trainLoss = CalculateGradients(trainBatch, grads, biasGrad);
                Console.WriteLine($"train loss: {trainLoss}");

                this.lossHistroy.Add((trainLoss, testLoss));

                var trainLossDiff = trainLoss - prevTrainLoss;
                if(WeightType.Abs(trainLossDiff) < this.options.Epsilon)
                {
                    Console.WriteLine("converged.");
                    SaveWeights();
                    break;
                }

                if ((epoch + 1) % this.options.CheckpointInterval == 0)
                {
                    if (testLoss <= bestTestLoss)
                    {
                        bestTestLoss = testLoss;
                        Console.WriteLine("checkpoint.\nsaving weights...");
                        SaveWeights();
                        Console.WriteLine("done.");
                    }
                }

                AggregateGradients(grads, aggregatedGrads);
                ApplyGradients(aggregatedGrads, biasGrad.Sum());
            }

            if (testLoss <= bestTestLoss)
            {
                bestTestLoss = testLoss;
                Console.WriteLine("saving weights...");
                SaveWeights();
                Console.WriteLine("done.");
            }
        }

        void SaveWeights()
        {
            this.valueFunc.CopyWeightsBlackToWhite();
            this.valueFunc.SaveToFile(Path.Combine(this.WORK_DIR_PATH, this.options.WeightsFileName));

            var trainLossSb = new StringBuilder("[");
            var testLossSb = new StringBuilder("[");   
            foreach((var trainLoss, var testLoss) in this.lossHistroy)
            {
                trainLossSb.Append(trainLoss).Append(", ");
                testLossSb.Append(testLoss).Append(", ");
            }
            trainLossSb.Remove(trainLossSb.Length - 2, 2);
            testLossSb.Remove(testLossSb.Length - 2, 2);
            trainLossSb.Append(']');
            testLossSb.Append(']');

            using var sw = new StreamWriter(Path.Combine(this.WORK_DIR_PATH, this.options.LossHistoryPath));
            sw.WriteLine(trainLossSb.ToString());
            sw.WriteLine(testLossSb.ToString());
        }

        static BatchItem<WeightType>[] CreateBatch(string path)
        {
            TrainDataItem[] data = TrainData.LoadFromFile(path);
            var batch = new BatchItem<WeightType>[data.Length];

            for(var i = 0; i < batch.Length; i++)
            {
                ref var batchItem = ref batch[i];
                ref var dataItem = ref data[i];

                batchItem.Input = dataItem.Position;
                if (dataItem.FinalDiscDiff == 0)
                    batchItem.Output = WeightType.One / (WeightType.One + WeightType.One);  // 0.5
                else
                    batchItem.Output = (dataItem.FinalDiscDiff > 0) ? WeightType.One : WeightType.Zero;
            }

            return batch;
        }

        WeightType CalculateGradients(BatchItem<WeightType>[] batch, WeightType[][][] grads, WeightType[] biasGrad)
        {
            foreach (var g in grads)
                foreach (var gPerThread in g)
                    Array.Clear(gPerThread);

            var nTuples = this.valueFunc.NTuples;
            var numItemsPerThread = batch.Length / this.options.NumThreads;
            var numRestItems = batch.Length % this.options.NumThreads;

            var loss = WeightType.Zero;
            Parallel.For(0, this.options.NumThreads, this.parallelOptions, threadID =>
            {
                var l = kernel(threadID, batch.AsSpan(threadID * numItemsPerThread, numItemsPerThread));
                AtomicOperations.Add(ref loss, l);
            });

            loss += kernel(0, batch.AsSpan(this.options.NumThreads * numItemsPerThread, numRestItems));

            return loss / WeightType.CreateChecked(batch.Length);

            [SkipLocalsInit]
            WeightType kernel(int threadID, Span<BatchItem<WeightType>> batch)
            {
                var loss = WeightType.Zero;
                var gradPerThread = grads[threadID];
                var featureVec = new PositionFeatureVector(this.valueFunc.NTuples);
                Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
                for (var i = 0; i < batch.Length; i++)
                {
                    ref var batchItem = ref batch[i];
                    var pos = new Position(batchItem.Input, DiscColor.Black);
                    var numMoves = pos.GetNextMoves(ref moves);
                    featureVec.Init(ref pos, moves[..numMoves]);
                    var y = this.valueFunc.Predict(featureVec);
                    var delta = y - batch[i].Output;
                    loss += LossFunctions.BinaryCrossEntropy(y, batch[i].Output);

                    for (var nTupleID = 0; nTupleID < nTuples.Length; nTupleID++)
                    {
                        var g = gradPerThread[nTupleID];
                        ref Feature feature = ref featureVec.GetFeature(nTupleID);
                        ReadOnlySpan<FeatureType> mirror = nTuples.GetMirroredFeatureTable(nTupleID);
                        for (var j = 0; j < feature.Length; j++)
                        {
                            var f = feature[j];
                            g[f] += delta;
                            g[mirror[f]] += delta;  // If f == mirror[f], twice grad is added to g[f], but this is not a problem because AdaGrad is adaptive algorithm.
                        }
                    }

                    biasGrad[threadID] += delta;
                }
                return loss;
            }
        }

        void AggregateGradients(WeightType[][][] grads, WeightType[][] aggregated)
        {
            foreach (var g in aggregated)
                Array.Clear(g);

            Parallel.For(0, grads.Length, this.parallelOptions, threadID =>
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

        unsafe void ApplyGradients(WeightType[][] grads, WeightType biasGrad)
        {
            var eta = this.options.LearningRate;

            fixed (WeightType* weight = this.valueFunc.Weights)
            fixed(int* nTupleOffset = this.valueFunc.NTupleOffset)
            {
                for (var nTupleID = 0; nTupleID < grads.Length; nTupleID++)
                {
                    var w = weight + nTupleOffset[nTupleID];
                    var gradsPerNTuple = grads[nTupleID];
                    var g2 = this.gradsPow2Sums[nTupleID];
                    Parallel.For(0, gradsPerNTuple.Length, this.parallelOptions, feature =>
                    {
                        var g = gradsPerNTuple[feature];
                        g2[feature] += g * g;
                        w[feature] -= eta * g / (WeightType.Sqrt(g2[feature]) + WeightType.Epsilon);
                    });
                }
            }

            this.biasGradPow2Sum += biasGrad * biasGrad;
            this.valueFunc.Bias -= eta * biasGrad / (WeightType.Sqrt(this.biasGradPow2Sum) + WeightType.Epsilon);
        }

        WeightType CalculateLoss(BatchItem<WeightType>[] batch)
        {
            var numItemsPerThread = batch.Length / this.options.NumThreads;
            var numRestItems = batch.Length % this.options.NumThreads;

            var loss = WeightType.Zero;
            Parallel.For(0, this.options.NumThreads, this.parallelOptions, 
                threadID => AtomicOperations.Add(ref loss, calcLoss(batch.AsSpan(threadID * numItemsPerThread, numItemsPerThread))));

            loss += calcLoss(batch.AsSpan(this.options.NumThreads * numItemsPerThread, numRestItems));

            return loss / WeightType.CreateChecked(batch.Length);

            [SkipLocalsInit]
            WeightType calcLoss(Span<BatchItem<WeightType>> batch)
            {
                var featureVec = new PositionFeatureVector(this.valueFunc.NTuples);
                Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
                var loss = WeightType.Zero;
                foreach (var item in batch)
                {
                    var pos = new Position(item.Input, DiscColor.Black);
                    var numMoves = pos.GetNextMoves(ref moves);
                    featureVec.Init(ref pos, moves[..numMoves]);
                    loss += LossFunctions.BinaryCrossEntropy(this.valueFunc.Predict(featureVec), item.Output);
                }
                return loss;
            }
        }
    }
}
