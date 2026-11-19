using System.Text.Json;
using IndustrialSim.Contracts;

namespace IndustrialSim.Contracts.Tests;

/// <summary>Etapa 7: mensagens do WebSocket da planta e tipos TypeScript gerados do contrato (D05, D40).</summary>
public class PlantSocketContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

    /// <summary>
    /// O arquivo do web é a saída do gerador. Se falhar, rode <c>npm run contracts</c> na pasta <c>web</c> e
    /// confira o diff.
    /// </summary>
    [Fact]
    public void GeneratedTypeScript_MatchesCommittedFile()
    {
        string path = Path.Combine(RepositoryRoot(), "web", "src", "net", "contracts.generated.ts");

        string committed = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.Equal(TypeScriptWriter.Write(), committed);
    }

    [Fact]
    public void ClientMessages_UseTypeDiscriminator()
    {
        string json = JsonSerializer.Serialize<ClientMessage>(new SetDisturbanceCommand("FV-103_command", 0.9), Web);

        Assert.Equal("""{"type":"setDisturbance","input":"FV-103_command","value":0.9}""", json);
    }

    /// <summary>O servidor aceita o campo <c>type</c> em qualquer posição do objeto.</summary>
    [Fact]
    public void ClientMessages_AcceptDiscriminatorOutOfOrder()
    {
        ClientMessage? message = JsonSerializer.Deserialize<ClientMessage>("""{"protocolVersion":1,"type":"hello"}""", Web);

        Assert.Equal(new HelloMessage(1), message);
    }

    [Fact]
    public void RestartCommand_HasOnlyTheType()
    {
        Assert.Equal("""{"type":"restart"}""", JsonSerializer.Serialize<ClientMessage>(new RestartCommand(), Web));
        Assert.IsType<RestartCommand>(JsonSerializer.Deserialize<ClientMessage>("""{"type":"restart"}""", Web));
    }

    [Fact]
    public void StateMessage_IsCompact()
    {
        var state = new StateMessage(1234, 12.34, 0, PlantStatuses.Running, false, [1f, 0.8228f], [0.6f, 1000000f]);

        string json = JsonSerializer.Serialize<ServerMessage>(state, Web);

        Assert.Equal(
            """{"type":"state","step":1234,"time":12.34,"run":0,"status":"running","watchdog":false,"inputs":[1,0.8228],"outputs":[0.6,1000000]}""",
            json);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Raiz do repositório (com global.json) não encontrada.");
    }
}
