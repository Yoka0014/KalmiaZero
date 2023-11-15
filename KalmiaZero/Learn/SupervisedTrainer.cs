using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero.Learn
{
    using static Reversi.Constants;
    using static NTuple.PositionFeaturesConstantConfig;

    public record class SupervisedTrainerConfig<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public int NumEpoch { get; init; } = 200;
        public WeightType LearningRate { get; init; } = WeightType.CreateChecked(0.1);
        public WeightType Epsilon { get; init; } = WeightType.CreateChecked(1.0e-7);
        public int Pacience { get; init; } = 0;

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string WeightsFileName { get; init; } = "value_func_weights_sl";
        public string LossHistroyFileName { get; init; } = "loss_histroy";
        public int SaveWeightsInterval { get; init; } = 10;
        public bool SaveOnlyLatestWeights = true;
    }

    public class SupervisedTrainer<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public string Label { get; }
        readonly SupervisedTrainerConfig<WeightType> CONFIG;
        readonly string WEIGHTS_FILE_PATH;
        readonly string LOSS_HISTORY_FILE_PATH;
        readonly StringBuilder logger = new();

        readonly PositionFeatureVector featureVec;
        readonly ValueFunction<WeightType> valueFunc;
        readonly WeightType[] weightGrads;
        WeightType biasGrad;
        readonly WeightType[] weightGradSquareSums;
        WeightType biasGradSquareSum;
        WeightType prevTrainLoss;
        WeightType prevTestLoss;
        List<(WeightType trainLoss, WeightType testLoss)> lossHistory = new();
        int overfittingCount;

        public SupervisedTrainer(ValueFunction<WeightType> valueFunc, SupervisedTrainerConfig<WeightType> config)
            : this(string.Empty, valueFunc, config) { }

        public SupervisedTrainer(string label, ValueFunction<WeightType> valueFunc, SupervisedTrainerConfig<WeightType> config)
        {
            this.Label = label;
            this.CONFIG = config;
            this.WEIGHTS_FILE_PATH = Path.Combine(this.CONFIG.WorkDir, $"{config.WeightsFileName}_{"{0}"}.bin");
            this.LOSS_HISTORY_FILE_PATH = Path.Combine(this.CONFIG.WorkDir, $"{config.LossHistroyFileName}.txt");
            this.valueFunc = valueFunc;
            this.featureVec = new PositionFeatureVector(this.valueFunc.NTuples);

            var weights = this.valueFunc.Weights;
            this.weightGrads = new WeightType[weights.Length];
            this.weightGradSquareSums = new WeightType[weights.Length];
        }

        public (WeightType trainLoss, WeightType testLoss) Train(TrainData[] trainData, TrainData[] testData, bool saveWeights = true, bool saveLossHistroy = true)
        {
            this.logger.Clear();
            Array.Clear(this.weightGradSquareSums);
            this.biasGradSquareSum = WeightType.Zero;
            this.prevTrainLoss = this.prevTestLoss = WeightType.PositiveInfinity;
            this.overfittingCount = 0;

            WriteLabel();
            this.logger.AppendLine("Start learning.\n");
            Console.WriteLine(this.logger.ToString());
            this.logger.Clear();

            var continueFlag = true;
            for (var epoch = 0; epoch < this.CONFIG.NumEpoch && continueFlag; epoch++)
            {
                WriteLabel();
                continueFlag = ExecuteOneEpoch(trainData, testData);
                this.logger.Append("Epoch ").Append(epoch + 1).AppendLine(" has done.");

                if ((epoch + 1) % this.CONFIG.SaveWeightsInterval == 0)
                {
                    if(saveWeights)
                        SaveWeights(epoch + 1);

                    if(saveLossHistroy)
                        SaveLossHistroy();
                }

                Console.WriteLine(this.logger.ToString());
                this.logger.Clear();
            }

            if(saveWeights)
                SaveWeights(this.CONFIG.NumEpoch);

            if(saveLossHistroy)
                SaveLossHistroy();

            return this.lossHistory[^1];
        }

        void WriteLabel()
        {
            if (!string.IsNullOrEmpty(this.Label))
                this.logger.AppendLine($"[{this.Label}]");
        }

        static WeightType CalcAdaFactor(WeightType x) => WeightType.One / WeightType.Sqrt(x + WeightType.Epsilon);

        bool ExecuteOneEpoch(TrainData[] trainData, TrainData[] testData)
        {
            Array.Clear(this.weightGrads);
            this.biasGrad = WeightType.Zero;

            var testLoss = CalculateLoss(testData);

            this.logger.Append("test loss: ").Append(testLoss).Append('\n');

            var testLossDiff = testLoss - prevTestLoss;
            if (testLossDiff > this.CONFIG.Epsilon && ++this.overfittingCount > this.CONFIG.Pacience)
            {
                this.logger.AppendLine("early stopping.");
                return false;
            }

            var trainLoss = CalculateGradients(trainData);
            this.logger.Append("train loss: ").Append(trainLoss).Append('\n');

            this.lossHistory.Add((trainLoss, testLoss));

            var trainLossDiff = trainLoss - prevTrainLoss;
            if (WeightType.Abs(trainLossDiff) < this.CONFIG.Epsilon)
            {
                this.logger.AppendLine("converged.");
                return false;
            }

            ApplyGradients();

            return true;
        }

        void SaveWeights(int epoch)
        {
            var weightsLabel = this.CONFIG.SaveOnlyLatestWeights ? "latest" : epoch.ToString();
            var path = string.Format(this.WEIGHTS_FILE_PATH, weightsLabel);
            this.valueFunc.SaveToFile(path);
        }

        void SaveLossHistroy()
        {
            var trainLossSb = new StringBuilder("[");
            var testLossSb = new StringBuilder("[");
            foreach ((var trainLoss, var testLoss) in this.lossHistory)
            {
                trainLossSb.Append(trainLoss).Append(", ");
                testLossSb.Append(testLoss).Append(", ");
            }

            // remove last ", "
            trainLossSb.Remove(trainLossSb.Length - 2, 2);
            testLossSb.Remove(testLossSb.Length - 2, 2);

            trainLossSb.Append(']');
            testLossSb.Append(']');

            using var sw = new StreamWriter(this.LOSS_HISTORY_FILE_PATH);
            sw.WriteLine(trainLossSb.ToString());
            sw.WriteLine(testLossSb.ToString());
        }

        unsafe WeightType CalculateGradients(TrainData[] trainData)
        {
            var loss = WeightType.Zero;
            var count = 0;
            Span<Move> moves = stackalloc Move[MAX_NUM_MOVES];
            var numMoves = 0;

            fixed(int* nTupleOffset = this.valueFunc.NTupleOffset)
            fixed (WeightType* w = this.valueFunc.Weights)
            fixed (WeightType* wg = this.weightGrads)
            fixed (WeightType* wg2 = this.weightGradSquareSums)
            {
                for (var i = 0; i < trainData.Length; i++)
                {
                    ref var data = ref trainData[i];
                    var pos = data.RootPos;

                    if (NUM_SQUARE_STATES == 4)
                        numMoves = pos.GetNextMoves(ref moves);

                    this.featureVec.Init(ref pos, moves[..numMoves]);

                    for (var j = 0; j < data.Moves.Length; j++)
                    {
                        var reward = GetReward(ref data, pos.SideToMove, pos.EmptySquareCount);
                        var value = this.valueFunc.PredictWithBlackWeights(this.featureVec);
                        var delta = value - reward;
                        loss += LossFunctions.BinaryCrossEntropy(value, reward);
                        count++;

                        if (pos.SideToMove == DiscColor.Black)
                            calcGrads<Black>(nTupleOffset, w, wg, delta);
                        else
                            calcGrads<White>(nTupleOffset, w, wg, delta);

                        ref var nextMove = ref data.Moves[j];
                        if (nextMove.Coord != BoardCoordinate.Pass)
                        {
                            pos.Update(ref nextMove);

                            if (NUM_SQUARE_STATES == 4)
                                numMoves = pos.GetNextMoves(ref moves);

                            this.featureVec.Update(ref nextMove, moves[..numMoves]);
                        }
                        else
                        {
                            pos.Pass();

                            if (NUM_SQUARE_STATES == 4)
                                numMoves = pos.GetNextMoves(ref moves);

                            this.featureVec.Pass(moves[..numMoves]);
                        }
                    }
                }
            }

            return loss / WeightType.CreateChecked(count);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe void calcGrads<DiscColor>(int* nTupleOffset, WeightType* weights, WeightType* weightGrads, WeightType delta) where DiscColor : struct, IDiscColor
            {
                for (var nTupleID = 0; nTupleID < this.featureVec.NTuples.Length; nTupleID++)
                {
                    var offset = nTupleOffset[nTupleID];
                    var w = weights + offset;
                    var wg = weightGrads + offset;
                    ref Feature feature = ref this.featureVec.GetFeature(nTupleID);
                    fixed (FeatureType* opp = this.valueFunc.NTuples.GetOpponentFeatureRawTable(nTupleID))
                    fixed (FeatureType* mirror = this.featureVec.NTuples.GetMirroredFeatureRawTable(nTupleID))
                    {
                        for (var k = 0; k < feature.Length; k++)
                        {
                            var f = (typeof(DiscColor) == typeof(Black)) ? feature[k] : opp[feature[k]];
                            wg[f] += delta;
                            wg[mirror[f]] += delta;
                        }
                    }
                }
                this.biasGrad += delta;
            }
        }

        unsafe void ApplyGradients()
        {
            var eta = this.CONFIG.LearningRate;

            fixed (WeightType* w = this.valueFunc.Weights)
            fixed (WeightType* wg = this.weightGrads)
            fixed (WeightType* wg2 = this.weightGradSquareSums)
            {
                for (var i = 0; i < this.valueFunc.Weights.Length; i++)
                {
                    var g = wg[i];
                    wg2[i] += g * g;
                    w[i] -= eta * CalcAdaFactor(wg2[i]) * g;
                }
            }

            this.biasGradSquareSum += this.biasGrad * this.biasGrad;
            this.valueFunc.Bias -= eta * CalcAdaFactor(this.biasGradSquareSum) * this.biasGrad;
        }

        WeightType CalculateLoss(TrainData[] testData)
        {
            var loss = WeightType.Zero;
            var count = 0;
            Span<Move> moves = stackalloc Move[MAX_NUM_MOVES];
            var numMoves = 0;
            for (var i = 0; i < testData.Length; i++)
            {
                ref var data = ref testData[i];
                var pos = data.RootPos;

                if (NUM_SQUARE_STATES == 4)
                    numMoves = pos.GetNextMoves(ref moves);

                this.featureVec.Init(ref pos, moves[..numMoves]);

                for (var j = 0; j < data.Moves.Length; j++)
                {
                    var reward = GetReward(ref data, pos.SideToMove, pos.EmptySquareCount);
                    loss += LossFunctions.BinaryCrossEntropy(this.valueFunc.PredictWithBlackWeights(this.featureVec), reward);
                    count++;

                    ref var move = ref data.Moves[j];
                    if (move.Coord != BoardCoordinate.Pass)
                    {
                        pos.Update(ref move);

                        if (NUM_SQUARE_STATES == 4)
                            numMoves = pos.GetNextMoves(ref moves);

                        this.featureVec.Update(ref move, moves[..numMoves]);
                    }
                    else
                    {
                        pos.Pass();

                        if (NUM_SQUARE_STATES == 4)
                            numMoves = pos.GetNextMoves(ref moves);

                        this.featureVec.Pass(moves[..numMoves]);
                    }
                }
            }
            return loss / WeightType.CreateChecked(count);
        }

        static WeightType GetReward(ref TrainData data, DiscColor sideToMove, int emptySquareCount)
        {
            var score = (emptySquareCount >= data.TheoreticalScoreDepth) ? data.TheoreticalScoreFromBlack : data.ScoreFromBlack;
            if (sideToMove != DiscColor.Black)
                score *= -1;

            if (score == 0)
                return WeightType.One / (WeightType.One + WeightType.One);

            return (score > 0) ? WeightType.One : WeightType.Zero;
        }
    }
}