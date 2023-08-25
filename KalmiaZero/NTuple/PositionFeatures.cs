using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using KalmiaZero.Utils;
using KalmiaZero.Reversi;

namespace KalmiaZero.NTuple
{
    using static Utils.BitManipulations;
    using static Reversi.Constants;
    using static PositionFeaturesConstantConfig;

    public static class PositionFeaturesConstantConfig 
    {
        public const int NUM_SQUARE_STATES = 4;     // BLACK, WHITE, REACHEABLE_EMPTY and UNREACHABLE_EMPTY
        //public const int NUM_SQUARE_STATES = 3;   // BLACK, WHITE and EMPTY

        public const int BLACK = (int)DiscColor.Black;  // 0
        public const int WHITE = (int)DiscColor.White;  // 1
        public const int UNREACHABLE_EMPTY = 2;
        public const int REACHABLE_EMPTY = 3;
    }

    /// <summary>
    /// Represents features of n-tuples in a position.
    /// </summary>
    public class PositionFeatures
    {
        public DiscColor SideToMove { get; private set; }
        public int NumNTuples => this.nTuples.Length;

        readonly NTupleInfo[] nTuples;
        readonly int[][] features;
        readonly FeatureDiff[][] featureDiffTable = new FeatureDiff[NUM_SQUARES][];

        delegate void Updator(ref Move move);
        Updator playerUpdator;
        Updator opponentUpdator;

        Move[] prevLegalMoves = new Move[MAX_NUM_MOVES];
        int numPrevLegalMoves = 0;

        readonly int[] POW_TABLE;

        public PositionFeatures(IEnumerable<NTupleInfo> nTuples)
        {
            this.nTuples = nTuples.ToArray();
            this.features = (from nTuple in this.nTuples select new int[nTuple.Tuples.Length]).ToArray();
            this.playerUpdator = UpdateAfterBlackMove;
            this.opponentUpdator = UpdateAfterWhiteMove;
            this.POW_TABLE = new int[this.nTuples.Max(x => x.Size)];
            InitPowTable();
            InitFeatureDiffTable();
        }

        void InitPowTable()
        {
            this.POW_TABLE[0] = 1;
            for (var i = 1; i < this.POW_TABLE.Length; i++)
                this.POW_TABLE[i] = this.POW_TABLE[i - 1] * NUM_SQUARE_STATES;
        }

        void InitFeatureDiffTable()
        {
            var table = new List<FeatureDiff>();
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
            {
                table.Clear();
                for (var nTupleID = 0; nTupleID < this.nTuples.Length; nTupleID++)
                {
                    BoardCoordinate[][] tuples = this.nTuples[nTupleID].Tuples;
                    for (var idx = 0; idx < tuples.Length; idx++)
                    {
                        var tuple = tuples[idx];
                        var coordIdx = Array.IndexOf(tuple, coord);
                        if (coordIdx != -1)
                            table.Add(new FeatureDiff { FeatureID = (nTupleID, idx), Diff = this.POW_TABLE[tuple.Length - coordIdx - 1] });
                    }
                }
                this.featureDiffTable[(int)coord] = table.ToArray();
            }
        }

        public ReadOnlySpan<int> GetFeatures(int nTupleID) => this.features[nTupleID];

        public void Init(ref Position pos, Span<Move> legalMoves)
        {
            this.SideToMove = pos.SideToMove;
            if(this.SideToMove == DiscColor.Black)
                (this.playerUpdator, this.opponentUpdator) = (UpdateAfterBlackMove, UpdateAfterWhiteMove);
            else
                (this.playerUpdator, this.opponentUpdator) = (UpdateAfterWhiteMove, UpdateAfterBlackMove);

            if (NUM_SQUARE_STATES == 4)
            {
                legalMoves.CopyTo(this.prevLegalMoves);
                this.numPrevLegalMoves = legalMoves.Length;
            }

            for (var i = 0; i < this.features.Length; i++)
            {
                ref NTupleInfo nTuple = ref this.nTuples[i];
                int[] f = this.features[i];
                for (var j = 0; j < nTuple.Tuples.Length; j++)
                {
                    f[j] = 0;
                    foreach (BoardCoordinate coord in nTuple.Tuples[j])
                    {
                        if (NUM_SQUARE_STATES == 4)
                        {
                            var color = pos.GetSquareColorAt(coord);
                            if (color != DiscColor.Null)
                                f[j] = f[j] * NUM_SQUARE_STATES + (int)color;
                            else if (legalMoves.Contains(coord))
                                f[j] = f[j] * NUM_SQUARE_STATES + REACHABLE_EMPTY;
                            else
                                f[j] = f[j] * NUM_SQUARE_STATES + UNREACHABLE_EMPTY;
                        }
                        else
                            f[j] = f[j] * NUM_SQUARE_STATES + (int)pos.GetSquareColorAt(coord);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref Move move, Span<Move> legalMoves)
        {
            if(NUM_SQUARE_STATES == 4)
                RemoveReachableEmpties();

            this.playerUpdator.Invoke(ref move);
            (this.playerUpdator, this.opponentUpdator) = (this.opponentUpdator, this.playerUpdator);

            if (NUM_SQUARE_STATES == 4)
            {
                SetReachableEmpties(ref legalMoves);
                legalMoves.CopyTo(this.prevLegalMoves);
                this.numPrevLegalMoves = legalMoves.Length;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Pass()
        {
            if (NUM_SQUARE_STATES == 4)
            {
                RemoveReachableEmpties();
                this.numPrevLegalMoves = 0;
            }

            this.SideToMove = Reversi.Utils.ToOpponentColor(this.SideToMove);
            (this.playerUpdator, this.opponentUpdator) = (this.opponentUpdator, this.playerUpdator);
        }

        void UpdateAfterBlackMove(ref Move move)
        {
            foreach (var diff in this.featureDiffTable[(int)move.Coord])
                this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (BLACK - UNREACHABLE_EMPTY) * diff.Diff;

            ulong flipped = move.Flip;
            for(int coord = FindFirstSet(flipped); flipped != 0; coord = FindNextSet(ref flipped))
            {
                var diffTable = this.featureDiffTable[coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (BLACK - WHITE) * diff.Diff;
            }
            
            this.SideToMove = DiscColor.White;
        }

        void UpdateAfterWhiteMove(ref Move move)
        {
            foreach (var diff in this.featureDiffTable[(int)move.Coord])
                this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (WHITE - UNREACHABLE_EMPTY) * diff.Diff;

            ulong flipped = move.Flip;
            for (int coord = FindFirstSet(flipped); flipped != 0; coord = FindNextSet(ref flipped))
            {
                var diffTable = this.featureDiffTable[coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (WHITE - BLACK) * diff.Diff;
            }

            this.SideToMove = DiscColor.Black;
        }

        void SetReachableEmpties(ref Span<Move> legalMoves)
        {
            foreach (var move in legalMoves)
            {
                var diffTable = this.featureDiffTable[(int)move.Coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (REACHABLE_EMPTY - UNREACHABLE_EMPTY) * diff.Diff;
            }
        }

        void RemoveReachableEmpties()
        {
            for (var i = 0; i < this.numPrevLegalMoves; i++)
            {
                var diffTable = this.featureDiffTable[(int)this.prevLegalMoves[i].Coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (UNREACHABLE_EMPTY - REACHABLE_EMPTY) * diff.Diff;
            }
        }

        struct FeatureDiff 
        {
            public (int TupleID, int Idx) FeatureID { get; set; }
            public int Diff { get; set; }
        }
    }
}
