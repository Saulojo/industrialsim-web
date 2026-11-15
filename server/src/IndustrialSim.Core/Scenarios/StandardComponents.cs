using IndustrialSim.Core.Components;

namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Biblioteca padrão de tipos de componente para os cenários: liga o nome usado no JSON, os parâmetros
/// (com unidade e faixa) e a classe que implementa a física. A física e as fontes estão em cada classe.
/// </summary>
public static class StandardComponents
{
    private const string Linear = "linear";
    private const string EqualPercentage = "equalPercentage";
    private const string Rising = "rising";
    private const string Falling = "falling";

    /// <summary>Todos os tipos, na ordem em que aparecem no esquema.</summary>
    public static IReadOnlyList<ComponentType> All { get; } =
    [
        new ComponentType(
            "pump",
            "Bomba liga/desliga: a vazão segue o comando com dinâmica de 1ª ordem. Entrada: run. Saídas: flow, running.",
            [
                ParameterSpec.Number("ratedFlow", "vazão nominal, na unidade de vazão do cenário (m³/s ou kg/s)", min: 0.0),
                ParameterSpec.Number("timeConstantSeconds", "constante de tempo de partida e parada", "s", defaultValue: 1.0, min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("runningThreshold", "fração da rotação acima da qual 'running' vale 1", defaultValue: 0.5, min: 0.0, max: 1.0, exclusiveMin: true, exclusiveMax: true),
            ],
            [
                ParameterSpec.Number("speed", "rotação inicial, como fração da nominal", defaultValue: 0.0, min: 0.0, max: 1.0),
            ],
            context =>
            {
                var pump = new Pump(
                    context.Id,
                    context.Parameters.Number("ratedFlow"),
                    context.Parameters.Number("timeConstantSeconds"),
                    context.Parameters.Number("runningThreshold"));
                if (context.Initial.IsGiven("speed"))
                {
                    pump.SetState([context.Initial.Number("speed")]);
                }

                return pump;
            }),

        new ComponentType(
            "valve",
            "Válvula de controle com atuador de 1ª ordem e característica linear ou igual-percentual. " +
            "Entradas: command (0 a 1), inletFlow. Saídas: position, opening, flow.",
            [
                ParameterSpec.Number("timeConstantSeconds", "constante de tempo do atuador", "s", min: 0.0, exclusiveMin: true),
                ParameterSpec.Choice("characteristic", "característica inerente de vazão", [Linear, EqualPercentage], Linear),
                ParameterSpec.Number("rangeability", "rangeabilidade R da característica igual-percentual", defaultValue: 50.0, min: 1.0, exclusiveMin: true),
                ParameterSpec.Number("ratedFlow", "vazão com a válvula toda aberta, usada quando 'inletFlow' não é ligada", defaultValue: 1.0, min: 0.0),
            ],
            [
                ParameterSpec.Number("position", "posição inicial", defaultValue: 0.0, min: 0.0, max: 1.0),
            ],
            context => new Valve(
                context.Id,
                context.Parameters.Number("timeConstantSeconds"),
                context.Parameters.Choice("characteristic") == EqualPercentage ? ValveCharacteristic.EqualPercentage : ValveCharacteristic.Linear,
                context.Parameters.Number("rangeability"),
                context.Parameters.Number("ratedFlow"),
                context.Initial.Number("position"))),

        new ComponentType(
            "tank",
            "Tanque atmosférico de seção constante, com saída por orifício no fundo (Torricelli). " +
            "Entradas: inflow (m³/s), outletOpening (0 a 1). Saídas: level (m), outflow, overflow, meanOutflow (m³/s).",
            [
                ParameterSpec.Number("areaM2", "área da seção transversal", "m²", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("heightM", "altura do tanque", "m", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("outletAreaM2", "área do orifício de saída totalmente aberto", "m²", min: 0.0),
                ParameterSpec.Number("dischargeCoefficient", "coeficiente de descarga do orifício", defaultValue: 0.61, min: 0.0, max: 1.0, exclusiveMin: true),
            ],
            [
                ParameterSpec.Number("levelM", "nível inicial", "m", defaultValue: 0.0, min: 0.0),
            ],
            context => new Tank(
                context.Id,
                context.Parameters.Number("areaM2"),
                context.Parameters.Number("heightM"),
                context.Parameters.Number("outletAreaM2"),
                context.Parameters.Number("dischargeCoefficient"),
                context.Initial.Number("levelM"))),

        new ComponentType(
            "drum",
            "Tubulão v1: balanço de massa da água, com evaporação dada pelo calor recebido. " +
            "Entradas: feedwaterFlow (kg/s), heat (W). Saídas: level (m), steamGeneration (kg/s), overflow (kg/s), waterMass (kg).",
            [
                ParameterSpec.Number("areaM2", "área da seção horizontal", "m²", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("heightM", "altura interna (diâmetro)", "m", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("waterDensityKgPerM3", "densidade da água no tubulão", "kg/m³", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("evaporationEnthalpyJPerKg", "entalpia para evaporar 1 kg de água de alimentação", "J/kg", min: 0.0, exclusiveMin: true),
            ],
            [
                ParameterSpec.Number("levelM", "nível inicial", "m", defaultValue: 0.0, min: 0.0),
            ],
            context => new Drum(
                context.Id,
                context.Parameters.Number("areaM2"),
                context.Parameters.Number("heightM"),
                context.Parameters.Number("waterDensityKgPerM3"),
                context.Parameters.Number("evaporationEnthalpyJPerKg"),
                context.Initial.Number("levelM"))),

        new ComponentType(
            "burner",
            "Queimador e fornalha: calor = eficiência × poder calorífico × vazão de combustível, com dinâmica de 1ª ordem. " +
            "Entrada: fuelFlow (kg/s). Saída: heat (W).",
            [
                ParameterSpec.Number("lowerHeatingValueJPerKg", "poder calorífico inferior do combustível", "J/kg", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("efficiency", "fração do calor do combustível que chega à água", min: 0.0, max: 1.0, exclusiveMin: true),
                ParameterSpec.Number("timeConstantSeconds", "constante de tempo da fornalha", "s", min: 0.0, exclusiveMin: true),
            ],
            [
                ParameterSpec.Number("heatW", "calor entregue no instante inicial", "W", defaultValue: 0.0, min: 0.0),
            ],
            context => new Burner(
                context.Id,
                context.Parameters.Number("lowerHeatingValueJPerKg"),
                context.Parameters.Number("efficiency"),
                context.Parameters.Number("timeConstantSeconds"),
                context.Initial.Number("heatW"))),

        new ComponentType(
            "steamPressure",
            "Modelo de pressão v1: acumulador de vapor de 1ª ordem, com saída proporcional à abertura e à pressão manométrica. " +
            "Entradas: steamGeneration (kg/s), outletOpening (0 a 1), reliefFlow (kg/s). Saídas: pressure (Pa abs), steamFlow (kg/s).",
            [
                ParameterSpec.Number("capacitanceKgPerPa", "capacitância de vapor do gerador", "kg/Pa", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("outletConductanceKgPerSPa", "condutância da saída de vapor toda aberta", "kg/(s·Pa)", min: 0.0),
                ParameterSpec.Number("downstreamPressurePa", "pressão absoluta a jusante da válvula de saída", "Pa", defaultValue: PhysicalConstants.StandardAtmospherePa, min: 0.0),
            ],
            [
                ParameterSpec.Number("pressurePa", "pressão absoluta inicial (padrão: a de jusante)", "Pa", defaultValue: 0.0, min: 0.0),
            ],
            context => new SteamPressure(
                context.Id,
                context.Parameters.Number("capacitanceKgPerPa"),
                context.Parameters.Number("outletConductanceKgPerSPa"),
                context.Parameters.Number("downstreamPressurePa"),
                context.Initial.IsGiven("pressurePa") ? context.Initial.Number("pressurePa") : null)),

        new ComponentType(
            "transmitter",
            "Transmissor: amortecimento de 1ª ordem, ruído gaussiano com semente e quantização. Entrada: value. Saída: measurement.",
            [
                ParameterSpec.Number("dampingSeconds", "constante de tempo do amortecimento (0 = sem filtro)", "s", defaultValue: 0.0, min: 0.0),
                ParameterSpec.Number("noiseStandardDeviation", "desvio padrão do ruído, na unidade da medição", defaultValue: 0.0, min: 0.0),
                ParameterSpec.Number("resolution", "passo de quantização, na unidade da medição (0 = sem quantização)", defaultValue: 0.0, min: 0.0),
            ],
            [
                ParameterSpec.Number("value", "valor inicial do filtro (padrão: o valor de entrada no instante inicial)", defaultValue: 0.0),
            ],
            context => new Transmitter(
                context.Id,
                context.Parameters.Number("dampingSeconds"),
                context.Parameters.Number("noiseStandardDeviation"),
                context.Parameters.Number("resolution"),
                context.NextNoiseSeed(),
                context.Initial.IsGiven("value") ? context.Initial.Number("value") : null)),

        new ComponentType(
            "switch",
            "Chave de processo (nível, pressão) com banda morta. Entrada: value. Saída: active (0 ou 1).",
            [
                ParameterSpec.Number("setpoint", "ponto de ajuste, na unidade do valor medido"),
                ParameterSpec.Choice("direction", "sentido de atuação: 'rising' atua na subida (chave de alta), 'falling' na queda (chave de baixa)", [Rising, Falling]),
                ParameterSpec.Number("deadband", "banda morta (diferencial), na unidade do valor medido", defaultValue: 0.0, min: 0.0),
            ],
            [],
            context => new ThresholdSwitch(
                context.Id,
                context.Parameters.Number("setpoint"),
                context.Parameters.Choice("direction") == Rising ? SwitchDirection.Rising : SwitchDirection.Falling,
                context.Parameters.Number("deadband"))),

        new ComponentType(
            "reliefValve",
            "Válvula de alívio de ação rápida: abre na pressão de ajuste e fecha na de fechamento. " +
            "Entrada: pressure (Pa abs). Saídas: open (0 ou 1), flow (kg/s).",
            [
                ParameterSpec.Number("setPressurePa", "pressão absoluta de ajuste (abertura)", "Pa", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("reseatPressurePa", "pressão absoluta de fechamento", "Pa", min: 0.0, exclusiveMin: true),
                ParameterSpec.Number("ratedFlowKgPerS", "vazão descarregada na pressão de ajuste", "kg/s", min: 0.0),
                ParameterSpec.Number("backPressurePa", "contrapressão absoluta na descarga", "Pa", defaultValue: PhysicalConstants.StandardAtmospherePa, min: 0.0),
            ],
            [],
            context => new ReliefValve(
                context.Id,
                context.Parameters.Number("setPressurePa"),
                context.Parameters.Number("reseatPressurePa"),
                context.Parameters.Number("ratedFlowKgPerS"),
                context.Parameters.Number("backPressurePa"))),
    ];
}
