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
            trainData[0].WriteToFile("train_data.bin");
            trainData[1].WriteToFile("test_data.bin");

            var data = TrainData.LoadFromFile("train_data.bin");
            Console.WriteLine("TrainData");
            Console.WriteLine(data.Length);
            Console.WriteLine(data[0].FinalDiscDiff);
            Console.WriteLine(data[0].NextMove);

            data = TrainData.LoadFromFile("test_data.bin");
            Console.WriteLine("\nTestData");
            Console.WriteLine(data.Length);
            Console.WriteLine(data[0].FinalDiscDiff);
            Console.WriteLine(data[0].NextMove);
        }
    }
}