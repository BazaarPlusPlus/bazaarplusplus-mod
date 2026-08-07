$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'build.ps1')

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$install = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcvars = Join-Path $install 'VC\Auxiliary\Build\vcvars64.bat'
$buildDir = Join-Path $PSScriptRoot 'build'
$command = 'call "{0}" && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /MT /DNOMINMAX /DUNICODE /D_UNICODE "{1}\BppReplayMediaFoundationSmoke.cpp" /link /OUT:"{2}\BppReplayMediaFoundationSmoke.exe" /LIBPATH:"{2}" BppReplayMediaFoundation.lib d3d11.lib' -f $vcvars, $PSScriptRoot, $buildDir
cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Native smoke test build failed with exit code $LASTEXITCODE."
}

& (Join-Path $buildDir 'BppReplayMediaFoundationSmoke.exe')
if ($LASTEXITCODE -ne 0) {
    throw "Native smoke test failed with exit code $LASTEXITCODE."
}
