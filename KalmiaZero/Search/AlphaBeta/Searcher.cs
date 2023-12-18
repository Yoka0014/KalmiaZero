global using AlphaBetaEvalType = System.Single;

using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;
using KalmiaZero.Search.MCTS;
using KalmiaZero.Utils;

namespace KalmiaZero.Search.AlphaBeta
{
    public struct SearchResult 
    {
        public BoardCoordinate BestMove { get; }
        public AlphaBetaEvalType EvalScore { get; }

        public SearchResult(BoardCoordinate bestMove, AlphaBetaEvalType evalScore)
        {
            this.BestMove = bestMove;
            this.EvalScore = evalScore;
        }
    }

    public class Searcher
    {
        const AlphaBetaEvalType SCORE_MIN = 0;
        const AlphaBetaEvalType SCORE_MAX = 1;
        const AlphaBetaEvalType NULL_WINDOW_WIDTH = (AlphaBetaEvalType)0.001;

        public int SearchEllapsedMs => this.isSearching ? Environment.TickCount - this.searchStartTime : this.searchEndTime - this.searchStartTime;
        public uint NodeCount { get; private set; }
        public double Nps => this.NodeCount / (this.SearchEllapsedMs * 1.0e-3);
        public bool IsSearching => this.isSearching;

        volatile bool isSearching;
        int searchStartTime = 0;
        int searchEndTime = 0;

        ValueFunction<AlphaBetaEvalType> valueFunc;

        CancellationTokenSource? cts;

        public Searcher(ValueFunction<AlphaBetaEvalType> valueFunc)
        {
            this.valueFunc = valueFunc;
        }

        public void SendStopSearchSignal() => this.cts?.Cancel();

        public async Task<SearchResult> SearchAsync(Position pos, int depth, Action<SearchResult> searchEndCallback)
        {
            this.cts = new CancellationTokenSource();
            this.isSearching = true;
            var ret = await Task.Run(() => 
            {
                var ret = Search(pos, depth);
                searchEndCallback(ret);
                return ret;
            }).ConfigureAwait(false);
            return ret;
        }

        public SearchResult Search(Position pos, int maxDepth)
        {
            var game = new GameInfo(pos, this.valueFunc.NTuples);

            if (game.Moves.Length == 0)
            {
                this.isSearching = false;
                return new SearchResult(BoardCoordinate.Pass, AlphaBetaEvalType.NaN);
            }

            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            this.searchStartTime = Environment.TickCount;
            this.isSearching = true;

            OrderMovesByFastestFirst(ref pos, moves[..numMoves]);

            var bestMove = moves[0].Coord;
            (AlphaBetaEvalType alpha, AlphaBetaEvalType beta) = (SCORE_MIN, SCORE_MAX);
            game.Update(ref moves[0]);
            var score = 1 - Search<False>(ref game, 1 - beta, 1 - alpha, maxDepth, out bool stopFlag);
            game.Undo(ref moves[0], moves[..numMoves]);

            if (score > alpha) 
                alpha = score;

            for (var i = 1; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);

                score = 1 - Search<False>(ref game, 1 - alpha - NULL_WINDOW_WIDTH, 1 - alpha, maxDepth, out stopFlag);  // NWS

                if (score > alpha)
                {
                    alpha = score;
                    score = 1 - Search<False>(ref game, 1 - beta, 1 - alpha, maxDepth, out stopFlag);

                    if (score > alpha)
                    {
                        alpha = score;
                        bestMove = move.Coord;
                    }
                }

                game.Undo(ref move, moves[..numMoves]);
            }

            this.searchEndTime = Environment.TickCount;
            this.isSearching = false;

            return new SearchResult(bestMove, alpha);
        }

        [SkipLocalsInit]
        AlphaBetaEvalType Search<AfterPass>(ref GameInfo game, AlphaBetaEvalType alpha, AlphaBetaEvalType beta, int depthLeft, out bool stopFlag) where AfterPass : struct, IFlag
        {
            stopFlag = false;
            if (game.Moves.Length == 0)  // pass
            {
                if (typeof(AfterPass) == typeof(True))
                {
                    this.NodeCount++;
                    if (this.cts is not null)
                        stopFlag = this.cts.IsCancellationRequested;
                    return DiscDiffToScore(game.Position.DiscDiff);
                }

                game.Pass();

                var ret = 1 - Search<True>(ref game, 1 - beta, 1 - alpha, depthLeft - 1, out stopFlag);

                game.Pass();

                return ret;
            }

            if (depthLeft <= 0)
            {
                this.NodeCount++;
                if (this.cts is not null)
                    stopFlag = this.cts.IsCancellationRequested;
                return this.valueFunc.Predict(game.FeatureVector);
            }

            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            OrderMovesByFastestFirst(ref game.Position, moves[..numMoves]);

            AlphaBetaEvalType maxScore, score;
            game.Update(ref moves[0]);
            maxScore = score = 1 - Search<False>(ref game, 1 - beta, 1 - alpha, depthLeft - 1, out stopFlag);
            game.Undo(ref moves[0], moves[..numMoves]);

            if (score >= beta)
                return score;

            if (score > alpha)
                alpha = score;

            for (var i = 1; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);

                score = 1 - Search<False>(ref game, 1 - alpha - NULL_WINDOW_WIDTH, 1 - alpha, depthLeft - 1, out stopFlag);  // NWS

                if (score >= beta)
                {
                    game.Undo(ref move, moves[..numMoves]);
                    return score;
                }

                if (score > alpha)
                {
                    alpha = score;
                    score = 1 - Search<False>(ref game, 1 - beta, 1 - alpha, depthLeft - 1, out stopFlag);

                    if (score >= beta)
                    {
                        game.Undo(ref move, moves[..numMoves]);
                        return score;
                    }
                }

                game.Undo(ref move, moves[..numMoves]);

                if (score > maxScore)
                {
                    maxScore = score;
                    alpha = Math.Max(alpha, score); 
                }
            }

            return maxScore;
        }

        [SkipLocalsInit]
        static void OrderMovesByFastestFirst(ref Position pos, Span<Move> moves)
        {
            Span<int> scores = stackalloc int[Constants.NUM_SQUARES];
            for(var i = 0; i < moves.Length; i++)
            {
                ref var move = ref moves[i];
                pos.GenerateMove(ref move);
                pos.Update(ref move);
                scores[(int)move.Coord] = pos.GetNumNextMoves();
                pos.Undo(ref move);
            }

            for(var i = 1; i < moves.Length; i++)
            {
                if (scores[(int)moves[i - 1].Coord] < scores[(int)moves[i].Coord])
                {
                    var j = i;
                    var tmp = moves[i];
                    do
                    {
                        moves[j] = moves[j - 1];
                        j--;
                    } while (j > 0 && scores[(int)moves[j - 1].Coord] < scores[(int)tmp.Coord]);
                    moves[j] = tmp;
                }
            }
        }

        static AlphaBetaEvalType DiscDiffToScore(int score)
        {
            if (score == 0)
                return (AlphaBetaEvalType)0.5;
            return (score > 0) ? 1 : 0;
        }
    }
}
