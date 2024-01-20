using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;
using KalmiaZero.Search.AlphaBeta;

namespace KalmiaZero.Search.MCTS
{
    struct NodePoolEntry
    {
        public Bitboard Position;
        public Node Node = new();
        public NodePoolEntry() { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NodePoolCluster 
    {
        public const int NUM_ENTRIES = 4;

        NodePoolEntry entry0 = new();
        NodePoolEntry entry1 = new();
        NodePoolEntry entry2 = new();
        NodePoolEntry entry3 = new();

        public unsafe ref NodePoolEntry this[int idx]
        {
            get
            {
#if DEBUG
                if (idx < 0 || idx > NUM_ENTRIES)
                    throw new IndexOutOfRangeException();
#endif

                fixed (NodePoolCluster* self = &this)
                {
                    var entries = (NodePoolEntry*)self;
                    return ref entries[idx];
                }
            }
        }

        public NodePoolCluster() { }
    }

    internal class NodePool
    {
        const int DEFAULT_POOL_SIZE_MIB = 512;

        long size;
        NodePoolCluster[] pool;

        public NodePool() : this(DEFAULT_POOL_SIZE_MIB * 1024 * 1024) { }

        public NodePool(long size)
        {
            var numClusters = size / Marshal.SizeOf<NodePoolCluster>();
            this.size = 1L << Math.ILogB(numClusters);
            this.pool = new NodePoolCluster[this.size];
            for (var i = 0; i < this.pool.Length; i++)
                this.pool[i] = new();
        }

        public void Clear() => Array.Clear(this.pool);

        public void Resize(long size)
        {
            var numClusters = size / Marshal.SizeOf<NodePoolCluster>();
            var newSize = 1L << Math.ILogB(numClusters);

            if (this.size == newSize)
                return;

            this.size = newSize;
            this.pool = new NodePoolCluster[newSize];
            for (var i = 0; i < this.pool.Length; i++)
                this.pool[i] = new();
        }

        public Node GetNode(ref Position pos)
        {
            var idx = pos.ComputeHashCode() & (ulong)(this.size - 1L);
            ref var entries = ref this.pool[idx];

            for (var i = 0; i < NodePoolCluster.NUM_ENTRIES; i++)
            {
                ref var entry = ref entries[i];

                if (pos.Has(ref entry.Position) && !entry.Node.IsUsed)
                    return entry.Node;

                if (!entry.Node.IsUsed)
                {
                    entry.Position = pos.GetBitboard();
                    entry.Node.Activate();
                    return entry.Node;
                }
            }

            return new Node();
        }
    }
}
