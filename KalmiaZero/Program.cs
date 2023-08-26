using System;
using System.Collections.Generic;
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
            for (var i = 0; i < 100; i++)
            {
                Console.WriteLine($"ID: {i}\n");

                var nTuple = new NTupleInfo(7);
                Console.WriteLine(nTuple);
            }
        }
    }
}