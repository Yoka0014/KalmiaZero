using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.Engines
{
    using MultiPV = List<MultiPVItem>;

    public class MCEngine : Engine
    {
        Task? simulationThread;
        CancellationTokenSource? cts;

        public MCEngine() : base("MonteCarloEngine", "0.0", "Yoka346")
        {
            this.Options.Add("NumPlayouts", new EngineOption(100, 1, long.MaxValue));
        }

        public override void Quit() { }
        public override void SetMainTime(DiscColor color, int mainTimeMs) { }
        public override void SetByoyomi(DiscColor color, int byoyomiMs) { }
        public override void SetByoyomiStones(DiscColor color, int byoyomiStones) { }
        public override void SetTimeIncrement(DiscColor color, int incMs) { }

        public override void SetLevel(int level) 
            => this.Options["NumPlayouts"].CurrentValueString = (level * 100).ToString();

        public override void SetBookContempt(int contempt) { }
        public override void AddCurrentGameToBook() { }

        public override void Go(bool ponder)
        {
            var nextMoves = this.Position.EnumerateNextMoves().ToArray();
            if (nextMoves.Length == 0)
            {
                SendMove(new EngineMove(BoardCoordinate.Pass));
                return;
            }

            this.cts = new();
            this.simulationThread = Task.Run(() =>
            {
                long numPlayouts = this.Options["NumPlayouts"].CurrentValue;
                var count = 1;
                var values = new double[nextMoves.Length];
                Parallel.For(0, nextMoves.Length, i =>
                {
                    if (this.cts.Token.IsCancellationRequested)
                        return; 

                    var pos = this.Position;
                    pos.Update(nextMoves[i]);
                    values[i] = 1.0 - Playout(pos);
                    for (var j = 0; j < numPlayouts - 1; j++)
                    {
                        var v = 1.0 - Playout(pos);
                        values[i] += (1.0 / (++count)) * (v - values[i]);
                    }
                });

                var multiPV = new MultiPV();
                for (var i = 0; i < nextMoves.Length; i++)
                {
                    multiPV.Add(new MultiPVItem
                    {
                        Depth = 0,
                        EvalScore = values[i] * 100.0,
                        EvalScoreType = EvalScoreType.WinRate
                    });
                    multiPV.Last().PrincipalVariation.Add(nextMoves[i]);
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
            });
            this.simulationThread.ConfigureAwait(false);
        }

        public override void Analyze(int numMoves)
        {
            SendErrorMessage("Analyze is not supported.");
            SendMultiPV(new MultiPV());
        }

        public override bool StopThinking(int timeoutMs)
        {
            if (this.simulationThread is null || this.simulationThread.IsCompleted)
                return true;

            this.cts?.Cancel();
            return this.simulationThread.Wait(timeoutMs);
        }

        protected override bool OnReady() => true;
        protected override void OnStartGame() { }
        protected override void OnEndGame() { }
        protected override void OnInitializedPosition() { }
        protected override void OnClearedPosition() { }
        protected override void OnUpdatedPosition() { }
        protected override void OnUndidPosition() { }

        static double Playout(Position pos)
        {
            var color = pos.SideToMove;
            var passCount = 0;
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            while(passCount < 2)
            {
                var numMoves = pos.GetNextMoves(ref moves);
                if(numMoves == 0)
                {
                    pos.Pass();
                    passCount++;
                    continue;
                }

                ref var move = ref moves[Random.Shared.Next(numMoves)];
                pos.CreateMove(ref move);
                pos.Update(ref move);
            }

            var score = pos.GetScore(color);
            if (score == 0)
                return 0.5;
            return (score > 0) ? 1.0 : 0.0;
        }
    }
}
