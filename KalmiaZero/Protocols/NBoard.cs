using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Utils;
using KalmiaZero.Engines;
using System.IO;

namespace KalmiaZero.Protocols
{
    using CommandHandler = Action<Tokenizer>;

    public class NBoard : IProtocol
    {
        public const int PROTOCOL_VERSION = 2;

        const int TIMEOUT_MS = 10000;

        TextReader cmdIn;
        TextWriter cmdOut, errOut;
        Dictionary<string, CommandHandler> commands = new();

        Engine? engine;
        StreamWriter logger;
        volatile int numHints;
        volatile bool engineIsThinking;
        volatile bool quitFlag;

        public NBoard() : this(Console.In, Console.Out, Console.Error) { }

        public NBoard(TextReader cmdIn, TextWriter cmdOut, TextWriter errOut)
        {
            this.cmdIn = cmdIn; 
            this.cmdOut = cmdOut; 
            this.errOut = errOut;
            this.logger = new StreamWriter(Stream.Null);
        }

        public void Mainloop(Engine engine, string logFilePath)
        {
           
        }

        void InitCommandHandlers()
        {
            
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

        void ExecuteNboardCommand(Tokenizer tokenizer)
        {
            var str = tokenizer.ReadNext();
            if(!int.TryParse(str, out int version))
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
            var str = tokenizer.ReadNext();
            if(!int.TryParse(str, out int depth))
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
            
        }

        void ExecuteSetContemptCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteSetTimeCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteSetOptionCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteMoveCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteHintCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteGoCommand(Tokenizer tokenizer)
        {

        }

        void ExecutePingCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteLearnCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteAnalyzeCommand(Tokenizer tokenizer)
        {

        }

        void ExecuteQuitCommand(Tokenizer tokenizer)
        {

        }
    }
}
