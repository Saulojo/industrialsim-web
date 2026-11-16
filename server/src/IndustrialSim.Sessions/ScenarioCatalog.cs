using System.Diagnostics.CodeAnalysis;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialSim.Sessions;

/// <summary>Um cenário que o servidor pode rodar.</summary>
/// <param name="Definition">Conteúdo do <c>scenario.json</c>, já validado.</param>
/// <param name="SourcePath">Arquivo de onde veio.</param>
public sealed record ScenarioEntry(ScenarioDefinition Definition, string SourcePath)
{
    public string Id => Definition.Id;
}

/// <summary>
/// Cenários disponíveis no servidor: cada subpasta da pasta de cenários com um <c>scenario.json</c> (D04).
/// </summary>
/// <remarks>
/// Cada arquivo é validado por inteiro na carga, montando uma planta e o seu mapa de I/O; um arquivo inválido
/// é ignorado com um aviso no log, e os outros continuam disponíveis. Só entram cenários com a seção
/// <c>io</c> (o servidor liga toda planta a uma imagem de I/O) e com o passo do laço de tempo real (D06).
/// </remarks>
public sealed class ScenarioCatalog
{
    public const string ScenarioFileName = "scenario.json";

    private readonly Dictionary<string, ScenarioEntry> _byId;

    public ScenarioCatalog(IEnumerable<ScenarioEntry> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        _byId = new Dictionary<string, ScenarioEntry>(StringComparer.Ordinal);
        foreach (ScenarioEntry entry in scenarios)
        {
            if (!_byId.TryAdd(entry.Id, entry))
            {
                throw new ArgumentException($"Cenário '{entry.Id}' repetido.", nameof(scenarios));
            }
        }

        Scenarios = [.. _byId.Values.OrderBy(s => s.Id, StringComparer.Ordinal)];
    }

    /// <summary>Cenários, em ordem de identificador.</summary>
    public IReadOnlyList<ScenarioEntry> Scenarios { get; }

    /// <summary>Carrega os cenários de <paramref name="directory"/>.</summary>
    /// <param name="directory">Pasta com uma subpasta por cenário.</param>
    /// <param name="stepSeconds">Passo do laço de tempo real; cenários com outro passo são ignorados.</param>
    /// <param name="logger">Logger para cenários carregados e ignorados.</param>
    public static ScenarioCatalog Load(string directory, double stepSeconds, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        logger ??= NullLogger.Instance;

        var entries = new List<ScenarioEntry>();
        if (!Directory.Exists(directory))
        {
            SessionsLog.ScenarioDirectoryMissing(logger, Path.GetFullPath(directory));
            return new ScenarioCatalog(entries);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> files = Directory.EnumerateDirectories(directory)
            .Select(d => Path.Combine(d, ScenarioFileName))
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (string file in files)
        {
            if (TryLoad(file, stepSeconds, out ScenarioEntry? entry, out string? reason) && !seen.Add(entry.Id))
            {
                reason = $"identificador '{entry.Id}' repetido.";
                entry = null;
            }

            if (entry is null)
            {
                SessionsLog.ScenarioSkipped(logger, file, reason ?? string.Empty);
                continue;
            }

            entries.Add(entry);
            SessionsLog.ScenarioLoaded(logger, entry.Id, file);
        }

        return new ScenarioCatalog(entries);
    }

    public bool TryGet(string id, [NotNullWhen(true)] out ScenarioEntry? entry) => _byId.TryGetValue(id, out entry);

    private static bool TryLoad(string file, double stepSeconds, [NotNullWhen(true)] out ScenarioEntry? entry, out string? reason)
    {
        entry = null;
        try
        {
            ScenarioDefinition definition = ScenarioLoader.Load(File.ReadAllText(file));
            if (definition.Io is null)
            {
                reason = "sem a seção 'io' (mapa Modbus).";
                return false;
            }

            if (definition.StepSeconds != stepSeconds)
            {
                reason = $"passo de {definition.StepSeconds} s, diferente do passo do laço ({stepSeconds} s).";
                return false;
            }

            // Monta uma vez para validar tipos, parâmetros, ligações e o mapa de I/O.
            _ = new PlantIo(ScenarioPlant.Create(definition));
            entry = new ScenarioEntry(definition, file);
            reason = null;
            return true;
        }
        catch (ScenarioValidationException ex)
        {
            reason = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            reason = ex.Message;
            return false;
        }
    }
}
