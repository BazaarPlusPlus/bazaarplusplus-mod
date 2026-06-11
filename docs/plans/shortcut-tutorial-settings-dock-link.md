# Shortcut Tutorial Settings Dock Link

## Goal

Add one BazaarPlusPlus settings dock row named `快捷键教程` for Chinese users and
`Hotkey Tutorial` for non-Chinese users. Clicking the row opens:

- Chinese language: `https://bazaarplusplus.com/tutorial`
- Non-Chinese language: `https://bazaarplusplus.com/tutorial?lang=en`

The new row must appear immediately before the existing BazaarDB row.

## Code Evidence

- Settings dock entries are feature-owned `ISettingsDockEntry` objects with an
  `int Order`, where lower values appear earlier in the dock
  (`src/BazaarPlusPlus/Game/Settings/ISettingsDockEntry.cs:11-18`).
- The registry preserves each entry's `Order` beside its built definition
  (`src/BazaarPlusPlus/Game/Settings/SettingsDockEntryRegistry.cs:27-34`), and
  `BppSettingsDockCatalog.Install` sorts definitions with
  `a.Order.CompareTo(b.Order)` before exposing them to the UI
  (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs:13-28`).
- The dock controller creates one row per catalog definition, wires row clicks
  to `ActivateDefinition`, and `ActivateDefinition` calls
  `definition.Activate()` before optionally collapsing the panel
  (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs:210-253`,
  `src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs:315-322`).
- `BppSettingsDockDefinition` already supports non-toggle action rows through
  the constructor that accepts `resolveStatus`, `isActive`, `activate`, and
  `collapseAfterActivate` (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockDefinition.cs:22-38`).
- The existing BazaarDB row is currently `Order => 7`
  (`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadSettingsDockEntry.cs:7-20`).
  The Chinese locale row is `Order => 6`
  (`src/BazaarPlusPlus/Game/Settings/ChineseLocaleModeSettingsDockEntry.cs:19-31`).
  Because `Order` is an `int`, inserting a row between them without duplicate
  order values requires changing BazaarDB to `Order => 8` and giving the new
  shortcut tutorial row `Order => 7`.
- Current order values are raw literals spread across feature-owned entries:
  history `0`, name override `1`, legendary position `2`, enchant preview `3`,
  package art `4`, combat status bar `5`, Chinese locale `6`, and BazaarDB `7`
  (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelSettingsDockEntry.cs:7-19`,
  `src/BazaarPlusPlus/Game/NameOverride/NameOverrideSettingsDockEntry.cs:7-20`,
  `src/BazaarPlusPlus/Game/LegendaryPosition/LegendaryPositionSettingsDockEntry.cs:8-20`,
  `src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewSettingsDockEntry.cs:9-18`,
  `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsDockEntry.cs:7-20`,
  `src/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBarSettingsDockEntry.cs:8-20`,
  `src/BazaarPlusPlus/Game/Settings/ChineseLocaleModeSettingsDockEntry.cs:19-31`,
  `src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadSettingsDockEntry.cs:7-20`).
- The existing settings dock test also asserts a raw order literal for package
  card art (`tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:90-119`),
  so the test should move to the same named constants.
- The composition root is the registration point for dock entries
  (`src/BazaarPlusPlus/BppComposition.cs:97-104`).
- Sponsor link behavior is the right reference for URL language selection and
  browser opening: `BPPSupporterLinks.ResolveSponsorUrl` chooses between Chinese
  and English URLs using `LanguageCodeMatcher.IsChinese`
  (`src/BazaarPlusPlus/Game/Supporters/BPPSupporterLinks.cs:8-14`), and the
  supporter button opens that URL with `Application.OpenURL`
  (`src/BazaarPlusPlus/Game/Supporters/Ui/BPPSupporterAttributionRow.cs:188-190`).
- `LanguageCodeMatcher.IsChinese` already covers `zh`, `zh-CN`, `zh-Hans`,
  `zh-SG`, `zh-TW`, `zh-Hant`, `zh-HK`, and `zh-MO`
  (`src/BazaarPlusPlus.Localization/LanguageCodeMatcher.cs:6-19`).
- `LocalizedTextSet(string english, string chineseMainland)` is enough for this
  row because non-Chinese languages fall back to English
  (`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs:6-26`).

## Design

Implement the tutorial row as a small Settings-owned action row, not as a new
config toggle. It has no persisted state, no feature runtime, and no dependency
on the Supporters module.

Before adding the row, centralize settings dock order numbers in one Settings
namespace class. The current code relies on small raw integers scattered across
feature entry files; the new tutorial insertion is the right moment to replace
those literals with named constants and avoid future duplicate-order mistakes.

Use Sponsor as prior art only for:

- resolving Chinese vs non-Chinese URL through `LanguageCodeMatcher.IsChinese`;
- opening the final URL through `Application.OpenURL`.

Do not reuse `BPPSupporterLinks` directly because its URLs and domain semantics
belong to supporter attribution, not product help/tutorial navigation.

## File Changes

### 1. Add `BppSettingsDockOrder`

Create `src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs`.

Responsibilities:

- define the canonical visible row order in one file;
- keep existing row order unchanged except for inserting `HotkeyTutorial` before
  BazaarDB;
- give every entry a named constant instead of a raw integer.

Sketch:

```csharp
#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockOrder
{
    internal const int GameHistory = 0;
    internal const int NameOverride = 1;
    internal const int LegendaryPosition = 2;
    internal const int EnchantPreview = 3;
    internal const int PackageCardArtReplacement = 4;
    internal const int CombatStatusBar = 5;
    internal const int ChineseLocaleMode = 6;
    internal const int HotkeyTutorial = 7;
    internal const int BazaarDbUpload = 8;
}
```

### 2. Replace existing raw order literals

Modify these entries to return the new constants:

- `HistoryPanelSettingsDockEntry`: `BppSettingsDockOrder.GameHistory`
- `NameOverrideSettingsDockEntry`: `BppSettingsDockOrder.NameOverride`
- `LegendaryPositionSettingsDockEntry`: `BppSettingsDockOrder.LegendaryPosition`
- `ItemEnchantPreviewSettingsDockEntry`: `BppSettingsDockOrder.EnchantPreview`
- `PackageCardArtReplacementSettingsDockEntry`:
  `BppSettingsDockOrder.PackageCardArtReplacement`
- `CombatStatusBarSettingsDockEntry`: `BppSettingsDockOrder.CombatStatusBar`
- `ChineseLocaleModeSettingsDockEntry`: `BppSettingsDockOrder.ChineseLocaleMode`
- `BazaarDbSnapshotUploadSettingsDockEntry`: `BppSettingsDockOrder.BazaarDbUpload`

This keeps the order table explicit and moves BazaarDB from `7` to `8` as part
of the named insertion point.

### 3. Add `HotkeyTutorialLinks`

Create `src/BazaarPlusPlus/Game/Settings/HotkeyTutorialLinks.cs`.

Responsibilities:

- expose `ResolveTutorialUrl(string languageCode)`;
- expose `OpenTutorial()`;
- choose exactly the user-requested URLs;
- use `L.CurrentLanguageCode` for runtime opening, so the game language lookup
  stays centralized in the installed localization provider.

Sketch:

```csharp
#nullable enable
using BazaarPlusPlus.Localization;
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings;

internal static class HotkeyTutorialLinks
{
    private const string ChineseTutorialUrl = "https://bazaarplusplus.com/tutorial";
    private const string EnglishTutorialUrl = "https://bazaarplusplus.com/tutorial?lang=en";

    internal static string ResolveTutorialUrl(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode)
            ? ChineseTutorialUrl
            : EnglishTutorialUrl;
    }

    internal static void OpenTutorial()
    {
        Application.OpenURL(ResolveTutorialUrl(L.CurrentLanguageCode));
    }
}
```

### 4. Add the row label/status helper

Create `src/BazaarPlusPlus/Game/Settings/HotkeyTutorialSettingsMenuLabel.cs`.

Responsibilities:

- row label: English `Hotkey Tutorial`, Chinese `快捷键教程`;
- status text: English `OPEN`, Chinese `打开`.

Sketch:

```csharp
#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal static class HotkeyTutorialSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new("Hotkey Tutorial", "快捷键教程");
    private static readonly LocalizedTextSet OpenStatuses = new("OPEN", "打开");

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveOpenStatus(string languageCode)
    {
        return OpenStatuses.Resolve(languageCode, L.CurrentMode);
    }
}
```

### 5. Add the SettingsDock entry

Create `src/BazaarPlusPlus/Game/Settings/HotkeyTutorialSettingsDockEntry.cs`.

Responsibilities:

- key: `HotkeyTutorial`;
- order: `BppSettingsDockOrder.HotkeyTutorial`;
- always actionable;
- action: `HotkeyTutorialLinks.OpenTutorial`;
- collapse the panel after click.

Sketch:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class HotkeyTutorialSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.HotkeyTutorial;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "HotkeyTutorial",
            HotkeyTutorialSettingsMenuLabel.Resolve,
            HotkeyTutorialSettingsMenuLabel.ResolveOpenStatus,
            isActive: () => true,
            HotkeyTutorialLinks.OpenTutorial,
            collapseAfterActivate: true
        );
}
```

### 6. Register the new entry

Modify `src/BazaarPlusPlus/BppComposition.cs`.

Add the registration alongside the existing settings dock entries:

```csharp
_settingsDockRegistry.Register(new HotkeyTutorialSettingsDockEntry());
```

The registration source order is not the primary ordering mechanism; the
catalog sorts by `Order`. Still, place this registration near BazaarDB for
readability.

### 7. Keep BazaarDB after the tutorial row

`BazaarDbSnapshotUploadSettingsDockEntry` should return
`BppSettingsDockOrder.BazaarDbUpload`, whose value is `8`. This preserves
current rows `0-6`, inserts tutorial at `7`, and keeps BazaarDB immediately
after it.

## Tests

Modify `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs`.

Add URL resolver coverage:

- `zh-CN` -> `https://bazaarplusplus.com/tutorial`
- `zh-Hant` -> `https://bazaarplusplus.com/tutorial`
- `en` -> `https://bazaarplusplus.com/tutorial?lang=en`
- `de-DE` -> `https://bazaarplusplus.com/tutorial?lang=en`
- empty string -> `https://bazaarplusplus.com/tutorial?lang=en`
- assert the English URL does not duplicate the query string.

Add dock definition coverage:

- `entry.Order == BppSettingsDockOrder.HotkeyTutorial`;
- definition key is `HotkeyTutorial`;
- `ResolveLabel("zh-CN") == "快捷键教程"`;
- `ResolveLabel("en") == "Hotkey Tutorial"`;
- `ResolveStatus("zh-CN") == "打开"`;
- `ResolveStatus("en") == "OPEN"`;
- `definition.IsActive()` is true;
- `definition.CollapseAfterActivate` is true.

Add order regression coverage:

- each existing settings dock entry returns the matching `BppSettingsDockOrder`
  constant;
- `new BazaarDbSnapshotUploadSettingsDockEntry().Order == BppSettingsDockOrder.BazaarDbUpload`;
- optional catalog-order assertion: register `ChineseLocaleModeSettingsDockEntry`,
  `HotkeyTutorialSettingsDockEntry`, and `BazaarDbSnapshotUploadSettingsDockEntry`,
  call `BppSettingsDockCatalog.Install`, and assert the resulting keys appear as
  `ChineseLocaleMode`, `HotkeyTutorial`, `BazaarDbUpload`.

Do not unit-test `Application.OpenURL` directly. The existing Sponsor pattern
does not wrap it, and the deterministic part is URL resolution.

Run:

```bash
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

`Supporters.Tests` is not required by the new row, but it cheaply proves the
unchanged Sponsor URL helper still behaves as expected after using it as prior
art.

## Runtime Verification

1. Build and install the debug mod:

   ```bash
   ./run.sh build
   ```

2. Launch through Steam, not by opening the app directly:

   ```bash
   open "steam://run/1617400"
   ```

3. Open the BazaarPlusPlus settings dock.
4. Confirm row order around the bottom is:

   ```text
   中文模式
   快捷键教程
   BazaarDB 数据共建
   ```

5. With a Chinese game language, click `快捷键教程` and confirm the browser opens
   `https://bazaarplusplus.com/tutorial`.
6. With a non-Chinese game language, click `Hotkey Tutorial` and confirm the
   browser opens `https://bazaarplusplus.com/tutorial?lang=en`.

## Acceptance Criteria

- The visible Chinese row name is exactly `快捷键教程`.
- The non-Chinese row name is `Hotkey Tutorial`.
- The row appears immediately before BazaarDB.
- All settings dock entries use `BppSettingsDockOrder` constants instead of raw
  order literals.
- Chinese language codes open `https://bazaarplusplus.com/tutorial`.
- Non-Chinese and unknown language codes open
  `https://bazaarplusplus.com/tutorial?lang=en`.
- The row is an action row, not a config toggle, and it collapses the dock after
  activation.
- Existing Sponsor link behavior is unchanged.
