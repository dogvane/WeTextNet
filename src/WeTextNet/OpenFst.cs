// Managed ports of the OpenFst pieces that decide equal-cost path selection in
// ShortestPath: the heap (heap.h), the queue disciplines (queue.h), Tarjan's
// SCC visitor (cc-visitors.h), the DFS driver (dfs-visit.h) and AutoQueue's
// selection logic. The rule FSTs shipped with wetext store no graph properties,
// so a composed FST never claims kTopSorted/kAcyclic/kUnweighted and AutoQueue
// always takes the strongly-connected-component branch below.

namespace Dogvane.WeTextNet;

/// <summary>Read-only graph view shared by the queue machinery.</summary>
internal interface IGraph
{
    int StateCount { get; }
    int ArcBegin(int state);
    int ArcEnd(int state);
    int Target(int arc);
    float Weight(int arc);
}

internal interface IStateQueue
{
    int Head();
    void Enqueue(int state);
    void Dequeue();
    void Update(int state);
    bool Empty();
}

/// <summary>OpenFst Heap over int state ids with a "better comes first" comparator.</summary>
internal sealed class Heap
{
    private readonly Func<int, int, bool> _comp;
    private readonly List<int> _pos = new();
    private readonly List<int> _key = new();
    private readonly List<int> _values = new();
    private int _size;

    public Heap(Func<int, int, bool> comp) => _comp = comp;

    public int Insert(int value)
    {
        if (_size < _values.Count)
        {
            _values[_size] = value;
            _pos[_key[_size]] = _size;
        }
        else
        {
            _values.Add(value);
            _pos.Add(_size);
            _key.Add(_size);
        }
        ++_size;
        return InsertUp(value, _size - 1);
    }

    public int Pop()
    {
        int top = _values[0];
        Swap(0, _size - 1);
        --_size;
        Heapify(0);
        return top;
    }

    public int Top() => _values[0];
    public bool Empty() => _size == 0;

    private static int Left(int i) => 2 * (i + 1) - 1;
    private static int Right(int i) => 2 * (i + 1);
    private static int Parent(int i) => (i - 1) / 2;

    private void Swap(int j, int k)
    {
        int tkey = _key[j];
        _pos[_key[j] = _key[k]] = j;
        _pos[_key[k] = tkey] = k;
        (_values[j], _values[k]) = (_values[k], _values[j]);
    }

    private void Heapify(int i)
    {
        int largest = i;
        int l = Left(i);
        if (l < _size && _comp(_values[l], _values[i]))
            largest = l;
        int r = Right(i);
        if (r < _size && _comp(_values[r], _values[largest]))
            largest = r;
        if (largest != i)
        {
            Swap(i, largest);
            Heapify(largest);
        }
    }

    private int InsertUp(int value, int i)
    {
        while (i > 0)
        {
            int p = Parent(i);
            if (_comp(_values[p], value))
                break;
            Swap(i, p);
            i = p;
        }
        return _key[i];
    }
}

internal sealed class FifoQueue : IStateQueue
{
    private readonly Queue<int> _queue = new();

    public int Head() => _queue.Peek();
    public void Enqueue(int state) => _queue.Enqueue(state);
    public void Dequeue() => _queue.Dequeue();
    public void Update(int state) { }
    public bool Empty() => _queue.Count == 0;
}

internal sealed class LifoQueue : IStateQueue
{
    private readonly List<int> _stack = new();

    public int Head() => _stack[^1];
    public void Enqueue(int state) => _stack.Add(state);
    public void Dequeue() => _stack.RemoveAt(_stack.Count - 1);
    public void Update(int state) { }
    public bool Empty() => _stack.Count == 0;
}

/// <summary>ShortestFirstQueue with update=false, ordered by live distances.</summary>
internal sealed class ShortestFirstQueue : IStateQueue
{
    private readonly Heap _heap;

    public ShortestFirstQueue(float[] distance) =>
        _heap = new Heap((a, b) => distance[a] < distance[b]);

    public int Head() => _heap.Top();
    public void Enqueue(int state) => _heap.Insert(state);
    public void Dequeue() => _heap.Pop();
    public void Update(int state) { }
    public bool Empty() => _heap.Empty();
}

/// <summary>TopOrderQueue over a precomputed order (SCC numbers or DFS finish order).</summary>
internal sealed class TopOrderQueue : IStateQueue
{
    private readonly int[] _order;
    private readonly int[] _state;
    private int _front;
    private int _back = -1;

    public TopOrderQueue(int[] order)
    {
        _order = order;
        _state = new int[order.Length];
        Array.Fill(_state, -1);
    }

    public int Head() => _state[_front];

    public void Enqueue(int state)
    {
        if (_front > _back)
        {
            _front = _order[state];
            _back = _order[state];
        }
        else if (_order[state] > _back)
        {
            _back = _order[state];
        }
        else if (_order[state] < _front)
        {
            _front = _order[state];
        }
        _state[_order[state]] = state;
    }

    public void Dequeue()
    {
        _state[_front] = -1;
        while (_front <= _back && _state[_front] == -1)
            ++_front;
    }

    public void Update(int state) { }

    public bool Empty() => _front > _back;
}

/// <summary>SccQueue: per-SCC queues scanned in SCC order, trivial SCCs inline.</summary>
internal sealed class SccQueue : IStateQueue
{
    private readonly int[] _scc;
    private readonly IStateQueue?[] _queues;
    private readonly List<int> _trivial = new();
    private int _front;
    private int _back = -1;

    public SccQueue(int[] scc, IStateQueue?[] queues)
    {
        _scc = scc;
        _queues = queues;
    }

    public int Head()
    {
        while (_front <= _back)
        {
            IStateQueue? queue = _queues[_front];
            if (queue != null)
            {
                if (!queue.Empty())
                    return queue.Head();
            }
            else if (_front < _trivial.Count && _trivial[_front] != -1)
            {
                return _trivial[_front];
            }
            ++_front;
        }
        throw new InvalidOperationException("SccQueue head on empty queue");
    }

    public void Enqueue(int state)
    {
        int scc = _scc[state];
        if (_front > _back)
        {
            _front = scc;
            _back = scc;
        }
        else if (scc > _back)
        {
            _back = scc;
        }
        else if (scc < _front)
        {
            _front = scc;
        }

        if (_queues[scc] != null)
        {
            _queues[scc]!.Enqueue(state);
        }
        else
        {
            while (_trivial.Count <= scc)
                _trivial.Add(-1);
            _trivial[scc] = state;
        }
    }

    public void Dequeue()
    {
        if (_queues[_front] != null)
            _queues[_front]!.Dequeue();
        else if (_front < _trivial.Count)
            _trivial[_front] = -1;
    }

    public void Update(int state) => _queues[_scc[state]]?.Update(state);

    public bool Empty()
    {
        if (_front < _back)
            return false;
        if (_front > _back)
            return true;
        if (_queues[_front] != null)
            return _queues[_front]!.Empty();
        return _front >= _trivial.Count || _trivial[_front] == -1;
    }
}

internal static class OpenFstQueues
{
    private enum QueueType
    {
        Trivial,
        Fifo,
        Lifo,
        ShortestFirst,
    }

    /// <summary>
    /// AutoQueue for a composed (cyclic, weighted, not top-sorted) graph: Tarjan
    /// SCC decomposition, per-SCC queue types from the intra-SCC arc weights, and
    /// the SccQueue meta-discipline.
    /// </summary>
    public static IStateQueue AutoQueue(IGraph graph, int start, float[] distance)
    {
        int[] scc = SccVisitor.ComputeScc(graph, start);
        int nscc = scc.Max() + 1;

        var types = new QueueType[nscc];
        Array.Fill(types, QueueType.Trivial);
        bool allTrivial = true;
        bool unweighted = true;
        for (int s = 0; s < graph.StateCount; s++)
        {
            for (int arc = graph.ArcBegin(s); arc < graph.ArcEnd(s); arc++)
            {
                float weight = graph.Weight(arc);
                int target = graph.Target(arc);
                if (scc[s] == scc[target])
                {
                    QueueType type = types[scc[s]];
                    if (weight < 0f)
                    {
                        // NaturalLess(weight, One): a negative intra-SCC arc forces FIFO.
                        type = QueueType.Fifo;
                    }
                    else if (type is QueueType.Trivial or QueueType.Lifo)
                    {
                        // Idempotent semiring: any weight other than Zero (+inf) or
                        // One (0) upgrades the SCC to shortest-first.
                        if (!float.IsInfinity(weight) && weight != 0f)
                            type = QueueType.ShortestFirst;
                        else
                            type = QueueType.Lifo;
                    }
                    types[scc[s]] = type;
                    if (type != QueueType.Trivial)
                        allTrivial = false;
                }
                if (!float.IsInfinity(weight) && weight != 0f)
                    unweighted = false;
            }
        }

        if (unweighted)
            return new LifoQueue();
        if (allTrivial)
            return new TopOrderQueue(scc);

        var queues = new IStateQueue?[nscc];
        for (int i = 0; i < nscc; i++)
        {
            queues[i] = types[i] switch
            {
                QueueType.Fifo => new FifoQueue(),
                QueueType.Lifo => new LifoQueue(),
                QueueType.ShortestFirst => new ShortestFirstQueue(distance),
                _ => null,
            };
        }
        return new SccQueue(scc, queues);
    }

    /// <summary>Tarjan SCC via DfsVisit, with the final topological renumbering.</summary>
    private static class SccVisitor
    {
        public static int[] ComputeScc(IGraph graph, int start)
        {
            int n = graph.StateCount;
            var color = new byte[n]; // 0 white, 1 grey, 2 black
            var dfnumber = new int[n];
            var lowlink = new int[n];
            var onstack = new bool[n];
            var scc = new int[n];
            var stack = new List<int>();
            int counter = 0;
            int nscc = 0;

            // Iterative DFS over every tree, roots in state id order.
            var frames = new Stack<(int State, int Cursor)>();
            for (int root = start; root >= 0 && root < n; )
            {
                color[root] = 1;
                frames.Push((root, graph.ArcBegin(root)));
                InitState(root);
                Dfs(root);

                for (root = root == start ? 0 : root + 1; root < n && color[root] != 0; ++root)
                {
                }
            }
            int[] result = scc;
            for (int s = 0; s < n; s++)
                result[s] = nscc - 1 - result[s];
            return result;

            void InitState(int s)
            {
                stack.Add(s);
                dfnumber[s] = counter;
                lowlink[s] = counter;
                onstack[s] = true;
                ++counter;
            }

            void Dfs(int treeRoot)
            {
                while (frames.Count > 0)
                {
                    int s = frames.Peek().State;
                    int cursor = frames.Peek().Cursor;
                    if (cursor >= graph.ArcEnd(s))
                    {
                        // Aiter done: blacken, finish, advance the parent's cursor.
                        color[s] = 2;
                        frames.Pop();
                        if (frames.Count > 0)
                        {
                            var parent = frames.Pop();
                            FinishState(s, parent.State);
                            parent.Cursor++;
                            frames.Push(parent);
                        }
                        else
                        {
                            FinishState(s, -1);
                        }
                        continue;
                    }

                    int t = graph.Target(cursor);
                    frames.Pop();
                    frames.Push((s, cursor + (color[t] == 0 ? 0 : 1)));
                    switch (color[t])
                    {
                        case 0: // white -> tree arc, descend
                            color[t] = 1;
                            frames.Push((t, graph.ArcBegin(t)));
                            InitState(t);
                            break;
                        case 1: // grey -> back arc
                            if (dfnumber[t] < lowlink[s])
                                lowlink[s] = dfnumber[t];
                            break;
                        default: // black -> forward or cross arc
                            if (dfnumber[t] < dfnumber[s] && onstack[t] && dfnumber[t] < lowlink[s])
                                lowlink[s] = dfnumber[t];
                            break;
                    }
                }
            }

            void FinishState(int s, int parent)
            {
                if (dfnumber[s] == lowlink[s])
                {
                    int i = stack.Count;
                    int t;
                    do
                    {
                        t = stack[--i];
                    } while (s != t);
                    do
                    {
                        t = stack[^1];
                        scc[t] = nscc;
                        onstack[t] = false;
                        stack.RemoveAt(stack.Count - 1);
                    } while (s != t);
                    ++nscc;
                }
                if (parent >= 0 && lowlink[s] < lowlink[parent])
                    lowlink[parent] = lowlink[s];
            }
        }
    }
}
