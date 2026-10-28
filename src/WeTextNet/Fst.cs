// Port of the OpenFst vector FST binary format (kFstMagicNumber / VectorFst file
// version 2, standard arcs, tropical semiring) as written by pynini and read by
// kaldifst. Pure managed parsing, no native interop.

using System.Buffers.Binary;
using System.Text;

namespace Dogvane.WeTextNet;

/// <summary>An OpenFst vector FST with standard (tropical) arcs, stored in a flat, read-only layout.</summary>
public sealed class Fst
{
    private const int MagicNumber = 2125659606;

    // Structure-of-arrays graph: arcs of state s are [ArcOffset[s], ArcOffset[s + 1]).
    private readonly float[] _finalWeights;
    private readonly int[] _arcOffsets;
    private readonly int[] _arcIlabels;
    private readonly int[] _arcOlabels;
    private readonly float[] _arcWeights;
    private readonly int[] _arcNextStates;

    public int Start { get; }
    public int StateCount { get; }
    public int ArcCount => _arcIlabels.Length;

    private Fst(float[] finalWeights, int[] arcOffsets, int[] arcIlabels, int[] arcOlabels,
        float[] arcWeights, int[] arcNextStates, int start)
    {
        _finalWeights = finalWeights;
        _arcOffsets = arcOffsets;
        _arcIlabels = arcIlabels;
        _arcOlabels = arcOlabels;
        _arcWeights = arcWeights;
        _arcNextStates = arcNextStates;
        Start = start;
        StateCount = finalWeights.Length;
    }

    public int ArcOffset(int state) => _arcOffsets[state];
    public int ArcEnd(int state) => _arcOffsets[state + 1];
    public int ArcIlabel(int arc) => _arcIlabels[arc];
    public int ArcOlabel(int arc) => _arcOlabels[arc];
    public float ArcWeight(int arc) => _arcWeights[arc];
    public int ArcNextState(int arc) => _arcNextStates[arc];

    /// <summary>Tropical final weight; positive infinity means the state is not final.</summary>
    public float FinalWeight(int state) => _finalWeights[state];

    public static Fst Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    public static Fst Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray(), source: "stream");
    }

    private static Fst Parse(byte[] data, string source)
    {
        var reader = new Reader(data);

        if (reader.ReadInt32() != MagicNumber)
            throw new InvalidDataException($"{source}: bad FST magic number");
        string fstType = reader.ReadString();
        string arcType = reader.ReadString();
        if (fstType != "vector" || arcType != "standard")
            throw new InvalidDataException($"{source}: unsupported FST type {fstType}/{arcType}");

        int version = reader.ReadInt32();
        if (version < 2)
            throw new InvalidDataException($"{source}: unsupported FST file version {version}");
        _ = reader.ReadInt32();                 // flags
        _ = reader.ReadUInt64();                // properties
        int start = checked((int)reader.ReadInt64());
        long stateCount = reader.ReadInt64();
        _ = reader.ReadInt64();                 // numarcs (informational)

        if (start < -1 || stateCount < 0 || start >= stateCount)
            throw new InvalidDataException($"{source}: invalid start state or state count");

        var finalWeights = new float[stateCount];
        var arcOffsets = new int[checked((int)stateCount) + 1];
        // First pass: read per-state headers to size the arc arrays.
        var narcsPerState = new long[stateCount];
        long totalArcs = 0;
        for (long s = 0; s < stateCount; s++)
        {
            finalWeights[s] = reader.ReadSingle();
            long narcs = reader.ReadInt64();
            if (narcs < 0)
                throw new InvalidDataException($"{source}: negative arc count for state {s}");
            narcsPerState[s] = narcs;
            totalArcs += narcs;
            for (long a = 0; a < narcs; a++)
            {
                reader.Skip(4 + 4 + 4 + 4);     // ilabel, olabel, weight, nextstate
            }
        }
        if (reader.Offset != data.Length)
            throw new InvalidDataException($"{source}: {data.Length - reader.Offset} trailing bytes (symbol tables are not supported)");

        // Second pass: fill arc arrays.
        var reader2 = new Reader(data);
        _ = reader2.ReadInt32();
        _ = reader2.ReadString();
        _ = reader2.ReadString();
        _ = reader2.ReadInt32();
        _ = reader2.ReadInt32();
        _ = reader2.ReadUInt64();
        _ = reader2.ReadInt64();
        _ = reader2.ReadInt64();
        _ = reader2.ReadInt64();

        var ilabels = new int[totalArcs];
        var olabels = new int[totalArcs];
        var weights = new float[totalArcs];
        var nexts = new int[totalArcs];
        int arcIndex = 0;
        for (int s = 0; s < stateCount; s++)
        {
            arcOffsets[s] = arcIndex;
            _ = reader2.ReadSingle();
            long narcs = reader2.ReadInt64();
            for (long a = 0; a < narcs; a++)
            {
                int ilabel = reader2.ReadInt32();
                int olabel = reader2.ReadInt32();
                float weight = reader2.ReadSingle();
                int next = reader2.ReadInt32();
                if (next < 0 || next >= stateCount)
                    throw new InvalidDataException($"{source}: arc to invalid state {next}");
                ilabels[arcIndex] = ilabel;
                olabels[arcIndex] = olabel;
                weights[arcIndex] = weight;
                nexts[arcIndex] = next;
                arcIndex++;
            }
        }
        arcOffsets[stateCount] = arcIndex;

        return new Fst(finalWeights, arcOffsets, ilabels, olabels, weights, nexts, start);
    }

    private struct Reader
    {
        private readonly byte[] _data;
        public int Offset { get; private set; }

        public Reader(byte[] data)
        {
            _data = data;
            Offset = 0;
        }

        public int ReadInt32()
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(Offset));
            Offset += 4;
            return v;
        }

        public long ReadInt64()
        {
            long v = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(Offset));
            Offset += 8;
            return v;
        }

        public ulong ReadUInt64()
        {
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(Offset));
            Offset += 8;
            return v;
        }

        public float ReadSingle()
        {
            float v = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(Offset)));
            Offset += 4;
            return v;
        }

        public string ReadString()
        {
            int len = ReadInt32();
            string s = Encoding.UTF8.GetString(_data.AsSpan(Offset, len));
            Offset += len;
            return s;
        }

        public void Skip(int bytes) => Offset += bytes;
    }
}
