﻿global using PUCTValueType = System.Single;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;

using MathNet.Numerics.Distributions;

namespace KalmiaZero.Search.MCTS
{
    using static PUCTConstantOptions;

    static class PUCTConstantOptions
    {
        // cite: https://doi.org/10.1145/3293475.3293486
        public const bool ENABLE_EXACT_WIN_MCTS = true;

        public const bool ENABLE_SINGLE_THREAD_MODE = false;

        public const bool USE_UNIFORM_POLICY = false;
        public const float PUCT_FACTOR = 1.0f;
        public const uint VIRTUAL_LOSS = 1;
    }

    public class MoveEvaluation : IComparable<MoveEvaluation>
    {
        public BoardCoordinate Move { get; init; }
        public double Effort { get; init; }
        public uint PlayoutCount { get; init; }
        public double ExpectedReward { get; init; }
        public GameResult GameResult { get; init; }
        public ReadOnlySpan<BoardCoordinate> PV => this.pv;

        BoardCoordinate[] pv;

        public MoveEvaluation(IEnumerable<BoardCoordinate> pv) => this.pv = pv.ToArray();

        public bool PriorTo(MoveEvaluation other)
        {
            if (this.GameResult != GameResult.NotOver)
            {
                if (this.GameResult == GameResult.Win)
                    return true;

                if (this.GameResult == GameResult.Loss)
                    return false;

                if (this.GameResult == GameResult.Draw)
                {
                    if (other.GameResult == GameResult.Loss || other.GameResult == GameResult.Draw)
                        return true;
                }
            }

            var diff = (long)this.PlayoutCount - other.PlayoutCount;
            if (diff != 0)
                return diff > 0;
            return this.ExpectedReward > other.ExpectedReward;
        }

        public int CompareTo(MoveEvaluation? other)
        {
            if (other is null)
                return 1;

            if (this.GameResult != GameResult.NotOver)
            {
                if (this.GameResult == other.GameResult)
                    return 0;

                if (this.GameResult == GameResult.Win)
                    return 1;

                if (this.GameResult == GameResult.Loss)
                    return -1;

                if (this.GameResult == GameResult.Draw && other.GameResult == GameResult.Loss)
                    return 1;
            }

            if (other.GameResult != GameResult.NotOver)
            {
                if (other.GameResult == this.GameResult)
                    return 0;

                if (other.GameResult == GameResult.Win)
                    return -1;

                if (other.GameResult == GameResult.Loss)
                    return 1;

                if (other.GameResult == GameResult.Draw && this.GameResult == GameResult.Loss)
                    return -1;
            }

            var diff = (long)this.PlayoutCount - other.PlayoutCount;
            if (diff == 0)
                return 0;
            return diff > 0 ? 1 : -1;
        }
    }

    public class SearchInfo
    {
        public MoveEvaluation RootEval { get; }
        public ReadOnlySpan<MoveEvaluation> ChildEvals => this.childEvals;
        readonly MoveEvaluation[] childEvals;

        public SearchInfo(MoveEvaluation rootEval, IEnumerable<MoveEvaluation> childEvals)
        {
            this.RootEval = rootEval;
            this.childEvals = childEvals.ToArray();
        }
    }

    public enum SearchEndStatus : ushort
    {
        Completed = 0x0001,
        Timeout = 0x0004,
        Proved = 0x0002,
        SuspendedByStopSignal = 0x0008,
        EarlyStopping = 0x0010,
        Extended = 0x0020
    }

    public class PUCT
    {
        const float EPSILON = 1.0e-6f;
        static ReadOnlySpan<GameResult> TO_OPPONENT_GAME_RESULT => new GameResult[3] { GameResult.Loss, GameResult.Win, GameResult.Draw };
        static ReadOnlySpan<double> GAME_RESULT_TO_REWARD => new double[3] { 1.0, 0.0, 0.5 };

        public event EventHandler<SearchInfo?>? SearchInfoWasSent;
        public int SearchInfoSendIntervalCs { get; set; }
        public bool EnableEarlyStopping { get; set; }
        public int SearchEllapsedMs => this.isSearching ? Environment.TickCount - this.searchStartTime : this.searchEndTime - this.searchStartTime;
        public uint NodeCount => this.nodeCountPerThread.Sum();
        public double Nps => this.NodeCount / (this.SearchEllapsedMs * 1.0e-3);

        public double RootDirichletAlpha { get; set; } = 0.3;
        public double RootExplorationFraction { get; set; } = 0.0;

        public double EnoughSearchRate
        {
            get => this.enoughSearchRate;

            set
            {
                if (value < 1.0)
                    throw new ArgumentOutOfRangeException(nameof(this.EnoughSearchRate), $"The enough search rate must be more than or equal 1.0");

                this.enoughSearchRate = value;
            }
        }

        double enoughSearchRate = 1.5;

        public int NumThreads
        {
            get => this.numThreads;

            set
            {
                if (this.IsSearching)
                    throw new InvalidOperationException("Cannot set the number of threads while searching.");

                this.numThreads = value;
                var nodeCount = this.nodeCountPerThread.Sum();
                this.nodeCountPerThread = new uint[this.numThreads];
                this.nodeCountPerThread[0] = nodeCount;
            }
        }

        int numThreads = Environment.ProcessorCount;

        public bool IsSearching => this.isSearching;
        volatile bool isSearching;

        ValueFunction<PUCTValueType> valueFunc;

        Node? root;
        Position rootState;
        EdgeLabel rootEdgeLabel;

        int searchStartTime = 0;
        int searchEndTime = 0;
        uint[] nodeCountPerThread;
        uint maxPlayoutCount;
        uint playoutCount;

        CancellationTokenSource? cts;

        public PUCT(ValueFunction<PUCTValueType> valueFunc)
        {
            this.valueFunc = valueFunc;
            this.nodeCountPerThread = new uint[numThreads];
        }

        public void SetRootState(ref Position pos)
        {
            this.rootState = pos;
            this.root = new Node();
            InitRootChildNodes();
            this.rootEdgeLabel = EdgeLabel.NotProved;
            Array.Clear(this.nodeCountPerThread);
        }

        public bool TransitionRootStateToChildState(BoardCoordinate move)
        {
            if (this.root is null || !this.root.IsExpanded || !this.root.ChildNodeWasInitialized)
                return false;

            Debug.Assert(this.root.Edges is not null && this.root.ChildNodes is not null);

            Edge[] edges = this.root.Edges;
            for (var i = 0; i < edges.Length; i++)
            {
                if (move == edges[i].Move.Coord && this.root.ChildNodes[i] is not null)
                {
                    if (move != BoardCoordinate.Pass)
                        this.rootState.Update(ref edges[i].Move);
                    else
                        this.rootState.Pass();

                    this.root = this.root.ChildNodes[i];
                    InitRootChildNodes();
                    this.rootEdgeLabel = edges[i].Label;
                    Array.Clear(this.nodeCountPerThread);
                    return true;
                }
            }

            return false;
        }

        public SearchInfo? CollectSearchInfo()
        {
            if (this.root is null || this.root.Edges is null)
                return null;

            Edge[] edges = this.root.Edges;
            var childEvals = new MoveEvaluation[this.root.Edges.Length];

            var rootEval = new MoveEvaluation(GetPV(this.root))
            {
                Move = BoardCoordinate.Null,
                Effort = 1.0,
                PlayoutCount = (uint)this.root.VisitCount,
                GameResult = ((this.rootEdgeLabel & EdgeLabel.Proved) != 0) ? (GameResult)(this.rootEdgeLabel ^ EdgeLabel.Proved) : GameResult.NotOver,
                ExpectedReward = ((this.rootEdgeLabel & EdgeLabel.Proved) != 0) ? GAME_RESULT_TO_REWARD[(int)(this.rootEdgeLabel ^ EdgeLabel.Proved)] : this.root.ExpectedReward
            };

            for (var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                childEvals[i] = new MoveEvaluation(GetPV(this.root.ChildNodes?[i], edge.Move.Coord))
                {
                    Move = edge.Move.Coord,
                    Effort = (double)edge.VisitCount / this.root.VisitCount,
                    PlayoutCount = edge.VisitCount,
                    GameResult = edge.IsProved ? (GameResult)(edge.Label ^ EdgeLabel.Proved) : GameResult.NotOver,
                    ExpectedReward = edge.IsProved ? GAME_RESULT_TO_REWARD[(int)(edge.Label ^ EdgeLabel.Proved)] : edge.ExpectedReward
                };
            }

            Array.Sort(childEvals, (x, y) => -x.CompareTo(y));

            return new SearchInfo(rootEval, childEvals);
        }

        public Move SelectMaxVisitMove() => this.root.Edges[SelectBestChildNode(this.root)].Move;

        public Move SelectMoveWithVisitCountDist(Random rand)
        {
            var edges = this.root.Edges;
            var visitCountSum = this.root.VisitCount;
            var arrow = visitCountSum * rand.NextDouble();
            var sum = 0UL;
            var i = -1;
            do
                sum += edges[++i].VisitCount;
            while (sum < arrow);
            return edges[i].Move;
        }

        public async Task<SearchEndStatus> SearchAsync(uint numPlayouts, int timeLimitCs, int extraTimeCs, Action<SearchEndStatus> searchEndCallback)
        {
            this.cts = new CancellationTokenSource();
            this.isSearching = true;

            var status = await Task.Run(() =>
            {
                var status = Search(numPlayouts, timeLimitCs, extraTimeCs);
                searchEndCallback(status);
                return status;
            }).ConfigureAwait(false);

            return status;
        }

        public SearchEndStatus Search(uint numPlayouts, int timeLimitCs, int extraTimeCs)
        {
            if (this.root is null)
                throw new InvalidOperationException("The root state was not initialized.");

            this.isSearching = true;
            this.cts ??= new CancellationTokenSource();
            this.maxPlayoutCount = numPlayouts;
            this.playoutCount = 0;

            var timeLimitMs = timeLimitCs * 10;
            var extraTimeMs = extraTimeCs * 10;
            this.searchStartTime = Environment.TickCount;

            var searchTasks = new Task[ENABLE_SINGLE_THREAD_MODE ? 1 : this.numThreads];
            for (var i = 0; i < searchTasks.Length; i++)
            {
                var game = new GameInfo(this.rootState, this.valueFunc.NTuples);
                var threadID = i;
                searchTasks[i] = Task.Run(() => SearchWorker(threadID, ref game, this.cts.Token));
            }

            SearchEndStatus status = WaitForSearch(searchTasks, timeLimitMs, extraTimeMs);

            this.SearchInfoWasSent?.Invoke(this, CollectSearchInfo());

            this.isSearching = false;
            this.searchEndTime = Environment.TickCount;
            this.cts = null;

            return status;
        }

        public void SearchOnSingleThread(uint numPlayouts)
        {
            var game = new GameInfo(this.rootState, this.valueFunc.NTuples);
            var g = new GameInfo(this.valueFunc.NTuples);
            for (var i = 0u; i < numPlayouts && this.rootEdgeLabel == EdgeLabel.NotProved; i++)
            {
                this.playoutCount++;
                game.CopyTo(ref g);
                VisitRootNode(0, ref g);
            }
        }

        public void SendStopSearchSignal() => this.cts?.Cancel();

        void SearchWorker(int threadID, ref GameInfo game, CancellationToken ct)
        {
            var g = new GameInfo(this.valueFunc.NTuples);
            while (!ct.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref this.playoutCount) > this.maxPlayoutCount)
                {
                    Interlocked.Decrement(ref this.playoutCount);
                    continue;
                }

                game.CopyTo(ref g);
                VisitRootNode(threadID, ref g);
            }
        }

        SearchEndStatus WaitForSearch(Task[] searchTasks, int timeLimitMs, int extraTimeMs)
        {
            var endStatus = SearchEndStatus.Completed;
            var checkPointMs = Environment.TickCount;
            var enteredExtraSearch = false;

            while (true)
            {
                if (CanStopSearch(timeLimitMs, ref endStatus))
                {
                    var suspended = ((endStatus & SearchEndStatus.SuspendedByStopSignal) != 0);
                    if (!suspended && !enteredExtraSearch && extraTimeMs != 0 && ExtraSearchIsNecessary())
                    {
                        this.maxPlayoutCount *= 2;
                        timeLimitMs += extraTimeMs;
                        enteredExtraSearch = true;
                    }
                    else
                        break;
                }

                var searchInfoSendIntervalMs = this.SearchInfoSendIntervalCs * 10;
                if (this.SearchInfoSendIntervalCs * 10 != 0 && Environment.TickCount - checkPointMs >= this.SearchInfoSendIntervalCs * 10)
                {
                    this.SearchInfoWasSent?.Invoke(this, CollectSearchInfo());
                    checkPointMs = Environment.TickCount;
                }

                Thread.Sleep(10);
            }

            this.cts?.Cancel();

            if (enteredExtraSearch)
                endStatus |= SearchEndStatus.Extended;

            Task.WaitAll(searchTasks);

            return endStatus;
        }

        bool CanStopSearch(int timeLimitMs, ref SearchEndStatus endStatus)
        {
            Debug.Assert(this.cts is not null);

            if (this.cts.IsCancellationRequested)
            {
                endStatus = SearchEndStatus.SuspendedByStopSignal;
                return true;
            }

            if ((this.rootEdgeLabel & EdgeLabel.Proved) != 0)
            {
                endStatus = SearchEndStatus.Proved;
                return true;
            }

            if (this.SearchEllapsedMs >= timeLimitMs)
            {
                endStatus = SearchEndStatus.Timeout;
                return true;
            }

            if (this.playoutCount >= this.maxPlayoutCount)
            {
                endStatus = SearchEndStatus.Completed;
                return true;
            }

            if (this.EnableEarlyStopping && CanDoEarlyStopping(timeLimitMs))
            {
                endStatus = SearchEndStatus.EarlyStopping;
                return true;
            }

            return false;
        }

        bool CanDoEarlyStopping(int timeLimitMs)
        {
            if (this.SearchEllapsedMs < timeLimitMs * 0.1)  // Consume at least 10% of the time limit.
                return false;

            Debug.Assert(this.root is not null && this.root.Edges is not null);

            // Even if consume the rest of the time to search the second best node, if the playout count of the second best node will never reach its the best node, further search is pointless.
            Edge[] edges = this.root.Edges;
            (var bestIdx, var secondBestIdx) = GetTop2Children();
            return (edges[bestIdx].VisitCount - edges[secondBestIdx].VisitCount) > this.Nps * (timeLimitMs - this.SearchEllapsedMs) * 1.0e-3;
        }

        bool ExtraSearchIsNecessary()
        {
            Debug.Assert(this.root is not null && this.root.Edges is not null);

            (var i, var j) = GetTop2Children();
            ref Edge best = ref this.root.Edges[i];
            ref Edge second = ref this.root.Edges[j];
            return second.ExpectedReward > best.ExpectedReward  // values are inversed.
                || second.VisitCount * this.enoughSearchRate > best.VisitCount; // not enough difference of playout count.
        }

        (int bestIdx, int secondBestIdx) GetTop2Children()
        {
            Debug.Assert(this.root is not null);
            Debug.Assert(this.root.Edges is not null);

            Edge[] edges = this.root.Edges;
            (var bestIdx, var secondBestIdx) = (edges[0].VisitCount > edges[1].VisitCount) ? (0, 1) : (1, 0);
            for (var i = 2; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                if (edge.VisitCount > edges[bestIdx].VisitCount)
                    bestIdx = i;
                else if (edge.VisitCount > edges[secondBestIdx].VisitCount)
                    secondBestIdx = i;
            }

            return (bestIdx, secondBestIdx);
        }

        IEnumerable<BoardCoordinate> GetPV(Node? node, BoardCoordinate prevMove = BoardCoordinate.Null)
        {
            if (prevMove != BoardCoordinate.Null)
                yield return prevMove;

            if (node is null || node.Edges is null)
                yield break;

            var childIdx = SelectBestChildNode(node);
            var childNode = node.ChildNodes?[childIdx];
            foreach (var move in GetPV(childNode, node.Edges[childIdx].Move.Coord))
                yield return move;
        }

        void InitRootChildNodes()
        {
            Debug.Assert(this.root is not null);

            if (!this.root.IsExpanded)
            {
                var game = new GameInfo(this.rootState, this.valueFunc.NTuples);
                if (game.Moves.Length == 0)
                    return;

                this.root.Expand(game.Moves);

                Debug.Assert(this.root.Edges is not null);

                if (game.Moves.Length != 0)
                    SetPolicyProbsAndValues(ref game, this.root.Edges);
            }

            Debug.Assert(this.root.Edges is not null);

            var edges = this.root.Edges;
            var frac = this.RootExplorationFraction;
            for (var i = 0; i < edges.Length; i++)
                edges[i].PolicyProb = (Half)((double)edges[i].PolicyProb * (1.0 - frac) + Gamma.Sample(this.RootDirichletAlpha, 1.0) * frac);

            if (!this.root.ChildNodeWasInitialized)
                this.root.InitChildNodes();

            Debug.Assert(this.root.ChildNodes is not null);

            for (var i = 0; i < this.root.ChildNodes.Length; i++)
            {
                if (this.root.ChildNodes[i] is null)
                    this.root.CreateChildNode(i);
            }
        }

        void VisitRootNode(int threadID, ref GameInfo game)
        {
            Debug.Assert(this.root is not null);
            Debug.Assert(this.root.Edges is not null);
            Debug.Assert(this.root.ChildNodes is not null);

            Edge[] edges = this.root.Edges;

            int childIdx;
            bool isFirstVisit;
            lock (this.root)
            {
                childIdx = SelectRootChildNode();
                isFirstVisit = edges[childIdx].VisitCount == 0;
                AddVirtualLoss(root, ref edges[childIdx]);
            }

            ref var childEdge = ref edges[childIdx];
            if (isFirstVisit)
            {
                this.nodeCountPerThread[threadID]++;
                UpdateNodeStats(this.root, ref childEdge, (double)childEdge.Value);
            }
            else
                UpdateNodeStats(this.root, ref childEdge, VisitNode<False>(threadID, ref game, this.root.ChildNodes[childIdx], ref childEdge));
        }

        double VisitNode<AfterPass>(int threadID, ref GameInfo game, Node node, ref Edge edgeToNode) where AfterPass : struct, IFlag
        {
            var lockTaken = false;
            try
            {
                Monitor.Enter(node, ref lockTaken);

                double reward;
                Edge[] edges;
                if (node.Edges is null) // need to expand
                {
                    if (typeof(AfterPass) == typeof(False))
                        game.Update(ref edgeToNode.Move);
                    else
                        game.Pass();

                    edges = node.Expand(game.Moves);

                    if (game.Moves.Length != 0)
                        SetPolicyProbsAndValues(ref game, edges);
                }
                else
                {
                    if (typeof(AfterPass) == typeof(False))
                        game.Update(ref edgeToNode.Move, node.Edges);
                    else
                        game.Pass(node.Edges);

                    edges = node.Edges;
                }

                if (game.Moves.Length == 0)  // pass
                {
                    if (typeof(AfterPass) == typeof(True))  // gameover
                    {
                        GameResult res = game.Position.GetGameResult();
                        edges[0].Label = (EdgeLabel)res | EdgeLabel.Proved;
                        edgeToNode.Label = (EdgeLabel)TO_OPPONENT_GAME_RESULT[(int)res] | EdgeLabel.Proved;

                        Monitor.Exit(node);
                        lockTaken = false;

                        reward = GAME_RESULT_TO_REWARD[(int)res];
                    }
                    else if (edges[0].IsProved)
                    {
                        Monitor.Exit(node);
                        lockTaken = false;

                        reward = GAME_RESULT_TO_REWARD[(int)(edges[0].Label ^ EdgeLabel.Proved)];
                    }
                    else
                    {
                        Node childNode;
                        if (node.ChildNodes is null)
                        {
                            node.InitChildNodes();
                            childNode = node.CreateChildNode(0);
                        }
                        else
                            childNode = node.ChildNodes[0];

                        Monitor.Exit(node);
                        lockTaken = false;

                        reward = VisitNode<True>(threadID, ref game, childNode, ref edges[0]);

                        if (edges[0].IsProved)
                            edgeToNode.Label = InverseEdgeLabel(edges[0].Label);
                    }

                    UpdatePassNodeStats(node, ref edges[0], reward);
                    return 1.0 - reward;
                }

                // not pass
                var childIdx = SelectChildNode(node, ref edgeToNode);
                ref var childEdge = ref edges[childIdx];
                var isFirstVisit = childEdge.VisitCount == 0;
                AddVirtualLoss(node, ref childEdge);

                if (isFirstVisit)
                {
                    Monitor.Exit(node);
                    lockTaken = false;

                    this.nodeCountPerThread[threadID]++;
                    reward = (double)childEdge.Value;
                }
                else if (childEdge.IsProved)
                {
                    Monitor.Exit(node);
                    lockTaken = false;

                    reward = GAME_RESULT_TO_REWARD[(int)(childEdge.Label ^ EdgeLabel.Proved)];
                }
                else
                {
                    Node[] childNodes = node.ChildNodes is null ? node.InitChildNodes() : node.ChildNodes;
                    var childNode = childNodes[childIdx] ?? node.CreateChildNode(childIdx);

                    Monitor.Exit(node);
                    lockTaken = false;

                    reward = VisitNode<False>(threadID, ref game, childNode, ref childEdge);
                }

                UpdateNodeStats(node, ref childEdge, reward);
                return 1.0 - reward;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(node);
            }
        }

        int SelectRootChildNode()
        {
            Debug.Assert(this.root is not null);
            Debug.Assert(this.root.Edges is not null);

            Edge[] edges = this.root.Edges;
            var maxIdx = 0;
            var maxScore = float.NegativeInfinity;
            var visitSum = this.root.VisitCount;
            var sqrtVisitSum = MathF.Sqrt(visitSum + EPSILON);

            var lossCount = 0;
            var drawCount = 0;
            for (var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];

                if (ENABLE_EXACT_WIN_MCTS)
                {
                    if (edge.IsWin)
                    {
                        this.rootEdgeLabel = EdgeLabel.Win;     // definitely select win edge.
                        return i;
                    }

                    if (edge.IsLoss)
                    {
                        lossCount++;    // ignore loss edge.
                        continue;
                    }

                    if (edge.IsDraw)
                        drawCount++;    // just count up draw edge.
                }

                // calculate PUCB score.
                var q = (float)(edge.RewardSum / (edge.VisitCount + EPSILON));
                var u = PUCT_FACTOR * (float)edge.PolicyProb * sqrtVisitSum / (1.0f + edge.VisitCount);
                var score = q + u;

                if (score > maxScore)
                {
                    maxScore = score;
                    maxIdx = i;
                }
            }

            if (ENABLE_EXACT_WIN_MCTS)
            {
                if (lossCount + drawCount == edges.Length)
                    this.rootEdgeLabel = (drawCount != 0) ? EdgeLabel.Draw : EdgeLabel.Loss;
            }

            return maxIdx;
        }

        int SelectChildNode(Node parent, ref Edge edgeToParent)
        {
            Debug.Assert(parent.Edges is not null);

            Edge[] edges = parent.Edges;
            var maxIdx = 0;
            var maxScore = float.NegativeInfinity;
            var visitSum = parent.VisitCount;
            var sqrtVisitSum = MathF.Sqrt(visitSum + EPSILON);

            var drawCount = 0;
            var lossCount = 0;
            for (var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];

                if (ENABLE_EXACT_WIN_MCTS)
                {
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
                }

                // calculate PUCB score.
                var q = (float)(edge.RewardSum / (edge.VisitCount + EPSILON));
                var u = PUCT_FACTOR * (float)edge.PolicyProb * sqrtVisitSum / (1.0f + edge.VisitCount);
                var score = q + u;

                if (score > maxScore)
                {
                    maxScore = score;
                    maxIdx = i;
                }
            }

            if (ENABLE_EXACT_WIN_MCTS)
            {
                if (lossCount + drawCount == edges.Length)
                    edgeToParent.Label = (drawCount != 0) ? EdgeLabel.Draw : EdgeLabel.Win;
            }

            return maxIdx;
        }

        int SelectBestChildNode(Node parent)
        {
            Debug.Assert(parent.Edges is not null);

            Edge[] edges = parent.Edges;
            var maxIdx = 0;

            for (var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];

                if (ENABLE_EXACT_WIN_MCTS)
                {
                    if (edge.IsWin)
                        return i;

                    if (edge.IsLoss)
                        continue;
                }

                if (edge.PriorTo(ref edges[maxIdx]))
                    maxIdx = i;
            }

            return maxIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SkipLocalsInit]
        unsafe void SetPolicyProbsAndValues(ref GameInfo game, Edge[] edges)
        {
            PUCTValueType uniformProb;
            if (USE_UNIFORM_POLICY)
                uniformProb = 1 / (PUCTValueType)edges.Length;

            float value;
            PUCTValueType expValueSum = 0;
            var expValues = stackalloc PUCTValueType[edges.Length];
            for (var i = 0; i < edges.Length; i++)
            {
                Debug.Assert(edges[i].Move.Coord != BoardCoordinate.Pass);

                ref var edge = ref edges[i];
                ref Move move = ref game.Moves[i];
                game.Position.GenerateMove(ref move);
                edge.Move = move;
                game.Update(ref edge.Move);
                edge.Value = (Half)(value = 1 - this.valueFunc.Predict(game.FeatureVector));
                expValueSum += expValues[i] = !USE_UNIFORM_POLICY ? FastMath.Exp(value) : uniformProb;
                game.Undo(ref edge.Move, edges);
            }

            if (!USE_UNIFORM_POLICY)
            {
                // softmax
                for (var i = 0; i < edges.Length; i++)
                    edges[i].PolicyProb = (Half)(expValues[i] / expValueSum);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AddVirtualLoss(Node parent, ref Edge childEdge)
        {
            Interlocked.Add(ref parent.VisitCount, VIRTUAL_LOSS);
            Interlocked.Add(ref childEdge.VisitCount, VIRTUAL_LOSS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdateNodeStats(Node parent, ref Edge childEdge, double reward)
        {
            if (VIRTUAL_LOSS != 1)
            {
                Interlocked.Add(ref parent.VisitCount, unchecked(1 - VIRTUAL_LOSS));
                Interlocked.Add(ref childEdge.VisitCount, unchecked(1 - VIRTUAL_LOSS));
            }
            AtomicOperations.Add(ref childEdge.RewardSum, reward);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdatePassNodeStats(Node parent, ref Edge childEdge, double reward)
        {
            Interlocked.Increment(ref parent.VisitCount);
            Interlocked.Increment(ref childEdge.VisitCount);
            AtomicOperations.Add(ref childEdge.RewardSum, reward);
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