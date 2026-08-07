# Windows native replay recorder

`GfxPluginBppReplayMediaFoundation.dll` is a Unity D3D11 rendering plugin. It converts the
persistent replay capture texture to NV12 with the D3D11 video processor and writes H.264 through
Media Foundation. The active encoder is checked against Media Foundation's hardware MFT catalog;
software encoding is rejected instead of being used as a hidden fallback.

Build on Windows with Visual Studio 2022 and the Windows SDK:

```powershell
./build.ps1
```

The DLL must be installed in `TheBazaar_Data/Plugins/x86_64` so Unity loads it before BepInEx
constructs the managed recording backend.
