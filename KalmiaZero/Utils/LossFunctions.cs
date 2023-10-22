using System.Numerics;

namespace KalmiaZero.Utils
{
    public static class LossFunctions
    {
        public static T BinaryCrossEntropy<T>(T y, T t) where T : struct, IFloatingPointIeee754<T>
            => -(t * T.Log(y + T.Epsilon)
            + (T.One - t) * T.Log(T.One - y + T.Epsilon));

        public static T SquaredError<T>(T y, T t) where T : struct, IFloatingPointIeee754<T>
        {
            var delta = t - y;
            return (T.One / (T.One + T.One)) * delta * delta;
        }
    }
}
