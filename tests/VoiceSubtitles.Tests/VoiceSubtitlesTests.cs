using System.Reflection;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesTests
{
    [Fact]
    public void Embedded_seed_loads_expected_voice_lines()
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var loadEmbeddedSeed = GetRequiredStaticMethod(repositoryType, "LoadEmbeddedSeed");

        var lines = Assert.IsAssignableFrom<Array>(loadEmbeddedSeed.Invoke(null, null));

        Assert.Equal(5032, lines.Length);
        var first = lines.GetValue(0);
        Assert.NotNull(first);
        Assert.Equal("001_Dooley_V5_RunDefeat_03", GetString(first, "Stem"));
        Assert.Equal("001_Dooley_V5_RunDefeat_03", GetString(first, "English"));
        Assert.Equal("「这次运算结果，不太理想。」", GetString(first, "Chinese"));
        Assert.Equal(2.08f, GetSingle(first, "DurationSeconds"), precision: 2);
    }

    [Fact]
    public void Catalog_resolves_exact_stem_from_embedded_seed()
    {
        var repositoryType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesRepository"
        );
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");
        var loadEmbeddedSeed = GetRequiredStaticMethod(repositoryType, "LoadEmbeddedSeed");
        var replaceCatalog = GetRequiredStaticMethod(catalogType, "ReplaceCatalog");
        var resolveDetailed = GetRequiredStaticMethod(catalogType, "ResolveDetailed");
        var lines = Assert.IsAssignableFrom<Array>(loadEmbeddedSeed.Invoke(null, null));

        replaceCatalog.Invoke(null, new object[] { lines, "embedded-test" });
        var resolution = resolveDetailed.Invoke(
            null,
            new object[] { "event:/VO/Dooley/001_Dooley_V5_RunDefeat_03", "Hero", "Tutorial" }
        );
        Assert.NotNull(resolution);
        var line = GetPropertyValue(resolution, "Line");
        Assert.NotNull(line);

        Assert.Equal("001_Dooley_V5_RunDefeat_03", GetString(line, "Stem"));
        Assert.Equal("event-stem", GetString(resolution, "Strategy"));
        Assert.Equal("embedded-test", GetString(resolution, "CatalogName"));
    }

    private static Type GetRequiredType(string name)
    {
        return Assembly.Load("BazaarPlusPlus").GetType(name)
            ?? throw new InvalidOperationException($"Missing type {name}");
    }

    private static MethodInfo GetRequiredStaticMethod(Type type, string name)
    {
        return type.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
    }

    private static object? GetPropertyValue(object instance, string name)
    {
        return instance.GetType().GetProperty(name)?.GetValue(instance)
            ?? throw new InvalidOperationException(
                $"Missing property {instance.GetType().FullName}.{name}"
            );
    }

    private static string GetString(object instance, string name)
    {
        return Assert.IsType<string>(GetPropertyValue(instance, name));
    }

    private static float GetSingle(object instance, string name)
    {
        return Assert.IsType<float>(GetPropertyValue(instance, name));
    }
}
