using System.Text.Json;

namespace IndustrialSim.Contracts.Tests;

public class HealthResponseTests
{
    // Mesmas opções que o ASP.NET Core usa nas respostas JSON (camelCase).
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serializes_WithCamelCasePropertyNames()
    {
        var response = new HealthResponse("1.2.3", new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(response, WebOptions);

        Assert.Equal("{\"version\":\"9.9.9\",\"serverTimeUtc\":\"2026-11-11T12:00:00+00:00\"}", json);
    }

    [Fact]
    public void RoundTrips_ThroughJson()
    {
        var original = new HealthResponse("0.1.0", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        var copy = JsonSerializer.Deserialize<HealthResponse>(JsonSerializer.Serialize(original, WebOptions), WebOptions);

        Assert.Equal(original, copy);
    }
}
