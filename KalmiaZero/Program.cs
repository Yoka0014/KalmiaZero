using System;

using KalmiaZero.Engines;
using KalmiaZero.GameFormats;
using KalmiaZero.Protocols;

namespace KalmiaZero
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var nb = new NBoard();
            nb.Mainloop(new MCEngine(), "log.txt");
        }
    }
}