<#
    Builds the Windows application into one self-contained executable.

        powershell -File windows\build.ps1 [-Version 0.2.0] [-Output path]

    The result needs no .NET installation on the machine it runs on. Single file
    compression is deliberately left off: it halves the download but doubles the
    memory the process holds, and this one stays running all day.
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$Output
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not yet bound while parameter defaults are evaluated.
if (-not $Output) {
    $Output = Join-Path $PSScriptRoot '.build'
}

# Tags are written v0.2.0; assembly versions are not.
$Version = $Version.TrimStart('v')

dotnet publish (Join-Path $PSScriptRoot 'src/OtpBar.App') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $Output `
    -warnaserror `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Output "Built $(Join-Path $Output 'OTPBar.exe')"
