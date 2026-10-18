<#
.SYNOPSIS
  Publishes SonosDesk as a self-contained single file and builds the Windows installer.

.DESCRIPTION
  Requires the .NET 10 SDK and Inno Setup 7 (https://jrsoftware.org/isinfo.php). The compiler
  (ISCC.exe) is taken from -Iscc, the ISCC environment variable, PATH or the default install folders.

.EXAMPLE
  ./tools/build-installer.ps1
  ./tools/build-installer.ps1 -Version 0.2.0 -Iscc 'C:\Tools\Inno Setup 7\ISCC.exe'
#>
[CmdletBinding()]
param(
    # Version of the build; defaults to <Version> in Directory.Build.props.
    [string]$Version,
    [string]$Iscc,
    # Use an existing publish folder instead of publishing again.
    [switch]$SkipPublish,
    [string]$PublishDir = 'artifacts/SonosDesk-win-x64',
    [string]$OutputDir = 'artifacts/installer'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $Version) {
        [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
        $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must look like 1.2.3." }

    $publishPath = [IO.Path]::GetFullPath((Join-Path $root $PublishDir))
    $outputPath = [IO.Path]::GetFullPath((Join-Path $root $OutputDir))

    if (-not $SkipPublish) {
        Write-Host "Publishing SonosDesk $Version ..."
        dotnet publish src/SonosDesk.App/SonosDesk.App.csproj --configuration Release --runtime win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
            -p:DebugType=none -p:Version=$Version --output $publishPath
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
    }
    if (-not (Test-Path (Join-Path $publishPath 'SonosDesk.exe'))) { throw "SonosDesk.exe not found in $publishPath." }

    $candidates = @(
        $Iscc,
        $env:ISCC,
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    $compiler = @($candidates)[0]
    if (-not $compiler) { throw 'ISCC.exe (Inno Setup 7) not found. Install it or pass -Iscc.' }

    Write-Host "Building installer with $compiler ..."
    & $compiler "/DAppVersion=$Version" "/DSourceDir=$publishPath" "/DOutputDir=$outputPath" (Join-Path $root 'installer\SonosDesk.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

    $setup = Join-Path $outputPath "SonosDesk-Setup-$Version-win-x64.exe"
    Write-Host "Installer: $setup"
}
finally {
    Pop-Location
}
