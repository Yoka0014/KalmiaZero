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
            var pos = new Position();
            var i = 0;
            foreach(var tuples in ntuples.Tuples)
            {
                Console.WriteLine($"Tuple ID: {i++}");
                foreach (var tuple in tuples)
                {
                    pos.RemoveAllDiscs();
                    foreach (var coord in tuple)
                        pos.PutPlayerDiscAt(coord);
                    Console.WriteLine($"{pos}\n");
                }
            }
        }
    }
}