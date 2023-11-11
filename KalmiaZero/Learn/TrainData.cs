using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using KalmiaZero.Utils;
using KalmiaZero.GameFormats;
using KalmiaZero.Reversi;

namespace KalmiaZero.Learn
{
    using static Reversi.Constants;

    public readonly struct TrainData
    {
        public readonly Position RootPos { get; }
        public readonly sbyte ScoreFromBlack { get; }
        public readonly sbyte TheoreticalScoreFromBlack { get; }
        public readonly sbyte TheoreticalScoreDepth { get; }
        public readonly Move[] Moves { get; }

        public TrainData(WTHORHeader wtbHeader, WTHORGameRecord game)
        {
            this.TheoreticalScoreDepth = (sbyte)wtbHeader.Depth;
            this.RootPos = new Position();
            this.Moves = game.MoveRecord.Append(Move.Pass).ToArray();
            var finalDiscCount = this.Moves.Count(move => move.Coord != BoardCoordinate.Pass) + 4;
            this.ScoreFromBlack = (sbyte)(game.BlackDiscCount * 2 - NUM_SQUARES);
            this.TheoreticalScoreFromBlack = (sbyte)(game.BestBlackDiscCount * 2 - NUM_SQUARES);
        }

        public readonly int GetScoreForm(DiscColor color)
        {
            if (color == DiscColor.Null)
                throw new ArgumentException($"{nameof(color)} cannot be {DiscColor.Null}");

            return (color == DiscColor.Black) ? this.ScoreFromBlack : -this.ScoreFromBlack;
        }

        public readonly int GetTheoreticalScoreForm(DiscColor color)
        {
            if (color == DiscColor.Null)
                throw new ArgumentException($"{nameof(color)} cannot be {DiscColor.Null}");

            return (color == DiscColor.Black) ? this.TheoreticalScoreFromBlack : -this.TheoreticalScoreFromBlack;
        }

        public static (TrainData[] trainData, TrainData[] testData) CreateTrainDataFromWTHORFiles(string dir, string jouFileName, string trnFileName, double testSize=0.1)
        {
            var allData = new List<TrainData>();
            var jouPath = Path.Combine(dir, jouFileName);
            var trnPath = Path.Combine(dir, trnFileName);
            foreach(var wtbPath in Directory.GetFiles(dir, "*.wtb"))
            {
                var wthor = new WTHORFile(jouPath, trnPath, wtbPath);
                foreach (var game in wthor.GameRecords)
                    allData.Add(new TrainData(wthor.WtbHeader, game));
            }

            Random.Shared.Shuffle(allData);
            var numTestData = (int)(allData.Count * testSize);
            var trainData = allData.Take(allData.Count - numTestData).ToArray();
            var testData = allData.Skip(trainData.Length).Take(numTestData).ToArray();
            return (trainData, testData);
        }
    }
}
