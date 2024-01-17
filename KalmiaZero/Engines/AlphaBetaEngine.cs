using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;
using KalmiaZero.Search.AlphaBeta;
using KalmiaZero.Utils;

namespace KalmiaZero.Engines
{
    using static AlphaBetaEngineConstantConfig;

    static class AlphaBetaEngineConstantConfig
    {
        public const string PARAMS_DIR = "params/";
        public const string LOG_DIR = "log/";
        public const string DEFAULT_VALUE_FUNC_WEIGHTS_FILE_NAME = "value_func_weights.bin";
    }

    public class AlphaBetaEngine : Engine
    {
        Searcher? searcher;
        Task? searchTask;

        GameTimer[] gameTimer = new GameTimer[2] { new GameTimer(), new GameTimer() };

        public AlphaBetaEngine() : base("AlphaBetaEngine", "0.0", "Yoka0014")
        {
            this.Options.Add("mid_depth", new EngineOption(3, 1, Constants.NUM_SQUARES - 4));
            this.Options.Add("end_depth", new EngineOption(6, 1, Constants.NUM_SQUARES - 4));

            var option = new EngineOption(1024, 64, long.MaxValue);
            option.ValueChanged += (_, e) => this.searcher?.SetTranspositionTableSize((long)(e * 1024L * 1024L));
            this.Options.Add("tt_size_mib", option);

            var weightsPath = Path.Combine(PARAMS_DIR, DEFAULT_VALUE_FUNC_WEIGHTS_FILE_NAME);
            option = new EngineOption(weightsPath, EngineOptionType.FileName);
            option.ValueChanged += OnValueFuncWeightsPathSpecified;
            this.Options.Add("value_func_weights_path", option);

            this.Options["show_search_info_interval_cs"] = new EngineOption(10, 0, 6000); 
        }

        public override void Quit()
        {
            if (this.searcher is not null && this.searcher.IsSearching)
                this.searcher.SendStopSearchSignal();
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

        public override void SetLevel(int level)
        {
            this.Options["mid_depth"].CurrentValueString = level.ToString();
            this.Options["end_depth"].CurrentValueString = (level * 2).ToString();
        }

        public override void SetBookContempt(int contempt) { }
        public override void AddCurrentGameToBook() { }

        public override void Go(bool ponder)
        {
            if (this.searcher is null)
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
            if(moves.Count() == 1)
            {
                SendMove(new EngineMove(moves.First()));
                return;
            }

            GenerateMove();
        }

        protected override void OnInitializedPosition()
        {
            StopIfPondering();
            this.searchTask?.Wait();
            this.searcher?.SetRootPos(this.Position);
        }

        protected override void OnUpdatedPosition()
        {
            StopIfPondering();
            this.searchTask?.Wait();
            this.searcher?.UpdateRootGame(this.MoveHistory[^1].Coord);
        }

        protected override void OnUndidPosition()
        {
            StopIfPondering();
            this.searchTask?.Wait();
            this.searcher?.SetRootPos(this.Position);
        }

        public override void Analyze(int numMoves) => SendErrorMessage("Analysis is not supported.");

        public override bool StopThinking(int timeoutMs)
        {
            if (this.searcher is null || this.searchTask is null)
                return true;

            this.searcher.SendStopSearchSignal();
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

            try
            {
                long ttSize = this.Options["tt_size_mib"].CurrentValue * 1024L * 1024L;
                this.searcher = new Searcher(ValueFunction<MiniMaxType>.LoadFromFile(valueFuncWeightsPath), ttSize);
                this.searcher.SetRootPos(this.Position);
                this.searcher.PVWasUpdated += (s, e) => SendThinkInfo(SearchResultToThinkInfo(ref e));
                this.searcher.PVWasUpdated += (s, e) => SendMultiPV(new List<MultiPVItem> { SearchResultToMultiPVItem(ref e) });
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

        void OnValueFuncWeightsPathSpecified(object? sender, dynamic e)
        {
            string path = e;

            if (!File.Exists(path))
            {
                SendErrorMessage($"Cannot find a weights file of value function at \"{path}\".");
                return;
            }

            if (this.State == EngineState.NotReady)
                return;

            try
            {
                long ttSize = this.Options["tt_size_mib"].CurrentValue * 1024L * 1024L;
                this.searcher = new Searcher(ValueFunction<MiniMaxType>.LoadFromFile(path), ttSize);
                this.searcher.SetRootPos(this.Position);
                this.searcher.PVWasUpdated += (s, e) => SendThinkInfo(SearchResultToThinkInfo(ref e));
                this.searcher.PVWasUpdated += (s, e) => SendMultiPV(new List<MultiPVItem> { SearchResultToMultiPVItem(ref e) });
            }
            catch (InvalidDataException ex)
            {
                SendErrorMessage(ex.Message);
            }
        }

        void GenerateMove()
        {
            Debug.Assert(this.searcher is not null);

            int depth;
            if (this.Position.EmptySquareCount > this.Options["end_depth"].CurrentValue)
                depth = (int)this.Options["mid_depth"].CurrentValue;
            else
                depth = (int)this.Options["end_depth"].CurrentValue;

            this.searcher.PVNotificationIntervalMs = (int)this.Options["show_search_info_interval_cs"].CurrentValue * 10;
            this.searchTask = this.searcher.SearchAsync(depth, searchEndCallback);

            void searchEndCallback(SearchResult searchRes)
            {
                SendThinkInfo(SearchResultToThinkInfo(ref searchRes));
                SendMultiPV(new List<MultiPVItem> { SearchResultToMultiPVItem(ref searchRes) });
                SendMove(new EngineMove(searchRes.BestMove, searchRes.EvalScore * 100.0, EvalScoreType.WinRate, searchRes.EllpasedMs));
            }
        }

        void StopIfPondering()
        {
            if (this.searcher is not null && this.searchTask is not null && !this.searchTask.IsCompleted)
                this.searcher.SendStopSearchSignal();
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

        ThinkInfo SearchResultToThinkInfo(ref SearchResult res)
        {
            return new ThinkInfo(res.PV)
            {
                Depth = res.Depth,
                NodeCount = res.NodeCount,
                Nps = (double)res.NodeCount / res.EllpasedMs,
                EvalScore = res.EvalScore * 100.0,
                EllpasedMs = res.EllpasedMs
            };
        }

        MultiPVItem SearchResultToMultiPVItem(ref SearchResult res)
        {
            return new MultiPVItem(res.PV)
            {
                Depth = res.Depth,
                NodeCount = res.NodeCount,
                EvalScore = res.EvalScore * 100.0,
                EvalScoreType = EvalScoreType.WinRate,
            };
        }
    }
}
