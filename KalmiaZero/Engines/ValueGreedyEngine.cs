using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using KalmiaZero.Evaluate;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;

namespace KalmiaZero.Engines
{
    using MultiPV = List<MultiPVItem>;

    internal class ValueGreedyEngine : Engine
    {
        PositionFeature? posFeatures;
        ValueFunction<Half>? valueFunc;

        public ValueGreedyEngine() : base("ValueGreedyEngine", "0.0", "Yoka346")
        {
            this.Options.Add("WeightsFilePath", new EngineOption(string.Empty, EngineOptionType.FileName));
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
            if (posFeatures is null || valueFunc is null)
                throw new InvalidOperationException("Specify value function's weights file path.");

            Span<Move> nextMoves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var numNextmoves = this.Position.GetNextMoves(ref nextMoves);

            if(numNextmoves == 0)
            {
                SendMove(new EngineMove(BoardCoordinate.Pass));
                return;
            }

            var pos = this.Position;
            var pf = new PositionFeature(this.posFeatures);
            Span<Move> legalMoves = stackalloc Move[Constants.MAX_NUM_MOVES];
            Span<Half> values = stackalloc Half[nextMoves.Length];
            for(var i = 0; i < values.Length; i++)
            {
                this.posFeatures.CopyTo(pf);
                pos.GenerateMove(ref nextMoves[i]);
                pos.Update(ref nextMoves[i]);
                var numMoves = pos.GetNextMoves(ref legalMoves);
                pf.Update(ref nextMoves[i], legalMoves[..numMoves]);
                values[i] = this.valueFunc.Predict(pf);
            }

            var multiPV = new MultiPV();
            for(var i = 0; i < nextMoves.Length; i++)
            {
                multiPV.Add(new MultiPVItem
                {
                    Depth = 0,
                    EvalScore = (double)values[i] * 100.0,
                    EvalScoreType = EvalScoreType.WinRate
                });
                multiPV[^1].PrincipalVariation.Add(nextMoves[i].Coord);
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

        public override void Analyze(int numMoves)
        {
            SendErrorMessage("Analyze is not supported.");
            SendMultiPV(new MultiPV());
        }

        public override bool StopThinking(int timeoutMs) => true;

        protected override bool OnReady() => true;
        protected override void OnStartGame() { }
        protected override void OnEndGame() { }
        protected override void OnInitializedPosition() { }
        protected override void OnClearedPosition() { }
        protected override void OnUpdatedPosition() { }
        protected override void OnUndidPosition() { }

        void ValueFuncWeightsPathSpecified(object? sender, dynamic e)
        {
            try
            {
                this.valueFunc = ValueFunction<Half>.LoadFromFile(e.ToString());
                this.posFeatures = new PositionFeature(this.valueFunc.NTuples.ToArray());
            }
            catch (Exception ex)
            {
                SendErrorMessage(ex.ToString());
            }
        }
    }
}
