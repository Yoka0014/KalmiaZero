using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

using KalmiaZero.Utils;

namespace KalmiaZero.Reversi
{
    public struct Position
    {
        public DiscColor SideToMove 
        {
            readonly get => this.sideToMove;

            set 
            {
                if (this.sideToMove != value)
                    Pass();
            } 
        }

        public DiscColor OpponentColor { get; private set; }

        public readonly int PlayerDiscCount => this.bitboard.PlayerDiscCount;
        public readonly int OpponentDiscCount => this.bitboard.OpponentDiscCount;
        public readonly int BlackDiscCount => this.sideToMove == DiscColor.Black ? this.PlayerDiscCount : this.OpponentDiscCount;
        public readonly int WhiteDiscCount => this.sideToMove == DiscColor.White ? this.PlayerDiscCount : this.OpponentDiscCount;
        public readonly int DiscCount => this.bitboard.DiscCount;
        public readonly int DiscDiff => (!Constants.ENABLE_WIN_BY_LOSING) ? this.PlayerDiscCount - this.OpponentDiscCount : this.OpponentDiscCount - this.PlayerDiscCount;
        public readonly int EmptySquareCount => this.bitboard.EmptySquareCount;
        public readonly bool CanPass => this.bitboard.ComputePlayerMobility() == 0UL && this.bitboard.ComputeOpponentMobility() != 0UL;
        public readonly bool IsGameOver => this.bitboard.ComputePlayerMobility() == 0UL &&  this.bitboard.ComputeOpponentMobility() == 0UL;

        Bitboard bitboard;
        DiscColor sideToMove;

        public Position()
        {
            this.bitboard = new Bitboard(1UL << (int)BoardCoordinate.E4 | 1UL << (int)BoardCoordinate.D5,
                                         1UL << (int)BoardCoordinate.D4 | 1UL << (int)BoardCoordinate.E5);
            this.sideToMove = DiscColor.Black;
            this.OpponentColor = DiscColor.White;
        }

        public Position(Bitboard bitboard, DiscColor sideToMove) => Init(bitboard, sideToMove);

        public readonly Bitboard GetBitboard() => this.bitboard;
        public void SetBitboard(Bitboard bitboard) { this.bitboard = bitboard; }

        public void Init(Bitboard bitboard, DiscColor sideToMove)
        {
            this.bitboard = bitboard;
            this.sideToMove = sideToMove;
            this.OpponentColor = Utils.ToOpponentColor(sideToMove);
        }

        public static bool operator==(Position left, Position right)
            => left.bitboard == right.bitboard && left.sideToMove == right.sideToMove;

        public static bool operator !=(Position left, Position right)
            => !(left == right);

        /// <summary>
        /// This method is only for supressing a warning.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override readonly bool Equals(object? obj)
            => (obj is Position pos) && (pos == this);

        /// <summary>
        /// This method is only for supressing a warning.
        /// </summary>
        /// <returns></returns>
        public override readonly int GetHashCode() => (int)ComputeHashCode();

        public void MirrorHorizontal() => this.bitboard.MirrorHorizontal();

        public void Rotate90Clockwise() => this.bitboard.Rotate90Clockwise();

        public readonly Player GetSquareOwnerAt(BoardCoordinate coord) => this.bitboard.GetSquareOwnerAt(coord);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly DiscColor GetSquareColorAt(BoardCoordinate coord)
        {
            var owner = this.bitboard.GetSquareOwnerAt(coord);
            if (owner == Player.Null)
                return DiscColor.Null;
            return owner == Player.First ? this.sideToMove : this.OpponentColor;
        }

        public readonly bool IsLegalMoveAt(BoardCoordinate coord)
            => coord == BoardCoordinate.Pass ? this.CanPass : (this.bitboard.ComputePlayerMobility() & (1UL << (int)coord)) != 0UL;

        public readonly int GetScore(DiscColor color) => (color == this.sideToMove) ? this.DiscDiff : -this.DiscDiff;

        public void Pass()
        {
            (this.sideToMove, this.OpponentColor) = (this.OpponentColor, this.sideToMove);
            this.bitboard.Swap();
        }

        public void PutPlayerDiscAt(BoardCoordinate coord) => this.bitboard.PutPlayerDiscAt(coord);
        public void PutOpponentDiscAt(BoardCoordinate coord) => this.bitboard.PutOpponentDiscAt(coord);

        public void PutDisc(DiscColor color, BoardCoordinate coord)
        {
            if (color == DiscColor.Null)
                return;

            if (this.sideToMove == color)
                PutPlayerDiscAt(coord);
            else
                PutOpponentDiscAt(coord);
        }

        public void RemoveDiscAt(BoardCoordinate coord) => this.bitboard.RemoveDiscAt(coord);

        public void RemoveAllDiscs()
        {
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
                RemoveDiscAt(coord);
        }

        /// <summary>
        /// Update position by a specified move without checking legality.
        /// Pass move is not supported. Use Position.Pass method instead.
        /// </summary>
        /// <param name="move"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref Move move)
        {
            (this.sideToMove, this.OpponentColor) = (this.OpponentColor, this.sideToMove);
            this.bitboard.Update(move.Coord, move.Flip);
        }

        /// <summary>
        /// Update position by a move at specified coordinate. 
        /// BoardCoordinate.Pass means pass move. 
        /// This method may fail failed if an illegal coordinate is specified.
        /// </summary>
        /// <param name="coord"></param>
        /// <returns>Whether the move at the specified coordinate is legal or not</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Update(BoardCoordinate coord)
        {
            if (!IsLegalMoveAt(coord))
                return false;

            if(coord == BoardCoordinate.Pass)
            {
                Pass();
                return true;
            }

            ulong flip = this.bitboard.ComputeFlippingDiscs(coord);
            var move = new Move(coord, flip);
            Update(ref move);
            return true;
        }

        /// <summary>
        /// Undo the specified move without checking its legality. 
        /// Thus, if an illegal move is specified, the contents of the position may lose consistency.
        /// </summary>
        /// <param name="move"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(ref Move move)
        {
            (this.sideToMove, this.OpponentColor) = (this.OpponentColor, this.sideToMove);
            this.bitboard.Undo(move.Coord, move.Flip);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int GetNextMoves(ref Span<Move> moves)
        {
            ulong mobility = this.bitboard.ComputePlayerMobility();
            var moveCount = 0;
            for (var coord = BitManipulations.FindFirstSet(mobility); mobility != 0; coord = BitManipulations.FindNextSet(ref mobility))
                moves[moveCount++].Coord = (BoardCoordinate)coord;
            return moveCount;
        }

        public readonly IEnumerable<BoardCoordinate> EnumerateNextMoves()
        {
            foreach (var coord in BitManipulations.EnumerateSets(this.bitboard.ComputePlayerMobility()))
                yield return (BoardCoordinate)coord;
        }

        public readonly int GetNumNextMoves() => BitOperations.PopCount(this.bitboard.ComputePlayerMobility());

        public readonly Move GenerateMove(BoardCoordinate coord) => new(coord, this.bitboard.ComputeFlippingDiscs(coord));

        public readonly void GenerateMove(ref Move move) => move.Flip = this.bitboard.ComputeFlippingDiscs(move.Coord);

        public readonly GameResult GetGameResult()
        {
            var diff = this.DiscDiff;
            if (diff == 0)
                return GameResult.Draw;
            return diff > 0 ? GameResult.Win : GameResult.Loss;
        }

        public readonly ulong ComputeHashCode() => this.bitboard.ComputeHashCode();

        public override readonly string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("  ");
            for (var i = 0; i < Constants.BOARD_SIZE; i++)
                sb.Append((char)('A' + i)).Append(' ');

            (ulong p, ulong o) = (this.bitboard.Player, this.bitboard.Opponent);
            var mask = 1UL;
            for(var y = 0; y < Constants.BOARD_SIZE; y++)
            {
                sb.Append('\n').Append(y + 1).Append(' ');
                for(var x = 0; x < Constants.BOARD_SIZE; x++)
                {
                    if ((p & mask) != 0UL)
                        sb.Append((this.sideToMove == DiscColor.Black) ? '*' : 'O').Append(' ');
                    else if ((o & mask) != 0UL)
                        sb.Append((this.OpponentColor == DiscColor.Black) ? '*' : 'O').Append(' ');
                    else
                        sb.Append("- ");
                    mask <<= 1;
                }
            }

            return sb.ToString();
        }
    }
}
