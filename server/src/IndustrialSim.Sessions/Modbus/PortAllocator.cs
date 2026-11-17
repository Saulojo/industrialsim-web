namespace IndustrialSim.Sessions.Modbus;

/// <summary>
/// Portas Modbus da faixa das plantas: cada planta em execução fica com uma, devolvida quando ela termina.
/// </summary>
/// <remarks>
/// A busca começa depois da última porta entregue e dá a volta na faixa, para que a porta de uma planta que
/// acabou de terminar não vá logo para outra planta: o mestre da planta antiga, se reconectar, não cai na nova
/// (e, se cair, ainda passa pelo pareamento). Uma porta ocupada por outro programa é pulada.
/// </remarks>
internal sealed class PortAllocator
{
    private readonly Lock _gate = new();
    private readonly HashSet<int> _inUse = [];
    private readonly int _start;
    private readonly int _end;
    private int _next;

    public PortAllocator(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        _start = start;
        _end = end;
        _next = start;
    }

    public int InUseCount
    {
        get
        {
            lock (_gate)
            {
                return _inUse.Count;
            }
        }
    }

    /// <summary>
    /// Entrega a primeira porta livre em que <paramref name="tryBind"/> conseguir escutar; nula se nenhuma da
    /// faixa servir.
    /// </summary>
    public int? Allocate(Func<int, bool> tryBind)
    {
        ArgumentNullException.ThrowIfNull(tryBind);
        lock (_gate)
        {
            int size = _end - _start + 1;
            for (int i = 0; i < size; i++)
            {
                int port = _start + ((_next - _start + i) % size);
                if (_inUse.Contains(port) || !tryBind(port))
                {
                    continue;
                }

                _inUse.Add(port);
                _next = port == _end ? _start : port + 1;
                return port;
            }

            return null;
        }
    }

    public void Release(int port)
    {
        lock (_gate)
        {
            _inUse.Remove(port);
        }
    }
}
