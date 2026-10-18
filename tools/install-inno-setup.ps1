<#
.SYNOPSIS
  Downloads a pinned Inno Setup release, checks its SHA-256 and installs it in portable mode
  (no registry entries, no uninstaller). Used by the GitHub workflows.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Url,
    [Parameter(Mandatory)] [string]$Sha256,
    [Parameter(Mandatory)] [string]$Destination
)

$ErrorActionPreference = 'Stop'
$download = Join-Path ([IO.Path]::GetTempPath()) ("innosetup-" + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    Invoke-WebRequest -Uri $Url -OutFile $download -UseBasicParsing
    $hash = (Get-FileHash $download -Algorithm SHA256).Hash
    if ($hash -ne $Sha256.ToUpperInvariant()) { throw "Inno Setup download has SHA-256 $hash, expected $Sha256." }

    $process = Start-Process -FilePath $download -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/PORTABLE=1', "/DIR=`"$Destination`"")
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed ($($process.ExitCode))." }
    if (-not (Test-Path (Join-Path $Destination 'ISCC.exe'))) { throw "ISCC.exe not found in $Destination." }
    Write-Host "Inno Setup installed to $Destination"
}
finally {
    Remove-Item $download -ErrorAction SilentlyContinue
}
