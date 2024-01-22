using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;
using KalmiaZero.Evaluation;
using KalmiaZero.Search.MCTS;
using KalmiaZero.Search.MCTS.Training;

using static KalmiaZero.Engines.PUCTEngineConstantConfig;

namespace KalmiaZero.Engines
{
    public class FastPUCTEngine : Engine
    {
        const int NUM_SIMULATIONS = 10000;

        FastPUCT? tree;
        Random rand;

        public FastPUCTEngine() : base("FastPUCTEngine", "0.0", "Yoka0014")
        {
            InitOptions();
            this.rand = new Random((int)this.Options["rand_seed"].CurrentValue);
        }

        void InitOptions()
        {
            EngineOption option;

            var weightsPath = Path.Combine(PARAMS_DIR, DEFAULT_VALUE_FUNC_WEIGHTS_FILE_NAME);
            option = new EngineOption(weightsPath, EngineOptionType.FileName);
            option.ValueChanged += OnValueFuncWeightsPathSpecified;
            this.Options["value_func_weights_path"] = option;

            this.Options["num_stochastic_moves"] = new EngineOption(0, 0, Constants.NUM_SQUARES - 4);

            option = new EngineOption(Random.Shared.Next(), 0, int.MaxValue);
            option.ValueChanged += (s, e) => { lock (this.rand) this.rand = new Random(e); };
            this.Options["rand_seed"] = option;
        }

        public override void Quit() { }

        public override void Go(bool ponder)
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

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

            this.tree.Search();
            Move move;
            if (60 - this.Position.EmptySquareCount < this.Options["num_stochastic_moves"].CurrentValue)
                move = this.tree.SelectMoveWithVisitCountDist(this.rand);
            else
                move = this.tree.SelectMaxVisitMove();

            SendMove(new EngineMove(move.Coord));
        }

        protected override void OnInitializedPosition()
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            var pos = this.Position;
            this.tree.SetRootState(ref pos);
        }

        protected override void OnUpdatedPosition()
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            var move = this.MoveHistory[^1].Coord;

            if (move != BoardCoordinate.Pass)
                this.tree.UpdateRootState(move);
            else
                this.tree.PassRootState();
        }

        protected override void OnUndidPosition()
        {
            if (this.tree is null)
            {
                SendErrorMessage("Specifiy weights file path of value function.");
                return;
            }

            var pos = this.Position;
            this.tree.SetRootState(ref pos);
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
                InitTree(ValueFunction<PUCTValueType>.LoadFromFile(valueFuncWeightsPath));
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
                InitTree(ValueFunction<PUCTValueType>.LoadFromFile(path));
            }
            catch (InvalidDataException ex)
            {
                SendErrorMessage(ex.Message);
            }
        }

        void InitTree(ValueFunction<PUCTValueType> valueFunc)
        {
            this.tree = new FastPUCT(valueFunc, NUM_SIMULATIONS);
            var pos = this.Position;
            this.tree.SetRootState(ref pos);
        }

        public override void SetMainTime(DiscColor color, int mainTimeMs)
        {
        }

        public override void SetByoyomi(DiscColor color, int byoyomiMs)
        {
        }

        public override void SetByoyomiStones(DiscColor color, int byoyomiStones)
        {
        }

        public override void SetTimeIncrement(DiscColor color, int incMs)
        {
        }

        public override void SetLevel(int level)
        {
        }

        public override void SetBookContempt(int contempt)
        {
        }

        public override void AddCurrentGameToBook()
        {
        }

        public override void Analyze(int numMoves)
        {
        }

        public override bool StopThinking(int timeoutMs) => true;
    }
}
