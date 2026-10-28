namespace Dogvane.WeTextNet.Tests;

/// <summary>
/// The SDK must work straight from a zip bundle: the parameterless constructors
/// use the bundled assets/wetext.zip, and an explicit `.zip` path is accepted by
/// the path constructor — decompressed in memory, without touching the disk.
/// </summary>
public class DefaultZipTests
{
    [Fact]
    public void ParameterlessConstructorUsesBundledZip()
    {
        var zh = new Normalizer(new NormalizerOptions { Lang = Language.Zh, Operator = Operator.Tn });
        Assert.Equal("二点五平方电线", zh.Normalize("2.5平方电线"));

        var en = new Normalizer(new NormalizerOptions { Lang = Language.En, Operator = Operator.Itn });
        Assert.Equal("$23.50", en.Normalize("twenty three dollars and fifty cents"));
    }

    [Fact]
    public void ZipPathConstructorLoadsInMemory()
    {
        string zip = Path.Combine(AppContext.BaseDirectory, "assets", "wetext.zip");
        Assert.True(File.Exists(zip), $"bundled zip missing from test output: {zip}");

        var normalizer = new Normalizer(zip, new NormalizerOptions { Lang = Language.Zh, Operator = Operator.Itn });
        Assert.Equal("2008/08/08", normalizer.Normalize("二零零八年八月八日"));

        // In-memory loading must not extract anything next to the zip or into the
        // per-user cache location.
        string extractedMarker = Path.Combine(AppContext.BaseDirectory, "assets", "zh");
        Assert.False(Directory.Exists(extractedMarker));
    }

    [Fact]
    public void DirectoryConstructorStillWorks()
    {
        var normalizer = new Normalizer(TestHost.FstsDirectory,
            new NormalizerOptions { Lang = Language.Zh, Operator = Operator.Tn });
        Assert.Equal("价格是十二点五元", normalizer.Normalize("价格是12.5元"));
    }

    [Fact]
    public void NonBundlePathIsRejected()
    {
        Assert.ThrowsAny<Exception>(() => new Normalizer(@"Z:\definitely\not\a\bundle"));
    }
}
