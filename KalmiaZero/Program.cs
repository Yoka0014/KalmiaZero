using System;
using System.Text.Json;

using KalmiaZero.Engines;
using KalmiaZero.GameFormats;
using KalmiaZero.NTuple;
using KalmiaZero.Protocols;
using KalmiaZero.Reversi;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var ntuples = new NTupleInfo(3, 7, 100);
            Console.WriteLine(ntuples);
        }
    }
}