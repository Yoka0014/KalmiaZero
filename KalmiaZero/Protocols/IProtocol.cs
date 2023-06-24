using KalmiaZero.Engines;

namespace KalmiaZero.Protocols
{
    public interface IProtocol
    {
        void Mainloop(Engine engine, string logFilePath);
        void Mainloop(Engine engine) => Mainloop(engine, string.Empty);
    }
}
