using BazaarPlusPlus.Game.Input;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void CheckEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{message}: expected '{expected}', got '{actual}'");
}

void CheckSequence(IEnumerable<string> expected, IEnumerable<string> actual, string message)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    if (!expectedArray.SequenceEqual(actualArray, StringComparer.OrdinalIgnoreCase))
    {
        failures.Add(
            $"{message}: expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}]"
        );
    }
}

var normalizeCases = new (string? Input, string Expected, string Name)[]
{
    ("<Keyboard>/CTRL", "<Keyboard>/CTRL", "keyboard casing is preserved"),
    ("<Keyboard>/bogus", "<Keyboard>/bogus", "unknown keyboard controls stay accepted"),
    ("<Mouse>/scroll", string.Empty, "unsupported mouse scroll is rejected"),
    ("<Mouse>/LEFTBUTTON", "<Mouse>/leftButton", "mouse button casing is canonicalized"),
    ("not-a-binding", string.Empty, "garbage input is rejected"),
    (null, string.Empty, "null input is rejected"),
    ("   ", string.Empty, "blank input is rejected"),
};

foreach (var testCase in normalizeCases)
{
    CheckEqual(
        testCase.Expected,
        HotkeyBindingPathCore.Normalize(testCase.Input),
        $"Normalize: {testCase.Name}"
    );
}

CheckSequence(
    ["<Keyboard>/ctrl", "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl"],
    HotkeyBindingPathCore.Expand("<Keyboard>/ctrl"),
    "Expand: Ctrl alias"
);
CheckSequence(
    ["<Keyboard>/shift", "<Keyboard>/leftShift", "<Keyboard>/rightShift"],
    HotkeyBindingPathCore.Expand("<Keyboard>/shift"),
    "Expand: Shift alias"
);
CheckSequence(
    ["<Keyboard>/f8"],
    HotkeyBindingPathCore.Expand("<Keyboard>/f8"),
    "Expand: ordinary key"
);

var currentPaths = new Dictionary<BppHotkeyActionId, string>
{
    [BppHotkeyActionId.HoldEnchantPreview] = "<Keyboard>/ctrl",
    [BppHotkeyActionId.HoldUpgradePreview] = "<Keyboard>/shift",
    [BppHotkeyActionId.ToggleCollectionPanel] = "<Keyboard>/tab",
    [BppHotkeyActionId.ToggleLiveBuildPanel] = "<Keyboard>/capsLock",
    [BppHotkeyActionId.ToggleHistoryPanel] = "<Keyboard>/f8",
};

CheckEqual(
    BppHotkeyActionId.HoldEnchantPreview,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.ToggleHistoryPanel,
        "<Keyboard>/leftCtrl",
        currentPaths
    ),
    "FindConflict: leftCtrl conflicts with Ctrl alias"
);

currentPaths[BppHotkeyActionId.HoldEnchantPreview] = "<Keyboard>/leftCtrl";
CheckEqual(
    BppHotkeyActionId.HoldEnchantPreview,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.ToggleHistoryPanel,
        "<Keyboard>/ctrl",
        currentPaths
    ),
    "FindConflict: Ctrl alias conflicts with leftCtrl"
);

CheckEqual<BppHotkeyActionId?>(
    null,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.HoldEnchantPreview,
        "<Keyboard>/leftCtrl",
        currentPaths
    ),
    "FindConflict: candidate action is skipped"
);
CheckEqual<BppHotkeyActionId?>(
    null,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.ToggleHistoryPanel,
        "<Keyboard>/f9",
        currentPaths
    ),
    "FindConflict: unrelated path has no conflict"
);

foreach (var mouseButtonName in HotkeyBindingPathCore.SupportedMouseButtonNames)
{
    Check(
        HotkeyBindingPathCore.DisplayAliases.ContainsKey("<Mouse>/" + mouseButtonName),
        $"DisplayAliases must contain supported mouse button '{mouseButtonName}'"
    );
}

Check(
    HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/scroll"),
    "Unsupported mouse paths: scroll"
);
Check(
    HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/position"),
    "Unsupported mouse paths: position"
);
Check(
    HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/delta"),
    "Unsupported mouse paths: delta"
);
Check(
    !HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/leftButton"),
    "Unsupported mouse paths: standard button stays supported"
);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED ({failures.Count})");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("PASS: HotkeyBindingPathCore behavior checks");
}
