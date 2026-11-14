using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Bloco linear genérico: integra um <see cref="StateSpaceModel"/> com RK4, com a entrada mantida constante no passo.
/// Serve para dinâmicas descritas por função de transferência (atrasos, filtros, modelos de 1ª e 2ª ordem).
/// </summary>
public sealed class StateSpaceBlock : Component
{
    public const string InputName = "u";
    public const string OutputName = "y";

    private readonly Rk4Integrator _integrator;
    private readonly OdeSystem _system;
    private double _heldInput;

    public StateSpaceBlock(string name, StateSpaceModel model)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        Inputs.Add(InputName);
        OutputSignals.Add(OutputName);
        for (int i = 1; i <= model.Order; i++)
        {
            StateSignals.Add($"x{i}");
        }

        _integrator = new Rk4Integrator(model.Order);
        _system = Derivative;
    }

    public StateSpaceBlock(string name, TransferFunction transferFunction)
        : this(name, (transferFunction ?? throw new ArgumentNullException(nameof(transferFunction))).ToStateSpace())
    {
    }

    public StateSpaceModel Model { get; }

    public double Input
    {
        get => Inputs[0];
        set => Inputs[0] = value;
    }

    public double Output => Outputs[0];

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        _heldInput = Inputs[0];
        _integrator.Step(_system, timeSeconds, StateSignals.AsSpan(), stepSeconds);
    }

    protected override void ComputeOutputs() => OutputSignals[0] = Model.Output(StateSignals.Values, Inputs[0]);

    private void Derivative(double timeSeconds, ReadOnlySpan<double> state, Span<double> derivative) =>
        Model.Derivative(state, _heldInput, derivative);
}
