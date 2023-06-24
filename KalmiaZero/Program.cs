using System;

using KalmiaZero.GameFormats;
using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var ggf = new GGFReversiGame("(;GM[Othello]PC[NBoard]DT[2014-02-21 20:52:27 GMT]PB[./mEdax]PW[chris]RE[?]TI[15:00]TY[8]BO[8 ---------------------------O*------*O--------------------------- *]B[F5]W[F6]B[D3]W[C5]B[E6]W[F7]B[E7]W[F4];)");
            Console.WriteLine(ggf.BlackPlayerName);
        }
    }
}