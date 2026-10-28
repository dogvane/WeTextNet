using System.Text;

namespace Dogvane.WeTextNet;

/// <summary>
/// Managed port of kaldifst's TextNormalizer.Normalize: it builds the byte-level
/// acceptor for the input, eagerly composes it with the rule FST (states are
/// numbered in breadth-first creation order), then runs OpenFst's
/// SingleShortestPath and reads the output labels off the winning linear path.
/// State numbering, arc order and queue discipline replicate the C++ runtime so
/// that equal-cost paths resolve identically.
/// </summary>
public static class Transducer
{
    public static string? Transduce(string input, Fst fst)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        if (bytes.Length == 0)
            return string.Empty;

        Composed composed = Compose(bytes, fst);
        return SingleShortestPath(composed, fst, bytes.Length);
    }

    /// <summary>
    /// The composition of the linear input acceptor with the rule FST. A
    /// composed state is a (input position, rule state) pair. For each state the
    /// byte-matching arcs come first and input-epsilon arcs second, following
    /// ComposeFst's expansion order; expansion itself walks states in id order.
    /// </summary>
    private sealed class Composed : IGraph
    {
        public readonly List<int> Positions = new();
        public readonly List<int> RuleStates = new();
        public readonly Dictionary<long, int> Ids = new();

        public readonly List<int> Targets = new();
        public readonly List<float> Weights = new();
        public readonly List<int> Olabels = new();
        public readonly List<int> Offsets = new();

        public int StateCount => Positions.Count;

        public int NewState(int position, int ruleState)
        {
            long key = ((long)position << 32) | (uint)ruleState;
            if (Ids.TryGetValue(key, out int id))
                return id;
            id = Positions.Count;
            Positions.Add(position);
            RuleStates.Add(ruleState);
            Offsets.Add(0);
            Ids[key] = id;
            return id;
        }

        public int ArcBegin(int state) => Offsets[state];
        public int ArcEnd(int state) => Offsets[state + 1];
        public int Target(int arc) => Targets[arc];
        public float Weight(int arc) => Weights[arc];
    }

    private static Composed Compose(byte[] bytes, Fst fst)
    {
        var composed = new Composed();
        _ = composed.NewState(0, fst.Start);
        int cursor = 0;
        while (cursor < composed.Positions.Count)
        {
            int s = cursor++;
            int position = composed.Positions[s];
            int ruleState = composed.RuleStates[s];
            // A state's arc run starts when the state is expanded, not when it is
            // created: states created by earlier expansions append their arcs first.
            composed.Offsets[s] = composed.Targets.Count;

            if (position < bytes.Length)
            {
                int label = bytes[position];
                for (int arc = fst.ArcOffset(ruleState); arc < fst.ArcEnd(ruleState); arc++)
                {
                    if (fst.ArcIlabel(arc) != label)
                        continue;
                    int target = composed.NewState(position + 1, fst.ArcNextState(arc));
                    composed.Targets.Add(target);
                    composed.Weights.Add(fst.ArcWeight(arc));
                    composed.Olabels.Add(fst.ArcOlabel(arc));
                }
            }
            for (int arc = fst.ArcOffset(ruleState); arc < fst.ArcEnd(ruleState); arc++)
            {
                if (fst.ArcIlabel(arc) != 0)
                    continue;
                int target = composed.NewState(position, fst.ArcNextState(arc));
                composed.Targets.Add(target);
                composed.Weights.Add(fst.ArcWeight(arc));
                composed.Olabels.Add(fst.ArcOlabel(arc));
            }
        }
        composed.Offsets.Add(composed.Targets.Count);
        return composed;
    }

    /// <summary>OpenFst internal::SingleShortestPath with an AutoQueue.</summary>
    private static string? SingleShortestPath(Composed composed, Fst fst, int inputLength)
    {
        int count = composed.Positions.Count;
        var distance = new float[count];
        Array.Fill(distance, float.PositiveInfinity);
        var parentState = new int[count];
        Array.Fill(parentState, -1);
        var parentArc = new int[count];
        var enqueued = new bool[count];
        IStateQueue queue = OpenFstQueues.AutoQueue(composed, 0, distance);

        distance[0] = 0f;
        enqueued[0] = true;
        queue.Enqueue(0);

        float fDistance = float.PositiveInfinity;
        int fParent = -1;

        while (!queue.Empty())
        {
            int s = queue.Head();
            queue.Dequeue();
            enqueued[s] = false;
            float sd = distance[s];

            // A composed state is final when the acceptor is exhausted (the input
            // position reached the end) and the rule state is final too.
            if (composed.Positions[s] == inputLength)
            {
                float final = fst.FinalWeight(composed.RuleStates[s]);
                if (!float.IsInfinity(final))
                {
                    // Plus(fDistance, Times(sd, final)): only a strict improvement
                    // replaces the best final state; a tie keeps the earlier one.
                    float plus = Math.Min(fDistance, sd + final);
                    if (fDistance != plus)
                    {
                        fDistance = plus;
                        fParent = s;
                    }
                }
            }

            for (int arc = composed.ArcBegin(s); arc < composed.ArcEnd(s); arc++)
            {
                int target = composed.Targets[arc];
                float weight = sd + composed.Weights[arc];
                if (weight < distance[target])
                {
                    distance[target] = weight;
                    parentState[target] = s;
                    parentArc[target] = arc;
                    if (!enqueued[target])
                    {
                        enqueued[target] = true;
                        queue.Enqueue(target);
                    }
                }
            }
        }

        if (fParent < 0)
            return null;

        // SingleShortestPathBacktrace + FstToString(remove_output_zero=true).
        var output = new List<byte>();
        int walk = fParent;
        while (parentState[walk] >= 0)
        {
            int olabel = composed.Olabels[parentArc[walk]];
            if (olabel != 0)
                output.Add((byte)olabel);
            walk = parentState[walk];
        }
        output.Reverse();
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
