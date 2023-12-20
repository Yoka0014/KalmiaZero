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
            var sideToMove = Console.ReadLine();
            var label = Console.ReadLine();


            Reversi.Utils.ParseDiscColor(sideToMove);
            if (sideToMove is null)
            {
                Console.Error.WriteLine("Error: Invalid disc color.");
                return;
            }
        }

        Position TryParsePosition(string? posStr, out bool success)
        {
            var pos = new Position(new Bitboard(0UL, 0UL), Disc);
            for(var i = 0; i < Constants.NUM_SQUARES; i++)
            {

            }
        }
    }
}
