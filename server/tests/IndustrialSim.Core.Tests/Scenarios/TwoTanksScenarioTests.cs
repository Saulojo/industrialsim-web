using IndustrialSim.Core.Components;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Core.Tests.Scenarios;

public class TwoTanksScenarioTests(ITestOutputHelper output)
{
    /// <summary>
    /// Critério 2 da Etapa 3: dois tanques em série criados só com JSON (<c>two-tanks.json</c>; nenhuma classe
    /// nova) conservam massa. A variação do volume total iguala ∫(Qin − Qout)dt, com Qin a entrada do primeiro
    /// tanque e Qout a saída do segundo, com erro relativo &lt; 1e-6 em 600 s.
    /// Como na Etapa 1, a integral é calculada aqui, fora da planta: Qin é constante em cada passo (soma exata)
    /// e Qout é integrada pela regra do trapézio, com a vazão lida no início e no fim de cada passo.
    /// </summary>
    [Fact]
    public void TwoTanksInSeries_ConserveMass()
    {
        var plant = ScenarioPlant.FromJson(ScenarioFiles.ReadTest("two-tanks.json"));
        var first = (Tank)plant.FindComponent("TQ-1");
        var second = (Tank)plant.FindComponent("TQ-2");
        double dt = plant.Clock.StepSeconds;

        double initialVolume = first.VolumeM3 + second.VolumeM3;
        double inflowIntegral = 0.0;
        double outflowIntegral = 0.0;
        double transferred = 0.0;
        double minLevel = double.MaxValue;
        double maxLevel = double.MinValue;

        const int Steps = 60_000; // 600 s
        for (int n = 0; n < Steps; n++)
        {
            int segment = n / 3000; // entradas mudam a cada 30 s
            double inflow = 0.012 + (0.008 * Math.Sin(segment));
            plant.SetInput("inflow", inflow);
            plant.SetInput("opening1", 0.6 + (0.4 * Math.Cos(2.0 * segment)));

            // A abertura do segundo tanque não muda, então a vazão de saída é contínua de um passo para o outro.
            double outflowAtStart = plant.GetOutput("outflow2");
            plant.Step();
            double outflowAtEnd = plant.GetOutput("outflow2");

            inflowIntegral += inflow * dt;
            outflowIntegral += 0.5 * (outflowAtStart + outflowAtEnd) * dt;
            transferred += first.MeanOutflowM3PerS * dt;
            minLevel = Math.Min(minLevel, Math.Min(first.LevelM, second.LevelM));
            maxLevel = Math.Max(maxLevel, Math.Max(first.LevelM, second.LevelM));

            Assert.Equal(0.0, plant.GetOutput("overflow1"));
            Assert.Equal(0.0, plant.GetOutput("overflow2"));
        }

        double volumeChange = first.VolumeM3 + second.VolumeM3 - initialVolume;
        double netFlowIntegral = inflowIntegral - outflowIntegral;
        double relativeError = Math.Abs(volumeChange - netFlowIntegral) / Math.Abs(volumeChange);
        output.WriteLine($"ΔV total = {volumeChange:R} m³; ∫(Qin − Qout)dt = {netFlowIntegral:R} m³; erro relativo = {relativeError:E3}");
        output.WriteLine($"∫Qin dt = {inflowIntegral:R} m³; ∫Qout dt = {outflowIntegral:R} m³; passou do 1º para o 2º tanque: {transferred:R} m³");
        output.WriteLine($"níveis entre {minLevel:F4} m e {maxLevel:F4} m; ordem de execução: {string.Join(" → ", plant.ExecutionOrderIds)}");

        // O ensaio só vale se nenhum tanque saturou, se passou água de um para o outro e se ΔV não é pequeno demais.
        Assert.True(minLevel > 0.1 && maxLevel < 2.9, $"níveis entre {minLevel} e {maxLevel}");
        Assert.True(transferred > 1.0, $"transferido = {transferred}");
        Assert.True(Math.Abs(volumeChange) > 0.1, $"ΔV = {volumeChange}");
        Assert.True(relativeError < 1e-6, $"erro relativo = {relativeError:E3}");
    }

    /// <summary>
    /// A ligação entre os tanques não cria nem perde água: o que sai do primeiro (pelo balanço dele) é o que
    /// entra no segundo, passo a passo, até a precisão de máquina.
    /// </summary>
    [Fact]
    public void WaterLeavingFirstTank_EntersSecond()
    {
        var plant = ScenarioPlant.FromJson(ScenarioFiles.ReadTest("two-tanks.json"));
        var first = (Tank)plant.FindComponent("TQ-1");
        var second = (Tank)plant.FindComponent("TQ-2");
        double dt = plant.Clock.StepSeconds;
        plant.SetInput("inflow", 0.0);
        plant.SetInput("opening2", 0.0); // segundo tanque fechado: tudo o que entra fica nele

        double firstBefore = first.VolumeM3;
        double secondBefore = second.VolumeM3;
        for (int n = 0; n < 20_000; n++)
        {
            plant.Step();
            Assert.Equal(first.MeanOutflowM3PerS, second.InflowM3PerS);
        }

        double left = firstBefore - first.VolumeM3;
        double arrived = second.VolumeM3 - secondBefore;
        output.WriteLine($"saiu do 1º: {left:R} m³; chegou ao 2º: {arrived:R} m³; diferença = {Math.Abs(left - arrived):E3} m³");
        Assert.True(left > 0.5);
        Assert.True(Math.Abs(left - arrived) < 1e-12, $"diferença = {Math.Abs(left - arrived):E3}");
    }
}
