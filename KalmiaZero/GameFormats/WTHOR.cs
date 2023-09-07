using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero.GameFormats
{
    public class WTHORHeader
    {
        public const int SIZE = 16;

        public DateTime FileCreationTime { get; set; }
        public int NumberOfGames { get; set; }
        public int NumberOfRecords { get; set; }
        public int GameYear { get; set; }
        public int BoardSize { get; set; }
        public int GameType { get; set; }
        public int Depth { get; set; }

        public WTHORHeader(string path)
        {
            const int BUFFER_SIZE = 4;

            var swapBytes = !BitConverter.IsLittleEndian;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            Span<byte> buffer = stackalloc byte[BUFFER_SIZE];
            try
            {
                this.FileCreationTime = new DateTime(fs.ReadByte()* 100 + fs.ReadByte(), fs.ReadByte(), fs.ReadByte());
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("The creation time is invalid.");
            }

            fs.Read(buffer[..sizeof(int)], swapBytes);
            this.NumberOfGames = BitConverter.ToInt32(buffer);

            fs.Read(buffer[..sizeof(ushort)], swapBytes);
            this.NumberOfRecords = BitConverter.ToUInt16(buffer);

            fs.Read(buffer[..sizeof(ushort)], swapBytes);
            this.GameYear = BitConverter.ToUInt16(buffer);

            this.BoardSize = fs.ReadByte();
            this.GameType = fs.ReadByte();
            this.Depth = fs.ReadByte();
        }

        public void WriteToFile(string path)
        {
            var swapBytes = !BitConverter.IsLittleEndian;
            using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
            fs.WriteByte((byte)(this.FileCreationTime.Year / 100));
            fs.WriteByte((byte)(this.FileCreationTime.Year % 100));
            fs.WriteByte((byte)this.FileCreationTime.Month);
            fs.WriteByte((byte)this.FileCreationTime.Day);
            fs.Write(BitConverter.GetBytes(this.NumberOfGames), swapBytes);
            fs.Write(BitConverter.GetBytes(this.NumberOfRecords), swapBytes);
            fs.Write(BitConverter.GetBytes(this.GameYear), swapBytes);
            fs.WriteByte((byte)this.BoardSize);
            fs.WriteByte((byte)this.GameType);
            fs.WriteByte((byte)this.Depth);
        }
    }

    public class WTHORGameRecord
    {
        public string TornamentName { get; set; }
        public string BlackPlayerName { get; set; }
        public string WhitePlayerName { get; set; }
        public int BlackDiscCount { get; set; }
        public int BestBlackDiscCount { get; set; }
        public ReadOnlyCollection<Move> MoveRecord { get; set; }

        public WTHORGameRecord(string tornamentName, string blackPlayerName, string whitePlayerName,
                               int blackDiscCount, int bestBlackDiscCount, List<Move> moveRecord)
        {
            this.TornamentName = tornamentName;
            this.BlackPlayerName = blackPlayerName;
            this.WhitePlayerName = whitePlayerName;
            this.BlackDiscCount = blackDiscCount;
            this.BestBlackDiscCount = bestBlackDiscCount;
            this.MoveRecord = new ReadOnlyCollection<Move>(moveRecord);
        }
    }

    public class WTHORFile
    {
        const string CHAR_ENCODING = "ISO-8859-1";
        const int PLAYER_NAME_SIZE = 20;
        const int TORNAMENT_NAME_SIZE = 26;
        const int GAME_INFO_SIZE = 68;

        public WTHORHeader JouHeader { get; }
        public WTHORHeader TrnHeader { get; }
        public WTHORHeader WtbHeader { get; }

        public ReadOnlyCollection<string> Players { get; }
        public ReadOnlyCollection<string> Tornaments { get; }
        public ReadOnlyCollection<WTHORGameRecord> GameRecords { get; }

        public WTHORFile(string jouPath, string trnPath, string wtbPath)
        {
            this.JouHeader = new WTHORHeader(jouPath);
            this.TrnHeader = new WTHORHeader(trnPath);
            this.WtbHeader = new WTHORHeader(wtbPath);
            LoadPlayersAndTornaments(jouPath, trnPath, out string[] players, out string[] tornaments);
            this.Players = new ReadOnlyCollection<string>(players);
            this.Tornaments = new ReadOnlyCollection<string>(tornaments);
            this.GameRecords = new ReadOnlyCollection<WTHORGameRecord>(LoadGameRecords(wtbPath));
        }

        public WTHORFile(WTHORHeader jouHeader, WTHORHeader trnHeader, WTHORHeader wtbHeader, string[] players, string[] tornaments, WTHORGameRecord[] gameRecords)
        {
            this.JouHeader = jouHeader;
            this.TrnHeader = trnHeader;
            this.WtbHeader = wtbHeader;
            this.Players = new ReadOnlyCollection<string>((string[])players.Clone());
            this.Tornaments = new ReadOnlyCollection<string>((string[])tornaments.Clone());
            this.GameRecords = new ReadOnlyCollection<WTHORGameRecord>((WTHORGameRecord[])gameRecords.Clone());
        }

        public void SaveToFiles(string jouFilePath, string trnFilePath, string wtbFilePath)
        {
            this.JouHeader.WriteToFile(jouFilePath);
            this.TrnHeader.WriteToFile(trnFilePath);
            this.WtbHeader.WriteToFile(wtbFilePath);

            var swapBytes = !BitConverter.IsLittleEndian;
            using var jouFs = new FileStream(jouFilePath, FileMode.OpenOrCreate, FileAccess.Write);
            using var trnFs = new FileStream(trnFilePath, FileMode.OpenOrCreate, FileAccess.Write);
            jouFs.Seek(WTHORHeader.SIZE, SeekOrigin.Begin);
            trnFs.Seek(WTHORHeader.SIZE, SeekOrigin.Begin);
            var encoding = Encoding.GetEncoding(CHAR_ENCODING);
            for (var i = 0; i < this.JouHeader.NumberOfRecords; i++)
            {
                var buffer = encoding.GetBytes(this.Players[i]);
                if (buffer.Length > PLAYER_NAME_SIZE)
                    jouFs.Write(buffer.AsSpan(0, PLAYER_NAME_SIZE), swapBytes);
                else
                    jouFs.Write(buffer, swapBytes);
            }

            for (var i = 0; i < this.TrnHeader.NumberOfRecords; i++)
            {
                var buffer = encoding.GetBytes(this.Tornaments[i]);
                if (buffer.Length > TORNAMENT_NAME_SIZE)
                    trnFs.Write(buffer.AsSpan(0, TORNAMENT_NAME_SIZE), swapBytes);
                else
                    trnFs.Write(buffer, swapBytes);
            }

            using var wtbFs = new FileStream(wtbFilePath, FileMode.OpenOrCreate, FileAccess.Write);
            wtbFs.Seek(WTHORHeader.SIZE, SeekOrigin.Begin);
            foreach (var gameRecord in this.GameRecords)
            {
                wtbFs.Write(BitConverter.GetBytes(Tornaments.IndexOf(gameRecord.TornamentName)));
                wtbFs.Write(BitConverter.GetBytes(Players.IndexOf(gameRecord.BlackPlayerName)));
                wtbFs.Write(BitConverter.GetBytes(Players.IndexOf(gameRecord.WhitePlayerName)));
                wtbFs.WriteByte((byte)gameRecord.BlackDiscCount);
                wtbFs.WriteByte((byte)gameRecord.BestBlackDiscCount);

                foreach (var move in gameRecord.MoveRecord)
                {
                    if (move.Coord == BoardCoordinate.Pass)
                        continue;
                    var x = (byte)((int)move.Coord % Constants.BOARD_SIZE + 1);
                    var y = (byte)((int)move.Coord / Constants.BOARD_SIZE + 1);
                    wtbFs.WriteByte((byte)(x * 10 + y));
                }
            }
        }

        void LoadPlayersAndTornaments(string jouPath, string trnPath, out string[] players, out string[] tornaments)
        {
            var swapBytes = !BitConverter.IsLittleEndian;
            players = new string[this.JouHeader.NumberOfRecords];
            tornaments = new string[this.TrnHeader.NumberOfRecords];
            Span<byte> playerNameBytes = stackalloc byte[PLAYER_NAME_SIZE];
            Span<byte> tornamentNameBytes = stackalloc byte[TORNAMENT_NAME_SIZE];
            var encoding = Encoding.GetEncoding(CHAR_ENCODING);

            using var jouFs = new FileStream(jouPath, FileMode.Open, FileAccess.Read);
            jouFs.Seek(WTHORHeader.SIZE, SeekOrigin.Begin);
            for (var i = 0; i < this.JouHeader.NumberOfRecords; i++)
            {
                jouFs.Read(playerNameBytes, swapBytes);
                players[i] = encoding.GetString(playerNameBytes);
            }

            using var trnFs = new FileStream(trnPath, FileMode.Open, FileAccess.Read);
            trnFs.Seek(WTHORHeader.SIZE, SeekOrigin.Begin);
            for (var i = 0; i < this.TrnHeader.NumberOfRecords; i++)
            {
                trnFs.Read(tornamentNameBytes, swapBytes);
                tornaments[i] = encoding.GetString(tornamentNameBytes);
            }
        }

        WTHORGameRecord[] LoadGameRecords(string wtbPath)
        {
            var swapBytes = !BitConverter.IsLittleEndian;
            var gameRecords = new WTHORGameRecord[this.WtbHeader.NumberOfGames];
            using var wtbFs = new FileStream(wtbPath, FileMode.Open, FileAccess.Read);
            wtbFs.Seek(WTHORHeader.SIZE, SeekOrigin.Begin);
            Span<byte> buffer = stackalloc byte[2];
            for (var i = 0; i < gameRecords.Length; i++)
            {
                wtbFs.Read(buffer, swapBytes);
                var tornamentName = this.Tornaments[BitConverter.ToUInt16(buffer[..sizeof(ushort)])];

                wtbFs.Read(buffer, swapBytes);
                var blackPlayerName = this.Players[BitConverter.ToUInt16(buffer[..sizeof(ushort)])];

                wtbFs.Read(buffer, swapBytes);
                var whitePlayerName = this.Players[BitConverter.ToUInt16(buffer[..sizeof(ushort)])];

                gameRecords[i] = new WTHORGameRecord(tornamentName, blackPlayerName, whitePlayerName,
                                                     wtbFs.ReadByte(), wtbFs.ReadByte(), CreateMoveRecord(wtbPath, wtbFs));
            }

            return gameRecords;
        }

        static List<Move> CreateMoveRecord(string wtbPath, Stream stream)
        {
            const int MAX_MOVE_RECORD_SIZE = 60;
            var moveRecord = new List<Move>();
            var pos = new Position();

            var count = 0;
            while(stream.Position != stream.Length)
            {
                if (++count > MAX_MOVE_RECORD_SIZE)
                    break;

                var d = stream.ReadByte();
                if (d == 0)
                {
                    while (count++ < 60)
                        stream.ReadByte();
                    break;
                }

                if (pos.CanPass)     // because pass is not described in WTHOR file, check if current board can be passed
                                     // and if so add pass to move record.
                {
                    pos.Pass();
                    moveRecord.Add(new Move(BoardCoordinate.Pass));
                }

                Move move;
                try
                {
                    move = new Move(Reversi.Utils.Coordinate2DTo1D(d % 10 - 1, d / 10 - 1));
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new InvalidDataException($"Wtb file \"{wtbPath}\" contains an invalid move data: {d}");
                }

                if (!pos.IsLegalMoveAt(move.Coord))
                    throw new InvalidDataException($"Wtb file \"{wtbPath}\" contains an invalid move.");

                pos.GenerateMove(ref move);
                pos.Update(ref move);
                moveRecord.Add(move);
            }

            return moveRecord;
        }
    }
}
