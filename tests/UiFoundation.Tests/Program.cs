using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;

TestEmbeddedFontExtractionWritesResourceBytes();
TestEmbeddedFontExtractionFailsForMissingResource();
TestTmpFontPolicyDetectsCjkText();
TestBilingualItemNamePresentation();
TestStablePanelTextCompactionKeepsStableSlots();
TestCollectionSortButtonWidthIsCompact();

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
        Assert(
            File.ReadAllText(path) == "font-bytes-for-test\n",
            "Extracted file content must match the embedded resource."
        );
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
                ex.Message.Contains(
                    "UiFoundation.Tests.Resources.missing-font.txt",
                    StringComparison.Ordinal
                ),
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
    Assert(
        !BppTmpFontPolicy.ShouldUseEmbeddedCjkFont("㐀"),
        "TMP font policy should leave Extension A characters on the existing TMP fallback chain."
    );
}

static void TestBilingualItemNamePresentation()
{
    Assert(
        BilingualItemNamePresentation.TryBuild(
            "Lighter",
            "打火机",
            enabled: true,
            isItem: true,
            currentLanguageIsChinese: false
        ) == "Lighter\n<size=65%><noparse>打火机</noparse></size>",
        "Enabled item tooltips should append a smaller official Chinese title."
    );
    Assert(
        BilingualItemNamePresentation.TryBuild(
            "Lighter",
            "打火机",
            enabled: false,
            isItem: true,
            currentLanguageIsChinese: false
        ) == null,
        "Disabled bilingual names should preserve the native title."
    );
    Assert(
        BilingualItemNamePresentation.TryBuild(
            "Lighter",
            "打火机",
            enabled: true,
            isItem: false,
            currentLanguageIsChinese: false
        ) == null,
        "Skill and encounter tooltips should not receive item subtitles."
    );
    Assert(
        BilingualItemNamePresentation.TryBuild(
            "打火机",
            "打火机",
            enabled: true,
            isItem: true,
            currentLanguageIsChinese: true
        ) == null,
        "Chinese clients should not duplicate the Chinese title."
    );
}

static void TestStablePanelTextCompactionKeepsStableSlots()
{
    Assert(
        StablePanelText.Compact("Short status", 32) == "Short status",
        "Short text should not be changed."
    );
    Assert(
        StablePanelText.Compact("Hidden status", 0) == string.Empty,
        "Non-positive budgets should hide the text instead of expanding the slot."
    );
    Assert(
        StablePanelText.Compact("Line one\n\tline two   line three", 64)
            == "Line one line two line three",
        "Medium text should collapse whitespace without truncating."
    );

    var longMessage =
        "Network refresh failed because the upstream endpoint returned a long diagnostic payload "
        + "with repeated retry metadata and a platform-specific file path that would otherwise "
        + "grow the panel footer.";
    var compact = StablePanelText.Compact(longMessage, 80);
    Assert(compact.Length <= 80, "Long text should stay within the caller's character budget.");
    Assert(
        compact.EndsWith("...", StringComparison.Ordinal),
        "Long text should advertise truncation."
    );
    Assert(
        compact.StartsWith("Network refresh failed", StringComparison.Ordinal),
        "Long text should keep the actionable prefix visible."
    );

    var unbroken = new string('x', 300);
    Assert(
        StablePanelText.Compact(unbroken, 48).Length == 48,
        "Unbroken text should also be clamped."
    );
}

static void TestCollectionSortButtonWidthIsCompact()
{
    Assert(
        Sizes.CollectionSortButtonWidth == 60f,
        "CollectionPanel sort buttons should use the compact 60pt token."
    );
    Assert(
        Sizes.CollectionSortButtonWidth < Sizes.RunsTabWidth,
        "CollectionPanel sort buttons should be narrower than top-level tab buttons."
    );
    Assert(
        Sizes.CollectionSortButtonWidth >= Sizes.InfoChipMinWidth,
        "CollectionPanel sort buttons should not shrink below the existing compact chip floor."
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
