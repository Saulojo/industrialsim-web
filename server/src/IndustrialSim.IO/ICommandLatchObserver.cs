namespace IndustrialSim.IO;

/// <summary>
/// Observa a fase de comandos de cada passo de um <see cref="PlantIo"/>: o que foi copiado da imagem e as
/// configurações em vigor, já com o watchdog avaliado e as entradas da planta atualizadas. Usado para gravar
/// ensaios (RF07).
/// </summary>
public interface ICommandLatchObserver
{
    /// <param name="stepIndex">Índice do passo que vai rodar com estes comandos (a partir de 0).</param>
    /// <param name="coils">Coils copiados da imagem; válidos só durante a chamada.</param>
    /// <param name="holdingRegisters">Holding registers copiados da imagem; válidos só durante a chamada.</param>
    /// <param name="settings">Configurações em vigor neste passo.</param>
    void OnCommandsLatched(long stepIndex, ReadOnlySpan<bool> coils, ReadOnlySpan<ushort> holdingRegisters, IoSettings settings);
}
