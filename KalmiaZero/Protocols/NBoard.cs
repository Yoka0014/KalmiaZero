using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Diagnostics;

using KalmiaZero.Utils;
using KalmiaZero.Engines;
using KalmiaZero.GameFormats;
using KalmiaZero.Reversi;

namespace KalmiaZero.Protocols
{
    using CommandHandler = Action<Tokenizer>;
    using MultiPV = List<MultiPVItem>;

    public class NBoard : IProtocol
    {
        public const int PROTOCOL_VERSION = 2;

        const int TIMEOUT_MS = 10000;

        readonly TextReader cmdIn;
        readonly TextWriter cmdOut, errOut;
        Dictionary<string, CommandHandler> commands = new();

        Engine? engine;
        StreamWriter logger;
        volatile int numHints;
        volatile bool engineIsThinking;
        volatile bool quitFlag;
        readonly object MAINLOOP_LOCK = new();

        public NBoard() : this(Console.In, Console.Out, Console.Error) { }

        public NBoard(TextReader cmdIn, TextWriter cmdOut, TextWriter errOut)
        {
            this.cmdIn = cmdIn; 
            this.cmdOut = cmdOut; 
            this.errOut = errOut;
            this.logger = new StreamWriter(Stream.Null);
            InitCommandHandlers();
        }

        void InitCommandHandlers()
        {
            this.commands["nboard"] = ExecuteNboardCommand;
            this.commands["set"] = ExecuteSetCommand;
            this.commands["move"] = ExecuteMoveCommand;
            this.commands["hint"] = ExecuteHintCommand;
            this.commands["go"] = ExecuteGoCommand;
            this.commands["ping"] = ExecutePingCommand;
            this.commands["learn"] = ExecuteLearnCommand;
            this.commands["analyze"] = ExecuteAnalyzeCommand;
            this.commands["quit"] = ExecuteQuitCommand;

            // set command items
            this.commands["depth"] = ExecuteSetDepthCommand;
            this.commands["game"] = ExecuteSetGameCommand;
            this.commands["contempt"] = ExecuteSetContemptCommand;
            this.commands["time"] = ExecuteSetTimeCommand;  // extension
            this.commands["option"] = ExecuteSetOptionCommand;  // extension
        }

        public void Mainloop(Engine? engine) => Mainloop(engine, null);

        public void Mainloop(Engine? engine, string? logFilePath)
        {
           if(engine is null)
                throw new ArgumentNullException(nameof(engine), "Specified engine was null.");

            try
            {
                if (!Monitor.TryEnter(MAINLOOP_LOCK))
                    throw new InvalidOperationException("Cannot execute multiple mainloop.");

                InitEngine(engine);
                this.quitFlag = false;
                this.logger = (logFilePath is not null) ? new StreamWriter(logFilePath) : new StreamWriter(Stream.Null);

                string cmdName;
                string? line;
                while (!this.quitFlag)
                {
                    line = this.cmdIn.ReadLine();
                    if (line is null)
                        break;

                    this.logger.WriteLine($"< {line}\n");

                    var tokenizer = new Tokenizer { Input = line };
                    cmdName = tokenizer.ReadNext();
                    if (!this.commands.TryGetValue(cmdName, out CommandHandler? handler))
                        Fail($"Unknown command: {cmdName}");
                    else
                        handler(tokenizer);
                }
            }
            finally
            {
                if(Monitor.IsEntered(MAINLOOP_LOCK))
                    Monitor.Exit(MAINLOOP_LOCK); 
            }
        }

        void InitEngine(Engine engine)
        {
            this.engine = engine;
            engine.ErrorMessageWasSent += (sender, msg) => Fail(msg);
            engine.ThinkInfoWasSent += (sender, thinkInfo) => SendNodeStats(thinkInfo);
            engine.MultiPVWereSent += (sender, multiPV) => SendHints(multiPV);
            engine.MoveWasSent += (sender, move) => SendMove(move);
            engine.AnalysisEnded += (sender, _) => { Succeed("status"); this.engineIsThinking = false; };
        }

        void Succeed(string responce)
        {
            this.cmdOut.WriteLine(responce);
            this.cmdOut.Flush();

            this.logger.WriteLine($"> {responce}");
            this.logger.Flush();
        }

        void Fail(string message)
        {
            this.errOut.WriteLine($"Error: {message}");
            this.errOut.Flush();

            this.logger.WriteLine($">! {message}");
            this.logger.Flush();
        }

        void SendNodeStats(ThinkInfo thinkInfo) 
        {
            if (!thinkInfo.NodeCount.HasValue)
                return;

            var sb = new StringBuilder();
            sb.Append("nodestats ").Append(thinkInfo.NodeCount.Value).Append(' ');

            if (thinkInfo.EllpasedMs.HasValue)
                sb.Append(thinkInfo.EllpasedMs.Value * 1.0e-3);

            Succeed(sb.ToString());
        }

        void SendHints(MultiPV multiPV)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < this.numHints && i < multiPV.Count; i++)
            {
                var pv = multiPV[i];

                sb.Append("search ");
                foreach (var move in pv.PrincipalVariation)
                {
                    if (move != BoardCoordinate.Pass)
                        sb.Append(move);
                    else
                        sb.Append("PA");
                }

                if (pv.EvalScore.HasValue)
                    sb.Append($" {pv.EvalScore.Value:f2}");
                else
                    sb.Append(' ').Append(0);

                sb.Append(" 0 ");

                if (pv.EvalScoreType == EvalScoreType.ExactWLD)
                    sb.Append("100%W");
                else if (pv.EvalScoreType == EvalScoreType.ExactDiscDiff)
                    sb.Append("100%");
                else
                    sb.Append(pv.Depth ?? 0);

                sb.Append('\n');
            }

            Succeed(sb.ToString());
        }

        void SendMove(EngineMove move)
        {
            var sb = new StringBuilder();
            sb.Append("=== ");

            if (move.Coord == BoardCoordinate.Pass)
                sb.Append("PA");
            else
                sb.Append(move.Coord);

            if(move.EvalScore.HasValue)
                sb.Append('/').Append(move.EvalScore.Value);

            if (move.EllapsedMs.HasValue)
                sb.Append('/').Append(move.EllapsedMs.Value);

            Succeed(sb.ToString());
            Succeed("status");
        }

        void ExecuteNboardCommand(Tokenizer tokenizer)
        {
            var token = tokenizer.ReadNext();
            if(!int.TryParse(token, out int version))
            {
                Fail("NBoard version must be an integer.");
                return;
            }

            if(version != PROTOCOL_VERSION)
            {
                Fail($"NBoard version {version} is not supported.");
                return;
            }

            this.engine?.Ready();

            Succeed($"set myname {this.engine?.Name}");
        }

        void ExecuteSetCommand(Tokenizer tokenizer)
        {
            var propertyName = tokenizer.ReadNext();

            if (!this.commands.TryGetValue(propertyName, out CommandHandler? handler))
            {
                Fail($"Unknown property: {propertyName}");
                return;
            }

            handler(tokenizer);
        }

        void ExecuteSetDepthCommand(Tokenizer tokenizer)
        {
            var token = tokenizer.ReadNext();
            if(!int.TryParse(token, out int depth))
            {
                Fail("Depth must be an integer.");
                return;
            }

            if (depth < 1 || depth > 60)
            {
                Fail("Depth must be within [1, 60].");
                return;
            }

            this.engine?.SetLevel(depth);
        }

        void ExecuteSetGameCommand(Tokenizer tokenizer)
        {
            Debug.Assert(engine is not null);

            if(this.engineIsThinking && !this.engine.StopThinking(TIMEOUT_MS))
            {
                Fail("Cannot suspend current thinking task.");
                return;
            }

            GGFReversiGame game;
            try
            {
                game = new GGFReversiGame(tokenizer.ReadToEnd());
            }
            catch (GGFParserException ex)
            {
                Fail($"Cannot parse GGF string. \nDetail: {ex.Message}\nStack trace:\n\t{ex.StackTrace}");
                return;
            }

            var pos = game.GetPosition();
            foreach(GGFMove move in game.Moves)
            {
                if (!pos.Update(move.Coord))
                {
                    Fail($"Specified moves contain an invalid move {move.Coord}.");
                    return;
                }
            }

            var currentPos = this.engine.GetPosition();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var num_moves = currentPos.GetNextMoves(ref moves);
            var updated = false;
            for(var i = 0; i < num_moves; i++)
            {
                ref var move = ref moves[i];   
                currentPos.CreateMove(ref move);
                currentPos.Update(ref move);

                if(currentPos == pos)
                {
                    this.engine.UpdatePosition(currentPos.OpponentColor, move.Coord);
                    updated = true;
                    break;
                }

                currentPos.Undo(ref move);
            }

            if (!updated)
                this.engine.InitPosition(ref pos);

            var times = new GameTimerOptions[] { game.BlackThinkingTime, game.WhiteThinkingTime };
            for(var color = DiscColor.Black; color <= DiscColor.White; color++)
            {
                var time = times[(int)color];
                if(time.MainTimeMs > 0)
                {
                    this.engine.SetMainTime(color, time.MainTimeMs);
                    this.engine.SetTimeIncrement(color, time.IncrementMs);
                }
            }
        }

        void ExecuteSetContemptCommand(Tokenizer tokenizer)
        {
            if(!int.TryParse(tokenizer.ReadNext(), out var contempt))
            {
                Fail("Contempt must be an integer.");
                return;
            }

            this.engine?.SetBookContempt(contempt);
        }

        void ExecuteSetTimeCommand(Tokenizer tokenizer)
        {
            // format: set time [color] main [int] inc [int] byoyomi [int]
            // main, inc, byoyomi can be omitted.

            var token = tokenizer.ReadNext().ToLower();
            DiscColor color;
            if (token == "b" || token == "black")
                color = DiscColor.Black;
            else if (token == "w" || token == "white")
                color = DiscColor.White;
            else
            {
                Fail("Specify a valid color.");
                return;
            }

            string timeStr;
            int timeMs;
            while (!tokenizer.IsEndOfString)
            {
                token = tokenizer.ReadNext().ToLower();
                timeStr = tokenizer.ReadNext().ToLower();

                switch (token)
                {
                    case "time":
                        if (!tryParseTime(timeStr, out timeMs))
                            return;
                        this.engine?.SetMainTime(color, timeMs);
                        break;

                    case "inc":
                        if (!tryParseTime(timeStr, out timeMs))
                            return;
                        this.engine?.SetTimeIncrement(color, timeMs);
                        break;

                    case "byoyomi":
                        if (!tryParseTime(timeStr, out timeMs))
                            return;
                        this.engine?.SetByoyomi(color, timeMs);
                        break;

                    default:
                        Fail($"\"{token}\" is an invalid token.");
                        return;
                }
            }

            bool tryParseTime(string str, out int timeMs)
            {
                if(!int.TryParse(str, out timeMs))
                {
                    Fail($"Time must be an integer.");
                    return false;
                }
                return true;
            }
        }

        void ExecuteSetOptionCommand(Tokenizer tokenizer)
        {
            // format: set option [option_name] [value]

            if (tokenizer.IsEndOfString)
            {
                Fail($"Specify name and value of option.");
                return;
            }

            var optionName = tokenizer.ReadNext();
            if(tokenizer.IsEndOfString) 
            {
                Fail($"Specify a value.");
                return;
            }

            this.engine?.SetOption(optionName, tokenizer.ReadNext());
        }

        void ExecuteMoveCommand(Tokenizer tokenizer)
        {
            Debug.Assert(this.engine is not null);

            if(this.engineIsThinking && !this.engine.StopThinking(TIMEOUT_MS))
            {
                Fail("Cannot suspend current thinking task.");
                return;
            }

            var moveStr = tokenizer.ReadTo('/').Trim().ToLower();
            var move = (moveStr == "pa") ? BoardCoordinate.Pass : Reversi.Utils.ParseCoordinate(moveStr);

            if(move == BoardCoordinate.Null)
            {
                Fail($"Specify a valid move coordinate.");
                return;
            }

            if(!this.engine.UpdatePosition(this.engine.SideToMove, move))
            {
                Fail($"Move {move} is invalid.");
                return;
            }

            // ignores eval and time. 
        }

        void ExecuteHintCommand(Tokenizer tokenizer)
        {
            if(!int.TryParse(tokenizer.ReadNext(), out var numHints))
            {
                Fail("The number of hints must be an integer.");
                return;
            }

            if(numHints < 1)
            {
                Fail("The number of hints must be more than or equal 1.");
                return;
            }

            this.numHints = numHints;

            Succeed("status Analysing");
            this.engineIsThinking = true;
            this.engine?.Analyze(this.numHints);
        }

        void ExecuteGoCommand(Tokenizer tokenizer)
        {
            Succeed("status Thinking");
            this.engineIsThinking = true;
            this.engine?.Go(false);
        }

        void ExecutePingCommand(Tokenizer tokenizer)
        {
            Debug.Assert(this.engine is not null);

            if (this.engineIsThinking && !this.engine.StopThinking(TIMEOUT_MS))
            {
                Fail("Cannot suspend current thinking task.");
                return;
            }

            if (!int.TryParse(tokenizer.ReadNext(), out var n))
                n = 0;

            Succeed($"pong {n}");
        }

        void ExecuteLearnCommand(Tokenizer tokenizer)
        {
            this.engine?.AddCurrentGameToBook();
            Succeed("learned");
        }

        void ExecuteAnalyzeCommand(Tokenizer tokenizer)
        {
            Fail("Not supported.");
        }

        void ExecuteQuitCommand(Tokenizer tokenizer)
        {
            if (this.quitFlag)
                return;

            ExecutePingCommand(new Tokenizer());
            this.quitFlag = true;
        }
    }
}
