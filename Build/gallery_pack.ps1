#Requires -Version 5.1
<#
.SYNOPSIS
  Publish Windows demos (WinForms / WPF / Avalonia) + ServerExample, then pack into one .7z archive.

.DESCRIPTION
  Uses dotnet publish with a Windows RID (default win-x64) and local 7-Zip (7z.exe).
  Staging folder lives under Build/; the archive is written next to this script unless -OutputArchive is set.

.PARAMETER Runtime
  Windows RID, e.g. win-x64, win-arm64.

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER SelfContained
  If set, publish self-contained deployments (larger archives, no shared runtime required).

.PARAMETER WpfFramework
  Target framework for WPF.Example (default net8.0-windows). Must match a TFM in the csproj.

.PARAMETER AvaloniaFramework
  Target framework for Avalonia.Example (default net8.0).

.PARAMETER SevenZip
  Full path to 7z.exe. If omitted, searches PATH then Program Files.

.PARAMETER OutputArchive
  Full path to the output .7z file. Default: Build/RemoteViewing-demos-<Runtime>-<timestamp>.7z

.PARAMETER KeepStaging
  Do not delete the staging folder after creating the archive (for inspection).

.EXAMPLE
  .\demo-pack.ps1

.EXAMPLE
  .\demo-pack.ps1 -Runtime win-arm64 -SelfContained
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SelfContained,

    [string] $WpfFramework = 'net8.0-windows',

    [string] $AvaloniaFramework = 'net8.0',

    [string] $SevenZip = '',

    [string] $OutputArchive = '',

    [switch] $KeepStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-7ZipPath {
    param([string] $Explicit)
    if ($Explicit -and (Test-Path -LiteralPath $Explicit)) {
        return (Resolve-Path -LiteralPath $Explicit).Path
    }
    $fromPath = Get-Command 7z -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }
    foreach ($candidate in @(
            (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
            (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe')
        )) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Invoke-DemoPublish {
    param(
        [Parameter(Mandatory)] [string] $StagingBase,
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $StagingSubdir,
        [string[]] $ExtraDotnetArgs = @()
    )

    $outDir = Join-Path $StagingBase $StagingSubdir
    if (Test-Path -LiteralPath $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null

    Write-Host "Publishing -> $StagingSubdir" -ForegroundColor Cyan
    $args = @(
        'publish', $ProjectPath,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', ($(if ($SelfContained) { 'true' } else { 'false' })),
        '-o', $outDir,
        '-v', 'minimal'
    )
    if ($ExtraDotnetArgs.Count -gt 0) {
        $args += $ExtraDotnetArgs
    }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE ($StagingSubdir)"
    }
}

Set-Location $PSScriptRoot

Write-Host @"
 ██████╗  █████╗ ██╗     ██╗     ███████╗██████╗ ██╗   ██╗
██╔════╝ ██╔══██╗██║     ██║     ██╔════╝██╔══██╗╚██╗ ██╔╝
██║  ███╗███████║██║     ██║     █████╗  ██████╔╝ ╚████╔╝ 
██║   ██║██╔══██║██║     ██║     ██╔══╝  ██╔══██╗  ╚██╔╝  
╚██████╔╝██║  ██║███████╗███████╗███████╗██║  ██║   ██║   
 ╚═════╝ ╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝╚═╝  ╚═╝   ╚═╝   
"@

# --- paths ---
$BuildDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BuildDir '..')).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$StagingRoot = Join-Path $BuildDir "demo-pack-staging-$stamp"

$seven = Resolve-7ZipPath -Explicit $SevenZip
if (-not $seven) {
    throw "7-Zip (7z.exe) not found. Install 7-Zip or pass -SevenZip 'C:\Path\To\7z.exe'"
}

if ([string]::IsNullOrWhiteSpace($OutputArchive)) {
    $OutputArchive = Join-Path $BuildDir "RemoteViewing-demos.7z"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputArchive)) {
    $OutputArchive = [System.IO.Path]::GetFullPath((Join-Path $BuildDir $OutputArchive))
}
else {
    $OutputArchive = [System.IO.Path]::GetFullPath($OutputArchive)
}

New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null

try {
    $readme = @"
RemoteViewing — Windows demo bundle
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
RID: $Runtime
Configuration: $Configuration
Self-contained: $SelfContained

Folders
-------
- WinForms.Example      : VNC client (Windows Forms), net48 + $Runtime
- WPF.Example           : VNC client (WPF), $WpfFramework + $Runtime
- Avalonia.Example      : VNC client (Avalonia), $AvaloniaFramework + $Runtime
- ServerExample         : Sample VNC server (net48 + $Runtime), listens on 0.0.0.0:5900, password: test

Quick try
---------
1) Run ServerExample\\ServerExample.exe (allow firewall if prompted).
2) Run any client demo; host 127.0.0.1, port 5900, password test.

Requires .NET runtime on the machine unless you repacked with -SelfContained.
"@
    Set-Content -Path (Join-Path $StagingRoot 'README.txt') -Value $readme -Encoding UTF8

    # WinForms example (single TFM net48 in repo)
    Invoke-DemoPublish `
        -StagingBase $StagingRoot `
        -ProjectPath (Join-Path $RepoRoot 'RemoteViewing.Windows.Forms.Example\RemoteViewing.Windows.Forms.Example.csproj') `
        -StagingSubdir 'WinForms.Example'

    # WPF example (multi-TFM — must pass -f)
    Invoke-DemoPublish `
        -StagingBase $StagingRoot `
        -ProjectPath (Join-Path $RepoRoot 'RemoteViewing.WPF.Example\RemoteViewing.WPF.Example.csproj') `
        -StagingSubdir 'WPF.Example' `
        -ExtraDotnetArgs @('-f', $WpfFramework)

    # Avalonia example
    Invoke-DemoPublish `
        -StagingBase $StagingRoot `
        -ProjectPath (Join-Path $RepoRoot 'RemoteViewing.Avalonia.Example\RemoteViewing.Avalonia.Example.csproj') `
        -StagingSubdir 'Avalonia.Example' `
        -ExtraDotnetArgs @('-f', $AvaloniaFramework)

    # Server sample
    Invoke-DemoPublish `
        -StagingBase $StagingRoot `
        -ProjectPath (Join-Path $RepoRoot 'RemoteViewing.ServerExample\RemoteViewing.ServerExample.csproj') `
        -StagingSubdir 'ServerExample'

    if (Test-Path -LiteralPath $OutputArchive) {
        Remove-Item -LiteralPath $OutputArchive -Force
    }

    Write-Host "Creating archive: $OutputArchive" -ForegroundColor Cyan
    $archiveParent = Split-Path -Parent $OutputArchive
    if ($archiveParent -and -not (Test-Path -LiteralPath $archiveParent)) {
        New-Item -ItemType Directory -Path $archiveParent -Force | Out-Null
    }

    Push-Location $StagingRoot
    try {
        & $seven a -t7z -mx9 -bd $OutputArchive *
        if ($LASTEXITCODE -ne 0) {
            throw "7z failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    $sizeMb = [math]::Round((Get-Item -LiteralPath $OutputArchive).Length / 1MB, 2)
    Write-Host "Done: $OutputArchive ($sizeMb MB)" -ForegroundColor Green
}
finally {
    if (-not $KeepStaging) {
        if (Test-Path -LiteralPath $StagingRoot) {
            Remove-Item -LiteralPath $StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host "Staging kept at: $StagingRoot" -ForegroundColor Yellow
    }
}
