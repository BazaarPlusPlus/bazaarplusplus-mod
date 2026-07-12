using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogExceptionTests
{
    [Fact]
    public void Render_projects_the_outer_exception_and_at_most_three_inner_exceptions()
    {
        var exception = new InvalidOperationException(
            "outer",
            new ArgumentException(
                "inner-one",
                new FormatException(
                    "inner-two",
                    new IOException("inner-three", new Exception("inner-four"))
                )
            )
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.Contains("exception_type=System.InvalidOperationException", rendered);
        Assert.Contains("exception_hresult=0x80131509", rendered);
        Assert.Contains("exception_message=outer", rendered);
        Assert.Contains("exception_inner_1_type=System.ArgumentException", rendered);
        Assert.Contains("exception_inner_2_type=System.FormatException", rendered);
        Assert.Contains("exception_inner_3_type=System.IO.IOException", rendered);
        Assert.DoesNotContain("exception_inner_4", rendered);
        Assert.DoesNotContain("inner-four", rendered);
    }

    [Fact]
    public void Render_aliases_known_roots_in_exception_messages_and_stacks()
    {
        var exception = new ProjectedException(
            "failed at /Users/alice/Games/The Bazaar/BazaarPlusPlusV4/replays/a.json",
            "HEAD /Users/alice/Games/The Bazaar/BepInEx/plugins/BazaarPlusPlus.dll\n"
                + "TAIL /Users/alice/private.txt"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.Contains("<bpp-data>/replays/a.json", rendered);
        Assert.Contains("<plugins>/BazaarPlusPlus.dll", rendered);
        Assert.Contains("<home>/private.txt", rendered);
        Assert.DoesNotContain("/Users/alice", rendered);
        Assert.DoesNotContain('\n', rendered);
    }

    [Fact]
    public void Render_aliases_windows_roots_in_exception_text_on_any_host_os()
    {
        var roots = new BppLogRedactionRoots(
            gameRoot: @"C:\Games\The Bazaar",
            dataRoot: @"C:\Games\The Bazaar\BazaarPlusPlusV4",
            pluginRoot: @"C:\Games\The Bazaar\BepInEx\plugins",
            homeRoot: @"C:\Users\alice"
        );
        var exception = new ProjectedException(
            @"failed at C:\Games\The Bazaar\BazaarPlusPlusV4\replays\a.json",
            @"HEAD C:\Games\The Bazaar\BepInEx\plugins\BazaarPlusPlus.dll"
        );

        var rendered = Renderer(roots).Render(Define(), [], exception);

        Assert.Contains("<bpp-data>\\\\replays\\\\a.json", rendered);
        Assert.Contains("<plugins>\\\\BazaarPlusPlus.dll", rendered);
        Assert.DoesNotContain(@"C:\Games", rendered);
    }

    [Fact]
    public void Render_redacts_secret_shaped_exception_text()
    {
        var exception = new ProjectedException(
            "Authorization: Bearer bearer-secret\n"
                + "token=token-secret\naccount_id=account-secret\nuser_name=Alice\n"
                + "display_name=AliceDisplay\nlink_code=LINK-SECRET\n"
                + "request_body={\n\"token\":\"json-secret\",\n\"account_id\":\"json-account\"\n}\n"
                + "response_body={private response}",
            "https://example.com/fail?token='url-secret identity'#fragment"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        foreach (
            var secret in new[]
            {
                "bearer-secret",
                "token-secret",
                "account-secret",
                "Alice",
                "AliceDisplay",
                "LINK-SECRET",
                "private request",
                "private response",
                "json-secret",
                "json-account",
                "url-secret",
                "identity",
                "fragment",
            }
        )
            Assert.DoesNotContain(secret, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", rendered);
        Assert.Contains("https://example.com/fail", rendered);
    }

    [Theory]
    [InlineData("{\"request_body\":\"body-secret\"}", "body-secret")]
    [InlineData("{\"response_body\":\"response-secret\"}", "response-secret")]
    [InlineData("{\"headers\":{\"X-Key\":\"header-secret\"}}", "header-secret")]
    [InlineData("{\"cookie\":\"cookie-secret\"}", "cookie-secret")]
    [InlineData("{\"token\":\"abc\\\"PRIVATE-TAIL\"}", "PRIVATE-TAIL")]
    public void Render_redacts_quoted_json_secret_fields(string message, string secret)
    {
        var rendered = Renderer().Render(Define(), [], new ProjectedException(message, "stack"));

        Assert.DoesNotContain(secret, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", rendered);
    }

    [Fact]
    public void Render_preserves_exception_stack_evidence_when_normal_fields_exhaust_their_budget()
    {
        var fields = Enumerable
            .Range(0, 12)
            .Select(index => new BppLogFieldDefinition(
                index,
                $"detail_{index}",
                BppLogFieldPrivacy.Public,
                BppLogCorrelationPolicy.None,
                BppLogCardinality.Low
            ))
            .ToArray();
        var values = fields.Select(field => field.Bind(new string('x', 300))).ToArray();
        var longMiddle = string.Join("\n", Enumerable.Repeat(new string('m', 200), 80));
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD\n" + longMiddle + "\nSTACK-TAIL"
        );
        var definition = new BppLogEventDefinition(
            BppLogFeatureScope.Logger,
            "logging.operation.failed",
            fields,
            null
        );

        var rendered = Renderer().Render(definition, values, exception);

        Assert.True(rendered.Length <= BppLogEventRenderer.ExceptionRecordCharacterBudget);
        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.Contains("exception_truncated=true", rendered);
        Assert.Contains("record_truncated=true", rendered);
    }

    [Fact]
    public void Render_preserves_stack_head_and_tail_with_an_in_budget_truncation_marker()
    {
        var longMiddle = string.Join("\n", Enumerable.Repeat(new string('m', 200), 80));
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD\n" + longMiddle + "\nSTACK-TAIL"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.True(rendered.Length <= BppLogEventRenderer.ExceptionRecordCharacterBudget);
        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.Contains("exception_truncated=true", rendered);
    }

    [Fact]
    public void Render_preserves_the_tail_of_a_long_single_line_stack()
    {
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD " + new string('m', 20000) + " STACK-TAIL"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.Contains("exception_truncated=true", rendered);
    }

    [Fact]
    public void Render_redacts_the_full_stack_before_selecting_its_tail()
    {
        var privateTail = string.Concat(Enumerable.Repeat("bob-secret", 2500));
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD\n/Users/bob/" + privateTail + "\nSTACK-TAIL"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.DoesNotContain("bob", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bob-secret", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_never_throws_when_exception_projection_accessors_throw()
    {
        var thrown = Record.Exception(() =>
            Renderer().Render(Define(), [], new HostileException())
        );
        var rendered = Renderer().Render(Define(), [], new HostileException());

        Assert.Null(thrown);
        Assert.Contains("exception_type=", rendered);
        Assert.Contains("exception_message=<unavailable>", rendered);
        Assert.Contains("exception_stack=<unavailable>", rendered);
    }

    private static BppLogEventRenderer Renderer() =>
        Renderer(
            new BppLogRedactionRoots(
                gameRoot: "/Users/alice/Games/The Bazaar",
                dataRoot: "/Users/alice/Games/The Bazaar/BazaarPlusPlusV4",
                pluginRoot: "/Users/alice/Games/The Bazaar/BepInEx/plugins",
                homeRoot: "/Users/alice"
            )
        );

    private static BppLogEventRenderer Renderer(BppLogRedactionRoots roots) => new(roots);

    private static BppLogEventDefinition Define() =>
        new(BppLogFeatureScope.Logger, "logging.operation.failed", [], null);

    private class ProjectedException(string message, string stackTrace) : Exception(message)
    {
        public override string? StackTrace => stackTrace;
    }

    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("unsafe message");

        public override string? StackTrace => throw new InvalidOperationException("unsafe stack");
    }
}
