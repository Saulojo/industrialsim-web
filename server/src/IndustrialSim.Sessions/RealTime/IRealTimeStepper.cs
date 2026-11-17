namespace IndustrialSim.Sessions.RealTime;

/// <summary>Algo que o laço de tempo real avança um passo por ciclo (uma planta em execução).</summary>
public interface IRealTimeStepper
{
    /// <summary>Nome para os logs.</summary>
    string Name { get; }

    /// <summary>Avança um passo. Chamado sempre pela mesma thread do laço.</summary>
    void Step();

    /// <summary>
    /// <see cref="Step"/> lançou uma exceção: o laço já tirou este item da sua lista e não o chama mais.
    /// </summary>
    void OnFault(Exception exception);
}
