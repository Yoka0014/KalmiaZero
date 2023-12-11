using System;
using System.Runtime.CompilerServices;

using KalmiaZero.NTuple;
using KalmiaZero.Reversi;
using KalmiaZero.Search.MCTS;

namespace KalmiaZero.Search
{
    internal struct GameInfo
    {
        public Position Position;
        public PositionFeatureVector FeatureVector;
        public readonly Span<Move> Moves => moves.AsSpan(0, numMoves);

        readonly Move[] moves = new Move[Constants.MAX_NUM_MOVES];
        int numMoves = 0;

        public GameInfo(NTupleGroup nTuples) : this(new Position(), nTuples) { }

        public GameInfo(Position pos, NTupleGroup nTuples)
        {
            this.Position = pos;
            InitMoves();
            this.FeatureVector = new PositionFeatureVector(nTuples);
            this.FeatureVector.Init(ref pos, this.Moves);
        }

        public readonly void CopyTo(ref GameInfo dest) 
        {
            dest.Position = this.Position;
            this.FeatureVector.CopyTo(dest.FeatureVector);
            this.Moves.CopyTo(dest.moves);
            dest.numMoves = this.numMoves;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref Move move)
        {
            this.Position.Update(ref move);
            InitMoves();
            this.FeatureVector.Update(ref move, this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref Move move, Edge[] edges)
        {
            this.Position.Update(ref move);
            InitMoves(edges);
            this.FeatureVector.Update(ref move, this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Pass()
        {
            this.Position.Pass();
            InitMoves();
            this.FeatureVector.Pass(this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Pass(Edge[] edges)
        {
            this.Position.Pass();
            InitMoves(edges);
            this.FeatureVector.Pass(this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(ref Move move)
        {
            this.Position.Undo(ref move);
            InitMoves();
            this.FeatureVector.Undo(ref move, this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(ref Move move, Span<Move> moves)
        {
            this.Position.Undo(ref move);
            moves.CopyTo(this.moves);
            this.numMoves = moves.Length;
            this.FeatureVector.Undo(ref move, this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(ref Move move, Edge[] edges)
        {
            this.Position.Undo(ref move);
            InitMoves(edges);
            this.FeatureVector.Undo(ref move, this.Moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitMoves()
        {
            var moves = this.moves.AsSpan();
            this.numMoves = this.Position.GetNextMoves(ref moves);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitMoves(Edge[] edges)
        {
            if (edges[0].Move.Coord == BoardCoordinate.Pass)
            {
                this.numMoves = 0;
                return;
            }

            for (var i = 0; i < edges.Length; i++)
                this.moves[i] = edges[i].Move;
            this.numMoves = edges.Length;
        }
    }
}
