namespace IndustrialSim.Contracts;

/// <summary>
/// Resposta de <c>GET /api/health</c>.
/// </summary>
/// <param name="Version">Versão do servidor (versão informativa do assembly, com o commit).</param>
/// <param name="ServerTimeUtc">Hora do servidor em UTC.</param>
public sealed record HealthResponse(string Version, DateTimeOffset ServerTimeUtc);
