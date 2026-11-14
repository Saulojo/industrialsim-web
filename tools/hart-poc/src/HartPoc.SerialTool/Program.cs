// Ferramenta de teste da Etapa 2: fica na COM10 no lugar do PACTware.
//   hart-serial echo    --port COM10 [--count 200]            confere a ponte com o servidor em modo eco
//   hart-serial measure --port COM10 [--count 100] [--poll 0] [--command 0|1|3]   mede o tempo de resposta
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using HartPoc;

if (args.Length == 0 || args[0] is not ("echo" or "measure"))
{
    Console.Error.WriteLine("Uso: hart-serial echo|measure --port COM10 [--count N] [--poll N] [--command N] [--timeout ms]");
    return 2;
}

var portName = Option("--port", "COM10");
var count = int.Parse(Option("--count", args[0] == "echo" ? "200" : "100"), CultureInfo.InvariantCulture);
var timeoutMs = int.Parse(Option("--timeout", "2000"), CultureInfo.InvariantCulture);

// Parâmetros da camada física do HART: 1200 baud, 8 bits, paridade ímpar, 1 stop bit.
using var port = new SerialPort(portName, 1200, Parity.Odd, 8, StopBits.One)
{
    Handshake = Handshake.None,
    DtrEnable = true,
    RtsEnable = true,
    ReadTimeout = timeoutMs,
    WriteTimeout = timeoutMs,
};
port.Open();
Console.WriteLine($"{portName} aberta em 1200 8O1. CTS={Bit(port.CtsHolding)} DSR={Bit(port.DsrHolding)} DCD={Bit(port.CDHolding)}");

return args[0] == "echo" ? RunEcho() : RunMeasure();

string Option(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

static int Bit(bool value) => value ? 1 : 0;

int RunEcho()
{
    // Sequência fixa (semente 2), com tamanhos de 1 a 64 bytes e todos os valores de byte.
    var random = new Random(2);
    var failures = 0;
    long total = 0;
    for (var i = 0; i < count; i++)
    {
        var sent = new byte[1 + random.Next(64)];
        random.NextBytes(sent);
        port.DiscardInBuffer();
        port.Write(sent, 0, sent.Length);

        var received = new byte[sent.Length];
        var read = 0;
        try
        {
            while (read < received.Length)
            {
                read += port.Read(received, read, received.Length - read);
            }
        }
        catch (TimeoutException)
        {
            // Conta como falha abaixo.
        }

        total += sent.Length;
        if (read != sent.Length || !sent.AsSpan().SequenceEqual(received))
        {
            failures++;
            Console.WriteLine($"FALHA no bloco {i + 1}: enviado {Convert.ToHexString(sent)}, recebido {Convert.ToHexString(received.AsSpan(0, read))}");
        }
    }

    Console.WriteLine($"Eco: {count - failures} de {count} blocos iguais byte a byte ({total} bytes enviados).");
    return failures == 0 ? 0 : 1;
}

int RunMeasure()
{
    var pollingAddress = byte.Parse(Option("--poll", "0"), CultureInfo.InvariantCulture);
    var command = byte.Parse(Option("--command", "0"), CultureInfo.InvariantCulture);
    const int preambles = 5;

    // O comando 0 em endereço curto descobre o endereço único, exigido pelos outros comandos.
    var identity = Transact(HartFrame.ShortRequest(primaryMaster: true, pollingAddress, 0).Encode(preambles), out _, out _);
    if (identity is null)
    {
        Console.WriteLine("Sem resposta ao comando 0. O servidor e a página de ponte estão conectados?");
        return 1;
    }

    var id = identity.Data;
    Console.WriteLine($"Instrumento encontrado: resposta do comando 0 = {Convert.ToHexString(id)}");
    byte[] uniqueAddress = [(byte)(id[3] & 0x3F), id[4], id[11], id[12], id[13]];
    var request = (command == 0
        ? HartFrame.ShortRequest(primaryMaster: true, pollingAddress, 0)
        : HartFrame.LongRequest(primaryMaster: true, uniqueAddress, command)).Encode(preambles);

    var firstByteMs = new List<double>();
    var fullFrameMs = new List<double>();
    var failures = 0;
    for (var i = 0; i < count; i++)
    {
        if (Transact(request, out var first, out var full) is null)
        {
            failures++;
        }
        else
        {
            firstByteMs.Add(first);
            fullFrameMs.Add(full);
        }

        Thread.Sleep(100);
    }

    // 11 bits por byte na linha: start, 8 de dados, paridade e stop.
    var requestWireMs = request.Length * 11 * 1000.0 / 1200.0;
    Console.WriteLine($"Comando {command}: {count - failures} respostas válidas em {count} pedidos de {request.Length} bytes.");
    Console.WriteLine($"Tempo do pedido na linha a 1200 baud: {requestWireMs:F1} ms (o cronômetro parte do retorno da escrita na porta).");
    Report("Até o primeiro byte da resposta", firstByteMs);
    Report("Até o quadro completo da resposta", fullFrameMs);
    return failures == 0 ? 0 : 1;
}

HartFrame? Transact(byte[] request, out double firstByteMs, out double fullFrameMs)
{
    firstByteMs = fullFrameMs = double.NaN;
    var reader = new HartFrameReader(gapTimeoutMs: timeoutMs);
    port.DiscardInBuffer();
    port.Write(request, 0, request.Length);
    var start = Stopwatch.GetTimestamp();
    var buffer = new byte[1];
    try
    {
        for (; ; )
        {
            buffer[0] = (byte)port.ReadByte();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (double.IsNaN(firstByteMs))
            {
                firstByteMs = elapsed;
            }

            foreach (var received in reader.Feed(buffer, (long)elapsed))
            {
                // Com eco na linha, o próprio pedido volta antes: só a resposta (ACK) interessa.
                if (received.Frame.Type == HartFrameType.Response && received.CheckByteOk)
                {
                    fullFrameMs = elapsed;
                    return received.Frame;
                }

                firstByteMs = double.NaN;
            }
        }
    }
    catch (TimeoutException)
    {
        return null;
    }
}

static void Report(string title, List<double> samples)
{
    if (samples.Count == 0)
    {
        Console.WriteLine($"{title}: sem amostras.");
        return;
    }

    samples.Sort();
    double Percentile(double p) => samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(p * samples.Count) - 1)];
    Console.WriteLine(
        $"{title}: mín. {samples[0]:F1} ms, mediana {Percentile(0.5):F1} ms, p95 {Percentile(0.95):F1} ms, máx. {samples[^1]:F1} ms");
}
