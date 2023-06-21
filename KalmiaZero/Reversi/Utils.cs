using System;

namespace KalmiaZero.Reversi
{
    using static Constants;

    public static class Utils
    {
        public static ReadOnlySpan<ulong> COORD_TO_BIT => new ulong[NUM_SQUARES + 1]
        {
            1UL << 0, 1UL << 1, 1UL << 2, 1UL << 3, 1UL << 4, 1UL << 5, 1UL << 6, 1UL << 7,
            1UL << 8, 1UL << 9, 1UL << 10, 1UL << 11, 1UL << 12, 1UL << 13, 1UL << 14, 1UL << 15,
            1UL << 16, 1UL << 17, 1UL << 18, 1UL << 19, 1UL << 20, 1UL << 21, 1UL << 22, 1UL << 23,
            1UL << 24, 1UL << 25, 1UL << 26, 1UL << 27, 1UL << 28, 1UL << 29, 1UL << 30, 1UL << 31,
            1UL << 32, 1UL << 33, 1UL << 34, 1UL << 35, 1UL << 36, 1UL << 37, 1UL << 38, 1UL << 39,
            1UL << 40, 1UL << 41, 1UL << 42, 1UL << 43, 1UL << 44, 1UL << 45, 1UL << 46, 1UL << 47,
            1UL << 48, 1UL << 49, 1UL << 50, 1UL << 51, 1UL << 52, 1UL << 53, 1UL << 54, 1UL << 55,
            1UL << 56, 1UL << 57, 1UL << 58, 1UL << 59, 1UL << 60, 1UL << 61, 1UL << 62, 1UL << 63,
            0UL
        };

        public static BoardCoordinate Coordinate2DTo1D((int x, int y) coord) => Coordinate2DTo1D(coord.x, coord.y);

        public static BoardCoordinate Coordinate2DTo1D(int x, int y)
        {
            if (x < 0 || y < 0 || x >= BOARD_SIZE || y >= BOARD_SIZE)
                throw new ArgumentOutOfRangeException($"(x, y)" ,$"Coordinate (x, y) was out of range within [(0, 0), ({nameof(BOARD_SIZE)}, {nameof(BOARD_SIZE)})].");

            return (BoardCoordinate)(x + y * BOARD_SIZE); 
        }

        public static BoardCoordinate ParseCoordinate(string str)
        {
            var lstr = str.Trim().ToLower();

            if (lstr == "pass")
                return BoardCoordinate.Pass;

            if (lstr.Length < 2 || lstr[0] < 'a' || lstr[0] > ('a' + BOARD_SIZE - 1) || lstr[1] < '1' || lstr[1] > ('1' + BOARD_SIZE - 1))
                return BoardCoordinate.Null;

            return Coordinate2DTo1D(lstr[0] - 'a', lstr[1] - '1');
        }

        public static DiscColor ToOpponentColor(DiscColor color) => color ^ DiscColor.White;

        public static DiscColor ParseDiscColor(string str)
        {
            var lstr = str.Trim().ToLower();

            if (lstr == "b" || lstr == "black")
                return DiscColor.Black;

            else if (lstr == "w" || lstr == "white")
                return DiscColor.White;

            return DiscColor.Null;
        }

        public static Player ToOpponentPlayer(Player player) => player ^ Player.Second;

        public static GameResult ToOpponentGameResult(GameResult result) => (GameResult)(-(int)result);
    }
}
