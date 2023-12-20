global using AlphaBetaEvalType = System.Single;

using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;
using KalmiaZero.Search.MCTS;
using KalmiaZero.Utils;

namespace KalmiaZero.Search.AlphaBeta
{
    public readonly struct SearchResult 
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
        const AlphaBetaEvalType TT_HIT_BONUS = 50;
        const int ID_DEPTH_MIN = 4;
        const int SHALLOW_SEARCH_DEPTH = 3;
        const int END_GAME_DEPTH = 15;

        public int SearchEllapsedMs => this.isSearching ? Environment.TickCount - this.searchStartTime : this.searchEndTime - this.searchStartTime;
        public uint NodeCount { get; private set; }
        public double Nps => this.NodeCount / (this.SearchEllapsedMs * 1.0e-3);
        public bool IsSearching => this.isSearching;

        volatile bool isSearching;
        int searchStartTime = 0;
        int searchEndTime = 0;

        Position rootPos;
        readonly TranspositionTable tt;
        readonly ValueFunction<AlphaBetaEvalType> valueFunc;

        CancellationTokenSource? cts;

        public Searcher(ValueFunction<AlphaBetaEvalType> valueFunc, long ttSize)
        {
            this.valueFunc = valueFunc;
            this.tt = new TranspositionTable(ttSize);
        }

        public void SendStopSearchSignal() => this.cts?.Cancel();

        public void SetTranspositionTableSize(long size) => this.tt.Resize(size);

        public void SetRootPos(Position pos)
        {
            this.rootPos = pos;
            this.tt.Clear();
        }

        public bool UpdateRootPos(BoardCoordinate move)
        {
            if (!this.rootPos.Update(move))
                return false;

            this.tt.IncrementGeneration();
            return true;
        }

        public async Task<SearchResult> SearchAsync(int depth, Action<SearchResult> searchEndCallback)
        {
            this.cts = new CancellationTokenSource();
            this.isSearching = true;
            var ret = await Task.Run(() => 
            {
                var ret = Search(depth);
                searchEndCallback(ret);
                return ret;
            }).ConfigureAwait(false);
            return ret;
        }

        public SearchResult Search(int maxDepth)
        {
            if (maxDepth <= ID_DEPTH_MIN)
                return Search(maxDepth, BoardCoordinate.Null, out _);

            var searchResult = Search(maxDepth, BoardCoordinate.Null, out _);
            for(var depth = ID_DEPTH_MIN + 1; depth <= maxDepth; depth++)
            {
                Search(maxDepth, searchResult.BestMove, out bool suspended);
                if (suspended)
                    return searchResult;
            }

            return searchResult;
        }

        SearchResult Search(int depth, BoardCoordinate prevBestMove, out bool suspended)
        {
            suspended = false;

            var game = new GameInfo(this.rootPos, this.valueFunc.NTuples);

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

            InitMoves(ref this.rootPos, moves);

            var offset = PlaceBestMoveAtTop(ref this.rootPos, prevBestMove, moves) ? 1 : 0;
            OrderMovesWithTT(ref game, moves[offset..numMoves]);

            var bestMove = moves[0].Coord;
            (AlphaBetaEvalType alpha, AlphaBetaEvalType beta) = (SCORE_MIN, SCORE_MAX);
            game.Update(ref moves[0]);
            var score = 1 - SearchWithTT<False>(ref game, 1 - beta, 1 - alpha, depth, out bool stopFlag);

            if (stopFlag)
            {
                suspended = true;
                return new SearchResult(bestMove, score);
            }

            game.Undo(ref moves[0], moves[..numMoves]);

            if (score > alpha) 
                alpha = score;

            for (var i = 1; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);

                score = 1 - SearchWithTT<False>(ref game, 1 - alpha - NULL_WINDOW_WIDTH, 1 - alpha, depth, out stopFlag);  // NWS

                if (stopFlag)
                {
                    suspended = true;
                    break;
                }

                if (score > alpha)
                {
                    alpha = score;
                    score = 1 - SearchWithTT<False>(ref game, 1 - beta, 1 - alpha, depth, out stopFlag);

                    if (stopFlag)
                    {
                        suspended = true;
                        break;
                    }

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
        AlphaBetaEvalType SearchWithTT<AfterPass>(ref GameInfo game, AlphaBetaEvalType alpha, AlphaBetaEvalType beta, int depth, out bool stopFlag) where AfterPass : struct, IFlag
        {
            stopFlag = false;

            ref var entry = ref this.tt.GetEntry(ref game.Position, out bool hit);
            if (hit && entry.Generation == this.tt.Generation)
            {
                var upper = (AlphaBetaEvalType)entry.Upper;
                var lower = (AlphaBetaEvalType)entry.Lower;
                if (alpha >= upper)
                    return upper;
                if (beta <= lower)
                    return lower;
                if (lower == upper)
                    return upper;

                alpha = Math.Max(alpha, lower);
                beta = Math.Min(beta, upper);
            }

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

                AlphaBetaEvalType ret;

                ret = 1 - SearchWithTT<True>(ref game, 1 - beta, 1 - alpha, depth, out stopFlag);

                if (stopFlag)
                    return alpha;

                game.Pass();

                return ret;
            }

            if (depth == 0)
            {
                this.NodeCount++;
                if (this.cts is not null)
                    stopFlag = this.cts.IsCancellationRequested;
                return this.valueFunc.Predict(game.FeatureVector);
            }

            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            InitMoves(ref game.Position, moves);

            var offset = (hit && PlaceBestMoveAtTop(ref game.Position, entry.Move, moves)) ? 1 : 0;
            OrderMovesWithTT(ref game, moves[offset..numMoves]);

            var bestMove = moves[0].Coord;
            AlphaBetaEvalType maxScore, score, a = alpha;
            game.Update(ref moves[0]);

            if (depth > SHALLOW_SEARCH_DEPTH + 1)
                maxScore = score = 1 - SearchWithTT<False>(ref game, 1 - beta, 1 - a, depth - 1, out stopFlag);
            else
                maxScore = score = 1 - SearchShallow<False>(ref game, 1 - beta, 1 - a, depth - 1, out stopFlag);

            if (stopFlag)
                return a;

            game.Undo(ref moves[0], moves[..numMoves]);

            if (score >= beta)
            {
                this.tt.SaveAt(ref entry, ref game.Position, score, SCORE_MAX, depth);
                return score;
            }

            if (score > a)
                a = score;

            for (var i = 1; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);

                // NWS
                if(depth > SHALLOW_SEARCH_DEPTH + 1)
                    score = 1 - SearchWithTT<False>(ref game, 1 - a - NULL_WINDOW_WIDTH, 1 - a, depth - 1, out stopFlag);  
                else
                    score = 1 - SearchShallow<False>(ref game, 1 - a - NULL_WINDOW_WIDTH, 1 - a, depth - 1, out stopFlag);  

                if (stopFlag)
                    return a;

                if (score >= beta)
                {
                    game.Undo(ref move, moves[..numMoves]);
                    this.tt.SaveAt(ref entry, ref game.Position, score, SCORE_MAX, depth);
                    return score;
                }

                if (score > a)
                {
                    a = score;
                    if(depth > SHALLOW_SEARCH_DEPTH + 1)
                        score = 1 - SearchWithTT<False>(ref game, 1 - beta, 1 - a, depth - 1, out stopFlag);
                    else
                        score = 1 - SearchShallow<False>(ref game, 1 - beta, 1 - a, depth - 1, out stopFlag);

                    if (stopFlag)
                        return a;

                    if (score >= beta)
                    {
                        game.Undo(ref move, moves[..numMoves]);
                        this.tt.SaveAt(ref entry, ref game.Position, score, SCORE_MAX, depth);
                        return score;
                    }
                }

                game.Undo(ref move, moves[..numMoves]);

                if (score > maxScore)
                {
                    bestMove = move.Coord;
                    maxScore = score;
                    a = Math.Max(alpha, score); 
                }
            }

            if (maxScore > alpha)
                this.tt.SaveAt(ref entry, ref game.Position, bestMove, maxScore, maxScore, depth);
            else
                this.tt.SaveAt(ref entry, ref game.Position, SCORE_MIN, maxScore, depth);

            return maxScore;
        }

        [SkipLocalsInit]
        AlphaBetaEvalType SearchShallow<AfterPass>(ref GameInfo game, AlphaBetaEvalType alpha, AlphaBetaEvalType beta, int depth, out bool stopFlag) where AfterPass : struct, IFlag
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

                var ret = 1 - SearchShallow<True>(ref game, 1 - beta, 1 - alpha, depth, out stopFlag);

                game.Pass();

                return ret;
            }

            if (depth == 0)
            {
                this.NodeCount++;
                if (this.cts is not null)
                    stopFlag = this.cts.IsCancellationRequested;
                return this.valueFunc.Predict(game.FeatureVector);
            }

            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            InitMoves(ref game.Position, moves[..numMoves]);

            AlphaBetaEvalType maxScore = SCORE_MIN;
            for (var i = 0; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);
                var score = 1 - SearchShallow<False>(ref game, 1 - beta, 1 - alpha, depth - 1, out stopFlag);

                if (score >= beta)
                {
                    game.Undo(ref move, moves[..numMoves]);
                    return score;
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
        static void InitMoves(ref Position pos, Span<Move> moves)
        {
            for (var i = 0; i < moves.Length; i++)
                pos.GenerateMove(ref moves[i]);
        }

        [SkipLocalsInit]
        static bool PlaceBestMoveAtTop(ref Position pos, BoardCoordinate move, Span<Move> moves)
        {
            if(move != BoardCoordinate.Null)
            {
                var bestIdx = moves.IndexOf(move);
                (moves[0], moves[bestIdx]) = (moves[bestIdx], moves[0]);
                return true;
            }
            return false;
        }

        [SkipLocalsInit]
        void OrderMovesWithTT(ref GameInfo game, Span<Move> moves)
        {
            Span<AlphaBetaEvalType> scores = stackalloc AlphaBetaEvalType[Constants.NUM_SQUARES];
            for (var i = 0; i < moves.Length; i++)
                scores[(int)moves[i].Coord] = CalculateMoveValue(ref game, moves, i);

            SortMovesByScore(moves, scores);
        }

        [SkipLocalsInit]
        static void OrderMovesByFastestFirst(ref Position pos, Span<Move> moves)
        {
            Span<int> scores = stackalloc int[Constants.NUM_SQUARES];
            for(var i = 0; i < moves.Length; i++)
            {
                ref var move = ref moves[i];
                pos.Update(ref move);
                scores[(int)move.Coord] = pos.GetNumNextMoves();
                pos.Undo(ref move);
            }

            SortMovesByScore(moves, scores);
        }

        static void SortMovesByScore<T>(Span<Move> moves, Span<T> scores) where T : struct, INumber<T>
        {
            for (var i = 1; i < moves.Length; i++)
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

        AlphaBetaEvalType CalculateMoveValue(ref GameInfo game, Span<Move> moves, int idx)
        {
            ref var move = ref moves[idx];
            game.Update(ref move);

            AlphaBetaEvalType value;
            ref var entry = ref this.tt.GetEntry(ref game.Position, out bool hit);
            if (hit)
            {
                var lower = 1 - (AlphaBetaEvalType)entry.Lower;
                var upper = 1 - (AlphaBetaEvalType)entry.Upper;
                var genDiff = this.tt.Generation - entry.Generation;
                if (lower != SCORE_MIN)
                    value = TT_HIT_BONUS - genDiff + lower;
                else if (upper != SCORE_MAX)
                    value = TT_HIT_BONUS - genDiff + upper;
                else
                    value = 1 - this.valueFunc.Predict(game.FeatureVector);
            }
            else
                value = 1 - this.valueFunc.Predict(game.FeatureVector);

            game.Undo(ref move, moves);

            return value;
        }

        static AlphaBetaEvalType DiscDiffToScore(int score)
        {
            if (score == 0)
                return (AlphaBetaEvalType)0.5;
            return (score > 0) ? 1 : 0;
        }
    }
}
