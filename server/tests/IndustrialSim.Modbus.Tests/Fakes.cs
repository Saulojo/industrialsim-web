using System.Net;
using System.Threading.Channels;

namespace IndustrialSim.Modbus.Tests;

/// <summary>Relógio controlado pelo teste: o tempo só anda por <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (_gate)
        {
            _utcNow += duration;
        }
    }
}

/// <summary>Como o pareamento de teste responde.</summary>
internal enum GateMode
{
    Allow,
    Refuse,
    Hold,
}

/// <summary>
/// Pareamento de teste: autoriza, recusa ou segura cada conexão até o teste decidir, e conta as conexões
/// autorizadas ainda abertas.
/// </summary>
internal sealed class TestGate(GateMode mode = GateMode.Allow) : IConnectionGate
{
    private readonly Channel<HeldRequest> _held = Channel.CreateUnbounded<HeldRequest>();
    private int _active;

    public int ActiveLeases => Volatile.Read(ref _active);

    public List<IPAddress> Addresses { get; } = [];

    public Task<IDisposable?> AuthorizeAsync(IPAddress remoteAddress, CancellationToken cancellationToken)
    {
        lock (Addresses)
        {
            Addresses.Add(remoteAddress);
        }

        switch (mode)
        {
            case GateMode.Allow:
                return Task.FromResult<IDisposable?>(NewLease());
            case GateMode.Refuse:
                return Task.FromResult<IDisposable?>(null);
            default:
                var request = new HeldRequest(remoteAddress, this);
                _held.Writer.TryWrite(request);
                return request.Decision.Task;
        }
    }

    /// <summary>Próxima conexão segurada pelo pareamento.</summary>
    public async Task<HeldRequest> NextHeldAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _held.Reader.ReadAsync(timeout.Token);
    }

    private Lease NewLease()
    {
        Interlocked.Increment(ref _active);
        return new Lease(this);
    }

    internal sealed class HeldRequest(IPAddress address, TestGate gate)
    {
        public IPAddress Address { get; } = address;

        public TaskCompletionSource<IDisposable?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Allow() => Decision.SetResult(gate.NewLease());

        public void Refuse() => Decision.SetResult(null);
    }

    private sealed class Lease(TestGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref gate._active);
            }
        }
    }
}
