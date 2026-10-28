namespace Dogvane.WeTextNet.Tests;

/// <summary>
/// Behavior cases ported from the wetext README and PythonWeTextProcessing's
/// normalizer/tagger tests that apply to a pure inference runtime.
/// </summary>
public class PipelineTests
{
    private static Normalizer New(Language lang, Operator op, bool removeErhua = false, bool enable0To9 = false,
        bool traditionalToSimple = false, bool fullToHalf = false, bool removeInterjections = false,
        bool removePuncts = false, bool tagOov = false) =>
        TestHost.GetNormalizer(new NormalizerOptions
        {
            Lang = lang,
            Operator = op,
            RemoveErhua = removeErhua,
            Enable0To9 = enable0To9,
            TraditionalToSimple = traditionalToSimple,
            FullToHalf = fullToHalf,
            RemoveInterjections = removeInterjections,
            RemovePuncts = removePuncts,
            TagOov = tagOov,
        });

    // ---- wetext README examples (expectations verified against wetext 0.1.8) ----

    [Fact]
    public void ChineseTnRemovesErhua()
    {
        var normalizer = New(Language.Zh, Operator.Tn, removeErhua: true);
        Assert.Equal("你好 WeTextProcessing 一点零，全新版本，简直六百六十六",
            normalizer.Normalize("你好 WeTextProcessing 1.0，全新版本儿，简直666"));
    }

    [Fact]
    public void ChineseTnKeepsErhuaByDefault()
    {
        var normalizer = New(Language.Zh, Operator.Tn);
        Assert.Equal("你好 WeTextProcessing 一点零，全新版本儿，简直六百六十六",
            normalizer.Normalize("你好 WeTextProcessing 1.0，全新版本儿，简直666"));
    }

    [Fact]
    public void ChineseItnSkipsSmallNumbersByDefault()
    {
        // The tagger resolves "直六六六" as a Z-train number, so the README's
        // "简直666" expectation is stale; wetext 0.1.8 (and WeTextNet) say 简Z666.
        var normalizer = New(Language.Zh, Operator.Itn);
        Assert.Equal("你好 WeTextProcessing 1.0，全新版本儿，简Z666，九和六",
            normalizer.Normalize("你好 WeTextProcessing 一点零，全新版本儿，简直六六六，九和六"));
    }

    [Fact]
    public void ChineseItnWithEnable0To9()
    {
        var normalizer = New(Language.Zh, Operator.Itn, enable0To9: true);
        Assert.Equal("123", normalizer.Normalize("一二三"));
    }

    [Fact]
    public void ChineseItnVerbalizerDeadEndYieldsEmptyOutput()
    {
        // With enable_0_to_9 the tagger labels "直六六六" as a train number the
        // verbalizer cannot pronounce; wetext (kaldifst) returns "" for the whole
        // input, and so does WeTextNet.
        var normalizer = New(Language.Zh, Operator.Itn, enable0To9: true);
        Assert.Equal(string.Empty, normalizer.Normalize("你好 WeTextProcessing 一点零，全新版本儿，简直六六六，九和六"));
        Assert.Equal(string.Empty, normalizer.Normalize("九和六"));
    }

    [Fact]
    public void EnglishTn()
    {
        var normalizer = New(Language.En, Operator.Tn);
        Assert.Equal("The price is twelve point five dollars, please pay now.",
            normalizer.Normalize("The price is $12.50, please pay now."));
    }

    [Fact]
    public void EnglishItn()
    {
        var normalizer = New(Language.En, Operator.Itn);
        Assert.Equal("$23.50", normalizer.Normalize("twenty three dollars and fifty cents"));
    }

    // ---- PythonWeTextProcessing tn/chinese/test/normalizer_test.py ----

    [Theory]
    [InlineData("今天中午12点", "math { value: \"12\" }")]
    [InlineData("2024/01/02", "date { year: \"2024\" month: \"01\" day: \"02\" }")]
    [InlineData("3/4", "fraction { numerator: \"3\" denominator: \"4\" }")]
    [InlineData("￥12", "money { currency: \"￥\" value: \"12\" }")]
    [InlineData("10km/h", "measure { numerator: \"10km\" denominator: \"h\" }")]
    [InlineData("蘋果", "char { value: \"蘋\" } char { value: \"果\" }")]
    public void TaggerPreservesWrittenFields(string written, string tagged)
    {
        var normalizer = New(Language.Zh, Operator.Tn,
            traditionalToSimple: true, removeErhua: true, fullToHalf: true, removeInterjections: true);
        Assert.Contains(tagged, normalizer.Tag(written, Language.Zh));
    }

    [Fact]
    public void RangeIsTaggedBeforeVerbalization()
    {
        var normalizer = New(Language.Zh, Operator.Tn,
            traditionalToSimple: true, removeErhua: true, fullToHalf: true, removeInterjections: true);
        string tagged = normalizer.Tag("10:30-11:20", Language.Zh);
        Assert.Contains("range { value: \"-\" }", tagged);
    }

    [Fact]
    public void TraditionalToSimpleHappensAfterTagging()
    {
        var normalizer = New(Language.Zh, Operator.Tn,
            traditionalToSimple: true, removeErhua: true, fullToHalf: true, removeInterjections: true);
        Assert.Equal("苹果", normalizer.Normalize("蘋果"));
    }

    // ---- edge behavior of the wetext pipeline ----

    [Fact]
    public void EmptyInputNormalizesToEmpty()
    {
        Assert.Equal(string.Empty, New(Language.Zh, Operator.Tn).Normalize(string.Empty));
        Assert.Equal(string.Empty, New(Language.Zh, Operator.Itn).Normalize(string.Empty));
        Assert.Equal(string.Empty, New(Language.En, Operator.Tn).Normalize(string.Empty));
    }

    [Fact]
    public void ChineseTnWithoutDigitsShortCircuits()
    {
        // should_normalize() is false for digit-free Chinese TN input: the text
        // passes through postprocessing untouched (wetext parity).
        Assert.Equal("你好世界", New(Language.Zh, Operator.Tn).Normalize("你好世界"));
        // ...but erhua removal still triggers normalization when asked for.
        Assert.Equal("哪", New(Language.Zh, Operator.Tn, removeErhua: true).Normalize("哪儿"));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.Equal("十二", New(Language.Zh, Operator.Tn).Normalize("  12  "));
    }

    [Fact]
    public void AutoLanguageDetectionPrefersChinese()
    {
        var auto = TestHost.GetNormalizer(new NormalizerOptions());
        Assert.Equal("价格是十二点五元", auto.Normalize("价格是12.5元"));
        Assert.Equal("The price is twelve point five dollars, please pay now.",
            auto.Normalize("The price is $12.50, please pay now."));
        // Pure digits are detected as Chinese, matching wetext's get_lang().
        Assert.Equal("两千零二", auto.Normalize("2002"));
    }

    [Fact]
    public void JapaneseTnAndItn()
    {
        Assert.Equal("これはテストです", New(Language.Ja, Operator.Tn).Normalize("これはテストです"));
        Assert.Equal("2002年", New(Language.Ja, Operator.Itn).Normalize("二零零二年"));
    }
}
