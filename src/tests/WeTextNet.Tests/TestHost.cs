using System.Collections.Concurrent;

namespace Dogvane.WeTextNet.Tests;

/// <summary>Locates the extracted wetext FST bundle and shares Normalizers across tests.</summary>
public static class TestHost
{
    private static readonly ConcurrentDictionary<string, Normalizer> Normalizers = new();

    public static string FstsDirectory { get; } = LocateFsts();

    public static Normalizer GetNormalizer(NormalizerOptions options) =>
        Normalizers.GetOrAdd(options.CacheKey, _ => new Normalizer(FstsDirectory, options));

    private static string LocateFsts()
    {
        string? env = Environment.GetEnvironmentVariable("WETEXTNET_FSTS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (; dir != null; dir = dir.Parent!)
        {
            string candidate = Path.Combine(dir.FullName, "wetext_fsts");
            if (File.Exists(Path.Combine(candidate, "zh", "tn", "tagger.fst")))
                return candidate;
        }
        throw new DirectoryNotFoundException(
            "wetext_fsts not found in any parent of the test binary; set WETEXTNET_FSTS to the FST bundle directory.");
    }
}
