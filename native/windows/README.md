# Windows native replay recorder

`GfxPluginBppReplayMediaFoundation.dll` is a Unity D3D11 rendering plugin. It converts the
persistent replay capture texture to NV12 with the D3D11 video processor and writes H.264 through
Media Foundation. The active encoder is checked against Media Foundation's hardware MFT catalog;
software encoding is rejected instead of being used as a hidden fallback.

Build on Windows with Visual Studio 2022 and the Windows SDK:

```powershell
./build.ps1
```

The script builds into `build/` by default, verifies the x64 PE metadata, exact catalog ABI exports,
reviewed system dependencies, and unsigned producer policy, then runs the native smoke program.
Pass `-OutputDirectory` for a side-effect-free staging build. `./run.sh publish` compares the
canonical Windows input digest with the installer manifest and invokes this build plus local
promotion only when the current Windows input is stale or damaged.

The DLL must be installed in `TheBazaar_Data/Plugins/x86_64` so Unity loads it before BepInEx
constructs the managed recording backend.
