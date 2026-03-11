param(
    [switch]$SkipInstall,
    [switch]$FrontendOnly,
    [switch]$Setup
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WindowsConfig = Join-Path $ScriptDir "src-tauri\tauri.windows.conf.json"
$WindowsZip = Join-Path $ScriptDir "src-tauri\resources\BepInExSource\windows\BepInEx.zip"
$BundleDir = Join-Path $ScriptDir "src-tauri\target\release\bundle"
$ReleaseExe = Join-Path $ScriptDir "src-tauri\target\release\bppinstaller.exe"
$NpmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue

function Assert-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [string]$Hint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        if ($Hint) {
            throw "$Name not found. $Hint"
        }

        throw "$Name not found."
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "==> $Label"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

Push-Location $ScriptDir

try {
    Assert-Command -Name "node" -Hint "Install Node.js first."
    if (-not $NpmCommand) {
        throw "npm.cmd not found. Install Node.js/npm first."
    }

    if (-not (Test-Path $WindowsZip)) {
        throw "Missing Windows resource zip: $WindowsZip"
    }

    if (-not $FrontendOnly) {
        Assert-Command -Name "cargo" -Hint "Install Rust toolchain first."
    }

    if (-not $SkipInstall) {
        Invoke-Step -Label "Installing npm dependencies" -Action {
            & $NpmCommand.Source install
        }
    }

    if ($FrontendOnly) {
        Invoke-Step -Label "Running frontend build" -Action {
            & $NpmCommand.Source run build
        }

        Write-Host "Frontend build complete: $(Join-Path $ScriptDir 'build')"
        exit 0
    }

    Invoke-Step -Label "Building Windows app binary" -Action {
        & $NpmCommand.Source run tauri build -- --no-bundle --config $WindowsConfig
    }

    Write-Host ""
    Write-Host "Build complete."
    Write-Host "Binary:"
    Write-Host "  $ReleaseExe"

    if ($Setup) {
        Invoke-Step -Label "Bundling NSIS setup.exe" -Action {
            & $NpmCommand.Source run tauri bundle -- --bundles nsis --config $WindowsConfig
        }

        Write-Host "Setup:"
        Write-Host "  $BundleDir\nsis"
    }
}
finally {
    Pop-Location
}
