using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.Search.MCTS
{
    internal enum EdgeLabel : byte
    {
        NotProved = 0x00,
        Proved = 0xf0,
        Win = Proved | GameResult.Win,
        Loss = Proved | GameResult.Loss,
        Draw = Proved | GameResult.Draw,
    }

    internal struct Edge
    {
        public Move Move;
        public Half PolicyProb;
        public Half Value;
        public uint VisitCount;
        public double RewardSum;
        public EdgeLabel Label;

        public readonly double ExpectedReward => this.RewardSum / this.VisitCount;
        public readonly bool IsProved => (this.Label & EdgeLabel.Proved) != 0;
        public readonly bool IsWin => this.Label == EdgeLabel.Win;
        public readonly bool IsLoss => this.Label == EdgeLabel.Loss;
        public readonly bool IsDraw => this.Label == EdgeLabel.Draw;

        public readonly bool PriorTo(ref Edge edge)
        {
            if (this.VisitCount == 0)
                return false;

            var diff = (long)this.VisitCount - edge.VisitCount;
            if (diff != 0)
                return diff > 0;
            return this.ExpectedReward > edge.ExpectedReward;
        }
    }

    internal class Node
    {
        public static uint ObjectCount => _ObjectCount;
        static uint _ObjectCount;

        public uint VisitCount;
        public Edge[]? Edges;
        public Node[]? ChildNodes;
        public byte usedFlag;

        public bool IsExpanded => this.Edges is not null;
        public bool ChildNodeWasInitialized => this.ChildNodes is not null;
        public bool IsUsed => this.usedFlag != 0;

        public double ExpectedReward
        {
            get
            {
                if (this.Edges is null)
                    return double.NaN;

                var reward = 0.0;
                for (var i = 0; i < this.Edges.Length; i++)
                    reward += this.Edges[i].RewardSum / this.VisitCount;
                return reward;
            }
        }

        public Node() => Interlocked.Increment(ref _ObjectCount);
        ~Node() => Interlocked.Decrement(ref _ObjectCount);

        public void Activate() => this.usedFlag = 1;
        public void DeActivate() => this.usedFlag = 0;

        public Node[] InitChildNodes()
        {
            Debug.Assert(this.Edges is not null);
            return this.ChildNodes = new Node[this.Edges.Length];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Edge[] Expand(Span<Move> moves)
        {
            if (moves.Length == 0)
            {
                this.Edges = new Edge[1];
                this.Edges[0].Move.Coord = BoardCoordinate.Pass;
                return this.Edges;
            }

            this.Edges = new Edge[moves.Length];
            for (var i = 0; i < this.Edges.Length; i++)
                this.Edges[i].Move = moves[i];

            return this.Edges;
        }
    }

    internal class NodeGC
    {
        const int COLLECT_INTERVAL_MS = 100;

        Stack<Node> garbage = new();
        CancellationTokenSource cts = new();

        public NodeGC()
        {
            Task.Run(() =>
            {
                Thread.Sleep(COLLECT_INTERVAL_MS);
                Collect();
            });
        }

        ~NodeGC() => this.cts.Cancel();

        public void Add(Node node)
        {
            lock (this.garbage)
                this.garbage.Push(node);
        }

        public void Collect()
        {
            var token = this.cts.Token;
            while (!token.IsCancellationRequested)
            {
                Node node;
                lock (this.garbage)
                {
                    if (this.garbage.Count != 0)
                    {
                        node = this.garbage.Pop();
                        DeleteNode(node);
                    }
                }
            }
        }

        public void DeleteNode(Node node)
        {
            node.VisitCount = 0u;
            node.Edges = null;
            node.ChildNodes = null;
            node.DeActivate();

            if (node.ChildNodes is null)
                return;

            foreach (var child in node.ChildNodes)
                if (child is not null)
                    DeleteNode(child);
        }
    }
}