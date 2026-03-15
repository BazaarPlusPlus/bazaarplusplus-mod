using System;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarSettingsMenuBridge
{
    private readonly Func<bool> _readValue;
    private readonly Action<bool> _writeValue;

    internal CombatStatusBarSettingsMenuBridge(Func<bool> readValue, Action<bool> writeValue)
    {
        _readValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        _writeValue = writeValue ?? throw new ArgumentNullException(nameof(writeValue));
    }

    internal bool GetInitialValue()
    {
        return _readValue();
    }

    internal void ApplyValue(bool value)
    {
        _writeValue(value);
    }
}
