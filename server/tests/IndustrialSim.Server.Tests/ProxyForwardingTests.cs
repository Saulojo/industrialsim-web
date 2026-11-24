using System.Net;
using System.Net.Http.Json;
using IndustrialSim.Contracts;
using IndustrialSim.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// IP real do navegador atrás do Caddy (Etapa 9A): <c>X-Forwarded-For</c> só vale vindo do loopback, e o autor que
/// vai para o log de comunicação (D44) é o IP do navegador, e não o do proxy.
/// </summary>
public class ProxyForwardingTests
{
    /// <summary>Cabeçalho só do teste: o IP da conexão, que o servidor de teste em memória não tem.</summary>
    private const string RemoteHeader = "X-Test-Remote-Address";

    private const string Browser = "203.0.113.7";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1", Browser, Browser)]
    [InlineData("::1", Browser, Browser)]
    [InlineData("198.51.100.20", Browser, "198.51.100.20")]
    [InlineData("127.0.0.1", null, "127.0.0.1")]
    public async Task AuthorInTheLog_IsTheForwardedAddress_OnlyFromLoopback(string connection, string? forwardedFor, string expectedAuthor)
    {
        using var factory = new SimulationFactory { ConfigureTestServices = SetRemoteAddressFromHeader };
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);
        PlantSummary plant = (await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/plants/{plant.Id}/modbus/masters")
        {
            Content = JsonContent.Create(new MasterAuthorizeRequest("10.9.8.7")),
        };
        request.Headers.Add(RemoteHeader, connection);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request, Cancel)).StatusCode);

        Assert.True(factory.Services.GetRequiredService<InstanceManager>().TryGet(plant.Id, out PlantInstance? instance));
        PlantEvent authorized = Assert.Single(instance.Events.All(), e => e.Kind == PlantEventKinds.MasterAuthorized);
        Assert.Equal(expectedAuthor, authorized.Arguments.Single(a => a.Name == "author").Value);
    }

    /// <summary>Só o primeiro salto conta: um X-Forwarded-For forjado pelo navegador fica atrás do que o Caddy acrescenta.</summary>
    [Fact]
    public async Task ForgedChain_UsesTheAddressAddedByTheProxy()
    {
        using var factory = new SimulationFactory { ConfigureTestServices = SetRemoteAddressFromHeader };
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);
        PlantSummary plant = (await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/plants/{plant.Id}/modbus/masters")
        {
            Content = JsonContent.Create(new MasterAuthorizeRequest("10.9.8.7")),
        };
        request.Headers.Add(RemoteHeader, "127.0.0.1");
        request.Headers.Add("X-Forwarded-For", "192.0.2.99, " + Browser);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request, Cancel)).StatusCode);

        Assert.True(factory.Services.GetRequiredService<InstanceManager>().TryGet(plant.Id, out PlantInstance? instance));
        PlantEvent authorized = Assert.Single(instance.Events.All(), e => e.Kind == PlantEventKinds.MasterAuthorized);
        Assert.Equal(Browser, authorized.Arguments.Single(a => a.Name == "author").Value);
    }

    private static void SetRemoteAddressFromHeader(IServiceCollection services) =>
        services.AddSingleton<IStartupFilter, RemoteAddressFilter>();

    /// <summary>Põe, antes de todo o pipeline do app, o IP da conexão dado pelo cabeçalho do teste.</summary>
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteHeader, out var value) && IPAddress.TryParse(value.ToString(), out IPAddress? address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
