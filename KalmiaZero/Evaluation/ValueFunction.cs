using System;
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

        public ReadOnlySpan<NTupleInfo> NTuples => N_TUPLES;

        readonly NTupleInfo[] N_TUPLES;
        readonly WeightType[][][] weights = new WeightType[2][][];     // weights[DiscColor][nTupleID][feature]

        readonly int[] POW_TABLE;
        readonly int[] NUM_POSSIBLE_FEATURES;   // NUM_POSSIBLE_FEATURES[nTupleID]
        readonly int[][] TO_OPPONENT_FEATURE;   // TO_OPPONENT_FEATURE[nTupleID][feature]
        readonly int[][] MIRROR_FEATURE;    // MIRROR_FEATURE[nTupleID][feature]

        public ValueFunction(NTupleInfo[] nTuples)
        {
            N_TUPLES = (from n in nTuples select new NTupleInfo(n)).ToArray();

            POW_TABLE = new int[nTuples.Max(x => x.Size) + 1];
            InitPowTable();

            NUM_POSSIBLE_FEATURES = (from n in nTuples select POW_TABLE[n.Size]).ToArray();

            TO_OPPONENT_FEATURE = (from n in NUM_POSSIBLE_FEATURES select new int[n]).ToArray();
            InitOpponentFeatureTable();

            MIRROR_FEATURE = (from n in NUM_POSSIBLE_FEATURES select new int[n]).ToArray();
            InitMirroredFeatureTable();

            for (var color = 0; color < 2; color++)
            {
                var w = weights[color] = new WeightType[nTuples.Length][];
                for (var nTupleID = 0; nTupleID < nTuples.Length; nTupleID++)
                    w[nTupleID] = new WeightType[NUM_POSSIBLE_FEATURES[nTupleID]];
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

            var valueFunc = new ValueFunction<WeightType>(nTuples);

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

        public ReadOnlySpan<WeightType> GetWeights(DiscColor color, int nTupleID) => weights[(int)color][nTupleID];

        public void InitWeightsWithUniformRand(WeightType min, WeightType max) => InitWeightsWithUniformRand(Random.Shared, min, max);

        public void InitWeightsWithUniformRand(Random rand, WeightType min, WeightType max)
        {
            WeightType[][] bWeights = weights[(int)DiscColor.Black];
            for (var nTupleID = 0; nTupleID < N_TUPLES.Length; nTupleID++)
            {
                var bw = bWeights[nTupleID];
                int[] mirror = MIRROR_FEATURE[nTupleID];
                for (var feature = 0; feature < NUM_POSSIBLE_FEATURES[nTupleID]; feature++)
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

            for (var nTupleID = 0; nTupleID < N_TUPLES.Length; nTupleID++)
            {
                var bw = bWeights[nTupleID];
                var ww = wWeights[nTupleID];
                int[] toOpponent = TO_OPPONENT_FEATURE[nTupleID];
                for (var feature = 0; feature < NUM_POSSIBLE_FEATURES[nTupleID]; feature++)
                    ww[feature] = bw[toOpponent[feature]];
            }
        }

        public WeightType PredictLogit(PositionFeature posFeature)
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

        public WeightType Predict(PositionFeature pf) => StdSigmoid(PredictLogit(pf));

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
            fs.Write(BitConverter.GetBytes(N_TUPLES.Length));
            for (var nTupleID = 0; nTupleID < N_TUPLES.Length; nTupleID++)
            {
                var coords = N_TUPLES[nTupleID].Coordinates[0];
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

        void InitPowTable()
        {
            POW_TABLE[0] = 1;
            for (var i = 1; i < POW_TABLE.Length; i++)
                POW_TABLE[i] = POW_TABLE[i - 1] * NUM_SQUARE_STATES;
        }

        void InitOpponentFeatureTable()
        {
            for (var nTupleID = 0; nTupleID < TO_OPPONENT_FEATURE.Length; nTupleID++)
            {
                ref NTupleInfo nTuple = ref N_TUPLES[nTupleID];
                var table = TO_OPPONENT_FEATURE[nTupleID] = new int[NUM_POSSIBLE_FEATURES[nTupleID]];
                for (var feature = 0; feature < table.Length; feature++)
                {
                    var oppFeature = 0;
                    for (var i = 0; i < nTuple.Size; i++)
                    {
                        var state = feature / POW_TABLE[i] % NUM_SQUARE_STATES;
                        if (state == UNREACHABLE_EMPTY || state == REACHABLE_EMPTY)
                            oppFeature += state * POW_TABLE[i];
                        else
                            oppFeature += (int)Reversi.Utils.ToOpponentColor((DiscColor)state) * POW_TABLE[i];
                    }
                    table[feature] = oppFeature;
                }
            }
        }

        void InitMirroredFeatureTable()
        {
            for (var nTupleID = 0; nTupleID < TO_OPPONENT_FEATURE.Length; nTupleID++)
            {
                ref NTupleInfo nTuple = ref N_TUPLES[nTupleID];
                var shuffleTable = nTuple.MirrorTable;

                if (shuffleTable.Length == 0)
                {
                    MIRROR_FEATURE[nTupleID] = Enumerable.Range(0, NUM_POSSIBLE_FEATURES[nTupleID]).ToArray();
                    continue;
                }

                var table = MIRROR_FEATURE[nTupleID] = new int[NUM_POSSIBLE_FEATURES[nTupleID]];
                for (var feature = 0; feature < table.Length; feature++)
                {
                    var mirroredFeature = 0;
                    for (var i = 0; i < nTuple.Size; i++)
                        mirroredFeature += feature / POW_TABLE[nTuple.Size - shuffleTable[i] - 1] % NUM_SQUARE_STATES * POW_TABLE[nTuple.Size - i - 1];
                    table[feature] = mirroredFeature;
                }
            }
        }

        WeightType[][] PackWeights()
        {
            WeightType[][] weights = this.weights[(int)DiscColor.Black];
            var packedWeights = (from _ in Enumerable.Range(0, weights.Length) select new List<WeightType>()).ToArray();
            for (var nTupleID = 0; nTupleID < N_TUPLES.Length; nTupleID++)
            {
                var w = weights[nTupleID];
                var pw = packedWeights[nTupleID];
                int[] mirror = MIRROR_FEATURE[nTupleID];
                for (var feature = 0; feature < w.Length; feature++)
                    if (feature <= mirror[feature])
                        pw.Add(w[feature]);
            }
            return packedWeights.Select(n => n.ToArray()).ToArray();
        }

        WeightType[][] ExpandPackedWeights(WeightType[][] packedWeights)
        {
            var weights = new WeightType[N_TUPLES.Length][];
            for (var nTupleID = 0; nTupleID < N_TUPLES.Length; nTupleID++)
            {
                var w = weights[nTupleID] = new WeightType[NUM_POSSIBLE_FEATURES[nTupleID]];
                var pw = packedWeights[nTupleID];
                int[] mirror = MIRROR_FEATURE[nTupleID];
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
