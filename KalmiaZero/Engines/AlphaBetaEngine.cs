using System.Diagnostics;
using System.IO;
using System.Linq;
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
            this.Options.Add("max_depth", new EngineOption(3, 1, Constants.NUM_SQUARES - 4));

            var weightsPath = Path.Combine(PARAMS_DIR, DEFAULT_VALUE_FUNC_WEIGHTS_FILE_NAME);
            var option = new EngineOption(weightsPath, EngineOptionType.FileName);
            option.ValueChanged += OnValueFuncWeightsPathSpecified;
            this.Options.Add("value_func_weights_path", option);
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

        public override void SetLevel(int level) => this.Options["max_depth"].CurrentValueString = level.ToString();

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
        }

        protected override void OnUpdatedPosition()
        {
            StopIfPondering();
            this.searchTask?.Wait();
        }

        protected override void OnUndidPosition()
        {
            StopIfPondering();
            this.searchTask?.Wait();
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
                this.searcher = new Searcher(ValueFunction<AlphaBetaEvalType>.LoadFromFile(valueFuncWeightsPath));
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
                this.searcher = new Searcher(ValueFunction<AlphaBetaEvalType>.LoadFromFile(path));
            }
            catch (InvalidDataException ex)
            {
                SendErrorMessage(ex.Message);
            }
        }

        void GenerateMove()
        {
            Debug.Assert(this.searcher is not null);

            var depth = (int)this.Options["max_depth"].CurrentValue;
            this.searchTask = this.searcher.SearchAsync(this.Position, depth, searchEndCallback);

            void searchEndCallback(SearchResult searchRes)
            {
                var score = searchRes.EvalScore * 100.0f;

                SendThinkInfo(new ThinkInfo(new BoardCoordinate[] { searchRes.BestMove })
                {
                    Depth = depth,
                    EvalScore = score,
                    NodeCount = this.searcher?.NodeCount,
                    Nps = this.searcher?.Nps,
                    EllpasedMs = this.searcher?.SearchEllapsedMs
                });

                SendMove(new EngineMove(searchRes.BestMove, score, EvalScoreType.WinRate, this.searcher?.SearchEllapsedMs));
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
    }
}
