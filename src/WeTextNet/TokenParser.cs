using System.Text;

namespace Dogvane.WeTextNet;

/// <summary>Error thrown when a tagged token stream is malformed.</summary>
public class TokenParseException : Exception
{
    public TokenParseException(string message) : base(message) { }
}

/// <summary>Canonical field orders applied while reordering tagged tokens (from tn/token_parser.py).</summary>
public static class TokenOrders
{
    public static readonly IReadOnlyDictionary<string, string[]> Tn = new Dictionary<string, string[]>
    {
        ["date"] = new[] { "year", "month", "day" },
        ["fraction"] = new[] { "denominator", "numerator" },
        ["measure"] = new[] { "denominator", "numerator", "value" },
        ["money"] = new[] { "value", "currency" },
        ["time"] = new[] { "noon", "hour", "minute", "second" },
    };

    public static readonly IReadOnlyDictionary<string, string[]> EnTn = new Dictionary<string, string[]>
    {
        ["date"] = new[] { "preserve_order", "text", "day", "month", "year" },
        ["money"] = new[] { "integer_part", "fractional_part", "quantity", "currency_maj" },
    };

    public static readonly IReadOnlyDictionary<string, string[]> Itn = new Dictionary<string, string[]>
    {
        ["date"] = new[] { "year", "month", "day", "preserve_order" },
        ["fraction"] = new[] { "sign", "numerator", "denominator" },
        ["measure"] = new[] { "numerator", "denominator", "value", "units" },
        ["money"] = new[] { "currency", "value", "decimal", "quantity" },
        ["time"] = new[] { "hour", "minute", "second", "noon", "zone" },
        ["telephone"] = new[] { "country_code", "number_part" },
        ["electronic"] = new[] { "username", "domain", "protocol" },
    };
}

public sealed class Token
{
    private readonly List<string> _order = new();
    private readonly Dictionary<string, string> _members = new();

    public string Name { get; }
    public IReadOnlyList<string> Order => _order;
    public IReadOnlyDictionary<string, string> Members => _members;

    public Token(string name) => Name = name;

    public void Append(string key, string value)
    {
        if (_members.ContainsKey(key))
            throw new TokenParseException($"duplicate field '{key}' in token '{Name}'");
        _order.Add(key);
        _members[key] = value;
    }

    public string Serialize(IReadOnlyDictionary<string, string[]> orders)
    {
        IEnumerable<string> order = _order;
        if (orders.TryGetValue(Name, out string[]? canonical)
            && (!Members.TryGetValue("preserve_order", out string? preserve) || preserve != "true"))
        {
            order = canonical.Concat(_order.Where(key => !canonical.Contains(key)));
        }

        var builder = new StringBuilder(Name).Append(" {");
        foreach (string key in order)
        {
            if (Members.TryGetValue(key, out string? value))
                builder.Append(' ').Append(key).Append(": \"").Append(EscapeValue(value)).Append('"');
        }
        return builder.Append(" }").ToString();
    }

    internal static string EscapeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// Parses the tagger's wire format (e.g. `time { hour: "两点" }`) and reorders each
/// token's fields into the reading order the verbalizer expects.
/// </summary>
public sealed class TokenParser
{
    private const int Eos = -1;

    private readonly IReadOnlyDictionary<string, string[]> _orders;

    // The Python reference indexes by Unicode code point; astral characters occupy
    // two UTF-16 units in a C# string, so parsing runs over a code-point array.
    private int[] _codePoints = Array.Empty<int>();
    private int _index;
    private int _char = Eos;
    private readonly List<Token> _tokens = new();

    public TokenParser(string lang, string operation = "tn")
    {
        bool en = lang == "en";
        _orders = (en, operation) switch
        {
            (true, "tn") => TokenOrders.EnTn,
            (true, "itn") => TokenOrders.Itn,
            (false, "tn") => TokenOrders.Tn,
            (false, "itn") => TokenOrders.Itn,
            _ => throw new NotImplementedException($"unsupported parser mode: {lang}/{operation}"),
        };
    }

    public IReadOnlyList<Token> Tokens => _tokens;

    public string Reorder(string input)
    {
        Parse(input);
        return string.Join(" ", _tokens.Select(token => token.Serialize(_orders)));
    }

    public void Parse(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new TokenParseException("token stream must not be empty");

        _codePoints = input.EnumerateRunes().Select(rune => rune.Value).ToArray();
        _index = 0;
        _char = CodePointAt(0);
        _tokens.Clear();

        while (ParseWs())
        {
            string name = ParseKey();
            ExpectChars(" { ");

            var token = new Token(name);
            bool closed = false;
            while (ParseWs())
            {
                if (_char == '}')
                {
                    ParseChar('}');
                    closed = true;
                    break;
                }
                string key = ParseKey();
                ExpectChars(": \"");
                string value = ParseValue();
                ExpectChars("\"");
                token.Append(key, value);
            }
            if (!closed)
                throw new TokenParseException($"unterminated token '{name}'");
            _tokens.Add(token);
        }
    }

    private int CodePointAt(int index) => index < _codePoints.Length ? _codePoints[index] : Eos;

    private bool Read()
    {
        if (_index < _codePoints.Length - 1)
        {
            _index += 1;
            _char = CodePointAt(_index);
            return true;
        }
        _char = Eos;
        return false;
    }

    private bool ParseWs()
    {
        bool notEos = _char != Eos;
        while (notEos && _char == ' ')
            notEos = Read();
        return notEos;
    }

    private bool ParseChar(int expected)
    {
        if (_char == expected)
        {
            Read();
            return true;
        }
        return false;
    }

    private bool ParseChars(string expected)
    {
        int start = _index;
        foreach (Rune ch in expected.EnumerateRunes())
        {
            if (!ParseChar(ch.Value))
            {
                _index = start;
                _char = CodePointAt(start);
                return false;
            }
        }
        return true;
    }

    private void ExpectChars(string expected)
    {
        if (!ParseChars(expected))
            throw new TokenParseException($"expected '{expected}' at position {_index}, got '{Describe(_char)}'");
    }

    private string ParseKey()
    {
        if (_char == Eos)
            throw new TokenParseException("expected key at end of token stream");
        // Python checks membership in string.whitespace (ASCII whitespace only).
        if (IsAsciiWhitespace(_char))
            throw new TokenParseException($"expected key at position {_index}");

        var key = new StringBuilder();
        while ((IsAsciiLetter(_char) || _char == '_') && _char != Eos)
        {
            key.Append(ConvertFromUtf32(_char));
            Read();
        }
        if (key.Length == 0)
            throw new TokenParseException($"invalid key at position {_index}");
        return key.ToString();
    }

    private string ParseValue()
    {
        if (_char == Eos)
            throw new TokenParseException("expected value at end of token stream");

        var value = new StringBuilder();
        while (_char != '"')
        {
            if (_char == Eos)
                throw new TokenParseException($"unterminated value at position {_index}");
            bool escaped = _char == '\\';
            if (!escaped)
                value.Append(ConvertFromUtf32(_char));
            Read();
            if (escaped)
            {
                if (_char == Eos)
                    throw new TokenParseException($"unterminated escape at position {_index}");
                if (_char is '"' or '\\')
                    value.Append(ConvertFromUtf32(_char));
                else
                    value.Append('\\').Append(ConvertFromUtf32(_char));
                Read();
            }
        }
        return value.ToString();
    }

    private static bool IsAsciiLetter(int cp) => cp is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsAsciiWhitespace(int cp) =>
        cp is ' ' or '\t' or '\n' or '\r' or '\v' or '\f';

    private static string ConvertFromUtf32(int cp) => char.ConvertFromUtf32(cp);

    private static string Describe(int cp) => cp == Eos ? "<EOS>" : char.ConvertFromUtf32(cp);
}
