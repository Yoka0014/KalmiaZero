using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.Search.AlphaBeta
{
    internal static class EndgameTest
    {
        public static void SearchOneEndgame(Searcher searcher)
        {
            var posStr = Console.ReadLine();
            var sideToMoveStr = Console.ReadLine();
            var label = Console.ReadLine();

            var pos = TryParsePosition(posStr, out bool success);
            if (!success)
            {
                Console.Error.WriteLine("Error: Invalid position.");
                return;
            }

            pos.SideToMove = Reversi.Utils.ParseDiscColor(sideToMoveStr);
            if (pos.SideToMove == DiscColor.Null)
            {
                Console.Error.WriteLine("Error: Invalid disc color.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Position:\n{pos}\n");
            Console.WriteLine($"Side to move: {pos.SideToMove}\n");
            Console.WriteLine($"Test case: {label}\n");

            searcher.SetRootPos(pos);
            var res = searcher.Search(pos.EmptySquareCount);

            Console.WriteLine($"Best move: {res.BestMove}");
            Console.WriteLine($"WLD: {res.EvalScore * 100.0f:f2}%");
            Console.WriteLine($"Ellapsed: {res.EllpasedMs}[ms]");
            Console.WriteLine($"NPS: {(double)res.NodeCount / res.EllpasedMs}[nps]");
            Console.WriteLine($"Node count: {searcher.nodeCount}[nodes]");

            Console.Write("PV: ");
            foreach (var move in res.PV)
                Console.Write(move);
            Console.WriteLine();
        }

        static Position TryParsePosition(string? posStr, out bool success)
        {
            if(posStr is null)
            {
                success = false;
                return new Position();
            }

            var str = posStr.Trim().ToLower();
            var pos = new Position(new Bitboard(0UL, 0UL), DiscColor.Black);
            for(var i = 0; i < Constants.NUM_SQUARES && i < str.Length; i++)
            {
                if (str[i] == 'x')
                    pos.PutDisc(DiscColor.Black, (BoardCoordinate)i);
                else if (str[i] == 'o')
                    pos.PutDisc(DiscColor.White, (BoardCoordinate)i);
                else if (str[i] != '-')
                {
                    success = false;
                    return new Position();
                }
            }

            success = true;
            return pos;
        }
    }
}
