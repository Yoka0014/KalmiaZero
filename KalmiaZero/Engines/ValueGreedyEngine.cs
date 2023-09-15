using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;

namespace KalmiaZero.Engines
{
    internal class ValueGreedyEngine : Engine
    {
        PositionFeatureVector? posFeatureVec;
        ValueFunction<double>? valueFunc;

        public ValueGreedyEngine() : base("ValueGreedyEngine", "0.0", "Yoka0014")
        {
            this.Options.Add("weights_file_path", new EngineOption(string.Empty, EngineOptionType.FileName));
            this.Options.Last().Value.ValueChanged += ValueFuncWeightsPathSpecified;
        }

        public override void Quit() { }
        public override void SetMainTime(DiscColor color, int mainTimeMs) { }
        public override void SetByoyomi(DiscColor color, int byoyomiMs) { }
        public override void SetByoyomiStones(DiscColor color, int byoyomiStones) { }
        public override void SetTimeIncrement(DiscColor color, int incMs) { }
        public override void SetLevel(int level) { }
        public override void SetBookContempt(int contempt) { }
        public override void AddCurrentGameToBook() { }

        public override void Go(bool ponder)
        {
            if (posFeatureVec is null || valueFunc is null)
                throw new InvalidOperationException("Specify weights file path of value fucntion.");

            Span<Move> nextMoves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var numNextMoves = this.Position.GetNextMoves(ref nextMoves);
            var pos = this.Position;

            if (numNextMoves == 0)
            {
                pos.Pass();
                numNextMoves = pos.GetNextMoves(ref nextMoves);
                this.posFeatureVec.Pass(nextMoves[..numNextMoves]);
                SendMove(new EngineMove(BoardCoordinate.Pass));
                return;
            }

            var pf = new PositionFeatureVector(this.posFeatureVec);
            Span<Move> legalMoves = stackalloc Move[Constants.MAX_NUM_MOVES];
            Span<double> values = stackalloc double[nextMoves.Length];
            for(var i = 0; i < numNextMoves; i++)
            {
                this.posFeatureVec.CopyTo(pf);
                pos.GenerateMove(ref nextMoves[i]);
                pos.Update(ref nextMoves[i]);
                var numMoves = pos.GetNextMoves(ref legalMoves);
                pf.Update(ref nextMoves[i], legalMoves[..numMoves]);
                values[i] = 1.0 - this.valueFunc.Predict(pf);
                pos.Undo(ref nextMoves[i]);
            }

            var multiPV = new MultiPV();
            for(var i = 0; i < numNextMoves; i++)
            {
                multiPV.Add(new MultiPVItem(new BoardCoordinate[1] { legalMoves[i].Coord })
                {
                    Depth = 0,
                    EvalScore = (double)values[i] * 100.0,
                    EvalScoreType = EvalScoreType.WinRate
                });
            }
            SendMultiPV(multiPV);

            var maxItem = multiPV.MaxBy(x => x.EvalScore);
            Debug.Assert(maxItem is not null);
            SendMove(new EngineMove
            {
                Coord = maxItem.PrincipalVariation[0],
                EvalScore = maxItem.EvalScore,
                EvalScoreType = EvalScoreType.WinRate
            });
        }

        protected override void OnInitializedPosition() => UpdateFeature();
        protected override void OnUpdatedPosition() => UpdateFeature();
        protected override void OnUndidPosition() => UpdateFeature();

        public override void Analyze(int numMoves)
        {
            SendErrorMessage("Analyze is not supported.");
            SendMultiPV(new MultiPV());
        }

        public override bool StopThinking(int timeoutMs) => true;

        protected override bool OnReady() => true;
        protected override void OnStartGame() { }
        protected override void OnEndGame() { }

        void UpdateFeature()
        {
            var pos = this.Position;
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var numMoves = pos.GetNextMoves(ref moves);
            this.posFeatureVec?.Init(ref pos, moves[..numMoves]);
        }

        void ValueFuncWeightsPathSpecified(object? sender, dynamic e)
        {
            try
            {
                this.valueFunc = ValueFunction<Half>.LoadFromFile(e.ToString());
                this.posFeatureVec = new PositionFeatureVector(this.valueFunc.NTuples);
                UpdateFeature();
            }
            catch (Exception ex)
            {
                SendErrorMessage(ex.ToString());
            }
        }
    }
}
