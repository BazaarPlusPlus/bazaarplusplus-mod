using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogPrivacyTests
{
    public static TheoryData<string> SensitiveFieldNames =>
        new()
        {
            "account_id",
            "user_name",
            "display_name",
            "link_code",
            "token",
            "authorization_header",
            "request_body",
            "response_body",
        };

    [Theory]
    [MemberData(nameof(SensitiveFieldNames))]
    public void Render_redacts_sensitive_fields_without_evaluating_their_values(string fieldName)
    {
        var sensitive = Field(0, fieldName, BppLogFieldPrivacy.Sensitive);

        var rendered = Renderer(PosixRoots)
            .Render(Define(sensitive), sensitive.Bind(new ThrowingValue()));

        Assert.EndsWith($" {fieldName}=<redacted>", rendered);
    }

    [Fact]
    public void Render_aliases_the_most_specific_known_posix_root_on_a_segment_boundary()
    {
        var dataPath = Field(0, "data_path", BppLogFieldPrivacy.LocalPath);
        var gamePath = Field(1, "game_path", BppLogFieldPrivacy.LocalPath);
        var homePath = Field(2, "home_path", BppLogFieldPrivacy.LocalPath);
        var nearPrefix = Field(3, "near_prefix", BppLogFieldPrivacy.LocalPath);
        var unknown = Field(4, "unknown", BppLogFieldPrivacy.LocalPath);

        var rendered = Renderer(PosixRoots)
            .Render(
                Define(dataPath, gamePath, homePath, nearPrefix, unknown),
                dataPath.Bind("/Users/alice/Games/The Bazaar/BazaarPlusPlusV4/Screenshots/a.png"),
                gamePath.Bind("/Users/alice/Games/The Bazaar/cache/file.db"),
                homePath.Bind("/Users/alice/Documents/log.txt"),
                nearPrefix.Bind("/Users/bob/Games/The Bazaar-old/private.txt"),
                unknown.Bind("/private/var/secret.txt")
            );

        Assert.Contains("data_path=<bpp-data>/Screenshots/a.png", rendered);
        Assert.Contains("game_path=<game>/cache/file.db", rendered);
        Assert.Contains("home_path=<home>/Documents/log.txt", rendered);
        Assert.Contains("near_prefix=<absolute-path>", rendered);
        Assert.Contains("unknown=<absolute-path>", rendered);
        Assert.DoesNotContain("alice", rendered);
        Assert.DoesNotContain("bob", rendered);
    }

    [Fact]
    public void Render_aliases_windows_paths_without_host_path_semantics_and_hides_unknown_unc_paths()
    {
        var path = Field(0, "path", BppLogFieldPrivacy.LocalPath);
        var unknown = Field(1, "unknown", BppLogFieldPrivacy.LocalPath);
        var roots = new BppLogRedactionRoots(
            gameRoot: @"C:\Games\The Bazaar",
            dataRoot: @"C:\Games\The Bazaar\BazaarPlusPlusV4",
            pluginRoot: @"C:\Games\The Bazaar\BepInEx\plugins",
            homeRoot: @"C:\Users\alice"
        );

        var rendered = Renderer(roots)
            .Render(
                Define(path, unknown),
                path.Bind(@"C:\Games\The Bazaar\BepInEx\plugins\BazaarPlusPlus.dll"),
                unknown.Bind(@"\\server\share\private\file.txt")
            );

        Assert.Contains("path=<plugins>/BazaarPlusPlus.dll", rendered);
        Assert.Contains("unknown=<absolute-path>", rendered);
        Assert.DoesNotContain("server", rendered);
        Assert.DoesNotContain("alice", rendered);
    }

    [Fact]
    public void Render_removes_url_userinfo_query_and_fragment_and_hides_malformed_urls()
    {
        var endpoint = Field(0, "endpoint", BppLogFieldPrivacy.RemoteUri);
        var malformed = Field(1, "malformed", BppLogFieldPrivacy.RemoteUri);

        var rendered = Renderer(PosixRoots)
            .Render(
                Define(endpoint, malformed),
                endpoint.Bind("https://user:pass@example.com/v1/resource?token=secret#private"),
                malformed.Bind("not a url?token=secret")
            );

        Assert.Contains("endpoint=https://example.com/v1/resource", rendered);
        Assert.Contains("malformed=<invalid-url>", rendered);
        Assert.DoesNotContain("user", rendered);
        Assert.DoesNotContain("pass", rendered);
        Assert.DoesNotContain("token", rendered);
        Assert.DoesNotContain("secret", rendered);
        Assert.DoesNotContain("private", rendered);
    }

    [Fact]
    public void Render_applies_declared_correlation_policy()
    {
        var full = Field(
            0,
            "full_id",
            correlation: BppLogCorrelationPolicy.Full,
            cardinality: BppLogCardinality.High
        );
        var shortened = Field(
            1,
            "short_id",
            correlation: BppLogCorrelationPolicy.Short,
            cardinality: BppLogCardinality.High
        );
        var hashed = Field(
            2,
            "hashed_id",
            correlation: BppLogCorrelationPolicy.Hash,
            cardinality: BppLogCardinality.High
        );

        var rendered = Renderer(PosixRoots)
            .Render(
                Define(full, shortened, hashed),
                full.Bind("recording-123456789"),
                shortened.Bind("recording-123456789"),
                hashed.Bind("recording-123456789")
            );

        Assert.Contains("full_id=recording-123456789", rendered);
        Assert.Contains("short_id=recordin", rendered);
        Assert.Contains("hashed_id=d706f4e4b9fb", rendered);
        Assert.DoesNotContain("hashed_id=recording", rendered);
    }

    [Fact]
    public void Render_hashes_the_complete_correlation_value()
    {
        var hashed = Field(
            0,
            "hashed_id",
            correlation: BppLogCorrelationPolicy.Hash,
            cardinality: BppLogCardinality.High
        );
        var sharedPrefix = new string('x', 5000);

        var first = Renderer(PosixRoots).Render(Define(hashed), hashed.Bind(sharedPrefix + "a"));
        var second = Renderer(PosixRoots).Render(Define(hashed), hashed.Bind(sharedPrefix + "b"));
        var invalidHigh = Renderer(PosixRoots).Render(Define(hashed), hashed.Bind("\ud800"));
        var differentInvalidHigh = Renderer(PosixRoots)
            .Render(Define(hashed), hashed.Bind("\ud801"));
        var literalEscape = Renderer(PosixRoots).Render(Define(hashed), hashed.Bind("\\uD800"));

        Assert.NotEqual(first, second);
        Assert.NotEqual(invalidHigh, differentInvalidHigh);
        Assert.NotEqual(invalidHigh, literalEscape);
        Assert.DoesNotContain("field_truncated=true", first);
        Assert.DoesNotContain("field_truncated=true", second);
    }

    [Fact]
    public void Render_bounds_and_escapes_untrusted_external_text_without_corrupting_cjk()
    {
        var external = Field(0, "external", BppLogFieldPrivacy.UntrustedText);
        var rendered = Renderer(PosixRoots)
            .Render(Define(external), external.Bind("编码失败\r\nnext\tline"));

        Assert.EndsWith(" external=\"编码失败\\r\\nnext\\tline\"", rendered);
        Assert.DoesNotContain('\r', rendered);
        Assert.DoesNotContain('\n', rendered);
    }

    [Fact]
    public void Render_hides_unknown_absolute_paths_with_spaces_and_colon_prefixes()
    {
        var external = Field(0, "external", BppLogFieldPrivacy.UntrustedText);
        var rendered = Renderer(PosixRoots)
            .Render(
                Define(external),
                external.Bind(
                    "unix=/Volumes/My Disk/Users/bob/private.txt; "
                        + @"windows=D:\My Games\Users\bob\private.txt; "
                        + "punctuation=/Volumes/My;Private/Users/bob/private.txt\n"
                        + "path:/Users/bob/private.txt\n"
                        + "leading_space=/ Users/bob/private.txt\n"
                        + "single=/alice\n"
                        + "hyphen=/mnt-data/users/bob/private.txt\n"
                        + "cross_line=success\n/alice.txt\n"
                        + @"root_relative=\Users\bob\private.txt"
                )
            );

        Assert.DoesNotContain("bob", rendered);
        Assert.DoesNotContain("My Disk", rendered);
        Assert.DoesNotContain("My Games", rendered);
        Assert.DoesNotContain("private.txt", rendered);
        Assert.Contains("<absolute-path>", rendered);
    }

    [Fact]
    public void Render_preserves_cjk_slash_prose()
    {
        var external = Field(0, "external", BppLogFieldPrivacy.UntrustedText);
        var rendered = Renderer(PosixRoots)
            .Render(Define(external), external.Bind("编码/解码失败 成功 / 失败"));

        Assert.Contains("编码/解码失败", rendered);
        Assert.Contains("成功 / 失败", rendered);
        Assert.DoesNotContain("<absolute-path>", rendered);
    }

    [Fact]
    public void Render_does_not_let_an_embedded_url_shield_a_following_private_path()
    {
        var external = Field(0, "external", BppLogFieldPrivacy.UntrustedText);
        var rendered = Renderer(PosixRoots)
            .Render(
                Define(external),
                external.Bind(
                    "GET https://example.com/ok then /Users/bob/private.txt\n"
                        + "GET https://example.com/ok,path=/Users/bob/private.txt"
                )
            );

        Assert.Contains("https://example.com/ok", rendered);
        Assert.Contains("<absolute-path>", rendered);
        Assert.DoesNotContain("bob", rendered);
        Assert.DoesNotContain("private.txt", rendered);
    }

    [Fact]
    public void Render_does_not_alias_paths_that_escape_a_known_root()
    {
        var path = Field(0, "path", BppLogFieldPrivacy.LocalPath);
        var rendered = Renderer(PosixRoots)
            .Render(Define(path), path.Bind("/Users/alice/Games/The Bazaar/../../private.txt"));

        Assert.DoesNotContain("<game>", rendered);
        Assert.DoesNotContain("..", rendered);
        Assert.DoesNotContain("/Users/alice", rendered);
    }

    [Fact]
    public void Render_does_not_alias_diagnostic_paths_that_escape_a_known_windows_root()
    {
        var external = Field(0, "external", BppLogFieldPrivacy.UntrustedText);
        var roots = new BppLogRedactionRoots(
            gameRoot: @"C:\Games\The Bazaar",
            dataRoot: @"C:\Games\The Bazaar\BazaarPlusPlusV4",
            pluginRoot: @"C:\Games\The Bazaar\BepInEx\plugins",
            homeRoot: @"C:\Users\alice"
        );
        var rendered = Renderer(roots)
            .Render(
                Define(external),
                external.Bind(@"C:\Games\The Bazaar\..\..\Users\bob\private.txt")
            );

        Assert.DoesNotContain("<game>", rendered);
        Assert.DoesNotContain("bob", rendered);
        Assert.DoesNotContain("private.txt", rendered);
        Assert.Contains("<absolute-path>", rendered);
    }

    private static readonly BppLogRedactionRoots PosixRoots = new(
        gameRoot: "/Users/alice/Games/The Bazaar",
        dataRoot: "/Users/alice/Games/The Bazaar/BazaarPlusPlusV4",
        pluginRoot: "/Users/alice/Games/The Bazaar/BepInEx/plugins",
        homeRoot: "/Users/alice"
    );

    private static BppLogEventRenderer Renderer(BppLogRedactionRoots roots) => new(roots);

    private static BppLogEventDefinition Define(params BppLogFieldDefinition[] fields) =>
        new(BppLogFeatureScope.Logger, "logging.privacy.rendered", fields, null);

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogFieldPrivacy privacy = BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None,
        BppLogCardinality cardinality = BppLogCardinality.Low
    ) => new(order, name, privacy, correlation, cardinality);

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("must not run");
    }
}
