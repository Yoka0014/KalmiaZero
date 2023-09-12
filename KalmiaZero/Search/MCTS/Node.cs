using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

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

            var diff = this.VisitCount - edge.VisitCount;
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

        public bool IsExpanded => this.Edges is not null;
        public bool ChildNodeWasInitialized => this.ChildNodes is not null;

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

        public Node CreateChildNode(int idx) 
        {
            Debug.Assert(this.ChildNodes is not null);
            return this.ChildNodes[idx] = new Node(); 
        }

        public void InitChildNodes() 
        {
            Debug.Assert(this.Edges is not null);
            this.ChildNodes = new Node[this.Edges.Length]; 
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Expand(Span<Move> moves)
        {
            if (moves.Length == 0)
            {
                this.Edges = new Edge[1];
                this.Edges[0].Move.Coord = BoardCoordinate.Pass;
                return;
            }

            this.Edges = new Edge[moves.Length];
            for (var i = 0; i < this.Edges.Length; i++)
                this.Edges[i].Move = moves[i];
        }
    }
}
