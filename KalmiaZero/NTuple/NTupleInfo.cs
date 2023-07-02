using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
