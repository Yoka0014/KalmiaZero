global using MiniMaxType = System.Single;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using KalmiaZero.Evaluation;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero.Search.AlphaBeta
{
    public readonly struct SearchResult 
    {
        public BoardCoordinate BestMove { get; }
        public MiniMaxType EvalScore { get; }
        public int Depth { get; }
        public ulong NodeCount { get; }
        public int EllpasedMs { get; }
        public ReadOnlyCollection<BoardCoordinate> PV => new(this.pv);

        readonly List<BoardCoordinate> pv;

        public SearchResult(BoardCoordinate bestMove, MiniMaxType evalScore, int depth, ulong nodeCount, int ellpasedMs, List<BoardCoordinate> pv)
        {
            this.BestMove = bestMove;
            this.EvalScore = evalScore;
            this.Depth = depth;
            this.NodeCount = nodeCount;
            this.EllpasedMs = ellpasedMs;
            this.pv = pv;
        }
    }

    public unsafe struct PV
    {
        public int Count { get; private set; }
        public bool CutByTT { get; set; }

        public BoardCoordinate this[int idx]
        {
            get
            {
#if DEBUG
                if (idx < 0 || idx >= this.Count)
                    throw new ArgumentOutOfRangeException();
#endif
                return (BoardCoordinate)this.moves[idx];
            }

            private set
            {
#if DEBUG
                if (idx < 0 || idx >= this.Count)
                    throw new ArgumentOutOfRangeException();
#endif
                this.moves[idx] = (byte)value;
            }
        }

        fixed byte moves[Constants.NUM_SQUARES];

        public PV() { }

        public PV(ref PV src)
        {
            this.Count = src.Count;
            for (var i = 0; i < src.Count; i++)
                this[i] = src[i];
        }

        public void Clear() => this.Count = 0;

        public bool Contains(BoardCoordinate move)
        {
            for (var i = 0; i < this.Count; i++)
                if (this[i] == move)
                    return true;
            return false;
        }

        public void AddMove(BoardCoordinate move)
        {
            Debug.Assert(this.Count < Constants.NUM_SQUARES);

            this.Count++;
            this[this.Count - 1] = move;
        }

        public void AddRange(ref PV pv)
        {
            for (var i = 0; i < pv.Count; i++)
                AddMove(pv[i]);
            this.CutByTT = pv.CutByTT;
        }

        public void RemoveUnder(int startIdx)
        {
            Debug.Assert(startIdx >= 0 && startIdx < this.Count);
            this.Count -= this.Count - startIdx;
            this.CutByTT = false;
        }

        public void UpdatePositionAlongPV(ref Position pos)
        {
            var move = new Move();
            for(var i = 0; i < this.Count; i++)
            {
                if (pos.CanPass)
                    pos.Pass();

                move.Coord = this[i];
                pos.GenerateMove(ref move);
                pos.Update(ref move);
            }
        }

        public List<BoardCoordinate> ToList()
        {
            var list = new List<BoardCoordinate>();
            for (var i = 0; i < this.Count; i++)
                list.Add(this[i]);
            return list;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < this.Count; i++)
                sb.Append(this[i]);

            return sb.ToString();
        }
    }

    public class Searcher
    {
        const MiniMaxType SCORE_MIN = 0;
        const MiniMaxType SCORE_MAX = 1;
        const MiniMaxType INVALID_SCORE = -1;
        const MiniMaxType NULL_WINDOW_WIDTH = (MiniMaxType)1.0e-7;
        const MiniMaxType TT_HIT_BONUS = 50;
        const int ID_DEPTH_MIN = 4;
        const int MIDGAME_SHALLOW_DEPTH = 3;
        const int ENDGAME_SHALLOW_DEPTH = 7;
        const int END_GAME_DEPTH = 15;
        const bool SHOW_LOG = false;

        public ulong nodeCount;

        public bool IsSearching => this.isSearching;

        public int PVNotificationIntervalMs { get; set; } = 100;
        public event EventHandler<SearchResult>? PVWasUpdated;

        volatile bool isSearching;

        GameInfo rootGame;
        GameInfo gameForSearch;
        readonly TranspositionTable tt;
        ValueFunction<MiniMaxType> valueFunc;

        CancellationTokenSource? cts;

        public Searcher(ValueFunction<MiniMaxType> valueFunc, long ttSize)
        {
            this.valueFunc = valueFunc;
            this.rootGame = new GameInfo(this.valueFunc.NTuples);
            this.gameForSearch = new GameInfo(this.valueFunc.NTuples);
            this.tt = new TranspositionTable(ttSize);
        }

        public void SendStopSearchSignal() => this.cts?.Cancel();

        public void SetTranspositionTableSize(long size) => this.tt.Resize(size);

        public void SetRootPos(Position pos)
        {
            var game = new GameInfo(pos, this.valueFunc.NTuples);
            SetRootGame(ref game);
        }

        public void SetRootGame(ref GameInfo game)
        {
            game.CopyTo(ref this.rootGame);
            this.tt.Clear();
        }

        public bool UpdateRootGame(BoardCoordinate moveCoord)
        {
            if (!this.rootGame.Position.IsLegalMoveAt(moveCoord))
                return false;

            var move = this.rootGame.Position.GenerateMove(moveCoord);
            UpdateRootGame(ref move);

            return true;
        }

        public void UpdateRootGame(ref Move move)
        {
            if (move.Coord != BoardCoordinate.Pass)
                this.rootGame.Update(ref move);
            else
                this.rootGame.Pass();
            this.tt.IncrementGeneration();
        }

        public void PassRootGame()
        {
            this.rootGame.Pass();
            this.tt.IncrementGeneration();
        }

        public List<BoardCoordinate> CreatePVList(ref PV pv, int maxDepth)
        {
            var pvList = pv.ToList();
            var pos = this.rootGame.Position;

            // 置換表を用いることで，葉ノードまで探索せずに切り上げている場合がある．
            // その場合は，残りのPVを置換表から探す．
            // Note: 置換表が小さい場合や長時間探索させた後だと，PVを葉ノードまで復元できない場合がある．
            if (pv.CutByTT)
            {
                pv.UpdatePositionAlongPV(ref pos);
                this.tt.ProbePV(ref pos, pvList, maxDepth - pv.Count);
            }

            return pvList;
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
            this.nodeCount = 0;
            this.isSearching = true;

            BoardCoordinate bestMove;
            MiniMaxType score;
            var ellapsedMs = 0;
            var pv = new PV();
            int depth;
            ulong nodeCount;
            if (maxDepth <= ID_DEPTH_MIN)   // 浅い読みのときは，最初からmaxDepthだけ読む．
            {
                this.nodeCount = 0UL;
                (bestMove, score) = SearchRoot(maxDepth, BoardCoordinate.Null, ref pv, out _);
                nodeCount = this.nodeCount;
                depth = maxDepth;
            }
            else
            {
                // 反復深化でID_DEPTH_MINから1ずつ深さを増やしてmaxDepthまで読む．
                depth = ID_DEPTH_MIN;
                this.nodeCount = 0UL;
                (bestMove, score) = SearchRoot(ID_DEPTH_MIN, BoardCoordinate.Null, ref pv, out _);
                nodeCount = this.nodeCount;

                if (this.PVNotificationIntervalMs == 0)
                    this.PVWasUpdated?.Invoke(this, new SearchResult(bestMove, score, depth, nodeCount, 0, CreatePVList(ref pv, depth)));

                var currentPV = new PV();
                for (depth = ID_DEPTH_MIN + 1; depth <= maxDepth; depth++)
                {
                    this.nodeCount = 0UL;
                    currentPV.Clear();
                    var startTime = Environment.TickCount;
                    (var nextBestMove, var nextScore) = SearchRoot(depth, bestMove, ref currentPV, out bool suspended);
                    var endTime = Environment.TickCount;
                    nodeCount = this.nodeCount;

                    if (suspended)
                    {
                        depth--;
                        break;
                    }

                    (bestMove, score) = (nextBestMove, nextScore);
                    pv = currentPV;
                    ellapsedMs = endTime - startTime;

                    if (ellapsedMs >= this.PVNotificationIntervalMs)
                        this.PVWasUpdated?.Invoke(this, new SearchResult(bestMove, score, depth, nodeCount, ellapsedMs, CreatePVList(ref pv, depth)));

                    if (SHOW_LOG)
                        Console.WriteLine($"BestMove: {bestMove} WinRate: {score * 100.0f:f2}% Depth: {depth}");
                }
            }

            this.isSearching = false;

            return new SearchResult(bestMove, score, depth, nodeCount, ellapsedMs, CreatePVList(ref pv, depth));
        }

        (BoardCoordinate move, MiniMaxType evalScore) SearchRoot(int depth, BoardCoordinate prevBestMove, ref PV pv, out bool suspended)
        {
            suspended = false;

            var game = this.gameForSearch;
            this.rootGame.CopyTo(ref game);

            if (game.Moves.Length == 0) // pass
            {
                this.isSearching = false;
                return (BoardCoordinate.Pass, MiniMaxType.NaN);
            }

            // 着手の列挙．
            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            // 各着手において裏返る石を計算.
            InitMoves(ref game.Position, moves);

            // 前回の探索における最善手を頭に持ってくる．
            var offset = PlaceBestMoveAtTop(prevBestMove, moves) ? 1 : 0;

            // 頭以外の手を並び替え．
            bool endgame;
            if (game.Position.EmptySquareCount > END_GAME_DEPTH)
            {
                endgame = false;
                if (depth > MIDGAME_SHALLOW_DEPTH)
                    OrderMidGameMoves<True>(ref game, moves[offset..numMoves]);
            }
            else
            {
                endgame = true;
                if (depth > ENDGAME_SHALLOW_DEPTH)
                    OrderEndGameMoves<True>(ref game, moves[offset..numMoves]);
            }
            
            // 頭の着手を最善手と仮定．
            var bestMove = moves[0].Coord;
            pv.AddMove(bestMove);
            game.Update(ref moves[0]);
            MiniMaxType alpha = SCORE_MIN, beta = SCORE_MAX, score;
            bool stopFlag;
            if(!endgame)
                score = 1 - GoDown<False>(ref game, 1 - beta, 1 - alpha, ref pv, depth - 1, out stopFlag);
            else
                score = 1 - GoDown<True>(ref game, 1 - beta, 1 - alpha, ref pv, depth - 1, out stopFlag);

            if (stopFlag)
            {
                suspended = true;
                return (bestMove, score);
            }

            game.Undo(ref moves[0], moves[..numMoves]);

            if (SHOW_LOG)
                Console.WriteLine($"Move: {moves[0].Coord}  Value: {score * 100.0}%");

            if (score > alpha) 
                alpha = score;

            var currentPV = new PV();
            for (var i = 1; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);

                // まずはNWSで，alpha値を超えるかどうか確認．
                var nullAlpha = Math.Max(1 - alpha - NULL_WINDOW_WIDTH, SCORE_MIN);
                currentPV.Clear();
                currentPV.AddMove(move.Coord);
                if (!endgame)
                    score = 1 - GoDown<False>(ref game, nullAlpha, 1 - alpha, ref currentPV, depth - 1, out stopFlag);
                else
                    score = 1 - GoDown<True>(ref game, nullAlpha, 1 - alpha, ref currentPV, depth - 1, out stopFlag);

                if (stopFlag)
                {
                    suspended = true;
                    break;
                }

                // alpha値を超えることが判明．
                if (score > alpha)
                {
                    if (SHOW_LOG)
                        Console.WriteLine($"Move: {moves[i].Coord}  Value(LCB): {score * 100.0}%");

                    currentPV.Clear();
                    currentPV.AddMove(move.Coord);

                    alpha = score; 
                    if (!endgame)
                        score = 1 - GoDown<False>(ref game, 1 - beta, 1 - alpha, ref currentPV, depth - 1, out stopFlag);
                    else
                        score = 1 - GoDown<True>(ref game, 1 - beta, 1 - alpha, ref currentPV, depth - 1, out stopFlag);

                    if (SHOW_LOG)
                        Console.WriteLine($"Move: {moves[i].Coord}  Value: {score * 100.0}%");

                    if (stopFlag)
                    {
                        suspended = true;
                        break;
                    }

                    // 最善手の更新.
                    if (score >= alpha)
                    {
                        alpha = score;
                        bestMove = move.Coord;
                        pv = currentPV;
                    }
                }
                else
                {
                    if (SHOW_LOG)
                        Console.WriteLine($"Move: {moves[i].Coord}  Value(UCB): {score * 100.0}%");
                }

                game.Undo(ref move, moves[..numMoves]);
            }

            this.isSearching = false;

            return (bestMove, alpha);
        }

        MiniMaxType GoDown<Endgame>(ref GameInfo game, MiniMaxType alpha, MiniMaxType beta, ref PV pv, int depth, out bool stopFlag)
            where Endgame : struct, IFlag
        {
            Debug.Assert(alpha <= beta);
            Debug.Assert(SCORE_MIN <= alpha && alpha <= SCORE_MAX);
            Debug.Assert(SCORE_MIN <= beta && beta <= SCORE_MAX);
            Debug.Assert(depth >= 0);

            this.nodeCount++;

            if (typeof(Endgame) == typeof(False))   // 終盤ではない場合．
            {
                if (depth > MIDGAME_SHALLOW_DEPTH)  // 葉ノードから遠い．
                {
                    if(game.Position.EmptySquareCount > END_GAME_DEPTH)
                        return SearchWithTT<False, False>(ref game, alpha, beta, ref pv, depth, out stopFlag);  // 中盤探索を続行．
                    else
                        return SearchWithTT<True, False>(ref game, alpha, beta, ref pv, depth, out stopFlag);   // 終盤探索に遷移．
                }
                else  // 葉ノードに近い．
                    return SearchShallow<False>(ref game, alpha, beta, ref pv, depth, out stopFlag);
            }
            else   // 終盤の場合．
            {
                if (depth > ENDGAME_SHALLOW_DEPTH)  // 葉ノードから遠い．
                    return SearchWithTT<True, False>(ref game, alpha, beta, ref pv, depth, out stopFlag);
                else  // 葉ノードに近い．
                    return SearchShallow<False>(ref game, alpha, beta, ref pv, depth, out stopFlag);
            }
        }

        [SkipLocalsInit]
        MiniMaxType SearchWithTT<Endgame, AfterPass>(ref GameInfo game, MiniMaxType alpha, MiniMaxType beta, ref PV pv, int depth, out bool stopFlag)
            where Endgame : struct, IFlag where AfterPass : struct, IFlag
        {
            stopFlag = false;

            // 置換表を参照し，枝刈り可能か確認．
            ref var entry = ref this.tt.GetEntry(ref game.Position, out bool hit);
            if (hit && entry.Generation == this.tt.Generation && entry.Depth == depth)
            {
                var upper = (MiniMaxType)entry.Upper;
                var lower = (MiniMaxType)entry.Lower;
                var ret = INVALID_SCORE;
                if (alpha >= upper)
                    ret = upper;
                else if (beta <= lower)
                    ret = lower;
                else if (lower == upper)
                    ret = upper;

                if(ret != INVALID_SCORE)
                {
                    pv.CutByTT = true;
                    return ret;
                }

                alpha = Math.Max(alpha, lower);
                beta = Math.Min(beta, upper);
            }

            if (game.Moves.Length == 0)  // pass
            {
                // 2連続パスは終局．
                if (typeof(AfterPass) == typeof(True))
                {
                    if (this.cts is not null)
                        stopFlag = this.cts.IsCancellationRequested;
                    return DiscDiffToScore(game.Position.DiscDiff);
                }

                game.Pass();

                // パスはそのまま1手先を読む(深さは消費しない)．
                MiniMaxType ret;
                if (typeof(Endgame) == typeof(False))
                    ret = 1 - SearchWithTT<False, True>(ref game, 1 - beta, 1 - alpha, ref pv, depth, out stopFlag);
                else
                    ret = 1 - SearchWithTT<True, True>(ref game, 1 - beta, 1 - alpha, ref pv, depth, out stopFlag);

                if (stopFlag)
                    return alpha;

                game.Pass();

                return ret;
            }

            // 葉ノードで評価．
            if (depth == 0)
            {
                if (this.cts is not null)
                    stopFlag = this.cts.IsCancellationRequested;
                return EvaluateNode(ref game);
            }

            // 以降，中間ノードの処理．

            // 現在何手目か記録.
            var ply = pv.Count;

            // 着手の列挙
            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            // 各着手において裏返る石を計算.
            InitMoves(ref game.Position, moves);

            // 置換表に前回の探索の最善手が記録されていれば，それを頭に持ってくる.
            var offset = (hit && PlaceBestMoveAtTop(entry.Move, moves)) ? 1 : 0;

            // 頭に持ってきた着手以外を並び替え．
            if (typeof(Endgame) == typeof(False))
                OrderMidGameMoves<True>(ref game, moves[offset..numMoves]);
            else
                OrderEndGameMoves<True>(ref game, moves[offset..numMoves]);

            // 頭の着手が最善手だと仮定.
            var bestMove = moves[0].Coord;
            MiniMaxType maxScore, score, a = alpha;  
            game.Update(ref moves[0]);
            pv.AddMove(bestMove);

            // 最善候補を広い探索窓で探索．
            maxScore = score = 1 - GoDown<Endgame>(ref game, 1 - beta, 1 - a, ref pv, depth - 1, out stopFlag);

            if (stopFlag)
                return a;

            game.Undo(ref moves[0], moves[..numMoves]);

            // beta cut
            if (score >= beta)
            {
                this.tt.SaveAt(ref entry, ref game.Position, score, SCORE_MAX, depth);
                return score;
            }

            // scoreがalpha値を超えたら探索窓を狭める.
            if (score > a)
                a = score;

            // 残りの手を探索.
            var currentPV = new PV();
            for (var i = 1; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);

                // まずはNWSで，alpha値を超えるかどうか確認．
                var nullAlpha = Math.Max(1 - a - NULL_WINDOW_WIDTH, SCORE_MIN);
                currentPV.Clear();
                currentPV.AddMove(move.Coord);
                score = 1 - GoDown<Endgame>(ref game, nullAlpha, 1 - a, ref currentPV, depth - 1, out stopFlag);

                if (stopFlag)
                    return a;

                // beta cut
                if (score >= beta)
                {
                    game.Undo(ref move, moves[..numMoves]);

                    // 置換表に，この局面は少なくともscore以上の評価値であることを記録．
                    this.tt.SaveAt(ref entry, ref game.Position, score, SCORE_MAX, depth);

                    pv.RemoveUnder(ply);
                    pv.AddRange(ref currentPV);

                    return score;
                }

                // alpha値を超えることが判明.
                if (score > a)
                {
                    // 探索窓を狭める.
                    a = score; 

                    currentPV.Clear();
                    currentPV.AddMove(move.Coord);

                    // 通常の探索窓で再探索し，真の評価値を求める.
                    score = 1 - GoDown<Endgame>(ref game, 1 - beta, 1 - a, ref currentPV, depth - 1, out stopFlag);

                    if (stopFlag)
                        return a;

                    // beta cut
                    if (score >= beta)
                    {
                        game.Undo(ref move, moves[..numMoves]);
                        this.tt.SaveAt(ref entry, ref game.Position, score, SCORE_MAX, depth);

                        pv.RemoveUnder(ply);
                        pv.AddRange(ref currentPV);

                        return score;
                    }
                }

                // 最善手の更新.
                if (score > maxScore)
                {
                    bestMove = move.Coord;
                    maxScore = score;
                    a = Math.Max(a, score);

                    pv.RemoveUnder(ply);
                    pv.AddRange(ref currentPV);
                }

                game.Undo(ref move, moves[..numMoves]);
            }

            if (maxScore >= alpha)  // 真の評価値はmaxScoreであると置換表に記録．
                this.tt.SaveAt(ref entry, ref game.Position, bestMove, maxScore, maxScore, depth);
            else  // 評価値はmaxScore以下であると置換表に記録．
                this.tt.SaveAt(ref entry, ref game.Position, SCORE_MIN, maxScore, depth);

            return maxScore;
        }

        [SkipLocalsInit]
        MiniMaxType SearchShallow<AfterPass>(ref GameInfo game, MiniMaxType alpha, MiniMaxType beta, ref PV pv, int depth, out bool stopFlag) where AfterPass : struct, IFlag
        {
            stopFlag = false;

            if (game.Moves.Length == 0)  // pass
            {
                if (typeof(AfterPass) == typeof(True))
                {
                    if (this.cts is not null)
                        stopFlag = this.cts.IsCancellationRequested;
                    return DiscDiffToScore(game.Position.DiscDiff);
                }

                game.Pass();

                var ret = 1 - SearchShallow<True>(ref game, 1 - beta, 1 - alpha, ref pv, depth, out stopFlag);

                game.Pass();

                return ret;
            }

            if (depth == 0)
            {
                if (this.cts is not null)
                    stopFlag = this.cts.IsCancellationRequested;
                return EvaluateNode(ref game);
            }

            var ply = pv.Count;
            pv.AddMove(BoardCoordinate.Null);   // 後の処理を簡略化するためにdummyを入れておく.

            Span<Move> moves = stackalloc Move[game.Moves.Length];
            var numMoves = game.Moves.Length;
            game.Moves.CopyTo(moves);

            InitMoves(ref game.Position, moves[..numMoves]);

            var currentPV = new PV();
            var bestMove = BoardCoordinate.Null;
            MiniMaxType maxScore = SCORE_MIN;
            for (var i = 0; i < numMoves; i++)
            {
                ref var move = ref moves[i];
                game.Update(ref move);
                currentPV.AddMove(move.Coord);
                this.nodeCount++;
                var score = 1 - SearchShallow<False>(ref game, 1 - beta, 1 - alpha, ref currentPV, depth - 1, out stopFlag);

                // beta cut
                if (score >= beta)
                {
                    game.Undo(ref move, moves[..numMoves]);
                    pv.RemoveUnder(ply);
                    pv.AddRange(ref currentPV);
                    return score;
                }

                game.Undo(ref move, moves[..numMoves]);

                if (score >= maxScore)
                {
                    bestMove = move.Coord;
                    maxScore = score;
                    alpha = Math.Max(alpha, score);

                    pv.RemoveUnder(ply);
                    pv.AddRange(ref currentPV);
                }

                currentPV.Clear();
            }

            // PVを辿れるように浅い探索でも最善手だけは置換表に記録．
            if (maxScore >= alpha)
            {
                ref var entry = ref this.tt.GetEntry(ref game.Position, out bool hit);
                this.tt.SaveAt(ref entry, ref game.Position, bestMove, maxScore, maxScore, depth);
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
        static bool PlaceBestMoveAtTop(BoardCoordinate move, Span<Move> moves)
        {
            if (move != BoardCoordinate.Null)
            {
                var bestIdx = moves.IndexOf(move);
                (moves[0], moves[bestIdx]) = (moves[bestIdx], moves[0]);
                return true;
            }
            return false;
        }

        void OrderMidGameMoves<UseTT>(ref GameInfo game, Span<Move> moves) where UseTT : struct, IFlag
        {
            Span<MiniMaxType> scores = stackalloc MiniMaxType[Constants.NUM_SQUARES];
            for (var i = 0; i < moves.Length; i++)
                scores[(int)moves[i].Coord] = CalculateMoveValue<UseValueFunc, UseTT>(ref game, moves, i);
            SortMovesByScore(moves, scores);
        }

        void OrderEndGameMoves<UseTT>(ref GameInfo game, Span<Move> moves) where UseTT : struct, IFlag
        {
            Span<MiniMaxType> scores = stackalloc MiniMaxType[Constants.NUM_SQUARES];
            for (var i = 0; i < moves.Length; i++)
                scores[(int)moves[i].Coord] = CalculateMoveValue<FastestFirst, UseTT>(ref game, moves, i);
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

        MiniMaxType EvaluateNode(ref GameInfo game)
        {
            // 置換表にはHalf型で評価値を格納しているので，一旦評価関数の出力をHalfにキャストして桁落ちさせる.
            // 桁落ちさせないと，置換表を用いた枝刈りでバグる．
            // MiniMaxTypeに再キャストする理由は，Halfのまま演算を行うと，毎回floatやdoubleにキャストされるため，演算が遅くなるから．
            return (MiniMaxType)(Half)this.valueFunc.Predict(game.FeatureVector);
        }

        MiniMaxType CalculateMoveValue<Policy, UseTT>(ref GameInfo game, Span<Move> moves, int idx) 
            where Policy : struct, IMoveOrderingPolicy where UseTT : struct, IFlag
        {
            ref var move = ref moves[idx];
            game.Update(ref move);

            MiniMaxType value = 0;

            var hit = false;
            if (typeof(UseTT) == typeof(True))
            {
                ref var entry = ref this.tt.GetEntry(ref game.Position, out hit);
                if (hit)
                {
                    var lower = 1 - (MiniMaxType)entry.Lower;
                    var upper = 1 - (MiniMaxType)entry.Upper;
                    var genDiff = this.tt.Generation - entry.Generation;
                    if (lower != SCORE_MIN)
                        value = TT_HIT_BONUS - genDiff + lower;
                    else if (upper != SCORE_MAX)
                        value = TT_HIT_BONUS - genDiff + upper;
                    else
                        value = 1 - this.valueFunc.Predict(game.FeatureVector);
                }
            }

            if (!hit)
            {
                if (typeof(Policy) == typeof(UseValueFunc))
                    value = 1 - this.valueFunc.Predict(game.FeatureVector);
                else if (typeof(Policy) == typeof(FastestFirst))
                    value = (MiniMaxType)1 / game.Position.GetNumNextMoves();
            }

            game.Undo(ref move, moves);

            return value;
        }

        static MiniMaxType DiscDiffToScore(int score)
        {
            if (score == 0)
                return (MiniMaxType)0.5;
            return (score > 0) ? 1 : 0;
        }

        interface IMoveOrderingPolicy { }
        struct UseValueFunc : IMoveOrderingPolicy { }
        struct FastestFirst : IMoveOrderingPolicy { }
    }
}
