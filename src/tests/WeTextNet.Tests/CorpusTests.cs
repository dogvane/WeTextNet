using System.Text.Json;

namespace Dogvane.WeTextNet.Tests;

/// <summary>
/// End-to-end corpus generated from the two upstream projects: every case comes
/// from PythonWeTextProcessing's tn/itn test data files, cross-validated against
/// the installed wetext 0.1.8 runtime (the exact graphs WeTextNet loads).
/// </summary>
public class CorpusTests
{
    public sealed record CorpusCase(string Config, string File, string Lang, string Operator,
        Dictionary<string, bool> Options, string Input, string Expected);

    public static IEnumerable<object[]> Cases()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WeTextNet_testcases.json"));
        var doc = JsonDocument.Parse(json);
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            var options = new Dictionary<string, bool>();
            foreach (JsonProperty prop in element.GetProperty("options").EnumerateObject())
                options[prop.Name] = prop.Value.GetBoolean();

            yield return new object[]
            {
                new CorpusCase(
                    element.GetProperty("config").GetString()!,
                    element.GetProperty("file").GetString()!,
                    element.GetProperty("lang").GetString()!,
                    element.GetProperty("operator").GetString()!,
                    options,
                    element.GetProperty("input").GetString()!,
                    element.GetProperty("expected").GetString()!),
            };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NormalizeMatchesWetextReference(CorpusCase corpusCase)
    {
        var options = new NormalizerOptions
        {
            Lang = Enum.Parse<Language>(corpusCase.Lang, ignoreCase: true),
            Operator = Enum.Parse<Operator>(corpusCase.Operator, ignoreCase: true),
            TraditionalToSimple = corpusCase.Options.GetValueOrDefault("traditional_to_simple"),
            FullToHalf = corpusCase.Options.GetValueOrDefault("full_to_half"),
            RemoveInterjections = corpusCase.Options.GetValueOrDefault("remove_interjections"),
            RemovePuncts = corpusCase.Options.GetValueOrDefault("remove_puncts"),
            TagOov = corpusCase.Options.GetValueOrDefault("tag_oov"),
            Enable0To9 = corpusCase.Options.GetValueOrDefault("enable_0_to_9"),
            RemoveErhua = corpusCase.Options.GetValueOrDefault("remove_erhua"),
        };

        Normalizer normalizer = TestHost.GetNormalizer(options);
        string actual = normalizer.Normalize(corpusCase.Input);

        Assert.True(actual == corpusCase.Expected,
            $"[{corpusCase.Config} {corpusCase.File}] {corpusCase.Input}\n  expected: {corpusCase.Expected}\n  actual:   {actual}");
    }
}
