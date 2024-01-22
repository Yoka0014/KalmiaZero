using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;

using MathNet.Numerics.Distributions;

using static KalmiaZero.Search.MCTS.Training.FastPUCTConstantConfig;

namespace KalmiaZero.Search.MCTS.Training
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
        public PUCTValueType PolicyProb;
        public PUCTValueType Value;
        public int VisitCount;
        public PUCTValueType RewardSum;
        public EdgeLabel Label;

        public readonly bool IsProved => (this.Label & EdgeLabel.Proved) != 0;
        public readonly bool IsWin => this.Label == EdgeLabel.Win;
        public readonly bool IsLoss => this.Label == EdgeLabel.Loss;
        public readonly bool IsDraw => this.Label == EdgeLabel.Draw;
    }

    internal class Node
    {
        public GameInfo State;
        public int VisitCount;
        public int NumChildren;
        public Span<Edge> Edges => this.edges.AsSpan(0, this.NumChildren);
        public Span<Node> ChildNodes => this.childNodes.AsSpan(0, this.NumChildren);

        Edge[] edges;
        Node[] childNodes;

        public Node(NTupleGroup ntuples)
        {
            this.State = new GameInfo(ntuples);
            this.edges = new Edge[Constants.MAX_NUM_MOVES];
            this.childNodes = new Node[Constants.MAX_NUM_MOVES];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Edge[] Expand(Span<Move> moves)
        {
            if (moves.Length == 0)
            {
                this.edges[0].Move.Coord = BoardCoordinate.Pass;
                this.NumChildren = 1;
                return this.edges;
            }

            for (var i = 0; i < moves.Length; i++)
                this.edges[i].Move = moves[i];
            this.NumChildren = moves.Length;

            return this.edges;
        }

        public void Clear()
        {
            Array.Clear(this.edges, 0, this.NumChildren);
            Array.Clear(this.childNodes, 0, this.NumChildren);
            this.NumChildren = this.VisitCount = 0;
        }
    }

    internal class NodePool
    {
        readonly Node[] nodes;
        int loc = 0;
        readonly NTupleGroup NTUPLES;

        public NodePool(NTupleGroup ntuples, int size)
        {
            this.nodes = Enumerable.Range(0, size).Select(_ => new Node(ntuples)).ToArray();
            this.NTUPLES = ntuples;
        }

        public Node GetNode()
        {
            if (this.loc == this.nodes.Length)
                return new Node(this.NTUPLES);

            var node = this.nodes[this.loc++];
            node.Clear();
            return node;
        }

        public void Clear() => this.loc = 0;
    }

    internal static class FastPUCTConstantConfig
    {
        public const float PUCT_FACTOR = 1.0f;
    }

    /// <summary>
    /// Implementation of PUCT fast but with high memory consumption.
    /// This class is used for Training.
    /// </summary>
    internal class FastPUCT
    {
        const float EPSILON = 1.0e-6f;
        static ReadOnlySpan<GameResult> TO_OPPONENT_GAME_RESULT => new GameResult[3] { GameResult.Loss, GameResult.Win, GameResult.Draw };
        static ReadOnlySpan<PUCTValueType> GAME_RESULT_TO_REWARD => new PUCTValueType[3] { 1.0f, 0.0f, 0.5f };

        public double RootDirichletAlpha { get; init; } = 0.3;
        public double RootExplorationFraction { get; init; } = 0.25;

        readonly int NUM_SIMULATIONS;

        ValueFunction<PUCTValueType> valueFunc;
        NodePool nodePool;
        GameInfo rootState;
        Node? root;

        public FastPUCT(ValueFunction<PUCTValueType> valueFunc, int numSimulations)
        {
            this.valueFunc = valueFunc;
            this.NUM_SIMULATIONS = numSimulations;
            this.nodePool = new NodePool(valueFunc.NTuples, (int)(this.NUM_SIMULATIONS * 1.5));
        }

        public void SetRootState(ref Position pos) => this.rootState = new GameInfo(pos, this.valueFunc.NTuples);

        public void UpdateRootState(ref Move move) => this.rootState.Update(ref move);

        public void UpdateRootState(BoardCoordinate moveCoord)
        {
            var move = this.rootState.Position.GenerateMove(moveCoord);
            UpdateRootState(ref move);
        }

        public void PassRootState() => this.rootState.Pass();

        public void Search()
        {
            this.nodePool.Clear();
            this.root = this.nodePool.GetNode();
            this.rootState.CopyTo(ref this.root.State);
            InitRootChildNodes();

            for (var i = 0; i < this.NUM_SIMULATIONS; i++)
                VisitRootNode();
        }

        public Move SelectMaxVisitMove()
        {
            Debug.Assert(this.root is not null);

            var edges = this.root.Edges;
            var maxIdx = 0;
            for (var i = 1; i < edges.Length; i++)
                if (edges[i].VisitCount > edges[maxIdx].VisitCount)
                    maxIdx = i;
            return edges[maxIdx].Move;
        }

        public Move SelectMoveWithVisitCountDist(Random rand)
        {
            Debug.Assert(this.root is not null);

            var edges = this.root.Edges;
            var visitCountSum = this.root.VisitCount;
            var arrow = visitCountSum * rand.NextDouble();
            var sum = 0;
            var i = -1;
            do
                sum += edges[++i].VisitCount;
            while (sum < arrow);
            return edges[i].Move;
        }

        void InitRootChildNodes()
        {
            Debug.Assert(this.root is not null);

            this.root.Expand(this.rootState.Moves);
            if (this.rootState.Moves.Length > 1)
                SetPolicyProbsAndValues(this.root);

            // add noise
            var edges = this.root.Edges;
            var frac = this.RootExplorationFraction;
            var noise = Dirichlet.Sample(Random.Shared, Enumerable.Repeat(this.RootDirichletAlpha, edges.Length).ToArray());
            for (var i = 0; i < edges.Length; i++)
                edges[i].PolicyProb = (PUCTValueType)(edges[i].PolicyProb * (1.0 - frac) + noise[i] * frac);

            for (var i = 0; i < this.root.ChildNodes.Length; i++)
                CreateChildNode(this.root, i);
        }

        void VisitRootNode()
        {
            Debug.Assert(this.root is not null);

            var edges = this.root.Edges;

            int childIdx;
            bool isFirstVisit;
            childIdx = SelectRootChildNode();
            isFirstVisit = edges[childIdx].VisitCount == 0;

            ref var childEdge = ref edges[childIdx];
            if (isFirstVisit)
                UpdateNodeStats(this.root, ref childEdge, childEdge.Value);
            else
                UpdateNodeStats(this.root, ref childEdge, VisitNode<False>(this.root.ChildNodes[childIdx], ref childEdge));
        }

        PUCTValueType VisitNode<AfterPass>(Node node, ref Edge edgeToNode) where AfterPass : struct, IFlag
        {
            var state = node.State;
            PUCTValueType reward;
            Span<Edge> edges;
            if (node.NumChildren == 0) // need to expand
            {
                edges = node.Expand(state.Moves);

                if (state.Moves.Length != 0)
                    SetPolicyProbsAndValues(node);
            }
            else
                edges = node.Edges;

            if (state.Moves.Length == 0)  // pass
            {
                if (typeof(AfterPass) == typeof(True))  // gameover
                {
                    GameResult res = state.Position.GetGameResult();
                    edges[0].Label = (EdgeLabel)res | EdgeLabel.Proved;
                    edgeToNode.Label = (EdgeLabel)TO_OPPONENT_GAME_RESULT[(int)res] | EdgeLabel.Proved;

                    reward = GAME_RESULT_TO_REWARD[(int)res];
                }
                else if (edges[0].IsProved)
                    reward = GAME_RESULT_TO_REWARD[(int)(edges[0].Label ^ EdgeLabel.Proved)];
                else
                {
                    Node childNode = node.ChildNodes[0] ?? CreatePassChildNode(node);
                    reward = VisitNode<True>(childNode, ref edges[0]);

                    if (edges[0].IsProved)
                        edgeToNode.Label = InverseEdgeLabel(edges[0].Label);
                }

                UpdateNodeStats(node, ref edges[0], reward);
                return 1 - reward;
            }

            // not pass
            var childIdx = SelectChildNode(node, ref edgeToNode);
            ref var childEdge = ref edges[childIdx];
            var isFirstVisit = childEdge.VisitCount == 0;

            if (isFirstVisit)
                reward = childEdge.Value;
            else if (childEdge.IsProved)
                reward = GAME_RESULT_TO_REWARD[(int)(childEdge.Label ^ EdgeLabel.Proved)];
            else
            {
                var childNodes = node.ChildNodes;
                var childNode = childNodes[childIdx] ?? CreateChildNode(node, childIdx);
                reward = VisitNode<False>(childNode, ref childEdge);
            }

            UpdateNodeStats(node, ref childEdge, reward);
            return 1 - reward;
        }

        int SelectRootChildNode()
        {
            Debug.Assert(this.root is not null);

            var edges = this.root.Edges;
            var maxIdx = 0;
            var maxScore = PUCTValueType.NegativeInfinity;
            var visitSum = this.root.VisitCount;
            var sqrtVisitSum = MathF.Sqrt(visitSum + EPSILON);

            var lossCount = 0;
            var drawCount = 0;
            for (var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];

                if (edge.IsWin)
                    return i;

                if (edge.IsLoss)
                {
                    lossCount++;    // ignore loss edge.
                    continue;
                }

                if (edge.IsDraw)
                    drawCount++;    // just count up draw edge.

                // calculate PUCB score.
                var q = (float)(edge.RewardSum / (edge.VisitCount + EPSILON));
                var u = PUCT_FACTOR * edge.PolicyProb * sqrtVisitSum / (1.0f + edge.VisitCount);
                var score = q + u;

                if (score > maxScore)
                {
                    maxScore = score;
                    maxIdx = i;
                }
            }

            return maxIdx;
        }

        int SelectChildNode(Node parent, ref Edge edgeToParent)
        {
            var edges = parent.Edges;
            var maxIdx = 0;
            var maxScore = float.NegativeInfinity;
            var visitSum = parent.VisitCount;
            var sqrtVisitSum = MathF.Sqrt(visitSum + EPSILON);

            var drawCount = 0;
            var lossCount = 0;
            for (var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];

                if (edge.IsWin)
                {
                    // if there is a win edge from player view, it determines loss from opponent. 
                    edgeToParent.Label = EdgeLabel.Loss;
                    return i;
                }

                if (edge.IsLoss)
                {
                    lossCount++;
                    continue;   // avoid to select loss edge.
                }

                if (edge.IsDraw)
                    drawCount++;

                // calculate PUCB score.
                var q = (float)(edge.RewardSum / (edge.VisitCount + EPSILON));
                var u = PUCT_FACTOR * edge.PolicyProb * sqrtVisitSum / (1.0f + edge.VisitCount);
                var score = q + u;

                if (score > maxScore)
                {
                    maxScore = score;
                    maxIdx = i;
                }
            }

            if (lossCount + drawCount == edges.Length)
                edgeToParent.Label = (drawCount != 0) ? EdgeLabel.Draw : EdgeLabel.Win;

            return maxIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdateNodeStats(Node parent, ref Edge childEdge, float reward)
        {
            parent.VisitCount++;
            childEdge.VisitCount++;
            childEdge.RewardSum += reward;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Node CreateChildNode(Node node, int idx)
        {
            ref var state = ref node.State;
            var child = node.ChildNodes[idx] = this.nodePool.GetNode();
            var move = state.Moves[idx];
            state.Update(ref move);
            state.CopyTo(ref child.State);
            state.Undo(ref move, node.Edges);
            return child;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Node CreatePassChildNode(Node node)
        {
            ref var state = ref node.State;
            var child = node.ChildNodes[0] = this.nodePool.GetNode();
            state.Pass();
            state.CopyTo(ref child.State);
            state.Pass(node.Edges);
            return child;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SkipLocalsInit]
        unsafe void SetPolicyProbsAndValues(Node node)
        {
            ref var state = ref node.State;
            var edges = node.Edges;
            PUCTValueType value;
            PUCTValueType expValueSum = 0;
            var expValues = stackalloc PUCTValueType[edges.Length];
            for(var i = 0; i < edges.Length; i++)
            {
                Debug.Assert(edges[i].Move.Coord != BoardCoordinate.Pass);

                ref var edge = ref edges[i];
                edge.Move = state.Moves[i];
                state.Position.GenerateMove(ref edge.Move);
                state.Update(ref edge.Move);
                edge.Value = value = 1 - this.valueFunc.Predict(state.FeatureVector);
                expValueSum += expValues[i] = FastMath.Exp(value);
                state.Undo(ref edge.Move, edges);
            }

            // softmax
            for (var i = 0; i < edges.Length; i++)
                edges[i].PolicyProb = expValues[i] / expValueSum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static EdgeLabel InverseEdgeLabel(EdgeLabel label)
        {
            if ((label & EdgeLabel.Proved) == 0)
                return label;

            return (EdgeLabel)TO_OPPONENT_GAME_RESULT[(int)(label ^ EdgeLabel.Proved)] | EdgeLabel.Proved;
        }
    }
}
