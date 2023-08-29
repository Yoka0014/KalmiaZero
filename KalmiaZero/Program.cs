using System;
using System.Collections.Generic;
using System.Text.Json;

using KalmiaZero.Engines;
using KalmiaZero.GameFormats;
using KalmiaZero.NTuple;
using KalmiaZero.Protocols;
using KalmiaZero.Reversi;
using KalmiaZero.Learning;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var trainData = TrainData.CreateTrainDataFromWthorFile(@"C:\Users\yu_ok\source\repos\KalmiaZero\TrainData", "WTHOR.JOU", "WTHOR.TRN");
            Console.WriteLine(trainData.Length);
        }
    }
}