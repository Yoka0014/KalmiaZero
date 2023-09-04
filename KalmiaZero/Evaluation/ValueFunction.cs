﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero.Evaluation
{
    using static PositionFeaturesConstantConfig;

    public class ValueFunction<WeightType> where WeightType : IFloatingPointIeee754<WeightType>
    {
        const string LABEL = "KalmiaZero";
        const string LABEL_INVERSED = "oreZaimlaK";
        const int LABEL_SIZE = 10;

        public NTuples NTuples { get; }
        readonly WeightType[][][] weights = new WeightType[2][][];     // weights[DiscColor][nTupleID][feature]

        public ValueFunction(NTuples nTuples)
        {
            this.NTuples = nTuples;

            for (var color = 0; color < 2; color++)
            {
                var w = weights[color] = new WeightType[this.NTuples.Length][];
                for (var nTupleID = 0; nTupleID < w.Length; nTupleID++)
                    w[nTupleID] = new WeightType[this.NTuples.NumPossibleFeatures[nTupleID]];
            }
        }

        public static ValueFunction<WeightType> LoadFromFile(string filePath)
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

            var valueFunc = new ValueFunction<WeightType>(new NTuples(nTuples));

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
                        pw[i] = CastToWeightType(BitConverter.ToHalf(buffer));
                    else if (weightSize == 4)
                        pw[i] = CastToWeightType(BitConverter.ToSingle(buffer));
                    else if (weightSize == 8)
                        pw[i] = CastToWeightType(BitConverter.ToDouble(buffer));
                }
            }

            // expand weights
            valueFunc.weights[(int)DiscColor.Black] = valueFunc.ExpandPackedWeights(packedWeights);
            valueFunc.CopyWeightsBlackToWhite();

            return valueFunc;
        }

        public WeightType[] GetWeights(DiscColor color, int nTupleID) => weights[(int)color][nTupleID];

        public void InitWeightsWithUniformRand(WeightType min, WeightType max) => InitWeightsWithUniformRand(Random.Shared, min, max);

        public void InitWeightsWithUniformRand(Random rand, WeightType min, WeightType max)
        {
            WeightType[][] bWeights = weights[(int)DiscColor.Black];
            for (var nTupleID = 0; nTupleID < this.NTuples.Length; nTupleID++)
            {
                var bw = bWeights[nTupleID];
                ReadOnlySpan<int> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                for (var feature = 0; feature < this.NTuples.NumPossibleFeatures[nTupleID]; feature++)
                {
                    var mirrored = mirror[feature];
                    if (feature <= mirrored)
                    {
                        if (typeof(WeightType) == typeof(Half) && (Half)rand.NextSingle() is WeightType hr)
                            bw[feature] = hr * (max - min) + min;
                        else if (typeof(WeightType) == typeof(float) && rand.NextSingle() is WeightType fr)
                            bw[feature] = fr * (max - min) + min;
                        else if (typeof(WeightType) == typeof(double) && rand.NextDouble() is WeightType dr)
                            bw[feature] = dr * (max - min) + min;
                    }
                    else
                        bw[feature] = bw[mirrored];
                }
            }
            CopyWeightsBlackToWhite();
        }

        public void CopyWeightsBlackToWhite()
        {
            WeightType[][] bWeights = weights[(int)DiscColor.Black];
            WeightType[][] wWeights = weights[(int)DiscColor.White];

            for (var nTupleID = 0; nTupleID < this.NTuples.Length; nTupleID++)
            {
                var bw = bWeights[nTupleID];
                var ww = wWeights[nTupleID];
                ReadOnlySpan<int> toOpponent = this.NTuples.GetOpponentFeatureTable(nTupleID);
                for (var feature = 0; feature < this.NTuples.NumPossibleFeatures[nTupleID]; feature++)
                    ww[feature] = bw[toOpponent[feature]];
            }
        }

        public WeightType PredictLogit(PositionFeatureVector posFeature)
        {
            WeightType[][] weights = this.weights[(int)posFeature.SideToMove];

            var logit = WeightType.Zero;
            for (var nTupleID = 0; nTupleID < weights.Length; nTupleID++)
            {
                var w = weights[nTupleID];
                ReadOnlySpan<int> features = posFeature.GetFeatures(nTupleID);
                for (var i = 0; i < features.Length; i++)
                    logit += w[features[i]];
            }

            return logit;
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
                    if (v is Half hv)
                        fs.Write(BitConverter.GetBytes(hv));
                    else if (v is float fv)
                        fs.Write(BitConverter.GetBytes(fv));
                    else if (v is double dv)
                        fs.Write(BitConverter.GetBytes(dv));
                }
            }
        }

        WeightType[][] PackWeights()
        {
            WeightType[][] weights = this.weights[(int)DiscColor.Black];
            var packedWeights = (from _ in Enumerable.Range(0, weights.Length) select new List<WeightType>()).ToArray();
            for (var nTupleID = 0; nTupleID < this.NTuples.Length; nTupleID++)
            {
                var w = weights[nTupleID];
                var pw = packedWeights[nTupleID];
                ReadOnlySpan<int> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                for (var feature = 0; feature < w.Length; feature++)
                    if (feature <= mirror[feature])
                        pw.Add(w[feature]);
            }
            return packedWeights.Select(n => n.ToArray()).ToArray();
        }

        WeightType[][] ExpandPackedWeights(WeightType[][] packedWeights)
        {
            var weights = new WeightType[this.NTuples.Length][];
            for (var nTupleID = 0; nTupleID < weights.Length; nTupleID++)
            {
                var w = weights[nTupleID] = new WeightType[this.NTuples.NumPossibleFeatures[nTupleID]];
                var pw = packedWeights[nTupleID];
                ReadOnlySpan<int> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                var i = 0;
                for (var feature = 0; feature < w.Length; feature++)
                {
                    var mirrored = mirror[feature];
                    w[feature] = feature <= mirrored ? pw[i++] : w[mirrored];
                }
            }
            return weights;
        }

        static WeightType CastToWeightType<SrcWeightType>(SrcWeightType sw) where SrcWeightType : IFloatingPointIeee754<SrcWeightType>
        {
            if (sw is WeightType ret)
                return ret;

            if (sw is Half hsw)
            {
                if (typeof(WeightType) == typeof(float) && (float)hsw is WeightType fdw)
                    return fdw;

                if (typeof(WeightType) == typeof(double) && (float)hsw is WeightType ddw)
                    return ddw;
            }
            else if (sw is float fsw)
            {
                if (typeof(WeightType) == typeof(Half) && (Half)fsw is WeightType hdw)
                    return hdw;

                if (typeof(WeightType) == typeof(double) && (float)fsw is WeightType ddw)
                    return ddw;
            }
            else if (sw is double dsw)
            {
                if (typeof(WeightType) == typeof(Half) && (Half)dsw is WeightType hdw)
                    return hdw;

                if (typeof(WeightType) == typeof(float) && (float)dsw is WeightType fdw)
                    return fdw;
            }

            throw new InvalidCastException();
        }

        static T StdSigmoid<T>(T x) where T : IFloatingPointIeee754<T>
            => T.One / (T.One + T.Exp(-x));
    }
}