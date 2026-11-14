using IndustrialSim.Core.Components;
using IndustrialSim.Core.Numerics;
using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Tests.ReferencePlants;

/// <summary>
/// Planta de tanque montada em código: é o "modelo codificado da Etapa 1" que o motor de cenários da
/// Etapa 3 terá de reproduzir bit a bit a partir de JSON. Fica no projeto de testes porque, pela D04,
/// o produto não tem classe por planta.
/// </summary>
/// <remarks>
/// Bomba P-101 → válvula de entrada FV-101 (característica linear) → tanque TQ-101, com saída por gravidade.
/// Não há controlador: a planta só reage aos comandos.
/// Ordem de cada passo (o motor de cenários deve seguir a mesma):
/// 1. os comandos são copiados para as entradas dos componentes (valem o passo inteiro);
/// 2. a bomba e a válvula avançam;
/// 3. a vazão de entrada do tanque é vazão da bomba × posição da válvula, com os valores já do fim do passo;
/// 4. o tanque avança;
/// 5. o relógio avança e a medição de nível recebe o ruído.
/// O ruído só entra na medição; nunca volta para a física.
/// </remarks>
internal sealed class WaterTankPlant
{
    private readonly SeededRandom _random;
    private readonly double _levelNoiseStdM;

    public WaterTankPlant(ulong noiseSeed, double levelNoiseStdM = 0.002, double initialLevelM = 0.5)
    {
        Pump = new Pump("P-101", ratedFlowM3PerS: 0.03, timeConstantSeconds: 1.0);
        InletValve = new FirstOrderActuator("FV-101", timeConstantSeconds: 2.0);
        Tank = new Tank("TQ-101", areaM2: 1.0, heightM: 2.0, outletAreaM2: 0.005, dischargeCoefficient: 0.61, initialLevelM);
        Clock = new SimulationClock();
        _random = new SeededRandom(noiseSeed);
        _levelNoiseStdM = levelNoiseStdM;
        MeasuredLevelM = Tank.LevelM;
    }

    public Pump Pump { get; }

    public FirstOrderActuator InletValve { get; }

    public Tank Tank { get; }

    public SimulationClock Clock { get; }

    /// <summary>Comando: ligar a bomba.</summary>
    public bool PumpRun { get; set; }

    /// <summary>Comando: abertura desejada da válvula de entrada, de 0 a 1.</summary>
    public double InletValveCommand { get; set; }

    /// <summary>Carga: abertura da saída do tanque, de 0 a 1.</summary>
    public double OutletOpening { get; set; } = 1.0;

    /// <summary>Nível real, em m.</summary>
    public double LevelM => Tank.LevelM;

    /// <summary>Nível medido (real + ruído gaussiano), em m.</summary>
    public double MeasuredLevelM { get; private set; }

    public void Step()
    {
        double time = Clock.TimeSeconds;
        double dt = Clock.StepSeconds;

        Pump.Run = PumpRun;
        InletValve.Target = InletValveCommand;
        Tank.OutletOpening = OutletOpening;

        Pump.Step(time, dt);
        InletValve.Step(time, dt);

        Tank.InflowM3PerS = Pump.FlowM3PerS * InletValve.Position;
        Tank.Step(time, dt);

        Clock.Advance();
        MeasuredLevelM = Tank.LevelM + _random.NextGaussian(0.0, _levelNoiseStdM);
    }

    /// <summary>Acrescenta o estado físico (passo do relógio e estados dos componentes) a um hash. O ruído fica de fora.</summary>
    public void AddStateTo(ref StateHasher hasher)
    {
        hasher.Add(Clock.StepCount);
        Pump.AddStateTo(ref hasher);
        InletValve.AddStateTo(ref hasher);
        Tank.AddStateTo(ref hasher);
    }
}
