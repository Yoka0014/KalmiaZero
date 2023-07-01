using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using KalmiaZero.Reversi;

namespace KalmiaZero.Engines
{
    using EngineOptions = Dictionary<string, EngineOption>;
    using MultiPV = List<MultiPVItem>;

    public enum EngineState
    {
        NotReady,
        Ready,
        Playing,
        GameOver
    }

    public enum EvalScoreType
    {
        WinRate,
        DiscDiff,
        ExactWLD,
        ExactDiscDiff,
        Other
    }

    public class ThinkInfo
    {
        public int? EllpasedMs { get; set; }
        public int? NodeCount { get; set; }
        public double? Nps { get; set; }
        public int? Depth { get; set; }
        public int? SelectedDepth { get; set; }
        public double? EvalScore { get; set; }
        public List<BoardCoordinate> PrincipalVariation { get; } = new();
    }

    public class MultiPVItem
    {
        public int? Depth { get; set; }
        public ulong? NodeCount { get; set; }
        public double? EvalScore { get; set; }
        public EvalScoreType EvalScoreType { get; set; } = EvalScoreType.Other;
        public GameResult ExactWLD { get; set; } = GameResult.NotOver;
        public int? ExactDiscDiff { get; set; }
        public List<BoardCoordinate> PrincipalVariation { get; } = new();
    }

    public class EngineMove
    {
        public BoardCoordinate Coord { get; set; } = BoardCoordinate.Null;
        public double? EvalScore { get; set; }
        public EvalScoreType EvalScoreType { get; set; }
        public int? EllapsedMs { get; set; }

        public EngineMove() { }

        public EngineMove(BoardCoordinate coord) : this(coord, null, EvalScoreType.Other, null) { }

        public EngineMove(BoardCoordinate coord, double? evalScore, EvalScoreType evalScoreType, int? ellapsedMs)
        {
            this.Coord = coord;
            this.EvalScore = evalScore;
            this.EvalScoreType = evalScoreType;
            this.EllapsedMs = ellapsedMs;
        }
    }

    public abstract class Engine
    {
        public EngineState State { get; private set; } = EngineState.NotReady;
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string Author { get; private set; }

        public DiscColor SideToMove => this.position.SideToMove;

        public EvalScoreType EvalScoreType { get; protected set; } = EvalScoreType.Other;

        public double EvalScoreMin { get; protected set; } = 0.0f;
        public double EvalScoreMax { get; protected set; } = 0.0f;

        public event EventHandler<string> MessageWasSent = delegate { };
        public event EventHandler<string> ErrorMessageWasSent = delegate { };
        public event EventHandler<ThinkInfo> ThinkInfoWasSent = delegate { };
        public event EventHandler<MultiPV> MultiPVWereSent = delegate { };
        public event EventHandler<EngineMove> MoveWasSent = delegate { };
        public event EventHandler AnalysisEnded = delegate { };

        protected EngineOptions Options = new();
        protected Position Position => this.position;
        protected ReadOnlyCollection<Move> MoveHistory => new(this.moveHistory);

        Position position;
        readonly List<Move> moveHistory = new();

        public Engine(string name, string version, string author)
        {
            this.Name = name;
            this.Version = version;
            this.Author = author;
        }

        public Position GetPosition() => this.position;

        public bool Ready()
        {
            if (!OnReady())
                return false;

            this.State = EngineState.Ready;
            return true;
        }

        public void StartGame()
        {
            OnStartGame();
            this.State = EngineState.Playing;
        }

        public void EndGame()
        {
            OnEndGame();
            this.State = EngineState.GameOver;
        }

        public void InitPosition(ref Position pos)
        {
            this.position = pos;
            this.moveHistory.Clear();
            OnInitializedPosition();
        }

        public void ClearPosition()
        {
            this.position = new Position();
            this.moveHistory.Clear();
            OnClearedPosition();
        }

        public bool UpdatePosition(DiscColor color, BoardCoordinate moveCoord)
        {
            if (color != this.position.SideToMove)
            {
                this.position.Pass();
                this.moveHistory.Add(Move.Pass);
            }

            if(moveCoord == BoardCoordinate.Pass)
            {
                this.position.Pass();
                this.moveHistory.Add(Move.Pass);
                OnUpdatedPosition();
                return true;
            }

            if (!this.position.IsLegalMoveAt(moveCoord))
                return false;

            var move = this.position.CreateMove(moveCoord);
            this.position.Update(ref move);
            this.moveHistory.Add(move);
            OnUpdatedPosition();
            return true;
        }

        public bool UndoPosition()
        {
            if (this.moveHistory.Count == 0)
                return false;

            var move = this.moveHistory.Last();
            this.position.Undo(ref move);
            OnUndidPosition();
            return true;
        }

        public bool SetOption(string name, string value)
        {
            if (!this.Options.TryGetValue(name, out EngineOption? option))
                return false;

            option.CurrentValueString = value;
            return true;
        }

        public IEnumerable<(string name, EngineOption option)> GetOptions()
        {
            foreach (var option in this.Options)
                yield return (option.Key, option.Value);
        }


        public abstract void Quit();
        public abstract void SetMainTime(DiscColor color, int mainTimeMs);
        public abstract void SetByoyomi(DiscColor color, int byoyomiMs);
        public abstract void SetByoyomiStones(DiscColor color, int byoyomiStones);
        public abstract void SetTimeIncrement(DiscColor color, int incMs);
        public abstract void SetLevel(int level);
        public abstract void SetBookContempt(int contempt);
        public abstract void AddCurrentGameToBook();
        public abstract void Go(bool ponder);
        public abstract void Analyze(int numMoves);
        public abstract bool StopThinking(int timeoutMs);

        protected void SendTextMessage(string msg) => this.MessageWasSent(this, msg);
        protected void SendErrorMessage(string errMsg) => this.ErrorMessageWasSent(this, errMsg);
        protected void SendThinkInfo(ThinkInfo thinkInfo) => this.ThinkInfoWasSent(this, thinkInfo);
        protected void SendMultiPV(MultiPV multiPV) => this.MultiPVWereSent(this, multiPV);
        protected void SendMove(EngineMove move) => this.MoveWasSent(this, move);

        protected abstract bool OnReady();
        protected abstract void OnStartGame();
        protected abstract void OnEndGame();
        protected abstract void OnInitializedPosition();
        protected abstract void OnClearedPosition();
        protected abstract void OnUpdatedPosition();
        protected abstract void OnUndidPosition();
    }
}
