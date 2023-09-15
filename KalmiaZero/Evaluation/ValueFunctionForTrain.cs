﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;
using System.Runtime.CompilerServices;

namespace KalmiaZero.Evaluation
{
    /// <summary>
    /// ValueFunction for training. 
    /// </summary>
    /// 
    /// <remarks>
    /// This class holds parameters(weights) of value function as IEEE754 floating point numbers.
    /// This class focuses on precision of parameters rather than speed, as this is important in training.
    /// </remarks>
    /// 
    /// <seealso cref="ValueFunction"/>
    /// <typeparam name="WeightType">the type of parameters</typeparam>
    public partial class ValueFunctionForTrain<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        const string LABEL = "KalmiaZero";
        const string LABEL_INVERSED = "oreZaimlaK";
        const int LABEL_SIZE = 10;

        public NTuples NTuples { get; }

        public WeightType[] Weights { get; private set; }
        public WeightType Bias { get; set; }

        public ReadOnlySpan<int> DiscColorOffset => this.DISC_COLOR_OFFSET;
        public ReadOnlySpan<int> NTupleOffset => this.N_TUPLE_OFFSET;

        readonly int[] DISC_COLOR_OFFSET;
        readonly int[] N_TUPLE_OFFSET;

        public ValueFunctionForTrain(NTuples nTuples)
        {
            this.NTuples = nTuples;
            this.Weights = new WeightType[2 * nTuples.NumPossibleFeatures.Sum()];
            this.DISC_COLOR_OFFSET = new int[2] { 0, this.Weights.Length / 2 };
            this.Bias = WeightType.Zero;

            this.N_TUPLE_OFFSET = new int[nTuples.Length];
            this.N_TUPLE_OFFSET[0] = 0;
            for(var i = 1; i < N_TUPLE_OFFSET.Length; i++)
                this.N_TUPLE_OFFSET[i] += this.N_TUPLE_OFFSET[i - 1] + nTuples.NumPossibleFeatures[i - 1];
        }

        /*
         * Format:
         * 
         * offset = 0:  label(for endianess check)
         * offset = 10: the number of N-Tuples
         * offset = 14: N-Tuple's coordinates
         * offset = M: the size of weight
         * offset = M + 4: weights
         * offset = -1: bias
         */
        public static ValueFunctionForTrain<WeightType> LoadFromFile(string filePath)
        {
            const int BUFFER_SIZE = 16;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            Span<byte> buffer = stackalloc byte[BUFFER_SIZE];
            fs.Read(buffer[..LABEL_SIZE]);
            var label = Encoding.ASCII.GetString(buffer[..LABEL_SIZE]);
            var swapBytes = label == LABEL_INVERSED;

            if (!swapBytes && label != LABEL)
                throw new InvalidDataException($"The format of \"{filePath}\" is invalid.");

            // load N-Tuples
            fs.Read(buffer[..sizeof(int)], swapBytes);
            var numNTuples = BitConverter.ToInt32(buffer);
            var nTuples = new NTupleInfo[numNTuples];
            for (var i = 0; i < nTuples.Length; i++)
            {
                fs.Read(buffer[..sizeof(int)], swapBytes);
                var size = BitConverter.ToInt32(buffer);
                var coords = new BoardCoordinate[size];
                for (var j = 0; j < size; j++)
                    coords[j] = (BoardCoordinate)fs.ReadByte();
                nTuples[i] = new NTupleInfo(coords);
            }

            var valueFunc = new ValueFunctionForTrain<WeightType>(new NTuples(nTuples));

            // load weights
            fs.Read(buffer[..sizeof(int)], swapBytes);
            var weightSize = BitConverter.ToInt32(buffer);
            if (weightSize != 2 && weightSize != 4 && weightSize != 8)
                throw new InvalidDataException($"The size {weightSize} is invalid for weight.");

            var packedWeights = new WeightType[nTuples.Length][];
            for (var nTupleID = 0; nTupleID < packedWeights.Length; nTupleID++)
            {
                fs.Read(buffer[..sizeof(int)], swapBytes);
                var size = BitConverter.ToInt32(buffer);
                var pw = packedWeights[nTupleID] = new WeightType[size];
                for (var i = 0; i < pw.Length; i++)
                {
                    fs.Read(buffer[..weightSize], swapBytes);
                    if (weightSize == 2)
                        pw[i] = WeightType.CreateChecked(BitConverter.ToHalf(buffer));
                    else if (weightSize == 4)
                        pw[i] = WeightType.CreateChecked(BitConverter.ToSingle(buffer));
                    else if (weightSize == 8)
                        pw[i] = WeightType.CreateChecked(BitConverter.ToDouble(buffer));
                }
            }

            fs.Read(buffer[..weightSize], swapBytes); 
            if (weightSize == 2)
                valueFunc.Bias = WeightType.CreateChecked(BitConverter.ToHalf(buffer));
            else if (weightSize == 4)
                valueFunc.Bias = WeightType.CreateChecked(BitConverter.ToSingle(buffer));
            else if (weightSize == 8)
                valueFunc.Bias = WeightType.CreateChecked(BitConverter.ToDouble(buffer));

            // expand weights
            valueFunc.Weights= valueFunc.ExpandPackedWeights(packedWeights);
            valueFunc.CopyWeightsBlackToWhite();

            return valueFunc;
        }

        public Span<WeightType> GetWeights(DiscColor color, int nTupleID)
            => Weights.AsSpan(this.DISC_COLOR_OFFSET[(int)color] + this.N_TUPLE_OFFSET[nTupleID], this.NTuples.NumPossibleFeatures[nTupleID]);

        public void InitWeightsWithUniformRand(WeightType min, WeightType max) => InitWeightsWithUniformRand(Random.Shared, min, max);

        public void InitWeightsWithUniformRand(Random rand, WeightType min, WeightType max)
        {
            for (var nTupleID = 0; nTupleID < this.NTuples.Length; nTupleID++)
            {
                var w = this.Weights.AsSpan(N_TUPLE_OFFSET[nTupleID]);
                ReadOnlySpan<FeatureType> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                for (var feature = 0; feature < this.NTuples.NumPossibleFeatures[nTupleID]; feature++)
                {
                    var mirrored = mirror[feature];
                    if (feature <= mirrored)
                    {
                        if (typeof(WeightType) == typeof(Half) || typeof(WeightType) == typeof(float))
                            w[feature] = WeightType.CreateChecked(rand.NextSingle()) * (max - min) + min;
                        else if (typeof(WeightType) == typeof(double))
                            w[feature] = WeightType.CreateChecked(rand.NextDouble()) * (max - min) + min;
                    }
                    else
                        w[feature] = w[mirrored];
                }
            }
            CopyWeightsBlackToWhite();

            this.Bias = WeightType.Zero;
        }

        public void CopyWeightsBlackToWhite()
        {
            var whiteOffset = this.DISC_COLOR_OFFSET[(int)DiscColor.White];
            Span<WeightType> bWeights = Weights.AsSpan(0, whiteOffset);
            Span<WeightType> wWeights = Weights.AsSpan(whiteOffset);

            for (var nTupleID = 0; nTupleID < this.N_TUPLE_OFFSET.Length; nTupleID++)
            {
                var bw = bWeights[N_TUPLE_OFFSET[nTupleID]..];
                var ww = wWeights[N_TUPLE_OFFSET[nTupleID]..];
                ReadOnlySpan<FeatureType> toOpponent = this.NTuples.GetOpponentFeatureTable(nTupleID);
                for (var feature = 0; feature < toOpponent.Length; feature++)
                    ww[feature] = bw[toOpponent[feature]];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe WeightType PredictLogit(PositionFeatureVector posFeatureVec)
        {
            var x = WeightType.Zero;
            fixed(int* discColorOffset = this.DISC_COLOR_OFFSET)
            fixed (WeightType* weights = &this.Weights[discColorOffset[(int)posFeatureVec.SideToMove]])
            fixed (Feature* features = posFeatureVec.Features)
            {
                for (var nTupleID = 0; nTupleID < this.N_TUPLE_OFFSET.Length; nTupleID++)
                {
                    var w = weights + this.N_TUPLE_OFFSET[nTupleID];
                    ref Feature feature = ref features[nTupleID];
                    for (var i = 0; i < feature.Length; i++)
                        x += w[feature[i]];
                }
            }

            return x + this.Bias;
        }

        public WeightType Predict(PositionFeatureVector pf) => StdSigmoid(PredictLogit(pf));

        /*
         * Format:
         * 
         * offset = 0:  label(for endianess check)
         * offset = 10: the number of N-Tuples
         * offset = 14: N-Tuple's coordinates
         * offset = M: the size of weight
         * offset = M + 4: weights
         * offset = -1: bias
         */
        public void SaveToFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            fs.Write(Encoding.ASCII.GetBytes(LABEL), 0, LABEL_SIZE);

            // save N-Tuples
            ReadOnlySpan<NTupleInfo> tuples = this.NTuples.Tuples;
            fs.Write(BitConverter.GetBytes(tuples.Length));
            for (var nTupleID = 0; nTupleID < tuples.Length; nTupleID++)
            {
                var coords = tuples[nTupleID].GetCoordinates(0);
                fs.Write(BitConverter.GetBytes(coords.Length));
                foreach (var coord in coords)
                    fs.WriteByte((byte)coord);
            }

            // save weights
            var packedWeights = PackWeights();
            var weightSize = Marshal.SizeOf<WeightType>();
            fs.Write(BitConverter.GetBytes(weightSize));
            Span<byte> weightBytes = stackalloc byte[weightSize];
            for (var nTupleID = 0; nTupleID < packedWeights.Length; nTupleID++)
            {
                var pw = packedWeights[nTupleID];
                fs.Write(BitConverter.GetBytes(pw.Length));
                foreach (var v in pw)
                {
                    if (typeof(WeightType) == typeof(Half))
                        fs.Write(BitConverter.GetBytes(Half.CreateChecked(v)));
                    else if (typeof(WeightType) == typeof(float))
                        fs.Write(BitConverter.GetBytes(float.CreateChecked(v)));
                    else if (typeof(WeightType) == typeof(double))
                        fs.Write(BitConverter.GetBytes(double.CreateChecked(v)));
                }
            }

            if (typeof(WeightType) == typeof(Half))
                fs.Write(BitConverter.GetBytes(Half.CreateChecked(this.Bias)));
            else if (typeof(WeightType) == typeof(float))
                fs.Write(BitConverter.GetBytes(float.CreateChecked(this.Bias)));
            else if (typeof(WeightType) == typeof(double))
                fs.Write(BitConverter.GetBytes(double.CreateChecked(this.Bias)));
        }

        WeightType[][] PackWeights()
        {
            var packedWeights = (from _ in Enumerable.Range(0, Weights.Length) select new List<WeightType>()).ToArray();
            var numPossibleFeatures = this.NTuples.NumPossibleFeatures;
            for (var nTupleID = 0; nTupleID < this.N_TUPLE_OFFSET.Length; nTupleID++)
            {
                var w = this.Weights.AsSpan(this.N_TUPLE_OFFSET[nTupleID], numPossibleFeatures[nTupleID]);
                var pw = packedWeights[nTupleID];
                ReadOnlySpan<FeatureType> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                for (var feature = 0; feature < w.Length; feature++)
                    if (feature <= mirror[feature])
                        pw.Add(w[feature]);
            }
            return packedWeights.Select(n => n.ToArray()).ToArray();
        }

        WeightType[] ExpandPackedWeights(WeightType[][] packedWeights)
        {
            var weights = new WeightType[2 * this.NTuples.NumPossibleFeatures.Sum()];
            for (var nTupleID = 0; nTupleID < N_TUPLE_OFFSET.Length; nTupleID++)
            {
                var w = weights.AsSpan(N_TUPLE_OFFSET[nTupleID], this.NTuples.NumPossibleFeatures[nTupleID]);
                var pw = packedWeights[nTupleID];
                ReadOnlySpan<FeatureType> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                var i = 0;
                for (var feature = 0; feature < w.Length; feature++)
                {
                    var mirrored = mirror[feature];
                    w[feature] = feature <= mirrored ? pw[i++] : w[mirrored];
                }
            }
            return weights;
        }

        static T StdSigmoid<T>(T x) where T : IFloatingPointIeee754<T>
            => T.One / (T.One + T.Exp(-x));
    }
}
