using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using KalmiaZero.Reversi;
using KalmiaZero.Utils;

namespace KalmiaZero.GameFormats
{
    public class GGFParserException : Exception
    {
        public GGFParserException(string message) : base(message) { }
    }

    public class GGFGameResult
    {
        public double? FirstPlayerScore { get; set; }
        public bool IsResigned { get; set; }
        public bool IsTimeout { get; set; }

        /// <summary>
        /// Mutual agreement.
        /// </summary>
        public bool IsMutual { get; set; }

        public bool IsUnknown => !this.FirstPlayerScore.HasValue;
    }

    public class GGFMove
    {
        public DiscColor Color { get; set; }
        public BoardCoordinate Coord { get; set; }
        public double? EvalScore { get; set; }
        public double? Time { get; set; }
    }

    public class GGFReversiGame
    {
        const string GAME_START_DELIMITER = "(;";
        const string GAME_END_DELIMITER = ";)";
        const char PROPERTY_START_DELIMITER = '[';
        const char PROPERTY_END_DELIMITER = ']';
        const string GAME_TYPE = "othello";

        public string Place { get; set; } = string.Empty;

        /// <summary>
        /// In the GGF specification (see the following URL) the format of date is "year.month.day_hour:minute:second.zone".<br/>
        /// However, some GGF game records use UNIX time or something, so date is loaded as a text.<br/>
        /// <see href="https://skatgame.net/mburo/ggsa/ggf"/> 
        /// </summary>
        public string Date { get; set; } = string.Empty;

        public string BlackPlayerName { get; set; } = string.Empty;
        public string WhitePlayerName { get; set; } = string.Empty;
        public double BlackPlayerRating { get; set; }
        public double WhitePlayerRating { get; set; }
        public GameTimerOptions BlackThinkingTime { get; set; } = new();
        public GameTimerOptions WhiteThinkingTime { get; set; } = new();
        public GGFGameResult? GameResult { get; set; }
        public List<GGFMove> Moves { get; } = new();

        Position position;

        public GGFReversiGame(string ggfStr)
        {
            var tokenizer = new Tokenizer { Input = ggfStr };

            if (!FindGameStartDelimiter(tokenizer))
                throw new GGFParserException($"GGF must start with \"{GAME_START_DELIMITER}\"");

            ParseProperties(tokenizer);
        }

        public Position GetPosition() => this.position;

        void ParseProperties(Tokenizer tokenizer)
        {
            var sb = new StringBuilder();

            char ch;
            while(!tokenizer.IsEndOfString)
            {
                ch = tokenizer.ReadNextChar();
                if(ch == GAME_END_DELIMITER[0])
                {
                    if (tokenizer.ReadNextChar() == GAME_END_DELIMITER[1])
                        return;
                    throw new GGFParserException($"Unexpected token \"{GAME_END_DELIMITER[0]}\". Maybe \"{GAME_END_DELIMITER}\"?");
                }

                if (ch >= 'A' && ch <= 'Z')
                {
                    sb.Clear();
                    sb.Append(ch);

                    while (!tokenizer.IsEndOfString)
                    {
                        ch = tokenizer.ReadNextChar();
                        if (ch == PROPERTY_START_DELIMITER)
                            break;

                        if (ch < 'A' || ch > 'Z')
                            throw new GGFParserException($"The property name contains invalid character \'{ch}\'");

                        sb.Append(ch);
                    }

                    if (tokenizer.IsEndOfString)
                        throw new GGFParserException($"GGF must end with \"{GAME_END_DELIMITER}\"");

                    ParseProperty(sb.ToString(), tokenizer);
                }
            }
        }

        void ParseProperty(string propertyName, Tokenizer tokenizer)
        {
            var value = tokenizer.ReadTo(PROPERTY_END_DELIMITER);

            switch (propertyName) 
            {
                case "GM":
                    var lvalue = value.ToLower();
                    if (lvalue != GAME_TYPE)
                        throw new GGFParserException($"Game \"{value}\" is not supported.");
                    return;

                case "PC":
                    this.Place = value;
                    return;

                case "DT":
                    this.Date = value;
                    return;

                case "PB":
                    this.BlackPlayerName = value;
                    return;

                case "PW":
                    this.WhitePlayerName = value;
                    return;

                case "RB":
                    if (!double.TryParse(value, out double blackRating))
                        throw new GGFParserException("The value of RB must be a real number.");
                    this.BlackPlayerRating = blackRating;
                    return;

                case "RW":
                    if (!double.TryParse(value, out double whiteRating))
                        throw new GGFParserException("The value of RB must be a real number.");
                    this.WhitePlayerRating = whiteRating;
                    return;

                case "TI":
                    this.BlackThinkingTime = ParseTime(value);
                    this.WhiteThinkingTime = (GameTimerOptions)this.BlackThinkingTime.Clone();
                    return;

                case "TB":
                    this.BlackThinkingTime = ParseTime(value);
                    return;

                case "TW":
                    this.WhiteThinkingTime = ParseTime(value);
                    return;

                case "RE":
                    this.GameResult = ParseResult(value);
                    return;

                case "BO":
                    this.position = ParsePosition(value);
                    return;

                case "B":
                    var bmove = ParseMove(DiscColor.Black, value);
                    this.Moves.Add(bmove);
                    return;

                case "W":
                    var wmove = ParseMove(DiscColor.White, value);
                    this.Moves.Add(wmove);
                    return;
            }
        }

        static bool FindGameStartDelimiter(Tokenizer tokenizer)
        {
            while (!tokenizer.IsEndOfString)
                if (tokenizer.ReadNextChar() == GAME_START_DELIMITER[0] && tokenizer.ReadNextChar() == GAME_START_DELIMITER[1])
                    return true;
            return false;
        }

        static GameTimerOptions ParseTime(string timeStr)
        {
            var tokenizer = new Tokenizer { Input = timeStr };
            var times = new List<string>();
            string s;
            while ((s = tokenizer.ReadTo('/')) != string.Empty)
                times.Add(s);

            if (times.Count > 3)
                throw new GGFParserException("The representation of time was invalid. Valid format is \"[main_time]/[increment_time]/[extension_time]\".");

            var timesMs = new List<int>();
            for(var i = 0; i < times.Count; i++)
            {
                var clockTime = new List<string>();
                var clockTokenizer = new Tokenizer { Input = times[i] };
                while ((s = clockTokenizer.ReadTo(':')) != string.Empty)
                {
                    var idx = s.IndexOf(',');   // According to the GGF specification, some options can be specified after a colon,
                                                // but there are few reversi games that use them, so a colon is ignored. 
                    if(idx != -1)
                        s = s[..idx];
                    clockTime.Add(s);
                }

                if (clockTime.Count > 3)
                    throw new GGFParserException("The representation of clock time was invalid. Valid format is \"[hours]:[minutes]:[seconds]\".");

                var timeMs = 0;
                var unit = 1000;
                foreach(var t in clockTime.Reverse<string>())
                {
                    if (!int.TryParse(t, out int v))
                        throw new GGFParserException("The value of hour, minute and second must be an integer.");

                    timeMs += v * unit;
                    unit *= 60;
                }
                timesMs.Add(timeMs);
            }

            var options = new GameTimerOptions();
            if (timesMs.Count > 0)
                options.MainTimeMs = timesMs[0];

            if(timesMs.Count > 1)
                options.IncrementMs = timesMs[1];

            return options;
        }

        static GGFGameResult? ParseResult(string resStr)
        {
            var result = new GGFGameResult();
            var tokenizer = new Tokenizer { Input = resStr };
            tokenizer.ReadTo(':');

            if (!double.TryParse(tokenizer.Input, out double score))
                return null;

            result.FirstPlayerScore = score;
            switch (tokenizer.ReadNextChar())
            {
                case 'r':
                    result.IsResigned = true;
                    break;

                case 't':
                    result.IsTimeout = true;
                    break;

                case 's':
                    result.IsMutual = true;
                    break;
            }

            return result;
        }

        static Position ParsePosition(string posStr)
        {
            var tokenizer = new Tokenizer { Input = posStr };
            if(tokenizer.ReadNextChar() != '8')
                throw new GGFParserException("Only 8x8 board is supported.");

            var pos = new Position(new Bitboard(0UL, 0UL), DiscColor.Black);
            var coord = BoardCoordinate.A1;
            char ch;
            while(coord <= BoardCoordinate.H8 && !tokenizer.IsEndOfString)
            {
                ch = tokenizer.ReadNextChar();
                switch (ch)
                {
                    case '*':
                        pos.PutPlayerDiscAt(coord++);
                        break;

                    case 'O':
                        pos.PutOpponentDiscAt(coord++);
                        break;

                    case '-':
                        coord++;
                        break;

                    default:
                        throw new GGFParserException($"Unexpected symbol \'{ch}\'");
                }
            }

            if (tokenizer.IsEndOfString)
                throw new GGFParserException("Missing side to move.");

            ch = tokenizer.ReadNextChar();
            if (ch == '*')
                pos.SideToMove = DiscColor.Black;
            else if (ch == 'O')
                pos.SideToMove = DiscColor.White;
            else
                throw new GGFParserException($"Unexpected symbol \'{ch}\'");

            return pos;
        }

        static GGFMove ParseMove(DiscColor color, string moveStr)
        {
            var move = new GGFMove { Color = color };
            var moveInfo = new List<string>();
            var tokenizer = new Tokenizer { Input = moveStr };

            string s;
            while ((s = tokenizer.ReadTo('/')) != string.Empty)
                moveInfo.Add(s.ToLower());

            if (moveInfo.Count == 0)
                throw new GGFParserException("Coordinate was empty.");

            move.Coord = (moveInfo[0] == "pa") ? BoardCoordinate.Pass : Reversi.Utils.ParseCoordinate(moveInfo[0]);
            if (move.Coord == BoardCoordinate.Null)
                throw new GGFParserException($"Cannot parse \"{moveInfo[0]}\" as a coordinate.");

            if (moveInfo.Count > 1)
                if (double.TryParse(moveInfo[1], out double score))
                    move.EvalScore = score;

            if (moveInfo.Count > 2)
                if (double.TryParse(moveInfo[2], out double time))
                    move.Time = time;

            return move;
        }
    }
}
