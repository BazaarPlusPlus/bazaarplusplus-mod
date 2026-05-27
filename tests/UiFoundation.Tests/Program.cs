using BazaarPlusPlus.Infrastructure.Fonts;
TestEmbeddedFontExtractionWritesResourceBytes();
TestEmbeddedFontExtractionFailsForMissingResource();
TestTmpFontPolicyDetectsCjkText();

Console.WriteLine("UiFoundation checks passed.");

static void TestEmbeddedFontExtractionWritesResourceBytes()
{
    var cacheRoot = CreateTempDirectory();
    try
    {
        var path = EmbeddedFontFile.Extract(
            typeof(Program).Assembly,
            "UiFoundation.Tests.Resources.embedded-font-test.txt",
            "sample-font.txt",
            cacheRoot
        );

        Assert(path == Path.Combine(cacheRoot, "sample-font.txt"), "Unexpected extracted path.");
        Assert(File.ReadAllText(path) == "font-bytes-for-test\n", "Extracted file content must match the embedded resource.");
    }
    finally
    {
        TryDeleteDirectory(cacheRoot);
    }
}

static void TestEmbeddedFontExtractionFailsForMissingResource()
{
    var cacheRoot = CreateTempDirectory();
    try
    {
        try
        {
            EmbeddedFontFile.Extract(
                typeof(Program).Assembly,
                "UiFoundation.Tests.Resources.missing-font.txt",
                "missing-font.txt",
                cacheRoot
            );
        }
        catch (FileNotFoundException ex)
        {
            Assert(
                ex.Message.Contains("UiFoundation.Tests.Resources.missing-font.txt", StringComparison.Ordinal),
                "Missing-resource error should include the manifest resource name."
            );
            return;
        }

        throw new InvalidOperationException("Missing resource should throw FileNotFoundException.");
    }
    finally
    {
        TryDeleteDirectory(cacheRoot);
    }
}

static void TestTmpFontPolicyDetectsCjkText()
{
    Assert(
        BppTmpFontPolicy.ShouldUseEmbeddedCjkFont("由 Alice 支持"),
        "TMP font policy should use the embedded CJK font for Chinese sponsor text."
    );
    Assert(
        BppTmpFontPolicy.ShouldUseEmbeddedCjkFont(
            "Card Set Selection: 点击物品加入/移除，CapsLock 退出"
        ),
        "TMP font policy should use the embedded CJK font for mixed Chinese mode text."
    );
    Assert(
        !BppTmpFontPolicy.ShouldUseEmbeddedCjkFont("Supported by Alice"),
        "TMP font policy should leave pure Latin text on the existing TMP font."
    );
}

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), $"bpp-ui-foundation-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
        // Best-effort cleanup for temp test directories.
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
