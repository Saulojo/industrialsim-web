using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IndustrialSim.Contracts;

namespace IndustrialSim.SimRun.NetworkLoad;

/// <summary>API REST do servidor usada pelo teste de carga: saúde, plantas, autorização de mestres e métricas.</summary>
internal sealed class LoadServerClient(HttpClient http)
{
    public HttpClient Http { get; } = http;

    /// <summary>
    /// Cliente HTTP para o servidor. Com <paramref name="caPath"/>, o certificado do servidor só vale se a cadeia
    /// chegar a essa raiz (ex.: a raiz do <c>tls internal</c> do Caddy, Etapa 9A); sem ele, vale o repositório do sistema.
    /// </summary>
    public static HttpClient CreateHttpClient(Uri server, string? caPath)
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (caPath is not null)
        {
            X509Certificate2 root = X509CertificateLoader.LoadCertificateFromFile(caPath);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                ValidateWithRoot(root, certificate, chain, errors);
        }

        return new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<HealthResponse?> TryGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync("/api/health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Cria uma planta; devolve o resumo, ou o código de erro da API (ex.: limite de instâncias).</summary>
    public async Task<(PlantSummary? Plant, string? Error)> CreatePlantAsync(string scenario, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.PostAsJsonAsync("/api/plants", new PlantCreateRequest(scenario), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            return (await response.Content.ReadFromJsonAsync<PlantSummary>(cancellationToken).ConfigureAwait(false), null);
        }

        return (null, await ErrorCode(response, cancellationToken).ConfigureAwait(false));
    }

    public async Task<string?> AuthorizeMasterAsync(Guid plant, IPAddress master, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.PostAsJsonAsync(
            $"/api/plants/{plant}/modbus/masters", new MasterAuthorizeRequest(master.ToString()), cancellationToken).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await ErrorCode(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlantStateResponse?> GetStateAsync(Guid plant, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync($"/api/plants/{plant}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PlantStateResponse>(cancellationToken).ConfigureAwait(false)
            : null;
    }

    public Task<LoopMetricsResponse?> GetMetricsAsync(CancellationToken cancellationToken) =>
        Http.GetFromJsonAsync<LoopMetricsResponse>("/api/simulation/metrics", cancellationToken);

    public async Task EndPlantAsync(Guid plant, CancellationToken cancellationToken)
    {
        using HttpResponseMessage _ = await Http.DeleteAsync($"/api/plants/{plant}", cancellationToken).ConfigureAwait(false);
    }

    internal static bool ValidateWithRoot(X509Certificate2 root, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        // Nome errado ou certificado ausente não se salvam com a raiz própria; só a cadeia é refeita.
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.Add(root);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (chain is not null)
        {
            foreach (X509ChainElement element in chain.ChainElements)
            {
                custom.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return custom.Build(leaf);
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ApiError? error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken).ConfigureAwait(false);
            return error?.Code ?? $"http-{(int)response.StatusCode}";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return $"http-{(int)response.StatusCode}";
        }
    }
}
