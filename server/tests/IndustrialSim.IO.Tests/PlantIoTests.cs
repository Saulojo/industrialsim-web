namespace IndustrialSim.IO.Tests;

/// <summary>Critérios 4 a 7 da Etapa 4, com a caldeira: a fronteira entre a planta e o CLP.</summary>
public class PlantIoTests(ITestOutputHelper output)
{
    private const AnalogFormat Siemens = AnalogFormat.Counts27648;

    /// <summary>Antes do primeiro passo, a imagem já mostra a planta em regime e os comandos iniciais do cenário.</summary>
    [Fact]
    public void InitialImage_ShowsThePlantAtItsInitialCondition()
    {
        PlantIo io = Caldeira.Create();
        IoSnapshot snapshot = io.Image.Snapshot();

        Assert.Equal(Siemens, snapshot.Format);
        Assert.Equal(0, snapshot.StepCount);
        Assert.True(snapshot.Coils[Caldeira.PumpRun]);
        Assert.True(snapshot.DiscreteInputs[Caldeira.PumpRunning]);
        Assert.Equal(22749, AnalogCodec.FromRegister(snapshot.HoldingRegisters[Caldeira.WaterValveCommand])); // 82,28%
        Assert.Equal(13824, AnalogCodec.FromRegister(snapshot.HoldingRegisters[Caldeira.FuelValveCommand])); // 50%
        Assert.Equal(13824, snapshot.Counts(Caldeira.Level)); // 0,6 m em 0–1,2 m
        Assert.Equal(17280, snapshot.Counts(Caldeira.Pressure)); // 10 bar em 0–16 bar
        Assert.Equal(0.6f, snapshot.Float(Caldeira.Diagnostics));
        Assert.Equal(10.0f, snapshot.Float(Caldeira.Diagnostics + 2));
        Assert.Equal(50.0f, snapshot.Float(Caldeira.Diagnostics + 8));
        Assert.DoesNotContain(true, snapshot.ForcedInputRegisters);
    }

    /// <summary>
    /// Critério 4: trocar o formato com a planta rodando muda os valores no passo seguinte, sem reiniciar.
    /// Uma planta troca de formato três vezes; a outra fica em 0–27648. Os comandos (50% e 25%) são exatos em
    /// todos os formatos usados, então as duas têm de terminar no mesmo estado, bit a bit.
    /// </summary>
    [Fact]
    public void ChangingTheFormatWhileRunning_TakesEffectOnTheNextStep_WithoutRestarting()
    {
        PlantIo reference = Caldeira.Create();
        PlantIo io = Caldeira.Create();
        foreach (PlantIo plant in new[] { reference, io })
        {
            plant.WriteCounts(Caldeira.WaterValveCommand, 13824);
            plant.WriteCounts(Caldeira.FuelValveCommand, 6912);
        }

        StepBoth(100);

        // 0–27648 → 0–10000
        io.UpdateSettings(io.Settings with { Format = AnalogFormat.Counts10000 });
        IoSnapshot before = io.Image.Snapshot();
        Assert.Equal(Siemens, before.Format); // ainda não houve passo: nada mudou
        StepBoth(1);
        IoSnapshot after = io.Image.Snapshot();
        output.WriteLine($"passo {before.StepCount}: LT-101 = {before.Counts(Caldeira.Level)} (0–27648)");
        output.WriteLine($"passo {after.StepCount}: LT-101 = {after.Counts(Caldeira.Level)} (0–10000)");
        Assert.Equal(AnalogFormat.Counts10000, after.Format);
        Assert.Equal(101, after.StepCount);
        Assert.Equal(ExpectedCounts(io, "LT-101", 1.2, AnalogFormat.Counts10000), after.Counts(Caldeira.Level));
        Assert.Equal(ExpectedCounts(io, "PT-101", 1.6e6, AnalogFormat.Counts10000), after.Counts(Caldeira.Pressure));
        Assert.Equal(ExpectedCounts(reference, "LT-101", 1.2, Siemens), reference.Image.Snapshot().Counts(Caldeira.Level));
        Assert.Equal(5000, AnalogCodec.FromRegister(after.HoldingRegisters[Caldeira.WaterValveCommand])); // comando regravado
        Assert.Equal(2500, AnalogCodec.FromRegister(after.HoldingRegisters[Caldeira.FuelValveCommand]));

        StepBoth(99);

        // 0–10000 → float32 (cada sinal passa a ocupar 2 registradores: 2a e 2a+1)
        io.UpdateSettings(io.Settings with { Format = AnalogFormat.Float32 });
        StepBoth(1);
        after = io.Image.Snapshot();
        output.WriteLine($"passo {after.StepCount}: LT-101 = {after.Float(0)} m, PT-101 = {after.Float(2)} bar (float32)");
        Assert.Equal(AnalogFormat.Float32, after.Format);
        Assert.Equal((float)io.Plant.GetOutput("LT-101"), after.Float(0), 1e-5f);
        Assert.Equal((float)(io.Plant.GetOutput("PT-101") * 1e-5), after.Float(2), 1e-4f);
        Assert.Equal(50.0f, AnalogCodec.ReadFloat32([after.HoldingRegisters[0], after.HoldingRegisters[1]], WordOrder.HighWordFirst));
        Assert.Equal(25.0f, AnalogCodec.ReadFloat32([after.HoldingRegisters[2], after.HoldingRegisters[3]], WordOrder.HighWordFirst));

        StepBoth(99);

        // float32 normal → float32 com as palavras invertidas
        io.UpdateSettings(io.Settings with { WordOrder = WordOrder.LowWordFirst });
        StepBoth(1);
        after = io.Image.Snapshot();
        Assert.Equal(WordOrder.LowWordFirst, after.WordOrder);
        Assert.Equal((float)io.Plant.GetOutput("LT-101"), after.Float(0), 1e-5f);
        Assert.Equal(50.0f, AnalogCodec.ReadFloat32([after.HoldingRegisters[0], after.HoldingRegisters[1]], WordOrder.LowWordFirst));

        StepBoth(99);

        // Não reiniciou: mesmo número de passos e mesmo estado da planta que nunca trocou de formato.
        output.WriteLine($"hash com trocas = {io.Plant.StateHash():X16}; sem trocas = {reference.Plant.StateHash():X16}");
        Assert.Equal(400, io.Plant.Clock.StepCount);
        Assert.Equal(reference.Plant.StateHash(), io.Plant.StateHash());

        void StepBoth(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                reference.Step();
                io.Step();
            }
        }
    }

    [Theory]
    [InlineData(AnalogFormat.Counts27648, 13824)]
    [InlineData(AnalogFormat.Counts10000, 5000)]
    [InlineData(AnalogFormat.Counts32767, 16384)]
    public void EachIntegerFormat_ScalesTheSameMeasurement(AnalogFormat format, int expectedLevelCounts)
    {
        PlantIo io = Caldeira.Create(new IoSettings(Format: format));

        Assert.Equal(expectedLevelCounts, io.Image.Snapshot().Counts(Caldeira.Level));
    }

    /// <summary>Critério 5: um valor forçado ignora a planta até ser liberado.</summary>
    [Fact]
    public void ForcedValue_IgnoresThePlantUntilReleased()
    {
        PlantIo io = Caldeira.Create();
        io.WriteCounts(Caldeira.WaterValveCommand, 0); // fecha a água: o nível real vai cair
        io.Step();

        Assert.True(io.ForceInputRegister(Caldeira.Level, 0.9)); // 0,9 m
        Assert.True(io.ForceDiscreteInput(Caldeira.LevelLowLow, true));
        Assert.False(io.Image.Snapshot().ForcedInputRegisters[Caldeira.Level]); // só vale no passo seguinte

        double levelAtStart = io.Plant.GetOutput("LT-101");
        for (int i = 0; i < 2000; i++)
        {
            io.Step();
            IoSnapshot snapshot = io.Image.Snapshot();
            Assert.Equal(20736, snapshot.Counts(Caldeira.Level)); // 0,9 m em 0–1,2 m = 75%
            Assert.True(snapshot.ForcedInputRegisters[Caldeira.Level]);
            Assert.True(snapshot.DiscreteInputs[Caldeira.LevelLowLow]);
            Assert.True(snapshot.ForcedDiscreteInputs[Caldeira.LevelLowLow]);
            Assert.False(snapshot.ForcedInputRegisters[Caldeira.Pressure]);
        }

        double levelNow = io.Plant.GetOutput("LT-101");
        output.WriteLine($"nível real: {levelAtStart:F4} m -> {levelNow:F4} m; registrador forçado em 20736 por 2000 passos");
        Assert.True(levelNow < levelAtStart - 0.005, "a planta deveria ter continuado a evoluir por baixo do valor forçado");

        // Forçado também ignora a falha do transmissor.
        io.SetFault(Caldeira.Level, TransmitterFault.Low);
        io.Step();
        Assert.Equal(20736, io.Image.Snapshot().Counts(Caldeira.Level));
        io.SetFault(Caldeira.Level, TransmitterFault.None);

        Assert.True(io.ReleaseInputRegister(Caldeira.Level));
        Assert.True(io.ReleaseDiscreteInput(Caldeira.LevelLowLow));
        io.Step();
        IoSnapshot released = io.Image.Snapshot();
        output.WriteLine($"liberado: registrador = {released.Counts(Caldeira.Level)}");
        Assert.Equal(ExpectedCounts(io, "LT-101", 1.2, Siemens), released.Counts(Caldeira.Level));
        Assert.False(released.ForcedInputRegisters[Caldeira.Level]);
        Assert.False(released.ForcedDiscreteInputs[Caldeira.LevelLowLow]);
        Assert.Equal(io.Plant.GetOutput("LSLL-101") >= 0.5, released.DiscreteInputs[Caldeira.LevelLowLow]);
    }

    /// <summary>D30: o valor forçado é em unidade de engenharia e continua valendo se o formato mudar.</summary>
    [Fact]
    public void ForcedValue_SurvivesAFormatChange()
    {
        PlantIo io = Caldeira.Create();
        io.ForceInputRegister(Caldeira.Pressure, 8.0); // 8 bar
        io.Step();
        Assert.Equal(13824, io.Image.Snapshot().Counts(Caldeira.Pressure)); // 8 de 16 bar = 50%

        io.UpdateSettings(io.Settings with { Format = AnalogFormat.Float32 });
        io.Step();
        IoSnapshot snapshot = io.Image.Snapshot();
        Assert.Equal(8.0f, snapshot.Float(2));
        Assert.True(snapshot.ForcedInputRegisters[2]);
        Assert.True(snapshot.ForcedInputRegisters[3]);

        io.ReleaseAll();
        io.Step();
        Assert.DoesNotContain(true, io.Image.Snapshot().ForcedInputRegisters);
    }

    [Fact]
    public void ForcingAnAddressOutsideTheMap_IsRefused()
    {
        PlantIo io = Caldeira.Create();

        Assert.False(io.ForceInputRegister(7, 1.0));
        Assert.False(io.ForceInputRegister(Caldeira.Diagnostics, 1.0));
        Assert.False(io.ForceDiscreteInput(6, true));
        Assert.True(io.ForceDiscreteInput(Caldeira.WatchdogStatus, true));
    }

    /// <summary>
    /// Critério 6: um comando escrito no meio de um passo só muda a planta no passo seguinte.
    /// O passo é feito fase a fase; a escrita cai depois da cópia dos comandos. Uma planta gêmea, sem a
    /// escrita, serve de referência: as duas ficam idênticas nesse passo e só divergem no seguinte.
    /// </summary>
    [Fact]
    public void CommandWrittenInTheMiddleOfAStep_OnlyReachesThePlantOnTheNextStep()
    {
        PlantIo io = Caldeira.Create();
        PlantIo twin = Caldeira.Create();
        for (int i = 0; i < 50; i++)
        {
            io.Step();
            twin.Step();
        }

        double commandBefore = io.Plant.GetInput("FV-101_command");

        io.LatchCommands(); // início do passo 51: os comandos já foram copiados
        io.WriteCounts(Caldeira.WaterValveCommand, 0); // o mestre escreve 0% no meio do passo
        Assert.True(io.Image.TryWriteCoil(Caldeira.PumpRun, false));
        io.Plant.Step();
        io.PublishInputs();
        twin.Step();

        output.WriteLine($"passo 51 (escrita no meio): comando na planta = {io.Plant.GetInput("FV-101_command"):F4}, " +
            $"hash = {io.Plant.StateHash():X16}, gêmea = {twin.Plant.StateHash():X16}");
        Assert.Equal(commandBefore, io.Plant.GetInput("FV-101_command"));
        Assert.Equal(1.0, io.Plant.GetInput("P-101_run"));
        Assert.Equal(twin.Plant.StateHash(), io.Plant.StateHash());
        Assert.Equal(0, AnalogCodec.FromRegister(io.Image.Snapshot().HoldingRegisters[Caldeira.WaterValveCommand])); // a imagem já tem o valor

        io.Step();
        twin.Step();

        output.WriteLine($"passo 52: comando na planta = {io.Plant.GetInput("FV-101_command"):F4}, " +
            $"hash = {io.Plant.StateHash():X16}, gêmea = {twin.Plant.StateHash():X16}");
        Assert.Equal(0.0, io.Plant.GetInput("FV-101_command"));
        Assert.Equal(0.0, io.Plant.GetInput("P-101_run"));
        Assert.NotEqual(twin.Plant.StateHash(), io.Plant.StateHash());
    }

    /// <summary>
    /// Critério 7: com o watchdog ligado e o heartbeat parado por 2 s (200 passos), FV-101 vai a 100%, FV-102 a
    /// 0%, a entrada discreta 5 sobe e P-101 continua como estava; com o heartbeat de volta, os comandos do
    /// mestre voltam a valer.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Watchdog_GoesToTheSafeState_AndRecoversWithTheHeartbeat(bool pumpOn)
    {
        PlantIo io = Caldeira.Create(new IoSettings(WatchdogEnabled: true));
        Assert.Equal(2.0, io.Settings.WatchdogTimeoutSeconds);

        io.WriteCounts(Caldeira.WaterValveCommand, 6912); // 25%
        io.WriteCounts(Caldeira.FuelValveCommand, 13824); // 50%
        Assert.True(io.Image.TryWriteCoil(Caldeira.PumpRun, pumpOn));
        Assert.True(io.Image.TryWriteHoldingRegister(Caldeira.Heartbeat, 1));

        io.Step(); // o passo 1 vê o heartbeat mudar
        for (int i = 0; i < 199; i++)
        {
            io.Step(); // passos 2 a 200: 199 passos (1,99 s) sem mudança
        }

        Assert.False(io.WatchdogTripped);
        Assert.False(io.Image.Snapshot().DiscreteInputs[Caldeira.WatchdogStatus]);
        Assert.Equal(0.25, io.Plant.GetInput("FV-101_command"));
        Assert.Equal(0.5, io.Plant.GetInput("FV-102_command"));

        io.Step(); // passo 201: 200 passos (2,00 s) sem mudança
        IoSnapshot tripped = io.Image.Snapshot();
        output.WriteLine($"passo {tripped.StepCount} (t = {io.Plant.Clock.TimeSeconds:F2} s): watchdog disparado = {io.WatchdogTripped}");
        Assert.True(io.WatchdogTripped);
        Assert.True(tripped.DiscreteInputs[Caldeira.WatchdogStatus]);
        Assert.True(tripped.WatchdogTripped);
        Assert.Equal(1.0, io.Plant.GetInput("FV-101_command"));
        Assert.Equal(0.0, io.Plant.GetInput("FV-102_command"));
        Assert.Equal(pumpOn ? 1.0 : 0.0, io.Plant.GetInput("P-101_run"));

        for (int i = 0; i < 3000; i++)
        {
            io.Step(); // 30 s = 15 constantes de tempo das válvulas
        }

        IoSnapshot safe = io.Image.Snapshot();
        output.WriteLine($"30 s depois: FV-101 = {io.Plant.GetOutput("FV-101_position"):P2} ({safe.Counts(Caldeira.WaterValvePosition)}), " +
            $"FV-102 = {io.Plant.GetOutput("FV-102_position"):P2} ({safe.Counts(Caldeira.WaterValvePosition + 1)}), " +
            $"P-101 = {io.Plant.GetOutput("P-101_running")}, entrada discreta 5 = {safe.DiscreteInputs[Caldeira.WatchdogStatus]}");
        Assert.Equal(27648, safe.Counts(Caldeira.WaterValvePosition)); // FV-101 a 100%
        Assert.Equal(0, safe.Counts(Caldeira.WaterValvePosition + 1)); // FV-102 a 0%
        Assert.Equal(pumpOn, safe.DiscreteInputs[Caldeira.PumpRunning]);
        Assert.True(safe.DiscreteInputs[Caldeira.WatchdogStatus]);

        // O que o mestre escreveu continua nos registradores; só não vale enquanto o watchdog está disparado.
        Assert.Equal(6912, AnalogCodec.FromRegister(safe.HoldingRegisters[Caldeira.WaterValveCommand]));
        Assert.Equal(13824, AnalogCodec.FromRegister(safe.HoldingRegisters[Caldeira.FuelValveCommand]));

        Assert.True(io.Image.TryWriteHoldingRegister(Caldeira.Heartbeat, 2)); // o heartbeat volta
        io.Step();
        IoSnapshot recovered = io.Image.Snapshot();
        output.WriteLine($"heartbeat de volta: watchdog = {io.WatchdogTripped}, FV-101 = {io.Plant.GetInput("FV-101_command")}, " +
            $"FV-102 = {io.Plant.GetInput("FV-102_command")}");
        Assert.False(io.WatchdogTripped);
        Assert.False(recovered.DiscreteInputs[Caldeira.WatchdogStatus]);
        Assert.Equal(0.25, io.Plant.GetInput("FV-101_command"));
        Assert.Equal(0.5, io.Plant.GetInput("FV-102_command"));
    }

    [Fact]
    public void Watchdog_IsOffByDefault_AndNeverTripsWhileOff()
    {
        PlantIo io = Caldeira.Create();
        Assert.False(io.Settings.WatchdogEnabled);

        for (int i = 0; i < 1000; i++)
        {
            io.Step();
        }

        Assert.False(io.WatchdogTripped);
        Assert.False(io.Image.Snapshot().DiscreteInputs[Caldeira.WatchdogStatus]);
        Assert.Equal(13824.0 / 27648.0, io.Plant.GetInput("FV-102_command"));
    }

    [Fact]
    public void Watchdog_TimeoutIsConfigurable_AndCanBeSwitchedOffWhileTripped()
    {
        PlantIo io = Caldeira.Create(new IoSettings(WatchdogEnabled: true, WatchdogTimeoutSeconds: 0.5));

        for (int i = 0; i < 49; i++)
        {
            io.Step();
        }

        Assert.False(io.WatchdogTripped);
        io.Step(); // 50 passos = 0,5 s sem heartbeat
        Assert.True(io.WatchdogTripped);

        io.UpdateSettings(io.Settings with { WatchdogEnabled = false });
        io.Step();
        Assert.False(io.WatchdogTripped);
        Assert.False(io.Image.Snapshot().DiscreteInputs[Caldeira.WatchdogStatus]);
    }

    /// <summary>Transmissor em falha: correntes da NAMUR NE 43, vistas pelo mestre em cada formato.</summary>
    [Fact]
    public void TransmitterFault_DrivesTheNe43FaultCurrents()
    {
        PlantIo io = Caldeira.Create();

        io.SetFault(Caldeira.Level, TransmitterFault.Low);
        io.SetFault(Caldeira.Pressure, TransmitterFault.High);
        io.Step();
        IoSnapshot snapshot = io.Image.Snapshot();
        Assert.Equal(-691, snapshot.Counts(Caldeira.Level));
        Assert.Equal(29376, snapshot.Counts(Caldeira.Pressure));

        io.UpdateSettings(io.Settings with { Format = AnalogFormat.Float32 });
        io.Step();
        snapshot = io.Image.Snapshot();
        Assert.Equal(-0.03f, snapshot.Float(0), 1e-6f); // 3,6 mA numa faixa de 0–1,2 m
        Assert.Equal(17.0f, snapshot.Float(2), 1e-5f); // 21 mA numa faixa de 0–16 bar

        io.SetFault(Caldeira.Level, TransmitterFault.None);
        io.Step();
        Assert.Equal((float)io.Plant.GetOutput("LT-101"), io.Image.Snapshot().Float(0), 1e-5f);
    }

    /// <summary>A faixa (LRV/URV) muda a corrente e as contagens; fora da faixa, a medição satura em 3,8 e 20,5 mA.</summary>
    [Fact]
    public void Range_ChangesTheCounts_AndTheSignalSaturatesAtNe43Limits()
    {
        PlantIo io = Caldeira.Create();
        io.ForceInputRegister(Caldeira.Level, 0.6);

        io.SetRange(Caldeira.Level, 0.0, 2.4);
        io.Step();
        Assert.Equal(6912, io.Image.Snapshot().Counts(Caldeira.Level)); // 0,6 m em 0–2,4 m = 25%
        Assert.Equal((0.0, 2.4), io.GetRange(Caldeira.Level));

        io.SetRange(Caldeira.Level, 0.0, 0.3);
        io.Step();
        Assert.Equal(28512, io.Image.Snapshot().Counts(Caldeira.Level)); // acima da faixa: 20,5 mA

        io.SetRange(Caldeira.Level, 1.0, 2.0);
        io.Step();
        Assert.Equal(-346, io.Image.Snapshot().Counts(Caldeira.Level)); // abaixo da faixa: 3,8 mA

        Assert.Throws<ArgumentException>(() => io.SetRange(Caldeira.Level, 1.0, 1.0));
        Assert.Throws<KeyNotFoundException>(() => io.SetRange(9, 0.0, 1.0));
    }

    [Fact]
    public void Commands_AreClampedToTheirRange_AndFloat32CommandsUseEngineeringUnits()
    {
        PlantIo io = Caldeira.Create();

        io.WriteCounts(Caldeira.WaterValveCommand, 32000); // sobrefaixa
        io.WriteCounts(Caldeira.FuelValveCommand, -691); // 3,6 mA
        io.Step();
        Assert.Equal(1.0, io.Plant.GetInput("FV-101_command"));
        Assert.Equal(0.0, io.Plant.GetInput("FV-102_command"));

        io.UpdateSettings(io.Settings with { Format = AnalogFormat.Float32 });
        io.Step();

        Span<ushort> registers = stackalloc ushort[2];
        AnalogCodec.WriteFloat32(30.0f, WordOrder.HighWordFirst, registers); // 30%
        Assert.True(io.Image.TryWriteHoldingRegisters(0, registers));
        io.Step();
        Assert.Equal(0.3, io.Plant.GetInput("FV-101_command"));

        AnalogCodec.WriteFloat32(float.NaN, WordOrder.HighWordFirst, registers); // não é número: o comando fica como estava
        Assert.True(io.Image.TryWriteHoldingRegisters(0, registers));
        io.Step();
        Assert.Equal(0.3, io.Plant.GetInput("FV-101_command"));
    }

    [Fact]
    public void DiagnosticsBlock_CanBeSwitchedOff()
    {
        PlantIo io = Caldeira.Create();
        io.Step();
        Assert.NotEqual(0.0f, io.Image.Snapshot().Float(Caldeira.Diagnostics));

        io.UpdateSettings(io.Settings with { DiagnosticsEnabled = false });
        io.Step();
        IoSnapshot snapshot = io.Image.Snapshot();
        Assert.All(Enumerable.Range(Caldeira.Diagnostics, 10), register => Assert.Equal(0, snapshot.InputRegisters[register]));
        Assert.NotEqual(0, snapshot.Counts(Caldeira.Level)); // os sinais do mapa continuam
    }

    [Fact]
    public void InvalidSettings_AreRefused()
    {
        PlantIo io = Caldeira.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => io.UpdateSettings(io.Settings with { WatchdogTimeoutSeconds = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => io.UpdateSettings(io.Settings with { Format = (AnalogFormat)99 }));
    }

    /// <summary>Contagens esperadas para a saída atual da planta, numa faixa de 0 a <paramref name="urv"/>.</summary>
    private static short ExpectedCounts(PlantIo io, string output, double urv, AnalogFormat format) =>
        AnalogCodec.MilliampsToCounts(CurrentLoop.Saturate(CurrentLoop.ToMilliamps(io.Plant.GetOutput(output), 0.0, urv)), format);
}
