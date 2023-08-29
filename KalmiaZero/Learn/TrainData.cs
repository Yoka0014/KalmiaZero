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

namespace KalmiaZero.Learn
{
    public struct TrainDataItem
    {
        Bitboard Position { get; set; }
        BoardCoordinate NextMove { get; set; }  
        sbyte FinalDiscDiff { get; set; }
        float EvalScore { get; set; }

        public TrainDataItem(Stream stream, bool swapBytes)
        {
            const int BUFFER_SIZE = 8;
            Span<byte> buffer = stackalloc byte[BUFFER_SIZE];

            stream.Read(buffer, swapBytes);
            var player = BitConverter.ToUInt64(buffer);
            stream.Read(buffer, swapBytes);
            var opponent = BitConverter.ToUInt64(buffer);
            this.Position = new Bitboard(player, opponent);
            this.FinalDiscDiff = (sbyte)stream.ReadByte();
            stream.Read(buffer, swapBytes);
            this.EvalScore = BitConverter.ToSingle(buffer);
        }

        public readonly void WriteTo(Stream stream)
        {
            stream.Write(BitConverter.GetBytes(this.Position.Player));
            stream.Write(BitConverter.GetBytes(this.Position.Opponent));
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
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            fs.Write(Encoding.ASCII.GetBytes(LABEL));
            fs.Write(BitConverter.GetBytes(trainData.Count()));
            foreach (var item in trainData)
                item.WriteTo(fs);
        }

        public static TrainDataItem[] CreateTrainDataFromWthorFile(string dir, string jouFileName, string trnFileName)
        {
            var jouPath = Path.Combine(dir, jouFileName);
            var trnPath = Path.Combine(dir, trnFileName);
            foreach(var file in Directory.GetFiles(dir))
            {
                if (Path.GetExtension(file) != ".wtb")
                    continue;

                var wthor = new WTHORFile(jouPath, trnPath, file);
                foreach(WTHORGameRecord game in wthor.GameRecords)
                {
                    var pos = new Position();
                    foreach(var move in game.MoveRecord)
                    {
                        var bb = pos.GetBitboard();
                        
                    }
                }
            }
        }
    }
}
