//#define ENABLE_FEATURES_VALIDATION_CHECK

global using FeatureType = System.UInt16;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using KalmiaZero.Utils;
using KalmiaZero.Reversi;
using System.Diagnostics;
using System.Xml;
using System.Numerics;

namespace KalmiaZero.NTuple
{
    using static Utils.BitManipulations;
    using static Reversi.Constants;
    using static PositionFeaturesConstantConfig;

    public static class PositionFeaturesConstantConfig 
    {
        //public const FeatureType NUM_SQUARE_STATES = 4;     // BLACK, WHITE, REACHEABLE_EMPTY and UNREACHABLE_EMPTY
        public const int NUM_SQUARE_STATES = 3;   // BLACK, WHITE and EMPTY

        public const FeatureType BLACK = (int)DiscColor.Black;  // 0
        public const FeatureType WHITE = (int)DiscColor.White;  // 1
        public const FeatureType UNREACHABLE_EMPTY = 2;
        public const FeatureType REACHABLE_EMPTY = 3;
    }

    public unsafe struct Feature
    {
        public FeatureType this[int idx]
        {
            get
            {
#if DEBUG
                if (idx < 0 || idx >= this.Length)
                    throw new IndexOutOfRangeException();
#endif
                return this.values[idx];
            }

            set
            {
#if DEBUG
                if (idx < 0 || idx >= this.Length)
                    throw new IndexOutOfRangeException();
#endif
                this.values[idx] = value;
            }
        }

        public int Length { get; private set; }
        fixed FeatureType values[8];

        public Feature(int length)
        {
            if (length < 0 || length > 8)
                throw new ArgumentOutOfRangeException(nameof(length));
            this.Length = length;
        }

        public void CopyTo(ref Feature dest)
        {
            for (var i = 0; i < this.Length; i++)
                dest.values[i] = this.values[i];
            dest.Length = this.Length;
        }
    }

    /// <summary>
    /// Represents features of n-tuples in a position.
    /// </summary>
    public class PositionFeatureVector
    {
        public static PositionFeatureVector Empty => EMPTY;
        static readonly PositionFeatureVector EMPTY = new();

        public DiscColor SideToMove { get; private set; }
        public int NumNTuples => this.NTuples.Length;
        public NTuples NTuples { get; }

        public Feature[] Features { get; private set; }  // features[nTupleID]
        readonly FeatureDiff[] featureDiffTable = new FeatureDiff[NUM_SQUARES];

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
            this.Features = new Feature[this.NTuples.Length];
            var tuples = this.NTuples.Tuples;
            for (var nTupleID = 0; nTupleID < this.Features.Length; nTupleID++)
                this.Features[nTupleID] = new Feature(tuples[nTupleID].NumSymmetricExpansions);

            (this.playerUpdator, this.opponentUpdator) = (Update<Black>, Update<White>);
            (this.playerRestorer, this.opponentRestorer) = (Undo<White>, Undo<Black>);
            
            InitFeatureDiffTable();
        }

        PositionFeatureVector()
        {
            this.NTuples = new NTuples();
            this.Features = Array.Empty<Feature>();
            this.featureDiffTable = Array.Empty<FeatureDiff>();
            (this.playerUpdator, this.opponentUpdator) = (Update<Black>, Update<White>);
            (this.playerRestorer, this.opponentRestorer) = (Undo<White>, Undo<Black>);
        }

        public PositionFeatureVector(PositionFeatureVector pf)
        {
            this.SideToMove = pf.SideToMove;
            this.NTuples = pf.NTuples;
            this.Features = new Feature[pf.NumNTuples];
            for(var i = 0; i < this.Features.Length; i++)
                pf.Features[i].CopyTo(ref this.Features[i]);

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
            var diffs = new List<(int nTupleID, int idx, FeatureType diff)>();
            var tuples = this.NTuples.Tuples;
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
            {
                diffs.Clear();
                for (var nTupleID = 0; nTupleID < tuples.Length; nTupleID++)
                {
                    for (var idx = 0; idx < tuples[nTupleID].NumSymmetricExpansions; idx++)
                    {
                        var coords = tuples[nTupleID].GetCoordinates(idx);
                        var coordIdx = Array.IndexOf(coords.ToArray(), coord);
                        if (coordIdx != -1)
                            diffs.Add((nTupleID, idx, (FeatureType)this.NTuples.PowTable[coords.Length - coordIdx - 1]));
                    }
                }
                this.featureDiffTable[(int)coord].Values = diffs.ToArray();
            }
        }

        public ref Feature GetFeature(int nTupleID) => ref this.Features[nTupleID];

        public void Init(Bitboard bitboard, DiscColor sideToMove, Span<Move> legalMoves)
        {
            var pos = new Position(bitboard, sideToMove);
            Init(ref pos, legalMoves);
        }

        public void Init(ref Position pos, Span<Move> legalMoves)
        {
            this.SideToMove = pos.SideToMove;
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

            if (NUM_SQUARE_STATES == 4)
            {
                legalMoves.CopyTo(this.prevLegalMoves);
                this.numPrevLegalMoves = legalMoves.Length;
            }

            for (var nTupleID = 0; nTupleID < this.Features.Length; nTupleID++)
            {
                ReadOnlySpan<NTupleInfo> tuples = this.NTuples.Tuples;
                ref Feature f = ref this.Features[nTupleID];
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

        public unsafe void CopyTo(PositionFeatureVector dest)
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

            fixed (Feature* features = this.Features)
            fixed (Feature* destFeatures = dest.Features)
            {
                for (var i = 0; i < this.NTuples.Length; i++)
                    features[i].CopyTo(ref destFeatures[i]);
            }

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

            AssertFeaturesAreValid();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(ref Move move, Span<Move> legalMoves)
        {
            if (NUM_SQUARE_STATES == 4)
                RemoveReachableEmpties();

            this.playerRestorer.Invoke(ref move);
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        unsafe void Update<SideToMove>(ref Move move) where SideToMove : struct, IDiscColor
        {
            var placer = typeof(SideToMove) == typeof(Black) ? BLACK - UNREACHABLE_EMPTY : WHITE - UNREACHABLE_EMPTY;
            var flipper = typeof(SideToMove) == typeof(Black) ? BLACK - WHITE : WHITE - BLACK;

            fixed(Feature* features = this.Features)
            fixed (FeatureDiff* featureDiffTable = this.featureDiffTable)
            {
                foreach (var (nTupleID, idx, diff) in featureDiffTable[(int)move.Coord].Values)
                    features[nTupleID][idx] += (FeatureType)(placer * diff);

                ulong flipped = move.Flip;
                for (int coord = FindFirstSet(flipped); flipped != 0; coord = FindNextSet(ref flipped))
                {
                    foreach (var (nTupleID, idx, diff) in featureDiffTable[coord].Values)
                        features[nTupleID][idx] += (FeatureType)(flipper * diff);
                }
            }

            AssertFeaturesAreValid();

            this.SideToMove = typeof(SideToMove) == typeof(Black) ? DiscColor.White : DiscColor.Black;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        unsafe void Undo<SideToMove>(ref Move move) where SideToMove : struct, IDiscColor
        {
            var remover = typeof(SideToMove) == typeof(Black) ? UNREACHABLE_EMPTY - BLACK : UNREACHABLE_EMPTY - WHITE;
            var flipper = typeof(SideToMove) == typeof(Black) ? WHITE - BLACK : BLACK - WHITE;

            fixed (Feature* features = this.Features)
            fixed (FeatureDiff* featureDiffTable = this.featureDiffTable)
            {
                foreach (var (nTupleID, idx, diff) in featureDiffTable[(int)move.Coord].Values)
                    features[nTupleID][idx] += (FeatureType)(remover * diff);

                ulong flipped = move.Flip;
                for (int coord = FindFirstSet(flipped); flipped != 0; coord = FindNextSet(ref flipped))
                {
                    foreach (var (nTupleID, idx, diff) in featureDiffTable[coord].Values)
                        features[nTupleID][idx] += (FeatureType)(flipper * diff);
                }
            }

            AssertFeaturesAreValid();

            this.SideToMove = typeof(SideToMove) == typeof(Black) ? DiscColor.Black : DiscColor.White;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        unsafe void SetReachableEmpties(ref Span<Move> legalMoves)
        {
            fixed (Feature* features = this.Features)
            fixed (FeatureDiff* featureDiffTable = this.featureDiffTable)
            {
                foreach (var move in legalMoves)
                    foreach (var (nTupleID, idx, diff) in featureDiffTable[(int)move.Coord].Values)
                        features[nTupleID][idx] += (FeatureType)((REACHABLE_EMPTY - UNREACHABLE_EMPTY) * diff);
            }

            AssertFeaturesAreValid();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        unsafe void RemoveReachableEmpties()
        {
            fixed (Feature* features = this.Features)
            fixed (FeatureDiff* featureDiffTable = this.featureDiffTable)
            fixed (Move* prevLegalMoves = this.prevLegalMoves)
            {
                for (var i = 0; i < this.numPrevLegalMoves; i++)
                    foreach (var (nTupleID, idx, diff) in featureDiffTable[(int)prevLegalMoves[i].Coord].Values)
                        features[nTupleID][idx] += (FeatureType)((UNREACHABLE_EMPTY - REACHABLE_EMPTY) * diff);
            }

            AssertFeaturesAreValid();
        }

        void AssertFeaturesAreValid()
        {
#if DEBUG && ENABLE_FEATURES_VALIDATION_CHECK
            for (var nTupleID = 0; nTupleID < this.Features.Length; nTupleID++)
            {
                ReadOnlySpan<NTupleInfo> tuples = this.NTuples.Tuples;
                ref Feature f = ref this.Features[nTupleID];
                for (var i = 0; i < tuples[nTupleID].NumSymmetricExpansions; i++)
                    Debug.Assert(f[i] >= 0 && f[i] < this.NTuples.NumPossibleFeatures[nTupleID]);
            }
#endif
        }

        struct FeatureDiff 
        {
            public (int NTupleID, int Idx, FeatureType Diff)[] Values { get; set; }
        }
    }
}
