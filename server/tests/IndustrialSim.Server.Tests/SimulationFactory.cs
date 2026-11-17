using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// Servidor de teste com as plantas em execução, configurado por chaves do appsettings; as gravações vão para
/// uma pasta temporária, apagada no fim.
/// </summary>
internal sealed class SimulationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings;

    public SimulationFactory(params (string Key, string Value)[] settings)
    {
        RecordingDirectory = Path.Combine(Path.GetTempPath(), "industrialsim-api-" + Guid.NewGuid().ToString("N"));
        _settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Simulation:Instances:RecordingDirectory"] = RecordingDirectory,
        };

        foreach (var (key, value) in settings)
        {
            _settings[key] = value;
        }
    }

    public string RecordingDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(RecordingDirectory))
        {
            Directory.Delete(RecordingDirectory, recursive: true);
        }
    }
}
