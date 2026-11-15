using IndustrialSim.Core.Components;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Core.Tests.Components;

public class ValveTests(ITestOutputHelper output)
{
    private const double Dt = 0.01;

    /// <summary>
    /// Critério 5 da Etapa 3: a válvula igual-percentual segue a curva teórica f(x) = R^(x − 1) com erro &lt; 1%.
    /// A válvula é criada por JSON e ensaiada pelo motor de cenários: para cada comando de 5% a 100%, espera-se
    /// o regime (10 constantes de tempo) e compara-se a vazão medida com a teórica, calculada aqui de forma
    /// independente (exp e log, não a função da válvula).
    /// </summary>
    [Fact]
    public void EqualPercentage_FollowsTheoreticalCurve()
    {
        const double Rangeability = 50.0;
        const double RatedFlow = 4.0;
        const string Json = """
            {
              "schemaVersion": 1,
              "id": "valve-test",
              "components": [
                {
                  "id": "FV-1",
                  "type": "valve",
                  "params": { "timeConstantSeconds": 2.0, "characteristic": "equalPercentage", "rangeability": 50.0, "ratedFlow": 4.0 }
                }
              ],
              "inputs": [ { "name": "command", "to": ["FV-1.command"] } ],
              "outputs": [ { "name": "flow", "from": "FV-1.flow" }, { "name": "position", "from": "FV-1.position" } ]
            }
            """;

        var plant = ScenarioPlant.FromJson(Json);
        double worst = 0.0;
        output.WriteLine("comando   vazão medida   vazão teórica   erro relativo");

        for (int percent = 5; percent <= 100; percent += 5)
        {
            double command = percent / 100.0;
            plant.SetInput("command", command);
            for (int n = 0; n < 2000; n++) // 20 s = 10·τ
            {
                plant.Step();
            }

            double theoretical = RatedFlow * Math.Exp((command - 1.0) * Math.Log(Rangeability));
            double measured = plant.GetOutput("flow");
            double error = Math.Abs(measured - theoretical) / theoretical;
            worst = Math.Max(worst, error);
            output.WriteLine($"{percent,5}%   {measured,12:F6}   {theoretical,13:F6}   {error:E3}");
        }

        output.WriteLine($"maior erro relativo = {worst:E3} (limite 1%)");
        Assert.True(worst < 0.01, $"maior erro = {worst:E3}");
    }

    /// <summary>
    /// Propriedade que dá nome à característica: passos iguais de posição multiplicam a vazão pelo mesmo fator
    /// (aqui, cada 10% do curso multiplica por 50^0,1 ≈ 1,479).
    /// </summary>
    [Fact]
    public void EqualPercentage_EqualStepsGiveEqualPercentChange()
    {
        double expectedRatio = Math.Pow(50.0, 0.1);

        for (int i = 1; i < 10; i++)
        {
            double low = Valve.FlowFraction(ValveCharacteristic.EqualPercentage, i / 10.0, 50.0);
            double high = Valve.FlowFraction(ValveCharacteristic.EqualPercentage, (i + 1) / 10.0, 50.0);
            Assert.Equal(expectedRatio, high / low, 12);
        }

        Assert.Equal(1.0, Valve.FlowFraction(ValveCharacteristic.EqualPercentage, 1.0, 50.0));
    }

    /// <summary>Abaixo de 2% do curso a igual-percentual desce em linha reta até zero: comando 0 fecha a válvula.</summary>
    [Fact]
    public void EqualPercentage_ClosesAtZero()
    {
        double atShutoff = Valve.FlowFraction(ValveCharacteristic.EqualPercentage, Valve.ShutoffPosition, 50.0);

        Assert.Equal(0.0, Valve.FlowFraction(ValveCharacteristic.EqualPercentage, 0.0, 50.0));
        Assert.Equal(atShutoff / 2.0, Valve.FlowFraction(ValveCharacteristic.EqualPercentage, Valve.ShutoffPosition / 2.0, 50.0), 15);
        Assert.Equal(Math.Pow(50.0, Valve.ShutoffPosition - 1.0), atShutoff);

        // A curva é monótona em todo o curso.
        double previous = -1.0;
        for (int i = 0; i <= 1000; i++)
        {
            double fraction = Valve.FlowFraction(ValveCharacteristic.EqualPercentage, i / 1000.0, 50.0);
            Assert.True(fraction > previous);
            previous = fraction;
        }
    }

    [Fact]
    public void Linear_FlowIsProportionalToPosition()
    {
        var valve = new Valve("FV-1", timeConstantSeconds: 1.0, ratedFlow: 3.0) { Command = 0.4 };

        Run(valve, seconds: 20.0);

        Assert.Equal(0.4, valve.Position, 8);
        Assert.Equal(valve.Position, valve.Opening);
        Assert.Equal(3.0 * valve.Position, valve.Flow);
    }

    /// <summary>O atuador da válvula é o de 1ª ordem da Etapa 1: 63,2% do degrau em t = τ, e bit a bit igual a ele.</summary>
    [Fact]
    public void Actuator_MatchesFirstOrderActuator()
    {
        var valve = new Valve("FV-1", timeConstantSeconds: 2.0) { Command = 1.0 };
        var actuator = new FirstOrderActuator("ACT", timeConstantSeconds: 2.0) { Target = 1.0 };

        for (int n = 0; n < 200; n++)
        {
            valve.Step(n * Dt, Dt);
            actuator.Step(n * Dt, Dt);
            Assert.Equal(BitConverter.DoubleToInt64Bits(actuator.Position), BitConverter.DoubleToInt64Bits(valve.Position));
        }

        Assert.True(Math.Abs(valve.Position - (1.0 - Math.Exp(-1.0))) < 0.005 * (1.0 - Math.Exp(-1.0)));
    }

    [Fact]
    public void Command_IsLimitedToStroke()
    {
        var valve = new Valve("FV-1", timeConstantSeconds: 0.5) { Command = 7.0 };
        Run(valve, seconds: 20.0);
        Assert.True(valve.Position <= 1.0 && valve.Position > 0.999999);

        valve.Command = -3.0;
        Run(valve, seconds: 20.0);
        Assert.True(valve.Position >= 0.0 && valve.Position < 1e-6);
    }

    [Fact]
    public void InletFlow_ScalesFlow()
    {
        var valve = new Valve("FV-1", timeConstantSeconds: 0.5, initialPosition: 0.5) { Command = 0.5, InletFlow = 8.0 };

        valve.UpdateOutputs();

        Assert.Equal(4.0, valve.Flow);
    }

    [Fact]
    public void InvalidParameters_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Valve("FV-1", timeConstantSeconds: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Valve("FV-1", 1.0, ValveCharacteristic.EqualPercentage, rangeability: 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Valve("FV-1", 1.0, ratedFlow: -1.0));
    }

    private static void Run(Component component, double seconds)
    {
        int steps = (int)Math.Round(seconds / Dt);
        for (int n = 0; n < steps; n++)
        {
            component.Step(n * Dt, Dt);
        }
    }
}
