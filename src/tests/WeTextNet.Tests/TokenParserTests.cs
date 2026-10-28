namespace Dogvane.WeTextNet.Tests;

/// <summary>Ported from PythonWeTextProcessing tn/chinese/test/token_parser_test.py.</summary>
public class TokenParserTests
{
    private readonly TokenParser _parser = new("zh", "tn");

    [Fact]
    public void WhitespaceOnlyStreamProducesNoTokens()
    {
        _parser.Parse(" ");
        Assert.Empty(_parser.Tokens);
    }

    [Fact]
    public void ParseExtractsTokensWithFields()
    {
        const string input = "time { minute: \"零二分\" hour: \"两点\" } char { value: \"走\" }";
        _parser.Parse(input);
        Assert.Equal(2, _parser.Tokens.Count);
        Assert.Equal("time", _parser.Tokens[0].Name);
        Assert.Equal("char", _parser.Tokens[1].Name);
        Assert.Equal(new[] { "minute", "hour" }, _parser.Tokens[0].Order);
        Assert.Equal(new Dictionary<string, string> { ["minute"] = "零二分", ["hour"] = "两点" }, _parser.Tokens[0].Members);
        Assert.Equal(new Dictionary<string, string> { ["value"] = "走" }, _parser.Tokens[1].Members);
    }

    [Fact]
    public void ReorderAppliesCanonicalOrder()
    {
        const string input = "time { minute: \"零二分\" hour: \"两点\" } char { value: \"走\" }";
        Assert.Equal("time { hour: \"两点\" minute: \"零二分\" } char { value: \"走\" }",
            _parser.Reorder(input));
    }

    [Fact]
    public void ReorderPreservesUnknownFields()
    {
        const string input = "money { unexpected: \"keep\" currency: \"元\" value: \"十\" }";
        Assert.Equal("money { value: \"十\" currency: \"元\" unexpected: \"keep\" }", _parser.Reorder(input));
    }

    [Fact]
    public void ReorderRoundTripsEscapedValues()
    {
        const string tagged = "char { value: \"中\\\"文\\\\尾\" }";
        _parser.Parse(tagged);
        Assert.Equal("中\"文\\尾", _parser.Tokens[0].Members["value"]);
        Assert.Equal(tagged, _parser.Reorder(tagged));
    }

    [Fact]
    public void DuplicateFieldIsRejected()
    {
        Assert.Throws<TokenParseException>(
            () => _parser.Reorder("char { value: \"first\" value: \"second\" }"));
    }

    [Theory]
    [InlineData("time { hour: \"12 }")]
    [InlineData("time { hour \"12\" }")]
    [InlineData("time { hour: \"12\" ")]
    public void MalformedInputIsRejected(string tagged)
    {
        Assert.Throws<TokenParseException>(() => _parser.Reorder(tagged));
    }

    [Fact]
    public void PreserveOrderDisablesReordering()
    {
        const string input = "date { day: \"2\" year: \"2024\" preserve_order: \"true\" month: \"1\" }";
        Assert.Equal(input, _parser.Reorder(input));
    }
}
