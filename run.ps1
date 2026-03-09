$ErrorActionPreference = "Stop"

$Managed = "C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed"

function Build-Project {
    dotnet build
}

function Format-Project {
    csharpier format .
}

function Ensure-ILSpy {
    if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) {
        Write-Host "ilspycmd not found. Installing..."
        dotnet tool install -g ilspycmd
    }
}

function Decompile-Dll {
    param(
        [string]$DllName = "Assembly-CSharp"
    )

    Ensure-ILSpy

    $outDir = Join-Path "." "decompiled/$DllName"
    $dllPath = Join-Path $Managed "$DllName.dll"

    Write-Host "Decompiling $DllName to $outDir..."
    & {
        $env:DOTNET_ROLL_FORWARD = "Major"
        ilspycmd -p -o $outDir $dllPath
    }
    Write-Host "Done: $outDir"
}

function Decompile-All {
    foreach ($dll in @(
            "Assembly-CSharp",
            "BazaarGameClient",
            "BazaarGameShared",
            "BazaarBattleService",
            "TheBazaarRuntime"
        )) {
        Decompile-Dll -DllName $dll
    }
}

if ($args.Count -lt 1) {
    Write-Host "Usage: .\run.ps1 {build|format|decompile [DllName]|decompile-all}"
    exit 1
}

switch ($args[0]) {
    "build" {
        Build-Project
    }
    "format" {
        Format-Project
    }
    "decompile" {
        if ($args.Count -ge 2) {
            Decompile-Dll -DllName $args[1]
        }
        else {
            Decompile-Dll
        }
    }
    "decompile-all" {
        Decompile-All
    }
    default {
        Write-Host "Usage: .\run.ps1 {build|format|decompile [DllName]|decompile-all}"
        exit 1
    }
}
