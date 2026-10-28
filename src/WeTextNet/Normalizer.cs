using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Dogvane.WeTextNet;

public enum Language
{
    Auto,
    Zh,
    En,
    Ja,
}

public enum Operator
{
    Tn,
    Itn,
}

/// <summary>Options mirroring wetext 0.1.8's NormalizerConfig; every flag defaults to false.</summary>
public sealed class NormalizerOptions
{
    public Language Lang { get; init; } = Language.Auto;
    public Operator Operator { get; init; } = Operator.Tn;
    public bool TraditionalToSimple { get; init; }
    public bool FullToHalf { get; init; }
    public bool RemoveInterjections { get; init; }
    public bool RemovePuncts { get; init; }
    public bool TagOov { get; init; }
    public bool Enable0To9 { get; init; }
    public bool RemoveErhua { get; init; }

    internal string CacheKey => string.Join('|',
        (int)Lang, (int)Operator, TraditionalToSimple, FullToHalf, RemoveInterjections,
        RemovePuncts, TagOov, Enable0To9, RemoveErhua);
}

/// <summary>
/// Text normalization / inverse text normalization runtime. Loads the pre-built
/// OpenFst graphs shipped in the `wetext` pip package (wetext/fsts) and runs the
/// same pipeline as the Python runtime: preprocess → tag → reorder → verbalize →
/// postprocess.
/// </summary>
public sealed class Normalizer
{
    private static readonly Regex DigitRegex = new(@"\d", RegexOptions.Compiled);
    private static readonly Regex ErhuaRegex = new(@"儿|兒", RegexOptions.Compiled);

    private readonly GraphStore _graphs;
    private readonly NormalizerOptions _options;

    /// <summary>
    /// Creates a normalizer backed by the default FST bundle (`assets/wetext.zip`
    /// placed next to the assembly — the NuGet package ships it as a content
    /// file, so it is already in your output directory). Override the location
    /// with the WETEXTNET_ZIP environment variable.
    /// </summary>
    public Normalizer(NormalizerOptions? options = null)
    {
        _graphs = DefaultStore();
        _options = options ?? new NormalizerOptions();
    }

    /// <summary>
    /// Creates a normalizer from an FST bundle. The path may be either a
    /// directory holding the extracted `wetext/fsts` layout, or a `.zip` file
    /// with that layout inside — zips are decompressed in memory, one entry per
    /// graph, and never touch the disk.
    /// </summary>
    public Normalizer(string fstsDirectoryOrZipFile, NormalizerOptions? options = null)
    {
        _graphs = GraphStore.Create(fstsDirectoryOrZipFile);
        _options = options ?? new NormalizerOptions();
    }

    private static GraphStore DefaultStore()
    {
        string? envZip = Environment.GetEnvironmentVariable("WETEXTNET_ZIP");
        if (!string.IsNullOrEmpty(envZip) && File.Exists(envZip))
            return GraphStore.Create(envZip);

        string adjacent = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "wetext.zip");
        if (File.Exists(adjacent))
            return GraphStore.Create(adjacent);

        throw new FileNotFoundException(
            "Default FST bundle not found: expected 'assets/wetext.zip' next to the application "
            + "(the NuGet package provides it as a content file). Set WETEXTNET_ZIP to an explicit "
            + "path, or construct Normalizer with an FST directory or zip file.");
    }

    /// <summary>
    /// Lazily loads graphs from a directory or a zip file. Entries are cached
    /// per relative path; zip archives are read in memory and hold no permanent
    /// file handle.
    /// </summary>
    private sealed class GraphStore
    {
        private readonly string? _directory;
        private readonly string? _zipFile;
        private readonly ConcurrentDictionary<string, Fst> _cache = new();

        private GraphStore(string? directory, string? zipFile)
        {
            _directory = directory;
            _zipFile = zipFile;
        }

        public static GraphStore Create(string path)
        {
            if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using ZipArchive probe = ZipFile.OpenRead(path);
                if (probe.GetEntry("zh/tn/tagger.fst") is null)
                    throw new InvalidDataException($"{path}: not a wetext FST bundle (missing zh/tn/tagger.fst)");
                return new GraphStore(directory: null, zipFile: path);
            }
            if (Directory.Exists(path))
                return new GraphStore(directory: path, zipFile: null);
            throw new DirectoryNotFoundException($"FST bundle not found (neither zip nor directory): {path}");
        }

        public Fst Get(string relativePath) => _cache.GetOrAdd(relativePath, Load);

        private Fst Load(string relativePath)
        {
            if (_directory is not null)
                return Fst.Read(System.IO.Path.Join(_directory, relativePath));

            using ZipArchive archive = ZipFile.OpenRead(_zipFile!);
            var entry = archive.GetEntry(relativePath)
                ?? throw new FileNotFoundException($"{_zipFile} does not contain {relativePath}");
            using Stream stream = entry.Open();
            return Fst.Read(stream);
        }
    }

    public NormalizerOptions Options => _options;

    /// <summary>Normalizes the text end to end.</summary>
    public string Normalize(string text)
    {
        string prepared = text.Trim();
        Language lang = ResolveLang(prepared);

        string output = Preprocess(prepared);
        if (ShouldNormalize(output, lang))
        {
            output = Tag(output, lang);
            output = new TokenParser(LangName(lang), OpName()).Reorder(output);
            output = Verbalize(output, lang);
        }
        return Postprocess(output);
    }

    /// <summary>Runs the tagger graph over raw text, producing the tagged token stream.</summary>
    public string Tag(string text, Language? lang = null)
    {
        if (text.Length == 0)
            return string.Empty;
        Language resolved = lang ?? ResolveLang(text);
        return Transduce(text, TaggerPath(resolved))!.Trim();
    }

    /// <summary>Runs the verbalizer graph over a (reordered) tagged token stream.</summary>
    public string Verbalize(string tagged, Language? lang = null)
    {
        if (tagged.Length == 0)
            return string.Empty;
        Language resolved = lang ?? ResolveLang(tagged);
        return Transduce(tagged, VerbalizerPath(resolved))!.Trim();
    }

    /// <summary>Applies the optional preprocessor (traditional → simplified) and trims.</summary>
    private string Preprocess(string text)
    {
        if (_options.TraditionalToSimple)
            text = Transduce(text, "traditional_to_simple.fst")!;
        return text.Trim();
    }

    /// <summary>Applies the enabled postprocessor graphs in wetext's fixed order, then trims.</summary>
    private string Postprocess(string text)
    {
        if (_options.FullToHalf)
            text = Transduce(text, "full_to_half.fst")!;
        if (_options.RemoveInterjections)
            text = Transduce(text, "remove_interjections.fst")!;
        if (_options.RemovePuncts)
            text = Transduce(text, "remove_puncts.fst")!;
        if (_options.TagOov)
            text = Transduce(text, "tag_oov.fst")!;
        return text.Trim();
    }

    private bool ShouldNormalize(string text, Language lang)
    {
        if (_options.Operator == Operator.Tn && lang != Language.En)
        {
            if (DigitRegex.IsMatch(text))
                return true;
            if (_options.RemoveErhua && ErhuaRegex.IsMatch(text))
                return true;
            return false;
        }
        return text.Length > 0;
    }

    private Language ResolveLang(string text)
    {
        if (_options.Lang != Language.Auto)
            return _options.Lang;
        return DetectLanguage(text);
    }

    private string TaggerPath(Language lang)
    {
        string name = _options.Enable0To9 && lang != Language.En && _options.Operator == Operator.Itn
            ? "tagger_enable_0_to_9.fst"
            : "tagger.fst";
        return $"{LangName(lang)}/{OpName()}/{name}";
    }

    private string VerbalizerPath(Language lang)
    {
        string name = _options.RemoveErhua && lang == Language.Zh && _options.Operator == Operator.Tn
            ? "verbalizer_remove_erhua.fst"
            : "verbalizer.fst";
        return $"{LangName(lang)}/{OpName()}/{name}";
    }

    // kaldifst's TextNormalizer yields an empty string when composition finds no
    // accepting path (e.g. the verbalizer dead-ends on an unusual tagging); the
    // pipeline then keeps going with "".
    private string Transduce(string text, string relativePath)
    {
        Fst fst = _graphs.Get(relativePath);
        return Transducer.Transduce(text, fst) ?? string.Empty;
    }

    private string LangName(Language lang) => lang switch
    {
        Language.En => "en",
        Language.Ja => "ja",
        _ => "zh",
    };

    private string OpName() => _options.Operator == Operator.Tn ? "tn" : "itn";

    /// <summary>Python wetext get_lang: Han characters or all-digits text means Chinese.</summary>
    internal static Language DetectLanguage(string text)
    {
        bool containsChinese = text.Any(IsHan);
        return containsChinese || (text.Length > 0 && text.All(char.IsDigit)) ? Language.Zh : Language.En;
    }

    private static bool IsHan(char ch) => ch >= '\u4e00' && ch <= '\u9fff';
}
