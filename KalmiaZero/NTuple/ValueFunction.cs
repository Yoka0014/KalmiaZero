using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KalmiaZero.NTuple
{
    public enum Endian
    {
        SameAsNative,
        DifferentFromNative,
        Unknown
    }

    public class ValueFunction
    {
        const string LABEL = "KalmiaZero";
        const string LABEL_INVERSED = "oreZaimlaK";
        const int LABEL_SIZE = 10;

        float[][][] weights = new float[2][][];     // weights[DiscColor][nTupleID][feature]

        readonly int[] POW_TABLE;

        public ValueFunction(NTupleInfo[] nTuples)
        {
            this.POW_TABLE = new int[nTuples.Max(x => x.Size)];
            InitPowTable();
            for(var color = 0; color < 2; color++)
            {
                var w = this.weights[color] = new float[nTuples.Length][];
                for (var nTupleID = 0; nTupleID < nTuples.Length; nTupleID++)
                    w[nTupleID] = new float[this.POW_TABLE[nTuples[nTupleID].Size]];
            }
        }

        void InitPowTable()
        {
            this.POW_TABLE[0] = 1;
            for (var i = 1; i < this.POW_TABLE.Length; i++)
                this.POW_TABLE[i] = this.POW_TABLE[i - 1] * PositionFeaturesConstantConfig.NUM_SQUARE_STATES;
        }
    }
}
