using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppDockButtonIdempotencyTests
{
    [Fact]
    public void Clone_binding_and_visual_state_use_single_idempotent_paths()
    {
        var repoRoot = FindRepoRoot();
        var settingsRoot = Path.Combine(repoRoot, "src", "BazaarPlusPlus", "Game", "Settings");
        var cloneSource = File.ReadAllText(
            Path.Combine(settingsRoot, "BppNativeSettingsButtonClone.cs")
        );
        var visualsSource = File.ReadAllText(Path.Combine(settingsRoot, "BppDockButtonVisuals.cs"));
        var controllerSource = File.ReadAllText(
            Path.Combine(settingsRoot, "BppSettingsDockController.cs")
        );
        var expandedVisualSource = File.ReadAllText(
            Path.Combine(settingsRoot, "BppDockButtonExpandedVisualState.cs")
        );
        var avoidanceSource = File.ReadAllText(
            Path.Combine(settingsRoot, "BppDockButtonAvoidance.cs")
        );

        Assert.Contains("hostRect.Find(placement.DockButtonObjectName)", cloneSource);
        Assert.Contains(
            "GetComponent<Button>() ?? cloneObject.AddComponent<Button>()",
            cloneSource
        );
        Assert.Contains("GetComponent<BppDockButtonExpandedVisualState>()", visualsSource);
        Assert.Equal(
            1,
            CountOccurrences(visualsSource, "AddComponent<BppDockButtonExpandedVisualState>()")
        );
        Assert.Contains("_dockButton.onClick.RemoveAllListeners();", controllerSource);
        Assert.Equal(
            1,
            CountOccurrences(
                controllerSource,
                "_dockButton.onClick.AddListener(OnDockButtonClicked);"
            )
        );
        Assert.Contains("cloneObject.GetComponent<BazaarButtonController>()", cloneSource);
        Assert.DoesNotContain("GetComponentInChildren<BazaarButtonController>", cloneSource);
        Assert.DoesNotContain("Selectable.Transition.None", expandedVisualSource);
        Assert.Contains("currentSelectedGameObject", expandedVisualSource);
        Assert.Contains("SetSelectedGameObject(null)", expandedVisualSource);
        Assert.DoesNotContain("baselineSprite != null", expandedVisualSource);
        Assert.True(
            expandedVisualSource.IndexOf("ResolveBaselineSprite", StringComparison.Ordinal)
                < expandedVisualSource.IndexOf(
                    "switch (_nativeState.Transition)",
                    StringComparison.Ordinal
                ),
            "The verified native selected/base sprite must be applied for every transition type."
        );
        Assert.Contains(
            "GetComponentsInChildren(includeInactive: false, _buttonScratch)",
            avoidanceSource
        );
        Assert.DoesNotContain("var candidates = new BppSettingsDockLocalPosition", avoidanceSource);
        Assert.DoesNotContain("var anchorCorners = new Vector3[4]", avoidanceSource);
        Assert.DoesNotContain("var corners = new Vector3[4]", controllerSource);
    }

    private static string FindRepoRoot()
    {
        for (
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            current != null;
            current = current.Parent
        )
        {
            if (
                File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src", "BazaarPlusPlus"))
            )
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
