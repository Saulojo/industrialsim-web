using System.Globalization;

namespace HartPoc.Server;

/// <summary>Log da prova: cada linha com data e hora, no console e num arquivo por execução.</summary>
public sealed class FrameLog : IDisposable
{
    private readonly Lock _gate = new();
    private readonly StreamWriter _file;

    public FrameLog(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"hart-poc-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        _file = new StreamWriter(FilePath, append: false) { AutoFlush = true };
    }

    public string FilePath { get; }

    public void Write(string line)
    {
        var stamped = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}");
        lock (_gate)
        {
            _file.WriteLine(stamped);
            Console.WriteLine(stamped);
        }
    }

    public void Dispose() => _file.Dispose();
}
