using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero.NTuple
{
    using static PositionFeaturesConstantConfig;

    public enum Endian
    {
        SameAsNative,
        DifferentFromNative,
        Unknown
    }

    public class ValueFunction
    {
        const string LABEL = "KalmiaZero";
        const string LABEL_INVERSED = "oreZaimlaK";
        const int LABEL_SIZE = 10;

        public ReadOnlySpan<NTupleInfo> NTuples => this.N_TUPLES;

        readonly NTupleInfo[] N_TUPLES;
        readonly float[][][] weights = new float[2][][];     // weights[DiscColor][nTupleID][feature]

        readonly int[] POW_TABLE;
        readonly int[] NUM_POSSIBLE_FEATURES;   // NUM_POSSIBLE_FEATURES[nTupleID]
        readonly int[][] TO_OPPONENT_FEATURE;   // TO_OPPONENT_FEATURE[nTupleID][feature]
        readonly int[][] MIRROR_FEATURE;    // MIRROR_FEATURE[nTupleID][feature]

        public ValueFunction(NTupleInfo[] nTuples)
        {
            this.N_TUPLES = (from n in nTuples select new NTupleInfo(n)).ToArray();

            this.POW_TABLE = new int[nTuples.Max(x => x.Size) + 1];
            InitPowTable();

            this.NUM_POSSIBLE_FEATURES = (from n in nTuples select this.POW_TABLE[n.Size]).ToArray();

            this.TO_OPPONENT_FEATURE = (from n in this.NUM_POSSIBLE_FEATURES select new int[n]).ToArray();
            InitOpponentFeatureTable();

            this.MIRROR_FEATURE = (from n in this.NUM_POSSIBLE_FEATURES select new int[n]).ToArray();
            InitMirroredFeatureTable();

            for (var color = 0; color < 2; color++)
            {
                var w = this.weights[color] = new float[nTuples.Length][];
                for (var nTupleID = 0; nTupleID < nTuples.Length; nTupleID++)
                    w[nTupleID] = new float[this.NUM_POSSIBLE_FEATURES[nTupleID]];
            }
        }

        public static ValueFunction LoadFromFile(string filePath)
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

            var valueFunc = new ValueFunction(nTuples);

            // load weights
            var packedWeights = new float[nTuples.Length][];
            for (var nTupleID = 0; nTupleID < packedWeights.Length; nTupleID++)
            {
                fs.Read(buffer[..sizeof(int)], swapBytes);
                var size = BitConverter.ToInt32(buffer);
                var pw = packedWeights[nTupleID] = new float[size];
                for (var i = 0; i < pw.Length; i++)
                {
                    fs.Read(buffer[..sizeof(float)], swapBytes);
                    pw[i] = BitConverter.ToSingle(buffer);
                }
            }

            // expand weights
            valueFunc.weights[(int)DiscColor.Black] = valueFunc.ExpandPackedWeights(packedWeights);
            valueFunc.CopyWeightsBlackToWhite();

            return valueFunc;
        }

        public void InitWeightsWithUniformRand(float maxWeight = 0.001f) => InitWeightsWithUniformRand(Random.Shared, maxWeight);

        public void InitWeightsWithUniformRand(Random rand, float maxWeight)
        {
            float[][] bWeights = this.weights[(int)DiscColor.Black];
            for (var nTupleID = 0; nTupleID < this.N_TUPLES.Length; nTupleID++)
            {
                var bw = bWeights[nTupleID];
                int[] mirror = this.MIRROR_FEATURE[nTupleID];
                for (var feature = 0; feature < this.NUM_POSSIBLE_FEATURES[nTupleID]; feature++)
                {
                    var mirrored = mirror[feature];
                    if (feature <= mirrored)
                        bw[feature] = rand.NextSingle() * maxWeight;
                    else
                        bw[feature] = bw[mirrored];
                }
            }
            CopyWeightsBlackToWhite();
        }

        public void CopyWeightsBlackToWhite()
        {
            float[][] bWeights = this.weights[(int)DiscColor.Black];
            float[][] wWeights = this.weights[(int)DiscColor.White];

            for(var nTupleID = 0; nTupleID < this.N_TUPLES.Length; nTupleID++)
            {
                var bw = bWeights[nTupleID];
                var ww = wWeights[nTupleID];
                int[] toOpponent = this.TO_OPPONENT_FEATURE[nTupleID];
                for (var feature = 0; feature < this.NUM_POSSIBLE_FEATURES[nTupleID]; feature++)
                    ww[feature] = bw[toOpponent[feature]];
            }
        }

        public float PredictLogit(PositionFeature posFeature)
        {
            float[][] weights = this.weights[(int)posFeature.SideToMove];

            var logit = 0.0f;
            for(var nTupleID = 0; nTupleID < weights.Length; nTupleID++)
            {
                var w = weights[nTupleID];
                ReadOnlySpan<int> features = posFeature.GetFeatures(nTupleID);
                for (var i = 0; i < features.Length; i++)
                    logit += w[features[i]];
            }

            return logit;
        }

        public float Predict(PositionFeature pf) => StdSigmoid(PredictLogit(pf));

        public void SaveToFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            fs.Write(Encoding.ASCII.GetBytes(LABEL), 0, LABEL_SIZE);

            // save N-Tuples
            fs.Write(BitConverter.GetBytes(this.N_TUPLES.Length));
            for(var nTupleID = 0; nTupleID <  this.N_TUPLES.Length; nTupleID++)
            {
                var coords = this.N_TUPLES[nTupleID].Coordinates[0];
                fs.Write(BitConverter.GetBytes(coords.Length));
                foreach(var coord in coords)
                    fs.WriteByte((byte)coord);
            }

            // save weights
            var packedWeights = PackWeights();
            for(var nTupleID = 0; nTupleID < packedWeights.Length; nTupleID++)
            {
                var pw = packedWeights[nTupleID];
                fs.Write(BitConverter.GetBytes(pw.Length));
                foreach (var v in pw)
                    fs.Write(BitConverter.GetBytes(v));
            }
        }

        void InitPowTable()
        {
            this.POW_TABLE[0] = 1;
            for (var i = 1; i < this.POW_TABLE.Length; i++)
                this.POW_TABLE[i] = this.POW_TABLE[i - 1] * NUM_SQUARE_STATES;
        }

        void InitOpponentFeatureTable()
        {
            for (var nTupleID = 0; nTupleID < this.TO_OPPONENT_FEATURE.Length; nTupleID++)
            {
                ref NTupleInfo nTuple = ref this.N_TUPLES[nTupleID];
                var table = this.TO_OPPONENT_FEATURE[nTupleID] = new int[this.NUM_POSSIBLE_FEATURES[nTupleID]];
                for (var feature = 0; feature < table.Length; feature++)
                {
                    var oppFeature = 0;
                    for (var i = 0; i < nTuple.Size; i++)
                    {
                        var state = (feature / this.POW_TABLE[i]) % NUM_SQUARE_STATES;
                        if (state == UNREACHABLE_EMPTY || state == REACHABLE_EMPTY)
                            oppFeature += state * this.POW_TABLE[i];
                        else
                            oppFeature += (int)Reversi.Utils.ToOpponentColor((DiscColor)state) * this.POW_TABLE[i];
                    }
                    table[feature] = oppFeature;
                }
            }
        }

        void InitMirroredFeatureTable()
        {
            for (var nTupleID = 0; nTupleID < this.TO_OPPONENT_FEATURE.Length; nTupleID++)
            {
                ref NTupleInfo nTuple = ref this.N_TUPLES[nTupleID];
                var shuffleTable = nTuple.MirrorTable;

                if (shuffleTable.Length == 0)
                {
                    this.MIRROR_FEATURE[nTupleID] = Enumerable.Range(0, this.NUM_POSSIBLE_FEATURES[nTupleID]).ToArray();
                    continue;
                }

                var table = this.MIRROR_FEATURE[nTupleID] = new int[this.NUM_POSSIBLE_FEATURES[nTupleID]];
                for (var feature = 0; feature < table.Length; feature++)
                {
                    var mirroredFeature = 0;
                    for (var i = 0; i < nTuple.Size; i++)
                        mirroredFeature += ((feature / this.POW_TABLE[shuffleTable[i]]) % 3) * this.POW_TABLE[i];
                    table[feature] = mirroredFeature;
                }
            }
        }

        float[][] PackWeights()
        {
            float[][] weights = this.weights[(int)DiscColor.Black];
            var packedWeights = (from _ in Enumerable.Range(0, weights.Length) select new List<float>()).ToArray();
            for(var nTupleID = 0; nTupleID < this.N_TUPLES.Length; nTupleID++)
            {
                var w = weights[nTupleID];
                var pw = packedWeights[nTupleID];
                int[] mirror = this.MIRROR_FEATURE[nTupleID];
                for (var feature = 0; feature < w.Length; feature++)
                    if (feature <= mirror[feature])
                        pw.Add(w[feature]);
            }
            return packedWeights.Select(n => n.ToArray()).ToArray();
        }

        float[][] ExpandPackedWeights(float[][] packedWeights) 
        {
            var weights = new float[this.N_TUPLES.Length][];
            for(var nTupleID = 0; nTupleID < this.N_TUPLES.Length; nTupleID++)
            {
                var w = weights[nTupleID] = new float[this.NUM_POSSIBLE_FEATURES[nTupleID]];
                var pw = packedWeights[nTupleID];
                int[] mirror = this.MIRROR_FEATURE[nTupleID];
                var i = 0;
                for(var feature = 0; feature < w.Length; feature++)
                {
                    var mirrored = mirror[feature];
                    w[feature] = (feature <= mirrored) ? pw[i++] : w[mirrored];
                }
            }
            return weights;
        }

        static float StdSigmoid(float x) => 1.0f / (1.0f + FastMath.Exp(-x));
    }
}
