namespace IndustrialSim.Sessions.Tests;

/// <summary>Etapa 8: o log de comunicação guarda os últimos eventos, em ordem, e agrupa as repetições.</summary>
public class PlantEventLogTests
{
    private static readonly PlantEventArgument Address = new("address", "10.0.0.9");

    [Fact]
    public void RepeatedExceptions_AreGrouped_AndMovedToTheEnd_WithANewSequence()
    {
        var time = new ManualTimeProvider();
        var log = new PlantEventLog(time);
        PlantEvent first = log.Add(PlantEventKinds.ModbusException, Address, new("code", "2"));
        log.Add(PlantEventKinds.ConnectionAuthorized, Address);
        time.Advance(TimeSpan.FromSeconds(5));

        PlantEvent grouped = log.Add(PlantEventKinds.ModbusException, Address, new("code", "2"));

        Assert.Equal(first.Id, grouped.Id);
        Assert.Equal(2, grouped.Count);
        Assert.Equal(first.FirstUtc, grouped.FirstUtc);
        Assert.Equal(first.FirstUtc.AddSeconds(5), grouped.TimeUtc);
        Assert.Equal(3, grouped.Sequence);
        Assert.Equal([PlantEventKinds.ConnectionAuthorized, PlantEventKinds.ModbusException], log.All().Select(e => e.Kind));
        Assert.Equal([grouped], log.Since(2));
    }

    [Fact]
    public void DifferentArguments_OrEventsOutsideTheWindow_OrOtherKinds_AreNotGrouped()
    {
        var time = new ManualTimeProvider();
        var log = new PlantEventLog(time);
        log.Add(PlantEventKinds.ModbusException, Address, new("code", "2"));
        log.Add(PlantEventKinds.ModbusException, Address, new("code", "3"));
        time.Advance(PlantEventLog.GroupingWindow);
        log.Add(PlantEventKinds.ModbusException, Address, new("code", "3"));
        log.Add(PlantEventKinds.SettingChanged, new PlantEventArgument("setting", "unitId"));
        log.Add(PlantEventKinds.SettingChanged, new PlantEventArgument("setting", "unitId"));

        Assert.Equal(5, log.All().Count);
        Assert.All(log.All(), e => Assert.Equal(1, e.Count));
        Assert.Equal(5, log.LastSequence);
    }

    [Fact]
    public void OnlyTheLastEventsAreKept()
    {
        var log = new PlantEventLog(new ManualTimeProvider());
        for (int i = 0; i < PlantEventLog.Capacity + 20; i++)
        {
            log.Add(PlantEventKinds.SettingChanged, new PlantEventArgument("new", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        IReadOnlyList<PlantEvent> events = log.All();
        Assert.Equal(PlantEventLog.Capacity, events.Count);
        Assert.Equal("20", events[0].Arguments[0].Value);
        Assert.Equal(PlantEventLog.Capacity + 20, events[^1].Sequence);
        Assert.Empty(log.Since(log.LastSequence));
    }
}
