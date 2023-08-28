﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using KalmiaZero.Reversi;

namespace KalmiaZero.NTuple
{
    public readonly struct NTupleInfo
    {
        /// <summary>
        /// Coordinates that compose N-Tuple containing symmetric expansions.
        /// </summary>
        public BoardCoordinate[][] Coordinates { get; }

        /// <summary>
        /// Mirrored order of coordinates that compose N-Tuple. The mirroring axis depends on the shape of N-Tuple.
        /// If there is no mirroring axis, MirrorTable is int[0].
        /// </summary>
        public int[] MirrorTable { get; }

        public int Size => this.Coordinates[0].Length;

        public NTupleInfo(int size)
        {
            this.Coordinates = ExpandTuple(InitTupleByRandomWalk(size));
            var coords = this.Coordinates[0];
            this.MirrorTable = (from coord in MirrorTuple(coords) select Array.IndexOf(coords, coord)).ToArray();
        }

        public NTupleInfo(BoardCoordinate[] coords)
        {
            this.Coordinates = ExpandTuple(coords);
            this.MirrorTable = (from coord in MirrorTuple(coords) select Array.IndexOf(coords, coord)).ToArray();
        }

        public NTupleInfo(NTupleInfo nTuple)
        {
            this.Coordinates = new BoardCoordinate[nTuple.Coordinates.Length][];
            for(var i = 0; i < this.Coordinates.Length; i++)
            {
                var srcTuple = nTuple.Coordinates[i];
                var destTuple = this.Coordinates[i] = new BoardCoordinate[srcTuple.Length];
                Buffer.BlockCopy(srcTuple, 0, destTuple, 0, sizeof(BoardCoordinate) * destTuple.Length);
            }

            this.MirrorTable = new int[nTuple.MirrorTable.Length];
            Buffer.BlockCopy(nTuple.MirrorTable, 0, this.MirrorTable, 0, sizeof(int) * this.MirrorTable.Length);
        }

        public byte[] ToBytes()
        {
            var size = BitConverter.GetBytes(this.Size);
            var buffer = new byte[sizeof(int) + this.Size];
            Buffer.BlockCopy(size, 0, buffer, 0, size.Length);
            for (var i = sizeof(int); i < buffer.Length; i++)
                buffer[i] = (byte)this.Coordinates[0][i - sizeof(int)];
            return buffer;
        }

        public override readonly string ToString()
        {
            var sb = new StringBuilder();
            foreach (var tuple in this.Coordinates)
            {
                sb.Append("  ");
                for (var i = 0; i < Constants.BOARD_SIZE; i++)
                    sb.Append((char)('A' + i)).Append(' ');

                for (var y = 0; y < Constants.BOARD_SIZE; y++)
                {
                    sb.Append('\n').Append(y + 1).Append(' ');
                    for (var x = 0; x < Constants.BOARD_SIZE; x++)
                    {
                        var idx = Array.IndexOf(tuple, Reversi.Utils.Coordinate2DTo1D(x, y));
                        if (idx != -1)
                            sb.Append(idx).Append(' ');
                        else
                            sb.Append("- ");
                    }
                }

                sb.Append("\n\n");
            }

            return sb.ToString();
        }

        static BoardCoordinate[] InitTupleByRandomWalk(int size)
        {
            var tuple = new List<BoardCoordinate> { (BoardCoordinate)Random.Shared.Next(Constants.NUM_SQUARES) };
            var adjCoords = Reversi.Utils.GetAdjacent8Squares(tuple[0]).ToArray().ToList();

            while (tuple.Count < size)
            {
                tuple.Add(adjCoords[Random.Shared.Next(adjCoords.Count)]);
                foreach (var adjCoord in Reversi.Utils.GetAdjacent8Squares(tuple[^1]))
                    adjCoords.Add(adjCoord);
                adjCoords.RemoveAll(tuple.Contains);
            }

            return tuple.Order().ToArray();
        }

        static BoardCoordinate[][] ExpandTuple(BoardCoordinate[] tuple)
        {
            var tuples = new List<BoardCoordinate[]>();
            var rotated = new BoardCoordinate[tuple.Length];
            Buffer.BlockCopy(tuple, 0, rotated, 0, sizeof(BoardCoordinate) * rotated.Length);

            rotate(rotated);

            for (var j = 0; j < rotated.Length; j++)
                rotated[j] = Reversi.Utils.TO_HORIZONTAL_MIRROR_COORD[(int)rotated[j]];

            rotate(rotated);

            void rotate(BoardCoordinate[] rotated)
            {
                for (var i = 0; i < 4; i++)
                {
                    var ordered = rotated.Order();
                    if (!tuples.Any(x => x.Order().SequenceEqual(ordered)))
                    {
                        var newTuple = new BoardCoordinate[tuple.Length];
                        Buffer.BlockCopy(rotated, 0, newTuple, 0, sizeof(BoardCoordinate) * rotated.Length);
                        tuples.Add(newTuple);
                    }

                    for (var j = 0; j < rotated.Length; j++)
                        rotated[j] = Reversi.Utils.TO_ROTATE90_COORD[(int)rotated[j]];
                }
            }

                return tuples.ToArray();
        }

        static BoardCoordinate[] MirrorTuple(BoardCoordinate[] tuple)
        {
            var mirrored = new BoardCoordinate[tuple.Length];

            if (mirror(Reversi.Utils.TO_HORIZONTAL_MIRROR_COORD))
                return mirrored;

            if (mirror(Reversi.Utils.TO_VERTICAL_MIRROR_COORD))
                return mirrored;

            if (mirror(Reversi.Utils.TO_DIAG_A1H8_MIRROR))
                return mirrored;

            if (mirror(Reversi.Utils.TO_DIAG_A8H1_MIRROR))
                return mirrored;

            return Array.Empty<BoardCoordinate>();

            bool mirror(ReadOnlySpan<BoardCoordinate> table)
            {
                for (var i = 0; i < mirrored.Length; i++)
                    mirrored[i] = table[(int)tuple[i]];

                return mirrored.Order().SequenceEqual(tuple.Order());
            }
        }
    }
}
