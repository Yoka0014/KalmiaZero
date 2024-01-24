using System;
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
    public partial class ValueFunction<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        const string LABEL = "KalmiaZero";
        const string LABEL_INVERSED = "oreZaimlaK";
        const int LABEL_SIZE = 10;

        public int NumPhases { get; }
        public int NumMovesPerPhase { get; }
        public ReadOnlySpan<int> EmptySquareCountToPhase => this.emptyCountToPhase;

        public NTupleGroup NTuples { get; }

        public WeightType[] Weights { get; private set; }
        public WeightType[] Bias { get; private set; }  
        public ReadOnlySpan<int> DiscColorOffset => this.discColorOffset;
        public ReadOnlySpan<int> PhaseOffset => this.phaseOffset;
        public ReadOnlySpan<int> NTupleOffset => this.nTupleOffset;

        readonly int[] emptyCountToPhase;
        readonly int[] discColorOffset;
        readonly int[] phaseOffset;
        readonly int[] nTupleOffset;

        public ValueFunction(NTupleGroup nTuples) : this(nTuples, 60) { }

        public ValueFunction(NTupleGroup nTuples, int numMovesPerPhase)
        {
            this.NumMovesPerPhase = numMovesPerPhase;
            this.NumPhases = (Constants.NUM_SQUARES - 4) / numMovesPerPhase;
            this.emptyCountToPhase = new int[Constants.NUM_SQUARES];
            InitEmptyCountToPhaseTable();

            this.NTuples = nTuples;
            var numFeatures = nTuples.NumPossibleFeatures.Sum();
            this.Weights = new WeightType[2 * this.NumPhases * numFeatures];
            this.discColorOffset = new int[2] { 0, this.Weights.Length / 2 };
            this.Bias = new WeightType[this.NumPhases];

            this.phaseOffset = new int[this.NumPhases];
            for (var i = 0; i < this.phaseOffset.Length; i++)
                this.phaseOffset[i] = i * numFeatures;

            this.nTupleOffset = new int[nTuples.Length];
            this.nTupleOffset[0] = 0;
            for (var i = 1; i < nTupleOffset.Length; i++)
                this.nTupleOffset[i] += this.nTupleOffset[i - 1] + nTuples.NumPossibleFeatures[i - 1];
        }

        void InitEmptyCountToPhaseTable()
        {
            for (var phase = 0; phase < this.NumPhases; phase++)
            {
                var offset = phase * this.NumMovesPerPhase;
                for (var i = 0; i < this.NumMovesPerPhase; i++)
                    this.emptyCountToPhase[(Constants.NUM_SQUARES - 4) - offset - i] = phase;
            }
            this.emptyCountToPhase[0] = this.NumPhases - 1;
        }

        /*
         * Format:
         * 
         * offset = 0:  label(for endianess check)
         * offset = 10: the number of N-Tuples
         * offset = 14: N-Tuple's coordinates
         * offset = M: the size of weight
         * offset = M + 4: the number of moves per phase 
         * offset = M + 8: weights
         * offset = M + 8 + NUM_WEIGHTS: bias
         */
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

            // load weights
            fs.Read(buffer[..sizeof(int)], swapBytes);
            var weightSize = BitConverter.ToInt32(buffer);
            if (weightSize != 2 && weightSize != 4 && weightSize != 8)
                throw new InvalidDataException($"The size {weightSize} is invalid for weight.");

            fs.Read(buffer[..sizeof(int)], swapBytes);
            var numMovesPerPhase = BitConverter.ToInt32(buffer);
            var valueFunc = new ValueFunction<WeightType>(new NTupleGroup(nTuples), numMovesPerPhase);
            var numPhases = valueFunc.NumPhases;

            var packedWeights = Enumerable.Range(0, numPhases).Select(p => new WeightType[nTuples.Length][]).ToArray();
            for (var phase = 0; phase < packedWeights.Length; phase++)
            {
                for (var nTupleID = 0; nTupleID < packedWeights[phase].Length; nTupleID++)
                {
                    fs.Read(buffer[..sizeof(int)], swapBytes);
                    var size = BitConverter.ToInt32(buffer);
                    var pw = packedWeights[phase][nTupleID] = new WeightType[size];
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
            }

            for (var phase = 0; phase < numPhases; phase++)
            {
                fs.Read(buffer[..weightSize], swapBytes);
                if (weightSize == 2)
                    valueFunc.Bias[phase] = WeightType.CreateChecked(BitConverter.ToHalf(buffer));
                else if (weightSize == 4)
                    valueFunc.Bias[phase] = WeightType.CreateChecked(BitConverter.ToSingle(buffer));
                else if (weightSize == 8)
                    valueFunc.Bias[phase] = WeightType.CreateChecked(BitConverter.ToDouble(buffer));
            }

            // expand weights
            valueFunc.Weights = valueFunc.ExpandPackedWeights(packedWeights);
            valueFunc.CopyWeightsBlackToWhite();

            return valueFunc;
        }

        public Span<WeightType> GetWeights(DiscColor color, int nTupleID)
            => Weights.AsSpan(this.discColorOffset[(int)color] + this.nTupleOffset[nTupleID], this.NTuples.NumPossibleFeatures[nTupleID]);

        public void InitWeightsWithUniformRand(WeightType min, WeightType max) => InitWeightsWithUniformRand(Random.Shared, min, max);

        public void InitWeightsWithUniformRand(Random rand, WeightType min, WeightType max)
        {
            for (var nTupleID = 0; nTupleID < this.NTuples.Length; nTupleID++)
            {
                var w = this.Weights.AsSpan(nTupleOffset[nTupleID]);
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

            Array.Clear(this.Bias);
        }

        public void InitWeightsWithNormalRand(WeightType mu, WeightType sigma) => InitWeightsWithNormalRand(Random.Shared, mu, sigma);

        public void InitWeightsWithNormalRand(Random rand, WeightType mu, WeightType sigma)
        {
            for (var nTupleID = 0; nTupleID < this.NTuples.Length; nTupleID++)
            {
                var w = this.Weights.AsSpan(nTupleOffset[nTupleID]);
                ReadOnlySpan<FeatureType> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                for (var feature = 0; feature < this.NTuples.NumPossibleFeatures[nTupleID]; feature++)
                {
                    var mirrored = mirror[feature];
                    if (feature <= mirrored)
                        w[feature] = rand.NextNormal(mu, sigma);
                    else
                        w[feature] = w[mirrored];
                }
            }
            CopyWeightsBlackToWhite();

            Array.Clear(this.Bias);
        }

        public void CopyWeightsBlackToWhite()
        {
            var whiteOffset = this.discColorOffset[(int)DiscColor.White];
            Span<WeightType> bWeights = Weights.AsSpan(0, whiteOffset);
            Span<WeightType> wWeights = Weights.AsSpan(whiteOffset);

            for (var phase = 0; phase < this.NumPhases; phase++)
            {
                for (var nTupleID = 0; nTupleID < this.nTupleOffset.Length; nTupleID++)
                {
                    var bw = bWeights[(this.PhaseOffset[phase] + this.nTupleOffset[nTupleID])..];
                    var ww = wWeights[(this.PhaseOffset[phase] + this.nTupleOffset[nTupleID])..];
                    ReadOnlySpan<FeatureType> toOpponent = this.NTuples.GetOpponentFeatureTable(nTupleID);
                    for (var feature = 0; feature < toOpponent.Length; feature++)
                        ww[feature] = bw[toOpponent[feature]];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe WeightType PredictLogit(PositionFeatureVector posFeatureVec)
        {
            int phase;
            fixed (int* toPhase = this.EmptySquareCountToPhase)
                phase = toPhase[posFeatureVec.EmptySquareCount];

            var x = WeightType.Zero;
            fixed (int* discColorOffset = this.discColorOffset)
            fixed (WeightType* weights = &this.Weights[this.discColorOffset[(int)posFeatureVec.SideToMove] + this.phaseOffset[phase]])
            fixed (Feature* features = posFeatureVec.Features)
            {
                for (var nTupleID = 0; nTupleID < this.nTupleOffset.Length; nTupleID++)
                {
                    var w = weights + this.nTupleOffset[nTupleID];
                    ref Feature feature = ref features[nTupleID];
                    for (var i = 0; i < feature.Length; i++)
                        x += w[feature[i]];
                }

                fixed (WeightType* bias = this.Bias)
                    x += bias[phase];
            }

            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe WeightType PredictLogitWithBlackWeights(PositionFeatureVector posFeatureVec)
        {
            if (posFeatureVec.SideToMove == DiscColor.Black)
                return PredictLogit(posFeatureVec);

            int phase;
            fixed (int* toPhase = this.EmptySquareCountToPhase)
                phase = toPhase[posFeatureVec.EmptySquareCount];

            var x = WeightType.Zero;
            fixed (int* discColorOffset = this.discColorOffset)
            fixed (WeightType* weights = &this.Weights[this.discColorOffset[(int)DiscColor.Black] + this.phaseOffset[phase]])
            fixed (Feature* features = posFeatureVec.Features)
            {
                for (var nTupleID = 0; nTupleID < this.nTupleOffset.Length; nTupleID++)
                {
                    var w = weights + this.nTupleOffset[nTupleID];
                    ref Feature feature = ref features[nTupleID];
                    fixed (FeatureType* toOpp = this.NTuples.GetRawOpponentFeatureTable(nTupleID))
                    {
                        for (var i = 0; i < feature.Length; i++)
                            x += w[toOpp[feature[i]]];
                    }
                }

                fixed (WeightType* bias = this.Bias)
                    x += bias[phase];
            }

            return x;
        }

        public WeightType Predict(PositionFeatureVector pfv) => StdSigmoid(PredictLogit(pfv));

        public WeightType PredictWithBlackWeights(PositionFeatureVector pfv)
            => (pfv.SideToMove == DiscColor.Black) ? Predict(pfv) : StdSigmoid(PredictLogitWithBlackWeights(pfv));

        /*
         * Format:
         * 
         * offset = 0:  label(for endianess check)
         * offset = 10: the number of N-Tuples
         * offset = 14: N-Tuple's coordinates
         * offset = M: the size of weight
         * offset = M + 4: the number of moves per phase 
         * offset = M + 8: weights
         * offset = M + 8 + NUM_WEIGHTS: bias
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

            fs.Write(BitConverter.GetBytes(this.NumMovesPerPhase));

            for (var phase = 0; phase < packedWeights.Length; phase++)
            {
                for (var nTupleID = 0; nTupleID < packedWeights[phase].Length; nTupleID++)
                {
                    var pw = packedWeights[phase][nTupleID];
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
            }

            for (var phase = 0; phase < this.Bias.Length; phase++)
            {
                if (typeof(WeightType) == typeof(Half))
                    fs.Write(BitConverter.GetBytes(Half.CreateChecked(this.Bias[phase])));
                else if (typeof(WeightType) == typeof(float))
                    fs.Write(BitConverter.GetBytes(float.CreateChecked(this.Bias[phase])));
                else if (typeof(WeightType) == typeof(double))
                    fs.Write(BitConverter.GetBytes(double.CreateChecked(this.Bias[phase])));
            }
        }

        WeightType[][][] PackWeights()
        {
            var packedWeights = new List<WeightType>[this.NumPhases][];
            for (var i = 0; i < packedWeights.Length; i++)
                packedWeights[i] = (from _ in Enumerable.Range(0, this.NTuples.Length) select new List<WeightType>()).ToArray();

            var numPossibleFeatures = this.NTuples.NumPossibleFeatures;
            for (var phase = 0; phase < this.NumPhases; phase++)
            {
                for (var nTupleID = 0; nTupleID < this.nTupleOffset.Length; nTupleID++)
                {
                    var w = this.Weights.AsSpan(this.phaseOffset[phase] + this.nTupleOffset[nTupleID], numPossibleFeatures[nTupleID]);
                    var pw = packedWeights[phase][nTupleID];
                    ReadOnlySpan<FeatureType> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                    for (var feature = 0; feature < w.Length; feature++)
                        if (feature <= mirror[feature])
                            pw.Add(w[feature]);
                }
            }

            var ret = new WeightType[this.NumPhases][][];
            for (var i = 0; i < ret.Length; i++)
                ret[i] = packedWeights[i].Select(n => n.ToArray()).ToArray();
            return ret;
        }

        WeightType[] ExpandPackedWeights(WeightType[][][] packedWeights)
        {
            var numPhases = packedWeights.Length;
            var weights = new WeightType[2 * numPhases * this.NTuples.NumPossibleFeatures.Sum()];
            for (var phase = 0; phase < numPhases; phase++)
            {
                for (var nTupleID = 0; nTupleID < this.nTupleOffset.Length; nTupleID++)
                {
                    var w = weights.AsSpan(this.phaseOffset[phase] + this.nTupleOffset[nTupleID], this.NTuples.NumPossibleFeatures[nTupleID]);
                    var pw = packedWeights[phase][nTupleID];
                    ReadOnlySpan<FeatureType> mirror = this.NTuples.GetMirroredFeatureTable(nTupleID);
                    var i = 0;
                    for (var feature = 0; feature < w.Length; feature++)
                    {
                        var mirrored = mirror[feature];
                        w[feature] = (feature <= mirrored) ? pw[i++] : w[mirrored];
                    }
                }
            }
            return weights;
        }

        public static WeightType StdSigmoid(WeightType x)
            => WeightType.One / (WeightType.One + WeightType.Exp(-x));
    }
}