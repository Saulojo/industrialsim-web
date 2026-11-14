using System.Net.WebSockets;

namespace HartPoc.Server;

/// <summary>
/// Servidor mínimo da prova de conceito da Etapa 2: serve a página de ponte e dois WebSockets de bytes
/// crus — <c>/ws/hart</c> (instrumento HART simulado) e <c>/ws/echo</c> (devolve o que recebe).
/// </summary>
public static class HartPocApp
{
    public const string DefaultUrl = "http://localhost:5210";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });

        // Só em localhost por padrão; para o teste com outro PC, passar --urls http://0.0.0.0:5210.
        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        // O console fica para o log dos quadros; do ASP.NET, só avisos e erros.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        var hart = builder.Configuration.GetSection("Hart");
        var revision = hart.GetValue("Revision", 5);
        var pollingAddress = hart.GetValue<byte>("PollingAddress", 0);
        var responseDelayMs = hart.GetValue("ResponseDelayMs", 0);
        var logDirectory = hart.GetValue("LogDirectory", "logs")!;

        builder.Services.AddSingleton(new FrameLog(logDirectory));
        builder.Services.AddSingleton<BridgeCounters>();
        builder.Services.AddSingleton(services =>
        {
            var log = services.GetRequiredService<FrameLog>();
            return new HartDevice(new HartDeviceIdentity { HartRevision = revision }, SineWavePressureKpa, log.Write)
            {
                PollingAddress = pollingAddress,
            };
        });
        builder.Services.AddHostedService<DeviceClock>();

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();

        app.MapGet("/api/status", (HartDevice device, BridgeCounters counters, FrameLog log) => Results.Json(new
        {
            hartRevision = revision,
            pollingAddress,
            responseDelayMs,
            logFile = log.FilePath,
            device = device.Snapshot(),
            counters,
        }));

        app.Map("/ws/hart", (HttpContext context) => RunSocketAsync(context, echo: false, responseDelayMs));
        app.Map("/ws/echo", (HttpContext context) => RunSocketAsync(context, echo: true, 0));

        var frameLog = app.Services.GetRequiredService<FrameLog>();
        frameLog.Write($"Instrumento simulado: Smar LD301 (fabricante 0x3E, tipo 0x01), HART {revision}, endereço de polling {pollingAddress}");
        return app;
    }

    /// <summary>Valor senoidal da prova: 25 ± 20 kPa, com período de 60 s.</summary>
    public static double SineWavePressureKpa(double timeS) => 25.0 + (20.0 * Math.Sin(2.0 * Math.PI * timeS / 60.0));

    private static async Task RunSocketAsync(HttpContext context, bool echo, int responseDelayMs)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var log = context.RequestServices.GetRequiredService<FrameLog>();
        var session = new HartBridgeSession(
            context.RequestServices.GetRequiredService<HartDevice>(),
            context.RequestServices.GetRequiredService<BridgeCounters>(),
            log.Write);
        var remote = context.Connection.RemoteIpAddress;
        var mode = echo ? "eco" : "HART";

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        log.Write($"Ponte conectada ({mode}) de {remote}");
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }

                // O fluxo é de bytes: os limites das mensagens do WebSocket não significam nada.
                var output = echo
                    ? buffer.AsSpan(0, result.Count).ToArray()
                    : session.OnBytes(buffer.AsSpan(0, result.Count), Environment.TickCount64);
                if (output.Length == 0)
                {
                    continue;
                }

                if (responseDelayMs > 0)
                {
                    await Task.Delay(responseDelayMs, context.RequestAborted);
                }

                await socket.SendAsync(output.AsMemory(), WebSocketMessageType.Binary, endOfMessage: true, context.RequestAborted);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // A aba foi fechada ou a rede caiu: a conexão acaba e o instrumento continua.
        }

        log.Write($"Ponte desconectada ({mode}) de {remote}");
    }
}
