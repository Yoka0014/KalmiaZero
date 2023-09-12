global using FeatureType = System.UInt16;

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
        public const FeatureType NUM_SQUARE_STATES = 4;     // BLACK, WHITE, REACHEABLE_EMPTY and UNREACHABLE_EMPTY
        //public const int NUM_SQUARE_STATES = 3;   // BLACK, WHITE and EMPTY

        public const FeatureType BLACK = (int)DiscColor.Black;  // 0
        public const FeatureType WHITE = (int)DiscColor.White;  // 1
        public const FeatureType UNREACHABLE_EMPTY = 2;
        public const FeatureType REACHABLE_EMPTY = 3;
    }

    /// <summary>
    /// Represents features of n-tuples in a position.
    /// </summary>
    public class PositionFeatureVector
    {
        public DiscColor SideToMove { get; private set; }
        public int NumNTuples => this.NTuples.Length;
        public NTuples NTuples { get; }

        readonly FeatureType[][] features;  // features[nTupleID][idx]
        readonly FeatureDiff[][] featureDiffTable = new FeatureDiff[NUM_SQUARES][];

        delegate void Updator(ref Move move);
        Updator playerUpdator;
        Updator opponentUpdator;
        Updator playerRestorer;
        Updator opponentRestorer;


        readonly Move[] prevLegalMoves = new Move[MAX_NUM_MOVES];
        int numPrevLegalMoves = 0;

        public PositionFeatureVector(NTuples nTuples)
        {
            this.NTuples = nTuples;
            this.features = new FeatureType[this.NTuples.Length][];
            var tuples = this.NTuples.Tuples;
            for (var nTupleID = 0; nTupleID < this.features.Length; nTupleID++)
                this.features[nTupleID] = new FeatureType[tuples[nTupleID].NumSymmetricExpansions];

            (this.playerUpdator, this.opponentUpdator) = (Update<Black>, Update<White>);
            (this.playerRestorer, this.opponentRestorer) = (Undo<White>, Undo<Black>);

            InitFeatureDiffTable();
        }

        public PositionFeatureVector(PositionFeatureVector pf)
        {
            this.SideToMove = pf.SideToMove;
            this.NTuples = pf.NTuples;
            this.features = new FeatureType[pf.NumNTuples][];
            for(var i = 0; i < this.features.Length; i++)
            {
                var f = this.features[i] = new FeatureType[pf.features[i].Length];
                Buffer.BlockCopy(pf.features[i], 0, f, 0, sizeof(FeatureType) * f.Length);
            }

            if (this.SideToMove == DiscColor.Black)
            {
                (this.playerUpdator, this.opponentUpdator) = (Update<Black>, Update<White>);
                (this.playerRestorer, this.opponentRestorer) = (Undo<White>, Undo<Black>);
            }
            else
            {
                (this.playerUpdator, this.opponentUpdator) = (Update<White>, Update<Black>);
                (this.playerRestorer, this.opponentRestorer) = (Undo<Black>, Undo<White>);
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

        public ReadOnlySpan<FeatureType> GetFeatures(int nTupleID) => this.features[nTupleID];

        public void Init(Bitboard bitboard, DiscColor sideToMove, Span<Move> legalMoves)
        {
            var pos = new Position(bitboard, sideToMove);
            Init(ref pos, legalMoves);
        }

        public void Init(ref Position pos, Span<Move> legalMoves)
        {
            this.SideToMove = pos.SideToMove;
            if(this.SideToMove == DiscColor.Black)
                (this.playerUpdator, this.opponentUpdator) = (Update<Black>, Update<White>);
            else
                (this.playerUpdator, this.opponentUpdator) = (Update<White>, Update<Black>);

            if (NUM_SQUARE_STATES == 4)
            {
                legalMoves.CopyTo(this.prevLegalMoves);
                this.numPrevLegalMoves = legalMoves.Length;
            }

            for (var nTupleID = 0; nTupleID < this.features.Length; nTupleID++)
            {
                ReadOnlySpan<NTupleInfo> tuples = this.NTuples.Tuples;
                FeatureType[] f = this.features[nTupleID];
                for (var i = 0; i < tuples[nTupleID].NumSymmetricExpansions; i++)
                {
                    f[i] = 0;
                    var coordinates = tuples[nTupleID].GetCoordinates(i);
                    foreach (BoardCoordinate coord in coordinates)
                        f[i] = (FeatureType)(f[i] * NUM_SQUARE_STATES + (int)pos.GetSquareColorAt(coord));
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
                (dest.playerUpdator, dest.opponentUpdator) = (dest.Update<Black>, dest.Update<White>);
                (dest.playerRestorer, dest.opponentRestorer) = (dest.Undo<White>, dest.Undo<Black>);
            }
            else
            {
                (dest.playerUpdator, dest.opponentUpdator) = (dest.Update<White>, dest.Update<Black>);
                (dest.playerRestorer, dest.opponentRestorer) = (dest.Undo<Black>, dest.Undo<White>);
            }

            for (var i = 0; i < this.NTuples.Length; i++)
                Buffer.BlockCopy(this.features[i], 0, dest.features[i], 0, sizeof(FeatureType) * dest.features[i].Length);

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
            (this.playerRestorer, this.opponentRestorer) = (this.opponentRestorer, this.playerRestorer);

            if (NUM_SQUARE_STATES == 4)
            {
                SetReachableEmpties(ref legalMoves);
                legalMoves.CopyTo(this.prevLegalMoves);
                this.numPrevLegalMoves = legalMoves.Length;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(ref Move move, Span<Move> legalMoves)
        {
            if (NUM_SQUARE_STATES == 4)
                RemoveReachableEmpties();

            this.playerRestorer.Invoke(ref move);
            (this.playerUpdator, this.opponentUpdator) = (this.opponentUpdator, this.playerUpdator);
            (this.playerRestorer, this.opponentRestorer) = (this.opponentRestorer, this.playerRestorer);

            if(NUM_SQUARE_STATES == 4)
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
            (this.playerRestorer, this.opponentRestorer) = (this.opponentRestorer, this.playerRestorer);
        }

        void Update<SideToMove>(ref Move move) where SideToMove : struct, IDiscColor
        {
            var placer = typeof(SideToMove) == typeof(Black) ? BLACK - UNREACHABLE_EMPTY : WHITE - UNREACHABLE_EMPTY;
            var flipper = typeof(SideToMove) == typeof(Black) ? BLACK - WHITE : WHITE - BLACK;

            foreach (var diff in this.featureDiffTable[(int)move.Coord])
                this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (FeatureType)(placer * diff.Diff);

            ulong flipped = move.Flip;
            for (int coord = FindFirstSet(flipped); flipped != 0; coord = FindNextSet(ref flipped))
            {
                var diffTable = this.featureDiffTable[coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (FeatureType)(flipper * diff.Diff);
            }

            this.SideToMove = typeof(SideToMove) == typeof(Black) ? DiscColor.White : DiscColor.Black;
        }

        void Undo<SideToMove>(ref Move move) where SideToMove : struct, IDiscColor
        {
            var remover = typeof(SideToMove) == typeof(Black) ? UNREACHABLE_EMPTY - BLACK : UNREACHABLE_EMPTY - WHITE;
            var flipper = typeof(SideToMove) == typeof(Black) ? WHITE - BLACK : BLACK - WHITE;

            foreach (var diff in this.featureDiffTable[(int)move.Coord])
                this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (FeatureType)(remover * diff.Diff);

            ulong flipped = move.Flip;
            for (int coord = FindFirstSet(flipped); flipped != 0; coord = FindNextSet(ref flipped))
            {
                var diffTable = this.featureDiffTable[coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (FeatureType)(flipper * diff.Diff);
            }

            this.SideToMove = typeof(SideToMove) == typeof(Black) ? DiscColor.Black : DiscColor.White;
        }

        void SetReachableEmpties(ref Span<Move> legalMoves)
        {
            foreach (var move in legalMoves)
            {
                var diffTable = this.featureDiffTable[(int)move.Coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (FeatureType)((REACHABLE_EMPTY - UNREACHABLE_EMPTY) * diff.Diff);
            }
        }

        void RemoveReachableEmpties()
        {
            for (var i = 0; i < this.numPrevLegalMoves; i++)
            {
                var diffTable = this.featureDiffTable[(int)this.prevLegalMoves[i].Coord];
                foreach (var diff in diffTable)
                    this.features[diff.FeatureID.TupleID][diff.FeatureID.Idx] += (FeatureType)((UNREACHABLE_EMPTY - REACHABLE_EMPTY) * diff.Diff);
            }
        }

        struct FeatureDiff 
        {
            public (int TupleID, int Idx) FeatureID { get; set; }
            public int Diff { get; set; }
        }
    }
}
