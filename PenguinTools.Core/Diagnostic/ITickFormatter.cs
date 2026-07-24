namespace PenguinTools.Core.Diagnostic;

public interface ITickFormatter
{
    TickPosition GetPosition(int tick);

    string FormatTick(int tick) => GetPosition(tick).ToString();
}
