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

        public TrainData(Position rootPos, IEnumerable<Move> moves, sbyte scoreFromBlack) : this(rootPos, moves, scoreFromBlack, 0, NUM_SQUARES) { }

        public TrainData(Position rootPos, IEnumerable<Move> moves, sbyte scoreFromBlack,  sbyte theoreticalScoreFromBlack, sbyte theoreticalScoreDepth)
        {
            this.RootPos = rootPos;
            this.Moves = moves.ToArray();
            this.ScoreFromBlack = scoreFromBlack;
            this.TheoreticalScoreFromBlack = theoreticalScoreFromBlack;
            this.TheoreticalScoreDepth = theoreticalScoreDepth;
        }

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

        public static (TrainData[] trainData, TrainData[] testData) CreateTrainDataFromWTHORFiles(string dir, string jouFileName, string trnFileName, int numData=-1, double testSize=0.1)
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

            if (numData < 0)
                numData = allData.Count;

            Random.Shared.Shuffle(allData);
            var numTestData = (int)(numData * testSize);
            var trainData = allData.Take(numData - numTestData).ToArray();
            var testData = allData.Skip(trainData.Length).Take(numTestData).ToArray();
            return (trainData, testData);
        }

        public static TrainData[] CreateTrainDataFormF5D6File(string path)
        {
            var allData = new List<TrainData>();
            var moves = new List<Move>();
            using var sr = new StreamReader(path);
            var lineCount = 0;
            var move = new Move();
            while(sr.Peek() != -1)
            {
                lineCount++;
                var line = sr.ReadLine();
                var pos = new Position();
                moves.Clear();
                for(var i = 0; i < line.Length; i += 2)
                {
                    if (pos.CanPass)
                    {
                        pos.Pass();
                        move.Coord = BoardCoordinate.Pass;
                        moves.Add(move);
                    }

                    move.Coord = Reversi.Utils.ParseCoordinate(line[i..(i + 2)]);
                    if (move.Coord == BoardCoordinate.Null)
                        throw new InvalidDataException($"File \"{path}\" included invalid board coordinate: {line[i..(i + 2)]}.");

                    if(!pos.IsLegalMoveAt(move.Coord))
                        throw new InvalidDataException($"File \"{path}\" included an illegal move at line {lineCount}.");

                    pos.GenerateMove(ref move);
                    pos.Update(ref move);
                    moves.Add(move);
                }
                allData.Add(new TrainData(new Position(), moves, (sbyte)pos.GetScore(DiscColor.Black)));
            }
            return allData.ToArray();
        }

        public static (TrainData[] trainData, TrainData[] testData) SplitIntoTrainAndTest(TrainData[] data, double testSize=0.1)
        {
            var allData = (TrainData[])data.Clone();
            Random.Shared.Shuffle(allData);
            var numTestData = (int)(allData.Length * testSize);
            var trainData = allData.Take(allData.Length - numTestData).ToArray();
            var testData = allData.Skip(trainData.Length).Take(numTestData).ToArray();
            return (trainData, testData);
        }
    }
}
