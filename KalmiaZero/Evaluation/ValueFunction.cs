using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.NTuple;
using KalmiaZero.Utils;

namespace KalmiaZero.Evaluation
{
    /// <summary>
    /// Value function to evaluate a position.
    /// </summary>
    /// 
    /// <remarks>
    /// This class holds parameters(weights) as 16-bit integers to improve cache hit rate.
    /// This class is just for predicting not for training. 
    /// </remarks>
    /// 
    /// <seealso cref="ValueFunctionForTrain{WeightType}"/>
    public class ValueFunction
    {
        const float WEIGHT_SCALE = 1000.0f;

        public short[] Weights { get; private set; }
        public short Bias { get; set; }

        public ReadOnlySpan<int> DiscColorOffset => this.DISC_COLOR_OFFSET;
        public ReadOnlySpan<int> NTupleOffset => this.N_TUPLE_OFFSET;

        readonly int[] DISC_COLOR_OFFSET;
        readonly int[] N_TUPLE_OFFSET;

        ValueFunction(short[] weights, short bias, int[] discColorOffset, int[] nTupleOffset)
        {
            this.Weights = weights;
            this.Bias = bias;
            this.DISC_COLOR_OFFSET = discColorOffset;
            this.N_TUPLE_OFFSET = nTupleOffset;
        }

        public static ValueFunction CreateFrom<WeightType>(ValueFunctionForTrain<WeightType> src) where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
        {
            var weights = new short[src.Weights.Length];
            for (var i = 0; i < weights.Length; i++)
                weights[i] = short.CreateSaturating((float)(object)src.Weights[i] * WEIGHT_SCALE);
            return new ValueFunction(weights, short.CreateSaturating((float)(object)src.Bias * WEIGHT_SCALE), src.DiscColorOffset.ToArray(), src.NTupleOffset.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe float PredictLogit(PositionFeatureVector posFeatureVec)
        {
            var x = 0;
            fixed (int* discColorOffset = this.DISC_COLOR_OFFSET)
            fixed (short* weights = &this.Weights[discColorOffset[(int)posFeatureVec.SideToMove]])
            fixed (Feature* features = posFeatureVec.Features)
            {
                for (var nTupleID = 0; nTupleID < this.N_TUPLE_OFFSET.Length; nTupleID++)
                {
                    var w = weights + this.N_TUPLE_OFFSET[nTupleID];
                    ref Feature feature = ref features[nTupleID];
                    for (var i = 0; i < feature.Length; i++)
                        x += w[feature[i]];
                }
            }

            return (x + this.Bias) / WEIGHT_SCALE;
        }

        public float Predict(PositionFeatureVector posFeatureVec) => FastMath.Exp(PredictLogit(posFeatureVec)); 
    }
}
