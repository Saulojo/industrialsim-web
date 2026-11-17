using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Sessions.Recording;

/// <summary>
/// Arquivo de gravação de uma planta em execução. A thread do laço só enfileira as linhas; uma tarefa à parte
/// as escreve no disco, para que uma escrita lenta não atrase o passo da planta. O arquivo é descarregado
/// sempre que a fila esvazia, então pode ser lido (com compartilhamento) enquanto a planta roda.
/// </summary>
internal sealed class RecordingFile
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly ILogger _logger;
    private readonly Task _writer;

    public RecordingFile(string path, ILogger logger)
    {
        Path = path;
        _logger = logger;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _writer = Task.Run(() => WriteLoopAsync(writer));
    }

    public string Path { get; }

    /// <summary>Enfileira uma linha (sem o fim de linha). Chamado pela thread do laço.</summary>
    public void WriteLine(string line) => _lines.Writer.TryWrite(line);

    /// <summary>Fecha a fila e espera o arquivo ser escrito e fechado. Falso se não terminou no tempo dado.</summary>
    public bool Complete(TimeSpan timeout)
    {
        _lines.Writer.TryComplete();
        try
        {
            return _writer.Wait(timeout);
        }
        catch (AggregateException)
        {
            // A falha já foi registrada no log pela própria tarefa.
            return true;
        }
    }

    private async Task WriteLoopAsync(StreamWriter writer)
    {
        try
        {
            await using (writer.ConfigureAwait(false))
            {
                ChannelReader<string> reader = _lines.Reader;
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out string? line))
                    {
                        await writer.WriteAsync(line).ConfigureAwait(false);
                        await writer.WriteAsync('\n').ConfigureAwait(false);
                    }

                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SessionsLog.RecordingFailed(_logger, ex, Path);
            throw;
        }
    }
}
