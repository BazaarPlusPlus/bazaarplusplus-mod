using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeTooltipRefreshTransactionTests
{
    [Fact]
    public void Success_commits_replacement_before_rehover()
    {
        var current = new Value("old");
        var replacement = new Value("new");
        var stored = current;
        var calls = new List<string>();

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () =>
            {
                calls.Add("create");
                return replacement;
            },
            value =>
            {
                stored = value;
                calls.Add($"write:{value.Name}");
                return true;
            },
            () => calls.Add("hide"),
            () =>
            {
                calls.Add("hover");
                return true;
            }
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.Refreshed, result.Status);
        Assert.Same(replacement, stored);
        Assert.Equal(new[] { "create", "write:new", "hide", "hover" }, calls);
    }

    [Fact]
    public void Failed_rehover_restores_old_value_and_tooltip()
    {
        var current = new Value("old");
        var stored = current;
        var hoverCount = 0;

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () => new Value("new"),
            value =>
            {
                stored = value;
                return true;
            },
            () => { },
            () => ++hoverCount > 1
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.RehoverFailed, result.Status);
        Assert.Same(current, stored);
        Assert.Equal(2, hoverCount);
    }

    [Fact]
    public void Failed_write_rolls_back_even_if_write_partially_mutated_state()
    {
        var current = new Value("old");
        var stored = current;
        var writeCount = 0;

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () => new Value("new"),
            value =>
            {
                stored = value;
                writeCount++;
                return writeCount > 1;
            },
            () => throw new InvalidOperationException("must not rehover"),
            () => throw new InvalidOperationException("must not rehover")
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.WriteFailed, result.Status);
        Assert.Same(current, stored);
        Assert.Equal(2, writeCount);
    }

    [Fact]
    public void Failed_rollback_does_not_rehover_unrestored_replacement()
    {
        var current = new Value("old");
        var replacement = new Value("new");
        var stored = current;
        var writeCount = 0;
        var rehoverCount = 0;

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () => replacement,
            value =>
            {
                writeCount++;
                if (writeCount == 1)
                {
                    stored = value;
                    return true;
                }

                return false;
            },
            () => { },
            () =>
            {
                rehoverCount++;
                return false;
            }
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.RollbackFailed, result.Status);
        Assert.Same(replacement, stored);
        Assert.Equal(2, writeCount);
        Assert.Equal(1, rehoverCount);
    }

    private sealed record Value(string Name);
}
