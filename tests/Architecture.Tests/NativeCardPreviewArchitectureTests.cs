#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class NativeCardPreviewArchitectureTests
{
    [Fact]
    public void Concrete_native_card_preview_ownership_stays_inside_owning_module()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var moduleRoot = Path.Combine(sourceRoot, "GameInterop", "CardPreview");
        var forbidden = new[]
        {
            "NativeCardPreviewRuntime",
            "NativeCardPreviewReflection",
            "NativeCardPreviewPool",
            "NativeCardPreviewFactory",
            "NativeCardPreviewResource",
            "NativeCardPreviewAssetLoader",
            "NativeCardPreviewPresentation",
            "NativePreviewPresentationTransaction",
        };
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            if (file.StartsWith(moduleRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var source = File.ReadAllText(file);
            foreach (var token in forbidden.Where(source.Contains))
                violations.Add($"{Path.GetRelativePath(sourceRoot, file)}: {token}");
        }

        Assert.True(
            violations.Count == 0,
            "Native preview runtime/reflection/pool/resource types must stay behind the host seam:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Consumers_retain_only_host_scope_and_opaque_sessions()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var collection = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionGridVirtualizer.cs"
            )
        );
        var itemBoard = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "GameInterop",
                "ItemBoardPreview",
                "ItemBoardPreviewSurface.cs"
            )
        );

        Assert.Contains("INativeCardPreviewScope", collection);
        Assert.Contains("INativeCardPreviewSession", collection);
        Assert.DoesNotContain("SetUpTask", collection);
        Assert.DoesNotContain("NativeCardPreviewKind", collection);
        Assert.DoesNotContain("Component Card", collection);

        Assert.Contains("INativeCardPreviewHost", itemBoard);
        Assert.Contains("INativeCardPreviewScope", itemBoard);
        Assert.Contains("INativeCardPreviewSession", itemBoard);
        Assert.DoesNotContain("NativeCardPreviewPool", itemBoard);
        Assert.DoesNotContain("NativeCardPreviewFactory", itemBoard);
        Assert.DoesNotContain("NativeCardPreviewReflection", itemBoard);
    }

    [Fact]
    public void Legacy_handle_lease_and_global_hover_registry_are_removed()
    {
        var moduleRoot = Path.Combine(MainSourceRoot(RepoRoot()), "GameInterop", "CardPreview");

        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewHandle.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewLease.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewHoverTracker.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewHoverRelay.cs")));
    }

    [Fact]
    public void Native_destroy_lifecycle_is_owned_by_the_native_preview_module()
    {
        var modulePatch = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "CardPreview",
                "NativeCardPreviewLifecycle.cs"
            )
        );
        var collectionPatch = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Patches",
                "CollectionPanel",
                "CollectionCardPreviewDestroyPatch.cs"
            )
        );

        Assert.Contains("[HarmonyPatch(typeof(CardPreviewBase), \"OnDestroy\")]", modulePatch);
        Assert.Contains("NativeCardPreviewLifecycle.NotifyDestroyed", modulePatch);
        Assert.DoesNotContain("CollectionPanel", modulePatch);

        Assert.Contains("PreviewOwner?.OnNativeDestroyed", collectionPatch);
        Assert.DoesNotContain("NativeCardPreviewLifecycle", collectionPatch);
    }

    [Fact]
    public void Host_and_pool_wire_the_tested_lifecycle_settlement_seams()
    {
        var moduleRoot = Path.Combine(MainSourceRoot(RepoRoot()), "GameInterop", "CardPreview");
        var host = File.ReadAllText(Path.Combine(moduleRoot, "NativeCardPreviewHost.cs"));
        var pool = File.ReadAllText(Path.Combine(moduleRoot, "NativeCardPreviewPool.cs"));
        var factory = File.ReadAllText(Path.Combine(moduleRoot, "NativeCardPreviewFactory.cs"));

        Assert.Contains("NativeCardPreviewScopeLifetime<NativeCardPreviewResource>", host);
        Assert.Contains("NativeCardPreviewScopeDisposal", host);
        Assert.Contains("NativeCardPreviewHoverState<Scope, NativeCardPreviewResource>", host);
        Assert.Contains("NativeCardPreviewLifecycle.Bind", host);
        Assert.Contains("NativePreviewPresentationTransaction.Apply", host);
        Assert.Contains("NativeCardPreviewPresentation.Create", factory);
        Assert.Contains("NativeCardPreviewPoolSettlement.Prepare", pool);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))
            )
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}
