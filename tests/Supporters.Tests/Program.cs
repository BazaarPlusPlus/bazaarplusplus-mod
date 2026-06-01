using BazaarPlusPlus.Game.Supporters;

TestAttributionText();
TestEmptyInputReturnsNoSample();
TestInvalidEntriesAreIgnored();
TestTierWeightsSelectDefaultBronzeSilverAndGoldBuckets();
TestTierBucketSelectionUsesUniformIndexRanges();

Console.WriteLine("Supporters checks passed.");

static void TestAttributionText()
{
    AssertEqual(
        "Supported by Alice",
        BPPSupporterAttributionText.FormatSupportedBy("Alice", "en"),
        "English attribution should preserve the existing Supported by wording."
    );
    AssertEqual(
        "由 Alice 支持",
        BPPSupporterAttributionText.FormatSupportedBy("Alice", "zh-CN"),
        "Chinese attribution should preserve the existing localized wording."
    );
    AssertEqual(
        "由 Alice 支持",
        BPPSupporterAttributionText.FormatSupportedBy(" Alice ", "zh-Hant"),
        "Attribution should trim supporter names before formatting."
    );
    AssertEqual(
        string.Empty,
        BPPSupporterAttributionText.FormatSupportedBy(" ", "zh-CN"),
        "Blank supporter names should not produce visible attribution text."
    );
}

static void TestEmptyInputReturnsNoSample()
{
    var sample = BPPSupporterSampler.Sample(Array.Empty<BPPSupporterEntry>(), Rolls(0f));

    AssertFalse(sample.HasValue, "Empty supporter input should not produce a sample.");
    AssertEqual(string.Empty, sample.Name, "Empty supporter input should return an empty name.");
    AssertEqual(0, sample.Tier, "Empty supporter input should return tier 0.");
}

static void TestInvalidEntriesAreIgnored()
{
    var entries = new[] { Entry("  ", 4), Entry("Zero Tier", 0), Entry("Valid Supporter", 2) };

    var sample = BPPSupporterSampler.Sample(entries, Rolls(0.99f, 0.99f));

    AssertTrue(
        sample.HasValue,
        "A valid entry should still be sampled when invalid entries exist."
    );
    AssertEqual(
        "Valid Supporter",
        sample.Name,
        "Sampler should ignore blank names and non-positive tiers."
    );
    AssertEqual(2, sample.Tier, "Sampler should preserve the selected supporter's tier.");
}

static void TestTierWeightsSelectDefaultBronzeSilverAndGoldBuckets()
{
    var entries = new[]
    {
        Entry("Default", 1),
        Entry("Bronze", 2),
        Entry("Silver", 3),
        Entry("Gold", 4),
    };

    AssertEqual(
        "Default",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0f)).Name,
        "The default tier occupies the first 1/12 of the tier roll."
    );
    AssertEqual(
        "Bronze",
        BPPSupporterSampler.Sample(entries, Rolls(1f / 12f + 0.001f, 0f)).Name,
        "Tier 2 should be selected after the default 1/12 range."
    );
    AssertEqual(
        "Silver",
        BPPSupporterSampler.Sample(entries, Rolls(3f / 12f + 0.001f, 0f)).Name,
        "Tier 3 should be selected after the default plus tier-2 ranges."
    );
    AssertEqual(
        "Gold",
        BPPSupporterSampler.Sample(entries, Rolls(6f / 12f + 0.001f, 0f)).Name,
        "Tier 4 should receive the final 6/12 range."
    );
}

static void TestTierBucketSelectionUsesUniformIndexRanges()
{
    var entries = new[] { Entry("Alice", 4), Entry("Bob", 4), Entry("Cara", 4) };

    AssertEqual(
        "Alice",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0f)).Name,
        "The first third of a tier bucket should select the first entry."
    );
    AssertEqual(
        "Bob",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0.34f)).Name,
        "The second third of a tier bucket should select the second entry."
    );
    AssertEqual(
        "Cara",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0.67f)).Name,
        "The final third of a tier bucket should select the third entry."
    );
}

static BPPSupporterEntry Entry(string name, int tier) => new() { Name = name, Tier = tier };

static Func<float> Rolls(params float[] values)
{
    var index = 0;
    return () =>
    {
        if (index >= values.Length)
            throw new InvalidOperationException("Test roll provider was exhausted.");

        return values[index++];
    };
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
