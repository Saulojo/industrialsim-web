using System.Net;
using System.Net.Http.Json;
using IndustrialSim.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 11, 14, 30, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow))));
    }

    [Fact]
    public async Task Health_ReturnsVersionAndServerTime()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetFromJsonAsync<HealthResponse>("/api/health", TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(ServerVersion.Current, response.Version);
        Assert.StartsWith("0.1.0", response.Version, StringComparison.Ordinal);
        Assert.Equal(FixedNow, response.ServerTimeUtc);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
