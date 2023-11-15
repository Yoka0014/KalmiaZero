using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using KalmiaZero.Reversi;

namespace KalmiaZero.NTuple
{
    using static PositionFeaturesConstantConfig;

    public readonly struct NTupleInfo
    {
        /// <summary>
        /// Mirrored order of coordinates that compose N-Tuple. The mirroring axis depends on the shape of N-Tuple.
        /// If there is no mirroring axis, MirrorTable is int[0].
        /// </summary>
        public ReadOnlySpan<int> MirrorTable => this.MIRROR_TABLE;

        readonly BoardCoordinate[][] COORDINATES;
        readonly int[] MIRROR_TABLE;

        public int Size => this.COORDINATES[0].Length;
        public int NumSymmetricExpansions => this.COORDINATES.Length;

        public NTupleInfo(int size)
        {
            this.COORDINATES = ExpandTuple(InitTupleByRandomWalk(size));
            var coords = this.COORDINATES[0];
            this.MIRROR_TABLE = (from coord in MirrorTuple(coords) select Array.IndexOf(coords, coord)).ToArray();
        }

        public NTupleInfo(BoardCoordinate[] coords)
        {
            this.COORDINATES = ExpandTuple(coords);
            this.MIRROR_TABLE = (from coord in MirrorTuple(coords) select Array.IndexOf(coords, coord)).ToArray();
        }

        public NTupleInfo(NTupleInfo nTuple)
        {
            this.COORDINATES = new BoardCoordinate[nTuple.COORDINATES.Length][];
            for(var i = 0; i < this.COORDINATES.Length; i++)
            {
                var srcTuple = nTuple.COORDINATES[i];
                var destTuple = this.COORDINATES[i] = new BoardCoordinate[srcTuple.Length];
                Buffer.BlockCopy(srcTuple, 0, destTuple, 0, sizeof(BoardCoordinate) * destTuple.Length);
            }

            this.MIRROR_TABLE = new int[nTuple.MIRROR_TABLE.Length];
            Buffer.BlockCopy(nTuple.MIRROR_TABLE, 0, this.MIRROR_TABLE, 0, sizeof(int) * this.MIRROR_TABLE.Length);
        }

        public ReadOnlySpan<BoardCoordinate> GetCoordinates(int idx) => this.COORDINATES[idx];

        public byte[] ToBytes()
        {
            var size = BitConverter.GetBytes(this.Size);
            var buffer = new byte[sizeof(int) + this.Size];
            Buffer.BlockCopy(size, 0, buffer, 0, size.Length);
            for (var i = sizeof(int); i < buffer.Length; i++)
                buffer[i] = (byte)this.COORDINATES[0][i - sizeof(int)];
            return buffer;
        }

        public override readonly string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("  ");
            for (var i = 0; i < Constants.BOARD_SIZE; i++)
                sb.Append((char)('A' + i)).Append(' ');

            var tuple = this.COORDINATES[0];
            for (var y = 0; y < Constants.BOARD_SIZE; y++)
            {
                sb.Append('\n').Append(y + 1).Append(' ');
                for (var x = 0; x < Constants.BOARD_SIZE; x++)
                {
                    var idx = Array.IndexOf(tuple, Reversi.Utils.Coordinate2DTo1D(x, y));
                    if (idx != -1)
                        sb.Append(idx).Append(' ');
                    else
                        sb.Append("- ");
                }
            }

            return sb.ToString();
        }

        static BoardCoordinate[] InitTupleByRandomWalk(int size)
        {
            var tuple = new List<BoardCoordinate> { (BoardCoordinate)Random.Shared.Next(Constants.NUM_SQUARES) };
            var adjCoords = Reversi.Utils.GetAdjacent8Squares(tuple[0]).ToArray().ToList();

            while (tuple.Count < size)
            {
                tuple.Add(adjCoords[Random.Shared.Next(adjCoords.Count)]);
                foreach (var adjCoord in Reversi.Utils.GetAdjacent8Squares(tuple[^1]))
                    adjCoords.Add(adjCoord);
                adjCoords.RemoveAll(tuple.Contains);
            }

            return tuple.Order().ToArray();
        }

        static BoardCoordinate[][] ExpandTuple(BoardCoordinate[] tuple)
        {
            var tuples = new List<BoardCoordinate[]>();
            var rotated = new BoardCoordinate[tuple.Length];
            Buffer.BlockCopy(tuple, 0, rotated, 0, sizeof(BoardCoordinate) * rotated.Length);

            rotate(rotated);

            for (var j = 0; j < rotated.Length; j++)
                rotated[j] = Reversi.Utils.TO_HORIZONTAL_MIRROR_COORD[(int)rotated[j]];

            rotate(rotated);

            void rotate(BoardCoordinate[] rotated)
            {
                for (var i = 0; i < 4; i++)
                {
                    var ordered = rotated.Order();
                    if (!tuples.Any(x => x.Order().SequenceEqual(ordered)))
                    {
                        var newTuple = new BoardCoordinate[tuple.Length];
                        Buffer.BlockCopy(rotated, 0, newTuple, 0, sizeof(BoardCoordinate) * rotated.Length);
                        tuples.Add(newTuple);
                    }

                    for (var j = 0; j < rotated.Length; j++)
                        rotated[j] = Reversi.Utils.TO_ROTATE90_COORD[(int)rotated[j]];
                }
            }

                return tuples.ToArray();
        }

        static BoardCoordinate[] MirrorTuple(BoardCoordinate[] tuple)
        {
            var mirrored = new BoardCoordinate[tuple.Length];

            if (mirror(Reversi.Utils.TO_HORIZONTAL_MIRROR_COORD))
                return mirrored;

            if (mirror(Reversi.Utils.TO_VERTICAL_MIRROR_COORD))
                return mirrored;

            if (mirror(Reversi.Utils.TO_DIAG_A1H8_MIRROR))
                return mirrored;

            if (mirror(Reversi.Utils.TO_DIAG_A8H1_MIRROR))
                return mirrored;

            return Array.Empty<BoardCoordinate>();

            bool mirror(ReadOnlySpan<BoardCoordinate> table)
            {
                for (var i = 0; i < mirrored.Length; i++)
                    mirrored[i] = table[(int)tuple[i]];

                return mirrored.Order().SequenceEqual(tuple.Order());
            }
        }
    }

    public readonly struct NTuples
    {
        public readonly ReadOnlySpan<NTupleInfo> Tuples => this.TUPLES;
        public readonly int Length => this.TUPLES.Length;
        public readonly ReadOnlySpan<int> NumPossibleFeatures => this.NUM_POSSIBLE_FEATURES;
        public readonly ReadOnlySpan<int> PowTable => this.POW_TABLE;

        readonly NTupleInfo[] TUPLES;
        readonly int[] POW_TABLE;
        readonly int[] NUM_POSSIBLE_FEATURES;
        readonly FeatureType[][] TO_OPPONENT_FEATURE;
        readonly FeatureType[][] TO_MIRRORED_FEATURE;

        public NTuples(Span<NTupleInfo> tuples)
        {
            this.TUPLES = tuples.ToArray();

            var powTable = this.POW_TABLE = new int[this.TUPLES.Max(x => x.Size) + 1];
            InitPowTable();

            this.NUM_POSSIBLE_FEATURES = this.TUPLES.Select(x => powTable[x.Size]).ToArray();

            this.TO_OPPONENT_FEATURE = new FeatureType[this.TUPLES.Length][];
            InitOpponentFeatureTable();

            this.TO_MIRRORED_FEATURE = new FeatureType[this.TUPLES.Length][];
            InitMirroredFeatureTable();
        }

        public NTuples()
        {
            this.TUPLES = Array.Empty<NTupleInfo>();
            this.POW_TABLE = Array.Empty<int>();
            this.NUM_POSSIBLE_FEATURES = Array.Empty<int>();
            this.TO_OPPONENT_FEATURE = Array.Empty<FeatureType[]>();
            this.TO_MIRRORED_FEATURE = Array.Empty<FeatureType[]>();
        }

        public readonly ReadOnlySpan<FeatureType> GetOpponentFeatureTable(int nTupleID) => this.TO_OPPONENT_FEATURE[nTupleID];
        public readonly ReadOnlySpan<FeatureType> GetMirroredFeatureTable(int nTupleID) => this.TO_MIRRORED_FEATURE[nTupleID];

        internal readonly FeatureType[] GetOpponentFeatureRawTable(int nTupleID) => this.TO_OPPONENT_FEATURE[nTupleID];
        internal readonly FeatureType[] GetMirroredFeatureRawTable(int nTupleID) => this.TO_MIRRORED_FEATURE[nTupleID];

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
                ref NTupleInfo nTuple = ref this.TUPLES[nTupleID];
                var table = TO_OPPONENT_FEATURE[nTupleID] = new FeatureType[NUM_POSSIBLE_FEATURES[nTupleID]];
                for (var feature = 0; feature < table.Length; feature++)
                {
                    FeatureType oppFeature = 0;
                    for (var i = 0; i < nTuple.Size; i++)
                    {
                        var state = feature / POW_TABLE[i] % NUM_SQUARE_STATES;
                        if (state == UNREACHABLE_EMPTY || state == REACHABLE_EMPTY)
                            oppFeature += (FeatureType)(state * POW_TABLE[i]);
                        else
                            oppFeature += (FeatureType)((int)Reversi.Utils.ToOpponentColor((DiscColor)state) * POW_TABLE[i]);
                    }
                    table[feature] = oppFeature;
                }
            }
        }

        void InitMirroredFeatureTable()
        {
            for (var nTupleID = 0; nTupleID < this.TO_MIRRORED_FEATURE.Length; nTupleID++)
            {
                ref NTupleInfo nTuple = ref this.TUPLES[nTupleID];
                var shuffleTable = nTuple.MirrorTable;

                if (shuffleTable.Length == 0)
                {
                    this.TO_MIRRORED_FEATURE[nTupleID] = (from f in Enumerable.Range(0, NUM_POSSIBLE_FEATURES[nTupleID])
                                                          select (FeatureType)f).ToArray();
                    continue;
                }

                var table = this.TO_MIRRORED_FEATURE[nTupleID] = new FeatureType[NUM_POSSIBLE_FEATURES[nTupleID]];
                for (var feature = 0; feature < table.Length; feature++)
                {
                    FeatureType mirroredFeature = 0;
                    for (var i = 0; i < nTuple.Size; i++)
                    {
                        var state = feature / POW_TABLE[nTuple.Size - shuffleTable[i] - 1] % NUM_SQUARE_STATES;
                        mirroredFeature += (FeatureType)(state * POW_TABLE[nTuple.Size - i - 1]);
                    }
                    table[feature] = mirroredFeature;
                }
            }
        }
    }
}
