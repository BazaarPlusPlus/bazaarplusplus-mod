# 字幕模式 SettingDock 合并方案

日期：2026-07-08　状态：已实现

## 背景

当前语音字幕在配置层已经拆成两个持久化项：`VoiceSubtitles.Enabled` 是总开关，默认 `false`；`VoiceSubtitles.Language` 是语言模式，默认 `Both`。代码位置分别是 `src/BazaarPlusPlus/Core/Config/BppConfig.cs:84-100`。

当前 SettingDock 也对应拆成两行：`VoiceSubtitlesSettingsDockEntry.Build()` 用 `SettingsMenuToggleBridge` 暴露一个 ON/OFF 开关（`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesSettingsDockEntry.cs:14-22`），`VoiceSubtitlesLanguageSettingsDockEntry` 单独暴露 `Both / ChineseOnly / EnglishOnly` 循环（`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesSettingsDockEntry.cs:106-163`）。`RegisterAll()` 现在会把这两行和位置/字号行一起注册进去（`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesSettingsDockEntry.cs:24-33`）。

运行时没有必要重做：是否观察/显示字幕只看 `EnableVoiceSubtitlesConfig`（`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesGate.cs:8-10`），显示英文/中文再看 `SubtitleLanguageMode`（`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceLineDisplay.cs:239-249`）。因此本方案只合并 SettingDock 的用户入口，继续写现有两个配置项。

## 目标

把 SettingDock 里的字幕入口合并成一行，用户可见名称直接叫：

- English: `Subtitle Mode`
- 简中: `字幕模式`
- 繁中: `字幕模式`

这行是四态循环：

| UI 状态 | `Enabled` | `Language` | 下一次点击 |
| --- | --- | --- | --- |
| `OFF` / `关闭` | `false` | `Both` | 双语 |
| `BOTH` / `双语` | `true` | `Both` | 中文 |
| `ZH` / `中文` | `true` | `ChineseOnly` | 英文 |
| `EN` / `英文` | `true` | `EnglishOnly` | 关闭 |

`IsActive()` 语义：只有 `关闭/OFF` 是 false，其余三态都是 true。这样 dock 行的高亮仍表达"这个功能正在启用"。

## 非目标

- 不新增新的 config key，不做迁移。
- 不改字幕渲染、字幕匹配、VO 观察逻辑。
- 不改字幕位置、英文字号、中文字号三行的行为。
- 不把字号行藏到子菜单；SettingDock 目前没有分组/子菜单模型，本次只减少一行。

## 实现步骤

### 1. 主字幕 dock entry 改为四态模式

改造 `src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesSettingsDockEntry.cs` 中的 `VoiceSubtitlesSettingsDockEntry`：

- 不再使用 `SettingsMenuToggleBridge`，改用 `BppSettingsDockDefinition` 的完整构造器（同类多态循环模式可参考 `src/BazaarPlusPlus/Game/Settings/PreviewVisibilityModeDockEntry.cs:24-36`）。
- 保留内部 key `"VoiceSubtitles"`，避免不必要改变 Unity row object 名称和既有测试关注点；只改用户可见 label 为 `Subtitle Mode / 字幕模式`。
- 新增私有四态解析：`Off / Both / Chinese / English`。这个 enum 只服务 SettingDock UI，不放进 `Core/Config`，因为底层持久化仍是现有 `bool + SubtitleLanguageMode`。
- `ReadMode(config)`：`Enabled == false` 时一律返回 `Off`；`Enabled == true` 时按 `VoiceSubtitlesLanguageModeConfig` 返回 `Both / Chinese / English`。
- `WriteMode(config, mode)`：按目标表同时写 `EnableVoiceSubtitlesConfig` 和 `VoiceSubtitlesLanguageModeConfig`。写 `Off` 时也把 `Language` 归一到 `Both`，让配置文件保持确定状态。
- `CycleMode()` 顺序固定为 `Off -> Both -> Chinese -> English -> Off`。

状态文案保持短，因为 SettingDock 的状态列宽固定为 `80f`，且状态文本不换行（`src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs:27`、`src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs:282-289`）。

### 2. 删除单独的字幕语言 dock 行

在 `RegisterAll()` 中移除 `VoiceSubtitlesLanguageSettingsDockEntry` 注册。当前注册点在 `src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceSubtitlesSettingsDockEntry.cs:29-33`。

删除 `VoiceSubtitlesLanguageSettingsDockEntry` 类本体，避免留下一个未注册的旧路径。删除后 SettingDock 中字幕相关行变为：

1. `字幕模式`
2. `字幕位置`
3. `英文字号`
4. `中文字号`

### 3. 调整排序常量

`src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs:16-23` 当前包含 `VoiceSubtitlesLanguage = 11`，并让两个字号行排在 12/13。删除语言行后：

- 移除 `VoiceSubtitlesLanguage`
- `VoiceSubtitlesEnglishFontScale` 改到语言行原来的位置
- `VoiceSubtitlesChineseFontScale` 和后续常量整体前移一位

所有生产代码走命名常量，主要影响测试断言。

### 4. 更新配置描述但不迁移配置

`BppConfig` 里的两个现有配置项继续保留在原 section/key，避免破坏老用户 cfg：

- `VoiceSubtitles.Enabled` 仍是运行时 gate（`src/BazaarPlusPlus/Core/Config/BppConfig.cs:84-89`）
- `VoiceSubtitles.Language` 仍是运行时语言选择（`src/BazaarPlusPlus/Core/Config/BppConfig.cs:96-100`）

但把描述文案从"bilingual subtitles"改成"Subtitle Mode writes this together with Language"，避免手动编辑 cfg 的用户看到旧语义。

### 5. 更新测试

重点改 `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs`：

- `VoiceSubtitlesDockEntry_defaults_off_and_toggles_config` 当前只断言 OFF -> ON（`tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:461-490`），改成断言完整四态循环和两项 config 写入。
- `VoiceSubtitlesDockEntries_register_master_and_setting_rows` 当前期望 5 个 key，包含 `"VoiceSubtitlesLanguage"`（`tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:500-539`），改成 4 个 key。
- order consistency 测试里删除 `VoiceSubtitlesLanguageSettingsDockEntry` 的断言；catalog 排序测试里也删除 `"VoiceSubtitlesLanguage"`。

建议新增/覆盖的断言：

- 默认：label 是 `Subtitle Mode` / `字幕模式`，status 是 `OFF` / `关闭`，`IsActive()` false。
- 第一次点击：`Enabled=true`、`Language=Both`、status `BOTH` / `双语`。
- 第二次点击：`Language=ChineseOnly`、status `ZH` / `中文`。
- 第三次点击：`Language=EnglishOnly`、status `EN` / `英文`。
- 第四次点击：`Enabled=false`、`Language=Both`、status 回到 `OFF` / `关闭`。

## 验证

1. `dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj`
2. `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj`
3. `./run.sh build`
4. `./run.sh format`，只带入本次改动相关文件
5. 游戏内手动验证：SettingDock 显示"字幕模式"，点击顺序为关闭 -> 双语 -> 中文 -> 英文 -> 关闭；每个状态下实际字幕显示与状态一致。

## 风险

- 老用户如果配置里 `Enabled=false` 但 `Language=EnglishOnly`，首次打开 dock 会显示 `OFF`。点击后进入 `BOTH`，这是有意的确定化行为。
- 状态列宽很窄，英文状态不用 `Chinese` / `English` 全词，使用 `ZH` / `EN`。
- 底层仍保留两个 config key，所以代码里不能把 `SubtitleLanguageMode` 当成"是否关闭"的来源；关闭只由 `Enabled=false` 表示。
