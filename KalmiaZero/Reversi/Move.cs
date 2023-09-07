using System;

namespace KalmiaZero.Reversi
{
    public struct Move
    {
        public static ref readonly Move Pass => ref PASS;
        static readonly Move PASS = new (BoardCoordinate.Pass);

        public BoardCoordinate Coord { get; set; }
        public ulong Flip { get; set; }

        public Move() : this(BoardCoordinate.Null, 0UL) { }
        public Move(BoardCoordinate coord) : this(coord, 0UL) { }

        public Move(BoardCoordinate coord, ulong flip)
        {
            this.Coord = coord;
            this.Flip = flip;
        }
    }

    public static class MoveSpanExtensions
    {
        public static bool Contains(this Span<Move> moves, BoardCoordinate coord)
        {
            foreach (var move in moves)
                if (move.Coord == coord)
                    return true;
            return false;
        }
    }
}
