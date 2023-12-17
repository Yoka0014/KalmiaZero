//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection.Metadata.Ecma335;
//using System.Runtime.CompilerServices;
//using System.Text;
//using System.Threading.Tasks;

//using KalmiaZero.Evaluation;
//using KalmiaZero.NTuple;
//using KalmiaZero.Reversi;
//using KalmiaZero.Utils;

//namespace KalmiaZero.Search.AlphaBeta
//{
//    public class Searcher
//    {
//        ValueFunction<float> valueFunc;
//        int maxDepth;

//        public Searcher(ValueFunction<float> valueFunc)
//        {
//            this.valueFunc = valueFunc;
//        }

//        public (BoardCoordinate bestMove, float eval) Search(ref Position pos, int depth)
//        {
//            this.maxDepth = depth;
//            var game = new GameInfo(pos, this.valueFunc.NTuples);
//            (var alpha, var beta) = (0.0f, 1.0f);

//            var bestMove = BoardCoordinate.Pass;
//            var 
//            for (var i = 0; i < game.Moves.Length; i++)
//            {
//                ref var move = ref game.Moves[i];
//            }
//        }

//        [SkipLocalsInit]
//        float Search<AfterPass>(ref GameInfo game, float alpha, float beta, int depth) where AfterPass : struct, IFlag
//        {
//            if (depth == this.maxDepth)
//                return this.valueFunc.Predict(game.FeatureVector);

//            if (game.Moves.Length == 0)  // pass
//            {
//                if (typeof(AfterPass) == typeof(True))
//                    return ScoreToEval(game.Position.DiscDiff);

//                game.Pass();
//                alpha = Math.Max(alpha, 1.0f - Search<True>(ref game, 1.0f - beta, 1.0f - alpha, depth + 1));
//                game.Pass();
//                return alpha;
//            }

//            Span<Move> moves = stackalloc Move[game.Moves.Length];
//            var numMoves = game.Moves.Length;
//            game.Moves.CopyTo(moves);

//            for (var i = 0; i < game.Moves.Length; i++)
//            {
//                var move = game.Moves[i];
//                game.Update(ref move);
//                alpha = Math.Max(alpha, 1.0f - Search<False>(ref game, 1.0f - beta, 1.0f - alpha, depth + 1));
//                game.Undo(ref move, moves[..numMoves]);
//                if (alpha >= beta)
//                    return alpha;   // alpha cut
//            }

//            return alpha;
//        }

//        static float ScoreToEval(int score)
//        {
//            if (score == 0)
//                return 0.5f;
//            return (score > 0) ? 1.0f : 0.0f;
//        }
//    }
//}
