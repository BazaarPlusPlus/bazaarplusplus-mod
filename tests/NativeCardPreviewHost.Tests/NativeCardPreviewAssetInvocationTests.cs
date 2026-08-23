#nullable enable
using System.Reflection;
using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeCardPreviewAssetInvocationTests
{
    [Fact]
    public async Task Legacy_single_parameter_signature_is_invokable()
    {
        var method = RequiredMethod(nameof(InstantiateLegacy));
        var assetReference = new FakeAssetReference();

        var supported = NativeCardPreviewAssetInvocation.TryBuildArguments(
            method,
            assetReference,
            out var arguments
        );

        Assert.True(supported);
        Assert.Single(arguments);
        var task = Assert.IsType<Task<object>>(method.Invoke(null, arguments));
        Assert.Same(assetReference, await task);
    }

    [Fact]
    public async Task Optional_asset_scope_signature_uses_native_current_scope_default()
    {
        var method = RequiredMethod(nameof(InstantiateScoped));
        var assetReference = new FakeAssetReference();

        var supported = NativeCardPreviewAssetInvocation.TryBuildArguments(
            method,
            assetReference,
            out var arguments
        );

        Assert.True(supported);
        Assert.Equal(2, arguments.Length);
        Assert.Null(arguments[1]);
        var task = Assert.IsType<Task<InvocationResult>>(method.Invoke(null, arguments));
        var result = await task;
        Assert.Same(assetReference, result.AssetReference);
        Assert.Null(result.Scope);
    }

    [Fact]
    public void Unknown_second_parameter_is_rejected()
    {
        var method = RequiredMethod(nameof(InstantiateUnknown));

        var supported = NativeCardPreviewAssetInvocation.TryBuildArguments(
            method,
            new FakeAssetReference(),
            out var arguments
        );

        Assert.False(supported);
        Assert.Empty(arguments);
    }

    private static MethodInfo RequiredMethod(string name) =>
        typeof(NativeCardPreviewAssetInvocationTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"Missing test method {name}.");

    private static Task<object> InstantiateLegacy(FakeAssetReference assetReference) =>
        Task.FromResult<object>(assetReference);

    private static Task<InvocationResult> InstantiateScoped(
        FakeAssetReference assetReference,
        AssetScope? scope = null
    ) => Task.FromResult(new InvocationResult(assetReference, scope));

    private static Task<object> InstantiateUnknown(
        FakeAssetReference assetReference,
        string? unexpected = null
    ) => Task.FromResult<object>(assetReference);

    private sealed class FakeAssetReference;

    private enum AssetScope
    {
        Global,
        InRun,
        OutOfRun,
    }

    private sealed record InvocationResult(FakeAssetReference AssetReference, AssetScope? Scope);
}
