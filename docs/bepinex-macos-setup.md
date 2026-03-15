# BepInEx macOS 26 + Apple Silicon 安装指南

适用于：macOS 26+，Apple Silicon (M1–M4)，BepInEx 5.4.x，The Bazaar。

---

## 问题根因

macOS 26 + Apple Silicon 上有三层问题叠加，导致标准安装完全失效：

| 层次 | 问题 | 现象 |
|------|------|------|
| 1 | macOS 26 拒绝运行无代码签名的进程 | 游戏被 SIGKILL，或安装后打不开 |
| 2 | BepInEx Preloader 的 RuntimeFixes 与 macOS 26 Mono 运行时不兼容 | BepInEx 加载到一半崩溃，无 LogOutput.log |
| 3 | Apple Silicon 硬件级 W^X 策略阻止 Harmony 写入可执行内存 | 所有 Harmony patch 静默失败，插件无效果 |

---

## 安装步骤

### 1. 安装 BepInEx 5.4.23.5

从 GitHub Releases 下载 `BepInEx_macos_universal_5.4.23.5.zip`，解压到游戏根目录：

```
The Bazaar/
├── BepInEx/
│   ├── core/
│   └── plugins/
├── run_bepinex.sh
├── libdoorstop.dylib
└── TheBazaar.app
```

### 2. 修改 `run_bepinex.sh`

需要对脚本做两处修改。

#### 修改 A：代码签名处理（解决问题 1）

找到脚本末尾 `gib: workaround to ensure game is not codesigned` 这段，替换为：

```sh
# macOS 26+: remove original signature, re-sign with ad-hoc + JIT entitlements
app_path="${executable_path%/Contents/MacOS*}"
if command -v codesign &>/dev/null; then
    _entitlements_file="$(mktemp /tmp/bepinex_ents.XXXXXX.plist)"
    cat > "$_entitlements_file" << 'ENTEOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
ENTEOF
    codesign --remove-signature "$app_path" 2>/dev/null || true
    codesign --force --deep --sign - --entitlements "$_entitlements_file" "$app_path"
    rm -f "$_entitlements_file"
fi
```

**原理**：
- 移除原始签名（清除 hardened runtime，否则 DYLD 注入被拒绝）
- 重新 ad-hoc 签名（macOS 26 要求进程必须有签名才能运行）
- `allow-jit` + `allow-unsigned-executable-memory`：允许 Harmony 修改内存页

#### 修改 B：强制 x86_64 运行（解决问题 3）

找到脚本末尾 `if [ -n "${is_apple_silicon}" ]` 这段，替换为：

```sh
if [ -n "${is_apple_silicon}" ]; then
    # Force x86_64 (Rosetta) so Harmony can use mprotect W+X for patching.
    # On arm64, Apple Silicon hardware enforces W^X at CPU level, blocking all Harmony patches.
    exec arch -x86_64 -e DYLD_INSERT_LIBRARIES="${DYLD_INSERT_LIBRARIES}" "$executable_path" "$@"
else
    exec "$executable_path" "$@"
fi
```

**原理**：Apple Silicon arm64 进程的 W^X 是硬件强制的，即使有 JIT entitlement 也无法用普通 mprotect 绕过。x86_64 (Rosetta) 进程由于 Rosetta 本身需要 JIT 能力，mprotect 限制更宽松，Harmony 可以正常工作。

### 3. Patch BepInEx.Preloader.dll（解决问题 2）

运行项目内的 patcher：

```bash
cd scripts/bepinex-mac-patcher
dotnet run
```

或指定自定义路径：

```bash
dotnet run -- /path/to/BepInEx/core/BepInEx.Preloader.dll
```

**原理**：将以下三个方法替换为空方法（直接 `ret`）：

- `ConsoleSetOutFix.Apply()` — 控制台输出重定向
- `HarmonyInteropFix.Apply()` — Harmony 互操作辅助
- `UnityPatches.Apply()` — Unity 调试补丁

这三个方法在 macOS 26 的 Mono 运行时上调用 `MonoMod.RuntimeDetour.DetourHelper.GetIdentifiable()` 时返回 null，导致 NullReferenceException。它们仅影响调试辅助功能，对 mod 功能无影响。

### 4. 设置 Steam 启动选项

右键游戏 → 属性 → 通用 → 启动选项：

```
/usr/bin/arch "-x86_64" /bin/bash "/Users/<你的用户名>/Library/Application Support/Steam/steamapps/common/The Bazaar/run_bepinex.sh" %command%
```

---

## 验证安装成功

启动游戏后关闭，检查：

```bash
cat "~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log" | grep -E "Chainloader|Plugin|Configuration"
```

应该看到：

```
[Message:   BepInEx] Chainloader startup complete
[Info   :BazaarPlusPlus] [BPP][Plugin] Plugin BazaarPlusPlus loaded
[Info   :BazaarPlusPlus] [BPP][ModState] Configuration initialized: ...
```

---

## 维护说明

### 游戏更新后

Steam 验证文件会覆盖 `.app`，导致游戏无法启动（签名被还原）。**重新通过脚本启动一次即可**——脚本每次运行都会自动重签。

### BepInEx 更新后

新版本的 `BepInEx.Preloader.dll` 需要重新 patch：

```bash
cd scripts/bepinex-mac-patcher
dotnet run
```

同时重新检查 `run_bepinex.sh` 的两处修改是否被覆盖。

---

## 不适用场景

- **IL2CPP 游戏**：The Bazaar 是 Mono，此文档仅针对 Mono 游戏
- **Intel Mac**：不存在 W^X 问题，只需处理 quarantine 和签名即可，标准安装流程即可
- **BepInEx 6.x**：API 不同，patcher 的类名可能变化，需重新验证
