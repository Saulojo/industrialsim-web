using IndustrialSim.Contracts;
using IndustrialSim.Server;

var builder = WebApplication.CreateBuilder(args);

// Relógio injetável, para os testes poderem fixar a hora.
builder.Services.AddSingleton(TimeProvider.System);

// Plantas em execução: cenários, laço de tempo real e gerenciador (Etapa 5).
builder.Services.AddSimulation(builder.Configuration, builder.Environment);

// IP real do navegador atrás do Caddy (Etapa 9A), aceito só do loopback.
builder.Services.AddProxyForwarding();

var app = builder.Build();

// Primeiro de tudo: o resto do pipeline (API, WebSocket, log) já vê o IP do navegador.
app.UseForwardedHeaders();

// Build do web (Vite) copiado para wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

// WebSocket da planta (Etapa 7), na mesma porta do site: /api/plants/{id}/ws.
app.UseWebSockets();

app.MapGet("/api/health", (TimeProvider time) =>
    new HealthResponse(ServerVersion.Current, time.GetUtcNow()));

app.MapSimulationApi();

// Rotas /api desconhecidas devolvem 404; o resto cai na página do web (SPA).
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
