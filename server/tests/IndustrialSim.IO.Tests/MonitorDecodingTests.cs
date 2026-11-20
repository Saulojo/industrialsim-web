namespace IndustrialSim.IO.Tests;

/// <summary>
/// Etapa 8: o monitor de registradores mostra, para cada linha do mapa, o valor de engenharia que o mestre
/// obteria ao escalar o registrador, em qualquer formato, e a lista de valores forçados.
/// </summary>
public class MonitorDecodingTests
{
    [Theory]
    [InlineData(AnalogFormat.Counts27648, WordOrder.HighWordFirst)]
    [InlineData(AnalogFormat.Counts10000, WordOrder.HighWordFirst)]
    [InlineData(AnalogFormat.Counts32767, WordOrder.HighWordFirst)]
    [InlineData(AnalogFormat.Float32, WordOrder.HighWordFirst)]
    [InlineData(AnalogFormat.Float32, WordOrder.LowWordFirst)]
    public void EngineeringValues_MatchThePlant_InEveryFormat(AnalogFormat format, WordOrder order)
    {
        PlantIo io = Caldeira.Create(new IoSettings(format, order));
        for (int i = 0; i < 50; i++)
        {
            io.Step();
        }

        IoSnapshot snapshot = io.Image.Snapshot();
        IReadOnlyList<IoMapEntry> entries = io.Map.Describe(snapshot.Format);
        double[] values = io.DecodeEngineering(entries, snapshot);

        // Uma contagem de 0–10000 em 0–16 bar vale 1,6 mbar: tolerância de meia contagem do formato mais grosso.
        double Tolerance(IoMapEntry entry, double span) =>
            format == AnalogFormat.Float32 ? Math.Abs(span) * 1e-6 : Math.Abs(span) / 10000.0 / 2.0 * 1.01;

        for (int k = 0; k < entries.Count; k++)
        {
            IoMapEntry entry = entries[k];
            switch (entry.Kind)
            {
                case IoPointKind.Analog when entry.Area == ModbusArea.InputRegister:
                    AnalogPoint point = io.Map.InputRegisters[entry.Index];
                    double expected = io.Plant.GetOutput(point.PlantIndex) * point.UnitFactor;
                    Assert.Equal(expected, values[k], Tolerance(entry, (point.Urv - point.Lrv) * point.UnitFactor));
                    break;
                case IoPointKind.Analog:
                    AnalogPoint command = io.Map.HoldingRegisters[entry.Index];
                    Assert.Equal(io.Plant.GetInput(command.PlantIndex) * command.UnitFactor, values[k], Tolerance(entry, (command.Urv - command.Lrv) * command.UnitFactor));
                    break;
                case IoPointKind.Diagnostic:
                    DiagnosticPoint diagnostic = io.Map.Diagnostics[entry.Index];
                    Assert.Equal((float)(io.Plant.GetOutput(diagnostic.PlantIndex) * diagnostic.UnitFactor), values[k]);
                    break;
                case IoPointKind.Digital when entry.Area == ModbusArea.Coil:
                    Assert.Equal(snapshot.Coils[entry.Address] ? 1.0 : 0.0, values[k]);
                    break;
                case IoPointKind.Digital or IoPointKind.WatchdogStatus:
                    Assert.Equal(snapshot.DiscreteInputs[entry.Address] ? 1.0 : 0.0, values[k]);
                    break;
                case IoPointKind.Heartbeat:
                    Assert.Equal(snapshot.HoldingRegisters[entry.Address], values[k]);
                    break;
            }
        }
    }

    /// <summary>Uma faixa trocada (como fará o HART) muda as contagens e o valor de engenharia continua certo.</summary>
    [Fact]
    public void AChangedRange_IsUsedToDecodeTheMeasurement()
    {
        PlantIo io = Caldeira.Create();
        io.SetRange(Caldeira.Level, 0.0, 2.4);
        io.Step();

        IoSnapshot snapshot = io.Image.Snapshot();
        IReadOnlyList<IoMapEntry> entries = io.Map.Describe(snapshot.Format);
        int level = entries.ToList().FindIndex(e => e.Area == ModbusArea.InputRegister && e.Signal == "LT-101");

        double plantLevel = io.Plant.GetOutput(io.Map.InputRegisters[0].PlantIndex);
        Assert.Equal(plantLevel, io.DecodeEngineering(entries, snapshot)[level], 2.4 / 27648.0);
    }

    [Fact]
    public void MapEntries_SayWhatEachPointIs()
    {
        IReadOnlyList<IoMapEntry> entries = Caldeira.Create().Map.Describe(AnalogFormat.Counts27648);

        Assert.Equal(
            [
                IoPointKind.Digital,
                IoPointKind.Digital, IoPointKind.Digital, IoPointKind.Digital, IoPointKind.Digital, IoPointKind.Digital,
                IoPointKind.WatchdogStatus,
                IoPointKind.Analog, IoPointKind.Analog, IoPointKind.Heartbeat,
                IoPointKind.Analog, IoPointKind.Analog, IoPointKind.Analog, IoPointKind.Analog, IoPointKind.Analog, IoPointKind.Analog,
                IoPointKind.Diagnostic, IoPointKind.Diagnostic, IoPointKind.Diagnostic, IoPointKind.Diagnostic, IoPointKind.Diagnostic,
            ],
            entries.Select(e => e.Kind));
        Assert.Equal([0, 1, 2, 3, 4], entries.Where(e => e.Kind == IoPointKind.Diagnostic).Select(e => e.Index));
    }

    [Fact]
    public void RequestedSettings_ShowAPendingChange_BeforeTheNextStep()
    {
        PlantIo io = Caldeira.Create();
        io.UpdateSettings(new IoSettings(AnalogFormat.Float32));

        Assert.Equal(AnalogFormat.Counts27648, io.Settings.Format);
        Assert.Equal(AnalogFormat.Float32, io.RequestedSettings.Format);
        io.Step();
        Assert.Equal(AnalogFormat.Float32, io.Settings.Format);
    }

    [Fact]
    public void ForcedValues_AreListedWithTheirSignals_AndRepublishShowsThemWithoutAStep()
    {
        PlantIo io = Caldeira.Create();
        io.ForceInputRegister(Caldeira.Pressure, 12.5);
        io.ForceDiscreteInput(Caldeira.WatchdogStatus, true);

        Assert.Equal(
            [
                new ForcedInput(ModbusArea.DiscreteInput, Caldeira.WatchdogStatus, IoMap.WatchdogSignal, 1.0),
                new ForcedInput(ModbusArea.InputRegister, Caldeira.Pressure, "PT-101", 12.5),
            ],
            io.GetForced());

        io.RepublishInputs();
        IoSnapshot snapshot = io.Image.Snapshot();
        Assert.Equal(0, snapshot.StepCount);
        Assert.Equal(21600, snapshot.Counts(Caldeira.Pressure)); // 12,5 bar em 0–16 bar
        Assert.True(snapshot.ForcedInputRegisters[Caldeira.Pressure]);
        Assert.True(snapshot.DiscreteInputs[Caldeira.WatchdogStatus]);
    }
}
