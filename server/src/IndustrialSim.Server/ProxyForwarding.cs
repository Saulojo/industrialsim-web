using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace IndustrialSim.Server;

/// <summary>
/// Cabeçalhos encaminhados pelo proxy reverso (Etapa 9A, D10): na VM, o Caddy recebe o navegador na 443 e repassa
/// ao Kestrel em <c>127.0.0.1</c>. Sem isto, todo pedido pareceria vir de 127.0.0.1, e o log de comunicação e o
/// monitor (D44) mostrariam esse IP como autor. <c>X-Forwarded-For</c> e <c>X-Forwarded-Proto</c> só valem quando
/// a conexão vem do próprio computador (loopback); vindos de qualquer outro IP, são ignorados, para ninguém
/// forjar o autor mandando o cabeçalho direto ao Kestrel.
/// </summary>
/// <remarks>
/// O Modbus não passa pelo proxy (porta própria por planta, D07): o pareamento continua usando o IP da conexão TCP.
/// Não usar a variável <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>: ela aceita os cabeçalhos de qualquer IP.
/// </remarks>
internal static class ProxyForwarding
{
    public static IServiceCollection AddProxyForwarding(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Só o primeiro salto (o Caddy); um X-Forwarded-For mandado pelo próprio navegador não passa adiante.
            options.ForwardLimit = 1;

            // Proxies confiáveis: só o loopback (IPv4 127.0.0.0/8 e IPv6 ::1), escrito aqui para não depender do padrão.
            options.KnownIPNetworks.Clear();
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });
}
