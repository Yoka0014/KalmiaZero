using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.NTuple
{
    public readonly struct NTupleInfo
    {
        public BoardCoordinate[][][] Tuples { get; }

        public NTupleInfo(int minTupleSize, int maxTupleSize, int numTuples)
        {
            this.Tuples = new BoardCoordinate[numTuples][][];

            for(var i = 0; i < this.Tuples.Length; i++)
            {
                BoardCoordinate[][]? tuple;
                do
                    tuple = InitNTupleByRandomWalk(minTupleSize, maxTupleSize);
                while (tuple is null);
                this.Tuples[i] = tuple;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            for(var ntupleID = 0; ntupleID < this.Tuples.Length; ntupleID++) 
            {
                sb.Append("NTupleID: ").Append(ntupleID).Append('\n');
                foreach(var tuple in this.Tuples[ntupleID]) 
                {
                    sb.Append("  ");
                    for (var i = 0; i < Constants.BOARD_SIZE; i++)
                        sb.Append((char)('A' + i)).Append(' ');

                    var count = 0;
                    for (var y = 0; y < Constants.BOARD_SIZE; y++)
                    {
                        sb.Append('\n').Append(y + 1).Append(' ');
                        for (var x = 0; x < Constants.BOARD_SIZE; x++)
                        {
                            if (tuple.Contains(Reversi.Utils.Coordinate2DTo1D(x, y)))
                                sb.Append(count++).Append(' ');
                            else
                                sb.Append("- ");
                        }
                    }

                    sb.Append("\n\n");
                }
            }

            return sb.ToString();
        }

        static BoardCoordinate[][]? InitNTupleByRandomWalk(int minSize, int maxSize)
        {
            var tuple = new List<BoardCoordinate>();
            var coord = (BoardCoordinate)Random.Shared.Next(Constants.NUM_SQUARES);
            var adjCoords = new List<BoardCoordinate>();
            while (tuple.Count < maxSize)
            {
                adjCoords.Clear();
                foreach(var adjCoord in Reversi.Utils.GetAdjacent8Squares(coord))
                {
                    if(!tuple.Contains(adjCoord))
                        adjCoords.Add(adjCoord);
                }

                if (adjCoords.Count == 0)
                    break;

                coord = adjCoords[Random.Shared.Next(adjCoords.Count)];
                tuple.Add(coord);
            }

            if (tuple.Count < minSize)
                return null;

            var tuples = new List<BoardCoordinate[]>();
            rotate(tuple.OrderBy(x => x).ToArray());
            rotate(tuple.Select(c => Reversi.Utils.TO_HORIZONTAL_MIRROR_COORD[(int)c]).ToArray());

            void rotate(BoardCoordinate[] rotated)
            {
                if (!tuples.Any(x => x.All(y => rotated.Contains(y))))
                    tuples.Add((BoardCoordinate[])rotated.Clone());

                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < rotated.Length; j++)
                        rotated[j] = Reversi.Utils.TO_ROTATE90_COORD[(int)rotated[j]];

                    if (!tuples.Any(x => x.All(y => rotated.Contains(y))))
                        tuples.Add((BoardCoordinate[])rotated.Clone());
                }
            }

            return tuples.ToArray();
        }
    }
}
