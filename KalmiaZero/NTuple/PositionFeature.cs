using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Reversi;

namespace KalmiaZero.NTuple
{
    public struct NTupleInfo 
    {
        public int MinTupleSize { get; }
        public int MaxTupleSize { get; }
        public int NumTuples { get; }

        BoardCoordinate[][] tuples;

        public NTupleInfo(int minTupleSize, int maxTupleSize, int numTuples)
        {
            this.MinTupleSize = minTupleSize;
            this.MaxTupleSize = maxTupleSize;
            this.NumTuples = numTuples;
            this.tuples = new BoardCoordinate[numTuples][];
        }

        readonly BoardCoordinate[] InitNTupleByRandomWalk()
        {
            var size = Random.Shared.Next(this.MinTupleSize, this.MaxTupleSize + 1);
            var tuple = new List<BoardCoordinate>();
            var coord = (BoardCoordinate)Random.Shared.Next(0, Constants.NUM_SQUARES);
            while(tuple.Count < size)
            {

            }
        }
    }

    public class PositionFeature
    {
    }
}
