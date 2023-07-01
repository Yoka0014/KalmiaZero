using System;
using System.Collections.Generic;

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
        public readonly int DiscDiff => this.PlayerDiscCount - this.OpponentDiscCount;
        public readonly int EmptySquareCount => this.bitboard.EmptySquareCount;
        public readonly bool CanPass => this.bitboard.ComputePlayerMobility() == 0UL && this.bitboard.ComputeOpponentMobility() != 0UL;
        public readonly bool IsGameOver => this.bitboard.ComputePlayerMobility() == 0UL &&  this.bitboard.ComputeOpponentMobility() == 0UL;

        Bitboard bitboard;
        DiscColor sideToMove;

        public Position()
        {
            this.bitboard = new Bitboard(Utils.COORD_TO_BIT[(int)BoardCoordinate.E4] | Utils.COORD_TO_BIT[(int)BoardCoordinate.D5],
                                         Utils.COORD_TO_BIT[(int)BoardCoordinate.D4] | Utils.COORD_TO_BIT[(int)BoardCoordinate.E5]);
            this.sideToMove = DiscColor.Black;
            this.OpponentColor = DiscColor.White;
        }

        public Position(Bitboard bitboard, DiscColor sideToMove)
        {
            this.bitboard = bitboard;
            this.sideToMove = sideToMove;
            this.OpponentColor = Utils.ToOpponentColor(sideToMove);
        }

        public readonly Bitboard GetBitboard() => this.bitboard;
        public void SetBitboard(Bitboard bitboard) { this.bitboard = bitboard; }

        public static bool operator==(Position left, Position right)
            => left.bitboard == right.bitboard && left.sideToMove == right.sideToMove;

        public static bool operator !=(Position left, Position right)
            => !(left == right);

        public readonly bool Equals(Position pos)
            => this.sideToMove == pos.sideToMove && this.bitboard == pos.bitboard;

        public readonly Player GetSquareOwnerAt(BoardCoordinate coord) => this.bitboard.GetSquareOwnerAt(coord);

        public readonly DiscColor GetSquareColorAt(BoardCoordinate coord)
        {
            var owner = this.bitboard.GetSquareOwnerAt(coord);
            if (owner == Player.Null)
                return DiscColor.Null;
            return owner == Player.First ? this.sideToMove : this.OpponentColor;
        }

        public readonly bool IsLegalMoveAt(BoardCoordinate coord)
            => coord == BoardCoordinate.Pass ? this.CanPass : (this.bitboard.ComputePlayerMobility() & Utils.COORD_TO_BIT[(int)coord]) != 0UL;

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

        /// <summary>
        /// Update position by a specified move without checking legality.
        /// Pass move is not supported. Use Position.Pass method instead.
        /// </summary>
        /// <param name="move"></param>
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
        public void Undo(ref Move move)
        {
            (this.sideToMove, this.OpponentColor) = (this.OpponentColor, this.sideToMove);
            this.bitboard.Undo(move.Coord, move.Flip);
        }

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

        public readonly Move CreateMove(BoardCoordinate coord) => new(coord, this.bitboard.ComputeFlippingDiscs(coord));

        public readonly void CreateMove(ref Move move) => move.Flip = this.bitboard.ComputeFlippingDiscs(move.Coord);

        public readonly GameResult GetGameResult()
        {
            var diff = this.DiscDiff;
            if (diff == 0)
                return GameResult.Draw;
            return diff > 0 ? GameResult.Win : GameResult.Loss;
        }
    }
}
