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

namespace KalmiaZero.Search.MCTS
{
    using static PUCTConstantOptions;

    static class PUCTConstantOptions
    {
        // cite: https://doi.org/10.1145/3293475.3293486
        public const bool ENABLE_EXACT_WIN_MCTS = false;

        public const bool USE_UNIFORM_POLICY = false;
        public const float PUCT_FACTOR = 1.0f;
        public const uint VIRTUAL_LOSS = 3;
    }

    public class MoveEvaluation 
    {
        public BoardCoordinate Move { get; init; }
        public double Effort { get; init; }
        public uint PlayoutCount { get; init; }
        public double ExpectedReward { get; init; }
        public GameResult GameResult { get; init; }
        public ReadOnlySpan<BoardCoordinate> PV => this.pv;

        BoardCoordinate[] pv;

        public MoveEvaluation(IEnumerable<BoardCoordinate> pv) => this.pv = pv.ToArray();

        public bool PriorTo(MoveEvaluation moveEval)
        {
            var diff = this.PlayoutCount - moveEval.PlayoutCount;
            if (diff != 0)
                return diff > 0;
            return this.ExpectedReward > moveEval.ExpectedReward;
        }
    }

    public class SearchInfo
    {
        public MoveEvaluation RootEval { get; }
        public ReadOnlySpan<MoveEvaluation> ChildEvals => this.childEvals;
        MoveEvaluation[] childEvals;

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
        OverNodes = 0x0010,
        EarlyStopping = 0x0020,
        Extended = 0x0f00
    }

    public class PUCT
    {
        const float EPSILON = 1.0e-6f;
        static ReadOnlySpan<GameResult> TO_OPPONENT_GAME_RESULT => new GameResult[3] { GameResult.Loss, GameResult.Win, GameResult.Draw };
        static ReadOnlySpan<double> GAME_RESULT_TO_REWARD => new double[3] { 1.0, 0.0, 0.5 };

        public event EventHandler<SearchInfo?>? SearchInfoWasSent;
        public uint NumNodesLimit { get; set; } = 5000_000;
        public int SearchInfoSendIntervalCs { get; set; }
        public bool EnableEarlyStopping { get; set; }
        public int SearchEllapsedMs => this.isSearching ? Environment.TickCount - this.searchStartTime : this.searchEndTime - this.searchStartTime;
        public uint NodeCount => this.nodeCountPerThread.Sum();
        public double Nps => this.NodeCount / (this.SearchEllapsedMs * 1.0e-3);

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

        ValueFunction<Half> valueFunc;

        Node? root;
        Position rootState;
        EdgeLabel rootEdgeLabel;

        int searchStartTime = 0;
        int searchEndTime = 0;
        uint[] nodeCountPerThread;
        uint maxPlayoutCount;
        uint playoutCount;

        CancellationTokenSource? cts;

        public PUCT(ValueFunction<Half> valueFunc) 
        {
            this.valueFunc = valueFunc;
            this.nodeCountPerThread = new uint[numThreads];
        }

        public void SetRootState(ref Position pos)
        {
            this.rootState = pos;
            this.root = new Node();
            InitRootChildNodes();
            rootEdgeLabel = EdgeLabel.NotProved;
            Array.Clear(this.nodeCountPerThread);
        }

        public bool TransitionRootStateToChildState(BoardCoordinate move)
        {
            if (this.root is null || !this.root.IsExpanded || !this.root.ChildNodeWasInitialized)
                return false;

            Debug.Assert(this.root.Edges is not null && this.root.ChildNodes is not null);

            Edge[] edges = this.root.Edges;
            for(var i = 0; i < edges.Length; i++)
            {
                if(move == edges[i].Move.Coord && this.root.ChildNodes[i] is not null)
                {
                    this.rootState.Update(ref edges[i].Move);
                    this.root = this.root.ChildNodes[i];
                    InitRootChildNodes();
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
            var game = new GameInfo(this.rootState, this.valueFunc.NTuples);

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
                childEvals[i] = new MoveEvaluation(GetPV(this.root.ChildNodes?[i]))
                {
                    Move = edge.Move.Coord,
                    Effort = (double)edge.VisitCount / this.root.VisitCount,
                    PlayoutCount = edge.VisitCount,
                    GameResult = edge.IsProved ? (GameResult)(edge.Label ^ EdgeLabel.Proved) : GameResult.NotOver,
                    ExpectedReward = edge.IsProved ? GAME_RESULT_TO_REWARD[(int)(edge.Label ^ EdgeLabel.Proved)] : edge.ExpectedReward
                };
            }

            Array.Sort(childEvals, (x, y) => x.PriorTo(y) ? 1 : -1);

            return new SearchInfo(rootEval, childEvals);
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

            var searchTasks = new Task[this.numThreads];
            for(var i = 0; i < searchTasks.Length; i++)
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
                if(CanStopSearch(timeLimitMs, ref endStatus))
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
                    this.SearchInfoWasSent?.Invoke(this, CollectSearchInfo());

                Thread.Sleep(10);
            }

            this.cts?.Cancel();

            if (enteredExtraSearch)
                endStatus |= SearchEndStatus.Extended;

            foreach (var task in searchTasks)
                task.Wait();

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

            if(this.rootEdgeLabel == EdgeLabel.Proved)
            {
                endStatus = SearchEndStatus.Proved;
                return true;
            }

            if(this.SearchEllapsedMs >= timeLimitMs)
            {
                endStatus = SearchEndStatus.Timeout;
                return true;
            }

            if(Node.ObjectCount >= this.NumNodesLimit)
            {
                endStatus = SearchEndStatus.OverNodes;
                return true;
            }

            if(this.playoutCount >= this.maxPlayoutCount)
            {
                endStatus = SearchEndStatus.Completed;
                return true;
            }

            if(this.EnableEarlyStopping && CanDoEarlyStopping(timeLimitMs))
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
            for(var i = 2; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                if (edge.VisitCount > edges[bestIdx].VisitCount)
                    bestIdx = i;
                else if (edge.VisitCount > edges[secondBestIdx].VisitCount)
                    secondBestIdx = i;
            }

            return (bestIdx, secondBestIdx);
        }

        IEnumerable<BoardCoordinate> GetPV(Node? node)
        {
            if (node is null || node.Edges is null)
                yield break;

            var childIdx = SelectBestChildNode(node);
            yield return node.Edges[childIdx].Move.Coord;

            var childNode = node.ChildNodes?[childIdx];
            foreach (var move in GetPV(childNode))
                yield return move;
        }

        void InitRootChildNodes()
        {
            Debug.Assert(this.root is not null);

            if (!this.root.IsExpanded)
            {
                var gameInfo = new GameInfo(this.rootState, this.valueFunc.NTuples);
                Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
                this.root.Expand(gameInfo.Moves);

                Debug.Assert(this.root.Edges is not null);

                SetPolicyProbsAndValues(ref gameInfo, this.root.Edges);
            }

            Debug.Assert(this.root.Edges is not null);

            if (!this.root.ChildNodeWasInitialized)
                this.root.InitChildNodes();

            Debug.Assert(this.root.ChildNodes is not null);

            for(var i = 0; i < this.root.ChildNodes.Length; i++)
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
            var locked = false;
            try
            {
                Monitor.Enter(node, ref locked);

                if (!node.IsExpanded)   // first visit
                {
                    if (typeof(AfterPass) == typeof(False))
                    {
                        game.Position.GenerateMove(ref edgeToNode.Move);
                        game.Update(ref edgeToNode.Move);
                    }
                    else
                        game.Pass();

                    node.Expand(game.Moves);

                    Debug.Assert(node.Edges is not null);

                    Edge[] edges = node.Edges;
                    if (game.Moves.Length != 0)
                    {
                        SetPolicyProbsAndValues(ref game, edges);
                        this.nodeCountPerThread[threadID]++;
                        ref var childEdge = ref edges[SelectChildNode(node, ref edgeToNode)];
                        AddVirtualLoss(node, ref childEdge);

                        Monitor.Exit(node);
                        locked = false;

                        var value = (double)childEdge.Value;
                        UpdateNodeStats(node, ref childEdge, value);
                        return 1.0 - value;
                    }
                    else    // pass
                    {
                        if (typeof(AfterPass) == typeof(True)) // game over
                        {
                            GameResult res = game.Position.GetGameResult();
                            edges[0].Label = (EdgeLabel)res | EdgeLabel.Proved;
                            edgeToNode.Label = (EdgeLabel)TO_OPPONENT_GAME_RESULT[(int)res] | EdgeLabel.Proved;

                            Monitor.Exit(node);
                            locked = false;

                            double reward = GAME_RESULT_TO_REWARD[(int)res];
                            UpdatePassNodeStats(node, ref edges[0], reward);
                            return 1.0 - reward;
                        }
                        else
                        {
                            node.InitChildNodes();
                            node.CreateChildNode(0);

                            Debug.Assert(node.ChildNodes is not null);

                            Monitor.Exit(node);
                            locked = false;

                            var reward = VisitNode<True>(threadID, ref game, node.ChildNodes[0], ref node.Edges[0]);
                            UpdatePassNodeStats(node, ref edges[0], reward);
                            return 1.0 - reward;
                        }
                    }
                }
                else
                {
                    Debug.Assert(node.Edges is not null);

                    if (typeof(AfterPass) == typeof(False))
                        game.Update(ref edgeToNode.Move);
                    else
                        game.Pass();

                    double reward;
                    Edge[] edges = node.Edges;
                    if (typeof(AfterPass) == typeof(False) && edges[0].Move.Coord == BoardCoordinate.Pass)
                    {
                        if (edges[0].IsProved)
                        {
                            Monitor.Exit(node);
                            locked = false;
                            reward = GAME_RESULT_TO_REWARD[(int)(edges[0].Label ^ EdgeLabel.Proved)];
                            UpdatePassNodeStats(node, ref edges[0], reward);
                            return 1.0 - reward;
                        }
                        else
                        {
                            Monitor.Exit(node);
                            locked = false;

                            Debug.Assert(node.ChildNodes is not null && node.ChildNodes[0] is not null);

                            reward = VisitNode<True>(threadID, ref game, node.ChildNodes[0], ref edges[0]);
                            UpdatePassNodeStats(node, ref edges[0], reward);
                            return 1.0 - reward;
                        }
                    }

                    var childIdx = SelectChildNode(node, ref edgeToNode); 
                    ref var childEdge = ref edges[childIdx];

                    if (childEdge.IsProved)
                    {
                        Monitor.Exit(node);
                        locked = false;

                        reward = GAME_RESULT_TO_REWARD[(int)(childEdge.Label ^ EdgeLabel.Proved)];
                    }
                    else
                    {
                        if (!node.ChildNodeWasInitialized)
                            node.InitChildNodes();

                        Debug.Assert(node.ChildNodes is not null);

                        var childNode = node.ChildNodes[childIdx];
                        childNode ??= node.CreateChildNode(childIdx);

                        Monitor.Exit(node);
                        locked = false;

                        reward = VisitNode<False>(threadID, ref game, childNode, ref childEdge);
                    }

                    UpdateNodeStats(node, ref childEdge, reward);
                    return 1.0 - reward;
                }
            }
            finally
            {
                if (locked)
                    Monitor.Exit(node);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int SelectRootChildNode()
        {
            Debug.Assert(this.root is not null);
            Debug.Assert(this.root.Edges is not null);

            Edge[] edges = this.root.Edges;
            var maxIdx = 0;
            var maxScore = float.NegativeInfinity;
            var sqrtSum = MathF.Sqrt(this.root.VisitCount + EPSILON);

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
                var u = PUCT_FACTOR * (float)edge.PolicyProb * MathF.Sqrt(sqrtSum / (1.0f + edge.VisitCount));
                var score = q + u;

                if(score > maxScore)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int SelectChildNode(Node parent, ref Edge edgeToParent)
        {
            Debug.Assert(parent.Edges is not null);

            Edge[] edges = parent.Edges;
            var maxIdx = 0;
            var maxScore = float.NegativeInfinity;
            var sqrtSum = MathF.Sqrt(parent.VisitCount + EPSILON);

            var drawCount = 0;
            var lossCount = 0;
            for(var i = 0; i < edges.Length; i++)
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
                var u = PUCT_FACTOR * (float)edge.PolicyProb * MathF.Sqrt(sqrtSum / (1.0f + edge.VisitCount));
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
                   edgeToParent.Label = (drawCount != 0) ? EdgeLabel.Draw : EdgeLabel.Loss;
            }

            return maxIdx;
        }

        int SelectBestChildNode(Node parent)
        {
            Debug.Assert(parent.Edges is not null);

            Edge[] edges = parent.Edges;
            var maxIdx = 0;

            for(var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];

                if (edge.IsWin)
                    return i;

                if (edge.IsLoss)
                    continue;

                if (edge.PriorTo(ref edges[maxIdx]))
                    maxIdx = i;
            }

            return maxIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetPolicyProbsAndValues(ref GameInfo game, Edge[] edges)
        {
            Half uniformProb;
            if (USE_UNIFORM_POLICY)
                uniformProb = Half.One / (Half)edges.Length;

            var expValueSum = Half.Zero;
            for(var i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                Move move = game.Moves[i];
                game.Position.GenerateMove(ref move);
                game.Update(ref move);
                edge.Value = Half.One - this.valueFunc.Predict(game.FeatureVector);
                edge.PolicyProb = !USE_UNIFORM_POLICY ? Half.Exp(edge.Value) : uniformProb;
                expValueSum += edge.PolicyProb;
                game.Undo(ref move);
            }

            if (!USE_UNIFORM_POLICY)
            {
                // softmax
                for (var i = 0; i < edges.Length; i++)
                    edges[i].PolicyProb /= expValueSum;
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
            if(VIRTUAL_LOSS != 1)
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
    }
}
