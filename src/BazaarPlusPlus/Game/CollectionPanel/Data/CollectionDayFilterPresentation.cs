#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal readonly struct CollectionDayFilterPresentation
{
    private CollectionDayFilterPresentation(bool isVisible, bool isEnabled, bool isActive)
    {
        IsVisible = isVisible;
        IsEnabled = isEnabled;
        IsActive = isActive;
    }

    public bool IsVisible { get; }

    public bool IsEnabled { get; }

    public bool IsActive { get; }

    public static CollectionDayFilterPresentation For(
        CollectionTabProfile profile,
        bool hasCurrentRunDay,
        bool isSelected
    ) =>
        new(
            isVisible: true,
            isEnabled: profile.ShowDayFilter && hasCurrentRunDay,
            isActive: profile.ShowDayFilter && hasCurrentRunDay && isSelected
        );
}
