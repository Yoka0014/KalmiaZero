using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;
using KalmiaZero.NTuple;
using KalmiaZero.Evaluation;
using KalmiaZero.Search.MCTS;
using KalmiaZero.Utils;
using System.Diagnostics;

namespace KalmiaZero.Engines
{
    using static PUCTEngineConstantConfig;

    static class PUCTEngineConstantConfig
    {
        public const string PARAMS_DIR = "params/";
        public const string LOG_DIR = "log/";
        public const string DEFAULT_VALUE_FUNC_WEIGHTS_FILE_NAME = "value_func_weights.bin";
    }

    internal class PUCTEngine : Engine
    {
        StreamWriter logger;

        PUCT? tree;
        Task? searchTask;
        Random rand;

        GameTimer[] gameTimer = new GameTimer[2] { new GameTimer(), new GameTimer() };

        public PUCTEngine() : this(string.Empty) { }

        public PUCTEngine(string logFilePath) : base("PUCTEngine", "0.0", "Yoka0014")
        {
            if (string.IsNullOrEmpty(logFilePath))
                this.logger = new StreamWriter(Stream.Null);
            else
                this.logger = new StreamWriter(logFilePath);

            InitOptions();
            this.rand = new Random((int)this.Options["rand_seed"].CurrentValue);
        }

        void InitOptions()
        {
            EngineOption option;

            this.Options["latency_ms"] = new EngineOption(50, 0, int.MaxValue);

            var weightsPath = Path.Combine(PARAMS_DIR, DEFAULT_VALUE_FUNC_WEIGHTS_FILE_NAME);
            option = new EngineOption(weightsPath, EngineOptionType.FileName);
            option.ValueChanged += OnValueFuncWeightsPathSpecified;
            this.Options["value_func_weights_path"] = option;

            option = new EngineOption(Environment.ProcessorCount, 1, Environment.ProcessorCount);
            option.ValueChanged += OnNumThreadsChanged;
            this.Options["num_threads"] = option;

            option = new EngineOption(5000_000, 100, uint.MaxValue);
            option.ValueChanged += OnNumNodesLimitChanged;
            this.Options["num_nodes_limit"] = option;

            this.Options["num_playouts"] = new EngineOption(3200000, 10, uint.MaxValue);
            this.Options["num_stochastic_moves"] = new EngineOption(0, 0, Constants.NUM_SQUARES - 4);
            this.Options["softmax_temperature"] = new EngineOption(1000, 0, long.MaxValue);

            option = new EngineOption(Random.Shared.Next(), 0, int.MaxValue);
            option.ValueChanged += (s, e) => { lock (this.rand) this.rand = new Random(e); };
            this.Options["rand_seed"] = option;

            this.Options["reuse_subtree"] = new EngineOption(false);
            this.Options["enable_extra_search"] = new EngineOption(false);
            this.Options["enable_early_stopping"] = new EngineOption(false);
            this.Options["enable_pondering"] = new EngineOption(false);
            this.Options["show_search_info_interval_cs"] = new EngineOption(50, 1, 6000);

            option = new EngineOption(Path.Combine(LOG_DIR, "puct.log"), EngineOptionType.FileName);
            option.ValueChanged += OnThoughtLogPathChanged;
            this.Options["thought_log_path"] = option;

            this.Options["show_log"] = new EngineOption(false);
        }

        public override void Quit() 
        {
            if (this.tree is not null && this.tree.IsSearching)
                this.tree.SendStopSearchSignal();
            this.logger.Close();
        }

        public override void SetMainTime(DiscColor color, int mainTimeMs)
        {
            GameTimer timer = this.gameTimer[(int)color];

            if (CheckIfTicking(timer))
                return;

            if (mainTimeMs >= timer.MainTimeMs)
                timer.MainTimeMs = mainTimeMs;
            else
                timer.MainTimeLeftMs = mainTimeMs;
        }

        public override void SetByoyomi(DiscColor color, int byoyomiMs)
        {
            GameTimer timer = this.gameTimer[(int)color];

            if (CheckIfTicking(timer))
                return;

            timer.ByoyomiMs = byoyomiMs;
        }

        public override void SetByoyomiStones(DiscColor color, int byoyomiStones) 
        {
            GameTimer timer = this.gameTimer[(int)color];

            if (CheckIfTicking(timer))
                return;

            if (byoyomiStones >= timer.ByoyomiStones)
                timer.ByoyomiStones = byoyomiStones;

        }

        public override void SetTimeIncrement(DiscColor color, int incMs) 
        {
            GameTimer timer = this.gameTimer[(int)color];

            if (CheckIfTicking(timer))
                return;

            timer.IncrementMs = incMs;
        }

        public override void SetLevel(int level) => this.Options["num_playouts"].CurrentValueString = (8U << level).ToString();

        public override void SetBookContempt(int contempt) { }
        public override void AddCurrentGameToBook() { }

        public override void Go(bool ponder)
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            StopIfPondering();
            this.searchTask?.Wait();

            if (this.Position.CanPass)
            {
                SendMove(new EngineMove(BoardCoordinate.Pass));
                return;
            }

            var moves = this.Position.EnumerateNextMoves();
            if (moves.Count() == 1)
            {
                SendMove(new EngineMove(moves.First()));
                return;
            }

            GenerateMove();
        }

        protected override void OnInitializedPosition()
        {
            if(this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            StopIfPondering();
            this.searchTask?.Wait();

            var pos = this.Position;
            this.tree.SetRootState(ref pos);
            WriteLog("Tree was cleared.\n");
        }

        protected override void OnUpdatedPosition()
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            StopIfPondering();
            this.searchTask?.Wait();

            if (!this.Options["reuse_subtree"].CurrentValue || !this.tree.TransitionRootStateToChildState(this.MoveHistory[^1].Coord))
            {
                var pos = this.Position;
                this.tree.SetRootState(ref pos);
                WriteLog("Tree was cleared.\n");
            }
            else
                WriteLog("Tree was updated.\n");
        }

        protected override void OnUndidPosition()
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            StopIfPondering();
            this.searchTask?.Wait();

            var pos = this.Position;
            this.tree.SetRootState(ref pos);
            WriteLog("Undo.\n");
            WriteLog("Tree was cleared.\n");
        }

        public override void Analyze(int numMoves)
        {
            // TODO: 探索のデバッグ後に実装.
        }

        public override bool StopThinking(int timeoutMs)
        {
            if (this.tree is null || this.searchTask is null)
                return true;

            WriteLog("Recieved stop signal.\n");

            this.tree.SendStopSearchSignal();
            return this.searchTask.Wait(timeoutMs);
        }

        protected override bool OnReady()
        {
            string valueFuncWeightsPath = this.Options["value_func_weights_path"].CurrentValue;
            if (!File.Exists(valueFuncWeightsPath))
            {
                SendErrorMessage($"Cannot find value func weights file: \"{valueFuncWeightsPath}\".");
                return false;
            }

            var logPath = this.Options["thought_log_path"].CurrentValueString;
            if (!string.IsNullOrEmpty(logPath))
                this.logger = new StreamWriter(logPath);
            else
                this.logger = new StreamWriter(Stream.Null);

            try
            {
                this.tree = new PUCT(ValueFunction<PUCTValueType>.LoadFromFile(valueFuncWeightsPath));
                var pos = this.Position;
                this.tree.SetRootState(ref pos);
            }
            catch (InvalidDataException ex)
            {
                SendErrorMessage(ex.Message);
                return false;
            }

            return true;
        }

        protected override void OnStartGame() { }

        protected override void OnEndGame() { }

        void StopIfPondering()
        {
            if (this.tree is not null && this.searchTask is not null && !this.searchTask.IsCompleted)
            {
                this.tree.SendStopSearchSignal();
                WriteLog("stop pondering.\n\n");

                SearchInfo? searchInfo;
                if ((searchInfo = this.tree.CollectSearchInfo()) is not null)
                    WriteLog(SearchInfoToString(searchInfo));
            }
        }

        bool CheckIfTicking(GameTimer timer)
        {
            if (timer.IsTicking)
            {
                SendErrorMessage("Cannot set time while timer is ticking.");
                return true;
            }
            return false;
        }

        void GenerateMove()
        {
            Debug.Assert(this.tree is not null);

            WriteLog("Start search.\n");

            this.tree.SearchInfoWasSent += (s, e) => SendSearchInfo(e);
            this.tree.SearchInfoSendIntervalCs = (int)this.Options["show_search_info_interval_cs"].CurrentValue;
            this.tree.EnableEarlyStopping = this.Options["enable_early_stopping"].CurrentValue;
            uint numPlayouts = (uint)this.Options["num_playouts"].CurrentValue;
            (var mainTimeMs, var extraTimeMs) = AllocateTime(this.Position.SideToMove);
            //this.searchTask = this.tree.SearchAsync(numPlayouts, mainTimeMs / 10, extraTimeMs / 10, searchEndCallback);

            //void searchEndCallback(SearchEndStatus status)
            //{
            //    WriteLog($"{status}.\n");
            //    WriteLog($"End search.\n");

            //    var searchInfo = this.tree.CollectSearchInfo();

            //    if (searchInfo is null)
            //        return;

            //    WriteLog(SearchInfoToString(searchInfo));
            //    SendMove(SelectMove(searchInfo));
            //}

            this.tree.Search(numPlayouts, mainTimeMs / 10, extraTimeMs / 10);

            WriteLog($"End search.\n");

            var searchInfo = this.tree.CollectSearchInfo();

            if (searchInfo is null)
                return;

            WriteLog(SearchInfoToString(searchInfo));
            SendMove(SelectMove(searchInfo));
        }

        EngineMove SelectMove(SearchInfo searchInfo)
        {
            var childEvals = searchInfo.ChildEvals;
            var selectedIdx = 0;
            var moveNum = (Constants.NUM_SQUARES - 4) - this.Position.EmptySquareCount + 1;
            if(moveNum <= this.Options["num_stochastic_moves"].CurrentValue)
            {
                var tInv = 1.0 / (this.Options["softmax_temperature"].CurrentValue * 1.0e-3);
                var indices = Enumerable.Range(0, childEvals.Length).ToArray();
                var expPlayoutCount = new double[childEvals.Length];
                var expPlayoutCountSum = 0.0;

                for(var i = 0; i < indices.Length; i++)
                {
                    indices[i] = i;
                    expPlayoutCountSum += expPlayoutCount[i] = Math.Pow(childEvals[i].PlayoutCount, tInv);
                }

                double arrow;
                lock (this.rand)
                {
                    arrow = rand.NextDouble() * expPlayoutCountSum;
                    this.rand.Shuffle(indices);
                }

                var sum = 0.0;
                foreach (var i in indices)
                    if ((sum += expPlayoutCount[selectedIdx = i]) >= arrow)
                        break;
            }

            var selected = childEvals[selectedIdx];
            return new EngineMove(selected.Move, selected.ExpectedReward * 100.0, EvalScoreType.WinRate, this.tree?.SearchEllapsedMs);
        }

        (int mainTimeMs, int extraTimeMs) AllocateTime(DiscColor color)
        {
            // TODO: 時間管理は探索のデバッグが済んでから実装.
            return (int.MaxValue, this.Options["enable_extra_search"].CurrentValue ? int.MaxValue : 0);
        }

        void SendSearchInfo(SearchInfo? searchInfo)
        {
            if (searchInfo is null)
                return;

            SendThinkInfo(CreateThinkInfo(searchInfo));
            SendMultiPV(CreateMultiPV(searchInfo));

            WriteLog(SearchInfoToString(searchInfo));
            WriteLog("\n");
        }

        ThinkInfo CreateThinkInfo(SearchInfo searchInfo)
        {
            Debug.Assert(this.tree is not null);

            return new ThinkInfo(searchInfo.RootEval.PV.ToArray())
            {
                EllpasedMs = this.tree.SearchEllapsedMs,
                NodeCount = this.tree.NodeCount,
                Nps = this.tree.Nps,
                Depth = searchInfo.ChildEvals[0].PV.Length,
                EvalScore = searchInfo.ChildEvals[0].ExpectedReward * 100.0,
            };
        }

        static MultiPV CreateMultiPV(SearchInfo searchInfo)
        {
            var multiPV = new MultiPV(searchInfo.ChildEvals.Length);
            foreach (var childEval in searchInfo.ChildEvals)
            {
                if (double.IsNaN(childEval.ExpectedReward))
                    continue;

                multiPV.Add(new MultiPVItem(childEval.PV.ToArray())
                {
                    NodeCount = childEval.PlayoutCount,
                    EvalScore = childEval.ExpectedReward * 100.0,
                    EvalScoreType = EvalScoreType.WinRate
                });
            }
            return multiPV;
        }

        string SearchInfoToString(SearchInfo searchInfo)
        {
            Debug.Assert(this.tree is not null);

            var sb = new StringBuilder();
            sb.Append("ellapsed=").Append(this.tree.SearchEllapsedMs).Append("[ms] ");
            sb.Append(this.tree.NodeCount).Append("[nodes] ");
            sb.Append(this.tree.Nps).Append("[nps] ");
            sb.Append(searchInfo.RootEval.PlayoutCount).Append("[po] ");
            sb.Append("winning_rate=").Append((searchInfo.RootEval.ExpectedReward * 100.0).ToString("F2")).Append("%\n");
            sb.Append("|move|win_rate|effort|simulation|depth|pv\n");

            foreach (MoveEvaluation childEval in searchInfo.ChildEvals)
            {
                sb.Append("| ").Append(childEval.Move).Append(' ');
                sb.Append('|').Append((childEval.ExpectedReward * 100.0).ToString("F2").PadLeft(7));
                sb.Append('|').Append((childEval.Effort * 100.0).ToString("F2").PadLeft(5));
                sb.Append('|').Append(childEval.PlayoutCount.ToString().PadLeft(10));
                sb.Append('|').Append(childEval.PV.Length.ToString().PadLeft(5));
                sb.Append('|');
                foreach (var move in childEval.PV)
                    sb.Append(move).Append(' ');
                sb.Append('\n');
            }

            return sb.ToString();
        }

        void WriteLog(string msg)
        {
            this.logger.Write(msg);
            if (this.Options["show_log"].CurrentValue)
                Console.Write(msg);
            this.logger.Flush();
        }

        void OnValueFuncWeightsPathSpecified(object? sender, dynamic e)
        {
            if (this.State == EngineState.NotReady)
            {
                SendErrorMessage($"Cannot set the value of \"value_func_weights_path\" because engine is not ready state.");
                return;
            }

            string path = e;

            if (!File.Exists(path))
            {
                SendErrorMessage($"Cannot find a weights file of value function at \"{path}\".");
                return;
            }

            try
            {
                this.tree = new PUCT(ValueFunction<PUCTValueType>.LoadFromFile(path));
                var pos = this.Position;
                this.tree.SetRootState(ref pos);
            }
            catch (InvalidDataException ex)
            {
                SendErrorMessage(ex.Message);
            }
        }


        void OnNumThreadsChanged(object? sender, dynamic e)
        {
            if(this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            if (this.tree.IsSearching)
            {
                SendErrorMessage("Cannot set the number of threads while searching.");
                return;
            }

            this.tree.NumThreads = (int)e;
        }

        void OnNumNodesLimitChanged(object? sender, dynamic e)
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            this.tree.NumNodesLimit = e;
        }

        void OnThoughtLogPathChanged(object? sender, dynamic e)
        {
            lock (this.logger)
            {
                this.logger.Close();
                this.logger = new StreamWriter(this.Options["thought_log_path"].CurrentValue);
            }
        }
    }
}
