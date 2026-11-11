using IndustrialSim.Contracts;
using IndustrialSim.Server;

var builder = WebApplication.CreateBuilder(args);

// Relógio injetável, para os testes poderem fixar a hora.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Build do web (Vite) copiado para wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (TimeProvider time) =>
    new HealthResponse(ServerVersion.Current, time.GetUtcNow()));

// Rotas /api desconhecidas devolvem 404; o resto cai na página do web (SPA).
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
