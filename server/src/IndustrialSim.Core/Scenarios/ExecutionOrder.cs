namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Ordem em que os componentes avançam dentro de um passo, calculada pelas ligações.
/// </summary>
/// <remarks>
/// Regra: quem fornece um sinal avança antes de quem o consome (ordem topológica; empate decidido pela ordem
/// do arquivo, para a ordem ser sempre a mesma). Num laço de realimentação isso é impossível para uma das
/// ligações; essa ligação passa a entregar o valor do passo anterior, e é escolhida de modo a entrar num
/// componente com estado (o primeiro do laço, pela ordem do arquivo). Um laço só de componentes sem estado
/// é um laço algébrico: não há estado que justifique o atraso, e o cenário é recusado.
/// Algoritmos: ordenação topológica de Kahn (Kahn, A. B., Commun. ACM, v. 5, n. 11, 1962) e componentes
/// fortemente conexos de Tarjan (Tarjan, R., SIAM J. Comput., v. 1, n. 2, 1972).
/// </remarks>
internal static class ExecutionOrder
{
    /// <param name="count">Número de componentes (índices na ordem do arquivo).</param>
    /// <param name="edges">Ligações (fornecedor → consumidor), por índice de componente.</param>
    /// <param name="hasState">Diz se o componente tem estado.</param>
    /// <param name="order">Ordem de execução.</param>
    /// <param name="algebraicLoop">Se falhar, os componentes do laço algébrico.</param>
    public static bool TryCompute(
        int count,
        IReadOnlyList<(int From, int To)> edges,
        Func<int, bool> hasState,
        out int[] order,
        out int[] algebraicLoop)
    {
        var successors = new List<int>[count];
        var predecessors = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            successors[i] = [];
            predecessors[i] = [];
        }

        // Ligações repetidas entre o mesmo par contam uma vez.
        var active = new HashSet<(int From, int To)>();
        foreach (var edge in edges)
        {
            if (edge.From == edge.To)
            {
                // Saída ligada à própria entrada: só faz sentido com estado, e sempre com um passo de atraso.
                if (!hasState(edge.From))
                {
                    order = [];
                    algebraicLoop = [edge.From];
                    return false;
                }

                continue;
            }

            if (active.Add(edge))
            {
                successors[edge.From].Add(edge.To);
                predecessors[edge.To].Add(edge.From);
            }
        }

        var placed = new bool[count];
        var pending = new int[count];
        for (int i = 0; i < count; i++)
        {
            pending[i] = predecessors[i].Count;
        }

        var result = new List<int>(count);
        while (result.Count < count)
        {
            int next = -1;
            for (int i = 0; i < count; i++)
            {
                if (!placed[i] && pending[i] == 0)
                {
                    next = i;
                    break;
                }
            }

            if (next < 0)
            {
                // Só sobraram laços (e quem depende deles). Abre o primeiro laço "fonte" num componente com estado.
                int[] loop = FindSourceLoop(count, successors, placed, active);
                int breakAt = -1;
                foreach (int candidate in loop)
                {
                    if (hasState(candidate))
                    {
                        breakAt = candidate;
                        break;
                    }
                }

                if (breakAt < 0)
                {
                    order = [];
                    algebraicLoop = loop;
                    return false;
                }

                foreach (int predecessor in predecessors[breakAt])
                {
                    if (!placed[predecessor])
                    {
                        active.Remove((predecessor, breakAt));
                    }
                }

                pending[breakAt] = 0;
                continue;
            }

            placed[next] = true;
            result.Add(next);
            foreach (int successor in successors[next])
            {
                if (active.Contains((next, successor)))
                {
                    pending[successor]--;
                }
            }
        }

        order = [.. result];
        algebraicLoop = [];
        return true;
    }

    /// <summary>
    /// Entre os componentes ainda não colocados, acha um laço (componente fortemente conexo com mais de um nó)
    /// que não recebe ligação de outro laço pendente. Devolve os nós em ordem crescente.
    /// </summary>
    private static int[] FindSourceLoop(int count, List<int>[] successors, bool[] placed, HashSet<(int From, int To)> active)
    {
        var index = new int[count];
        var low = new int[count];
        var onStack = new bool[count];
        var group = new int[count];
        Array.Fill(index, -1);
        Array.Fill(group, -1);
        var stack = new Stack<int>();
        var groups = new List<List<int>>();
        int counter = 0;

        for (int i = 0; i < count; i++)
        {
            if (!placed[i] && index[i] < 0)
            {
                Visit(i);
            }
        }

        var hasIncoming = new bool[groups.Count];
        foreach (var (from, to) in active)
        {
            if (!placed[from] && !placed[to] && group[from] != group[to])
            {
                hasIncoming[group[to]] = true;
            }
        }

        int[]? best = null;
        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].Count > 1 && !hasIncoming[g])
            {
                int[] nodes = [.. groups[g].Order()];
                if (best is null || nodes[0] < best[0])
                {
                    best = nodes;
                }
            }
        }

        return best ?? throw new InvalidOperationException("Ordenação travada sem laço: erro interno do motor de cenários.");

        void Visit(int node)
        {
            index[node] = low[node] = counter++;
            stack.Push(node);
            onStack[node] = true;

            foreach (int successor in successors[node])
            {
                if (placed[successor] || !active.Contains((node, successor)))
                {
                    continue;
                }

                if (index[successor] < 0)
                {
                    Visit(successor);
                    low[node] = Math.Min(low[node], low[successor]);
                }
                else if (onStack[successor])
                {
                    low[node] = Math.Min(low[node], index[successor]);
                }
            }

            if (low[node] == index[node])
            {
                var members = new List<int>();
                int member;
                do
                {
                    member = stack.Pop();
                    onStack[member] = false;
                    group[member] = groups.Count;
                    members.Add(member);
                }
                while (member != node);

                groups.Add(members);
            }
        }
    }
}
