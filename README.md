# BazaarPlusPlus

A BepInEx mod for The Bazaar.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download)
- The Bazaar installed via Steam

## Build

```bash
dotnet build
```

The compiled DLL is automatically copied to the BepInEx plugins folder.

## Format

Install [CSharpier](https://csharpier.com) if not already installed:

```bash
dotnet tool install -g csharpier
```

Run formatter:

```bash
csharpier format .
```

## Decompiling Game DLLs

Game DLLs are located at:

- **macOS**: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed/`
- **Windows**: `C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed\`

Key DLLs:

| DLL | Description |
|-----|-------------|
| `Assembly-CSharp.dll` | Main game code |
| `BazaarGameClient.dll` | Client-side game logic |
| `BazaarGameShared.dll` | Shared types and models |
| `BazaarBattleService.dll` | Battle simulation |
| `TheBazaarRuntime.dll` | Runtime utilities |

### Install ILSpy

```bash
dotnet tool install -g ilspycmd
```

### Decompile a DLL

```bash
ilspycmd -p -o ./decompiled/BazaarGameShared BazaarGameShared.dll
```

- `-p` generates a full project structure
- `-o` specifies the output directory

Decompile all key DLLs at once (macOS):

```bash
MANAGED="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"

for dll in Assembly-CSharp BazaarGameClient BazaarGameShared BazaarBattleService TheBazaarRuntime; do
    ilspycmd -p -o "./decompiled/$dll" "$MANAGED/$dll.dll"
done
```


Inspired By：

https://github.com/Duangi/BazaarHelper

https://github.com/oceanseth/BazaarPlannerMod