using System;
using System.Collections.Generic;
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
    public class PositionFeatureVector
    {
        public DiscColor SideToMove { get; private set; }
        public int NumNTuples => this.NTuples.Length;
        public NTuples NTuples { get; }

        readonly int[][] features;  // features[nTupleID][idx]
        readonly FeatureDiff[][] featureDiffTable = new FeatureDiff[NUM_SQUARES][];

        delegate void Updator(ref Move move);
        Updator playerUpdator;
        Updator opponentUpdator;

        Move[] prevLegalMoves = new Move[MAX_NUM_MOVES];
        int numPrevLegalMoves = 0;

        public PositionFeatureVector(NTuples nTuples)
        {
            this.NTuples = nTuples;
            this.features = new int[this.NTuples.Length][];
            var tuples = this.NTuples.Tuples;
            for (var nTupleID = 0; nTupleID < this.features.Length; nTupleID++)
                this.features[nTupleID] = new int[tuples[nTupleID].NumSymmetricExpansions];

            this.playerUpdator = UpdateAfterBlackMove;
            this.opponentUpdator = UpdateAfterWhiteMove;

            InitFeatureDiffTable();
        }

        public PositionFeatureVector(PositionFeatureVector pf)
        {
            this.SideToMove = pf.SideToMove;
            this.NTuples = pf.NTuples;
            this.features = new int[pf.NumNTuples][];
            for(var i = 0; i < this.features.Length; i++)
            {
                var f = this.features[i] = new int[pf.features[i].Length];
                Buffer.BlockCopy(pf.features[i], 0, f, 0, sizeof(int) * f.Length);
            }

            if (this.SideToMove == DiscColor.Black)
            {
                this.playerUpdator = UpdateAfterBlackMove;
                this.opponentUpdator = UpdateAfterWhiteMove;
            }
            else
            {
                this.playerUpdator = UpdateAfterWhiteMove;
                this.opponentUpdator = UpdateAfterBlackMove;
            }

            this.featureDiffTable = pf.featureDiffTable;

            if (NUM_SQUARE_STATES == 4)
            {
                Array.Copy(pf.prevLegalMoves, 0, this.prevLegalMoves, 0, pf.numPrevLegalMoves);
                this.numPrevLegalMoves = pf.numPrevLegalMoves;
            }
        }

        void InitFeatureDiffTable()
        {
            var table = new List<FeatureDiff>();
            var tuples = this.NTuples.Tuples;
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
            {
                table.Clear();
                for (var nTupleID = 0; nTupleID < tuples.Length; nTupleID++)
                {
                    for (var idx = 0; idx < tuples[nTupleID].NumSymmetricExpansions; idx++)
                    {
                        var coords = tuples[nTupleID].GetCoordinates(idx);
                        var coordIdx = coords.IndexOf(coord);
                        if (coordIdx != -1)
                            table.Add(new FeatureDiff { FeatureID = (nTupleID, idx), Diff = this.NTuples.PowTable[coords.Length - coordIdx - 1] });
                    }
                }
                this.featureDiffTable[(int)coord] = table.ToArray();
            }
        }

        public ReadOnlySpan<int> GetFeatures(int nTupleID) => this.features[nTupleID];

        public void Init(Bitboard bitboard, DiscColor sideToMove, Span<Move> legalMoves)
        {
            var pos = new Position(bitboard, sideToMove);
            Init(ref pos, legalMoves);
        }

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

            for (var nTupleID = 0; nTupleID < this.features.Length; nTupleID++)
            {
                var tuples = this.NTuples.Tuples;
                int[] f = this.features[nTupleID];
                for (var i = 0; i < tuples[nTupleID].NumSymmetricExpansions; i++)
                {
                    f[i] = 0;
                    var coordinates = tuples[nTupleID].GetCoordinates(i);
                    foreach (BoardCoordinate coord in coordinates)
                        f[i] = f[i] * NUM_SQUARE_STATES + (int)pos.GetSquareColorAt(coord);
                }
            }

            if(NUM_SQUARE_STATES == 4)
                SetReachableEmpties(ref legalMoves);
        }

        public void CopyTo(PositionFeatureVector dest)
        {
            dest.SideToMove = this.SideToMove;
            if (dest.SideToMove == DiscColor.Black)
            {
                dest.playerUpdator = dest.UpdateAfterBlackMove;
                dest.opponentUpdator = dest.UpdateAfterWhiteMove;
            }
            else
            {
                dest.playerUpdator = dest.UpdateAfterWhiteMove;
                dest.opponentUpdator = dest.UpdateAfterBlackMove;
            }

            for (var i = 0; i < this.NTuples.Length; i++)
                Buffer.BlockCopy(this.features[i], 0, dest.features[i], 0, sizeof(int) * dest.features[i].Length);

            if (NUM_SQUARE_STATES == 4)
            {
                Array.Copy(this.prevLegalMoves, 0, dest.prevLegalMoves, 0, this.numPrevLegalMoves);
                dest.numPrevLegalMoves = this.numPrevLegalMoves;
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
        public void Pass(Span<Move> legalMoves)
        {
            if (NUM_SQUARE_STATES == 4)
            {
                RemoveReachableEmpties();
                SetReachableEmpties(ref legalMoves);
                legalMoves.CopyTo(this.prevLegalMoves);
                this.numPrevLegalMoves = legalMoves.Length;
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
