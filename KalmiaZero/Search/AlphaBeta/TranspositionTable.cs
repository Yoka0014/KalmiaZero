using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.Search.AlphaBeta
{
    struct TTEntry
    {
        public Bitboard Position;
        public Half Lower;
        public Half Upper;
        public BoardCoordinate Move;
        public byte Depth;
        public byte Generation;

        public TTEntry() { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TTCluster 
    {
        public const int NUM_ENTRIES = 4;

        TTEntry entry0;
        TTEntry entry1;
        TTEntry entry2;
        TTEntry entry3;

        public unsafe ref TTEntry this[int idx]
        {
            get
            {
#if DEBUG
                if (idx < 0 || idx > NUM_ENTRIES)
                    throw new IndexOutOfRangeException();
#endif

                fixed (TTCluster* self = &this)
                {
                    var entries = (TTEntry*)self;
                    return ref entries[idx];
                }
            }
        }
    }

    internal class TranspositionTable
    {
        const byte GEN_INC = 4;

        public byte Generation => this.generation;

        long size;
        TTCluster[] tt;
        byte generation;

        public TranspositionTable(long size)
        {
            var numClusters = size / (long)Marshal.SizeOf<TTCluster>();
            this.size = 1L << Math.ILogB(numClusters);
            this.tt = new TTCluster[this.size];
            this.generation = 0;
        }

        public void Clear()
        {
            Array.Clear(this.tt);
            this.generation = 0;
        }

        public void Resize(long size)
        {
            var newSize = 1L << Math.ILogB(size);
            if (this.size == newSize)
                return;

            this.size = newSize;
            this.tt = new TTCluster[newSize];
            this.generation = 0;
        }

        public void IncrementGeneration() => this.generation += GEN_INC;

        public void SaveAt(ref TTEntry entry, ref Position pos, AlphaBetaEvalType lower, AlphaBetaEvalType upper, int depth)
            => SaveAt(ref entry, ref pos, BoardCoordinate.Null, lower, upper, depth);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SaveAt(ref TTEntry entry, ref Position pos, BoardCoordinate move, AlphaBetaEvalType lower, AlphaBetaEvalType upper, int depth)
        {
            entry.Position = pos.GetBitboard();
            entry.Move = move;
            entry.Lower = (Half)lower;
            entry.Upper = (Half)upper;
            entry.Depth = (byte)depth;
            entry.Generation = this.generation;
        }

        public ref TTEntry GetEntry(ref Position pos, out bool hit)
        {
            var idx = pos.ComputeHashCode() & (ulong)(this.size - 1L);
            ref var entries = ref this.tt[idx];

            for (var i = 0; i < TTCluster.NUM_ENTRIES; i++)
            {
                ref var entry = ref entries[i]; 
                if (pos.Has(ref entry.Position))
                {
                    hit = true;
                    return ref entries[i];
                }

                if(entry.Depth == 0)
                {
                    hit = false;
                    return ref entries[i];
                }
            }

            // need to overwrite an entry
            ref var replace = ref entries[0];
            for(var i = 1; i < TTCluster.NUM_ENTRIES; i++)
                if (GetPenalizedDepth(ref replace) > GetPenalizedDepth(ref entries[i]))
                    replace = ref entries[i];

            hit = false;
            return ref replace;
        }

        int GetPenalizedDepth(ref TTEntry entry) => entry.Generation - (this.generation - entry.Generation);
    }
}
