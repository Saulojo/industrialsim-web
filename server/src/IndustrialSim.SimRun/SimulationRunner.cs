using System.Globalization;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.Core.Simulation;

namespace IndustrialSim.SimRun;

/// <summary>Resultado de uma execução: passos dados, linhas escritas e hash de determinismo.</summary>
/// <param name="Steps">Número de passos simulados.</param>
/// <param name="RowsWritten">Linhas de dados escritas no CSV de saída.</param>
/// <param name="StateHash">Hash do estado acumulado em todos os passos (RNF02): igual entre execuções iguais.</param>
public sealed record RunResult(long Steps, long RowsWritten, ulong StateHash);

/// <summary>
/// Roda uma planta seguindo um roteiro de entradas e escreve o resultado em CSV: uma coluna <c>time</c>,
/// depois as entradas externas e as saídas externas do cenário, na ordem do arquivo.
/// </summary>
/// <remarks>
/// Uma linha do roteiro com tempo t vale a partir do passo que começa em t (o passo de índice t/dt, arredondado).
/// A saída tem uma linha para o instante inicial e depois uma a cada <c>outputEverySteps</c> passos; cada linha
/// mostra o estado no fim do passo e as entradas que valeram nele. Os números saem com ponto decimal e todos os
/// dígitos necessários para reler o mesmo valor (formato "R").
/// </remarks>
public static class SimulationRunner
{
    public static RunResult Run(ScenarioPlant plant, InputScript script, TextWriter output, double durationSeconds, int outputEverySteps = 1)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputEverySteps, 1);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "A duração deve ser finita e não negativa.");
        }

        int[] inputIndexes = MapInputs(plant, script);
        double step = plant.Clock.StepSeconds;
        long steps = (long)Math.Round(durationSeconds / step);

        output.Write(InputScript.TimeColumn);
        foreach (string name in plant.InputNames.Concat(plant.OutputNames))
        {
            output.Write(',');
            output.Write(name);
        }

        output.Write('\n');

        int nextRow = 0;
        long rowsWritten = 0;
        var hasher = default(StateHasher);

        ApplyRows(plant, script, inputIndexes, ref nextRow, stepIndex: 0, step);
        WriteRow(plant, output);
        rowsWritten++;

        for (long n = 0; n < steps; n++)
        {
            ApplyRows(plant, script, inputIndexes, ref nextRow, n, step);
            plant.Step();
            plant.AddStateTo(ref hasher);

            if ((n + 1) % outputEverySteps == 0)
            {
                WriteRow(plant, output);
                rowsWritten++;
            }
        }

        return new RunResult(steps, rowsWritten, hasher.Value);
    }

    /// <summary>Confere que toda coluna do roteiro é uma entrada externa do cenário.</summary>
    private static int[] MapInputs(ScenarioPlant plant, InputScript script)
    {
        var indexes = new int[script.InputNames.Count];
        for (int i = 0; i < indexes.Length; i++)
        {
            string name = script.InputNames[i];
            int index = -1;
            for (int k = 0; k < plant.InputNames.Count; k++)
            {
                if (plant.InputNames[k] == name)
                {
                    index = k;
                    break;
                }
            }

            if (index < 0)
            {
                string available = plant.InputNames.Count == 0 ? "nenhuma" : string.Join(", ", plant.InputNames);
                throw new FormatException($"a coluna '{name}' do roteiro não é uma entrada do cenário '{plant.Definition.Id}'. Entradas: {available}.");
            }

            indexes[i] = index;
        }

        return indexes;
    }

    private static void ApplyRows(ScenarioPlant plant, InputScript script, int[] inputIndexes, ref int nextRow, long stepIndex, double step)
    {
        while (nextRow < script.Rows.Count && (long)Math.Round(script.Rows[nextRow].TimeSeconds / step) <= stepIndex)
        {
            InputScript.Row row = script.Rows[nextRow++];
            for (int i = 0; i < inputIndexes.Length; i++)
            {
                if (row.Values[i] is double value)
                {
                    plant.SetInput(inputIndexes[i], value);
                }
            }
        }
    }

    private static void WriteRow(ScenarioPlant plant, TextWriter output)
    {
        output.Write(Format(plant.Clock.TimeSeconds));
        for (int i = 0; i < plant.InputNames.Count; i++)
        {
            output.Write(',');
            output.Write(Format(plant.GetInput(i)));
        }

        for (int i = 0; i < plant.OutputNames.Count; i++)
        {
            output.Write(',');
            output.Write(Format(plant.GetOutput(i)));
        }

        output.Write('\n');
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
