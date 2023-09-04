using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Utils;
using KalmiaZero.Reversi;
using System.Runtime.CompilerServices;
using KalmiaZero.GameFormats;

namespace KalmiaZero.Learning
{
    public struct TrainDataItem
    {
        public Bitboard Position { get; set; }
        public BoardCoordinate NextMove { get; set; }  
        public sbyte FinalDiscDiff { get; set; }
        public float EvalScore { get; set; }

        public TrainDataItem(Stream stream, bool swapBytes)
        {
            const int BUFFER_SIZE = 8;
            Span<byte> buffer = stackalloc byte[BUFFER_SIZE];

            stream.Read(buffer, swapBytes);
            var player = BitConverter.ToUInt64(buffer);
            stream.Read(buffer, swapBytes);
            var opponent = BitConverter.ToUInt64(buffer);
            this.Position = new Bitboard(player, opponent);
            this.NextMove = (BoardCoordinate)stream.ReadByte();
            this.FinalDiscDiff = (sbyte)stream.ReadByte();
            stream.Read(buffer, swapBytes);
            this.EvalScore = BitConverter.ToSingle(buffer);
        }

        public readonly void WriteTo(Stream stream)
        {
            stream.Write(BitConverter.GetBytes(this.Position.Player));
            stream.Write(BitConverter.GetBytes(this.Position.Opponent));
            stream.WriteByte((byte)this.NextMove);
            stream.WriteByte((byte)this.FinalDiscDiff);
            stream.Write(BitConverter.GetBytes(this.EvalScore));
        }
    }

    public static class TrainData
    {
        const string LABEL = "KalmiaZero";
        const string LABEL_INVERSED = "oreZaimlaK";
        const int LABEL_SIZE = 10;

        public static TrainDataItem[] LoadFromFile(string filePath)
        {
            Span<byte> buffer = stackalloc byte[LABEL_SIZE];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            fs.Read(buffer);
            var label = Encoding.ASCII.GetString(buffer);
            var swapBytes = label != LABEL;

            if(swapBytes && label != LABEL_INVERSED)
                throw new InvalidDataException($"The format of \"{filePath}\" is invalid.");

            fs.Read(buffer[..sizeof(int)], swapBytes);
            var numData = BitConverter.ToInt32(buffer);
            return (from _ in Enumerable.Range(0, numData) select new TrainDataItem(fs, swapBytes)).ToArray();
        }

        public static void WriteToFile(this IEnumerable<TrainDataItem> trainData, string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            fs.Write(Encoding.ASCII.GetBytes(LABEL));
            fs.Write(BitConverter.GetBytes(trainData.Count()));
            foreach (var item in trainData)
                item.WriteTo(fs);
        }

        public static TrainDataItem[][] CreateTrainDataFromWthorFile(string dir, string jouFileName, string trnFileName, double testGameRate = 0.1)
        {
            var jouPath = Path.Combine(dir, jouFileName);
            var trnPath = Path.Combine(dir, trnFileName);
            var gameRecords = new List<(WTHORGameRecord game, int depth)>();

            foreach (var file in Directory.GetFiles(dir))
            {
                Console.WriteLine(file);
                if (Path.GetExtension(file) != ".wtb")
                    continue;

                var wthor = new WTHORFile(jouPath, trnPath, file);
                gameRecords.AddRange(wthor.GameRecords.Zip(Enumerable.Repeat(wthor.WtbHeader.Depth, wthor.GameRecords.Count)));
            }

            Random.Shared.Shuffle(gameRecords);
            var numTestGames = (int)(gameRecords.Count * testGameRate);

            var testData = new List<TrainDataItem>();
            for (var i = 0; i < numTestGames; i++)
                addPositions(testData, gameRecords[i].game, gameRecords[i].depth);

            var trainData = new List<TrainDataItem>();
            for (var i = numTestGames; i < gameRecords.Count; i++)
                addPositions(trainData, gameRecords[i].game, gameRecords[i].depth);
            Random.Shared.Shuffle(trainData);

            return new TrainDataItem[][] { trainData.ToArray(), testData.ToArray() };

            void addPositions(List<TrainDataItem> trainData, WTHORGameRecord game, int depth)
            {
                var pos = new Position();
                foreach (var move in game.MoveRecord)
                {
                    var item = new TrainDataItem
                    {
                        Position = pos.GetBitboard(),
                        NextMove = move.Coord
                    };

                    var discDiff = (pos.EmptySquareCount > depth) ? game.BlackDiscCount : game.BestBlackDiscCount;
                    discDiff = 2 * discDiff - Constants.NUM_SQUARES;
                    item.FinalDiscDiff = (pos.SideToMove == DiscColor.Black) ? (sbyte)discDiff : (sbyte)(-discDiff);
                    item.EvalScore = float.NaN;
                    trainData.Add(item);
                }
            }
        }
    }
}
