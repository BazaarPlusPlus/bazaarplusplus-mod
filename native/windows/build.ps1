$ErrorActionPreference = 'Stop'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer vswhere.exe was not found.'
}

$install = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($install)) {
    throw 'Visual Studio C++ build tools were not found.'
}

$vcvars = Join-Path $install 'VC\Auxiliary\Build\vcvars64.bat'
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $sourceDir 'build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$command = 'call "{0}" && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /MT /LD /DNOMINMAX /DUNICODE /D_UNICODE "{1}\BppReplayMediaFoundation.cpp" /link /OUT:"{2}\GfxPluginBppReplayMediaFoundation.dll" /IMPLIB:"{2}\BppReplayMediaFoundation.lib" d3d11.lib dxgi.lib evr.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib shlwapi.lib' -f $vcvars, $sourceDir, $buildDir
cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Native Windows replay plugin build failed with exit code $LASTEXITCODE."
}
