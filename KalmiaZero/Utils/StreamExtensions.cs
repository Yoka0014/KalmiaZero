using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KalmiaZero.Utils
{
    internal static class StreamExtensions
    {
        public static int Read(this Stream stream, byte[] buffer, int offset, int count, bool swapBytes) 
            => stream.Read(buffer.AsSpan(offset, count), swapBytes);

        public static int Read(this Stream stream, Span<byte> buffer, bool swapBytes)
        {
            var ret = stream.Read(buffer);
            if (swapBytes)
                SwapBytes(buffer);
            return ret;
        }

        static void SwapBytes(Span<byte> buffer)
        {
            for (var i = 0; i < buffer.Length / 2; i++)
                (buffer[i], buffer[buffer.Length - i - 1]) = (buffer[buffer.Length - i - 1], buffer[i]);
        }
    }
}
